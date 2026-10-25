using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Hak.Application.Attachments;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Data;
using Hak.Domain.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Backup;

public sealed class BackupOptions
{
    /// <summary>Folder holding encrypted backups and restored copies.</summary>
    public string Directory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HAK", "backups");
    public int Iterations { get; set; } = BackupFile.DefaultIterations;
    public int MinPassphraseLength { get; set; } = 12;
}

public sealed record BackupInfo(string FileName, long Size, DateTime CreatedAt, long DatabaseBytes, string DatabaseSha256, string Note);

public sealed record CompanyChainCheck(Guid CompanyId, string CompanyCode, int Vouchers, int JournalEntries, long? BrokenAuditSeq);

/// <summary>Result of decrypting and checking a backup. RestoredPath is set only by a restore.</summary>
public sealed record BackupVerification(string FileName, DateTime CreatedAt, string Integrity, bool SystemAuditChainOk, IReadOnlyList<CompanyChainCheck> Companies,
    string? RestoredPath, int AttachmentCount = 0, IReadOnlyList<string>? AttachmentProblems = null, string? RestoredAttachmentsPath = null)
{
    public bool IsValid => Integrity == "ok" && SystemAuditChainOk && Companies.All(c => c.BrokenAuditSeq is null) && (AttachmentProblems is null || AttachmentProblems.Count == 0);
}

/// <summary>Danh sách tệp đính kèm trong gói sao lưu (đường dẫn tương đối, kích thước, SHA-256).</summary>
public sealed record BackupManifest(int Version, DateTime CreatedAt, string DatabaseSha256, IReadOnlyList<BackupManifestFile> Attachments);

public sealed record BackupManifestFile(string Path, long Size, string Sha256);

/// <summary>
/// Sao lưu / khôi phục (SQLite, one-machine mode). A backup is an online snapshot of the whole database (all companies), so only
/// a system administrator may create, verify or restore one. Restore never touches the live database: the backup is decrypted
/// to a new file, checked (SHA-256, PRAGMA integrity_check, every company's audit hash chain) and left beside the backups for
/// the operator to switch Hak:DatabasePath to it. PostgreSQL (LAN) mode is backed up with pg_dump on the server [docs].
/// </summary>
public sealed class BackupService(HakDbContext db, AuditService audit, IClock clock, BackupOptions options, AttachmentStore? attachments = null)
{
    private const string DbEntry = "hak.db";
    private const string ManifestEntry = "manifest.json";
    private const string AttachmentPrefix = "attachments/";

    public async Task<BackupInfo> CreateAsync(Guid userId, string passphrase, string? note = null)
    {
        await RequireSystemAdminAsync(userId);
        return await CreateCoreAsync(userId, passphrase, note);
    }

    /// <summary>Quản trị hệ thống đầu tiên còn hoạt động — người được ghi trong nhật ký cho sao lưu tự động theo lịch.</summary>
    public async Task<Guid> ScheduledActorAsync()
        => await db.Users.AsNoTracking().Where(u => u.IsActive && u.IsSystemAdmin).OrderBy(u => u.CreatedAt).Select(u => (Guid?)u.Id).FirstOrDefaultAsync()
           ?? throw new DomainException(ErrorCodes.NotFound, "Chưa có quản trị hệ thống để ghi nhận sao lưu tự động.");

