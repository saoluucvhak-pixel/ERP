using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hak.Integration.Tests;

/// <summary>Hợp đồng giữa giao diện và API: mọi đường dẫn tĩnh mà web/src gọi phải khớp một route có thật (lỗi lệch tiền tố nhóm từng làm hai màn hình gọi 404).</summary>
public partial class RouteContractTests
{
    [GeneratedRegex(@"\b(?:get|post|put|del|api|postRaw)\s*(?:<[^>(]*>)?\(\s*(?:'[A-Z]+'\s*,\s*)?([`'""])(/[^`'""]*)\1")]
    private static partial Regex CallPattern();

    private static string WebSrc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "web", "src"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "web", "src");
    }

    private static string[] Segments(string path)
    {
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool Matches(string[] route, string[] call)
    {
        if (route.Length != call.Length) return false;
        for (var i = 0; i < route.Length; i++)
        {
            if (route[i].StartsWith('{')) continue;          // tham số của route
            if (call[i].Contains("${")) continue;            // phần động của lời gọi (id, mã…)
            if (!string.Equals(route[i], call[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    [Fact]
    public async Task EveryStaticApiPathCalledByTheWebClient_ExistsAsARoute()
    {
        Directory.CreateDirectory(TestHost.Root);
        var dbPath = Path.Combine(TestHost.Root, $"routes-{Guid.NewGuid():N}.db");
        await using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Hak:Environment", "Test");
            b.UseSetting("Hak:DatabasePath", dbPath);
        });
        using var _ = f.CreateClient();
        var routes = f.Services.GetServices<EndpointDataSource>().SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Select(e => Segments(e.RoutePattern.RawText ?? "")).Where(s => s.Length > 1 && s[0] == "api")
            .Select(s => s[1..]).ToList();
        Assert.True(routes.Count > 300, $"Chỉ đọc được {routes.Count} route.");

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var checkedCalls = 0;
        foreach (var file in Directory.EnumerateFiles(WebSrc(), "*.ts*").Where(x => !x.EndsWith(".test.ts")))
            foreach (Match m in CallPattern().Matches(File.ReadAllText(file)))
            {
                var call = Segments(m.Groups[2].Value);
                if (call.Length == 0 || call[0].Contains("${")) continue;      // đường dẫn dựng hoàn toàn bằng biểu thức: không kiểm được tĩnh
                checkedCalls++;
                if (!routes.Any(r => Matches(r, call))) missing.Add($"{Path.GetFileName(file)}: /api{m.Groups[2].Value}");
            }
        Assert.True(checkedCalls > 150, $"Chỉ đối chiếu được {checkedCalls} lời gọi: biểu thức quét có thể đã hỏng.");
        Assert.True(missing.Count == 0, "Giao diện gọi đường dẫn không có route:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// Quét mọi route /api khi CHƯA đăng nhập: chỉ vài route được phép ẩn danh; mọi route khác phải bị chặn trước khi chạm nghiệp vụ
    /// (401, hoặc 400/415 khi yêu cầu rỗng không qua nổi bước đọc tham số). Route mới quên kiểm tra phiên sẽ làm test này hỏng.
    /// </summary>
    [Fact]
    public async Task EveryApiRoute_RefusesAnonymousCallers_ExceptTheFewPublicOnes()
    {
        Directory.CreateDirectory(TestHost.Root);
        var dbPath = Path.Combine(TestHost.Root, $"anon-{Guid.NewGuid():N}.db");
        await using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Hak:Environment", "Test");
            b.UseSetting("Hak:DatabasePath", dbPath);
            b.UseSetting("Hak:LoginRateLimitPerMinute", "0");
        });
        var client = f.CreateClient();
        string[] publicRoutes = ["/api/health", "/api/setup/status", "/api/setup/bootstrap", "/api/auth/login"];
        var routes = f.Services.GetServices<EndpointDataSource>().SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? "").StartsWith("/api/", StringComparison.Ordinal) || e.RoutePattern.RawText == "/api").ToList();
        Assert.True(routes.Count > 300);

        var leaks = new List<string>();
        var probed = 0;
        foreach (var e in routes)
        {
            var raw = e.RoutePattern.RawText!;
            if (publicRoutes.Contains(raw, StringComparer.OrdinalIgnoreCase)) continue;
            var path = Regex.Replace(raw, @"\{[^}:]+:guid\??\}", Guid.NewGuid().ToString());
            path = Regex.Replace(path, @"\{[^}]+\}", "1");
            foreach (var method in e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
            {
                if (method == "OPTIONS" || method == "HEAD") continue;
                using var req = new HttpRequestMessage(new HttpMethod(method), path);
                if (method is "POST" or "PUT" or "PATCH") req.Content = JsonContent.Create(new { });
                using var res = await client.SendAsync(req);
                probed++;
                if (res.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType))
                    leaks.Add($"{method} {raw} → {(int)res.StatusCode}");
            }
        }
        Assert.True(probed > 300, $"Chỉ thử được {probed} route.");
        Assert.True(leaks.Count == 0, "Route trả lời người chưa đăng nhập:\n" + string.Join("\n", leaks));
    }
}
