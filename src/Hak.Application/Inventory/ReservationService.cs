using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Inventory;

public sealed record ReserveLineResult(int LineNo, Guid ItemId, string ItemCode, decimal Ordered, decimal Delivered, decimal AlreadyReserved, decimal NewlyReserved, decimal Waiting);

/// <summary>Kết quả giữ hàng cho một đơn: Waiting là phần chờ (chưa đủ tồn khả dụng để giữ), không phải lỗi.</summary>
public sealed record ReserveResult(Guid OrderId, string Number, IReadOnlyList<ReserveLineResult> Lines, bool AnyWaiting);

public sealed record ReservationView(Guid Id, Guid WarehouseId, string WarehouseCode, Guid ItemId, string ItemCode, Guid OrderId, string OrderNumber, int OrderLineNo,
    decimal Quantity, decimal Consumed, decimal Remaining, string Status, DateTime CreatedAt, DateTime? ClosedAt, string? CloseReason);

/// <summary>Tồn theo các trạng thái tách riêng: vật lý, đang giữ, khả dụng, đang về (đơn mua đã xác nhận chưa nhận). Hàng chờ QC chưa có chỗ trong mô hình dữ liệu nên chưa tách.</summary>
public sealed record StockAvailability(Guid ItemId, string ItemCode, Guid? WarehouseId, decimal OnHand, decimal Reserved, decimal Available, decimal Incoming);

