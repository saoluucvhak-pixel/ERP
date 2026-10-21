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
        foreach (var file in Directory.EnumerateFiles(WebSrc(), "*.ts*").Where(x => !x.EndsWith(".test.ts")))
            foreach (Match m in CallPattern().Matches(File.ReadAllText(file)))
            {
                var call = Segments(m.Groups[2].Value);
                if (call.Length == 0 || call[0].Contains("${")) continue;      // đường dẫn dựng hoàn toàn bằng biểu thức: không kiểm được tĩnh
                if (!routes.Any(r => Matches(r, call))) missing.Add($"{Path.GetFileName(file)}: /api{m.Groups[2].Value}");
            }
        Assert.True(missing.Count == 0, "Giao diện gọi đường dẫn không có route:\n" + string.Join("\n", missing));
    }
}
