using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

public sealed record MatchFlag(string Code, bool Blocking, string Message);
public sealed record MatchLineView(int LineNo, string? ItemCode, decimal? InvoicedQty, decimal? ReceivedQty, decimal? AcceptedQty, decimal? OrderedQty, decimal UnitPrice, decimal? OrderPrice,
    IReadOnlyList<MatchFlag> Flags);
public sealed record PurchaseMatch(Guid InvoiceId, string Number, IReadOnlyList<MatchLineView> Lines, int BlockingCount, bool Matched)
{
    public IEnumerable<MatchFlag> Flags => Lines.SelectMany(l => l.Flags);
}

/// <summary>
/// Đối chiếu nhiều bên của hóa đơn mua (tệp phân tích ERP, mục V.2.2.3): hóa đơn ↔ đơn đặt hàng ↔ phiếu nhập kho ↔ biên bản kiểm nghiệm.
/// Cờ chặn (phải xử lý hoặc xác nhận kèm lý do trước khi chấp nhận thanh toán): số lượng hóa đơn vượt số lượng thực nhận đạt trên biên bản kiểm nghiệm;
/// đơn giá hóa đơn khác đơn giá đơn đặt hàng (cùng cơ sở tính giá). Cờ thông tin: không gắn phiếu nhập, không theo đơn, chưa có biên bản kiểm nghiệm.
/// </summary>
public sealed class PurchaseMatchService(HakDbContext db)
{
    public Task<PurchaseMatch> MatchAsync(SessionContext ctx, Guid invoiceId)
    {
        ctx.Require(Permissions.ReportView);
        return MatchCoreAsync(ctx.CompanyId, invoiceId);
    }

    public async Task<IReadOnlyDictionary<Guid, PurchaseMatch>> MatchManyAsync(SessionContext ctx, IReadOnlyList<Guid> invoiceIds)
    {
        ctx.Require(Permissions.ReportView);
        var result = new Dictionary<Guid, PurchaseMatch>();
        foreach (var id in invoiceIds) result[id] = await MatchCoreAsync(ctx.CompanyId, id);
        return result;
    }

    internal async Task<PurchaseMatch> MatchCoreAsync(Guid companyId, Guid invoiceId)
    {
        var inv = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == companyId)
                  ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn không tồn tại.");
        var docIds = inv.Lines.Where(l => l.StockDocumentId != null).Select(l => l.StockDocumentId!.Value).Distinct().ToList();
        var docs = await db.StockDocuments.AsNoTracking().Include(d => d.Lines).Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id);
        var bbs = await db.GoodsInspections.AsNoTracking().Include(i => i.Lines).Where(i => docIds.Contains(i.StockDocumentId) && !i.IsCancelled).ToListAsync();
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id);
        var orderIds = inv.Lines.Select(l => l.OrderId).Concat(docs.Values.SelectMany(d => d.Lines).Select(l => l.OrderId)).Where(o => o != null).Select(o => o!.Value).Distinct().ToList();
        var orders = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id);

        var lines = new List<MatchLineView>();
        foreach (var l in inv.Lines.OrderBy(x => x.LineNo))
        {
            var flags = new List<MatchFlag>();
            decimal? received = null, accepted = null, ordered = null, orderPrice = null;
            decimal? invoiced = l.StockQuantity;
            Guid? orderId = l.OrderId; int? orderLineNo = l.OrderLineNo;
            if (l.StockDocumentId is { } sd && docs.TryGetValue(sd, out var doc))
            {
                var sl = doc.Lines.FirstOrDefault(x => x.LineNo == l.StockLineNo);
                received = sl?.Quantity; invoiced ??= sl?.Quantity;
                orderId ??= sl?.OrderId; orderLineNo ??= sl?.OrderLineNo;
                var bb = bbs.FirstOrDefault(b => b.StockDocumentId == sd);
                if (bb is null) flags.Add(new MatchFlag("NO_INSPECTION", false, $"Phiếu nhập {doc.Number} chưa có biên bản kiểm nghiệm."));
                else
                {
                    accepted = bb.Lines.FirstOrDefault(x => x.StockLineNo == l.StockLineNo)?.AcceptedQty;
                    if (accepted is { } a && invoiced is { } q && q > a)
                        flags.Add(new MatchFlag("OVER_ACCEPTED", true, $"Hóa đơn {q} vượt số lượng thực nhận đạt {a} theo biên bản {bb.Number}."));
                }
            }
            else flags.Add(new MatchFlag("NO_RECEIPT", false, "Dòng không gắn phiếu nhập kho (dịch vụ hoặc chi phí)."));
            if (orderId is { } oid && orders.TryGetValue(oid, out var order) && order.Lines.FirstOrDefault(x => x.LineNo == orderLineNo) is { } ol)
            {
                ordered = ol.Quantity; orderPrice = ol.UnitPrice;
                if (ol.Basis == l.Basis && ol.UnitPrice != l.UnitPrice)
                    flags.Add(new MatchFlag("PRICE_DIFF", true, $"Đơn giá hóa đơn {l.UnitPrice:N0} khác đơn giá đơn đặt hàng {order.Number} ({ol.UnitPrice:N0})."));
            }
            else if (l.StockDocumentId is not null) flags.Add(new MatchFlag("NO_ORDER", false, "Dòng không theo đơn đặt hàng."));
            lines.Add(new MatchLineView(l.LineNo, l.ItemId is { } it && items.TryGetValue(it, out var item) ? item.Code : null, invoiced, received, accepted, ordered, l.UnitPrice, orderPrice, flags));
        }
        var blocking = lines.Sum(x => x.Flags.Count(f => f.Blocking));
        return new PurchaseMatch(inv.Id, inv.Number, lines, blocking, blocking == 0);
    }
}
