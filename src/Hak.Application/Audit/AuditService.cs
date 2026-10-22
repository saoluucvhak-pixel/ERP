using System.Security.Cryptography;
using System.Text.Json;
using Hak.Application.Exchange;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Audit;

public sealed record AuditFilter(DateOnly? From = null, DateOnly? To = null, string? Action = null, string? EntityType = null, string? EntityId = null,
    Guid? UserId = null, string? Result = null, int Skip = 0, int Take = 100);

public sealed record AuditRow(long Seq, DateTime At, string? UserName, string? Device, string Action, string EntityType, string? EntityId, string Result,
    string? DataJson, string Hash);

public sealed record AuditPage(int Total, IReadOnlyList<AuditRow> Rows);

/// <summary>
/// Hash-chained audit log (docs/02 ADR-08): hash = HMAC-SHA256(chain key, prev_hash + canonical JSON of the record), stored as "h1:" + hex.
/// The chain key is derived from the master key outside the database (SEC-09), so someone who can edit the database file cannot
/// recompute the chain after changing a row. Without a master key (tests) and in rows written before SEC-09 the hash is the plain
/// SHA256(prev_hash + canonical JSON). A signed <see cref="AuditChainAnchor"/> per chain records where the plain part ends; every row
/// after it must be keyed, so rewriting the tail in the plain form (or deleting the anchor) is detected.
/// Deleting, reordering or editing any row breaks the chain, which <see cref="VerifyChainAsync"/> detects.
/// Must be called inside the same transaction as the change it records.
/// </summary>
public sealed class AuditService(HakDbContext db, IClock clock, MasterKey? masterKey = null)
{
    public const string Genesis = "GENESIS";

    /// <summary>Prefix of keyed (HMAC) hashes.</summary>
    public const string KeyedPrefix = "h1:";

    private readonly byte[]? chainKey = masterKey?.DeriveKey("hak.audit-chain.v1");

    /// <summary>Same key, another database (verifying a backup copy).</summary>
    public AuditService ForDatabase(HakDbContext other) => new(other, clock, masterKey);

    private string AnchorMac(Guid companyId, long legacySeq, string legacyHash)
        => Convert.ToHexStringLower(HMACSHA256.HashData(chainKey!, System.Text.Encoding.UTF8.GetBytes($"anchor|{companyId:N}|{legacySeq}|{legacyHash}")));

    /// <summary>
    /// With a master key, the first keyed row of a chain is preceded by a signed anchor naming the last plain row (SEC-09).
    /// Not created over a chain that already ends in a keyed row: a missing anchor there is tampering, left for the verifier to report.
    /// </summary>
    private async Task AnchorIfMissingAsync(Guid companyId, long lastSeq, string lastHash)
    {
        if (chainKey is null || lastHash.StartsWith(KeyedPrefix, StringComparison.Ordinal)) return;
        if (db.AuditChainAnchors.Local.Any(a => a.CompanyId == companyId) || await db.AuditChainAnchors.AnyAsync(a => a.CompanyId == companyId)) return;
        db.AuditChainAnchors.Add(new AuditChainAnchor
        {
            CompanyId = companyId, LegacySeq = lastSeq, LegacyHash = lastHash, CreatedAt = clock.UtcNow, Mac = AnchorMac(companyId, lastSeq, lastHash),
        });
    }

    /// <summary>
    /// At startup with a master key: anchor every existing chain (system scope and each company) at its current end, so the plain
    /// history written before SEC-09 is sealed even if that company writes nothing new. No-op without a key or when already anchored.
    /// </summary>
    public async Task<int> EnsureAnchorsAsync()
    {
        if (chainKey is null) return 0;
        var scopes = (await db.Companies.AsNoTracking().Select(c => c.Id).ToListAsync()).Prepend(Guid.Empty).ToList();
        var anchored = (await db.AuditChainAnchors.AsNoTracking().Select(a => a.CompanyId).ToListAsync()).ToHashSet();
        var added = 0;
        foreach (var companyId in scopes.Where(c => !anchored.Contains(c)))
        {
            var last = await db.AuditLogs.AsNoTracking().Where(a => a.CompanyId == companyId).OrderByDescending(a => a.Seq).Select(a => new { a.Seq, a.Hash }).FirstOrDefaultAsync();
            var before = db.AuditChainAnchors.Local.Count;
            await AnchorIfMissingAsync(companyId, last?.Seq ?? 0, last?.Hash ?? Genesis);
            added += db.AuditChainAnchors.Local.Count - before;
        }
        if (added > 0) await db.SaveChangesAsync();
        return added;
    }

