namespace Hak.Domain.Tax;

public enum RiskNoticeStatus { Received = 1, Responded = 2, Closed = 3 }

/// <summary>
/// Thông báo của cơ quan thuế về hóa đơn điện tử có rủi ro / sai sót (Mẫu 01/TB-RSĐT, Phụ lục IV Thông tư 91/2026 [CẦN XÁC MINH mẫu và thời hạn phản hồi]). Đây là văn bản NHẬN VÀO:
/// HAK chỉ ghi nhận, theo dõi hạn và việc phản hồi (giải trình hoặc thông báo sai sót 04/SS kèm theo), KHÔNG tự đổi hóa đơn hay sổ. Hạn phản hồi do người nhập theo nội dung thông báo.
/// </summary>
public sealed class EInvoiceRiskNotice
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = "";
    /// <summary>Số / ký hiệu thông báo ghi trên văn bản của cơ quan thuế (duy nhất trong công ty).</summary>
    public string AuthorityNoticeNo { get; set; } = "";
    public DateOnly NoticeDate { get; set; }
    public DateOnly ReceivedDate { get; set; }
    public DateOnly? ResponseDeadline { get; set; }
    public string Reason { get; set; } = "";
    public RiskNoticeStatus Status { get; set; } = RiskNoticeStatus.Received;
    public string? ResponseNote { get; set; }
    /// <summary>Thông báo sai sót 04/SS (đã lập trong HAK) dùng để phản hồi, nếu có.</summary>
    public Guid? ResponseNoticeId { get; set; }
    public DateTime? RespondedAt { get; set; }
    public Guid? RespondedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
    public List<EInvoiceRiskNoticeLine> Lines { get; set; } = [];
}

/// <summary>Hóa đơn bị nêu trong thông báo; <see cref="DocumentId"/> được tự ghép nếu hóa đơn phát hành từ HAK có cùng ký hiệu + số.</summary>
public sealed class EInvoiceRiskNoticeLine
{
    public Guid Id { get; set; }
    public Guid NoticeId { get; set; }
    public Guid CompanyId { get; set; }
    public string Series { get; set; } = "";
    public string InvoiceNo { get; set; } = "";
    public DateOnly? IssueDate { get; set; }
    public string Issue { get; set; } = "";
    public Guid? DocumentId { get; set; }
}
