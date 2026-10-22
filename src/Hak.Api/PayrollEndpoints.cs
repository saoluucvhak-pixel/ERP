using Hak.Application.Payroll;

namespace Hak.Api;

public sealed record ExchangeKeyRequest(string SourceSystem, string KeyId);
public sealed record RotateExchangeKeyRequest(string KeyId, string Reason);

public static class PayrollEndpoints
{
    public static void MapPayrollEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/payroll");
        // The raw body is the signed package; it is verified as received (no re-serialisation before the signature check).
        api.MapPost("/import", async (HttpContext http, bool? commit, PayrollService s) =>
        {
            using var reader = new StreamReader(http.Request.Body);
            return Results.Ok(await s.ProcessAsync(await http.SessionAsync(), await reader.ReadToEndAsync(), commit ?? false));
        });
        var keys = app.MapGroup("/api/exchange/keys");
        keys.MapGet("", async (HttpContext http, Hak.Application.Exchange.ExchangeKeyService s) => Results.Ok(await s.ListAsync(await http.SessionAsync())));
        keys.MapPost("", async (HttpContext http, ExchangeKeyRequest r, Hak.Application.Exchange.ExchangeKeyService s) =>
            Results.Ok(await s.IssueAsync(await http.SessionAsync(), r.SourceSystem, r.KeyId)));
        keys.MapPost("/{id:guid}/deactivate", async (HttpContext http, Guid id, ReasonRequest r, Hak.Application.Exchange.ExchangeKeyService s) =>
        { await s.DeactivateAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        // Cấp lại: ngừng khóa cũ + cấp khóa mới trong một giao dịch; phản hồi mang bí mật mới nên đường dẫn nằm trong danh sách loại trừ idempotency.
        keys.MapPost("/{id:guid}/rotate", async (HttpContext http, Guid id, RotateExchangeKeyRequest r, Hak.Application.Exchange.ExchangeKeyService s) =>
            Results.Ok(await s.RotateAsync(await http.SessionAsync(), id, r.KeyId, r.Reason)));

        api.MapGet("/sheets", async (HttpContext http, DateOnly from, DateOnly to, PayrollService s) => Results.Ok(await s.SheetsAsync(await http.SessionAsync(), from, to)));
        api.MapGet("/summary", async (HttpContext http, DateOnly from, DateOnly to, PayrollService s) => Results.Ok(await s.SummaryAsync(await http.SessionAsync(), from, to)));
    }
}
