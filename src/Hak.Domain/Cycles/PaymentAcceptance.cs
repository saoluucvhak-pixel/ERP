namespace Hak.Domain.Cycles;

/// <summary>
/// Bàn giao Cung ứng → Thanh toán (tài liệu thiết kế docs/10, mục V.2.1.2.4 và V.2.2.3 của tệp phân tích ERP).
/// Một hóa đơn mua đã ghi sổ, đã đối chiếu, được "chấp nhận thanh toán": khoản còn phải trả vào hàng chờ theo ngày đáo hạn của
/// chu trình thanh toán, nơi tự tạo lệnh thanh toán Nháp. Chấp nhận KHÔNG phát sinh bút toán (chỉ khi chi tiền mới ghi Nhật ký).
/// "Đã thanh toán" không lưu ở đây: suy ra từ số còn lại của dòng công nợ, nên không bao giờ lệch sổ.
/// </summary>
public enum PaymentAcceptanceStatus
{
    /// <summary>Đã chấp nhận, chu trình thanh toán chưa tạo lệnh.</summary>
    Pending = 1,
    /// <summary>Đã có lệnh thanh toán (trạng thái lệnh quyết định bước tiếp theo).</summary>
    OrderCreated = 2,
    /// <summary>Chu trình thanh toán từ chối kèm lý do, trả về Cung ứng.</summary>
    Rejected = 3,
    /// <summary>Cung ứng rút lại chấp nhận.</summary>
    Cancelled = 9,
}

public sealed class PaymentAcceptance
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid PartnerId { get; set; }
    /// <summary>Dòng công nợ phải trả của hóa đơn (tài khoản AP_TRADE, bên Có) mà lệnh thanh toán sẽ tất toán.</summary>
    public Guid SettleJournalLineId { get; set; }
    /// <summary>Số còn phải trả tại thời điểm chấp nhận (VND).</summary>
    public decimal Amount { get; set; }
    public DateOnly? DueDate { get; set; }
    public PaymentAcceptanceStatus Status { get; set; } = PaymentAcceptanceStatus.Pending;
    public string? Note { get; set; }
    public Guid? PaymentOrderId { get; set; }
    public Guid AcceptedBy { get; set; }
    public DateTime AcceptedAt { get; set; }
    public Guid? ClosedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? CloseReason { get; set; }
    public long RowVersion { get; set; }
}
