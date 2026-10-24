using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hak.Integration.Tests;

/// <summary>ERP-R02: phản hồi đã lưu theo Idempotency-Key chỉ được trả lại khi người gọi VẪN còn quyền; không bao giờ chạy lại thao tác ghi.</summary>
public class IdempotencyReplayAuthTests
{
    private const string Pw = "Api-Password-123";

    private static async Task<JsonNode> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonNode.Parse(body)!;
    }

    private static HttpClient As(WebApplicationFactory<Program> f, string token, string? company = null)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (company is not null) c.DefaultRequestHeaders.Add("X-Company-Id", company);
        return c;
    }

    private static async Task<string> Login(HttpClient c, string user)
        => (string)(await Json(await c.PostAsJsonAsync("/api/auth/login", new { userName = user, password = Pw })))["token"]!;

    [Fact]
    public async Task CachedReplay_RechecksCurrentPermissions_AndNeverWritesAgain()
    {
        var dbPath = Path.Combine(TestHost.Root, $"idem-auth-{Guid.NewGuid():N}.db");
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
        var admin = As(f, await Login(anon, "admin"));
        var regime = (string)(await Json(await admin.PostAsJsonAsync("/api/admin/regime-versions", new
        {
            regimeCode = "TEST-REGIME", regimeName = "Kiểm thử", versionCode = "TEST-1", effectiveFrom = "2020-01-01", effectiveTo = (string?)null,
            sources = new[] { new { ruleGroup = "TEST", documentNo = "TEST-FIXTURE" } },
        })))["id"]!;
        var company = (string)(await Json(await admin.PostAsJsonAsync("/api/admin/companies",
            new { code = "IDM01", name = "Công ty Idem", functionalCurrency = "VND", regimeVersionId = regime })))["id"]!;
        async Task<string> NewUser(string name, params string[] roles)
        {
            var id = (string)(await Json(await admin.PostAsJsonAsync("/api/admin/users", new { userName = name, fullName = name, password = Pw })))["id"]!;
            foreach (var role in roles)
                Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = id, companyId = company, roleCode = role })).StatusCode);
            return id;
        }
        var setupId = await NewUser("setup", "CHIEF_ACCOUNTANT", "ADMIN");
        var wId = await NewUser("writer", "GL_ACCOUNTANT");
        var setup = As(f, await Login(anon, "setup"), company);
        await Json(await setup.PostAsJsonAsync("/api/fiscal-years", new { code = "2026", startDate = "2026-01-01", endDate = "2026-12-31" }));
        var cash = (string)(await Json(await setup.PostAsJsonAsync("/api/accounts", new { code = "CASH", name = "Tiền mặt" })))["id"]!;
        var rev = (string)(await Json(await setup.PostAsJsonAsync("/api/accounts", new { code = "REV", name = "Doanh thu" })))["id"]!;

        var writerToken = await Login(anon, "writer");
        var writer = As(f, writerToken, company);
        object Voucher(string description) => new
        {
            voucherType = "GJ", voucherDate = "2026-04-02", postingDate = "2026-04-02", description,
            lines = new object[] { new { accountId = cash, side = "D", amount = "1000" }, new { accountId = rev, side = "C", amount = "1000" } },
        };
        async Task<HttpResponseMessage> Keyed(HttpClient c, string key, object payload)
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, "/api/vouchers") { Content = JsonContent.Create(payload) };
            msg.Headers.Add("Idempotency-Key", key);
            return await c.SendAsync(msg);
        }
        async Task<int> Total() => (int)(await Json(await setup.GetAsync("/api/vouchers")))["total"]!;

        var key = "replay-auth-0001";
        var first = await Keyed(writer, key, Voucher("Ghi một lần"));
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, await Total());

        // Còn đủ quyền: nhận lại đúng phản hồi cũ, không ghi thêm; cùng khóa khác nội dung → 422.
        var replayOk = await Keyed(writer, key, Voucher("Ghi một lần"));
        Assert.Equal(firstBody, await replayOk.Content.ReadAsStringAsync());
        Assert.True(replayOk.Headers.Contains("Idempotent-Replay"));
        Assert.Equal((HttpStatusCode)422, (await Keyed(writer, key, Voucher("Nội dung khác"))).StatusCode);
        Assert.Equal(1, await Total());

        // Thu hồi quyền ghi nhưng vẫn còn vai trò chỉ đọc tại công ty (INTERNAL_AUDITOR: xem + kiểm toán).
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = wId, companyId = company, roleCode = "INTERNAL_AUDITOR" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/users/{wId}/revoke", new { companyId = company, roleCode = "GL_ACCOUNTANT", reason = "kiểm thử R02" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await writer.GetAsync("/api/vouchers")).StatusCode);                  // vẫn xem được
        Assert.Equal(HttpStatusCode.Forbidden, (await Keyed(writer, "replay-auth-0002", Voucher("Ghi mới"))).StatusCode);
        var replayRevoked = await Keyed(writer, key, Voucher("Ghi một lần"));
        Assert.Equal(HttpStatusCode.Forbidden, replayRevoked.StatusCode);
        Assert.False(replayRevoked.Headers.Contains("Idempotent-Replay"));
        Assert.DoesNotContain("Ghi một lần", await replayRevoked.Content.ReadAsStringAsync());
        Assert.Equal((HttpStatusCode)403, (await Keyed(writer, key, Voucher("Nội dung khác"))).StatusCode);   // không lộ cả việc khóa đã dùng (422)
        Assert.Equal(1, await Total());

        // Cấp lại quyền → nhận lại phản hồi cũ, vẫn không ghi thêm.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = wId, companyId = company, roleCode = "GL_ACCOUNTANT" })).StatusCode);
        Assert.Equal(firstBody, await (await Keyed(writer, key, Voucher("Ghi một lần"))).Content.ReadAsStringAsync());
        Assert.Equal(1, await Total());

        // Mất mọi vai trò tại công ty → 403.
        foreach (var role in new[] { "GL_ACCOUNTANT", "INTERNAL_AUDITOR" })
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/users/{wId}/revoke", new { companyId = company, roleCode = role, reason = "kiểm thử R02" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Keyed(writer, key, Voucher("Ghi một lần"))).StatusCode);

        // Đăng xuất → phiên cũ gửi lại → 401 (giữ nguyên hành vi hiện có).
        Assert.Equal(HttpStatusCode.NoContent, (await writer.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Keyed(writer, key, Voucher("Ghi một lần"))).StatusCode);
        Assert.Equal(1, await Total());
        _ = setupId;
    }
}
