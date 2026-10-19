using Hak.Domain.Common;

namespace Hak.Domain.Production;

/// <summary>
/// Định mức nguyên vật liệu của một thành phẩm (Bill of Materials; tài liệu thiết kế chu trình sản xuất, bảng product_bom).
/// Mỗi thành phẩm có một định mức hiện hành; sửa định mức thay các dòng và ghi nhật ký kiểm toán, lệnh sản xuất cũ giữ số liệu đã chốt.
/// </summary>
public sealed class Bom
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ItemId { get; set; }
    public string? Note { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
    public long RowVersion { get; set; }
    public List<BomLine> Lines { get; set; } = [];
}

public sealed class BomLine
{
    public Guid Id { get; set; }
    public Guid BomId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RawItemId { get; set; }
    /// <summary>Số lượng nguyên vật liệu cho 1 đơn vị thành phẩm.</summary>
    public decimal QuantityPerUnit { get; set; }
    /// <summary>Tỷ lệ hao hụt cho phép (%).</summary>
    public decimal ScrapRate { get; set; }
}

/// <summary>Tổ xưởng / trung tâm sản xuất với đơn giá nhân công và máy + sản xuất chung theo giờ (bảng work_centers).</summary>
public sealed class WorkCenter
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal LaborRatePerHour { get; set; }
    public decimal OverheadRatePerHour { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
}

public enum ProductionOrderStatus { Draft = 1, Released = 2, InProgress = 3, Completed = 4, Closed = 5, Cancelled = 9 }

/// <summary>
/// Lệnh sản xuất (bảng production_orders): chứng từ điều phối. Khâu lập, ban hành, xuất nguyên vật liệu (phiếu kho), báo công không tự ghi Nhật ký;
/// hoàn thành lệnh ghi Nhật ký phân bổ nhân công và sản xuất chung (Nợ TK dở dang) và lập phiếu nhập thành phẩm theo giá thành thực tế của lệnh.
/// </summary>
public sealed class ProductionOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>LSX{năm}-{số}.</summary>
    public string Number { get; set; } = "";
    public Guid ItemId { get; set; }
    /// <summary>Đối tượng tính giá thành: cho biết TK dở dang chi phí của lệnh dồn vào.</summary>
    public Guid CostObjectId { get; set; }
    /// <summary>Kho xuất nguyên vật liệu và nhập thành phẩm.</summary>
    public Guid WarehouseId { get; set; }
    public decimal PlannedQuantity { get; set; }
    public DateOnly PlannedStart { get; set; }
    public ProductionOrderStatus Status { get; set; } = ProductionOrderStatus.Draft;
    public string? Note { get; set; }
    public decimal? ActualQuantity { get; set; }
    public DateOnly? CompletedDate { get; set; }
    /// <summary>Giá thành đã chốt khi hoàn thành lệnh.</summary>
    public decimal MaterialCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal OverheadCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal UnitCost { get; set; }
    public Guid? ReceiptDocumentId { get; set; }
    public Guid? AllocationVoucherId { get; set; }
    public string? CancelReason { get; set; }
    public Guid? ReleasedBy { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }

    public void Release(Guid userId, DateTime at, bool requireDistinct)
    {
        if (Status != ProductionOrderStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Lệnh {Number} không còn ở trạng thái lập.");
        if (requireDistinct && userId == CreatedBy) throw new DomainException(ErrorCodes.SodViolation, "Người ban hành lệnh sản xuất không được trùng người lập.");
        Status = ProductionOrderStatus.Released; ReleasedBy = userId; ReleasedAt = at;
    }
}

/// <summary>Phiếu xuất kho nguyên vật liệu gắn với lệnh sản xuất (bảng production_material_issues; số lượng, giá vốn lấy từ phiếu kho).</summary>
public sealed class ProductionOrderIssue
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public Guid StockDocumentId { get; set; }
}

/// <summary>Báo công: giờ nhân công và giờ máy thực tế tại một tổ xưởng (bảng production_labor_confirmations); đơn giá chốt tại thời điểm báo.</summary>
public sealed class ProductionConfirmation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public Guid WorkCenterId { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal LaborHours { get; set; }
    public decimal MachineHours { get; set; }
    public decimal LaborRate { get; set; }
    public decimal OverheadRate { get; set; }
    public decimal LaborCost { get; set; }
    public decimal OverheadCost { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
}
