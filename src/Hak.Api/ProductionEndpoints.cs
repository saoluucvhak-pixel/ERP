using Hak.Application.Production;

namespace Hak.Api;

public sealed record ProductionRunRequest(Guid CostObjectId, Guid PeriodId, decimal ClosingWip, string? Note);

public static class ProductionEndpoints
{
    public static void MapProductionEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/production");
        api.MapGet("/cost-objects", async (HttpContext http, ProductionCostService s) => Results.Ok(await s.CostObjectsAsync(await http.SessionAsync())));
        api.MapPost("/cost-objects", async (HttpContext http, CostObjectInput input, ProductionCostService s) =>
            Results.Ok(await s.CreateCostObjectAsync(await http.SessionAsync(), input)));
        api.MapGet("/cost-runs", async (HttpContext http, ProductionCostService s) => Results.Ok(await s.RunsAsync(await http.SessionAsync())));
        api.MapPost("/cost-runs/preview", async (HttpContext http, ProductionRunRequest r, ProductionCostService s) =>
            Results.Ok(await s.PreviewAsync(await http.SessionAsync(), r.CostObjectId, r.PeriodId, r.ClosingWip)));
        api.MapPost("/cost-runs", async (HttpContext http, ProductionRunRequest r, ProductionCostService s) =>
            Results.Ok(await s.RunAsync(await http.SessionAsync(), r.CostObjectId, r.PeriodId, r.ClosingWip, r.Note ?? "")));
        api.MapPost("/cost-runs/{id:guid}/reverse", async (HttpContext http, Guid id, ReasonRequest r, ProductionCostService s) =>
        { await s.ReverseAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
    }
}
