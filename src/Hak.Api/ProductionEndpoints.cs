using Hak.Application.Production;

namespace Hak.Api;

public sealed record ProductionRunRequest(Guid CostObjectId, Guid PeriodId, decimal ClosingWip, string? Note);

public sealed record BomRequest(IReadOnlyList<BomLineInput> Lines, string? Note);
public sealed record WorkCenterRequest(Guid? Id, string Code, string Name, decimal LaborRatePerHour, decimal OverheadRatePerHour, bool IsActive = true);
public sealed record IssueRequest(DateOnly Date, IReadOnlyList<BomLineInput>? Lines);
public sealed record ConfirmWorkRequest(Guid WorkCenterId, DateOnly Date, decimal LaborHours, decimal MachineHours, string? Note);
public sealed record CompleteOrderRequest(decimal ActualQuantity, DateOnly Date);

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
        // Chu trình sản xuất: định mức, tổ xưởng, lệnh sản xuất.
        api.MapGet("/boms", async (HttpContext http, ProductionOrderService s) => Results.Ok(await s.BomsAsync(await http.SessionAsync())));
        api.MapPut("/boms/{itemId:guid}", async (HttpContext http, Guid itemId, BomRequest r, ProductionOrderService s) => Results.Ok(await s.SaveBomAsync(await http.SessionAsync(), itemId, r.Lines, r.Note)));
        api.MapGet("/work-centers", async (HttpContext http, ProductionOrderService s) => Results.Ok(await s.WorkCentersAsync(await http.SessionAsync())));
        api.MapPost("/work-centers", async (HttpContext http, WorkCenterRequest r, ProductionOrderService s) =>
            Results.Ok(await s.SaveWorkCenterAsync(await http.SessionAsync(), r.Id, r.Code, r.Name, r.LaborRatePerHour, r.OverheadRatePerHour, r.IsActive)));
        api.MapGet("/orders", async (HttpContext http, ProductionOrderService s) => Results.Ok(await s.ListAsync(await http.SessionAsync())));
        api.MapPost("/orders", async (HttpContext http, ProductionOrderInput i, ProductionOrderService s) => Results.Ok(await s.CreateAsync(await http.SessionAsync(), i)));
        api.MapPost("/orders/{id:guid}/release", async (HttpContext http, Guid id, ProductionOrderService s) => Results.Ok(await s.ReleaseAsync(await http.SessionAsync(), id)));
        api.MapPost("/orders/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, ProductionOrderService s) => Results.Ok(await s.CancelAsync(await http.SessionAsync(), id, r.Reason)));
        api.MapGet("/orders/{id:guid}/requirements", async (HttpContext http, Guid id, ProductionOrderService s) => Results.Ok(await s.RequirementsAsync(await http.SessionAsync(), id)));
        api.MapGet("/orders/{id:guid}/issues", async (HttpContext http, Guid id, ProductionOrderService s) => Results.Ok(await s.IssuesAsync(await http.SessionAsync(), id)));
        api.MapPost("/orders/{id:guid}/issues", async (HttpContext http, Guid id, IssueRequest r, ProductionOrderService s) =>
        { var d = await s.CreateIssueAsync(await http.SessionAsync(), id, r.Lines, r.Date); return Results.Ok(new { d.Id, d.Number }); });
        api.MapGet("/orders/{id:guid}/confirmations", async (HttpContext http, Guid id, ProductionOrderService s) => Results.Ok(await s.ConfirmationsAsync(await http.SessionAsync(), id)));
        api.MapPost("/orders/{id:guid}/confirmations", async (HttpContext http, Guid id, ConfirmWorkRequest r, ProductionOrderService s) =>
            Results.Ok(await s.ConfirmWorkAsync(await http.SessionAsync(), id, r.WorkCenterId, r.Date, r.LaborHours, r.MachineHours, r.Note)));
        api.MapPost("/confirmations/{id:guid}/void", async (HttpContext http, Guid id, ReasonRequest r, ProductionOrderService s) =>
        { await s.VoidConfirmationAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        api.MapGet("/orders/{id:guid}/cost-sheet", async (HttpContext http, Guid id, ProductionOrderService s) => Results.Ok(await s.CostSheetAsync(await http.SessionAsync(), id)));
        api.MapPost("/orders/{id:guid}/complete", async (HttpContext http, Guid id, CompleteOrderRequest r, ProductionOrderService s) =>
            Results.Ok(await s.CompleteAsync(await http.SessionAsync(), id, r.ActualQuantity, r.Date)));
        api.MapPost("/orders/{id:guid}/close", async (HttpContext http, Guid id, ProductionOrderService s) => Results.Ok(await s.CloseAsync(await http.SessionAsync(), id)));

        api.MapPost("/cost-runs/{id:guid}/reverse", async (HttpContext http, Guid id, ReasonRequest r, ProductionCostService s) =>
        { await s.ReverseAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
    }
}
