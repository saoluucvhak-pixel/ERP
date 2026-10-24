using Hak.Application.Attachments;
using Hak.Application.Backup;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Vouchers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Encrypted backup / verify / restore (SQLite mode). Low PBKDF2 iteration count is test-only.</summary>
public class BackupTests
{
    private const string Pass = "Mat-khau-sao-luu-2026";
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    [Fact]
    public void Container_RoundTrips_AndRejectsWrongPassphrase_Tampering_Truncation()
    {
        var data = new byte[3 * 1000 + 17];
        Random.Shared.NextBytes(data);
        using var enc = new MemoryStream();
        var header = BackupFile.Encrypt(new MemoryStream(data), enc, Pass, "SQLite", "test", DateTime.UtcNow, iterations: 1_000, chunkSize: 1_000);
        Assert.Equal(data.Length, header.PlaintextLength);
        var bytes = enc.ToArray();

        using var dec = new MemoryStream();
        BackupFile.Decrypt(new MemoryStream(bytes), dec, Pass);
        Assert.Equal(data, dec.ToArray());

        Assert.Equal(ErrorCodes.BackupInvalid, Assert.Throws<DomainException>(() => BackupFile.Decrypt(new MemoryStream(bytes), Stream.Null, "sai-mat-khau-123")).Code);
        var tampered = (byte[])bytes.Clone();
        tampered[^40] ^= 0x01;
        Assert.Equal(ErrorCodes.BackupInvalid, Assert.Throws<DomainException>(() => BackupFile.Decrypt(new MemoryStream(tampered), Stream.Null, Pass)).Code);
        var truncated = bytes[..(bytes.Length - 1_100)];   // drops the last chunk
        Assert.Equal(ErrorCodes.BackupInvalid, Assert.Throws<DomainException>(() => BackupFile.Decrypt(new MemoryStream(truncated), Stream.Null, Pass)).Code);
        var otherHeader = (byte[])bytes.Clone();
        var noteAt = System.Text.Encoding.UTF8.GetString(otherHeader).IndexOf("\"test\"", StringComparison.Ordinal);
        otherHeader[noteAt + 1] = (byte)'T';                // header edits break every chunk's associated data
        Assert.Equal(ErrorCodes.BackupInvalid, Assert.Throws<DomainException>(() => BackupFile.Decrypt(new MemoryStream(otherHeader), Stream.Null, Pass)).Code);
    }

