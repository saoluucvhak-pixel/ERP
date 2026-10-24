namespace Hak.Domain.Common;

/// <summary>
/// Kết quả đã trả cho một yêu cầu ghi có khóa Idempotency-Key (Đợt 4). Bản ghi được lưu CÙNG giao dịch với nghiệp vụ nên "đã ghi" và "đã có bản ghi" luôn đi cùng nhau:
/// mất phản hồi sau khi commit thì gửi lại cùng khóa sẽ nhận đúng phản hồi cũ, không ghi lần hai. Bảng này không thuộc sổ cái; được dọn sau vài ngày.
/// </summary>
public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }
    /// <summary>Mã băm của phiên đăng nhập và công ty; cùng khóa ở người khác không dùng chung.</summary>
    public string Scope { get; set; } = "";
    public string Key { get; set; } = "";
    /// <summary>Mã băm của phương thức, đường dẫn và nội dung yêu cầu: cùng khóa mà khác nội dung bị từ chối.</summary>
    public string RequestHash { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
    public string? ContentType { get; set; }
    public string ResponseBody { get; set; } = "";
    /// <summary>Các quyền (phân tách bằng dấu phẩy) mà yêu cầu gốc đã đòi khi chạy; kiểm lại với quyền HIỆN TẠI trước khi trả phản hồi đã lưu. Null = bản ghi cũ, không biết quyền.</summary>
    public string? RequiredPermissions { get; set; }
    public DateTime CreatedAt { get; set; }
}
