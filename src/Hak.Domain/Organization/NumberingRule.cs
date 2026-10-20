namespace Hak.Domain.Organization;

/// <summary>
/// Tùy chỉnh bộ mã của một công ty cho một loại chứng từ: đổi tiền tố hiển thị và/hoặc độ rộng số thứ tự. Chỉ áp dụng cho số cấp SAU khi đổi; số đã cấp giữ nguyên,
/// bộ đếm (khóa nội bộ của loại) không đổi nên số thứ tự vẫn liên tục. Quy tắc và danh mục loại chứng từ: docs/11-QUY-TAC-MA.md.
/// </summary>
public sealed class NumberingRule
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>Khóa nội bộ của loại chứng từ (không đổi), ví dụ INV-HDB.</summary>
    public string Key { get; set; } = "";
    public string? Prefix { get; set; }
    public int? Width { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
}
