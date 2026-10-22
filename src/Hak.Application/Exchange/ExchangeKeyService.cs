using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Integration;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Exchange;

/// <param name="Exposed">Khóa đang dùng mà bí mật đã lộ (nhật ký "exchange_key.exposed", ví dụ sự cố SEC-01): phải cấp lại.</param>
public sealed record ExchangeKeyRow(Guid Id, string SourceSystem, string KeyId, bool IsActive, bool Exposed = false);

/// <summary>A newly issued key. The secret is shown once, to be configured in the sending system, and never returned again.</summary>
public sealed record IssuedExchangeKey(Guid Id, string SourceSystem, string KeyId, string SecretBase64);

/// <summary>
/// HMAC keys of external systems allowed to send JSON packages (journal vouchers, payroll). Issuing generates a random 256-bit
/// secret server-side; keys are deactivated, never deleted, so packages already received stay attributable.
/// </summary>
public sealed partial class ExchangeKeyService(HakDbContext db, AuditService audit, MasterKey? masterKey = null)
{
    [GeneratedRegex("^[A-Z0-9_.-]{1,30}$")]
    private static partial Regex Code();

    public async Task<IssuedExchangeKey> IssueAsync(SessionContext ctx, string sourceSystem, string keyId)
    {
        ctx.Require(Permissions.ConfigManage);
        sourceSystem = sourceSystem.Trim().ToUpperInvariant();
        keyId = keyId.Trim();
        if (!Code().IsMatch(sourceSystem)) throw new DomainException(ErrorCodes.ValidationFailed, "Mã hệ thống nguồn chỉ gồm A-Z, 0-9, _ . - (tối đa 30 ký tự).");
        if (keyId.Length is 0 or > 50) throw new DomainException(ErrorCodes.ValidationFailed, "Mã khóa bắt buộc, tối đa 50 ký tự.");
        return await db.InTransactionAsync(async () =>
        {
            if (await db.ExchangePartnerKeys.AnyAsync(k => k.CompanyId == ctx.CompanyId && k.SourceSystem == sourceSystem && k.KeyId == keyId))
                throw new DomainException(ErrorCodes.DuplicateCode, $"Khóa {sourceSystem}/{keyId} đã tồn tại; dùng mã khóa mới khi đổi khóa.");
            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var key = new ExchangePartnerKey
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, SourceSystem = sourceSystem, KeyId = keyId,
                SecretBase64 = (masterKey ?? MasterKey.None).Protect(secret),   // mã hóa khi lưu; chỉ trả bản gốc đúng một lần
            };
            db.ExchangePartnerKeys.Add(key);
            await audit.AppendAsync(ctx.CompanyId, ctx, "exchange_key.issue", "exchange_key", key.Id.ToString(), new { sourceSystem, keyId });  // never the secret
            await db.SaveChangesAsync();
            return new IssuedExchangeKey(key.Id, sourceSystem, keyId, secret);
        });
    }

    public async Task<IReadOnlyList<ExchangeKeyRow>> ListAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ConfigManage);
        var exposed = (await db.AuditLogs.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId && a.Action == ExposedAction && a.EntityId != null)
            .Select(a => a.EntityId!).ToListAsync()).ToHashSet();
        return (await db.ExchangePartnerKeys.AsNoTracking().Where(k => k.CompanyId == ctx.CompanyId)
                .OrderBy(k => k.SourceSystem).ThenBy(k => k.KeyId).ToListAsync())
            .Select(k => new ExchangeKeyRow(k.Id, k.SourceSystem, k.KeyId, k.IsActive, k.IsActive && exposed.Contains(k.Id.ToString()))).ToList();
    }

    /// <summary>Nhật ký kiểm toán đánh dấu khóa bị lộ bí mật (ghi bởi bước dọn SEC-01 khi khởi động).</summary>
    public const string ExposedAction = "exchange_key.exposed";

    /// <summary>
    /// Cấp lại khóa (khi bí mật bị lộ hoặc đến hạn đổi): trong MỘT giao dịch ngừng dùng khóa cũ và cấp khóa mới cùng hệ thống nguồn với mã
    /// khóa mới. Bí mật mới chỉ trả về một lần; từ lúc này gói ký bằng khóa cũ bị từ chối, nên cấu hình khóa mới vào hệ thống gửi ngay.
    /// </summary>
    public async Task<IssuedExchangeKey> RotateAsync(SessionContext ctx, Guid id, string newKeyId, string reason)
    {
        ctx.Require(Permissions.ConfigManage);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do cấp lại khóa.");
        return await db.InTransactionAsync(async () =>
        {
            var old = await db.ExchangePartnerKeys.FirstOrDefaultAsync(k => k.Id == id && k.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Khóa không tồn tại.");
            if (!old.IsActive) throw new DomainException(ErrorCodes.InvalidStateTransition, "Khóa đã ngừng dùng; tạo khóa mới thay vì cấp lại.");
            old.IsActive = false;
            var issued = await IssueAsync(ctx, old.SourceSystem, newKeyId);
            await audit.AppendAsync(ctx.CompanyId, ctx, "exchange_key.rotate", "exchange_key", old.Id.ToString(),
                new { old.SourceSystem, oldKeyId = old.KeyId, newKeyId = issued.KeyId, newId = issued.Id, reason });
            await db.SaveChangesAsync();
            return issued;
        });
    }

    public async Task DeactivateAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.ConfigManage);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do ngừng dùng khóa.");
        await db.InTransactionAsync(async () =>
        {
            var key = await db.ExchangePartnerKeys.FirstOrDefaultAsync(k => k.Id == id && k.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Khóa không tồn tại.");
            if (!key.IsActive) throw new DomainException(ErrorCodes.InvalidStateTransition, "Khóa đã ngừng dùng.");
            key.IsActive = false;
            await audit.AppendAsync(ctx.CompanyId, ctx, "exchange_key.deactivate", "exchange_key", key.Id.ToString(), new { key.SourceSystem, key.KeyId, reason });
            await db.SaveChangesAsync();
        });
    }
}
