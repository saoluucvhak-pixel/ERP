using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

/// <summary>
/// Một đối tượng: doanh số gộp → các khoản giảm trừ → doanh số thuần (bán) / giá trị mua thuần (mua), kèm chiết khấu thanh toán (khoản tài chính, KHÔNG nằm trong doanh số thuần).
/// Mọi số là tiền hạch toán, chưa thuế, theo ngày hạch toán hóa đơn.
/// </summary>
public sealed record DiscountReportRow(Guid PartnerId, string PartnerCode, string PartnerName, decimal Gross, decimal OnInvoiceTradeDiscount, decimal AfterSaleTradeDiscount,
    decimal Allowance, decimal Returns, decimal Increase, decimal Net, decimal PaymentDiscount);

public sealed record DiscountReport(InvoiceKind Kind, DateOnly FromDate, DateOnly ToDate, IReadOnlyList<DiscountReportRow> Rows, DiscountReportRow Total);

/// <summary>
/// Báo cáo chiết khấu: phân biệt ba nhóm giảm trừ làm đổi giá trị hàng (chiết khấu thương mại ghi trên hóa đơn, chiết khấu thương mại sau bán, giảm giá hàng bán) với hàng trả lại,
/// và chiết khấu thanh toán (chi phí / thu nhập tài chính). Dữ liệu dẫn xuất từ hóa đơn và chứng từ đã ghi sổ — không có số dư riêng nên không bao giờ lệch sổ.
/// </summary>
public sealed class DiscountReportService(HakDbContext db)
{
    public async Task<DiscountReport> ReportAsync(SessionContext ctx, InvoiceKind kind, DateOnly fromDate, DateOnly toDate)
    {
        ctx.Require(Permissions.ReportView);
        var invs = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.CompanyId == ctx.CompanyId && i.Kind == kind && i.Status == InvoiceStatus.Posted && i.PostingDate >= fromDate && i.PostingDate <= toDate).ToListAsync();
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var recognizedByVoucher = (await (from link in db.DiscountSettlementInvoiceLinks.AsNoTracking()
            join run in db.DiscountAgreementRuns.AsNoTracking() on link.RunId equals run.Id
            join voucher in db.Vouchers.AsNoTracking() on run.VoucherId equals voucher.Id
            where run.CompanyId == ctx.CompanyId && run.VoidedAt == null && link.Amount > 0 && voucher.Status == VoucherStatus.Posted
            select link.AdjustmentInvoiceId).ToListAsync()).ToHashSet();
        var acc = new Dictionary<Guid, decimal[]>();   // 0 gross, 1 onInvoice, 2 trade after, 3 allowance, 4 returns, 5 increase, 6 payment discount
        decimal[] Row(Guid p) => acc.TryGetValue(p, out var r) ? r : acc[p] = new decimal[7];
        foreach (var i in invs)
        {
            var r = Row(i.PartnerId);
            var net = i.Lines.Sum(l => l.FunctionalAmount);
            switch (i.Purpose)
            {
                case InvoicePurpose.Normal: r[0] += net + i.Lines.Sum(l => l.FunctionalDiscountAmount); r[1] += i.Lines.Sum(l => l.FunctionalDiscountAmount); break;
                case InvoicePurpose.Return: r[4] += net; break;
                case InvoicePurpose.Increase when i.AdjustmentReason == InvoiceAdjustmentReason.CommercialRebate: r[2] -= net; break;
                case InvoicePurpose.Increase: r[5] += net; break;
                case InvoicePurpose.Discount: if (i.DiscountKind == DiscountKind.Trade) { if (!recognizedByVoucher.Contains(i.Id)) r[2] += net; } else r[3] += net; break;
            }
        }
        // Chiết khấu thương mại quyết toán bằng hạch toán điều chỉnh giảm (không có hóa đơn điều chỉnh): chứng từ đã ghi sổ trong kỳ, tính vào cột "CK thương mại sau bán".
        {
            var byVoucher = await (from r in db.DiscountAgreementRuns.AsNoTracking()
                                   join ag in db.DiscountAgreements.AsNoTracking() on r.AgreementId equals ag.Id
                                   join v in db.Vouchers.AsNoTracking() on r.VoucherId equals v.Id
                                   where r.CompanyId == ctx.CompanyId && ag.Kind == kind && r.VoidedAt == null && v.Status == VoucherStatus.Posted && v.PostingDate >= fromDate && v.PostingDate <= toDate
                                   select new { ag.PartnerId, r.Amount }).ToListAsync();
            foreach (var x in byVoucher) Row(x.PartnerId)[2] += x.Amount;
        }
        // Chiết khấu thanh toán: dòng tài khoản vai trò PAYMENT_DISCOUNT_* của chứng từ nguồn HAK.PAYDISC đã ghi sổ, theo đối tượng của hóa đơn nguồn.
        var role = kind == InvoiceKind.Purchase ? "PAYMENT_DISCOUNT_INCOME" : "PAYMENT_DISCOUNT_EXPENSE";
        var account = await db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && m.RoleCode == role).Select(m => (Guid?)m.AccountId).FirstOrDefaultAsync();
        if (account is { } acct)
        {
            var rows = await (from v in db.Vouchers.AsNoTracking()
                              join l in db.VoucherLines.AsNoTracking() on v.Id equals l.VoucherId
                              where v.CompanyId == ctx.CompanyId && v.SourceSystem == PaymentDiscountService.Source && v.Status == VoucherStatus.Posted
                                    && v.PostingDate >= fromDate && v.PostingDate <= toDate && l.AccountId == acct && l.SettleJournalLineId == null
                              select new { v.Id, v.SourceId, l.Amount }).ToListAsync();
            // Chiết khấu độc lập (nguồn PARTNER:…) tính theo đối tượng ghi trong nguồn.
            foreach (var x in rows)
                if (PaymentDiscountService.TryParsePartnerSource(x.SourceId, out var ppid, out _) && (await db.BusinessPartners.AsNoTracking().AnyAsync(p => p.Id == ppid && p.CompanyId == ctx.CompanyId))) Row(ppid)[6] += x.Amount;
            // Thu/chi gộp nhiều hóa đơn (nguồn "MULTI:{đối tượng}:…") và thu/chi ngoại tệ (nguồn "FX:…"): đối tượng lấy từ nguồn hoặc từ dòng bù trừ công nợ của chứng từ.
            foreach (var x in rows)
            {
                Guid? owner = null;
                if (x.SourceId is not null && x.SourceId.StartsWith("MULTI:", StringComparison.Ordinal) && x.SourceId.Length >= 42 && Guid.TryParse(x.SourceId[6..42], out var mp)) owner = mp;
                else if (x.SourceId is not null && x.SourceId.StartsWith("FX:", StringComparison.Ordinal))
                    owner = await db.VoucherLines.AsNoTracking().Where(l => l.VoucherId == x.Id && l.SettleJournalLineId != null && l.PartnerId != null).Select(l => l.PartnerId).FirstOrDefaultAsync();
                if (owner is { } oid && await db.BusinessPartners.AsNoTracking().AnyAsync(p => p.Id == oid && p.CompanyId == ctx.CompanyId)) Row(oid)[6] += x.Amount;
            }
            var invIds = rows.Select(x => Guid.TryParse(x.SourceId, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();
            var owners = await db.CommercialInvoices.AsNoTracking().Where(i => invIds.Contains(i.Id) && i.Kind == kind).Select(i => new { i.Id, i.PartnerId }).ToDictionaryAsync(i => i.Id.ToString(), i => i.PartnerId);
            foreach (var x in rows.Where(x => x.SourceId is not null && owners.ContainsKey(x.SourceId))) Row(owners[x.SourceId!])[6] += x.Amount;
        }
        DiscountReportRow Make(Guid id, string code, string name, decimal[] r)
        {
            var net = r[0] - r[1] - r[2] - r[3] - r[4] + r[5];   // thuần = gộp − (CK trên hóa đơn, CK thương mại sau bán, giảm giá, trả lại) + điều chỉnh tăng
            return new DiscountReportRow(id, code, name, r[0], r[1], r[2], r[3], r[4], r[5], net, r[6]);
        }
        var result = acc.Select(kv => Make(kv.Key, partners[kv.Key].Code, partners[kv.Key].Name, kv.Value)).OrderBy(x => x.PartnerCode, StringComparer.Ordinal).ToList();
        var tot = new decimal[7];
        foreach (var kv in acc) for (var k = 0; k < 7; k++) tot[k] += kv.Value[k];
        return new DiscountReport(kind, fromDate, toDate, result, Make(Guid.Empty, "", "Cộng", tot));
    }
}
