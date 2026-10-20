using Hak.Application.Trade;
using Hak.Domain.Trade;

namespace Hak.Api;

public sealed record MilestonesRequest(IReadOnlyList<MilestoneInput> Milestones);
public sealed record CompleteMilestoneRequest(DateOnly Date);
public sealed record TimeEntryRequest(Guid OrderId, int OrderLineNo, DateOnly WorkDate, string Worker, decimal Hours, string? Note);
public sealed record CloseTimesheetRequest(Guid OrderId, int OrderLineNo, DateOnly Through, decimal HoursPerUnit, DateOnly AcceptanceDate);
public sealed record AcceptancePlanRequest(IReadOnlyList<AcceptancePlanInput> Plan);

public static class ServiceAcceptanceEndpoints
{
    public static void MapServiceAcceptanceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/contracts/{id:guid}/acceptance-plan", async (HttpContext http, Guid id, ContractServicePlanService s) => Results.Ok(await s.PlanAsync(await http.SessionAsync(), id)));
        app.MapPut("/api/contracts/{id:guid}/acceptance-plan", async (HttpContext http, Guid id, AcceptancePlanRequest r, ContractServicePlanService s) => { await s.SetPlanAsync(await http.SessionAsync(), id, r.Plan); return Results.NoContent(); });
        app.MapGet("/api/service-acceptances/plan-overdue", async (HttpContext http, InvoiceKind? kind, ContractServicePlanService s) => Results.Ok(await s.OverdueAsync(await http.SessionAsync(), kind)));
        var p = app.MapGroup("/api/service-progress");
        p.MapGet("/milestones", async (HttpContext http, Guid? orderId, ServiceProgressService s) => Results.Ok(await s.MilestonesAsync(await http.SessionAsync(), orderId)));
        p.MapPut("/milestones/{orderId:guid}/{lineNo:int}", async (HttpContext http, Guid orderId, int lineNo, MilestonesRequest r, ServiceProgressService s) => { await s.SetMilestonesAsync(await http.SessionAsync(), orderId, lineNo, r.Milestones); return Results.NoContent(); });
        p.MapPost("/milestones/{id:guid}/complete", async (HttpContext http, Guid id, CompleteMilestoneRequest r, ServiceProgressService s) => Results.Ok(new { (await s.CompleteMilestoneAsync(await http.SessionAsync(), id, r.Date)).Id }));
        p.MapGet("/time", async (HttpContext http, Guid? orderId, bool? openOnly, ServiceProgressService s) => Results.Ok(await s.TimeEntriesAsync(await http.SessionAsync(), orderId, openOnly ?? false)));
        p.MapPost("/time", async (HttpContext http, TimeEntryRequest r, ServiceProgressService s) => Results.Ok(new { (await s.AddTimeAsync(await http.SessionAsync(), r.OrderId, r.OrderLineNo, r.WorkDate, r.Worker, r.Hours, r.Note)).Id }));
        p.MapDelete("/time/{id:guid}", async (HttpContext http, Guid id, ServiceProgressService s) => { await s.DeleteTimeAsync(await http.SessionAsync(), id); return Results.NoContent(); });
        p.MapPost("/time/close", async (HttpContext http, CloseTimesheetRequest r, ServiceProgressService s) => Results.Ok(new { (await s.CloseTimesheetAsync(await http.SessionAsync(), r.OrderId, r.OrderLineNo, r.Through, r.HoursPerUnit, r.AcceptanceDate)).Id }));
        var a = app.MapGroup("/api/service-acceptances");
        a.MapGet("", async (HttpContext http, InvoiceKind? kind, Guid? orderId, ServiceAcceptanceService s) => Results.Ok(await s.ListAsync(await http.SessionAsync(), kind, orderId)));
        a.MapGet("/report", async (HttpContext http, InvoiceKind kind, DateOnly fromDate, DateOnly toDate, ServiceReportService s) => Results.Ok(await s.ReportAsync(await http.SessionAsync(), kind, fromDate, toDate)));
        a.MapGet("/invoice-candidates", async (HttpContext http, InvoiceKind kind, Guid? partnerId, ServiceAcceptanceService s) => Results.Ok(await s.InvoiceCandidatesAsync(await http.SessionAsync(), kind, partnerId)));
        a.MapPost("", async (HttpContext http, ServiceAcceptanceInput i, ServiceAcceptanceService s) => Results.Ok(new { (await s.CreateAsync(await http.SessionAsync(), i)).Id }));
        a.MapPost("/{id:guid}/decide", async (HttpContext http, Guid id, ContractDecisionRequest r, ServiceAcceptanceService s) => Results.Ok(new { (await s.DecideAsync(await http.SessionAsync(), id, r.Approve, r.Note)).Id }));
        a.MapPost("/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, ServiceAcceptanceService s) => { await s.CancelAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
    }
}