/// <summary>
/// Giữ hàng theo kho cho đơn bán đã xác nhận (Đợt 2). Giữ hàng không tạo journal và không trừ tồn thực; thiếu tồn thì giữ phần có thể giữ và để phần chờ
/// (chị xác nhận 21/10/2026). Xác nhận đơn tự giữ; giải phóng thủ công hoặc khi đóng / hủy đơn; xuất kho theo đơn trừ dần phần giữ.
/// Chưa chặn xuất kho không gắn đơn lấy phần hàng đang giữ — khoảng trống đã ghi nhận ở docs/10.
/// </summary>
public sealed class ReservationService(HakDbContext db, AuditService audit, IClock clock)
{
    public Task<ReserveResult> ReserveAsync(SessionContext ctx, Guid orderId, Guid? warehouseId = null)
    {
        ctx.Require(Permissions.StockDocument);
        return db.InTransactionAsync(async () =>
        {
            var order = await db.TradeOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == ctx.CompanyId)
                        ?? throw new DomainException(ErrorCodes.NotFound, "Đơn hàng không tồn tại.");
            if (order.Kind != InvoiceKind.Sales || order.Status != OrderStatus.Confirmed)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Chỉ giữ hàng cho đơn bán đã xác nhận.");
            return await ReserveOrderAsync(db, audit, clock, ctx, order, warehouseId);
        });
    }

    /// <summary>Giữ hàng cho các dòng hàng hóa của đơn trong transaction hiện tại: lấy tối đa phần còn cần, từ kho nhiều hàng khả dụng nhất trước.</summary>
    internal static async Task<ReserveResult> ReserveOrderAsync(HakDbContext db, AuditService audit, IClock clock, SessionContext ctx, TradeOrder order, Guid? warehouseId)
    {
        var itemIds = order.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var warehouses = warehouseId is { } w
            ? await db.Warehouses.AsNoTracking().Where(x => x.Id == w && x.CompanyId == ctx.CompanyId).Select(x => x.Id).ToListAsync()
            : await db.Warehouses.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId).Select(x => x.Id).ToListAsync();
        if (warehouseId is not null && warehouses.Count == 0) throw new DomainException(ErrorCodes.NotFound, "Kho không tồn tại.");

        var moves = await db.StockMovements.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && itemIds.Contains(m.ItemId)).Select(m => new { m.ItemId, m.WarehouseId, m.Quantity }).ToListAsync();
        var held = (await db.StockReservations.Where(r => r.CompanyId == ctx.CompanyId && itemIds.Contains(r.ItemId) && r.Status == ReservationStatus.Active).ToListAsync());
        var delivered = await (from l in db.StockDocumentLines join d in db.StockDocuments on l.DocumentId equals d.Id
                               where d.CompanyId == ctx.CompanyId && d.Type == StockDocumentType.Issue && d.Status == StockDocumentStatus.Posted && l.OrderId == order.Id
                               select new { Line = l.OrderLineNo!.Value, l.Quantity }).ToListAsync();

        var results = new List<ReserveLineResult>();
        var created = new List<StockReservation>();
        foreach (var l in order.Lines.OrderBy(x => x.LineNo))
        {
            var item = items[l.ItemId];
            var done = delivered.Where(x => x.Line == l.LineNo).Sum(x => x.Quantity);
            var mine = held.Where(r => r.OrderId == order.Id && r.OrderLineNo == l.LineNo).Sum(r => r.Remaining);
            if (item.IsService) { results.Add(new ReserveLineResult(l.LineNo, l.ItemId, item.Code, l.Quantity, 0, 0, 0, 0)); continue; }
            var wanted = Math.Max(0, l.Quantity - done - mine);
            decimal taken = 0;
            foreach (var (wh, available) in warehouses
                         .Select(id => (id, avail: moves.Where(m => m.ItemId == l.ItemId && m.WarehouseId == id).Sum(m => m.Quantity)
                                                  - held.Concat(created).Where(r => r.ItemId == l.ItemId && r.WarehouseId == id && r.Status == ReservationStatus.Active).Sum(r => r.Remaining)))
                         .Where(x => x.avail > 0).OrderByDescending(x => x.avail))
            {
                var take = Math.Min(wanted - taken, available);
                if (take <= 0) break;
                created.Add(new StockReservation
                {
                    Id = Ids.New(), CompanyId = ctx.CompanyId, WarehouseId = wh, ItemId = l.ItemId, OrderId = order.Id, OrderLineNo = l.LineNo,
                    Quantity = take, Status = ReservationStatus.Active, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId,
                });
                taken += take;
            }
            results.Add(new ReserveLineResult(l.LineNo, l.ItemId, item.Code, l.Quantity, done, mine, taken, wanted - taken));
        }
        if (created.Count > 0)
        {
            db.StockReservations.AddRange(created);
            await audit.AppendAsync(ctx.CompanyId, ctx, "reservation.create", "order", order.Id.ToString(),
                new { order.Number, lines = created.Select(c => new { c.OrderLineNo, c.WarehouseId, c.Quantity }) });
            await db.SaveChangesAsync();
        }
        return new ReserveResult(order.Id, order.Number, results, results.Any(r => r.Waiting > 0));
    }

    public Task ReleaseAsync(SessionContext ctx, Guid reservationId, string reason)
    {
        ctx.Require(Permissions.StockDocument);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải ghi lý do giải phóng hàng giữ.");
        return db.InTransactionAsync(async () =>
        {
            var r = await db.StockReservations.FirstOrDefaultAsync(x => x.Id == reservationId && x.CompanyId == ctx.CompanyId)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Không có hàng giữ này.");
            if (r.Status != ReservationStatus.Active) throw new DomainException(ErrorCodes.InvalidStateTransition, "Hàng giữ đã được đóng.");
            Close(r, ReservationStatus.Released, ctx.UserId, clock.UtcNow, reason.Trim());
            await audit.AppendAsync(ctx.CompanyId, ctx, "reservation.release", "order", r.OrderId.ToString(), new { r.OrderLineNo, r.WarehouseId, remaining = r.Quantity - r.ConsumedQuantity, reason });
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Giải phóng toàn bộ hàng đang giữ của đơn (đóng hoặc hủy đơn), trong transaction của lệnh gọi.</summary>
    internal static async Task ReleaseOrderAsync(HakDbContext db, Guid companyId, Guid orderId, Guid userId, DateTime now, string reason)
    {
        foreach (var r in await db.StockReservations.Where(x => x.CompanyId == companyId && x.OrderId == orderId && x.Status == ReservationStatus.Active).ToListAsync())
            Close(r, ReservationStatus.Released, userId, now, reason);
    }

    /// <summary>Phiếu xuất theo đơn ghi sổ: trừ dần phần đang giữ của dòng đơn (cũ nhất trước); hết thì đóng là đã dùng.</summary>
    internal static async Task ConsumeAsync(HakDbContext db, Guid companyId, Guid orderId, int orderLineNo, decimal quantity, Guid userId, DateTime now)
    {
        var active = (await db.StockReservations.Where(r => r.CompanyId == companyId && r.OrderId == orderId && r.OrderLineNo == orderLineNo && r.Status == ReservationStatus.Active).ToListAsync())
            .OrderBy(r => r.CreatedAt).ToList();
        foreach (var r in active)
        {
            if (quantity <= 0) break;
            var take = Math.Min(quantity, r.Quantity - r.ConsumedQuantity);
            r.ConsumedQuantity += take;
            quantity -= take;
            if (r.ConsumedQuantity >= r.Quantity) Close(r, ReservationStatus.Consumed, userId, now, "Đã xuất kho theo đơn");
        }
    }

    private static void Close(StockReservation r, ReservationStatus status, Guid userId, DateTime now, string reason)
    {
        r.Status = status; r.ClosedAt = now; r.ClosedBy = userId; r.CloseReason = reason;
    }

    public async Task<IReadOnlyList<ReservationView>> ListAsync(SessionContext ctx, Guid? orderId = null, bool activeOnly = true)
    {
        ctx.Require(Permissions.VoucherView);
        var q = db.StockReservations.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId);
        if (orderId is not null) q = q.Where(r => r.OrderId == orderId);
        if (activeOnly) q = q.Where(r => r.Status == ReservationStatus.Active);
        var rows = await q.OrderByDescending(r => r.CreatedAt).Take(500).ToListAsync();
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id, i => i.Code);
        var whs = await db.Warehouses.AsNoTracking().Where(w => w.CompanyId == ctx.CompanyId).ToDictionaryAsync(w => w.Id, w => w.Code);
        var orders = await db.TradeOrders.AsNoTracking().Where(o => o.CompanyId == ctx.CompanyId).Select(o => new { o.Id, o.Number }).ToDictionaryAsync(o => o.Id, o => o.Number);
        return rows.Select(r => new ReservationView(r.Id, r.WarehouseId, whs.GetValueOrDefault(r.WarehouseId, ""), r.ItemId, items.GetValueOrDefault(r.ItemId, ""), r.OrderId,
            orders.GetValueOrDefault(r.OrderId, ""), r.OrderLineNo, r.Quantity, r.ConsumedQuantity, r.Remaining, r.Status.ToString(), r.CreatedAt, r.ClosedAt, r.CloseReason)).ToList();
    }

    /// <summary>Tồn vật lý / đang giữ / khả dụng / đang về của một vật tư (toàn công ty hoặc một kho).</summary>
    public async Task<StockAvailability> AvailabilityAsync(SessionContext ctx, Guid itemId, Guid? warehouseId = null)
    {
        ctx.Require(Permissions.VoucherView);
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Vật tư không tồn tại.");
        var moves = await db.StockMovements.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && m.ItemId == itemId && (warehouseId == null || m.WarehouseId == warehouseId)).Select(m => m.Quantity).ToListAsync();
        var held = await db.StockReservations.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId && r.ItemId == itemId && r.Status == ReservationStatus.Active && (warehouseId == null || r.WarehouseId == warehouseId)).ToListAsync();
        var onHand = moves.Sum();
        var reserved = held.Sum(r => r.Remaining);

        var purchase = await db.TradeOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == ctx.CompanyId && o.Kind == InvoiceKind.Purchase && o.Status == OrderStatus.Confirmed && o.Lines.Any(l => l.ItemId == itemId)).ToListAsync();
        var ids = purchase.Select(o => o.Id).ToList();
        var received = await (from l in db.StockDocumentLines join d in db.StockDocuments on l.DocumentId equals d.Id
                              where d.CompanyId == ctx.CompanyId && d.Type == StockDocumentType.Receipt && d.Status == StockDocumentStatus.Posted && l.OrderId != null && ids.Contains(l.OrderId.Value) && l.ItemId == itemId
                              select new { Order = l.OrderId!.Value, Line = l.OrderLineNo!.Value, l.Quantity }).ToListAsync();
        var incoming = purchase.SelectMany(o => o.Lines.Where(l => l.ItemId == itemId).Select(l => Math.Max(0, l.Quantity - received.Where(r => r.Order == o.Id && r.Line == l.LineNo).Sum(r => r.Quantity)))).Sum();
        return new StockAvailability(itemId, item.Code, warehouseId, onHand, reserved, onHand - reserved, incoming);
    }
}
