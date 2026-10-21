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

var runtime = new HakRuntimeOptions { Environment = builder.Configuration["Hak:Environment"] ?? "Development" };

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
builder.Services.AddScoped<Hak.Application.Attachments.AttachmentService>();
builder.Services.AddSingleton(runtime);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<SetupService>();
builder.Services.AddScoped<CatalogImportService>();
builder.Services.AddScoped<AccountingEngine>();
builder.Services.AddScoped<LedgerQueryService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<JournalVoucherPackageService>();
builder.Services.AddScoped<SettlementService>();
builder.Services.AddScoped<PostingRuleService>();
builder.Services.AddScoped<BusinessDocumentService>();
builder.Services.AddScoped<ClosingService>();
builder.Services.AddScoped<PaymentOrderService>();
builder.Services.AddScoped<PaymentAcceptanceService>();
builder.Services.AddScoped<PurchaseVarianceService>();
builder.Services.AddScoped<PurchaseReceivingService>();
builder.Services.AddScoped<SalesOrderCheckService>();
builder.Services.AddScoped<ReorderService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<HomeService>();
builder.Services.AddScoped<Hak.Application.Reports.FinancialAnalysisService>();
builder.Services.AddScoped<Hak.Application.Reports.DashboardOverviewService>();
builder.Services.AddScoped<PayrollPaymentService>();
builder.Services.AddScoped<Hak.Application.Assets.AssetCycleService>();
builder.Services.AddScoped<Hak.Application.Assets.AssetRepairService>();
builder.Services.AddScoped<Hak.Application.Assets.AssetJournalService>();
builder.Services.AddScoped<SalesDispatchService>();
builder.Services.AddScoped<GoodsInspectionService>();
builder.Services.AddScoped<PurchaseMatchService>();
builder.Services.AddScoped<SalesReportService>();
builder.Services.AddScoped<CashPlanService>();
builder.Services.AddScoped<BankReconciliationService>();
builder.Services.AddScoped<StatementVoucherService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<DrynessService>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ContractService>();
builder.Services.AddScoped<Hak.Application.Advances.AdvanceService>();
builder.Services.AddScoped<SalesApprovalService>();
builder.Services.AddScoped<ServiceAcceptanceService>();
builder.Services.AddScoped<ServiceReportService>();
builder.Services.AddScoped<Hak.Application.Accounting.SpecialJournalService>();
builder.Services.AddScoped<ContractServicePlanService>();
builder.Services.AddScoped<ServiceProgressService>();
builder.Services.AddScoped<PaymentDiscountService>();
builder.Services.AddScoped<NumberingService>();
builder.Services.AddScoped<Hak.Application.Setup.GroupService>();
builder.Services.AddScoped<Hak.Application.Banking.BankAccountService>();
builder.Services.AddScoped<Hak.Application.Equity.EquityService>();
builder.Services.AddScoped<Hak.Application.Advances.CashCountService>();
builder.Services.AddScoped<VatService>();
builder.Services.AddScoped<VatOffsetService>();
builder.Services.AddScoped<Hak.Application.Assets.AssetService>();
builder.Services.AddScoped<Hak.Application.Fx.FxService>();
builder.Services.AddScoped<Hak.Application.Fx.FxPaymentService>();
builder.Services.AddScoped<Hak.Application.Loans.LoanService>();
builder.Services.AddScoped<Hak.Application.Loans.LoanScheduleService>();
builder.Services.AddScoped<Hak.Application.Payroll.PayrollService>();
builder.Services.AddScoped<Hak.Application.Exchange.ExchangeKeyService>();
builder.Services.AddScoped<Hak.Application.Production.ProductionCostService>();
builder.Services.AddScoped<Hak.Application.Production.ProductionOrderService>();
builder.Services.AddSingleton(new Hak.Application.Backup.BackupOptions
{
    Directory = builder.Configuration["Hak:BackupPath"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HAK", runtime.Environment, "backups"),
});
builder.Services.AddScoped<Hak.Application.Backup.BackupService>();
builder.Services.AddScoped<Hak.Application.Reports.ReportService>();
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
if (runtime.Environment != "Test" || builder.Configuration["Hak:SecretKey"] is not null)
    SecretProtector.Configure(SecretProtector.LoadOrCreateKey(builder.Configuration["Hak:SecretKey"],
        builder.Configuration["Hak:SecretKeyFile"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HAK", runtime.Environment, "secret.key")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HakDbContext>();
    HakDb.Migrate(db);
    await scope.ServiceProvider.GetRequiredService<SetupService>().EnsureReferenceDataAsync();
    var upgraded = await SecretProtector.UpgradeStoredKeysAsync(db);
    if (upgraded > 0) app.Logger.LogInformation("Đã mã hóa {Count} khóa trao đổi đang lưu dạng thường.", upgraded);

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
app.UseMiddleware<IdempotencyMiddleware>();   // trong DomainExceptionMiddleware: lỗi nghiệp vụ hoàn tác giao dịch rồi mới thành mã HTTP
app.MapHakEndpoints();
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
