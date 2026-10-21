using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hak.Application.Common;
using Hak.Data;
using Hak.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Hak.Api;

/// <summary>
/// Idempotency-Key cho yêu cầu ghi (POST/PUT/DELETE, nội dung JSON). Toàn bộ yêu cầu chạy trong MỘT giao dịch cùng với việc lưu phản hồi, nên:
/// commit xong mà mất phản hồi → gửi lại cùng khóa nhận đúng phản hồi cũ (header Idempotent-Replay), không ghi lần hai; lỗi (HTTP ≥ 400 hoặc ngoại lệ) → hoàn tác và không lưu,
/// gửi lại sẽ chạy lại; hai yêu cầu cùng khóa chạy cùng lúc → yêu cầu sau chờ khóa ghi rồi nhận phản hồi của yêu cầu trước; cùng khóa khác nội dung → 422 IDEMPOTENCY_KEY_REUSED.
/// Không áp dụng cho đăng nhập, khởi tạo, sao lưu / khôi phục và tải tệp.
/// </summary>
public sealed partial class IdempotencyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReusedCode = "IDEMPOTENCY_KEY_REUSED";
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    [GeneratedRegex("^[A-Za-z0-9_-]{8,100}$")]
    private static partial Regex ValidKey();

    private sealed class NotRecorded : Exception { }

    public async Task InvokeAsync(HttpContext http, HakDbContext db, IClock clock)
    {
        var req = http.Request;
        if (!Applies(http, out var key)) { await next(http); return; }
        if (!ValidKey().IsMatch(key!))
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            await http.Response.WriteAsJsonAsync(new { code = ErrorCodes.ValidationFailed, message = "Idempotency-Key phải gồm 8–100 ký tự chữ, số, gạch ngang hoặc gạch dưới." });
            return;
        }

        req.EnableBuffering();
        string body;
        using (var reader = new StreamReader(req.Body, Encoding.UTF8, false, 1024, leaveOpen: true)) body = await reader.ReadToEndAsync();
        req.Body.Position = 0;
        var hash = Sha(req.Method + "\n" + req.Path + req.QueryString + "\n" + body);
        var scope = Sha(req.Headers.Authorization.ToString() + "\n" + req.Headers["X-Company-Id"].ToString());

        var original = http.Response.Body;
        var buffer = new MemoryStream();
        IdempotencyRecord? replay = null;
        var recorded = false;
        try
        {
            http.Response.Body = buffer;
            await db.InTransactionAsync(async () =>
            {
                // Chạy lại sau lỗi tuần tự hóa: bỏ phần đã đệm của lần trước và đọc lại nội dung từ đầu.
                buffer.SetLength(0);
                http.Response.StatusCode = StatusCodes.Status200OK;
                req.Body.Position = 0;
                // Sau khi giành khóa ghi: nếu yêu cầu cùng khóa đã hoàn tất thì trả lại phản hồi cũ.
                var existing = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Scope == scope && r.Key == key);
                if (existing is not null) { replay = existing; return; }
                await next(http);
                if (http.Response.StatusCode >= 400) throw new NotRecorded();   // lỗi: hoàn tác mọi thay đổi dở dang và không lưu
                await buffer.FlushAsync();
                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    Id = Ids.New(), Scope = scope, Key = key!, RequestHash = hash, Method = req.Method, Path = req.Path.Value ?? "", StatusCode = http.Response.StatusCode,
                    ContentType = http.Response.ContentType, ResponseBody = Encoding.UTF8.GetString(buffer.ToArray()), CreatedAt = clock.UtcNow,
                });
                await db.IdempotencyRecords.Where(r => r.CreatedAt < clock.UtcNow - Retention).ExecuteDeleteAsync();
                await db.SaveChangesAsync();
                recorded = true;
            });
        }
        catch (NotRecorded) { /* trả phản hồi lỗi đã đệm bên dưới */ }
        finally { http.Response.Body = original; }

        if (replay is not null)
        {
            if (replay.RequestHash != hash)
            {
                http.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                await http.Response.WriteAsJsonAsync(new { code = ReusedCode, message = "Idempotency-Key này đã dùng cho một yêu cầu khác nội dung. Dùng khóa mới cho thao tác mới." });
                return;
            }
            http.Response.StatusCode = replay.StatusCode;
            if (replay.ContentType is not null) http.Response.ContentType = replay.ContentType;
            http.Response.Headers["Idempotent-Replay"] = "true";
            await http.Response.WriteAsync(replay.ResponseBody);
            return;
        }
        _ = recorded;
        buffer.Position = 0;
        await buffer.CopyToAsync(original);
    }

    private static bool Applies(HttpContext http, out string? key)
    {
        key = http.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrEmpty(key)) return false;
        var m = http.Request.Method;
        if (m != "POST" && m != "PUT" && m != "DELETE") return false;
        var path = http.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/", StringComparison.Ordinal)) return false;
        if (path.StartsWith("/api/auth/", StringComparison.Ordinal) || path.StartsWith("/api/setup/", StringComparison.Ordinal) || path.StartsWith("/api/backups", StringComparison.Ordinal)) return false;
        return http.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true || http.Request.ContentLength is null or 0;
    }

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