    private async Task<BackupInfo> CreateCoreAsync(Guid userId, string passphrase, string? note)
    {
        RequireSqlite();
        if (string.IsNullOrEmpty(passphrase) || passphrase.Length < options.MinPassphraseLength)
            throw new DomainException(ErrorCodes.ValidationFailed, $"Mật khẩu sao lưu phải có ít nhất {options.MinPassphraseLength} ký tự (lưu giữ riêng, mất mật khẩu là mất bản sao lưu).");
        Directory.CreateDirectory(options.Directory);
        var stamp = clock.UtcNow.ToString("yyyyMMdd-HHmmss");
        var snapshot = Path.Combine(options.Directory, $".snapshot-{Guid.NewGuid():N}.db");
        var fileName = $"hak-backup-{stamp}.hakbk";
        var package = Path.Combine(options.Directory, $".package-{Guid.NewGuid():N}.zip");
        var target = Path.Combine(options.Directory, fileName);
        if (File.Exists(target)) throw new DomainException(ErrorCodes.DuplicateCode, "Đã có bản sao lưu cùng thời điểm; thử lại sau một giây.");
        try
        {
            // Online backup API: a consistent snapshot while other users keep working.
            await using (var source = new SqliteConnection(db.Database.GetConnectionString()))
            await using (var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot, Pooling = false }.ToString()))
            {
                await source.OpenAsync();
                await dest.OpenAsync();
                source.BackupDatabase(dest);
            }
            // Gói sao lưu = cơ sở dữ liệu + ảnh/tệp đính kèm + manifest (bản cũ chỉ có cơ sở dữ liệu thô vẫn đọc được).
            BuildPackage(snapshot, package);
            BackupHeader header;
            await using (var input = File.OpenRead(package))
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                header = BackupFile.Encrypt(input, output, passphrase, "SQLite+files", note?.Trim() ?? "", clock.UtcNow, options.Iterations);
            var info = new BackupInfo(fileName, new FileInfo(target).Length, header.CreatedAt, header.PlaintextLength, header.PlaintextSha256, header.Note);
            await LogAsync(userId, "backup.create", fileName, new { info.Size, info.DatabaseBytes, info.DatabaseSha256, note });
            return info;
        }
        catch
        {
            TryDelete(target);
            throw;
        }
        finally { TryDelete(snapshot); TryDelete(package); }
    }

    public async Task<IReadOnlyList<BackupInfo>> ListAsync(Guid userId)
    {
        await RequireSystemAdminAsync(userId);
        if (!Directory.Exists(options.Directory)) return [];
        var list = new List<BackupInfo>();
        foreach (var path in Directory.GetFiles(options.Directory, "*.hakbk").OrderByDescending(p => p, StringComparer.Ordinal))
        {
            try
            {
                await using var s = File.OpenRead(path);
                var h = BackupFile.ReadHeader(s);
                list.Add(new BackupInfo(Path.GetFileName(path), s.Length, h.CreatedAt, h.PlaintextLength, h.PlaintextSha256, h.Note));
            }
            catch (DomainException) { /* not a HAK backup: skipped */ }
        }
        return list;
    }

    /// <summary>Decrypts and checks a backup. With <paramref name="restore"/> the checked copy is kept as restored-&lt;stamp&gt;.db.</summary>
    public async Task<BackupVerification> VerifyAsync(Guid userId, string fileName, string passphrase, bool restore = false)
    {
        await RequireSystemAdminAsync(userId);
        var path = BackupPath(fileName);
        var raw = Path.Combine(options.Directory, $".verify-{Guid.NewGuid():N}.bin");
        var work = Path.Combine(options.Directory, $".verify-{Guid.NewGuid():N}.db");
        var stagedFiles = Path.Combine(options.Directory, $".verify-files-{Guid.NewGuid():N}");
        BackupVerification result;
        try
        {
            BackupHeader header;
            await using (var input = File.OpenRead(path))
            await using (var output = new FileStream(raw, FileMode.CreateNew, FileAccess.Write))
                header = BackupFile.Decrypt(input, output, passphrase);
            var (attachmentCount, problems) = Unpack(raw, work, stagedFiles);

            string integrity;
            await using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = work, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()))
            {
                await c.OpenAsync();
                await using var cmd = c.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check";
                integrity = (string)(await cmd.ExecuteScalarAsync())!;
            }
            var companies = new List<CompanyChainCheck>();
            bool systemOk;
            await using (var copy = new HakDbContext(HakDb.SqliteOptions(work)))
            {
                var chain = audit.ForDatabase(copy);   // cùng khóa chủ: chuỗi có khóa (SEC-09) kiểm được trên bản sao
                systemOk = await chain.VerifyChainAsync(Guid.Empty) is null;
                foreach (var co in await copy.Companies.AsNoTracking().OrderBy(x => x.Code).ToListAsync())
                    companies.Add(new CompanyChainCheck(co.Id, co.Code, await copy.Vouchers.CountAsync(v => v.CompanyId == co.Id),
                        await copy.JournalEntries.CountAsync(e => e.CompanyId == co.Id), await chain.VerifyChainAsync(co.Id)));
            }
            SqliteConnection.ClearAllPools();
            string? restored = null;
            string? restoredFiles = null;
            var ok = integrity == "ok" && systemOk && companies.All(c => c.BrokenAuditSeq is null) && problems.Count == 0;
            if (restore)
            {
                if (!ok) throw new DomainException(ErrorCodes.BackupInvalid, "Bản sao lưu không vượt qua kiểm tra toàn vẹn; không khôi phục.");
                restored = Path.Combine(options.Directory, $"restored-{clock.UtcNow:yyyyMMdd-HHmmss}-{Path.GetFileNameWithoutExtension(fileName)}.db");
                File.Move(work, restored);
                if (attachmentCount > 0)
                {
                    restoredFiles = Path.ChangeExtension(restored, null) + "-attachments";
                    Directory.Move(stagedFiles, restoredFiles);
                }
            }
            result = new BackupVerification(fileName, header.CreatedAt, integrity, systemOk, companies, restored, attachmentCount, problems, restoredFiles);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(raw);
            TryDelete(work);
            try { if (Directory.Exists(stagedFiles)) Directory.Delete(stagedFiles, true); } catch (IOException) { /* best effort */ }
        }
        await LogAsync(userId, restore ? "backup.restore" : "backup.verify", fileName, new { result.IsValid, result.Integrity, result.RestoredPath, result.AttachmentCount, result.RestoredAttachmentsPath });
        return result;
    }

    // ------------------------------------------------------------------ package

    private void BuildPackage(string snapshot, string package)
    {
        var files = new List<BackupManifestFile>();
        using (var fs = new FileStream(package, FileMode.CreateNew, FileAccess.ReadWrite))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            string dbHash;
            using (var src = File.OpenRead(snapshot))
            {
                dbHash = Convert.ToHexString(SHA256.HashData(src)).ToLowerInvariant();
                src.Position = 0;
                using var entry = zip.CreateEntry(DbEntry, CompressionLevel.Optimal).Open();
                src.CopyTo(entry);
            }
            if (attachments is not null && Directory.Exists(attachments.Root))
                foreach (var file in Directory.EnumerateFiles(attachments.Root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    var rel = Path.GetRelativePath(attachments.Root, file).Replace('\\', '/');
                    using var src = File.OpenRead(file);
                    var hash = Convert.ToHexString(SHA256.HashData(src)).ToLowerInvariant();
                    src.Position = 0;
                    // Ảnh đã nén sẵn: không nén lại.
                    using (var entry = zip.CreateEntry(AttachmentPrefix + rel, CompressionLevel.NoCompression).Open()) src.CopyTo(entry);
                    files.Add(new BackupManifestFile(rel, src.Length, hash));
                }
            var manifest = new BackupManifest(1, clock.UtcNow, dbHash, files);
            using var m = new StreamWriter(zip.CreateEntry(ManifestEntry).Open());
            m.Write(JsonSerializer.Serialize(manifest));
        }
    }

    /// <summary>Giải gói (hoặc nhận bản cũ là cơ sở dữ liệu thô) ra <paramref name="dbOut"/> và thư mục tệp; trả số tệp và các sai lệch so với manifest.</summary>
    private static (int Count, List<string> Problems) Unpack(string raw, string dbOut, string filesDir)
    {
        var problems = new List<string>();
        Span<byte> magic = stackalloc byte[4];
        using (var probe = File.OpenRead(raw)) if (probe.Read(magic) < 4) throw new DomainException(ErrorCodes.BackupInvalid, "Bản sao lưu rỗng hoặc hỏng.");
        if (!(magic[0] == 'P' && magic[1] == 'K'))
        {
            File.Move(raw, dbOut); // định dạng cũ: chỉ có cơ sở dữ liệu
            return (0, problems);
        }
        using var zip = ZipFile.OpenRead(raw);
        var dbEntry = zip.GetEntry(DbEntry) ?? throw new DomainException(ErrorCodes.BackupInvalid, "Gói sao lưu thiếu cơ sở dữ liệu.");
        using (var o = new FileStream(dbOut, FileMode.CreateNew, FileAccess.Write)) dbEntry.Open().CopyTo(o);
        var manifestEntry = zip.GetEntry(ManifestEntry) ?? throw new DomainException(ErrorCodes.BackupInvalid, "Gói sao lưu thiếu manifest.");
        BackupManifest manifest;
        using (var r = new StreamReader(manifestEntry.Open()))
            manifest = JsonSerializer.Deserialize<BackupManifest>(r.ReadToEnd()) ?? throw new DomainException(ErrorCodes.BackupInvalid, "Manifest sao lưu hỏng.");
        using (var o = File.OpenRead(dbOut))
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(o)), manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
                problems.Add("Cơ sở dữ liệu không khớp mã băm trong manifest.");
        var root = Path.GetFullPath(filesDir);
        foreach (var f in manifest.Attachments)
        {
            var entry = zip.GetEntry(AttachmentPrefix + f.Path);
            if (entry is null) { problems.Add($"Thiếu tệp đính kèm {f.Path}."); continue; }
            var dest = Path.GetFullPath(Path.Combine(root, f.Path));
            if (!dest.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) { problems.Add($"Đường dẫn tệp không hợp lệ: {f.Path}."); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using (var o = new FileStream(dest, FileMode.Create, FileAccess.Write)) entry.Open().CopyTo(o);
            using var check = File.OpenRead(dest);
            if (check.Length != f.Size || !string.Equals(Convert.ToHexString(SHA256.HashData(check)), f.Sha256, StringComparison.OrdinalIgnoreCase))
                problems.Add($"Tệp đính kèm {f.Path} không khớp mã băm.");
        }
        return (manifest.Attachments.Count, problems);
    }

    // ------------------------------------------------------------------ helpers

    private string BackupPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName) || !fileName.EndsWith(".hakbk", StringComparison.Ordinal))
            throw new DomainException(ErrorCodes.ValidationFailed, "Tên file sao lưu không hợp lệ.");
        var path = Path.Combine(options.Directory, fileName);
        return File.Exists(path) ? path : throw new DomainException(ErrorCodes.NotFound, $"Không có file sao lưu {fileName}.");
    }

    private void RequireSqlite()
    {
        if (!db.Database.IsSqlite())
            throw new DomainException(ErrorCodes.NotSupported, "Chế độ PostgreSQL (LAN): sao lưu bằng pg_dump trên máy chủ cơ sở dữ liệu theo hướng dẫn vận hành.");
    }

    private async Task RequireSystemAdminAsync(Guid userId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.IsSystemAdmin))
            throw new DomainException(ErrorCodes.Forbidden, "Chỉ quản trị hệ thống được sao lưu / khôi phục (bản sao lưu chứa dữ liệu mọi công ty).");
    }

    private Task LogAsync(Guid userId, string action, string fileName, object data)
        => db.InTransactionAsync(async () =>
        {
            await audit.AppendAsync(Guid.Empty, null, action, "backup", fileName, data, userIdOverride: userId);
            await db.SaveChangesAsync();
        });

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { /* best effort; temp file left for the operator */ }
    }
}
