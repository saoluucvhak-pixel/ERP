using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hak.Integration.Tests;

/// <summary>End-to-end over HTTP: proves permissions and money formatting hold at the API boundary, not only in services.</summary>
public class ApiTests
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

    [Theory]
    [InlineData("0.0000", "0")]
    [InlineData("-0.0000", "0")]
    [InlineData("1250000.0000", "1250000")]
    [InlineData("25133.333300", "25133.3333")]
    [InlineData("100", "100")]
    [InlineData("-7.2500", "-7.25")]
    public void MoneyIsSerialisedInCanonicalForm(string stored, string expected)
        => Assert.Equal(expected, Hak.Api.DecimalAsStringConverter.Canonical(decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public async Task EveryWebApiCall_HasAServerRoute()
    {
        // Mọi đường dẫn API mà giao diện gọi (web/src) phải trùng một route của server: đường dẫn lệch nhau thì màn hình luôn nhận 404
        // (đã xảy ra với /api/cycles/order-worklist và /api/orders/{id}/flow), mà không test nghiệp vụ nào chạm tới.
        Directory.CreateDirectory(TestHost.Root);
        await using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Hak:Environment", "Test");
            b.UseSetting("Hak:DatabasePath", Path.Combine(TestHost.Root, $"api-{Guid.NewGuid():N}.db"));
        });
        var routes = f.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Select(e => (Methods: e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods,
                // null = tham số (khớp mọi giá trị), còn lại là phần chữ cố định của đoạn đường dẫn
                Segments: e.RoutePattern.PathSegments.Select(s => s.IsSimple && s.Parts[0].IsLiteral
                    ? ((Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart)s.Parts[0]).Content : null).ToArray()))
            .ToList();

        var web = AppContext.BaseDirectory;
        while (web is not null && !Directory.Exists(Path.Combine(web, "web", "src"))) web = Path.GetDirectoryName(web);
        Assert.True(web is not null, "không tìm thấy thư mục web/src từ thư mục test");
        var helper = new System.Text.RegularExpressions.Regex(@"\b(get|post|put|postRaw)(?:<[^>(]*>)?\(\s*([`'""])(/[^`'""]*)\2");
        var generic = new System.Text.RegularExpressions.Regex(@"\bapi(?:<[^>(]*>)?\(\s*'([A-Z]+)'\s*,\s*([`'""])(/[^`'""]*)\2");
        var raw = new System.Text.RegularExpressions.Regex(@"([`'""])/api(/[^`'""]*)\1");
        var calls = new List<(string? Method, string Path, string File)>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(web, "web", "src"), "*.ts*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "api.ts" || file.EndsWith(".test.ts", StringComparison.Ordinal)) continue;
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match m in helper.Matches(text))
                calls.Add((m.Groups[1].Value == "get" ? "GET" : m.Groups[1].Value == "put" ? "PUT" : "POST", m.Groups[3].Value, Path.GetFileName(file)));
            foreach (System.Text.RegularExpressions.Match m in generic.Matches(text)) calls.Add((m.Groups[1].Value, m.Groups[3].Value, Path.GetFileName(file)));
            foreach (System.Text.RegularExpressions.Match m in raw.Matches(text)) calls.Add((null, m.Groups[2].Value, Path.GetFileName(file)));
        }
        Assert.True(calls.Count > 400, $"chỉ thấy {calls.Count} lời gọi API trong web/src: cách đọc mã nguồn đã hỏng");

        bool Matches((string? Method, string Path, string File) call)
        {
            // ${…} trong chuỗi mẫu = giá trị bất kỳ; bỏ phần query.
            var path = System.Text.RegularExpressions.Regex.Replace(call.Path, @"\$\{[^}]*\}", "\u0001").Split('?')[0];
            var segs = ("api/" + path.Trim('/')).Split('/', StringSplitOptions.RemoveEmptyEntries);
            return routes.Any(r => (call.Method is null || r.Methods is null || r.Methods.Contains(call.Method))
                && r.Segments.Length == segs.Length
                && r.Segments.Zip(segs).All(p => p.First is null || p.Second.Contains('\u0001') || string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)));
        }
        Assert.Empty(calls.Where(c => !Matches(c)).Select(c => $"{c.File}: {c.Method ?? "*"} {c.Path}").Distinct());
    }

    [Fact]
    public async Task EveryApiEndpoint_RequiresASession()
    {
        var dbPath = Path.Combine(TestHost.Root, $"api-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(TestHost.Root);
        await using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Hak:Environment", "Test");
            b.UseSetting("Hak:LoginRateLimitPerMinute", "0");
            b.UseSetting("Hak:DatabasePath", dbPath);
        });
        var anon = f.CreateClient();
        var forged = As(f, "khong-phai-token-that");
        var routes = f.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => (Method: m, e.RoutePattern)))
            .ToList();
        Assert.True(routes.Count > 300, $"chỉ thấy {routes.Count} route: cách liệt kê endpoint đã hỏng");

        var open = new List<string>();
        foreach (var (method, pattern) in routes)
        {
            // Điền tham số đường dẫn: GUID cho {x:guid}, chữ cho phần còn lại; thân "{}" để endpoint POST/PUT không dừng ở 415.
            var url = "/" + string.Join("/", pattern.PathSegments.Select(seg => string.Concat(seg.Parts.Select(p => p switch
            {
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart l => l.Content,
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart q when q.ParameterPolicies.Any(pp => pp.Content == "guid") => Guid.NewGuid().ToString(),
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart => "x",
                _ => "",
            }))));
            var path = url.TrimEnd('/');
            foreach (var client in new[] { anon, forged })
            {
                var status = (await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
                    { Content = method is "GET" or "DELETE" ? null : new StringContent("{}", System.Text.Encoding.UTF8, "application/json") })).StatusCode;
                if (Hak.Api.RequireSessionMiddleware.Anonymous.Contains(path)) continue;
                if (status != HttpStatusCode.Unauthorized) open.Add($"{method} {url} → {(int)status}");
            }
        }
        Assert.Empty(open);
        // Danh sách trắng chỉ gồm route có thật (không để sót tên sai làm lộ endpoint khác).
        Assert.All(Hak.Api.RequireSessionMiddleware.Anonymous, a => Assert.Contains(routes, r => "/" + string.Join("/", r.RoutePattern.PathSegments.Select(s =>
            string.Concat(s.Parts.OfType<Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart>().Select(l => l.Content)))) == a));
    }

    [Fact]
    public async Task FullFlow_OverHttp()
    {
        var dbPath = Path.Combine(TestHost.Root, $"api-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(TestHost.Root);
        // With HAK_TEST_PG the same flow runs against the API in PostgreSQL (LAN) mode on a throw-away hak_test_* database.
        using var pg = TestHost.PostgresServer is null ? null : ScratchPostgresDatabase.Create();
        await using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Hak:Environment", "Test");
            b.UseSetting("Hak:LoginRateLimitPerMinute", "0");   // test đăng nhập dồn dập từ một IP
            if (pg is null) b.UseSetting("Hak:DatabasePath", dbPath);
            else
            {
                b.UseSetting("Hak:Provider", "PostgreSQL");
                b.UseSetting("ConnectionStrings:Hak", pg.ConnectionString);
            }
        });
        var anon = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/accounts")).StatusCode);

        await Json(await anon.PostAsJsonAsync("/api/setup/bootstrap", new { userName = "admin", fullName = "Admin", password = Pw }));
        Assert.Equal(HttpStatusCode.Forbidden, (await anon.PostAsJsonAsync("/api/setup/bootstrap", new { userName = "x", fullName = "x", password = Pw })).StatusCode);
        var adminToken = await Login(anon, "admin");
        var admin = As(f, adminToken);

        var regime = (string)(await Json(await admin.PostAsJsonAsync("/api/admin/regime-versions", new
        {
            regimeCode = "TEST-REGIME", regimeName = "Kiểm thử", versionCode = "TEST-1", effectiveFrom = "2020-01-01", effectiveTo = (string?)null,
            sources = new[] { new { ruleGroup = "TEST", documentNo = "TEST-FIXTURE" } },
        })))["id"]!;
        var company = (string)(await Json(await admin.PostAsJsonAsync("/api/admin/companies",
            new { code = "API01", name = "Công ty API", functionalCurrency = "VND", regimeVersionId = regime })))["id"]!;
        var lapId = (string)(await Json(await admin.PostAsJsonAsync("/api/admin/users", new { userName = "lap", fullName = "Lập", password = Pw })))["id"]!;
        var kttId = (string)(await Json(await admin.PostAsJsonAsync("/api/admin/users", new { userName = "ktt", fullName = "KTT", password = Pw })))["id"]!;
        foreach (var (uid, role) in new[] { (lapId, "GL_ACCOUNTANT"), (kttId, "CHIEF_ACCOUNTANT") })
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = uid, companyId = company, roleCode = role })).StatusCode);

        var lap = As(f, await Login(anon, "lap"), company);
        var ktt = As(f, await Login(anon, "ktt"), company);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(f, adminToken, company).GetAsync("/api/accounts")).StatusCode); // admin has no role in API01

        // CHIEF_ACCOUNTANT has no config.manage by default; fiscal-year creation needs the ADMIN role in the company.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ktt.PostAsJsonAsync("/api/fiscal-years", new { code = "2026", startDate = "2026-01-01", endDate = "2026-12-31" })).StatusCode);
        var grantAdmin = await admin.PostAsJsonAsync("/api/admin/grants", new { userId = kttId, companyId = company, roleCode = "ADMIN" });
        Assert.Equal(HttpStatusCode.NoContent, grantAdmin.StatusCode);
        await Json(await ktt.PostAsJsonAsync("/api/fiscal-years", new { code = "2026", startDate = "2026-01-01", endDate = "2026-12-31" }));
        var cash = (string)(await Json(await ktt.PostAsJsonAsync("/api/accounts", new { code = "CASH", name = "Tiền mặt" })))["id"]!;
        var rev = (string)(await Json(await ktt.PostAsJsonAsync("/api/accounts", new { code = "REV", name = "Doanh thu" })))["id"]!;

        // Money as JSON number is refused; as string it is accepted and echoed back as string.
        var asNumber = await lap.PostAsync("/api/vouchers", new StringContent(
            $$"""{"voucherType":"GJ","voucherDate":"2026-03-01","postingDate":"2026-03-01","description":"x","lines":[{"accountId":"{{cash}}","side":"D","amount":100}]}""",
            System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, asNumber.StatusCode);

        // Malformed decimal strings are the caller's mistake (400 with a stable body), never a 500, and create no voucher.
        var before = (int)(await Json(await lap.GetAsync("/api/vouchers")))["total"]!;
        foreach (var bad in new[] { "\"abc\"", "\"\"", "null", "\"1,5\"", "\"1.2.3\"", "\"" + new string('9', 42) + "\"", "\"1e3\"", "\" 5\"", "true" })
        {
            var r = await lap.PostAsync("/api/vouchers", new StringContent(
                $$"""{"voucherType":"GJ","voucherDate":"2026-03-01","postingDate":"2026-03-01","description":"x","lines":[{"accountId":"{{cash}}","side":"D","amount":{{bad}}},{"accountId":"{{rev}}","side":"C","amount":"1"}]}""",
                System.Text.Encoding.UTF8, "application/json"));
            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"amount={bad}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        }
        Assert.Equal(before, (int)(await Json(await lap.GetAsync("/api/vouchers")))["total"]!);

        var v = await Json(await lap.PostAsJsonAsync("/api/vouchers", new
        {
            voucherType = "GJ", voucherDate = "2026-03-01", postingDate = "2026-03-01", description = "Bán lẻ thu tiền mặt",
            lines = new object[] { new { accountId = cash, side = "D", amount = "12345678901" }, new { accountId = rev, side = "C", amount = "12345678901" } },
        }));
        Assert.Equal(JsonValueKind.String, v["totalDebit"]!.GetValueKind());
        Assert.Equal("12345678901", (string)v["totalDebit"]!);
        var id = (string)v["id"]!;

        // Idempotency-Key: a repeated write returns the first response and writes once; another body under the same key is refused; failures are not remembered.
        async Task<HttpResponseMessage> Keyed(string key, object payload)
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, "/api/vouchers") { Content = JsonContent.Create(payload) };
            msg.Headers.Add("Idempotency-Key", key);
            return await lap.SendAsync(msg);
        }
        object Voucher(string description, string amount) => new
        {
            voucherType = "GJ", voucherDate = "2026-04-02", postingDate = "2026-04-02", description,   // April: the March counts asserted below stay as they were
            lines = new object[] { new { accountId = cash, side = "D", amount }, new { accountId = rev, side = "C", amount } },
        };
        var totalBefore = (int)(await Json(await lap.GetAsync("/api/vouchers")))["total"]!;
        var keyedFirst = await Keyed("key-aaaa-0001", Voucher("Ghi một lần", "1000"));
        var keyedFirstBody = await keyedFirst.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, keyedFirst.StatusCode);
        var keyedRetry = await Keyed("key-aaaa-0001", Voucher("Ghi một lần", "1000"));          // lost response → client retries with the same key
        Assert.Equal(keyedFirstBody, await keyedRetry.Content.ReadAsStringAsync());
        Assert.True(keyedRetry.Headers.Contains("Idempotent-Replay"));
        Assert.Equal(totalBefore + 1, (int)(await Json(await lap.GetAsync("/api/vouchers")))["total"]!);
        var keyedReused = await Keyed("key-aaaa-0001", Voucher("Nội dung khác", "2000"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, keyedReused.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", (string)JsonNode.Parse(await keyedReused.Content.ReadAsStringAsync())!["code"]!);
        Assert.Equal(HttpStatusCode.BadRequest, (await Keyed("key-bbbb-0002", Voucher("Sai số tiền", "abc"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Keyed("key-bbbb-0002", Voucher("Sai số tiền", "abc"))).StatusCode);   // errors are not stored: retried for real
        Assert.Equal(HttpStatusCode.BadRequest, (await Keyed("short", Voucher("Khóa ngắn", "1000"))).StatusCode);
        Assert.Equal(totalBefore + 1, (int)(await Json(await lap.GetAsync("/api/vouchers")))["total"]!);

        // Thao tác của người dùng bắt buộc mang rowVersion đã xem: thiếu → 400, cũ → 409, đúng → chạy.
        var rv = (long)v["rowVersion"]!;
        Assert.Equal(HttpStatusCode.BadRequest, (await lap.PostAsync($"/api/vouchers/{id}/submit", null)).StatusCode);
        rv = (long)(await Json(await lap.PostAsync($"/api/vouchers/{id}/submit?rowVersion={rv}", null)))["rowVersion"]!;
        Assert.Equal(HttpStatusCode.Forbidden, (await lap.PostAsync($"/api/vouchers/{id}/approve?rowVersion={rv}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ktt.PostAsync($"/api/vouchers/{id}/approve", null)).StatusCode);
        rv = (long)(await Json(await ktt.PostAsync($"/api/vouchers/{id}/approve?rowVersion={rv}", null)))["rowVersion"]!;
        Assert.Equal(HttpStatusCode.Conflict, (await ktt.PostAsync($"/api/vouchers/{id}/post?rowVersion=1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ktt.PostAsync($"/api/vouchers/{id}/post", null)).StatusCode);
        await Json(await ktt.PostAsync($"/api/vouchers/{id}/post?rowVersion={rv}", null));
        Assert.Equal(HttpStatusCode.Conflict, (await ktt.PostAsync($"/api/vouchers/{id}/post?rowVersion={rv}", null)).StatusCode);

        // Xem v → người khác trả lại, sửa, gửi lại (v mới) → duyệt bằng v cũ hoặc bỏ version bị từ chối; đúng version mới được duyệt.
        var vc = await Json(await lap.PostAsJsonAsync("/api/vouchers", Voucher("Duyệt đúng bản đã xem", "5000")));
        var vcId = (string)vc["id"]!;
        var seen = (long)(await Json(await lap.PostAsync($"/api/vouchers/{vcId}/submit?rowVersion={(long)vc["rowVersion"]!}", null)))["rowVersion"]!;   // ktt xem bản này
        var back = await Json(await ktt.PostAsJsonAsync($"/api/vouchers/{vcId}/reject?rowVersion={seen}", new { reason = "Sửa lại số liệu" }));
        Assert.Equal(HttpStatusCode.OK, (await lap.PutAsJsonAsync($"/api/vouchers/{vcId}?rowVersion={(long)back["rowVersion"]!}", Voucher("Đã sửa nội dung", "6000"))).StatusCode);
        var edited = await Json(await lap.GetAsync($"/api/vouchers/{vcId}"));
        var resubmitted = (long)(await Json(await lap.PostAsync($"/api/vouchers/{vcId}/submit?rowVersion={(long)edited["rowVersion"]!}", null)))["rowVersion"]!;
        Assert.Equal(HttpStatusCode.Conflict, (await ktt.PostAsync($"/api/vouchers/{vcId}/approve?rowVersion={seen}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ktt.PostAsync($"/api/vouchers/{vcId}/approve", null)).StatusCode);
        Assert.Equal("Approved", (string)(await Json(await ktt.PostAsync($"/api/vouchers/{vcId}/approve?rowVersion={resubmitted}", null)))["status"]!);

        var tb = await Json(await ktt.GetAsync("/api/reports/trial-balance?from=2026-03-01&to=2026-03-31"));
        Assert.True((bool)tb["isBalanced"]!);
        Assert.Equal("12345678901", (string)tb["totals"]!["totalPeriodDebit"]!);
        Assert.True((bool)(await Json(await ktt.GetAsync("/api/audit/verify")))["intact"]!);
        Assert.Equal(HttpStatusCode.Forbidden, (await lap.GetAsync("/api/audit/verify")).StatusCode);

        // Paged voucher list with filters.
        for (var i = 1; i <= 2; i++)
            await Json(await lap.PostAsJsonAsync("/api/vouchers", new
            {
                voucherType = "GJ", voucherDate = $"2026-03-0{i + 1}", postingDate = $"2026-03-0{i + 1}", description = $"Chi phí VĂN PHÒNG {i}",
                lines = new object[] { new { accountId = rev, side = "D", amount = "10" }, new { accountId = cash, side = "C", amount = "10" } },
            }));
        var page = await Json(await ktt.GetAsync("/api/vouchers?from=2026-03-01&to=2026-03-31&take=2"));
        Assert.Equal((3, 2), ((int)page["total"]!, page["items"]!.AsArray().Count));
        var second = await Json(await ktt.GetAsync("/api/vouchers?from=2026-03-01&to=2026-03-31&take=2&skip=2"));
        Assert.Single(second["items"]!.AsArray());
        var found = await Json(await ktt.GetAsync("/api/vouchers?text=" + Uri.EscapeDataString("văn phòng 2")));
        Assert.Equal(1, (int)found["total"]!);
        // Search is case-insensitive for Vietnamese capitals (non-ASCII) in the stored text too, on SQLite as on PostgreSQL.
        var upper = await Json(await ktt.GetAsync("/api/vouchers?text=" + Uri.EscapeDataString("chi phí Văn Phòng 2")));
        Assert.Equal(1, (int)upper["total"]!);

        // Hồ sơ chu trình đơn hàng: đúng đường dẫn giao diện gọi. Đơn không tồn tại → lỗi nghiệp vụ NOT_FOUND (có mã), không phải 404 vì sai route.
        Assert.NotNull((await Json(await ktt.GetAsync("/api/cycles/order-worklist?kind=Sales")))["items"]!.AsArray());
        var noOrder = await ktt.GetAsync($"/api/cycles/orders/{Guid.NewGuid()}/flow");
        Assert.Equal(HttpStatusCode.NotFound, noOrder.StatusCode);
        Assert.Equal("NOT_FOUND", (string)JsonNode.Parse(await noOrder.Content.ReadAsStringAsync())!["code"]!);
        Assert.Equal(1, (int)(await Json(await ktt.GetAsync("/api/vouchers?status=Posted")))["total"]!);
    }
}
