using System.Net;

namespace Hak.Api;

/// <summary>
/// SEC2-03: chạy trong mạng LAN bằng HTTP thuần thì mật khẩu và token đi dạng rõ. Hàm này nhận ra địa chỉ nghe HTTP không phải loopback;
/// Program ghi cảnh báo khi khởi động và, nếu bật <c>Hak:RequireTlsForLan=true</c> ở Production, từ chối khởi động. Chưa thay thế được TLS thật (chứng chỉ + Kestrel HTTPS — việc triển khai).
/// </summary>
public static class LanSecurity
{
    public static IReadOnlyList<string> InsecureLanUrls(IEnumerable<string> urls)
        => urls.Select(u => u.Trim()).Where(u => u.Length > 0).Where(IsInsecureLan).ToList();

    private static bool IsInsecureLan(string url)
    {
        // Mẫu Kestrel "http://*:5080", "http://+:5080" nghe mọi giao diện.
        var normalized = url.Replace("://*", "://0.0.0.0", StringComparison.Ordinal).Replace("://+", "://0.0.0.0", StringComparison.Ordinal);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)) return false;
        var host = uri.Host.Trim('[', ']');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return false;
        return !(IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));
    }
}
