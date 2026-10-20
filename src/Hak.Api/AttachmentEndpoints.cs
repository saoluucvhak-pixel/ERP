using Hak.Application.Attachments;

namespace Hak.Api;

public static class AttachmentEndpoints
{
    public static void MapAttachmentEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/attachments");
        // Nội dung ảnh gửi thẳng trong thân yêu cầu (Content-Type chỉ để tham khảo: loại thật xác định theo chữ ký đầu tệp), giới hạn 8 MB.
        api.MapPost("/{entityType}/{entityId:guid}", async (HttpContext http, string entityType, Guid entityId, string? fileName, string? caption, AttachmentService s) =>
        {
            var ctx = await http.SessionAsync();
            if (http.Request.ContentLength is > AttachmentService.MaxBytes) return Results.Json(new { code = "VALIDATION_FAILED", message = "Ảnh quá lớn (tối đa 8 MB).", errors = Array.Empty<object>() }, statusCode: 413);
            using var ms = new MemoryStream();
            var buffer = new byte[81920]; int n;
            while ((n = await http.Request.Body.ReadAsync(buffer)) > 0)
            {
                ms.Write(buffer, 0, n);
                if (ms.Length > AttachmentService.MaxBytes) return Results.Json(new { code = "VALIDATION_FAILED", message = "Ảnh quá lớn (tối đa 8 MB).", errors = Array.Empty<object>() }, statusCode: 413);
            }
            var a = await s.AddAsync(ctx, entityType, entityId, fileName, ms.ToArray(), caption);
            return Results.Ok(new { a.Id, a.FileName, a.SizeBytes, a.ContentType });
        });
        api.MapGet("/{entityType}/{entityId:guid}", async (HttpContext http, string entityType, Guid entityId, AttachmentService s) => Results.Ok(await s.ListAsync(await http.SessionAsync(), entityType, entityId)));
        api.MapGet("/{entityType}/counts", async (HttpContext http, string entityType, AttachmentService s) => Results.Ok(await s.CountsAsync(await http.SessionAsync(), entityType)));
        api.MapGet("/file/{id:guid}", async (HttpContext http, Guid id, AttachmentService s) =>
        {
            var (meta, data) = await s.ReadAsync(await http.SessionAsync(), id);
            http.Response.Headers.CacheControl = "private, max-age=3600";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(data, meta.ContentType);
        });
    }
}
