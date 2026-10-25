using Hak.Api;
using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Banking;
using Hak.Application.Common;
using Hak.Application.Cycles;
using Hak.Application.Exchange;
using Hak.Application.Inventory;
using Hak.Application.Rules;
using Hak.Application.Settlement;
using Hak.Application.Security;
using Hak.Application.Setup;
using Hak.Application.Tax;
using Hak.Application.Trade;
using Hak.Data;
using Hak.Domain.Common;

var builder = WebApplication.CreateBuilder(args);

// Không khai báo Hak:Environment = mặc định của HakRuntimeOptions (Production, an toàn khi quên cấu hình): chạy HAK.exe trực tiếp trên dữ liệu
// thật vẫn chặn ghi sổ khi chế độ kế toán chưa xác minh. Development / Test phải bật tường minh (--Hak:Environment=Development, launchSettings, test host).
var runtime = new HakRuntimeOptions();
if (builder.Configuration["Hak:Environment"] is { Length: > 0 } configuredEnvironment) runtime.Environment = configuredEnvironment;

// Storage (docs/02 ADR-04): Sqlite = one machine (default); PostgreSQL = LAN server mode. The PostgreSQL connection string comes
// from ConnectionStrings:Hak (environment variable ConnectionStrings__Hak or a protected settings file), never from the repository.
var provider = builder.Configuration["Hak:Provider"] ?? "Sqlite";
var usePostgres = provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) || provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);
if (!usePostgres && !provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException($"Hak:Provider '{provider}' không hợp lệ (Sqlite | PostgreSQL).");
string storage;
if (usePostgres)
{
    var cs = builder.Configuration.GetConnectionString("Hak");
    if (string.IsNullOrWhiteSpace(cs))
        throw new InvalidOperationException("Hak:Provider=PostgreSQL cần chuỗi kết nối ConnectionStrings:Hak (biến môi trường ConnectionStrings__Hak).");
    var csb = new Npgsql.NpgsqlConnectionStringBuilder(cs);
    if (runtime.IsProduction && csb.SslMode is Npgsql.SslMode.Disable)
        throw new InvalidOperationException("Production trên mạng LAN không được tắt SSL tới PostgreSQL (SslMode=Disable).");
    storage = $"PostgreSQL {csb.Host}:{csb.Port}/{csb.Database}";   // never log the password
    builder.Services.AddScoped(_ => new HakDbContext(HakDb.PostgresOptions(cs)));
}
else
{
    var dbPath = builder.Configuration["Hak:DatabasePath"]
                 ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HAK", runtime.Environment, "hak.db");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
    storage = $"SQLite {dbPath}";
    builder.Services.AddScoped(_ => new HakDbContext(HakDb.SqliteOptions(dbPath)));
}

var attachmentsRoot = builder.Configuration["Hak:AttachmentsPath"]
                      ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(builder.Configuration["Hak:DatabasePath"]
                          ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HAK", runtime.Environment, "hak.db"))) ?? AppContext.BaseDirectory, "attachments");
builder.Services.AddSingleton(new Hak.Application.Attachments.AttachmentStore(attachmentsRoot));
builder.Services.AddHakApplicationServices();   // ServiceRegistration.cs
builder.Services.AddSingleton(runtime);
// Hóa đơn điện tử (docs/12): mặc định KHÔNG có nhà cung cấp (không phát hành được). "Simulated" chỉ bật được ở Development / Test và giả lập toàn bộ (không ký số thật).
var einvoiceProvider = builder.Configuration["Hak:EInvoice:Provider"] ?? "None";
if (einvoiceProvider.Equals("Simulated", StringComparison.OrdinalIgnoreCase))
{
    if (runtime.IsProduction) throw new InvalidOperationException("Hak:EInvoice:Provider=Simulated chỉ dùng ở Development / Test, không dùng cho hóa đơn thật.");
    builder.Services.AddSingleton<Hak.Application.EInvoice.IEInvoiceProvider, Hak.Application.EInvoice.SimulatedEInvoiceProvider>();
}
else if (einvoiceProvider.Equals("None", StringComparison.OrdinalIgnoreCase)) builder.Services.AddSingleton<Hak.Application.EInvoice.IEInvoiceProvider, Hak.Application.EInvoice.NullEInvoiceProvider>();
else throw new InvalidOperationException($"Hak:EInvoice:Provider '{einvoiceProvider}' chưa có bộ chuyển đổi (None | Simulated).");
builder.Services.AddHostedService<EInvoiceWorker>();
builder.Services.AddSingleton<AutoBackupStatus>();
builder.Services.AddHostedService<AutoBackupWorker>();
builder.Services.AddSingleton(new Hak.Application.EInvoice.EInvoiceOptions(builder.Configuration["Hak:EInvoice:TemplateCode"] ?? "1"));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton(new Hak.Application.Backup.BackupOptions
{
    Directory = builder.Configuration["Hak:BackupPath"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HAK", runtime.Environment, "backups"),
});
// Giới hạn tần suất đăng nhập theo IP (chặn dò mật khẩu/tên đăng nhập hàng loạt; khóa theo tài khoản vẫn giữ nguyên). 0 = tắt.
var loginLimit = builder.Configuration.GetValue("Hak:LoginRateLimitPerMinute", 30);
if (loginLimit > 0)
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy("login", http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = loginLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new DecimalAsStringConverter()));

// Khóa chủ mã hóa bí mật trong CSDL (khóa trao đổi): Hak:SecretKey (base64 32 byte, nên đặt bằng biến môi trường), hoặc tệp secret.key
// cạnh thư mục dữ liệu. Phải sao lưu RIÊNG khỏi CSDL; mất khóa chủ thì phải cấp lại khóa trao đổi.
var masterKey = runtime.Environment != "Test" || builder.Configuration["Hak:SecretKey"] is not null
    ? new MasterKey(SecretProtector.LoadOrCreateKey(builder.Configuration["Hak:SecretKey"],
        builder.Configuration["Hak:SecretKeyFile"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HAK", runtime.Environment, "secret.key")))
    : MasterKey.None;
builder.Services.AddSingleton(masterKey);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HakDbContext>();
    HakDb.Migrate(db);
    await scope.ServiceProvider.GetRequiredService<SetupService>().EnsureReferenceDataAsync();
    var upgraded = await SecretProtector.UpgradeStoredKeysAsync(db, masterKey);
    if (upgraded > 0) app.Logger.LogInformation("Đã mã hóa {Count} khóa trao đổi đang lưu dạng thường.", upgraded);
    // SEC-01: xóa bí mật mà bản cũ đã lưu vào bảng chống gửi trùng; khóa trao đổi bị lộ được đánh dấu để cấp lại (docs/08).
    var purged = await IdempotencySecretPurge.RunAsync(db, scope.ServiceProvider.GetRequiredService<Hak.Application.Audit.AuditService>());
    if (purged.Deleted > 0)
        app.Logger.LogWarning("Đã xóa {Count} bản ghi chống gửi trùng chứa bí mật; {Keys} khóa trao đổi cần cấp lại (màn Lương → Khóa trao đổi dữ liệu).",
            purged.Deleted, purged.ExposedExchangeKeyIds.Count);
    // SEC-09: neo mọi chuỗi nhật ký tại điểm cuối hiện có (ký bằng khóa chủ); từ đây bản ghi mới đều có khóa.
    var anchors = await scope.ServiceProvider.GetRequiredService<Hak.Application.Audit.AuditService>().EnsureAnchorsAsync();
    if (anchors > 0) app.Logger.LogInformation("Đã neo {Count} chuỗi nhật ký kiểm toán vào khóa chủ.", anchors);

    // Local administrator recovery (docs/08 §8): Hak.Api.exe --reset-admin-password <user>; the password is typed twice, never echoed.
    var at = Array.IndexOf(args, "--reset-admin-password");
    if (at >= 0)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (at + 1 >= args.Length || Console.IsInputRedirected) { Console.Error.WriteLine("Dùng: Hak.Api.exe --reset-admin-password <tên đăng nhập> (chạy trong cửa sổ dòng lệnh)."); return 2; }
        var first = ConsoleSecret.Read("Mật khẩu mới: ");
        if (first != ConsoleSecret.Read("Nhập lại: ")) { Console.Error.WriteLine("Hai lần nhập không khớp."); return 1; }
        try
        {
            await scope.ServiceProvider.GetRequiredService<SetupService>().RecoverAdminPasswordAsync(args[at + 1], first);
            Console.WriteLine("Đã đặt lại mật khẩu và mở khóa. Hành động đã ghi vào nhật ký kiểm toán.");
            return 0;
        }
        catch (DomainException ex) { Console.Error.WriteLine($"{ex.Code}: {ex.Message}"); return 1; }
    }
}

// Header bảo mật cho mọi chế độ (API và giao diện đóng gói). HSTS chỉ gửi khi truy cập qua HTTPS (Production).
if (runtime.IsProduction) app.UseHsts();
app.Use(async (http, next) =>
{
    var h = http.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Content-Security-Policy"] = "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; frame-ancestors 'none'";
    await next();
});
if (loginLimit > 0) app.UseRateLimiter();

// Packaged mode (docs/08): the API also serves the built web UI from wwwroot beside the executable (or Hak:WebRoot), same origin,
// so the browser needs no proxy. Development keeps using the Vite dev server.
var webRoot = builder.Configuration["Hak:WebRoot"] ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (File.Exists(Path.Combine(webRoot, "index.html")))
{
    var files = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(webRoot));
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
    app.MapFallback(async http =>
    {
        if (http.Request.Path.StartsWithSegments("/api")) { http.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        http.Response.ContentType = "text/html; charset=utf-8";
        await http.Response.SendFileAsync(files.GetFileInfo("index.html"));
    });
}

app.UseMiddleware<DomainExceptionMiddleware>();
app.UseMiddleware<RequireSessionMiddleware>();   // mọi /api/* (trừ danh sách trắng) cần phiên hợp lệ, kiểm trước khi mở giao dịch
app.UseMiddleware<IdempotencyMiddleware>();   // trong DomainExceptionMiddleware: lỗi nghiệp vụ hoàn tác giao dịch rồi mới thành mã HTTP
app.MapHakEndpoints();
app.MapAdminEndpoints();
app.MapAccountEndpoints();
app.MapImportEndpoints();
app.MapPhase2Endpoints();
app.MapBankingEndpoints();
app.MapCycleEndpoints();
app.MapInventoryEndpoints();
app.MapDrynessEndpoints();
app.MapTradeEndpoints();
app.MapVatEndpoints();
app.MapAssetEndpoints();
app.MapContractEndpoints();
app.MapAdvanceEndpoints();
app.MapNumberingEndpoints();
app.MapGroupEndpoints();
app.MapAttachmentEndpoints();
app.MapSalesApprovalEndpoints();
app.MapServiceAcceptanceEndpoints();
app.MapPaymentDiscountEndpoints();
app.MapDiscountEndpoints();
app.MapEInvoiceEndpoints();
app.MapEquityEndpoints();
app.MapFxEndpoints();
app.MapLoanEndpoints();
app.MapPayrollEndpoints();
app.MapProductionEndpoints();
app.MapBackupEndpoints();
app.MapReportEngineEndpoints();
app.Logger.LogInformation("HAK API started. Environment={Env} Storage={Storage}", runtime.Environment, storage);
app.Run();
return 0;

public partial class Program;

internal static class ConsoleSecret
{
    /// <summary>Reads a line from the console without echoing it.</summary>
    public static string Read(string prompt)
    {
        Console.Write(prompt);
        var sb = new System.Text.StringBuilder();
        for (ConsoleKeyInfo k; (k = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter;)
        {
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
            else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }
}
