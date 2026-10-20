namespace Hak.Domain.Attachments;

/// <summary>
/// Tệp đính kèm (ảnh sự cố, ảnh nghiệm thu…). Chỉ thêm, không sửa, không xóa: ảnh là bằng chứng. Nội dung nằm trong thư mục đính kèm của máy chủ,
/// tên tệp lưu là mã nội bộ (không dùng tên người dùng đặt); Sha256 để phát hiện tệp bị thay ngoài phần mềm.
/// </summary>
public sealed class FileAttachment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>maintenance_request | asset_repair.</summary>
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    /// <summary>Tên tệp gốc đã làm sạch, chỉ để hiển thị.</summary>
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    /// <summary>Đường dẫn tương đối trong thư mục đính kèm.</summary>
    public string StoragePath { get; set; } = "";
    public string? Caption { get; set; }
    public DateTime UploadedAt { get; set; }
    public Guid UploadedBy { get; set; }
}
