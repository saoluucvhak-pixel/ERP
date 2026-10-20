using Hak.Application.Loans;
using Hak.Domain.Fx;

namespace Hak.Api;

public sealed record LoanRateRequest(DateOnly EffectiveFrom, decimal AnnualRatePercent, string Note);
public sealed record LoanLinkRequest(List<Guid> JournalLineIds);
public sealed record LoanAccrueRequest(Guid PeriodId, List<FxRate>? Rates);

public sealed record SchedulePreviewRequest(int Installments, DateOnly FirstDue, decimal? AnnualRatePercent);

public static class LoanEndpoints
{
    public static void MapLoanEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/loans");
        api.MapGet("", async (HttpContext http, DateOnly asOf, LoanService s) => Results.Ok(await s.RegisterAsync(await http.SessionAsync(), asOf)));
        api.MapPost("", async (HttpContext http, LoanInput input, LoanService s) => Results.Ok(await s.CreateAsync(await http.SessionAsync(), input)));
        api.MapPost("/{id:guid}/activate", async (HttpContext http, Guid id, LoanService s) => Results.Ok(await s.ActivateAsync(await http.SessionAsync(), id)));
        api.MapPost("/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, LoanService s) =>
        { await s.CancelDraftAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        api.MapPost("/{id:guid}/close", async (HttpContext http, Guid id, LoanService s) => Results.Ok(await s.CloseAsync(await http.SessionAsync(), id)));
        api.MapPost("/{id:guid}/rates", async (HttpContext http, Guid id, LoanRateRequest r, LoanService s) =>
            Results.Ok(await s.AddRateAsync(await http.SessionAsync(), id, r.EffectiveFrom, r.AnnualRatePercent, r.Note)));
        api.MapPost("/{id:guid}/schedule/preview", async (HttpContext http, Guid id, SchedulePreviewRequest r, Hak.Application.Loans.LoanScheduleService s) =>
            Results.Ok(await s.PreviewEqualPrincipalAsync(await http.SessionAsync(), id, r.Installments, r.FirstDue, r.AnnualRatePercent)));
        api.MapGet("/{id:guid}/schedule", async (HttpContext http, Guid id, DateOnly asOf, Hak.Application.Loans.LoanScheduleService s) => Results.Ok(await s.ListAsync(await http.SessionAsync(), id, asOf)));
        api.MapPut("/{id:guid}/schedule", async (HttpContext http, Guid id, List<Hak.Domain.Loans.ScheduleDraftItem> items, Hak.Application.Loans.LoanScheduleService s) =>
            Results.Ok(await s.SaveAsync(await http.SessionAsync(), id, items)));
        api.MapGet("/{id:guid}/candidates", async (HttpContext http, Guid id, LoanService s) => Results.Ok(await s.CandidatesAsync(await http.SessionAsync(), id)));
        api.MapPost("/{id:guid}/links", async (HttpContext http, Guid id, LoanLinkRequest r, LoanService s) =>
        { await s.LinkAsync(await http.SessionAsync(), id, r.JournalLineIds); return Results.NoContent(); });
        api.MapGet("/reconciliation", async (HttpContext http, DateOnly asOf, LoanService s) => Results.Ok(await s.ReconcileAsync(await http.SessionAsync(), asOf)));
        api.MapGet("/interest-runs", async (HttpContext http, LoanService s) => Results.Ok(await s.RunsAsync(await http.SessionAsync())));
        api.MapPost("/interest-runs", async (HttpContext http, LoanAccrueRequest r, LoanService s) =>
            Results.Ok(await s.AccrueAsync(await http.SessionAsync(), r.PeriodId, r.Rates)));
        api.MapPost("/interest-runs/{id:guid}/reverse", async (HttpContext http, Guid id, ReasonRequest r, LoanService s) =>
        { await s.ReverseRunAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
    }
}
