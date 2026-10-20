using Hak.Application.Trade;
using Hak.Domain.Trade;

namespace Hak.Api;

public static class ServiceAcceptanceEndpoints
{
    public static void MapServiceAcceptanceEndpoints(this WebApplication app)
    {
        var a = app.MapGroup("/api/service-acceptances");
        a.MapGet("", async (HttpContext http, InvoiceKind? kind, Guid? orderId, ServiceAcceptanceService s) => Results.Ok(await s.ListAsync(await http.SessionAsync(), kind, orderId)));
        a.MapGet("/invoice-candidates", async (HttpContext http, InvoiceKind kind, Guid? partnerId, ServiceAcceptanceService s) => Results.Ok(await s.InvoiceCandidatesAsync(await http.SessionAsync(), kind, partnerId)));
        a.MapPost("", async (HttpContext http, ServiceAcceptanceInput i, ServiceAcceptanceService s) => Results.Ok(new { (await s.CreateAsync(await http.SessionAsync(), i)).Id }));
        a.MapPost("/{id:guid}/decide", async (HttpContext http, Guid id, ContractDecisionRequest r, ServiceAcceptanceService s) => Results.Ok(new { (await s.DecideAsync(await http.SessionAsync(), id, r.Approve, r.Note)).Id }));
        a.MapPost("/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, ServiceAcceptanceService s) => { await s.CancelAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
    }
}
