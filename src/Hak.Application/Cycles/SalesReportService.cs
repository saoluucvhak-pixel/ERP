using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

/// <summary>Hóa đơn bán còn phải thu trong hàng chờ thu tiền theo ngày đáo hạn.</summary>
public sealed record ReceivableQueueRow(Guid InvoiceId, string Number, string InvoiceSeries, string InvoiceNo, DateOnly InvoiceDate, DateOnly? DueDate, int DaysOverdue,
    Guid PartnerId, string PartnerCode, string PartnerName, decimal Total, decimal Open, Guid SettleJournalLineId);

public sealed record SalesAnalysisRow(string Key, string Name, decimal Quantity, decimal Revenue, decimal Vat, int Invoices);
public sealed record SalesAnalysis(DateOnly FromDate, DateOnly ToDate, IReadOnlyList<SalesAnalysisRow> ByItem, IReadOnlyList<SalesAnalysisRow> ByPartner, IReadOnlyList<SalesAnalysisRow> ByMonth,
    decimal TotalRevenue, decimal TotalVat);

public sealed record CollectionRow(string PartnerCode, string PartnerName, decimal Collected, int Receipts);
public sealed record CollectionReport(DateOnly FromDate, DateOnly ToDate, IReadOnlyList<CollectionRow> ByPartner, decimal Total);

