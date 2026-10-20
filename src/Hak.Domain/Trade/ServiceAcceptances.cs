namespace Hak.Domain.Trade;

/// <summary>
/// Biên bản nghiệm thu dịch vụ (khâu thay cho giao / nhận hàng của dịch vụ; như Service Entry Sheet của SAP, "số lượng đã giao" nhập tay của dịch vụ ở Odoo): ghi số lượng dịch vụ đã thực hiện
/// theo dòng đơn hàng; người khác xác nhận. Hóa đơn của dịch vụ chỉ lập trong phạm vi số lượng đã được xác nhận. Không ghi Nhật ký (Nhật ký phát sinh ở hóa đơn).
/// Trạng thái dùng chung <see cref="ApprovalStatus"/>: Requested = chờ xác nhận, Approved = đã xác nhận.
/// </summary>
public sealed class ServiceAcceptance
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = "";
    public InvoiceKind Kind { get; set; }
    public Guid PartnerId { get; set; }
    public Guid OrderId { get; set; }
    public DateOnly AcceptanceDate { get; set; }
    public string? Note { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Requested;
    public string? DecisionNote { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public ICollection<ServiceAcceptanceLine> Lines { get; set; } = [];
}

public sealed class ServiceAcceptanceLine
{
    public Guid Id { get; set; }
    public Guid AcceptanceId { get; set; }
    public int OrderLineNo { get; set; }
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Lịch nghiệm thu dịch vụ của hợp đồng (như billing plan / milestone của SAP, Odoo, ERPNext): mỗi đợt có tên, ngày dự kiến và số lượng dự kiến của một dòng dịch vụ trên hợp đồng.
/// Số lượng thực tế lấy từ biên bản nghiệm thu đã xác nhận của các đơn kế thừa hợp đồng; đối chiếu theo lũy kế, đợt chưa đạt mà quá ngày thì quá hạn. Không ghi Nhật ký.
/// </summary>
public sealed class ContractAcceptancePlan
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>Số dòng của hợp đồng (ContractLine.LineNo).</summary>
    public int ContractLineNo { get; set; }
    public int PlanNo { get; set; }
    public string Name { get; set; } = "";
    public DateOnly PlannedDate { get; set; }
    public decimal PlannedQty { get; set; }
}

/// <summary>
/// Mốc thực hiện dịch vụ (milestone của Odoo / SAP billing plan): mốc nằm trên một dòng dịch vụ của đơn hàng, chiếm một tỷ lệ số lượng dòng đó. Hoàn thành mốc lập biên bản nghiệm thu
/// với số lượng = tỷ lệ × số lượng đặt; mốc xem là xong khi biên bản liên kết còn hiệu lực (chờ xác nhận hoặc đã xác nhận).
/// </summary>
public sealed class ServiceMilestone
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public int OrderLineNo { get; set; }
    public int MilestoneNo { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Tỷ lệ % của số lượng dòng đơn (0 &lt; % ≤ 100); tổng các mốc của dòng không quá 100.</summary>
    public decimal Percentage { get; set; }
    public DateOnly DueDate { get; set; }
    public Guid? AcceptanceId { get; set; }
}

/// <summary>
/// Chấm giờ dịch vụ (timesheet của Odoo / ERPNext): người thực hiện, ngày, số giờ trên dòng dịch vụ của đơn hàng. "Chốt chấm giờ" cộng giờ chưa chốt đến một ngày, đổi ra số lượng
/// theo số giờ mỗi đơn vị tính và lập biên bản nghiệm thu; dòng chấm giờ bị khóa khi biên bản còn hiệu lực, mở lại nếu biên bản bị từ chối hoặc hủy. Chỉ bổ sung hoặc xóa khi chưa khóa.
/// </summary>
public sealed class ServiceTimeEntry
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public int OrderLineNo { get; set; }
    public DateOnly WorkDate { get; set; }
    public string Worker { get; set; } = "";
    public decimal Hours { get; set; }
    public string? Note { get; set; }
    public Guid? AcceptanceId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
