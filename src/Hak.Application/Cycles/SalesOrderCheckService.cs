using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Data;
using Hak.Application.Inventory;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

public sealed record SalesOrderStockLine(int LineNo, Guid ItemId, string ItemCode, string ItemName, string Unit, decimal Ordered, decimal OnHand, decimal Shortfall);

/// <summary>
/// Kết quả "kiểm tra tín dụng và tồn kho" khi nhận đơn đặt hàng (tệp phân tích ERP, mục V.1.2.1): so hạn mức tín dụng của khách với
/// số dư công nợ hiện tại cộng giá trị ước tính của đơn; so số lượng đặt với tồn hiện tại. Thiếu hàng không chặn (đơn giao trễ).
/// </summary>
public sealed record SalesOrderCheck(Guid OrderId, string Number, Guid PartnerId, string PartnerName, decimal? CreditLimit, bool CreditCheckApplied, string? CreditCheckNote,
    decimal CurrentBalance, decimal OverdueAmount, decimal OrderValue, decimal BalanceAfter, bool ExceedsCredit, IReadOnlyList<SalesOrderStockLine> Lines, bool AnyShortage,
    decimal DeliveredNotInvoiced = 0, decimal OpenCommitments = 0, string? EstimateNote = null);

/// <summary>
/// Hạn mức tín dụng so với: khoản phải thu đang mở + hàng đã giao chưa lập hóa đơn + cam kết chưa giao của các đơn bán đã xác nhận khác của khách + giá trị đơn đang xét.
/// Phần đã chuyển sang bước sau chỉ được tính ở bước sau (đã giao thì không còn nằm trong cam kết chưa giao; đã lập hóa đơn thì nằm trong phải thu), nên không cộng trùng.
/// Dòng giá theo BDMT: lượng đặt là MT, được quy ra BDMT bằng độ khô bình quân thời điểm của tồn kho (công ty xác nhận 21/10/2026); chưa có cơ sở thì tạm tính theo MT (cận trên) và ghi chú.
/// </summary>
public sealed class SalesOrderCheckService(HakDbContext db, SettlementService settlement, IClock clock, DrynessService? dryness = null)
{
    public async Task<SalesOrderCheck> CheckAsync(SessionContext ctx, Guid orderId)
    {
        ctx.Require(Permissions.VoucherView);
        var order = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == ctx.CompanyId)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Đơn hàng không tồn tại.");
        if (order.Kind != InvoiceKind.Sales) throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ kiểm tra tín dụng và tồn kho cho đơn bán.");
        return await ComputeAsync(ctx, order);
    }

    /// <summary>Used by order confirmation inside its transaction; <paramref name="order"/> must have its lines loaded.</summary>
    public async Task<SalesOrderCheck> ComputeAsync(SessionContext ctx, TradeOrder order)
    {
        var partner = await db.BusinessPartners.AsNoTracking().FirstAsync(p => p.Id == order.PartnerId);
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var terms = await Hak.Application.Setup.GroupDefaults.PartnerTermsAsync(db, ctx.CompanyId, partner);
        var creditLimit = terms.CreditLimit;

        // Estimated order value incl. VAT, in functional currency (VND, rounded per line). Foreign-currency orders are not compared with the VND limit.
        var drynessPct = await DrynessByItemAsync(ctx.CompanyId, today);
        var estimateNotes = new List<string>();
        decimal Money(TradeOrderLine l, decimal qty)
        {
            var basisQty = qty;
            if (l.Basis == PricingBasis.Bdmt)
            {
                if (drynessPct.TryGetValue(l.ItemId, out var pct)) basisQty = qty * pct / 100m;
                else if (!estimateNotes.Contains("MT")) estimateNotes.Add("MT");
            }
            return Math.Round(basisQty * l.UnitPrice * (1 + (l.VatRate ?? 0) / 100m), 0, MidpointRounding.AwayFromZero);
        }
        var value = order.Lines.Sum(l => Money(l, l.Quantity));
        decimal balance = 0, overdue = 0, deliveredNotInvoiced = 0, commitments = 0;
        string? note = null;
        var applied = creditLimit is not null && order.Currency is null;
        if (creditLimit is null) note = "Khách hàng chưa khai báo hạn mức tín dụng: không kiểm tra.";
        else if (order.Currency is not null) note = "Đơn ngoại tệ: không so với hạn mức VND.";
        else
        {
            var ar = (await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == "AR_TRADE"))?.AccountId;
            if (ar is null) { applied = false; note = "Chưa ánh xạ vai trò AR_TRADE: không tính được công nợ."; }
            else
            {
                var open = await settlement.OpenItemsAsync(ctx, ar.Value, partner.Id, today);
                balance = open.Where(o => o.Side == Hak.Domain.Vouchers.Side.Debit).Sum(o => o.Open) - open.Where(o => o.Side == Hak.Domain.Vouchers.Side.Credit).Sum(o => o.Open);
                overdue = open.Where(o => o.Side == Hak.Domain.Vouchers.Side.Debit && o.DueDate < today).Sum(o => o.Open);
                (deliveredNotInvoiced, commitments) = await OtherCommitmentsAsync(ctx.CompanyId, order, Money);
            }
        }

        var itemIds = order.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var moves = await db.StockMovements.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && itemIds.Contains(m.ItemId)).Select(m => new { m.ItemId, m.Quantity }).ToListAsync();
        var onHand = moves.GroupBy(m => m.ItemId).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
        var lines = order.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var have = onHand.GetValueOrDefault(l.ItemId);
            return new SalesOrderStockLine(l.LineNo, l.ItemId, items[l.ItemId].Code, items[l.ItemId].Name, items[l.ItemId].Unit, l.Quantity, have, items[l.ItemId].IsService ? 0 : Math.Max(0, l.Quantity - have));
        }).ToList();

        var after = balance + deliveredNotInvoiced + commitments + value;
        string? estimate = estimateNotes.Count > 0 ? "Dòng giá theo BDMT chưa có độ khô bình quân thời điểm: tạm tính theo MT (cận trên)." : null;
        return new SalesOrderCheck(order.Id, order.Number, partner.Id, partner.Name, creditLimit, applied, note, balance, overdue, value, after,
            applied && after > creditLimit!.Value, lines, lines.Any(l => l.Shortfall > 0), deliveredNotInvoiced, commitments, estimate);
    }

    /// <summary>Độ khô bình quân thời điểm (BDMT tồn ÷ MT tồn, kể cả phần tạm tính) theo vật tư; vật tư không có tồn hoặc không theo dõi độ khô không có mặt trong kết quả.</summary>
    private async Task<Dictionary<Guid, decimal>> DrynessByItemAsync(Guid companyId, DateOnly today)
    {
        if (dryness is null) return [];
        var rows = await dryness.SummaryCoreAsync(companyId, new DateOnly(today.Year, 1, 1), today);
        return rows.GroupBy(r => r.ItemId).Select(g => (Item: g.Key, Mt: g.Sum(r => r.ClosingMt), Bdmt: g.Sum(r => r.ClosingBdmt + r.EstimatedClosingBdmt)))
            .Where(x => x.Mt > 0 && x.Bdmt > 0).ToDictionary(x => x.Item, x => Math.Min(100m, x.Bdmt * 100m / x.Mt));
    }

    /// <summary>
    /// Các đơn bán VND đã xác nhận khác của cùng khách: hàng đã xuất kho chưa lập hóa đơn ghi sổ, và phần chưa giao (hàng) / chưa lập hóa đơn (dịch vụ). Hóa đơn đã ghi sổ nằm trong phải thu nên bị loại.
    /// </summary>
    private async Task<(decimal DeliveredNotInvoiced, decimal Open)> OtherCommitmentsAsync(Guid companyId, TradeOrder current, Func<TradeOrderLine, decimal, decimal> money)
    {
        var others = await db.TradeOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == companyId && o.Kind == InvoiceKind.Sales && o.PartnerId == current.PartnerId && o.Status == OrderStatus.Confirmed && o.Id != current.Id && o.Currency == null).ToListAsync();
        if (others.Count == 0) return (0, 0);
        var ids = others.Select(o => o.Id).ToList();
        var itemIds = others.SelectMany(o => o.Lines).Select(l => l.ItemId).Distinct().ToList();
        var services = (await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id) && i.IsService).Select(i => i.Id).ToListAsync()).ToHashSet();

        var issues = await (from l in db.StockDocumentLines join d in db.StockDocuments on l.DocumentId equals d.Id
                            where d.CompanyId == companyId && d.Type == StockDocumentType.Issue && d.Status == StockDocumentStatus.Posted && l.OrderId != null && ids.Contains(l.OrderId.Value)
                            select new { Order = l.OrderId!.Value, Line = l.OrderLineNo!.Value, Doc = l.DocumentId, l.LineNo, l.Quantity }).ToListAsync();
        var invoiceLines = await (from l in db.CommercialInvoiceLines join i in db.CommercialInvoices on l.InvoiceId equals i.Id
                                  where i.CompanyId == companyId && i.Status == InvoiceStatus.Posted && i.Purpose == InvoicePurpose.Normal
                                        && ((l.OrderId != null && ids.Contains(l.OrderId.Value)) || l.StockDocumentId != null)
                                  select new { l.OrderId, l.OrderLineNo, l.StockDocumentId, l.StockLineNo, l.StockQuantity, l.BasisQuantity }).ToListAsync();
        var invoicedByStock = invoiceLines.Where(x => x.StockDocumentId != null).GroupBy(x => (x.StockDocumentId!.Value, x.StockLineNo ?? 0)).ToDictionary(g => g.Key, g => g.Sum(x => x.StockQuantity ?? 0));
        var invoicedService = invoiceLines.Where(x => x.OrderId != null && x.StockDocumentId == null).GroupBy(x => (x.OrderId!.Value, x.OrderLineNo ?? 0)).ToDictionary(g => g.Key, g => g.Sum(x => x.BasisQuantity));

        decimal notInvoiced = 0, open = 0;
        foreach (var o in others)
            foreach (var l in o.Lines)
            {
                if (services.Contains(l.ItemId))
                {
                    open += money(l, Math.Max(0, l.Quantity - invoicedService.GetValueOrDefault((o.Id, l.LineNo))));
                    continue;
                }
                var lineIssues = issues.Where(x => x.Order == o.Id && x.Line == l.LineNo).ToList();
                var issued = lineIssues.Sum(x => x.Quantity);
                open += money(l, Math.Max(0, l.Quantity - issued));
                notInvoiced += money(l, lineIssues.Sum(x => Math.Max(0, x.Quantity - invoicedByStock.GetValueOrDefault((x.Doc, x.LineNo)))));
            }
        return (notInvoiced, open);
    }
}
