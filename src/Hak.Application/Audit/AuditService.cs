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
/// Hash-chained audit log (docs/02 ADR-08): hash = SHA256(prev_hash + canonical JSON of the record).
/// Deleting, reordering or editing any row breaks the chain, which <see cref="VerifyChainAsync"/> detects.
/// Must be called inside the same transaction as the change it records.
/// </summary>
public sealed class AuditService(HakDbContext db, IClock clock)
{
    public const string Genesis = "GENESIS";

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
        log.Hash = ComputeHash(log);
        db.AuditLogs.Add(log);
        return log;
    }

    public static string ComputeHash(AuditLog a)
    {
        var body = new Dictionary<string, object?>
        {
            ["seq"] = a.Seq, ["company_id"] = a.CompanyId.ToString(), ["at"] = DateTime.SpecifyKind(a.At, DateTimeKind.Utc).ToString("O"),
            ["user_id"] = a.UserId?.ToString(), ["session_id"] = a.SessionId?.ToString(), ["device"] = a.Device,
            ["action"] = a.Action, ["entity_type"] = a.EntityType, ["entity_id"] = a.EntityId,
            ["result"] = a.Result, ["entity_row_version"] = a.EntityRowVersion, ["data"] = a.DataJson,
        };
        return PasswordHasher.Sha256Hex(a.PrevHash + JsonCanonicalizer.Canonicalize(JsonSerializer.SerializeToNode(body)));
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
    public async Task<long?> VerifyChainAsync(Guid companyId)
    {
        var expectedPrev = Genesis;
        long expectedSeq = 1;
        await foreach (var a in db.AuditLogs.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Seq).AsAsyncEnumerable())
        {
            if (a.Seq != expectedSeq || a.PrevHash != expectedPrev || ComputeHash(a) != a.Hash) return expectedSeq;
            expectedPrev = a.Hash;
            expectedSeq++;
        }
        return null;
    }
}
