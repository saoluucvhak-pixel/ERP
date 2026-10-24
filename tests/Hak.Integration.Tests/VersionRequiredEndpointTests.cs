using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hak.Integration.Tests;

/// <summary>Các thao tác duyệt / ghi sổ của người dùng trên hóa đơn, phiếu kho, lệnh thanh toán, hợp đồng bắt buộc mang phiên bản đã xem (thiếu → 400).</summary>
public class VersionRequiredEndpointTests
{
    private const string Pw = "Api-Password-123";

    [Fact]
    public async Task UserApprovalAndPostingEndpoints_RequireTheViewedVersion()
    {
        var dbPath = Path.Combine(TestHost.Root, $"version-required-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(TestHost.Root);
        using var pg = TestHost.PostgresServer is null ? null : ScratchPostgresDatabase.Create();
        await using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Hak:Environment", "Test");
            b.UseSetting("Hak:LoginRateLimitPerMinute", "0");
            if (pg is null) b.UseSetting("Hak:DatabasePath", dbPath);
            else { b.UseSetting("Hak:Provider", "PostgreSQL"); b.UseSetting("ConnectionStrings:Hak", pg.ConnectionString); }
        });
        var anon = f.CreateClient();
        async Task<JsonNode> Json(HttpResponseMessage r) { var t = await r.Content.ReadAsStringAsync(); Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {t}"); return JsonNode.Parse(t)!; }
        await Json(await anon.PostAsJsonAsync("/api/setup/bootstrap", new { userName = "admin", fullName = "Admin", password = Pw }));
        HttpClient As(string token, string? company = null)
        {
            var c = f.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (company is not null) c.DefaultRequestHeaders.Add("X-Company-Id", company);
            return c;
        }
        var admin = As((string)(await Json(await anon.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = Pw })))["token"]!);
        var regime = (string)(await Json(await admin.PostAsJsonAsync("/api/admin/regime-versions", new
        {
            regimeCode = "TEST-REGIME", regimeName = "Kiểm thử", versionCode = "TEST-1", effectiveFrom = "2020-01-01", effectiveTo = (string?)null,
            sources = new[] { new { ruleGroup = "TEST", documentNo = "TEST-FIXTURE" } },
        })))["id"]!;
        var company = (string)(await Json(await admin.PostAsJsonAsync("/api/admin/companies", new { code = "VER01", name = "Công ty phiên bản", functionalCurrency = "VND", regimeVersionId = regime })))["id"]!;
        var uid = (string)(await Json(await admin.PostAsJsonAsync("/api/admin/users", new { userName = "ktt", fullName = "KTT", password = Pw })))["id"]!;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = uid, companyId = company, roleCode = "CHIEF_ACCOUNTANT" })).StatusCode);
        var ktt = As((string)(await Json(await anon.PostAsJsonAsync("/api/auth/login", new { userName = "ktt", password = Pw })))["token"]!, company);

        var id = Guid.NewGuid();
        foreach (var path in new[] { $"/api/invoices/{id}/post", $"/api/invoices/{id}/reverse", $"/api/stock-documents/{id}/post", $"/api/payment-orders/{id}/approve", $"/api/assets/repairs/{id}/submit", $"/api/assets/repairs/{id}/decide",
                                   $"/api/invoices/{id}/cancel", $"/api/stock-documents/{id}/cancel", $"/api/stock-documents/{id}/reverse", $"/api/payment-orders/{id}/cancel" })
            Assert.Equal(HttpStatusCode.BadRequest, (await ktt.PostAsJsonAsync(path, new { reversalDate = "2026-03-01", reason = "x" })).StatusCode);   // thiếu rowVersion
        foreach (var path in new[] { $"/api/contracts/{id}/submit", $"/api/contracts/{id}/decide", $"/api/contracts/{id}/complete", $"/api/contracts/{id}/cancel" })
            Assert.Equal(HttpStatusCode.BadRequest, (await ktt.PostAsJsonAsync(path, new { approve = true, note = (string?)null })).StatusCode);              // thiếu version
        // Phân trang phía máy chủ trả tổng số và cờ còn thêm (danh sách rỗng thì total = 0).
        var stockPage = await Json(await ktt.GetAsync("/api/stock-documents/page?skip=0&take=10"));
        Assert.Equal((0, false), ((int)stockPage["total"]!, (bool)stockPage["hasMore"]!));
        var orderPage = await Json(await ktt.GetAsync("/api/payment-orders/page?skip=0&take=10"));
        Assert.Equal((0, false), ((int)orderPage["total"]!, (bool)orderPage["hasMore"]!));
        // Có phiên bản thì đi tiếp tới tầng nghiệp vụ (đối tượng không tồn tại → 404), chứng tỏ tham số được nhận.
        Assert.Equal(HttpStatusCode.NotFound, (await ktt.PostAsync($"/api/invoices/{id}/post?rowVersion=1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ktt.PostAsync($"/api/stock-documents/{id}/post?rowVersion=1", null)).StatusCode);
    }
}
