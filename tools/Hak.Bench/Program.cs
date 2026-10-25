// HAK performance benchmark (docs/09). Builds a synthetic company in a THROWAWAY database and times posting and reports.
// SQLite: a new file under %TEMP%\hak-bench (deleted afterwards unless --keep).
// PostgreSQL: --postgres uses the server in HAK_TEST_PG and a new hak_bench_<guid> database, dropped afterwards.
// Never point this at a real accounting database. Account codes are test codes, not a legal chart of accounts.
using System.Diagnostics;
using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Backup;
using Hak.Application.Common;
using Hak.Application.Inventory;
using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Application.Setup;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Vouchers;

var vouchers = IntArg("--vouchers", 20_000);
var partnerCount = IntArg("--partners", 500);
var stockMovements = IntArg("--stock", 0);   // số lần nhập (mỗi lần nhập kèm một lần xuất) trên MỖI kho đo; 0 = bỏ qua phần kho
var users = IntArg("--users", 0);                 // số người dùng ghi sổ ĐỒNG THỜI (0 = bỏ qua); mỗi người --user-vouchers chứng từ
var userVouchers = IntArg("--user-vouchers", 100);
var postgres = args.Contains("--postgres");
var keep = args.Contains("--keep");
int IntArg(string name, int fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1]) : fallback; }

var options = new HakRuntimeOptions { Environment = "Test", PasswordIterations = 1_000 };
var clock = new SystemClock();
string dbPath = "", pgName = "", pgServer = Environment.GetEnvironmentVariable("HAK_TEST_PG") ?? "";
string pgConn = "";
if (postgres)
{
    if (pgServer.Length == 0) { Console.Error.WriteLine("--postgres cần biến HAK_TEST_PG (chuỗi kết nối máy chủ, không có Database=)."); return 2; }
    pgName = $"hak_bench_{Guid.NewGuid():N}";
    PgAdmin($"CREATE DATABASE \"{pgName}\" ENCODING 'UTF8'");
    pgConn = new Npgsql.NpgsqlConnectionStringBuilder(pgServer) { Database = pgName }.ConnectionString;
}
else
{
    var root = Path.Combine(Path.GetTempPath(), "hak-bench");
    Directory.CreateDirectory(root);
    dbPath = Path.Combine(root, $"{Guid.NewGuid():N}.db");
}
var profiler = new SqlProfiler();
var tx = new TxProfiler();
var concurrentPhase = false;   // bộ đo SqlProfiler / TxProfiler không an toàn đa luồng: tắt khi nhiều người ghi đồng thời
HakDbContext NewDb()
{
    Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra = concurrentPhase ? [] : [profiler, tx];
    return new(postgres ? HakDb.PostgresOptions(pgConn, extra) : HakDb.SqliteOptions(dbPath, extra));
}
void PgAdmin(string sql)
{
    using var c = new Npgsql.NpgsqlConnection(new Npgsql.NpgsqlConnectionStringBuilder(pgServer) { Database = "postgres", Pooling = false }.ConnectionString);
    c.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
}

