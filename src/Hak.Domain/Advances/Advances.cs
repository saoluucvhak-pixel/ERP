namespace Hak.Domain.Advances;

public enum AdvanceStatus
{
    /// <summary>Đề nghị tạm ứng đã lập, chờ duyệt.</summary>
    Requested = 1,
    /// <summary>Đã duyệt (người duyệt khác người đề nghị), chờ lập chứng từ chi.</summary>
    Approved = 2,
    /// <summary>Chứng từ chi đã ghi sổ: người nhận đang giữ tiền tạm ứng.</summary>
    Paid = 3,
    /// <summary>Chứng từ quyết toán (hoàn ứng) đã ghi sổ.</summary>
    Settled = 4,
    Rejected = 5,
    Cancelled = 6,
}

/// <summary>
/// Đề nghị tạm ứng của nhân viên. Không tự ghi Nhật ký: chứng từ chi và chứng từ quyết toán là chứng từ nháp đi qua luồng duyệt – ghi sổ thông thường;
/// trạng thái Đã chi / Đã quyết toán lấy theo trạng thái của hai chứng từ đó.
/// </summary>
public sealed class AdvanceRequest
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = "";
    public Guid EmployeeId { get; set; }
    public string Purpose { get; set; } = "";
    public decimal Amount { get; set; }
    public DateOnly RequestDate { get; set; }
    /// <summary>Hạn quyết toán: quá hạn mà chưa quyết toán thì báo trên báo cáo tuổi nợ tạm ứng.</summary>
    public DateOnly DueDate { get; set; }
    public AdvanceStatus Status { get; set; } = AdvanceStatus.Requested;
    public string? DecisionNote { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public Guid? PaymentVoucherId { get; set; }
    public Guid? SettlementVoucherId { get; set; }
    /// <summary>Tổng chi thực tế khi quyết toán (để báo cáo hoàn ứng / chi bổ sung).</summary>
    public decimal? SpentAmount { get; set; }
    public ICollection<AdvanceSettlementLine> Lines { get; set; } = [];
}

public sealed class AdvanceSettlementLine
{
    public Guid Id { get; set; }
    public Guid AdvanceId { get; set; }
    public int LineNo { get; set; }
    public Guid ExpenseAccountId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    /// <summary>Số và ngày chứng từ gốc (hóa đơn, biên lai) kèm theo bảng kê quyết toán.</summary>
    public string? DocumentNo { get; set; }
    public DateOnly? DocumentDate { get; set; }
}

public enum CashCountStatus { Draft = 1, Confirmed = 2, Cancelled = 3 }

/// <summary>
/// Biên bản kiểm kê quỹ tiền mặt (mẫu 08a-TT/BB): đếm theo mệnh giá, đối chiếu số dư sổ quỹ tại ngày kiểm kê. Số dư sổ chốt lúc xác nhận.
/// Chênh lệch xử lý bằng chứng từ nháp riêng (thiếu: tài khoản chờ xử lý tài sản thiếu; thừa: tài khoản chờ xử lý tài sản thừa).
/// </summary>
public sealed class CashCount
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = "";
    public Guid CashAccountId { get; set; }
    public DateOnly CountDate { get; set; }
    public CashCountStatus Status { get; set; } = CashCountStatus.Draft;
    public decimal CountedTotal { get; set; }
    public decimal? BookBalance { get; set; }
    public decimal? Difference { get; set; }
    public string? Note { get; set; }
    public string? Participants { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public Guid? AdjustmentVoucherId { get; set; }
    public string? CancelReason { get; set; }
    public ICollection<CashCountLine> Lines { get; set; } = [];
}

public sealed class CashCountLine
{
    public Guid Id { get; set; }
    public Guid CashCountId { get; set; }
    public decimal Denomination { get; set; }
    public int Quantity { get; set; }
}
