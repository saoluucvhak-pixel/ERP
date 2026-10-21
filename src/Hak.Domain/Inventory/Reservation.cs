namespace Hak.Domain.Inventory;

public enum ReservationStatus { Active = 1, Released = 2, Consumed = 3 }

/// <summary>
/// Giữ hàng cho một dòng đơn bán tại một kho (Đợt 2, tệp đề xuất chu trình mục 4). Giữ hàng KHÔNG sinh bút toán và KHÔNG trừ tồn thực tế:
/// nó chỉ làm giảm số lượng khả dụng (tồn vật lý − đang giữ) cho các đơn khác. Phần đã xuất theo đơn được trừ dần vào <see cref="ConsumedQuantity"/>;
/// giải phóng (thủ công, đóng hoặc hủy đơn) đóng phần còn lại kèm lý do.
/// </summary>
public sealed class StockReservation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ItemId { get; set; }
    public Guid OrderId { get; set; }
    public int OrderLineNo { get; set; }
    /// <summary>Số lượng giữ ban đầu (MT / đơn vị chính của vật tư).</summary>
    public decimal Quantity { get; set; }
    /// <summary>Phần đã xuất kho theo đơn và được trừ khỏi phần giữ.</summary>
    public decimal ConsumedQuantity { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Active;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }
    public string? CloseReason { get; set; }

    /// <summary>Phần còn đang giữ (0 khi đã đóng).</summary>
    public decimal Remaining => Status == ReservationStatus.Active ? Quantity - ConsumedQuantity : 0;
}
