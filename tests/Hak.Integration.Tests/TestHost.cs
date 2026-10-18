using Hak.Application.Accounting;
using Hak.Application.Assets;
using Hak.Application.Audit;
using Hak.Application.Banking;
using Hak.Application.Common;
using Hak.Application.Exchange;
using Hak.Application.Inventory;
using Hak.Application.Rules;
using Hak.Application.Settlement;
using Hak.Application.Security;
using Hak.Application.Setup;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Integration;
using Hak.Domain.Organization;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hak.Integration.Tests;

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc);
}

/// <summary>Services bound to one DbContext — the equivalent of one HTTP request / one user session.</summary>
public sealed class Scope : IDisposable
{
    public Scope(TestHost host, IClock clock, HakRuntimeOptions options, IInterceptor[] interceptors)
    {
        Db = new HakDbContext(host.IsPostgres ? HakDb.PostgresOptions(host.ConnectionString, interceptors) : HakDb.SqliteOptions(host.DbPath, interceptors));
        Audit = new AuditService(Db, clock);
        Setup = new SetupService(Db, Audit, clock, options);
        Engine = new AccountingEngine(Db, Audit, clock, options);
        Ledger = new LedgerQueryService(Db);
        Auth = new AuthService(Db, Audit, clock, options);
        Import = new JournalVoucherPackageService(Db, Engine, Audit, clock);
        Settlement = new SettlementService(Db, Audit, clock);
        Rules = new PostingRuleService(Db, Audit, clock);
        Documents = new BusinessDocumentService(Db, Engine, Rules);
        Closing = new ClosingService(Db, Engine, Audit, clock);
        PaymentOrders = new PaymentOrderService(Db, Documents, Audit, clock);
        BankRecon = new BankReconciliationService(Db, Audit, clock);
        Stock = new StockService(Db, Engine, Audit, clock);
        Dryness = new DrynessService(Db, Audit, clock);
        Assets = new AssetService(Db, Engine, Audit, clock);
        Fx = new Hak.Application.Fx.FxService(Db, Engine, Audit, clock);
        FxPayments = new Hak.Application.Fx.FxPaymentService(Db, Engine, Audit, clock);
        Loans = new Hak.Application.Loans.LoanService(Db, Engine, Audit, clock);
        Payroll = new Hak.Application.Payroll.PayrollService(Db, Engine, Audit, clock);
        CatalogImport = new CatalogImportService(Db, Setup, Engine, Audit, Stock, Assets);
        Reports = new Hak.Application.Reports.ReportService(Db, Audit, clock);
        StatementVouchers = new StatementVoucherService(Db, Engine, Audit);
    }

    public Hak.Application.Reports.ReportService Reports { get; }
    public StatementVoucherService StatementVouchers { get; }

    public CatalogImportService CatalogImport { get; }

    public Hak.Application.Payroll.PayrollService Payroll { get; }

    public Hak.Application.Loans.LoanService Loans { get; }

    public Hak.Application.Fx.FxPaymentService FxPayments { get; }

    public AssetService Assets { get; }
    public Hak.Application.Fx.FxService Fx { get; }

    public StockService Stock { get; }
    public DrynessService Dryness { get; }

    public PaymentOrderService PaymentOrders { get; }
    public BankReconciliationService BankRecon { get; }

    public SettlementService Settlement { get; }
    public PostingRuleService Rules { get; }
    public BusinessDocumentService Documents { get; }
    public ClosingService Closing { get; }

    public HakDbContext Db { get; }
    public AuditService Audit { get; }
    public SetupService Setup { get; }
    public AccountingEngine Engine { get; }
    public LedgerQueryService Ledger { get; }
    public AuthService Auth { get; }
    public JournalVoucherPackageService Import { get; }
    public void Dispose() => Db.Dispose();
}

/// <summary>An empty hak_test_* PostgreSQL database on the HAK_TEST_PG server, dropped on dispose (guarded like TestHost).</summary>
public sealed class ScratchPostgresDatabase : IDisposable
{
    private readonly string name = $"hak_test_{Guid.NewGuid():N}";
    public string ConnectionString { get; }

