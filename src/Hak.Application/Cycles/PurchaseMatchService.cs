using System.Security.Cryptography;
using System.Text;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Cycles;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

public sealed record MatchFlag(string Code, bool Blocking, string Message);
public sealed record MatchLineView(int LineNo, string? ItemCode, decimal? InvoicedQty, decimal? ReceivedQty, decimal? AcceptedQty, decimal? OrderedQty, decimal UnitPrice, decimal? OrderPrice,
    IReadOnlyList<MatchFlag> Flags, decimal? CumulativeInvoicedOnReceipt = null, decimal? CumulativeInvoicedOnOrder = null);
/// <param name="SnapshotHash">Mã băm của dữ liệu đối chiếu (lượng lũy kế, nhận, đạt, đặt, giá, cờ, phiên bản chính sách); đổi nguồn thì mã băm đổi.</param>
public sealed record PurchaseMatch(Guid InvoiceId, string Number, IReadOnlyList<MatchLineView> Lines, int BlockingCount, bool Matched, string SnapshotHash = "", string SnapshotText = "", int? PolicyVersion = null)
{
    public IEnumerable<MatchFlag> Flags => Lines.SelectMany(l => l.Flags);
}

/// <summary>
/// Đối chiếu nhiều bên của hóa đơn mua (tệp phân tích ERP, mục V.2.2.3): hóa đơn ↔ đơn đặt hàng ↔ phiếu nhập kho ↔ biên bản kiểm nghiệm.
/// Cờ chặn (phải xử lý hoặc được người có quyền duyệt sai lệch duyệt trước khi chấp nhận thanh toán): LŨY KẾ số lượng các hóa đơn còn hiệu lực trên một dòng phiếu nhập
/// vượt số lượng thực nhận đạt; LŨY KẾ số lượng hóa đơn trên một dòng đơn vượt lượng đặt cộng dung sai lượng; đơn giá hóa đơn khác đơn giá đơn đặt hàng quá dung sai giá
/// (cùng cơ sở tính giá). Dung sai lấy từ chính sách có phiên bản (nhà cung cấp trước, công ty sau; không có thì bằng 0).
/// Cờ thông tin: không gắn phiếu nhập, không theo đơn, chưa có biên bản kiểm nghiệm, giá lệch trong dung sai.
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

        // Các hóa đơn mua khác còn hiệu lực cùng tham chiếu một dòng phiếu nhập / dòng đơn: lượng của chúng cộng dồn với hóa đơn đang xét.
        var orderDocIds = orderIds.Count == 0 ? [] : await db.StockDocumentLines.AsNoTracking().Where(sl => sl.CompanyId == companyId && sl.OrderId != null && orderIds.Contains(sl.OrderId.Value))
            .Select(sl => sl.DocumentId).Distinct().ToListAsync();
        var scopeDocIds = docIds.Concat(orderDocIds).Distinct().ToList();
        var others = await (from l in db.CommercialInvoiceLines join i in db.CommercialInvoices on l.InvoiceId equals i.Id
                            where i.CompanyId == companyId && i.Id != inv.Id && i.Kind == InvoiceKind.Purchase && i.Purpose == InvoicePurpose.Normal
                                  && (i.Status == InvoiceStatus.Draft || i.Status == InvoiceStatus.Posted)
                                  && ((l.StockDocumentId != null && scopeDocIds.Contains(l.StockDocumentId.Value)) || (l.OrderId != null && orderIds.Contains(l.OrderId.Value)))
                            select new { l.StockDocumentId, l.StockLineNo, l.OrderId, l.OrderLineNo, l.StockQuantity, l.BasisQuantity, l.Basis }).ToListAsync();
        var otherDocs = await db.StockDocuments.AsNoTracking().Include(d => d.Lines).Where(d => scopeDocIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id);
        var policy = await PolicyForAsync(companyId, inv.PartnerId, inv.InvoiceDate);
        var tolPricePct = policy?.PricePercent ?? 0; var tolPriceAbs = policy?.PriceAbsolute ?? 0; var tolQtyPct = policy?.QuantityPercent ?? 0;

        var lines = new List<MatchLineView>();
        foreach (var l in inv.Lines.OrderBy(x => x.LineNo))
        {
            var flags = new List<MatchFlag>();
            decimal? received = null, accepted = null, ordered = null, orderPrice = null;
            decimal? invoiced = l.StockQuantity;
            decimal? cumReceipt = null, cumOrder = null;
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
                    var priorOnReceipt = others.Where(o => o.StockDocumentId == sd && o.StockLineNo == l.StockLineNo).Sum(o => o.StockQuantity ?? 0);
                    cumReceipt = priorOnReceipt + (invoiced ?? 0);
                    if (accepted is { } a && cumReceipt is { } q && q > a)
                        flags.Add(new MatchFlag("OVER_ACCEPTED", true, priorOnReceipt > 0
                            ? $"Hóa đơn lũy kế {q} (gồm {priorOnReceipt} ở hóa đơn khác) vượt số lượng thực nhận đạt {a} theo biên bản {bb.Number}."
                            : $"Hóa đơn {q} vượt số lượng thực nhận đạt {a} theo biên bản {bb.Number}."));
                }
            }
            else flags.Add(new MatchFlag("NO_RECEIPT", false, "Dòng không gắn phiếu nhập kho (dịch vụ hoặc chi phí)."));
            if (orderId is { } oid && orders.TryGetValue(oid, out var order) && order.Lines.FirstOrDefault(x => x.LineNo == orderLineNo) is { } ol)
            {
                ordered = ol.Quantity; orderPrice = ol.UnitPrice;
                if (ol.Basis == l.Basis && ol.UnitPrice != l.UnitPrice)
                {
                    var allowed = Math.Max(tolPriceAbs, ol.UnitPrice * tolPricePct / 100m);
                    flags.Add(Math.Abs(l.UnitPrice - ol.UnitPrice) <= allowed
                        ? new MatchFlag("PRICE_WITHIN_TOLERANCE", false, $"Đơn giá hóa đơn {l.UnitPrice.Vn(0)} lệch đơn giá đơn {order.Number} ({ol.UnitPrice.Vn(0)}) nhưng trong dung sai {allowed.Vn(0)}.")
                        : new MatchFlag("PRICE_DIFF", true, $"Đơn giá hóa đơn {l.UnitPrice.Vn(0)} khác đơn giá đơn đặt hàng {order.Number} ({ol.UnitPrice.Vn(0)}), quá dung sai {allowed.Vn(0)}."));
                }
                // Lượng lũy kế trên dòng đơn (mọi hóa đơn còn hiệu lực): tính theo lượng phiếu kho; dòng không qua kho (dịch vụ) tính theo lượng tính giá khi cùng cơ sở lượng.
                decimal Qty(decimal? stock, decimal basisQty, PricingBasis basis) => stock ?? (basis == PricingBasis.Quantity ? basisQty : 0);
                var priorOnOrder = others.Where(o => o.OrderId == order.Id && o.OrderLineNo == orderLineNo || (o.StockDocumentId != null && otherDocs.TryGetValue(o.StockDocumentId.Value, out var od)
                                                       && od.Lines.FirstOrDefault(x => x.LineNo == o.StockLineNo) is { } osl && osl.OrderId == order.Id && osl.OrderLineNo == orderLineNo))
                    .Sum(o => Qty(o.StockQuantity, o.BasisQuantity, o.Basis));
                cumOrder = priorOnOrder + Qty(l.StockQuantity, l.BasisQuantity, l.Basis);
                var tolQty = Math.Max(tolQtyPct, order.OverDeliveryTolerancePercent);   // dung sai giao vượt đã ghi trên đơn vẫn được tôn trọng
                if (cumOrder > ol.Quantity * (1 + tolQty / 100m))
                    flags.Add(new MatchFlag("OVER_ORDER", true, $"Hóa đơn lũy kế {cumOrder} vượt lượng đặt {ol.Quantity} (dung sai {tolQty}%) của đơn {order.Number}."));
            }
            else if (l.StockDocumentId is not null) flags.Add(new MatchFlag("NO_ORDER", false, "Dòng không theo đơn đặt hàng."));
            lines.Add(new MatchLineView(l.LineNo, l.ItemId is { } it && items.TryGetValue(it, out var item) ? item.Code : null, invoiced, received, accepted, ordered, l.UnitPrice, orderPrice, flags, cumReceipt, cumOrder));
        }
        var blocking = lines.Sum(x => x.Flags.Count(f => f.Blocking));
        var text = string.Join("\n", lines.Select(x => $"{x.LineNo}|inv={x.InvoicedQty}|rec={x.ReceivedQty}|acc={x.AcceptedQty}|ord={x.OrderedQty}|cumR={x.CumulativeInvoicedOnReceipt}|cumO={x.CumulativeInvoicedOnOrder}|price={x.UnitPrice}|oprice={x.OrderPrice}|flags={string.Join(",", x.Flags.Select(f => f.Code))}"))
                   + $"\npolicy={policy?.Id}:{policy?.Version}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        return new PurchaseMatch(inv.Id, inv.Number, lines, blocking, blocking == 0, hash, text, policy?.Version);
    }

    /// <summary>Phiên bản chính sách dung sai đang hiệu lực tại ngày hóa đơn: của nhà cung cấp trước, rồi của công ty.</summary>
    private async Task<PurchaseMatchPolicy?> PolicyForAsync(Guid companyId, Guid partnerId, DateOnly on)
    {
        var active = await db.PurchaseMatchPolicies.AsNoTracking().Where(p => p.CompanyId == companyId && p.IsActive && p.EffectiveFrom <= on).ToListAsync();
        return active.Where(p => p.Scope == MatchPolicyScope.Partner && p.PartnerId == partnerId).OrderByDescending(p => p.Version).FirstOrDefault()
               ?? active.Where(p => p.Scope == MatchPolicyScope.Company).OrderByDescending(p => p.Version).FirstOrDefault();
    }
}