var results = new List<(string Name, string Value)>();
try
{
    Console.WriteLine($"HAK benchmark — {(postgres ? "PostgreSQL" : "SQLite")}, {vouchers:N0} chứng từ, {partnerCount:N0} đối tượng, {Environment.ProcessorCount} CPU");
    Guid adminId, prepId, apprId, companyId;
    SessionContext admin, prep, appr, viewer;
    var acc = new Dictionary<string, Guid>();
    var partners = new List<Guid>();
    using (var db = NewDb())
    {
        HakDb.Migrate(db);
        var audit = new AuditService(db, clock);
        var setup = new SetupService(db, audit, clock, options);
        var auth = new AuthService(db, audit, clock, options);
        await setup.EnsureReferenceDataAsync();
        adminId = (await setup.BootstrapAdminAsync("bench", "Bench", "Bench-Password-1")).Id;
        prepId = (await setup.CreateUserAsync(adminId, "lap", "Lập", "Bench-Password-1")).Id;
        apprId = (await setup.CreateUserAsync(adminId, "duyet", "Duyệt", "Bench-Password-1")).Id;
        var ver = await setup.CreateRegimeVersionAsync(adminId, "BENCH", "Chế độ đo hiệu năng", "B-1", new(2020, 1, 1), null,
            [new LegalSourceInput("BENCH", "BENCH-FIXTURE", null, null, null)]);
        companyId = (await setup.CreateCompanyAsync(adminId, "BENCH01", "Công ty đo hiệu năng", null, "VND", ver.Id)).Id;
        await setup.GrantRoleAsync(adminId, adminId, companyId, "ADMIN");
        await setup.GrantRoleAsync(adminId, prepId, companyId, "GL_ACCOUNTANT");
        await setup.GrantRoleAsync(adminId, apprId, companyId, "CHIEF_ACCOUNTANT");
        admin = await auth.ResolveContextAsync(adminId, companyId);
        prep = await auth.ResolveContextAsync(prepId, companyId);
        appr = await auth.ResolveContextAsync(apprId, companyId);
        viewer = appr;
        await setup.CreateFiscalYearAsync(admin, "2026", new(2026, 1, 1), new(2026, 12, 31));
        await setup.CreateAccountAsync(admin, "ASSETS", "Tài sản (tổng hợp)", isPostable: false);
        acc["CASH"] = (await setup.CreateAccountAsync(admin, "CASH", "Tiền", "ASSETS", cashKind: CashKind.Cash)).Id;
        acc["AR"] = (await setup.CreateAccountAsync(admin, "AR", "Phải thu", "ASSETS", true, true, BalanceDisplay.GrossByPartner, OpenItemNature.Receivable)).Id;
        acc["REV"] = (await setup.CreateAccountAsync(admin, "REV", "Doanh thu")).Id;
        acc["EXP"] = (await setup.CreateAccountAsync(admin, "EXP", "Chi phí")).Id;
        acc["EQ"] = (await setup.CreateAccountAsync(admin, "EQ", "Vốn")).Id;
        for (var i = 1; i <= partnerCount; i++)
            partners.Add((await setup.CreatePartnerAsync(admin, $"KH{i:00000}", $"Khách hàng {i}", PartnerRoles.Customer)).Id);
    }

    // ---- posting: each voucher goes draft → submit → approve → post (4 transactions, as in daily work) ----
    var rnd = new Random(42);
    var sw = Stopwatch.StartNew();
    var chunk = Stopwatch.StartNew();
    var perVoucher = new List<double>();
    {
        await PostAsync(new(VoucherTypes.General, new(2026, 1, 1), new(2026, 1, 1), "Vốn ban đầu",
            [new VoucherLineInput(acc["CASH"], Side.Debit, 10_000_000_000m), new VoucherLineInput(acc["EQ"], Side.Credit, 10_000_000_000m)]));
        for (var n = 0; n < vouchers; n++)
        {
            var date = new DateOnly(2026, 1, 2).AddDays(n * 360 / vouchers);
            var p = partners[rnd.Next(partners.Count)];
            var amount = rnd.Next(1, 5_000) * 1_000m;
            VoucherInput input = (n % 3) switch
            {
                0 => new(VoucherTypes.General, date, date, "Bán hàng", [new(acc["AR"], Side.Debit, amount, p), new(acc["REV"], Side.Credit, amount)]),
                1 => new(VoucherTypes.General, date, date, "Thu tiền", [new(acc["CASH"], Side.Debit, amount, null), new(acc["AR"], Side.Credit, amount, p)]),
                _ => new(VoucherTypes.General, date, date, "Chi phí", [new(acc["EXP"], Side.Debit, amount), new(acc["CASH"], Side.Credit, amount)]),
            };
            var t = Stopwatch.GetTimestamp();
            await PostAsync(input);
            perVoucher.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            if ((n + 1) % 5_000 == 0)
            {
                Console.WriteLine($"  {n + 1,7:N0} chứng từ — {5_000 / chunk.Elapsed.TotalSeconds:N0} chứng từ/giây (đợt gần nhất)");
                chunk.Restart();
            }
        }
    }
    sw.Stop();
    perVoucher.Sort();
    results.Add(("Ghi sổ (lập→gửi→duyệt→ghi sổ)", $"{vouchers / sw.Elapsed.TotalSeconds:N0} chứng từ/giây; trung vị {perVoucher[perVoucher.Count / 2]:N1} ms, p95 {perVoucher[(int)(perVoucher.Count * 0.95)]:N1} ms, p99 {perVoucher[(int)(perVoucher.Count * 0.99)]:N1} ms"));

    // ---- nhiều người dùng đồng thời: mỗi người một chuỗi lập→gửi→duyệt→ghi sổ độc lập, đếm lỗi theo loại ----
    if (users > 0)
    {
        var errors = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        var okCount = 0;
        var perUser = new System.Collections.Concurrent.ConcurrentBag<double>();
        concurrentPhase = true;
        var swUsers = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, users).Select(u => Task.Run(async () =>
        {
            var r = new Random(1000 + u);
            for (var n = 0; n < userVouchers; n++)
            {
                var date = new DateOnly(2026, 6, 1).AddDays(r.Next(0, 28));
                var amount = r.Next(1, 5_000) * 1_000m;
                var t = Stopwatch.GetTimestamp();
                try
                {
                    await PostAsync(new(VoucherTypes.General, date, date, $"Đồng thời U{u}", [new(acc["EXP"], Side.Debit, amount), new(acc["CASH"], Side.Credit, amount)]));
                    Interlocked.Increment(ref okCount); perUser.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
                }
                catch (Exception ex) { errors.AddOrUpdate(ex.GetType().Name + ": " + (ex.Message.Length > 70 ? ex.Message[..70] : ex.Message), 1, (_, c) => c + 1); }
            }
        })));
        swUsers.Stop(); concurrentPhase = false;
        var lat = perUser.OrderBy(x => x).ToList();
        results.Add(($"Ghi sổ đồng thời {users} người × {userVouchers} chứng từ", $"{okCount}/{users * userVouchers} thành công trong {swUsers.Elapsed.TotalSeconds:N1} s = {okCount / swUsers.Elapsed.TotalSeconds:N0} chứng từ/giây; "
            + (lat.Count > 0 ? $"trung vị {lat[lat.Count / 2]:N0} ms, p95 {lat[(int)(lat.Count * 0.95)]:N0} ms; " : "") + (errors.IsEmpty ? "không lỗi" : "lỗi: " + string.Join(" | ", errors.Select(e => $"{e.Value}× {e.Key}")))));
    }

    // One DbContext per step, as the API does per HTTP request.
    async Task PostAsync(VoucherInput input)
    {
        var v = await profiler.Step("1 lập", () => WithEngine(e => e.CreateDraftAsync(prep, input)));
        await profiler.Step("2 gửi duyệt", () => WithEngine(async e => { await e.SubmitAsync(prep, v.Id); return 0; }));
        await profiler.Step("3 duyệt", () => WithEngine(async e => { await e.ApproveAsync(appr, v.Id); return 0; }));
        await profiler.Step("4 ghi sổ", () => WithEngine(async e => { await e.PostAsync(appr, v.Id); return 0; }));
    }
    async Task<T> WithEngine<T>(Func<AccountingEngine, Task<T>> work)
    {
        using var db = NewDb();
        return await work(new AccountingEngine(db, new AuditService(db, clock), clock, options));
    }

    // ---- reports: each timed 3 times on a fresh context, median reported ----
    async Task Time(string name, Func<HakDbContext, Task<string>> work)
    {
        var times = new List<double>();
        var info = "";
        for (var i = 0; i < 3; i++)
        {
            using var db = NewDb();
            var t = Stopwatch.GetTimestamp();
            info = await work(db);
            times.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        }
        times.Sort();
        results.Add((name, $"{times[1]:N0} ms ({info})"));
    }
    // ---- kho: lịch sử phiếu dài dần; đo thời gian ghi sổ MỘT phiếu xuất khi kho đã có sẵn nhiều biến động ----
    async Task StockBenchAsync()
    {
        using (var db = NewDb())
            await new SetupService(db, new AuditService(db, clock), clock, options).CreateAccountAsync(admin, "INV", "Hàng tồn kho", "ASSETS");
        foreach (var (label, method) in new[] { ("bình quân tức thời", CostingMethod.MovingAverage), ("FIFO", CostingMethod.Fifo) })
        {
            Guid wh, item;
            using (var db = NewDb())
            {
                var stock = new StockService(db, new AccountingEngine(db, new AuditService(db, clock), clock, options), new AuditService(db, clock), clock);
                wh = (await stock.CreateWarehouseAsync(admin, $"K{(int)method}", $"Kho đo {label}")).Id;
                await stock.SetCostingPolicyAsync(admin, wh, method, new(2026, 1, 1), "Đo hiệu năng");
                item = (await stock.CreateItemAsync(admin, $"V{(int)method}", $"Vật tư đo {label}", "tấn", "INV")).Id;
            }
            var issueMs = new List<double>();
            var checkpoints = new List<string>();
            var swStock = Stopwatch.StartNew();
            for (var n = 0; n < stockMovements; n++)
            {
                var date = new DateOnly(2026, 1, 2).AddDays(n * 360 / stockMovements);
                var value = rnd.Next(5, 20) * 100_000m;
                await StockDocAsync(StockDocumentType.Receipt, wh, item, date, 10, value, acc["EQ"]);
                var t0 = Stopwatch.GetTimestamp();
                await StockDocAsync(StockDocumentType.Issue, wh, item, date, 7, null, acc["EXP"]);
                issueMs.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                if ((n + 1) % Math.Max(1, stockMovements / 4) == 0)
                {
                    var last = issueMs.TakeLast(Math.Min(20, issueMs.Count)).OrderBy(x => x).ToList();
                    checkpoints.Add($"sau {(n + 1) * 2:N0} biến động: {last[last.Count / 2]:N0} ms");
                }
            }
            results.Add(($"Kho {label}: ghi sổ + tạo 1 phiếu xuất theo độ dài lịch sử (trung vị 20 phiếu gần nhất)", string.Join("; ", checkpoints)));
            results.Add(($"Kho {label}: tổng thời gian {stockMovements:N0} nhập + {stockMovements:N0} xuất", $"{swStock.Elapsed.TotalSeconds:N1} s"));
        }
        await Time("Nhập–xuất–tồn cả năm", async db => (await new StockService(db, new AccountingEngine(db, new AuditService(db, clock), clock, options), new AuditService(db, clock), clock)
            .SummaryAsync(viewer, new(2026, 1, 1), new(2026, 12, 31))).Count + " dòng");
        await Time("Đối chiếu sổ kho ↔ sổ cái", async db => (await new StockService(db, new AccountingEngine(db, new AuditService(db, clock), clock, options), new AuditService(db, clock), clock)
            .ReconcileAsync(viewer, new(2026, 12, 31))).Count + " TK");
        await Time("Tuổi nợ mọi TK công nợ", async db => (await new SettlementService(db, new AuditService(db, clock), clock).AgingAllAsync(viewer, new(2026, 12, 31))).Count + " TK");
        await Time("Đối chiếu sổ chi tiết công nợ ↔ sổ cái", async db => (await new SettlementService(db, new AuditService(db, clock), clock).ReconcileAsync(viewer, new(2026, 12, 31))).Rows.Count + " dòng");

        async Task StockDocAsync(StockDocumentType type, Guid whId, Guid itemId, DateOnly date, decimal qty, decimal? value, Guid counter)
        {
            StockDocument d;
            using (var db = NewDb())
            {
                var stock = new StockService(db, new AccountingEngine(db, new AuditService(db, clock), clock, options), new AuditService(db, clock), clock);
                d = await stock.CreateDocumentAsync(prep, new StockDocumentInput(type, whId, date, date, type == StockDocumentType.Receipt ? "Nhập" : "Xuất", [new StockLineInput(itemId, qty, value, counter)]));
            }
            using (var db = NewDb())
            {
                var stock = new StockService(db, new AccountingEngine(db, new AuditService(db, clock), clock, options), new AuditService(db, clock), clock);
                await stock.PostDocumentAsync(appr, d.Id);
            }
        }
    }

    var aPartner = partners[0];
    await Time("Bảng cân đối số phát sinh cả năm", async db => (await new LedgerQueryService(db).TrialBalanceAsync(viewer, new(2026, 1, 1), new(2026, 12, 31))).Rows.Count + " dòng");
    await Time("BCĐPS cả năm, cộng dồn TK cha", async db => (await new LedgerQueryService(db).TrialBalanceAsync(viewer, new(2026, 1, 1), new(2026, 12, 31), rollup: true)).Groups.Count + " nhóm");
    await Time("Sổ chi tiết TK phải thu cả năm", async db => (await new LedgerQueryService(db).AccountLedgerAsync(viewer, acc["AR"], new(2026, 1, 1), new(2026, 12, 31))).Rows.Count + " dòng");
    await Time("Sổ chi tiết phải thu 1 đối tượng", async db => (await new LedgerQueryService(db).AccountLedgerAsync(viewer, acc["AR"], new(2026, 1, 1), new(2026, 12, 31), aPartner)).Rows.Count + " dòng");
    await Time("Sổ Nhật ký chung 1 tháng", async db => (await new LedgerQueryService(db).GeneralJournalAsync(viewer, new(2026, 6, 1), new(2026, 6, 30))).Count + " dòng");
    await Time("Khoản mở phải thu 1 đối tượng", async db => (await new SettlementService(db, new AuditService(db, clock), clock).OpenItemsAsync(viewer, acc["AR"], aPartner, new(2026, 12, 31))).Count + " khoản");
    await Time("Số dư TK tiền tại cuối năm", async db => (await new LedgerQueryService(db).AccountBalanceAsync(viewer, acc["CASH"], new(2026, 12, 31))).ToString("N0") + " đ");
    await Time("Kiểm tra chuỗi nhật ký kiểm toán", async db => (await new AuditService(db, clock).VerifyChainAsync(companyId)) is { } broken ? $"HỎNG tại {broken}" : "nguyên vẹn");

    if (stockMovements > 0) await StockBenchAsync();

    if (!postgres)
    {
        results.Add(("Dung lượng CSDL", $"{new FileInfo(dbPath).Length / 1024.0 / 1024.0:N1} MB"));
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            // The bundled SQLite has no dbstat: row counts per table plus the size of the audit JSON payloads.
            var parts = new List<string>();
            foreach (var table in new[] { "vouchers", "voucher_lines", "voucher_history", "journal_entries", "journal_lines", "audit_logs" })
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
                parts.Add($"{table} {Convert.ToInt64(cmd.ExecuteScalar()):N0}");
            }
            cmd.CommandText = "SELECT COALESCE(SUM(LENGTH(data_json)), 0) FROM audit_logs";
            parts.Add($"JSON nhật ký {Convert.ToInt64(cmd.ExecuteScalar()) / 1024.0 / 1024.0:N1} MB");
            results.Add(("Số dòng theo bảng", string.Join("; ", parts)));
        }
        var backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, Path.GetFileNameWithoutExtension(dbPath) + "-backups");
        var bo = new BackupOptions { Directory = backupDir };
        using var db = NewDb();
        var backup = new BackupService(db, new AuditService(db, clock), clock, bo);
        var t = Stopwatch.GetTimestamp();
        var b = await backup.CreateAsync(adminId, "bench-backup-passphrase");
        results.Add(("Sao lưu mã hóa", $"{Stopwatch.GetElapsedTime(t).TotalSeconds:N1} s ({b.Size / 1024.0 / 1024.0:N1} MB)"));
        t = Stopwatch.GetTimestamp();
        var check = await backup.VerifyAsync(adminId, b.FileName, "bench-backup-passphrase");
        results.Add(("Kiểm tra bản sao lưu (giải mã + integrity + chuỗi kiểm toán)", $"{Stopwatch.GetElapsedTime(t).TotalSeconds:N1} s ({(check.IsValid ? "hợp lệ" : "KHÔNG hợp lệ")})"));
        if (!keep) try { Directory.Delete(backupDir, true); } catch (IOException) { }
    }

    Console.WriteLine();
    Console.WriteLine("| Phép đo | Kết quả |");
    Console.WriteLine("|---|---|");
    foreach (var (name, value) in results) Console.WriteLine($"| {name} | {value} |");
    return 0;
}
finally
{
    if (args.Contains("--profile")) { profiler.Print(); tx.Print(); }
    if (postgres) { if (!keep) try { PgAdmin($"DROP DATABASE IF EXISTS \"{pgName}\" WITH (FORCE)"); } catch (Npgsql.NpgsqlException) { } }
    else if (!keep)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" }) try { File.Delete(f); } catch (IOException) { }
    }
}
