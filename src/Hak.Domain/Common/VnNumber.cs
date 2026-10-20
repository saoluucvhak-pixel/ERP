using System.Globalization;

namespace Hak.Domain.Common;

/// <summary>
/// Định dạng số theo cách người Việt đọc (1.234.567,5): dấu chấm ngăn nhóm nghìn, dấu phẩy thập phân, bỏ số 0 thừa. Máy chủ chạy InvariantGlobalization nên không dùng được culture vi-VN;
/// dùng cho thông báo hiển thị cho người dùng, khớp với formatMoney ở giao diện web.
/// </summary>
public static class VnNumber
{
    public static string Vn(this decimal value, int maxDecimals = 4)
    {
        var pattern = maxDecimals <= 0 ? "#,##0" : "#,##0." + new string('#', maxDecimals);
        var s = Math.Round(value, Math.Max(0, maxDecimals), MidpointRounding.AwayFromZero).ToString(pattern, CultureInfo.InvariantCulture);
        return s.Replace(',', '\u0001').Replace('.', ',').Replace('\u0001', '.');
    }

    /// <summary>Ngày dạng 19/10/2026 (DateOnly.ToString() với culture bất biến cho ra 10/19/2026, dễ đọc nhầm).</summary>
    public static string Vn(this DateOnly value) => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string Vn(this DateOnly? value) => value is { } v ? v.Vn() : "";

    public static string Vn(this decimal? value, int maxDecimals = 4) => value is { } v ? v.Vn(maxDecimals) : "";
}
