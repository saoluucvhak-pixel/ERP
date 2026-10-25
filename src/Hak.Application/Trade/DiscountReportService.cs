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
                case InvoicePurpose.Increase: r[5] += net; break;
                case InvoicePurpose.Discount: if (i.DiscountKind == DiscountKind.Trade) r[2] += net; else r[3] += net; break;
            }
        }
        // Chiết khấu thương mại quyết toán bằng hạch toán điều chỉnh giảm (không có hóa đơn điều chỉnh): chứng từ đã ghi sổ trong kỳ, tính vào cột "CK thương mại sau bán".
        if (kind == InvoiceKind.Sales)
        {
            var byVoucher = await (from r in db.DiscountAgreementRuns.AsNoTracking()
                                   join ag in db.DiscountAgreements.AsNoTracking() on r.AgreementId equals ag.Id
                                   join v in db.Vouchers.AsNoTracking() on r.VoucherId equals v.Id
                                   where r.CompanyId == ctx.CompanyId && r.VoidedAt == null && v.Status == VoucherStatus.Posted && v.PostingDate >= fromDate && v.PostingDate <= toDate
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
                              select new { v.SourceId, l.Amount }).ToListAsync();
            var invIds = rows.Select(x => PaymentDiscountService.TryParseSource(x.SourceId, out var g, out _) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();
            var owners = await db.CommercialInvoices.AsNoTracking().Where(i => invIds.Contains(i.Id) && i.Kind == kind).Select(i => new { i.Id, i.PartnerId }).ToDictionaryAsync(i => i.Id, i => i.PartnerId);
            foreach (var x in rows)
                if (PaymentDiscountService.TryParseSource(x.SourceId, out var g, out _) && owners.TryGetValue(g, out var pid)) Row(pid)[6] += x.Amount;
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
