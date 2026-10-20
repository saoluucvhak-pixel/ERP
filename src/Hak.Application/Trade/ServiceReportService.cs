using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record ServiceReportRow(string Key, string Name, decimal Quantity, decimal Amount, decimal Vat, int Invoices);
/// <summary>Dịch vụ đã nghiệm thu nhưng chưa lập hóa đơn: số lượng và giá trị ước tính theo đơn giá đơn hàng (chưa thuế).</summary>
public sealed record ServiceBacklogRow(string OrderNumber, int OrderLineNo, string PartnerCode, string PartnerName, string ItemCode, string ItemName, decimal Quantity, decimal UnitPrice, decimal Amount);
public sealed record ServiceReport(InvoiceKind Kind, DateOnly FromDate, DateOnly ToDate, IReadOnlyList<ServiceReportRow> ByService, IReadOnlyList<ServiceReportRow> ByPartner, IReadOnlyList<ServiceReportRow> ByMonth,
    decimal Total, decimal TotalVat, IReadOnlyList<ServiceBacklogRow> Backlog, decimal BacklogAmount);

/// <summary>
/// Báo cáo dịch vụ: doanh thu dịch vụ bán (hoặc chi phí dịch vụ mua) trong kỳ theo dịch vụ, đối tượng, tháng — tách khỏi hàng hóa — và phần đã nghiệm thu chưa lập hóa đơn
/// (doanh thu / chi phí chờ ghi nhận). Tính từ hóa đơn đã ghi sổ; trả lại / giảm giá ghi âm.
/// </summary>
public sealed class ServiceReportService(HakDbContext db, ServiceAcceptanceService acceptances)
{
    public async Task<ServiceReport> ReportAsync(SessionContext ctx, InvoiceKind kind, DateOnly fromDate, DateOnly toDate)
    {
        ctx.Require(Permissions.ReportView);
        var raw = await (from l in db.CommercialInvoiceLines.AsNoTracking()
                         join i in db.CommercialInvoices.AsNoTracking() on l.InvoiceId equals i.Id
                         join it in db.Items.AsNoTracking() on l.ItemId equals it.Id
                         where i.CompanyId == ctx.CompanyId && i.Kind == kind && i.Status == InvoiceStatus.Posted && i.PostingDate >= fromDate && i.PostingDate <= toDate && it.IsService
                         select new { i.Id, i.PartnerId, i.PostingDate, i.Purpose, it.Code, it.Name, l.BasisQuantity, l.FunctionalAmount, l.VatAmount }).ToListAsync();
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        static int Sign(InvoicePurpose p) => p is InvoicePurpose.Return or InvoicePurpose.Discount ? -1 : 1;
        var lines = raw.Select(r => (r.Id, r.PartnerId, r.PostingDate, r.Code, r.Name, Qty: r.Purpose is InvoicePurpose.Discount ? 0 : Sign(r.Purpose) * r.BasisQuantity, Amt: Sign(r.Purpose) * r.FunctionalAmount, Vat: Sign(r.Purpose) * r.VatAmount)).ToList();
        static ServiceReportRow Row(string key, string name, IEnumerable<(Guid Id, Guid PartnerId, DateOnly PostingDate, string Code, string Name, decimal Qty, decimal Amt, decimal Vat)> g)
        { var l = g.ToList(); return new ServiceReportRow(key, name, l.Sum(x => x.Qty), l.Sum(x => x.Amt), l.Sum(x => x.Vat), l.Select(x => x.Id).Distinct().Count()); }
        var byService = lines.GroupBy(l => (l.Code, l.Name)).Select(g => Row(g.Key.Code, g.Key.Name, g)).OrderByDescending(r => r.Amount).ToList();
        var byPartner = lines.GroupBy(l => l.PartnerId).Select(g => Row(partners[g.Key].Code, partners[g.Key].Name, g)).OrderByDescending(r => r.Amount).ToList();
        var byMonth = lines.GroupBy(l => $"{l.PostingDate.Year:D4}-{l.PostingDate.Month:D2}").Select(g => Row(g.Key, g.Key, g)).OrderBy(r => r.Key, StringComparer.Ordinal).ToList();

        var backlog = new List<ServiceBacklogRow>();
        var orders = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.CompanyId == ctx.CompanyId && o.Kind == kind && o.Status == OrderStatus.Confirmed).ToDictionaryAsync(o => o.Id);
        foreach (var c in await acceptances.InvoiceCandidatesAsync(ctx, kind, null))
        {
            var o = orders[c.OrderId]; var p = partners[o.PartnerId];
            backlog.Add(new ServiceBacklogRow(c.OrderNumber, c.OrderLineNo, p.Code, p.Name, c.ItemCode, c.ItemName, c.Remaining, c.UnitPrice, Math.Round(c.Remaining * c.UnitPrice, 0, MidpointRounding.AwayFromZero)));
        }
        return new ServiceReport(kind, fromDate, toDate, byService, byPartner, byMonth, lines.Sum(l => l.Amt), lines.Sum(l => l.Vat), backlog, backlog.Sum(b => b.Amount));
    }
}