/// <summary>
/// Báo cáo chu trình doanh thu (tệp phân tích ERP, mục V.1.2.3): hàng chờ thu tiền theo ngày đáo hạn, phân tích tiêu thụ (doanh thu, số lượng theo
/// mặt hàng, khách hàng, tháng) và báo cáo thu tiền bán hàng. Tính từ hóa đơn và sổ đã ghi sổ; hàng trả lại và giảm giá ghi âm.
/// </summary>
public sealed class SalesReportService(HakDbContext db, SettlementService settlement)
{
    public async Task<IReadOnlyList<ReceivableQueueRow>> ReceivableQueueAsync(SessionContext ctx, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var ar = (await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == "AR_TRADE"))?.AccountId
                 ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, "Chưa ánh xạ vai trò tài khoản AR_TRADE.");
        var invoices = await db.CommercialInvoices.AsNoTracking()
            .Where(i => i.CompanyId == ctx.CompanyId && i.Kind == InvoiceKind.Sales && i.Purpose == InvoicePurpose.Normal && i.Status == InvoiceStatus.Posted
                        && i.VoucherId != null && i.Currency == null && i.PostingDate <= asOf).ToListAsync();
        var voucherIds = invoices.Select(i => i.VoucherId!.Value).ToList();
        var lines = await (from e in db.JournalEntries.AsNoTracking().Where(e => e.CompanyId == ctx.CompanyId && voucherIds.Contains(e.VoucherId))
                           join l in db.JournalLines.AsNoTracking().Where(l => l.AccountId == ar && l.Side == Side.Debit) on e.Id equals l.JournalEntryId
                           select new { e.VoucherId, l.Id, l.PartnerId, l.LineNo }).ToListAsync();
        var open = (await settlement.OpenItemsAsync(ctx, ar, null, asOf)).ToDictionary(o => o.JournalLineId);
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var rows = new List<ReceivableQueueRow>();
        foreach (var inv in invoices)
        {
            var line = lines.Where(l => l.VoucherId == inv.VoucherId && l.PartnerId == inv.PartnerId).OrderBy(l => l.LineNo).FirstOrDefault();
            if (line is null || !open.TryGetValue(line.Id, out var item) || item.Open <= 0) continue;
            var p = partners[inv.PartnerId];
            rows.Add(new ReceivableQueueRow(inv.Id, inv.Number, inv.InvoiceSeries, inv.InvoiceNo, inv.InvoiceDate, inv.DueDate,
                inv.DueDate is { } d && d < asOf ? asOf.DayNumber - d.DayNumber : 0, inv.PartnerId, p.Code, p.Name, item.Amount, item.Open, line.Id));
        }
        return rows.OrderBy(r => r.DueDate ?? DateOnly.MaxValue).ThenBy(r => r.InvoiceDate).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
    }

    public async Task<SalesAnalysis> AnalyseAsync(SessionContext ctx, DateOnly fromDate, DateOnly toDate)
    {
        ctx.Require(Permissions.ReportView);
        var raw = await (from l in db.CommercialInvoiceLines.AsNoTracking()
                         join i in db.CommercialInvoices.AsNoTracking() on l.InvoiceId equals i.Id
                         where i.CompanyId == ctx.CompanyId && i.Kind == InvoiceKind.Sales && i.Status == InvoiceStatus.Posted && i.PostingDate >= fromDate && i.PostingDate <= toDate
                         select new { i.Id, i.PartnerId, i.PostingDate, i.Purpose, l.ItemId, l.StockQuantity, l.FunctionalAmount, l.VatAmount }).ToListAsync();
        var items = await db.Items.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId).ToDictionaryAsync(x => x.Id);
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        // Returns and discounts reduce revenue; increases add to it.
        static int Sign(InvoicePurpose p) => p is InvoicePurpose.Return or InvoicePurpose.Discount ? -1 : 1;
        var lines = raw.Select(r => (r.Id, r.PartnerId, r.PostingDate, r.ItemId, Qty: Sign(r.Purpose) * (r.Purpose is InvoicePurpose.Normal or InvoicePurpose.Return ? r.StockQuantity ?? 0 : 0),
            Rev: Sign(r.Purpose) * r.FunctionalAmount, Vat: Sign(r.Purpose) * r.VatAmount)).ToList();

        static SalesAnalysisRow Row(string key, string name, IEnumerable<(Guid Id, Guid PartnerId, DateOnly PostingDate, Guid? ItemId, decimal Qty, decimal Rev, decimal Vat)> g)
        {
            var list = g.ToList();
            return new SalesAnalysisRow(key, name, list.Sum(x => x.Qty), list.Sum(x => x.Rev), list.Sum(x => x.Vat), list.Select(x => x.Id).Distinct().Count());
        }
        var byItem = lines.GroupBy(l => l.ItemId).Select(g => Row(g.Key is { } id ? items[id].Code : "", g.Key is { } i2 ? items[i2].Name : "(không theo vật tư)", g)).OrderByDescending(r => r.Revenue).ToList();
        var byPartner = lines.GroupBy(l => l.PartnerId).Select(g => Row(partners[g.Key].Code, partners[g.Key].Name, g)).OrderByDescending(r => r.Revenue).ToList();
        var byMonth = lines.GroupBy(l => $"{l.PostingDate.Year:D4}-{l.PostingDate.Month:D2}").Select(g => Row(g.Key, g.Key, g)).OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        return new SalesAnalysis(fromDate, toDate, byItem, byPartner, byMonth, lines.Sum(l => l.Rev), lines.Sum(l => l.Vat));
    }

    /// <summary>Báo cáo thu tiền bán hàng: tiền khách đã trả (phiếu thu tiền mặt và báo có ngân hàng ghi bên Có của TK phải thu) theo khách hàng.</summary>
    public async Task<CollectionReport> CollectionsAsync(SessionContext ctx, DateOnly fromDate, DateOnly toDate)
    {
        ctx.Require(Permissions.ReportView);
        var ar = (await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == "AR_TRADE"))?.AccountId
                 ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, "Chưa ánh xạ vai trò tài khoản AR_TRADE.");
        var rows = await (from e in db.JournalEntries.AsNoTracking().Where(e => e.CompanyId == ctx.CompanyId && (e.VoucherType == VoucherTypes.CashReceipt || e.VoucherType == VoucherTypes.BankReceipt))
                          join l in db.JournalLines.AsNoTracking().Where(l => l.AccountId == ar && l.Side == Side.Credit && l.PostingDate >= fromDate && l.PostingDate <= toDate) on e.Id equals l.JournalEntryId
                          select new { l.PartnerId, l.Amount, e.VoucherId }).ToListAsync();
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var by = rows.Where(r => r.PartnerId != null).GroupBy(r => r.PartnerId!.Value)
            .Select(g => new CollectionRow(partners[g.Key].Code, partners[g.Key].Name, g.Sum(x => x.Amount), g.Select(x => x.VoucherId).Distinct().Count()))
            .OrderByDescending(r => r.Collected).ToList();
        return new CollectionReport(fromDate, toDate, by, by.Sum(r => r.Collected));
    }
}
