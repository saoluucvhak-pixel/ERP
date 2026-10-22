using System.Text.Json;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Data;
using Microsoft.EntityFrameworkCore;

namespace Hak.Api;

/// <summary>
/// Dọn dữ liệu cũ của lỗi SEC-01: trước bản sửa, bảng idempotency_records đã lưu băm nội dung yêu cầu (mật khẩu, passphrase sao lưu)
/// và nguyên văn phản hồi (khóa ký HMAC khi cấp khóa trao đổi) của các đường dẫn mang bí mật. Chạy mỗi lần khởi động (không có gì để xóa
/// thì gần như không tốn gì): xóa mọi bản ghi thuộc <see cref="IdempotencyMiddleware.ExcludedPath"/>, và với mỗi khóa trao đổi có bí mật
/// nằm trong đó thì ghi nhật ký kiểm toán "exchange_key.exposed" để màn hình khóa báo "đã lộ — cần cấp lại" (docs/08 §SEC-01).
/// </summary>
public static class IdempotencySecretPurge
{
    public const string ExposedAction = Hak.Application.Exchange.ExchangeKeyService.ExposedAction;

    public sealed record Result(int Deleted, IReadOnlyList<Guid> ExposedExchangeKeyIds);

    public static async Task<Result> RunAsync(HakDbContext db, AuditService audit)
    {
        // Bảng chỉ giữ 7 ngày nên nhỏ; lọc đúng bằng cùng quy tắc với middleware thay vì dựng lại tiền tố trong SQL.
        var candidates = await db.IdempotencyRecords.AsNoTracking().Select(r => new { r.Id, r.Path }).ToListAsync();
        var ids = candidates.Where(c => IdempotencyMiddleware.ExcludedPath(c.Path)).Select(c => c.Id).ToList();
        if (ids.Count == 0) return new Result(0, []);

        return await db.InTransactionAsync(async () =>
        {
            // Phản hồi của "cấp khóa" mang id khóa và bí mật: đọc id ra trước khi xóa.
            var bodies = await db.IdempotencyRecords.AsNoTracking()
                .Where(r => ids.Contains(r.Id) && r.StatusCode < 300)
                .Select(r => new { r.Path, r.ResponseBody }).ToListAsync();
            var keyIds = bodies.Where(b => b.Path.StartsWith("/api/exchange/keys", StringComparison.OrdinalIgnoreCase))
                .Select(b => KeyIdOf(b.ResponseBody)).OfType<Guid>().Distinct().ToList();

            var deleted = 0;
            foreach (var chunk in ids.Chunk(500))
                deleted += await db.IdempotencyRecords.Where(r => chunk.Contains(r.Id)).ExecuteDeleteAsync();

            var keys = await db.ExchangePartnerKeys.AsNoTracking().Where(k => keyIds.Contains(k.Id)).ToListAsync();
            foreach (var k in keys)
                await audit.AppendAsync(k.CompanyId, null, ExposedAction, "exchange_key", k.Id.ToString(),
                    new { k.SourceSystem, k.KeyId, cause = "SEC-01: bí mật nằm trong bảng chống gửi trùng (và các bản sao lưu lập trước bản sửa)" });
            await audit.AppendAsync(Guid.Empty, null, "idempotency.purge_secrets", "idempotency_record", null,
                new { deleted, exposedExchangeKeys = keys.Count });
            await db.SaveChangesAsync();
            return new Result(deleted, keys.Select(k => k.Id).ToList());
        });
    }

    private static Guid? KeyIdOf(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("id", out var id) && id.TryGetGuid(out var g) ? g : null;
        }
        catch (JsonException) { return null; }
    }
}
