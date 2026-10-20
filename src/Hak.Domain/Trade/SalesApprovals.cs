namespace Hak.Domain.Trade;

public enum AdjustmentKind { Return = 1, Discount = 2 }

public enum ApprovalStatus
{
    Requested = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>
/// Yêu cầu trả hàng / giảm giá hàng bán cần phê duyệt (chu trình doanh thu): ghi rõ lý do, số lượng hoặc số tiền trên từng dòng hóa đơn bán gốc. Khi công ty bật chính sách
/// duyệt, chỉ nhận hàng trả lại / lập hóa đơn giảm giá trong phạm vi số lượng, số tiền đã được duyệt. Không ghi Nhật ký.
/// </summary>
public sealed class AdjustmentRequest
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = "";
    public Guid InvoiceId { get; set; }
    public AdjustmentKind Kind { get; set; }
    public string Reason { get; set; } = "";
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Requested;
    public string? DecisionNote { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public ICollection<AdjustmentRequestLine> Lines { get; set; } = [];
}

public sealed class AdjustmentRequestLine
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public int OriginalLineNo { get; set; }
    /// <summary>Số lượng trả lại (kind = Return).</summary>
    public decimal? Quantity { get; set; }
    /// <summary>Số tiền giảm trừ theo tiền hạch toán, chưa thuế (kind = Discount).</summary>
    public decimal? Amount { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Phiếu xóa nợ phải thu khó đòi có phê duyệt: chọn các khoản đang mở của một khách hàng, ghi căn cứ (quyết định, biên bản, hồ sơ), người có thẩm quyền duyệt rồi lập chứng từ
/// nháp bù trừ khoản phải thu (Có TK phải thu; Nợ dự phòng nếu đã trích, phần còn lại Nợ chi phí). Trạng thái đã lập chứng từ / đã ghi sổ lấy từ chứng từ.
/// </summary>
public sealed class DebtWriteOff
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = "";
    public Guid PartnerId { get; set; }
    public string Reason { get; set; } = "";
    /// <summary>Số quyết định, biên bản, hồ sơ đòi nợ… làm căn cứ xóa.</summary>
    public string Basis { get; set; } = "";
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Requested;
    public string? DecisionNote { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public Guid? VoucherId { get; set; }
    public ICollection<DebtWriteOffItem> Items { get; set; } = [];
}

public sealed class DebtWriteOffItem
{
    public Guid Id { get; set; }
    public Guid WriteOffId { get; set; }
    public Guid JournalLineId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Phần đã trích dự phòng nợ phải thu khó đòi, dùng để bù khi xóa (còn lại ghi chi phí).</summary>
    public decimal ProvisionUsed { get; set; }
}