    private ScratchPostgresDatabase()
    {
        ConnectionString = new Npgsql.NpgsqlConnectionStringBuilder(TestHost.PostgresServer) { Database = name, Pooling = false }.ConnectionString;
        Admin($"CREATE DATABASE \"{name}\" ENCODING 'UTF8'");
    }

    public static ScratchPostgresDatabase Create() => new();

    private void Admin(string sql)
    {
        if (!name.StartsWith("hak_test_", StringComparison.Ordinal)) throw new InvalidOperationException("TestDbGuard: only hak_test_* databases.");
        using var c = new Npgsql.NpgsqlConnection(new Npgsql.NpgsqlConnectionStringBuilder(TestHost.PostgresServer) { Database = "postgres", Pooling = false }.ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { Admin($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)"); } catch (Npgsql.NpgsqlException) { /* best effort */ }
    }
}

/// <summary>
/// Fresh SQLite file per test under %TEMP%\hak-tests. The guard refuses any other location so
/// automated tests can never open a real accounting database (master prompt §4.5).
///
/// The chart of accounts below is a TEST FIXTURE with deliberately non-legal codes; it is not the
/// chart of accounts of Circular 99/2025/TT-BTC (Appendix II), which has not been verified yet.
/// </summary>
public sealed class TestHost : IDisposable
{
    public const string Password = "Test-Password-123";
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "hak-tests");

    /// <summary>
    /// Set HAK_TEST_PG to a PostgreSQL server connection string (no Database=) to run every integration test on PostgreSQL:
    /// each test gets its own database named hak_test_&lt;guid&gt;, dropped afterwards. Unset = SQLite files in %TEMP%.
    /// </summary>
    public static readonly string? PostgresServer = Environment.GetEnvironmentVariable("HAK_TEST_PG");

    public bool IsPostgres => PostgresServer is not null;
    public string ConnectionString { get; } = "";
    private readonly string pgDatabase = "";

    private TestHost(string environment)
    {
        Directory.CreateDirectory(Root);
        DbPath = Path.Combine(Root, $"{Guid.NewGuid():N}.db");
        if (!Path.GetFullPath(DbPath).StartsWith(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("TestDbGuard: test database must live under the test temp folder.");
        if (PostgresServer is not null)
        {
            pgDatabase = $"hak_test_{Guid.NewGuid():N}";
            ConnectionString = new Npgsql.NpgsqlConnectionStringBuilder(PostgresServer) { Database = pgDatabase, Pooling = false }.ConnectionString;
            AdminCommand($"CREATE DATABASE \"{pgDatabase}\" ENCODING 'UTF8'");
        }
        Options = new HakRuntimeOptions { Environment = environment, PasswordIterations = 1_000 }; // low iteration count is test-only
    }

    public string DbPath { get; }
    public FakeClock Clock { get; } = new();
    public HakRuntimeOptions Options { get; }

    public Guid AdminId, PreparerId, ApproverId, AuditorId, CompanyId, RegimeVersionId, FiscalYearId;
    public SessionContext Admin = null!, Preparer = null!, Approver = null!, Auditor = null!;
    public readonly Dictionary<string, Guid> Acc = new();
    public readonly Dictionary<string, Guid> Partner = new();
    public byte[] ExchangeSecret = [];
    public const string ExchangeSource = "PAYROLL01";
    public const string ExchangeKeyId = "k1";

    public Scope NewScope(params IInterceptor[] interceptors) => new(this, Clock, Options, interceptors);

    /// <summary>Provider-specific SQL for the few tests that deliberately tamper with the database.</summary>
    public string DisableAuditUpdateTriggerSql => IsPostgres ? "ALTER TABLE audit_logs DISABLE TRIGGER trg_audit_logs_append_only" : "DROP TRIGGER trg_audit_logs_no_update";
    public string SqlGuidLiteral(Guid id) => IsPostgres ? $"'{id}'::uuid" : $"'{id.ToString().ToUpperInvariant()}'";

    /// <summary>Runs a statement on the server's maintenance database; refuses anything but this test's own database.</summary>
    private void AdminCommand(string sql)
    {
        if (!pgDatabase.StartsWith("hak_test_", StringComparison.Ordinal)) throw new InvalidOperationException("TestDbGuard: only hak_test_* databases.");
        using var admin = new Npgsql.NpgsqlConnection(new Npgsql.NpgsqlConnectionStringBuilder(PostgresServer) { Database = "postgres", Pooling = false }.ConnectionString);
        admin.Open();
        using var cmd = admin.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static async Task<TestHost> CreateAsync(string environment = "Test")
    {
        var h = new TestHost(environment);
        using var s = h.NewScope();
        HakDb.Migrate(s.Db);
        await s.Setup.EnsureReferenceDataAsync();
        await s.Setup.SeedLegacyRolesAsync();   // the fixtures below keep using the finer per-function roles

        h.AdminId = (await s.Setup.BootstrapAdminAsync("admin", "Quản trị", Password)).Id;
        h.PreparerId = (await s.Setup.CreateUserAsync(h.AdminId, "lap", "Kế toán tổng hợp", Password)).Id;
        h.ApproverId = (await s.Setup.CreateUserAsync(h.AdminId, "duyet", "Kế toán trưởng", Password)).Id;
        h.AuditorId = (await s.Setup.CreateUserAsync(h.AdminId, "kiemtoan", "Kiểm toán nội bộ", Password)).Id;

        var version = await s.Setup.CreateRegimeVersionAsync(h.AdminId, "TEST-REGIME", "Chế độ kiểm thử", "TEST-1",
            new DateOnly(2020, 1, 1), null, [new LegalSourceInput("TEST", "TEST-FIXTURE", null, null, null)]);
        h.RegimeVersionId = version.Id;
        h.CompanyId = (await s.Setup.CreateCompanyAsync(h.AdminId, "TEST01", "Công ty kiểm thử", null, "VND", version.Id)).Id;

        await s.Setup.GrantRoleAsync(h.AdminId, h.AdminId, h.CompanyId, "ADMIN");
        await s.Setup.GrantRoleAsync(h.AdminId, h.PreparerId, h.CompanyId, "GL_ACCOUNTANT");
        await s.Setup.GrantRoleAsync(h.AdminId, h.ApproverId, h.CompanyId, "CHIEF_ACCOUNTANT");
        await s.Setup.GrantRoleAsync(h.AdminId, h.AuditorId, h.CompanyId, "INTERNAL_AUDITOR");
        h.Admin = await s.Auth.ResolveContextAsync(h.AdminId, h.CompanyId);
        h.Preparer = await s.Auth.ResolveContextAsync(h.PreparerId, h.CompanyId);
        h.Approver = await s.Auth.ResolveContextAsync(h.ApproverId, h.CompanyId);
        h.Auditor = await s.Auth.ResolveContextAsync(h.AuditorId, h.CompanyId);

        h.FiscalYearId = (await s.Setup.CreateFiscalYearAsync(h.Admin, "2026", new(2026, 1, 1), new(2026, 12, 31))).Id;

        await s.Setup.CreateAccountAsync(h.Admin, "ASSETS", "Tài sản (tổng hợp)", isPostable: false);
        foreach (var (code, name, parent, partner, display, openItem, cash, neg) in new (string, string, string?, bool, BalanceDisplay, OpenItemNature, CashKind, NegativeBalancePolicy)[]
                 {
                     ("CASH", "Tiền mặt", "ASSETS", false, BalanceDisplay.Net, OpenItemNature.None, CashKind.Cash, NegativeBalancePolicy.Block),
                     ("BANK", "Tiền gửi ngân hàng", "ASSETS", false, BalanceDisplay.Net, OpenItemNature.None, CashKind.Bank, NegativeBalancePolicy.Warn),
                     ("BANK2", "Tiền gửi ngân hàng 2", "ASSETS", false, BalanceDisplay.Net, OpenItemNature.None, CashKind.Bank, NegativeBalancePolicy.None),
                     ("AR", "Phải thu khách hàng", "ASSETS", true, BalanceDisplay.GrossByPartner, OpenItemNature.Receivable, CashKind.None, NegativeBalancePolicy.None),
                     ("AP", "Phải trả người bán", null, true, BalanceDisplay.GrossByPartner, OpenItemNature.Payable, CashKind.None, NegativeBalancePolicy.None),
                     ("EQUITY", "Vốn chủ sở hữu", null, false, BalanceDisplay.Net, OpenItemNature.None, CashKind.None, NegativeBalancePolicy.None),
                     ("REV", "Doanh thu", null, false, BalanceDisplay.Net, OpenItemNature.None, CashKind.None, NegativeBalancePolicy.None),
                     ("EXP", "Chi phí", null, false, BalanceDisplay.Net, OpenItemNature.None, CashKind.None, NegativeBalancePolicy.None),
                     ("RESULT", "Xác định kết quả", null, false, BalanceDisplay.Net, OpenItemNature.None, CashKind.None, NegativeBalancePolicy.None),
                     ("RETAINED", "Lợi nhuận chưa phân phối", null, false, BalanceDisplay.Net, OpenItemNature.None, CashKind.None, NegativeBalancePolicy.None),
                 })
            h.Acc[code] = (await s.Setup.CreateAccountAsync(h.Admin, code, name, parent, true, partner, display, openItem, cash, neg)).Id;
        h.Acc["ASSETS"] = s.Db.Accounts.Single(a => a.Code == "ASSETS").Id;

        h.Partner["KH01"] = (await s.Setup.CreatePartnerAsync(h.Admin, "KH01", "Khách hàng 1", PartnerRoles.Customer)).Id;
        h.Partner["KH02"] = (await s.Setup.CreatePartnerAsync(h.Admin, "KH02", "Khách hàng 2", PartnerRoles.Customer)).Id;
        h.Partner["NCC01"] = (await s.Setup.CreatePartnerAsync(h.Admin, "NCC01", "Nhà cung cấp 1", PartnerRoles.Supplier)).Id;

        h.ExchangeSecret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        s.Db.ExchangePartnerKeys.Add(new ExchangePartnerKey
        {
            Id = Ids.New(), CompanyId = h.CompanyId, SourceSystem = ExchangeSource, KeyId = ExchangeKeyId,
            SecretBase64 = Convert.ToBase64String(h.ExchangeSecret),
        });
        await s.Db.SaveChangesAsync();
        return h;
    }

    public VoucherInput Sale(decimal amount, DateOnly? date = null, string partner = "KH01", string? sourceId = null)
    {
        var d = date ?? new DateOnly(2026, 3, 10);
        return new VoucherInput(VoucherTypes.General, d, d, "Bán hàng chưa thu tiền",
        [
            new VoucherLineInput(Acc["AR"], Side.Debit, amount, Partner[partner]),
            new VoucherLineInput(Acc["REV"], Side.Credit, amount),
        ], SourceSystem: sourceId is null ? null : "TEST", SourceId: sourceId);
    }

    /// <summary>Draft → Submitted (preparer) → Approved (approver) → Posted (approver).</summary>
    public async Task<Voucher> PostedAsync(Scope s, VoucherInput input)
    {
        var v = await s.Engine.CreateDraftAsync(Preparer, input);
        await s.Engine.SubmitAsync(Preparer, v.Id);
        await s.Engine.ApproveAsync(Approver, v.Id);
        await s.Engine.PostAsync(Approver, v.Id);
        return v;
    }

    public void Dispose()
    {
        if (IsPostgres)
        {
            try { AdminCommand($"DROP DATABASE IF EXISTS \"{pgDatabase}\" WITH (FORCE)"); }
            catch (Npgsql.NpgsqlException) { /* best effort */ }
            return;
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { DbPath, DbPath + "-wal", DbPath + "-shm" })
            try { File.Delete(f); } catch (IOException) { /* best effort */ }
    }
}
