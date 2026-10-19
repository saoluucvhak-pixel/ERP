namespace Hak.Domain.Cycles;

/// <summary>
/// Chu trình doanh thu theo tệp phân tích ERP (mục V.1): Đơn đặt hàng → <b>Lệnh bán hàng</b> (không ghi Nhật ký) → chuẩn bị hàng →
/// <b>Phiếu giao hàng</b> (kèm vận đơn; sinh phiếu xuất kho, ghi Nhật ký khi ghi sổ phiếu xuất) → Hóa đơn bán hàng → Thu tiền.
/// Lệnh bán hàng làm căn cứ cho kho (như phiếu xuất), bộ phận chuẩn bị hàng, bộ phận lập hóa đơn và thông báo chấp nhận cho khách (4 liên).
/// </summary>
public enum DispatchOrderStatus
{
    /// <summary>Đang lập, chưa phát hành các liên.</summary>
    Draft = 1,
    /// <summary>Đã phát hành: kho được giao hàng theo lệnh.</summary>
    Issued = 2,
    Cancelled = 9,
}

public sealed class SalesDispatchOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>LBH{năm TC}-{số}.</summary>
    public string Number { get; set; } = "";
    public Guid OrderId { get; set; }
    public Guid PartnerId { get; set; }
    /// <summary>Kho xuất hàng.</summary>
    public Guid WarehouseId { get; set; }
    public DateOnly DispatchDate { get; set; }
    public string? ShipTo { get; set; }
    public string? Note { get; set; }
    public DispatchOrderStatus Status { get; set; } = DispatchOrderStatus.Draft;
    public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? IssuedBy { get; set; }
    public DateTime? IssuedAt { get; set; }
    public long RowVersion { get; set; }
    public List<SalesDispatchLine> Lines { get; set; } = [];
}

public sealed class SalesDispatchLine
{
    public Guid Id { get; set; }
    public Guid DispatchOrderId { get; set; }
    public Guid CompanyId { get; set; }
    public int LineNo { get; set; }
    /// <summary>Dòng của đơn đặt hàng mà lệnh này thực hiện.</summary>
    public int OrderLineNo { get; set; }
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>Phiếu giao hàng (gửi hàng): nơi nhận, đơn vị vận tải, nhân viên giao hàng, vận đơn; liên kết phiếu xuất kho.</summary>
public sealed class SalesDelivery
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>PGH{năm TC}-{số}.</summary>
    public string Number { get; set; } = "";
    public Guid DispatchOrderId { get; set; }
    /// <summary>Phiếu xuất kho sinh cùng phiếu giao hàng (Nháp cho đến khi ghi sổ; ghi sổ phiếu xuất là lúc ghi Nhật ký).</summary>
    public Guid StockDocumentId { get; set; }
    public DateOnly DeliveryDate { get; set; }
    public string? ShipTo { get; set; }
    public string? Carrier { get; set; }
    public string? DeliveryStaff { get; set; }
    public string? WaybillNo { get; set; }
    public string? Note { get; set; }
    public bool IsCancelled { get; set; }
    public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
    public List<SalesDeliveryLine> Lines { get; set; } = [];
}

public sealed class SalesDeliveryLine
{
    public Guid Id { get; set; }
    public Guid DeliveryId { get; set; }
    public Guid CompanyId { get; set; }
    public int LineNo { get; set; }
    public int DispatchLineNo { get; set; }
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Số dòng tương ứng trên phiếu xuất kho.</summary>
    public int StockLineNo { get; set; }
}
