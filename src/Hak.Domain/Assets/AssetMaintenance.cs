using Hak.Domain.Common;

namespace Hak.Domain.Assets;

public enum MaintenancePriority { Low = 1, Medium = 2, High = 3, Urgent = 4 }

public enum MaintenanceRequestStatus { New = 1, Surveying = 2, Proposed = 3, Approved = 4, InProgress = 5, Done = 6, Rejected = 7, Cancelled = 9 }

/// <summary>Phiếu yêu cầu sửa chữa / báo sự cố (YCSC) do người vận hành lập. Không ghi Nhật ký.</summary>
public sealed class MaintenanceRequest
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>YCSC{năm}-{số}.</summary>
    public string Number { get; set; } = "";
    public Guid AssetId { get; set; }
    public MaintenancePriority Priority { get; set; } = MaintenancePriority.Medium;
    public string Description { get; set; } = "";
    public DateTime ReportedAt { get; set; }
    public MaintenanceRequestStatus Status { get; set; } = MaintenanceRequestStatus.New;
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
}

public enum RepairType { Emergency = 1, Preventive = 2 }

/// <summary>Draft → PendingApproval → Approved → InProgress → Testing → Completed → Closed; hoặc Rejected / Cancelled.</summary>
public enum RepairStatus { Draft = 1, PendingApproval = 2, Approved = 3, InProgress = 4, Testing = 5, Completed = 6, Closed = 7, Rejected = 8, Cancelled = 9 }

/// <summary>
/// Phiếu thi công sửa chữa / bảo trì (work order): khảo sát → phương án và dự toán → duyệt (người duyệt khác người lập) → thi công, xuất vật tư → nghiệm thu → đóng.
/// Không ghi Nhật ký: vật tư xuất qua phiếu xuất kho (ghi sổ ở màn Kho), chi phí khác ghi sổ bằng hóa đơn / phiếu chi. Không đổi nguyên giá và lịch khấu hao.
/// </summary>
public sealed class AssetRepair
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>SCTS{năm}-{số}.</summary>
    public string Number { get; set; } = "";
    public Guid AssetId { get; set; }
    public Guid? RequestId { get; set; }
    public Guid? ScheduleId { get; set; }
    public RepairType Type { get; set; }
    public RepairStatus Status { get; set; } = RepairStatus.Draft;
    public DateOnly ReportedDate { get; set; }
    public string Description { get; set; } = "";
    public string? Vendor { get; set; }
    public string? AssignedTech { get; set; }
    public string? RootCause { get; set; }
    public string? SolutionPlan { get; set; }
    public decimal EstimatedLaborCost { get; set; }
    public decimal EstimatedMaterialCost { get; set; }
    public decimal? ActualLaborCost { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? ClosedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public long RowVersion { get; set; }
    public List<AssetRepairMaterial> Materials { get; set; } = [];

    public decimal EstimatedTotal => EstimatedLaborCost + EstimatedMaterialCost;
    public decimal ActualMaterialCost => Materials.Where(m => m.Status != RepairMaterialStatus.Cancelled).Sum(m => m.QtyIssued * m.UnitPrice);
    public decimal ActualTotal => (ActualLaborCost ?? 0) + ActualMaterialCost;

    public void Move(RepairStatus from, RepairStatus to)
    {
        if (Status != from) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phiếu {Number} đang ở trạng thái {Status}, không chuyển sang {to} được.");
        Status = to; RowVersion++;
    }
}

public enum RepairMaterialStatus { Pending = 1, Issued = 2, Cancelled = 9 }

public sealed class AssetRepairMaterial
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RepairId { get; set; }
    public Guid ItemId { get; set; }
    public decimal QtyRequested { get; set; }
    public decimal QtyIssued { get; set; }
    public decimal UnitPrice { get; set; }
    public RepairMaterialStatus Status { get; set; } = RepairMaterialStatus.Pending;
    public Guid? StockDocumentId { get; set; }
}

/// <summary>Lịch bảo trì phòng ngừa định kỳ. Đến hạn thì lập phiếu bảo trì; đóng phiếu thì cập nhật ngày thực hiện và hạn kế tiếp.</summary>
public sealed class PreventiveSchedule
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AssetId { get; set; }
    public string TaskName { get; set; } = "";
    public int FrequencyDays { get; set; }
    public DateOnly? LastPerformedAt { get; set; }
    public DateOnly NextDueDate { get; set; }
    public bool IsActive { get; set; } = true;
    public long RowVersion { get; set; }
}
