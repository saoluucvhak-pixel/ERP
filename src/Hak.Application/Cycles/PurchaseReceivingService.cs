using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

/// <param name="Arrived">Lượng thực đến (theo biên bản kiểm nghiệm nếu có, không thì theo phiếu nhập): không biến mất khi phần không đạt bị loại khỏi phiếu.</param>
/// <param name="PendingQc">Phần đã nhận nhưng vật tư bắt buộc kiểm tra chất lượng và chưa có biên bản.</param>
public sealed record OrderReceivingLine(int LineNo, Guid ItemId, string ItemCode, string Unit, decimal Ordered, decimal Arrived, decimal Accepted, decimal Rejected, decimal PendingQc,
    decimal PostedToStock, decimal Invoiced, decimal NotYetArrived);
public sealed record OrderReceiving(Guid OrderId, string Number, string Status, IReadOnlyList<OrderReceivingLine> Lines);

/// <summary>
/// Theo dõi nhận hàng của đơn mua (Đợt 3): đặt – thực đến – đạt – không đạt – chờ kiểm nghiệm – đã nhập kho – đã lập hóa đơn. Chỉ là báo cáo đọc từ phiếu nhập, biên bản kiểm nghiệm
/// và hóa đơn; KHÔNG ghi sổ và không quyết định quyền sở hữu hay cách hạch toán hàng không đạt (chờ quyết định của doanh nghiệp).
/// </summary>
public sealed class PurchaseReceivingService(HakDbContext db)
{
    public async Task<OrderReceiving> ForOrderAsync(SessionContext ctx, Guid orderId)
    {
        ctx.Require(Permissions.ReportView);
        var order = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == ctx.CompanyId && o.Kind == InvoiceKind.Purchase)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Đơn mua không tồn tại.");
        var itemIds = order.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var receipts = await (from l in db.StockDocumentLines join d in db.StockDocuments on l.DocumentId equals d.Id
                              where d.CompanyId == ctx.CompanyId && d.Type == StockDocumentType.Receipt && (d.Status == StockDocumentStatus.Draft || d.Status == StockDocumentStatus.Posted) && l.OrderId == order.Id
                              select new { l.DocumentId, l.LineNo, OrderLine = l.OrderLineNo!.Value, l.ItemId, l.Quantity, d.Status }).ToListAsync();
        var docIds = receipts.Select(r => r.DocumentId).Distinct().ToList();
        var inspections = await (from il in db.GoodsInspectionLines join bb in db.GoodsInspections on il.InspectionId equals bb.Id
                                 where docIds.Contains(bb.StockDocumentId) && !bb.IsCancelled
                                 select new { bb.StockDocumentId, il.StockLineNo, il.ReceivedQty, il.AcceptedQty }).ToListAsync();
        var invoiceLines = await (from l in db.CommercialInvoiceLines join i in db.CommercialInvoices on l.InvoiceId equals i.Id
                                  where i.CompanyId == ctx.CompanyId && i.Kind == InvoiceKind.Purchase && i.Purpose == InvoicePurpose.Normal && (i.Status == InvoiceStatus.Draft || i.Status == InvoiceStatus.Posted)
                                        && ((l.OrderId == order.Id) || (l.StockDocumentId != null && docIds.Contains(l.StockDocumentId.Value)))
                                  select new { l.Id, l.OrderId, l.OrderLineNo, l.StockDocumentId, l.StockLineNo, l.StockQuantity, l.BasisQuantity, l.Basis }).ToListAsync();

        var lines = new List<OrderReceivingLine>();
        foreach (var ol in order.Lines.OrderBy(l => l.LineNo))
        {
            var item = items[ol.ItemId];
            decimal arrived = 0, accepted = 0, rejected = 0, pending = 0, posted = 0;
            var myReceipts = receipts.Where(r => r.OrderLine == ol.LineNo).ToList();
            foreach (var r in myReceipts)
            {
                var insp = inspections.FirstOrDefault(x => x.StockDocumentId == r.DocumentId && x.StockLineNo == r.LineNo);
                if (insp is not null) { arrived += insp.ReceivedQty; accepted += insp.AcceptedQty; rejected += insp.ReceivedQty - insp.AcceptedQty; }
                else if (item.RequiresQc) { arrived += r.Quantity; pending += r.Quantity; }
                else { arrived += r.Quantity; accepted += r.Quantity; }
                if (r.Status == StockDocumentStatus.Posted) posted += r.Quantity;
            }
            var keys = myReceipts.Select(r => (r.DocumentId, r.LineNo)).ToHashSet();
            var invoiced = invoiceLines.Where(l => (l.OrderId == order.Id && l.OrderLineNo == ol.LineNo) || (l.StockDocumentId is { } d && keys.Contains((d, l.StockLineNo ?? 0))))
                .Sum(l => l.StockQuantity ?? (l.Basis == PricingBasis.Quantity ? l.BasisQuantity : 0));
            lines.Add(new OrderReceivingLine(ol.LineNo, ol.ItemId, item.Code, item.Unit, ol.Quantity, arrived, accepted, rejected, pending, posted, invoiced, Math.Max(0, ol.Quantity - arrived)));
        }
        return new OrderReceiving(order.Id, order.Number, order.Status.ToString(), lines);
    }
}
