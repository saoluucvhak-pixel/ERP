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
