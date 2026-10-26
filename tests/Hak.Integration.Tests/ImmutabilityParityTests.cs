using System.Text.RegularExpressions;
using Hak.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Hak.Integration.Tests;

/// <summary>
/// DB-08: trigger bất biến của SQLite và PostgreSQL phải bảo vệ cùng các bảng, cùng thao tác, cùng các cột "đóng băng".
/// Hai bên dùng tên trigger khác nhau (SQLite không gộp được "UPDATE OR DELETE", PostgreSQL đặt điều kiện trong hàm), nên so theo
/// phạm vi bảo vệ lấy từ script migration của cả hai provider — sinh ngay trong test, không cần máy chủ PostgreSQL.
/// </summary>
public partial class ImmutabilityParityTests
{
    private sealed record Trigger(string Name, string Table, HashSet<string> Ops, HashSet<string> Columns);

    [GeneratedRegex(@"CREATE\s+(?:OR\s+REPLACE\s+)?TRIGGER\s+""?(\w+)""?\s+(?:BEFORE|AFTER|INSTEAD\s+OF)\s+(.*?)\s+ON\s+""?(\w+)""?(.*?)(?:END;|EXECUTE\s+FUNCTION\s+(\w+)\(\);)|DROP\s+TRIGGER\s+(?:IF\s+EXISTS\s+)?""?(\w+)""?", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TriggerPattern();

    [GeneratedRegex(@"CREATE\s+OR\s+REPLACE\s+FUNCTION\s+(\w+)\(\).*?\$\$(.*?)\$\$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FunctionPattern();

    [GeneratedRegex(@"\bNEW\.(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex NewColumn();

    [GeneratedRegex(@"\b(INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Op();

    private static string Script(DbContextOptions<HakDbContext> options)
    {
        using var db = new HakDbContext(options);
        return db.GetService<IMigrator>().GenerateScript();
    }

    /// <summary>Trigger còn tồn tại sau toàn bộ script (CREATE rồi DROP theo đúng thứ tự), với các cột NEW.x mà điều kiện của nó kiểm.</summary>
    private static Dictionary<string, Trigger> LiveTriggers(string script)
    {
        var functions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match f in FunctionPattern().Matches(script)) functions[f.Groups[1].Value] = f.Groups[2].Value;   // định nghĩa cuối cùng thắng
        var live = new Dictionary<string, Trigger>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in TriggerPattern().Matches(script))
        {
            if (m.Groups[6].Success) { live.Remove(m.Groups[6].Value); continue; }
            var body = m.Groups[4].Value + (m.Groups[5].Success && functions.TryGetValue(m.Groups[5].Value, out var fn) ? fn : "");
            live[m.Groups[1].Value] = new Trigger(m.Groups[1].Value, m.Groups[3].Value.ToLowerInvariant(),
                Op().Matches(m.Groups[2].Value).Select(o => o.Value.ToUpperInvariant()).ToHashSet(),
                NewColumn().Matches(body).Select(c => c.Groups[1].Value.ToLowerInvariant()).ToHashSet());
        }
        return live;
    }

    private static readonly Dictionary<string, Trigger> Sqlite = LiveTriggers(Script(HakDb.SqliteOptions(":memory:")));
    private static readonly Dictionary<string, Trigger> Postgres = LiveTriggers(Script(HakDb.PostgresOptions("Host=127.0.0.1;Database=script_only")));

    private static SortedSet<string> Coverage(Dictionary<string, Trigger> triggers)
        => new(triggers.Values.SelectMany(t => t.Ops.Select(o => $"{t.Table}:{o}")), StringComparer.Ordinal);

    private static SortedSet<string> FrozenColumns(Dictionary<string, Trigger> triggers, string table)
        => new(triggers.Values.Where(t => t.Table == table && t.Ops.Contains("UPDATE")).SelectMany(t => t.Columns), StringComparer.Ordinal);

    [Fact]
    public void BothProviders_ProtectTheSameTablesAndOperations()
    {
        Assert.True(Sqlite.Count > 50 && Postgres.Count > 40, $"Chỉ đọc được {Sqlite.Count} / {Postgres.Count} trigger: biểu thức đọc script đã hỏng.");
        var sqlite = Coverage(Sqlite);
        var postgres = Coverage(Postgres);
        Assert.True(sqlite.SetEquals(postgres),
            "Lệch phạm vi trigger bất biến.\nChỉ PostgreSQL: " + string.Join(", ", postgres.Except(sqlite)) + "\nChỉ SQLite: " + string.Join(", ", sqlite.Except(postgres)));
        Assert.Contains("file_attachments:UPDATE", sqlite);
        Assert.Contains("file_attachments:DELETE", sqlite);
        Assert.Contains("depreciation_entry_splits:UPDATE", sqlite);
        Assert.Contains("depreciation_entry_splits:DELETE", sqlite);
        // DATA-01 / EIN-07: bằng chứng trích trước chiết khấu và thông báo hóa đơn điện tử.
        foreach (var t in new[] { "discount_accruals", "e_invoice_error_notice_lines", "e_invoice_risk_notice_lines" })
        { Assert.Contains($"{t}:UPDATE", sqlite); Assert.Contains($"{t}:DELETE", sqlite); }
        foreach (var t in new[] { "e_invoice_error_notices", "e_invoice_risk_notices" }) Assert.Contains($"{t}:DELETE", sqlite);
        Assert.Contains("e_invoice_outbox:UPDATE", sqlite);
        Assert.Contains("e_invoice_documents:UPDATE", sqlite);
    }

    [Theory]
    [InlineData("long_term_assets")]
    [InlineData("depreciation_entries")]
    [InlineData("vouchers")]
    [InlineData("loan_contracts")]
    [InlineData("stock_valuations")]
    [InlineData("settlement_allocations")]
    public void ConditionalTriggers_CheckTheSameColumns(string table)
    {
        var sqlite = FrozenColumns(Sqlite, table);
        var postgres = FrozenColumns(Postgres, table);
        Assert.NotEmpty(sqlite);
        Assert.True(sqlite.SetEquals(postgres), $"{table}: chỉ SQLite kiểm [{string.Join(", ", sqlite.Except(postgres))}], chỉ PostgreSQL kiểm [{string.Join(", ", postgres.Except(sqlite))}].");
    }

    [Fact]
    public void AssetTriggers_FreezeEveryDepreciationColumn_ExceptWhatTheAppMayChange()
    {
        // Cùng quy tắc với LedgerImmutabilityInterceptor: thẻ đã ghi tăng chỉ đổi được trạng thái, ghi giảm và kỳ ghi sổ.
        var asset = FrozenColumns(Sqlite, "long_term_assets");
        foreach (var c in new[] { "declining_factor", "schedule_end_date", "total_units", "unit_name", "original_cost", "useful_life_months", "method" })
            Assert.Contains(c, asset);
        Assert.DoesNotContain("allocation_frequency", asset);   // AssetService.SetFrequencyAsync
        // Số khấu hao: mọi cột trừ id và voided_at (hủy một lần).
        using var db = new HakDbContext(HakDb.SqliteOptions(":memory:"));
        var columns = db.Model.FindEntityType(typeof(Hak.Domain.Assets.DepreciationEntry))!.GetProperties().Select(p => p.GetColumnName()).ToHashSet();
        columns.ExceptWith(["id", "voided_at"]);
        Assert.Subset(FrozenColumns(Sqlite, "depreciation_entries").ToHashSet(), columns);
    }

    /// <summary>Script khớp database thật: trigger trong SQLite (và PostgreSQL khi có HAK_TEST_PG) đúng bằng tập đọc từ script.</summary>
    [Fact]
    public async Task MigratedDatabase_HasExactlyTheScriptedTriggers()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var expected = h.IsPostgres ? Postgres : Sqlite;
        var actual = h.IsPostgres
            ? await s.Db.Database.SqlQuery<string>($"SELECT tgname AS \"Value\" FROM pg_trigger WHERE NOT tgisinternal").ToListAsync()
            : await s.Db.Database.SqlQuery<string>($"SELECT name AS \"Value\" FROM sqlite_master WHERE type = 'trigger'").ToListAsync();
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }
}
