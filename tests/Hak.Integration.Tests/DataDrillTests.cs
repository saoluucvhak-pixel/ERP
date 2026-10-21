using Hak.Application.Backup;
using Hak.Application.Setup;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Vouchers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>
/// Diễn tập trọn vòng bằng bộ dữ liệu mẫu chuyển từ FAST (Fixtures/MauFast): nhập hệ thống TK TT99, đối tượng, số dư đầu kỳ,
/// chứng từ 2025–2026 → duyệt, ghi sổ → sao lưu mã hóa → khôi phục sang tệp khác → số liệu khôi phục phải trùng từng tài khoản.
/// </summary>
public class DataDrillTests
{
    private const string Pass = "dien-tap-sao-luu-2026";

    private static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        var path = Path.Combine(dir.FullName, "Fixtures", "MauFast", name);
        Assert.True(File.Exists(path), $"Thiếu tệp mẫu {path}");
        return path;
    }

    /// <summary>CSV có ngoặc kép (diễn giải chứa dấu phẩy); bỏ dòng tiêu đề và dòng trống; mỗi phần tử là (số dòng, các ô).</summary>
    private static List<(int Line, string[] Cells)> Csv(string name, int minCells)
    {
        var rows = new List<(int, string[])>();
        var lines = File.ReadAllLines(Fixture(name), System.Text.Encoding.UTF8);
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = new List<string>();
            var cur = new System.Text.StringBuilder();
            var quoted = false;
            foreach (var ch in lines[i])
            {
                if (ch == '"') quoted = !quoted;
                else if (ch == ',' && !quoted) { cells.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            cells.Add(cur.ToString());
            while (cells.Count < minCells) cells.Add("");
            rows.Add((i + 1, cells.ToArray()));
        }
        return rows;
    }

    private static string? N(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static async Task<Dictionary<string, (decimal Debit, decimal Credit)>> Totals(HakDbContext db)
    {
        var accounts = await db.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);
        var lines = await db.JournalLines.AsNoTracking().Select(l => new { l.AccountId, l.Side, l.Amount }).ToListAsync();
        return lines.GroupBy(l => accounts[l.AccountId]).ToDictionary(g => g.Key,
            g => (g.Where(l => l.Side == Side.Debit).Sum(l => l.Amount), g.Where(l => l.Side == Side.Credit).Sum(l => l.Amount)));
    }

    [Fact]
    public async Task SampleData_Import_Post_Backup_Restore_GivesIdenticalBalances()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        await s.Setup.CreateFiscalYearAsync(h.Admin, "2025", new(2025, 1, 1), new(2025, 12, 31));

        var accounts = Csv("he-thong-tk.csv", 10).Select(r => new AccountImportRow(r.Line, N(r.Cells[0]), N(r.Cells[1]), N(r.Cells[2]), N(r.Cells[3]), N(r.Cells[4]),
            N(r.Cells[5]), N(r.Cells[6]), N(r.Cells[7]), N(r.Cells[8]), N(r.Cells[9]))).ToList();
        var acc = await s.CatalogImport.ImportAccountsAsync(h.Admin, accounts, commit: true);
        Assert.True(acc.Committed, string.Join("; ", acc.Issues.Select(i => $"d{i.Line} {i.Field}: {i.Message}")));

        var partners = Csv("doi-tuong.csv", 4).Select(r => new PartnerImportRow(r.Line, N(r.Cells[0]), N(r.Cells[1]), N(r.Cells[2]), N(r.Cells[3]))).ToList();
        var par = await s.CatalogImport.ImportPartnersAsync(h.Admin, partners, commit: true);
        Assert.True(par.Committed, string.Join("; ", par.Issues.Select(i => $"d{i.Line} {i.Field}: {i.Message}")));

        var opening = Csv("so-du-dau.csv", 5).Select(r => new OpeningImportRow(r.Line, N(r.Cells[0]), N(r.Cells[1]), N(r.Cells[2]), N(r.Cells[3]), Description: N(r.Cells[4]))).ToList();
        var open = await s.CatalogImport.ImportOpeningBalancesAsync(h.Preparer, new(2025, 1, 1), null, opening, commit: true);
        Assert.True(open.Committed, string.Join("; ", open.Issues.Select(i => $"d{i.Line} {i.Field}: {i.Message}")));
        Assert.Equal(open.TotalDebit, open.TotalCredit);

        var expectedVouchers = 0;
        foreach (var file in new[] { "chung-tu-2025.csv", "chung-tu-2026.csv" })
        {
            var csv = Csv(file, 12);
            expectedVouchers += csv.Select(r => r.Cells[0]).Distinct().Count();
            var rows = csv.Select(r => new VoucherImportRow(r.Line, N(r.Cells[0]), N(r.Cells[1]), N(r.Cells[2]), N(r.Cells[3]), N(r.Cells[4]), N(r.Cells[5]),
                N(r.Cells[6]), N(r.Cells[7]), N(r.Cells[8]), N(r.Cells[9]), N(r.Cells[10]), N(r.Cells[11]))).ToList();
            var imp = await s.CatalogImport.ImportVouchersAsync(h.Preparer, rows, commit: true);
            Assert.True(imp.Committed, $"{file}: " + string.Join("; ", imp.Issues.Take(15).Select(i => $"d{i.Line} {i.Field}: {i.Message}")));
            Assert.Equal(imp.TotalDebit, imp.TotalCredit);
        }
        var submitted = await s.CatalogImport.SubmitImportedAsync(h.Preparer);
        Assert.Empty(submitted.Failures);
        var posted = await s.CatalogImport.ApproveAndPostImportedAsync(h.Approver);
        Assert.True(posted.Failures.Count == 0, string.Join("; ", posted.Failures.Take(10)));
        Assert.Equal(expectedVouchers, posted.Done);

        var before = await Totals(s.Db);
        Assert.NotEmpty(before);
        Assert.Equal(before.Sum(x => x.Value.Debit), before.Sum(x => x.Value.Credit));

        if (h.IsPostgres) return;   // sao lưu tệp chỉ có ở chế độ SQLite
        var dir = Path.Combine(TestHost.Root, $"drill-{Guid.NewGuid():N}");
        var svc = new BackupService(s.Db, s.Audit, h.Clock, new BackupOptions { Directory = dir, Iterations = 1_000 });
        var backup = await svc.CreateAsync(h.AdminId, Pass, "Diễn tập: dữ liệu mẫu FAST 2025–2026");
        var check = await svc.VerifyAsync(h.AdminId, backup.FileName, Pass);
        Assert.True(check.IsValid);
        Assert.Equal(expectedVouchers + 1, check.Companies.Single().Vouchers);   // + phiếu số dư đầu kỳ

        var restored = await svc.VerifyAsync(h.AdminId, backup.FileName, Pass, restore: true);
        Assert.NotNull(restored.RestoredPath);
        await using (var copy = new HakDbContext(HakDb.SqliteOptions(restored.RestoredPath!)))
        {
            var after = await Totals(copy);
            Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
            foreach (var (code, v) in before) Assert.Equal(v, after[code]);
            Assert.Equal(await s.Db.Vouchers.CountAsync(), await copy.Vouchers.CountAsync());
            Assert.Equal(await s.Db.JournalEntries.CountAsync(), await copy.JournalEntries.CountAsync());
        }
        SqliteConnection.ClearAllPools();
    }
}