    public async Task<AuditLog> AppendAsync(Guid companyId, SessionContext? ctx, string action, string entityType,
        string? entityId, object? data = null, long? entityRowVersion = null, string result = "OK", Guid? userIdOverride = null)
    {
        var pending = db.ChangeTracker.Entries<AuditLog>()
            .Where(e => e.State == EntityState.Added && e.Entity.CompanyId == companyId)
            .Select(e => e.Entity).OrderByDescending(a => a.Seq).FirstOrDefault();

        // Đã có bản ghi chờ lưu trong cùng giao dịch thì nối tiếp nó, không cần truy vấn CSDL lại (một lệnh ghi sổ gọi hàm này nhiều lần).
        var (prevSeq, prevHash) = (0L, Genesis);
        if (pending is not null) (prevSeq, prevHash) = (pending.Seq, pending.Hash);
        else
        {
            var last = await db.AuditLogs.Where(a => a.CompanyId == companyId)
                .OrderByDescending(a => a.Seq).Select(a => new { a.Seq, a.Hash }).FirstOrDefaultAsync();
            if (last is not null) (prevSeq, prevHash) = (last.Seq, last.Hash);
            await AnchorIfMissingAsync(companyId, prevSeq, prevHash);
        }

        var log = new AuditLog
        {
            // Truncated to microseconds: PostgreSQL timestamps keep 6 digits, so the hash must not depend on the 7th.
            Id = Ids.New(), CompanyId = companyId, Seq = prevSeq + 1, At = new DateTime(clock.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc),
            UserId = userIdOverride ?? ctx?.UserId, SessionId = ctx?.SessionId, Device = ctx?.Device,
            Action = action, EntityType = entityType, EntityId = entityId, Result = result,
            EntityRowVersion = entityRowVersion,
            DataJson = data is null ? null : JsonSerializer.Serialize(data),
            PrevHash = prevHash,
        };
        log.Hash = ComputeHash(log, chainKey);
        db.AuditLogs.Add(log);
        return log;
    }

