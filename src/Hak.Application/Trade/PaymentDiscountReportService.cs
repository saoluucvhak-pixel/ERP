using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record MissedDiscountRow(Guid InvoiceId, string InvoiceNumber, string InvoiceNo, DateOnly InvoiceDate, string PartnerCode, string PartnerName, string Terms, DateOnly LastDeadline,
    int DaysOver, decimal Open, decimal LostDiscount);

public sealed record SuspectDeductionRow(Guid InvoiceId, string InvoiceNumber, string InvoiceNo, string PartnerCode, string PartnerName, decimal OriginalAmount, decimal Allocated, decimal Open, decimal OpenPercent, DateOnly InvoiceDate);

public sealed record DsoDpoRow(string Month, decimal ClosingBalance, decimal Turnover, int DaysInMonth, decimal Days);

public sealed record DiscountEffectRow(string VoucherNumber, DateOnly PostingDate, string PartnerCode, string PartnerName, decimal Settled, decimal Discount, decimal Cash, int? DaysEarly, decimal? AnnualRatePercent);

public sealed record DiscountEffect(IReadOnlyList<DiscountEffectRow> Rows, decimal TotalDiscount, decimal TotalCash, decimal? WeightedAnnualRatePercent);

/// <summary>
/// Báo cáo chiết khấu thanh toán (tham khảo cách trình bày của Odoo — early payment discount, và MISA — công nợ theo hạn): chiết khấu đã mất vì quá hạn mà còn nợ, khoản nợ còn dư nhỏ sau thanh toán
/// (nghi khách tự khấu trừ), số ngày phải thu / phải trả (DSO / DPO) theo tháng, hiệu quả dòng tiền của chiết khấu đã thực hiện (chi phí tín dụng quy năm).
/// Chỉ đọc; số liệu từ sổ cái và hóa đơn đã ghi sổ.
/// </summary>
public sealed class PaymentDiscountReportService(HakDbContext db, LedgerQueryService ledger, AuditService audit, IClock clock)
{
    private async Task<Guid?> RoleAsync(Guid companyId, string role)
        => await db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == companyId && m.RoleCode == role).Select(m => (Guid?)m.AccountId).FirstOrDefaultAsync();

    /// <summary>Hóa đơn tiền hạch toán có điều khoản chiết khấu, còn nợ và đã QUÁ MỌI HẠN hưởng tại <paramref name="asOf"/>: số chiết khấu đã mất (tính ở bậc tốt nhất lúc còn hạn).</summary>
    public async Task<IReadOnlyList<MissedDiscountRow>> MissedAsync(SessionContext ctx, InvoiceKind kind, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var acct = await RoleAsync(ctx.CompanyId, kind == InvoiceKind.Purchase ? "AP_TRADE" : "AR_TRADE");
        if (acct is null) return [];
        var invs = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.CompanyId == ctx.CompanyId && i.Kind == kind && i.Purpose == InvoicePurpose.Normal && i.Status == InvoiceStatus.Posted && i.PaymentDiscountPercent != null && i.Currency == null && i.VoucherId != null).ToListAsync();
        var open = (await new SettlementService(db, audit, clock).OpenItemsAsync(ctx, acct.Value, null, asOf)).Where(o => o.Open > 0).GroupBy(o => o.VoucherId).ToDictionary(g => g.Key, g => g.First());
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var rows = new List<MissedDiscountRow>();
        foreach (var i in invs)
        {
            if (!open.TryGetValue(i.VoucherId!.Value, out var o)) continue;
            var terms = PaymentTerms.From(i.PaymentDiscountPercent, i.PaymentDiscountDays, i.PaymentDiscountOptions);
            if (terms.Applicable(i.InvoiceDate, asOf) is not null) continue;               // còn hạn: không phải khoản đã mất
            var best = terms.Tiers.OrderByDescending(t => t.Percent).First();
            var last = terms.Last(i.InvoiceDate).Deadline;
            var p = partners[i.PartnerId];
            rows.Add(new MissedDiscountRow(i.Id, i.Number, i.InvoiceNo, i.InvoiceDate, p.Code, p.Name, terms.Describe(), last, asOf.DayNumber - last.DayNumber, o.Open,
                PaymentDiscountService.ComputeDiscount(i, o.Amount, o.Open, best.Percent)));
        }
        return rows.OrderByDescending(r => r.LostDiscount).ThenBy(r => r.InvoiceNumber, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Khoản phải thu / phải trả đã được thanh toán phần lớn nhưng còn dư NHỎ (tỷ lệ còn lại ≤ <paramref name="maxPercent"/>% giá trị ban đầu): thường là khách tự khấu trừ chiết khấu / phí
    /// hoặc thanh toán thiếu. Chỉ là gợi ý để đối chiếu — không tự coi phần còn lại là chiết khấu.
    /// </summary>
    public async Task<IReadOnlyList<SuspectDeductionRow>> SuspectDeductionsAsync(SessionContext ctx, InvoiceKind kind, DateOnly asOf, decimal maxPercent = 5m)
    {
        ctx.Require(Permissions.ReportView);
        var acct = await RoleAsync(ctx.CompanyId, kind == InvoiceKind.Purchase ? "AP_TRADE" : "AR_TRADE");
        if (acct is null) return [];
        var open = (await new SettlementService(db, audit, clock).OpenItemsAsync(ctx, acct.Value, null, asOf)).Where(o => o.Open > 0 && o.Allocated > 0 && o.Amount > 0 && o.Open * 100m / o.Amount <= maxPercent).ToList();
        if (open.Count == 0) return [];
        var vids = open.Select(o => o.VoucherId).ToList();
        var invs = await db.CommercialInvoices.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId && i.VoucherId != null && vids.Contains(i.VoucherId.Value)).ToDictionaryAsync(i => i.VoucherId!.Value);
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        return open.Where(o => invs.ContainsKey(o.VoucherId)).Select(o =>
        {
            var i = invs[o.VoucherId]; var p = partners[o.PartnerId];
            return new SuspectDeductionRow(i.Id, i.Number, i.InvoiceNo, p.Code, p.Name, o.Amount, o.Allocated, o.Open, Math.Round(o.Open * 100m / o.Amount, 2), i.InvoiceDate);
        }).OrderByDescending(r => r.Open).ToList();
    }

    /// <summary>DSO (bán) / DPO (mua) theo tháng = số dư cuối tháng của TK công nợ ÷ phát sinh ghi hóa đơn trong tháng (cả thuế) × số ngày của tháng.</summary>
    public async Task<IReadOnlyList<DsoDpoRow>> DaysAsync(SessionContext ctx, InvoiceKind kind, DateOnly from, DateOnly to)
    {
        ctx.Require(Permissions.ReportView);
        var acct = await RoleAsync(ctx.CompanyId, kind == InvoiceKind.Purchase ? "AP_TRADE" : "AR_TRADE");
        if (acct is null) return [];
        var invs = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.CompanyId == ctx.CompanyId && i.Kind == kind && i.Purpose == InvoicePurpose.Normal && i.Status == InvoiceStatus.Posted && i.PostingDate >= from && i.PostingDate <= to).ToListAsync();
        var rows = new List<DsoDpoRow>();
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
        {
            var end = m.AddMonths(1).AddDays(-1);
            var dim = DateTime.DaysInMonth(m.Year, m.Month);
            var turnover = invs.Where(i => i.PostingDate >= m && i.PostingDate <= end).Sum(i => i.Lines.Sum(l => l.FunctionalAmount + l.VatAmount));
            var bal = Math.Abs(await ledger.AccountBalanceAsync(ctx, acct.Value, end));
            rows.Add(new DsoDpoRow($"{m.Year}-{m.Month:00}", bal, turnover, dim, turnover == 0 ? 0 : Math.Round(bal / turnover * dim, 1)));
        }
        return rows;
    }

    /// <summary>Hiệu quả của các chiết khấu thanh toán ĐÃ GHI SỔ trong kỳ: chiết khấu, tiền thực, số ngày thanh toán sớm hơn hạn và chi phí tín dụng quy năm = CK ÷ tiền thực × 365 ÷ số ngày sớm.</summary>
    public async Task<DiscountEffect> EffectAsync(SessionContext ctx, InvoiceKind kind, DateOnly from, DateOnly to)
    {
        ctx.Require(Permissions.ReportView);
        var role = kind == InvoiceKind.Purchase ? "PAYMENT_DISCOUNT_INCOME" : "PAYMENT_DISCOUNT_EXPENSE";
        var acct = await RoleAsync(ctx.CompanyId, role);
        if (acct is null) return new([], 0, 0, null);
        var vouchers = await db.Vouchers.AsNoTracking().Include(v => v.Lines)
            .Where(v => v.CompanyId == ctx.CompanyId && v.SourceSystem == PaymentDiscountService.Source && v.Status == VoucherStatus.Posted && v.PostingDate >= from && v.PostingDate <= to).ToListAsync();
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var rows = new List<DiscountEffectRow>();
        foreach (var v in vouchers.OrderBy(v => v.PostingDate).ThenBy(v => v.Number, StringComparer.Ordinal))
        {
            var discount = v.Lines.Where(l => l.AccountId == acct && l.SettleJournalLineId == null).Sum(l => l.Amount);
            if (discount == 0) continue;                                          // chứng từ của chiều kia
            var settleLines = v.Lines.Where(l => l.SettleJournalLineId != null).ToList();
            var settled = settleLines.Sum(l => l.Amount);
            // Hạn thanh toán thông thường: DueDate của hóa đơn bù trừ (nếu có); lấy hạn sớm nhất cho chứng từ gộp.
            DateOnly? due = null;
            foreach (var sl in settleLines)
            {
                var vid = await (from jl in db.JournalLines.AsNoTracking() join e in db.JournalEntries.AsNoTracking() on jl.JournalEntryId equals e.Id where jl.Id == sl.SettleJournalLineId select (Guid?)e.VoucherId).FirstOrDefaultAsync();
                var d = await db.CommercialInvoices.AsNoTracking().Where(i => i.VoucherId == vid).Select(i => i.DueDate).FirstOrDefaultAsync();
                if (d is { } dd && (due is null || dd < due)) due = dd;
            }
            var cash = settled - discount;
            var early = due is { } x ? x.DayNumber - v.PostingDate.DayNumber : (int?)null;
            decimal? annual = early is > 0 && cash > 0 ? Math.Round(discount / cash * 365m / early.Value * 100m, 2) : null;
            var pid = settleLines.Select(l => l.PartnerId).FirstOrDefault(p => p is not null);
            var p = pid is { } g && partners.TryGetValue(g, out var pp) ? pp : null;
            rows.Add(new DiscountEffectRow(v.Number, v.PostingDate, p?.Code ?? "", p?.Name ?? "", settled, discount, cash, early, annual));
        }
        var totalDiscount = rows.Sum(r => r.Discount); var totalCash = rows.Sum(r => r.Cash);
        var weighted = rows.Where(r => r.DaysEarly is > 0).ToList();
        decimal? w = weighted.Count == 0 ? null : Math.Round(weighted.Sum(r => r.Discount) / weighted.Sum(r => r.Cash * r.DaysEarly!.Value) * 365m * 100m, 2);   // CK ÷ (tiền × ngày) × 365
        return new DiscountEffect(rows, totalDiscount, totalCash, w);
    }
}
