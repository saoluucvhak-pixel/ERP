using Hak.Application.Reports;

namespace Hak.Api;

public sealed record ReportRunRequest(Guid DefinitionId, DateOnly FromDate, DateOnly ToDate, DateOnly? CompareFrom, DateOnly? CompareTo, string? Note = null);
public sealed record NoteRequest(string Note);

/// <summary>Report engine (docs/05): templates, runs with drill-down, finalised snapshots.</summary>
public static class ReportEngineEndpoints
{
    public static void MapReportEngineEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/report-definitions");
        api.MapGet("", async (HttpContext http, ReportService s) => Results.Ok(await s.ListAsync(await http.SessionAsync())));
        api.MapGet("/{id:guid}", async (HttpContext http, Guid id, ReportService s) => Results.Ok(await s.ExportAsync(await http.SessionAsync(), id)));
        api.MapPost("", async (HttpContext http, ReportDefinitionInput r, ReportService s) =>
        {
            var d = await s.SaveDraftAsync(await http.SessionAsync(), r);
            return Results.Ok(new { d.Id, d.Version });
        });
        api.MapPost("/{id:guid}/verify", async (HttpContext http, Guid id, NoteRequest r, ReportService s) =>
        { await s.VerifyAsync(await http.SessionAsync(), id, r.Note); return Results.NoContent(); });
        api.MapPost("/{id:guid}/discard", async (HttpContext http, Guid id, ReportService s) =>
        { await s.DiscardDraftAsync(await http.SessionAsync(), id); return Results.NoContent(); });

        var reports = app.MapGroup("/api/report-runs");
        reports.MapPost("", async (HttpContext http, ReportRunRequest r, ReportService s) =>
            Results.Ok(await s.RunAsync(await http.SessionAsync(), r.DefinitionId, r.FromDate, r.ToDate, r.CompareFrom, r.CompareTo)));
        reports.MapPost("/snapshots", async (HttpContext http, ReportRunRequest r, ReportService s) =>
        {
            var snap = await s.SnapshotAsync(await http.SessionAsync(), r.DefinitionId, r.FromDate, r.ToDate, r.CompareFrom, r.CompareTo, r.Note ?? "");
            return Results.Ok(new { snap.Id, snap.Sha256 });
        });
        reports.MapGet("/snapshots", async (HttpContext http, Guid? definitionId, ReportService s) =>
            Results.Ok(await s.SnapshotsAsync(await http.SessionAsync(), definitionId)));
        reports.MapGet("/snapshots/{id:guid}", async (HttpContext http, Guid id, ReportService s) =>
        {
            var (run, valid, info) = await s.SnapshotAsync(await http.SessionAsync(), id);
            return Results.Ok(new { run, hashValid = valid, info });
        });
    }
}