    /// <summary>Plain SHA-256 (no key) or keyed "h1:" HMAC-SHA256 of the previous hash and the record.</summary>
    public static string ComputeHash(AuditLog a, byte[]? key = null)
    {
        var payload = a.PrevHash + Canonical(a);
        return key is null
            ? PasswordHasher.Sha256Hex(payload)
            : KeyedPrefix + Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(key, System.Text.Encoding.UTF8.GetBytes(payload)));
    }

    private static string Canonical(AuditLog a)
    {
        var body = new Dictionary<string, object?>
        {
            ["seq"] = a.Seq, ["company_id"] = a.CompanyId.ToString(), ["at"] = DateTime.SpecifyKind(a.At, DateTimeKind.Utc).ToString("O"),
            ["user_id"] = a.UserId?.ToString(), ["session_id"] = a.SessionId?.ToString(), ["device"] = a.Device,
            ["action"] = a.Action, ["entity_type"] = a.EntityType, ["entity_id"] = a.EntityId,
            ["result"] = a.Result, ["entity_row_version"] = a.EntityRowVersion, ["data"] = a.DataJson,
        };
        return JsonCanonicalizer.Canonicalize(JsonSerializer.SerializeToNode(body));
    }

    /// <summary>
    /// Audit Viewer: newest first, filtered by date (UTC days), action prefix, entity and user; paged. Requires audit.view in
    /// the company. Rows are returned as stored (data JSON included) — the log never holds secrets by design.
    /// </summary>
    public async Task<AuditPage> QueryAsync(SessionContext ctx, AuditFilter filter)
    {
        ctx.Require(Permissions.AuditView);
        var q = db.AuditLogs.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId);
        if (filter.From is { } f) { var start = f.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); q = q.Where(a => a.At >= start); }
        if (filter.To is { } t) { var end = t.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); q = q.Where(a => a.At < end); }
        if (!string.IsNullOrWhiteSpace(filter.Action)) q = q.Where(a => a.Action.StartsWith(filter.Action.Trim()));
        if (!string.IsNullOrWhiteSpace(filter.EntityType)) q = q.Where(a => a.EntityType == filter.EntityType.Trim());
        if (!string.IsNullOrWhiteSpace(filter.EntityId)) q = q.Where(a => a.EntityId == filter.EntityId.Trim());
        if (filter.UserId is { } u) q = q.Where(a => a.UserId == u);
        if (!string.IsNullOrWhiteSpace(filter.Result)) q = q.Where(a => a.Result == filter.Result.Trim());
        var take = Math.Clamp(filter.Take, 1, 500);
        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(a => a.Seq).Skip(Math.Max(0, filter.Skip)).Take(take).ToListAsync();
        var userIds = rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.UserName);
        return new AuditPage(total, rows.Select(r => new AuditRow(r.Seq, r.At, r.UserId is { } id ? users.GetValueOrDefault(id) : null, r.Device, r.Action,
            r.EntityType, r.EntityId, r.Result, r.DataJson, r.Hash)).ToList());
    }

    /// <summary>Returns null when intact, otherwise the first broken sequence number.</summary>
    /// <remarks>
    /// Keyed rows need the master key of this installation; without it the chain cannot be checked (not "broken"), so this throws
    /// INTEGRITY_VIOLATION asking for secret.key / Hak:SecretKey instead of reporting a tampered row.
    /// </remarks>
    public async Task<long?> VerifyChainAsync(Guid companyId)
    {
        var anchor = await db.AuditChainAnchors.AsNoTracking().FirstOrDefaultAsync(a => a.CompanyId == companyId);
        long legacyUntil;   // bản ghi đến số này dùng SHA-256 thường; sau đó bắt buộc có khóa
        if (chainKey is null)
        {
            if (anchor is not null) throw NeedKey();
            legacyUntil = long.MaxValue;
        }
        else if (anchor is null)
        {
            // Có khóa chủ thì mọi chuỗi đều được neo (lúc khởi động hoặc ở lần ghi đầu); chuỗi có bản ghi mà không có mốc = mốc bị xóa.
            return await db.AuditLogs.AnyAsync(x => x.CompanyId == companyId) ? 1 : null;
        }
        else if (!Same(AnchorMac(companyId, anchor.LegacySeq, anchor.LegacyHash), anchor.Mac)) return 1;
        else legacyUntil = anchor.LegacySeq;

        var expectedPrev = Genesis;
        long expectedSeq = 1;
        await foreach (var a in db.AuditLogs.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Seq).AsAsyncEnumerable())
        {
            if (a.Seq != expectedSeq || a.PrevHash != expectedPrev) return expectedSeq;
            var keyed = a.Hash.StartsWith(KeyedPrefix, StringComparison.Ordinal);
            if (keyed && chainKey is null) throw NeedKey();
            if (keyed != a.Seq > legacyUntil) return expectedSeq;   // bản ghi thường sau mốc (hạ cấp để giả mạo) hoặc có khóa trước mốc
            if (!Same(ComputeHash(a, keyed ? chainKey : null), a.Hash)) return expectedSeq;
            if (a.Seq == legacyUntil && !Same(anchor!.LegacyHash, a.Hash)) return expectedSeq;
            expectedPrev = a.Hash;
            expectedSeq++;
        }
        if (anchor is not null && expectedSeq - 1 < anchor.LegacySeq) return expectedSeq;   // phần chuỗi cũ đã được neo bị xóa bớt
        return null;
    }

    private static bool Same(string a, string b) => CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(a), System.Text.Encoding.ASCII.GetBytes(b));

    private static DomainException NeedKey() => new(ErrorCodes.IntegrityViolation,
        "Nhật ký kiểm toán được ký bằng khóa chủ của cài đặt; cần tệp secret.key (hoặc Hak:SecretKey) của máy đã ghi để kiểm tra.");
}
