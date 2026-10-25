namespace Hak.Domain.Tax;

/// <summary>Trạng thái pháp lý của một hóa đơn điện tử do công ty PHÁT HÀNH qua nhà cung cấp (khác <see cref="EInvoiceStatus"/> của dữ liệu nhập vào).</summary>
public enum EInvoiceDocStatus
{
    /// <summary>Đã vào hàng đợi, chưa có kết quả cuối từ nhà cung cấp / cơ quan thuế (mạng lỗi cũng ở trạng thái này).</summary>
    Issuing = 1,
    /// <summary>Đã ký và được cơ quan thuế chấp nhận (số, ký hiệu, mã cơ quan thuế đã có).</summary>
    Issued = 2,
    /// <summary>Bị nhà cung cấp / cơ quan thuế từ chối: chưa có số hóa đơn, được phát hành lại sau khi sửa.</summary>
    Rejected = 3,
    /// <summary>Đã bị hóa đơn thay thế.</summary>
    Replaced = 4,
    /// <summary>Đã hủy qua thông báo sai sót được cơ quan thuế tiếp nhận.</summary>
    Cancelled = 5,
}

public enum EInvoiceRelation { Original = 1, Replacement = 2, Adjustment = 3 }

/// <summary>Bản chất điều chỉnh: tăng / giảm tiền, hoặc chỉ điều chỉnh thông tin (không bút toán).</summary>
public enum EInvoiceAdjustNature { None = 0, Increase = 1, Decrease = 2, Information = 3 }

/// <summary>
/// Một lần phát hành hóa đơn điện tử. Gắn với hóa đơn trong ERP (<see cref="InvoiceId"/>); riêng "điều chỉnh thông tin" không có hóa đơn ERP vì không phát sinh bút toán.
/// Số, ký hiệu, ngày ký, mã cơ quan thuế và XML do nhà cung cấp trả về — sau khi Issued là chỉ-thêm, không sửa.
/// </summary>
public sealed class EInvoiceDocument
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? InvoiceId { get; set; }
    public string Provider { get; set; } = "";
    public string? ProviderRef { get; set; }
    /// <summary>Ký hiệu mẫu số hóa đơn (ví dụ "1"), cấu hình theo công ty [CẦN XÁC MINH theo thông báo phát hành].</summary>
    public string TemplateCode { get; set; } = "";
    public string? Series { get; set; }
    public string? Number { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? TaxAuthorityCode { get; set; }
    public EInvoiceDocStatus Status { get; set; } = EInvoiceDocStatus.Issuing;
    public EInvoiceRelation Relation { get; set; } = EInvoiceRelation.Original;
    public Guid? RelatedDocumentId { get; set; }
    public EInvoiceAdjustNature Nature { get; set; }
    /// <summary>Lý do thay thế / điều chỉnh (bắt buộc với hóa đơn không phải bản gốc).</summary>
    public string? Reason { get; set; }
    public string? XmlContent { get; set; }
    public string? XmlSha256 { get; set; }
    /// <summary>Đã phát hành nhưng ghi sổ hóa đơn ERP thất bại (kỳ khóa, thiếu cấu hình…): nội dung lỗi để xử lý rồi ghi sổ lại.</summary>
    public string? PostError { get; set; }
    public string? LastError { get; set; }
    /// <summary>Số lần nhà cung cấp từ chối: khóa chống trùng đổi theo lần để lần phát hành lại không nhận lại kết quả từ chối cũ.</summary>
    public int Attempt { get; set; } = 1;
    public Guid RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; }
    public long RowVersion { get; set; }
}

public enum EInvoiceOutboxState { Pending = 1, Confirmed = 2, Failed = 3 }

/// <summary>Hàng đợi gửi (outbox): ghi cùng transaction với trạng thái "Issuing" nên không bao giờ mất lệnh phát hành, và gửi lại an toàn nhờ khóa chống trùng.</summary>
public sealed class EInvoiceOutboxItem
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public string Operation { get; set; } = "Issue";
    public string IdempotencyKey { get; set; } = "";
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public EInvoiceOutboxState State { get; set; } = EInvoiceOutboxState.Pending;
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Nhật ký chỉ-thêm các thông điệp gửi / nhận với nhà cung cấp (nội dung băm, không lưu bí mật).</summary>
public sealed class EInvoiceMessage
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? NoticeId { get; set; }
    /// <summary>"OUT" gửi đi, "IN" nhận về.</summary>
    public string Direction { get; set; } = "OUT";
    public string Kind { get; set; } = "";
    public string? PayloadSha256 { get; set; }
    public string? Code { get; set; }
    public string? Detail { get; set; }
    public DateTime At { get; set; }
}

/// <summary>Loại xử lý ghi trong thông báo sai sót (Mẫu 04/SS-HĐĐT) [CẦN XÁC MINH các giá trị theo mẫu hiện hành].</summary>
public enum ErrorNoticeKind { Explain = 1, Cancel = 2, Adjust = 3, Replace = 4 }

public enum ErrorNoticeStatus { Draft = 1, Sent = 2, Accepted = 3, Rejected = 4 }

/// <summary>
/// Thông báo sai sót (Mẫu 04/SS-HĐĐT): danh sách hóa đơn có sai sót kèm lý do. Không phát sinh bút toán; nếu được tiếp nhận với loại Hủy
/// thì hóa đơn chuyển Cancelled và đối soát sẽ yêu cầu đảo bút toán còn hiệu lực.
/// </summary>
public sealed class EInvoiceErrorNotice
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = "";
    public ErrorNoticeKind Kind { get; set; }
    public string Reason { get; set; } = "";
    public ErrorNoticeStatus Status { get; set; } = ErrorNoticeStatus.Draft;
    public string? ProviderRef { get; set; }
    public string? Response { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public long RowVersion { get; set; }
    public List<EInvoiceErrorNoticeLine> Lines { get; set; } = [];
}

public sealed class EInvoiceErrorNoticeLine
{
    public Guid Id { get; set; }
    public Guid NoticeId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public string ErrorDescription { get; set; } = "";
}
