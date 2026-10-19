using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

public sealed record ReorderRow(Guid ItemId, string Code, string Name, string Unit, decimal OnHand, decimal MinStock, decimal SuggestedQty, decimal? LastPrice = null, Guid? LastPartnerId = null);

/// <summary>
/// Danh mục cần đặt hàng (tệp phân tích ERP, mục V.2.2 "nhận biết nhu cầu"): vật tư có tồn hiện tại thấp hơn tồn tối thiểu; số lượng đề xuất là
/// số lượng đặt tối ưu của vật tư (nếu chưa khai báo thì phần thiếu so với tồn tối thiểu). Tính khi xem, không lưu: luôn khớp với sổ kho.
/// </summary>
public sealed class ReorderService(HakDbContext db)
{
    public async Task<IReadOnlyList<ReorderRow>> ListAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId && i.IsActive && i.MinStock != null).ToListAsync();
        if (items.Count == 0) return [];
        var ids = items.Select(i => i.Id).ToList();
        var moves = await db.StockMovements.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && ids.Contains(m.ItemId)).Select(m => new { m.ItemId, m.Quantity }).ToListAsync();
        var onHand = moves.GroupBy(m => m.ItemId).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
        var low = items.Select(i => (Item: i, Have: onHand.GetValueOrDefault(i.Id))).Where(x => x.Have < x.Item.MinStock!.Value).ToList();
        // Giá và nhà cung cấp của đơn mua gần nhất (đơn không bị hủy) để gợi ý khi tạo đơn mua nháp.
        var lowIds = low.Select(x => x.Item.Id).ToList();
        var recent = await (from l in db.TradeOrderLines join o in db.TradeOrders on l.OrderId equals o.Id
                            where o.CompanyId == ctx.CompanyId && o.Kind == InvoiceKind.Purchase && o.Status != OrderStatus.Cancelled && lowIds.Contains(l.ItemId)
                            orderby o.OrderDate descending, o.Number descending
                            select new { l.ItemId, l.UnitPrice, o.PartnerId, l.Basis, o.Currency }).ToListAsync();
        var last = recent.Where(r => r.Basis == PricingBasis.Quantity && r.Currency == null).GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.First());
        return low
            .Select(x => new ReorderRow(x.Item.Id, x.Item.Code, x.Item.Name, x.Item.Unit, x.Have, x.Item.MinStock!.Value, x.Item.ReorderQty ?? x.Item.MinStock!.Value - x.Have,
                last.TryGetValue(x.Item.Id, out var r) ? r.UnitPrice : null, last.TryGetValue(x.Item.Id, out var r2) ? r2.PartnerId : null))
            .OrderBy(r => r.Code, StringComparer.Ordinal).ToList();
    }
}
