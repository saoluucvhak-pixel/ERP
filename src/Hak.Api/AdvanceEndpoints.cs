using Hak.Application.Advances;
using Hak.Domain.Advances;

namespace Hak.Api;

public sealed record AdvanceDecisionRequest(bool Approve, string? Note);
public sealed record AdvancePayRequest(Guid CashAccountId, DateOnly Date);
public sealed record AdvanceSettleRequest(DateOnly Date, IReadOnlyList<AdvanceLineInput> Lines, Guid? CashAccountId);
public sealed record CashCountAdjustRequest(DateOnly Date);

public static class AdvanceEndpoints
{
    public static void MapAdvanceEndpoints(this WebApplication app)
    {
        var adv = app.MapGroup("/api/advances");
        adv.MapGet("", async (HttpContext http, DateOnly? asOf, Guid? employeeId, AdvanceService s) =>
            Results.Ok(await s.ListAsync(await http.SessionAsync(), asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), employeeId)));
        adv.MapGet("/aging", async (HttpContext http, DateOnly asOf, AdvanceService s) => Results.Ok(await s.AgingAsync(await http.SessionAsync(), asOf)));
        adv.MapPost("", async (HttpContext http, AdvanceInput i, AdvanceService s) => Results.Ok(new { (await s.CreateAsync(await http.SessionAsync(), i)).Id }));
        adv.MapGet("/{id:guid}", async (HttpContext http, Guid id, AdvanceService s) => Results.Ok(await s.GetAsync(await http.SessionAsync(), id)));
        adv.MapPost("/{id:guid}/decide", async (HttpContext http, Guid id, AdvanceDecisionRequest r, AdvanceService s) => Results.Ok(new { (await s.DecideAsync(await http.SessionAsync(), id, r.Approve, r.Note)).Id }));
        adv.MapPost("/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, AdvanceService s) => { await s.CancelAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        adv.MapPost("/{id:guid}/payment-voucher", async (HttpContext http, Guid id, AdvancePayRequest r, AdvanceService s) =>
        { var v = await s.CreatePaymentVoucherAsync(await http.SessionAsync(), id, r.CashAccountId, r.Date); return Results.Ok(new { v.Id, v.Number }); });
        adv.MapPost("/{id:guid}/settle", async (HttpContext http, Guid id, AdvanceSettleRequest r, AdvanceService s) =>
        { var v = await s.SettleAsync(await http.SessionAsync(), id, r.Date, r.Lines, r.CashAccountId); return Results.Ok(new { v.Id, v.Number }); });

        var cc = app.MapGroup("/api/cash-counts");
        cc.MapGet("", async (HttpContext http, DateOnly? from, DateOnly? to, CashCountService s) => Results.Ok(await s.ListAsync(await http.SessionAsync(), from, to)));
        cc.MapPost("", async (HttpContext http, CashCountInput i, CashCountService s) => Results.Ok(new { (await s.CreateAsync(await http.SessionAsync(), i)).Id }));
        cc.MapGet("/{id:guid}", async (HttpContext http, Guid id, CashCountService s) => Results.Ok(await s.GetAsync(await http.SessionAsync(), id)));
        cc.MapPost("/{id:guid}/confirm", async (HttpContext http, Guid id, CashCountService s) => Results.Ok(new { (await s.ConfirmAsync(await http.SessionAsync(), id)).Id }));
        cc.MapPost("/{id:guid}/adjust", async (HttpContext http, Guid id, CashCountAdjustRequest r, CashCountService s) =>
        { var v = await s.CreateAdjustmentVoucherAsync(await http.SessionAsync(), id, r.Date); return Results.Ok(new { v.Id, v.Number }); });
        cc.MapPost("/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, CashCountService s) => { await s.CancelAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
    }
}
