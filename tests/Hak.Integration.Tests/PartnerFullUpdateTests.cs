using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hak.Integration.Tests;

/// <summary>ERP-R03: lưu đối tượng là MỘT lệnh nguyên tử — dữ liệu sai ở phần thương mại không được để lại phần tên đã đổi; phiên bản cũ bị từ chối.</summary>
public class PartnerFullUpdateTests
{
    private const string Pw = "Api-Password-123";

    private static async Task<JsonNode> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonNode.Parse(body)!;
    }

    [Fact]
    public async Task FullUpdate_IsAtomic_AndVersioned()
    {
        var dbPath = Path.Combine(TestHost.Root, $"partner-full-{Guid.NewGuid():N}.db");
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
        var company = (string)(await Json(await admin.PostAsJsonAsync("/api/admin/companies", new { code = "PRT01", name = "Công ty đối tượng", functionalCurrency = "VND", regimeVersionId = regime })))["id"]!;
        var uid = (string)(await Json(await admin.PostAsJsonAsync("/api/admin/users", new { userName = "ktt", fullName = "KTT", password = Pw })))["id"]!;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = uid, companyId = company, roleCode = "CHIEF_ACCOUNTANT" })).StatusCode);
        var ktt = As((string)(await Json(await anon.PostAsJsonAsync("/api/auth/login", new { userName = "ktt", password = Pw })))["token"]!, company);

        var id = (string)(await Json(await ktt.PostAsJsonAsync("/api/partners", new { code = "KH01", name = "Tên cũ", roles = "Customer", taxCode = (string?)null })))["id"]!;
        async Task<JsonNode> Get() => (await Json(await ktt.GetAsync("/api/partners"))).AsArray().Single(p => (string)p!["id"]! == id)!;
        object Body(long version, string name, int? terms) => new
        {
            rowVersion = version, name, taxCode = "0123456789", isActive = true, address = "Đà Nẵng", phone = "0905", email = "a@b.vn",
            creditLimit = "1000000", paymentTermsDays = terms, paymentDiscountPercent = (string?)null, paymentDiscountDays = (int?)null,
        };
        var v0 = (long)(await Get())["rowVersion"]!;

        // Tên hợp lệ + hạn thanh toán 5000 ngày (ngoài 0–3650) → từ chối, KHÔNG đổi gì (kể cả tên), không tăng phiên bản.
        var bad = await ktt.PutAsJsonAsync($"/api/partners/{id}/full", Body(v0, "Tên mới", 5000));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        var afterBad = await Get();
        Assert.Equal("Tên cũ", (string)afterBad["name"]!);
        Assert.Equal(v0, (long)afterBad["rowVersion"]!);

        // Hợp lệ → lưu đủ mọi trường, phiên bản tăng một.
        Assert.Equal(HttpStatusCode.NoContent, (await ktt.PutAsJsonAsync($"/api/partners/{id}/full", Body(v0, "Tên mới", 30))).StatusCode);
        var saved = await Get();
        Assert.Equal(("Tên mới", "Đà Nẵng", 30, v0 + 1), ((string)saved["name"]!, (string)saved["address"]!, (int)saved["paymentTermsDays"]!, (long)saved["rowVersion"]!));

        // Phiên bản cũ → 409, dữ liệu giữ nguyên.
        var stale = await ktt.PutAsJsonAsync($"/api/partners/{id}/full", Body(v0, "Ghi đè bằng bản cũ", 45));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Tên mới", (string)(await Get())["name"]!);
    }
}