    [Fact]
    public async Task Backup_Verify_Restore_OnlySystemAdmin_NeverTouchesLiveDatabase()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var dir = Path.Combine(TestHost.Root, $"backups-{Guid.NewGuid():N}");
        var svc = new BackupService(s.Db, s.Audit, h.Clock, new BackupOptions { Directory = dir, Iterations = 1_000 });
        if (h.IsPostgres)
        {
            Assert.Equal(ErrorCodes.NotSupported, await CodeOf(() => svc.CreateAsync(h.AdminId, Pass)));
            return;
        }
        await h.PostedAsync(s, h.Sale(1_250_000));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => svc.CreateAsync(h.ApproverId, Pass)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.CreateAsync(h.AdminId, "ngan")));

        static async Task<SortedDictionary<string, decimal>> BalancesAsync(HakDbContext db)
        {
            var lines = await db.JournalLines.AsNoTracking().Select(l => new { l.AccountId, l.PartnerId, l.Side, l.Amount }).ToListAsync();
            return new SortedDictionary<string, decimal>(lines.GroupBy(l => $"{l.AccountId}|{l.PartnerId}").ToDictionary(g => g.Key, g => g.Sum(l => l.Side == Side.Debit ? l.Amount : -l.Amount)));
        }
        var atBackup = await BalancesAsync(s.Db);                       // số dư theo (tài khoản, đối tượng) tại thời điểm sao lưu
        var allocationsAtBackup = await s.Db.SettlementAllocations.CountAsync();
        var backup = await svc.CreateAsync(h.AdminId, Pass, "Trước khi khóa sổ quý 1");
        Assert.Equal(backup.FileName, Assert.Single(await svc.ListAsync(h.AdminId)).FileName);

        var check = await svc.VerifyAsync(h.AdminId, backup.FileName, Pass);
        Assert.True(check.IsValid);
        Assert.Equal((1, 1), (check.Companies.Single().Vouchers, check.Companies.Single().JournalEntries));
        Assert.Null(check.RestoredPath);
        Assert.Equal(ErrorCodes.BackupInvalid, await CodeOf(() => svc.VerifyAsync(h.AdminId, backup.FileName, "mat-khau-sai-123")));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.VerifyAsync(h.AdminId, "..\\hak.db", Pass)));

        // Work continues after the backup; restoring gives the state at backup time in a separate file.
        await h.PostedAsync(s, h.Sale(2_000_000));
        var restored = await svc.VerifyAsync(h.AdminId, backup.FileName, Pass, restore: true);
        Assert.NotNull(restored.RestoredPath);
        Assert.NotEqual(Path.GetFullPath(h.DbPath), Path.GetFullPath(restored.RestoredPath!));
        await using (var copy = new HakDbContext(HakDb.SqliteOptions(restored.RestoredPath!)))
        {
            Assert.Equal(1, await copy.JournalEntries.CountAsync());
            // Diễn tập khôi phục: số dư theo tài khoản / đối tượng và phân bổ công nợ của bản khôi phục khớp đúng trạng thái lúc sao lưu.
            Assert.Equal(atBackup, await BalancesAsync(copy));
            Assert.Equal(allocationsAtBackup, await copy.SettlementAllocations.CountAsync());
        }
        SqliteConnection.ClearAllPools();
        Assert.Equal(2, await s.Db.JournalEntries.CountAsync());
        Assert.Contains(await s.Db.AuditLogs.Where(a => a.CompanyId == Guid.Empty).Select(a => a.Action).ToListAsync(), a => a == "backup.restore");
        Assert.Null(await s.Audit.VerifyChainAsync(Guid.Empty));
        Assert.DoesNotContain(Pass, string.Join("|", await s.Db.AuditLogs.Select(a => a.DataJson ?? "").ToListAsync()));
    }

    [Fact]
    public async Task Backup_IncludesAttachments_RestoresThem_AndDetectsTampering_AndReadsLegacyBackups()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        if (h.IsPostgres) return;
        var dir = Path.Combine(TestHost.Root, $"backups-{Guid.NewGuid():N}");
        var store = new AttachmentStore(Path.Combine(TestHost.Root, $"att-{Guid.NewGuid():N}"));
        var photo = new byte[] { 0xFF, 0xD8, 0xFF, 1, 2, 3, 4, 5 };
        await store.WriteAsync("2026/10/a.jpg", photo);
        await store.WriteAsync("2026/10/b.jpg", [0xFF, 0xD8, 0xFF, 9]);
        var svc = new BackupService(s.Db, s.Audit, h.Clock, new BackupOptions { Directory = dir, Iterations = 1_000 }, store);

        var backup = await svc.CreateAsync(h.AdminId, Pass);
        var restored = await svc.VerifyAsync(h.AdminId, backup.FileName, Pass, restore: true);
        Assert.True(restored.IsValid);
        Assert.Equal(2, restored.AttachmentCount);
        Assert.NotNull(restored.RestoredAttachmentsPath);
        Assert.Equal(photo, await File.ReadAllBytesAsync(Path.Combine(restored.RestoredAttachmentsPath!, "2026", "10", "a.jpg")));
        SqliteConnection.ClearAllPools();

        // Legacy backup (plain SQLite file inside the envelope) is still verifiable.
        var legacyDb = Path.Combine(dir, "legacy.db");
        await using (var src = new SqliteConnection(s.Db.Database.GetConnectionString()))
        await using (var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = legacyDb, Pooling = false }.ToString()))
        {
            await src.OpenAsync(); await dst.OpenAsync(); src.BackupDatabase(dst);
        }
        SqliteConnection.ClearAllPools();
        await using (var input = File.OpenRead(legacyDb))
        await using (var output = File.Create(Path.Combine(dir, "hak-backup-legacy.hakbk")))
            BackupFile.Encrypt(input, output, Pass, "SQLite", "", h.Clock.UtcNow, 1_000);
        var legacy = await svc.VerifyAsync(h.AdminId, "hak-backup-legacy.hakbk", Pass);
        Assert.True(legacy.IsValid);
        Assert.Equal(0, legacy.AttachmentCount);
    }
}
