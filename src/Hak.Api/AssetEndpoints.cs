using Hak.Application.Assets;

namespace Hak.Api;

public sealed record DisposeAssetRequest(DateOnly Date, Guid ResidualAccountId, string Description);

public sealed record AssetDecisionRequest(bool Approve, string? Note);
public sealed record AssetLinkRequest(Guid AssetId);
public sealed record MaintenanceRequestInput(Guid AssetId, Hak.Domain.Assets.MaintenancePriority Priority, string Description);
public sealed record RepairIssueRequest(Guid WarehouseId, Guid CounterAccountId, DateOnly Date);
public sealed record RepairTestRequest(decimal ActualLaborCost, string? Note);
public sealed record RepairAcceptRequest(bool Passed, string? Note);
public sealed record PreventiveScheduleRequest(Guid? Id, Guid AssetId, string TaskName, int FrequencyDays, DateOnly NextDueDate, bool IsActive = true);
public sealed record AssetUsageRequest(Guid PeriodId, decimal Quantity);
public sealed record AssetFrequencyRequest(Hak.Domain.Assets.AllocationFrequency Frequency);
public sealed record AssetAssignRequest(string Department, string? Custodian, string? Location, DateOnly Date, string? Note);
public sealed record AssetCountCreateRequest(DateOnly Date, string? Note);

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/assets");
        api.MapGet("", async (HttpContext http, DateOnly asOf, AssetService s) => Results.Ok(await s.RegisterAsync(await http.SessionAsync(), asOf)));
        api.MapPost("", async (HttpContext http, AssetInput input, AssetService s) => Results.Ok(await s.CreateAsync(await http.SessionAsync(), input)));
        api.MapGet("/{id:guid}", async (HttpContext http, Guid id, AssetService s) => Results.Ok(await s.DetailAsync(await http.SessionAsync(), id)));
        api.MapPost("/{id:guid}/activate", async (HttpContext http, Guid id, AssetService s) => Results.Ok(await s.ActivateAsync(await http.SessionAsync(), id)));
        api.MapPost("/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, AssetService s) =>
        { await s.CancelDraftAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        api.MapPost("/{id:guid}/dispose", async (HttpContext http, Guid id, DisposeAssetRequest r, AssetService s) =>
            Results.Ok(await s.DisposeAsync(await http.SessionAsync(), id, r.Date, r.ResidualAccountId, r.Description)));
        api.MapPost("/{id:guid}/dispose/reverse", async (HttpContext http, Guid id, ReasonRequest r, AssetService s) =>
            Results.Ok(await s.ReverseDisposalAsync(await http.SessionAsync(), id, r.Reason)));
        api.MapGet("/cost-candidates", async (HttpContext http, Guid accountId, AssetService s) =>
            Results.Ok(await s.CostCandidatesAsync(await http.SessionAsync(), accountId)));
        api.MapGet("/{id:guid}/expense-splits", async (HttpContext http, Guid id, AssetService s) => Results.Ok(await s.ExpenseSplitsAsync(await http.SessionAsync(), id)));
        api.MapPut("/{id:guid}/expense-splits", async (HttpContext http, Guid id, IReadOnlyList<ExpenseSplitInput> r, AssetService s) => Results.Ok(await s.SetExpenseSplitsAsync(await http.SessionAsync(), id, r)));
        api.MapGet("/{id:guid}/usage", async (HttpContext http, Guid id, AssetService s) => Results.Ok(await s.UsageAsync(await http.SessionAsync(), id)));
        api.MapPut("/{id:guid}/usage", async (HttpContext http, Guid id, AssetUsageRequest r, AssetService s) => Results.Ok(await s.RecordUsageAsync(await http.SessionAsync(), id, r.PeriodId, r.Quantity)));
        api.MapPut("/{id:guid}/frequency", async (HttpContext http, Guid id, AssetFrequencyRequest r, AssetService s) => { await s.SetFrequencyAsync(await http.SessionAsync(), id, r.Frequency); return Results.NoContent(); });
        api.MapGet("/expense-allocation", async (HttpContext http, DateOnly from, DateOnly to, AssetService s) => Results.Ok(await s.ExpenseAllocationAsync(await http.SessionAsync(), from, to)));
        api.MapGet("/reconciliation", async (HttpContext http, DateOnly asOf, AssetService s) =>
            Results.Ok(await s.ReconcileAsync(await http.SessionAsync(), asOf)));

        // Chu trình quản lý tài sản: đề nghị mua sắm → bàn giao → kiểm kê.
        api.MapGet("/requests", async (HttpContext http, AssetCycleService s) => Results.Ok(await s.RequestsAsync(await http.SessionAsync())));
        api.MapPost("/requests", async (HttpContext http, AssetRequestInput i, AssetCycleService s) => Results.Ok(await s.RequestAsync(await http.SessionAsync(), i)));
        api.MapPost("/requests/{id:guid}/decide", async (HttpContext http, Guid id, AssetDecisionRequest r, AssetCycleService s) =>
            Results.Ok(await s.DecideAsync(await http.SessionAsync(), id, r.Approve, r.Note)));
        api.MapPost("/requests/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, AssetCycleService s) =>
            Results.Ok(await s.CancelRequestAsync(await http.SessionAsync(), id, r.Reason)));
        api.MapPost("/requests/{id:guid}/link", async (HttpContext http, Guid id, AssetLinkRequest r, AssetCycleService s) =>
            Results.Ok(await s.LinkAssetAsync(await http.SessionAsync(), id, r.AssetId)));
        api.MapGet("/assignments", async (HttpContext http, AssetCycleService s) => Results.Ok(await s.CurrentAsync(await http.SessionAsync())));
        api.MapGet("/{id:guid}/assignments", async (HttpContext http, Guid id, AssetCycleService s) => Results.Ok(await s.AssignmentHistoryAsync(await http.SessionAsync(), id)));
        api.MapPost("/{id:guid}/assign", async (HttpContext http, Guid id, AssetAssignRequest r, AssetCycleService s) =>
            Results.Ok(await s.AssignAsync(await http.SessionAsync(), id, r.Department, r.Custodian, r.Location, r.Date, r.Note)));
        api.MapGet("/counts", async (HttpContext http, AssetCycleService s) => Results.Ok(await s.CountsAsync(await http.SessionAsync())));
        api.MapPost("/counts", async (HttpContext http, AssetCountCreateRequest r, AssetCycleService s) => Results.Ok(await s.CreateCountAsync(await http.SessionAsync(), r.Date, r.Note)));
        api.MapPut("/counts/{id:guid}/lines", async (HttpContext http, Guid id, IReadOnlyList<AssetCountLineInput> lines, AssetCycleService s) =>
            Results.Ok(await s.RecordAsync(await http.SessionAsync(), id, lines)));
        api.MapPost("/counts/{id:guid}/complete", async (HttpContext http, Guid id, AssetCycleService s) => Results.Ok(await s.CompleteAsync(await http.SessionAsync(), id)));
        api.MapPost("/counts/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, AssetCycleService s) =>
        { await s.CancelCountAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });

        // Bảo trì, sửa chữa (CMMS) và Nhật ký tài sản.
        api.MapGet("/maintenance/requests", async (HttpContext http, Guid? assetId, AssetRepairService s) => Results.Ok(await s.RequestsAsync(await http.SessionAsync(), assetId)));
        api.MapPost("/maintenance/requests", async (HttpContext http, MaintenanceRequestInput i, AssetRepairService s) => Results.Ok(await s.RequestAsync(await http.SessionAsync(), i.AssetId, i.Priority, i.Description)));
        api.MapPost("/maintenance/requests/{id:guid}/reject", async (HttpContext http, Guid id, ReasonRequest r, AssetRepairService s) => Results.Ok(await s.RejectRequestAsync(await http.SessionAsync(), id, r.Reason)));
        api.MapGet("/repairs", async (HttpContext http, Guid? assetId, AssetRepairService s) => Results.Ok(await s.ListAsync(await http.SessionAsync(), assetId)));
        api.MapPost("/repairs", async (HttpContext http, AssetRepairInput i, AssetRepairService s) => Results.Ok(await s.CreateAsync(await http.SessionAsync(), i)));
        api.MapPut("/repairs/{id:guid}/plan", async (HttpContext http, Guid id, AssetRepairInput i, AssetRepairService s) => Results.Ok(await s.UpdatePlanAsync(await http.SessionAsync(), id, i)));
        api.MapPost("/repairs/{id:guid}/submit", async (HttpContext http, Guid id, AssetRepairService s) => Results.Ok(await s.SubmitAsync(await http.SessionAsync(), id)));
        api.MapPost("/repairs/{id:guid}/decide", async (HttpContext http, Guid id, AssetDecisionRequest r, AssetRepairService s) => Results.Ok(await s.DecideAsync(await http.SessionAsync(), id, r.Approve, r.Note)));
        api.MapPost("/repairs/{id:guid}/start", async (HttpContext http, Guid id, AssetRepairService s) => Results.Ok(await s.StartAsync(await http.SessionAsync(), id)));
        api.MapPost("/repairs/{id:guid}/issue", async (HttpContext http, Guid id, RepairIssueRequest r, AssetRepairService s) => Results.Ok(await s.IssueMaterialsAsync(await http.SessionAsync(), id, r.WarehouseId, r.CounterAccountId, r.Date)));
        api.MapPost("/repairs/{id:guid}/test", async (HttpContext http, Guid id, RepairTestRequest r, AssetRepairService s) => Results.Ok(await s.RequestAcceptanceAsync(await http.SessionAsync(), id, r.ActualLaborCost, r.Note)));
        api.MapPost("/repairs/{id:guid}/accept", async (HttpContext http, Guid id, RepairAcceptRequest r, AssetRepairService s) => Results.Ok(await s.AcceptAsync(await http.SessionAsync(), id, r.Passed, r.Note)));
        api.MapPost("/repairs/{id:guid}/close", async (HttpContext http, Guid id, AssetRepairService s) => Results.Ok(await s.CloseAsync(await http.SessionAsync(), id)));
        api.MapPost("/repairs/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, AssetRepairService s) => Results.Ok(await s.CancelAsync(await http.SessionAsync(), id, r.Reason)));
        api.MapGet("/maintenance/schedules", async (HttpContext http, DateOnly today, AssetRepairService s) => Results.Ok(await s.SchedulesAsync(await http.SessionAsync(), today)));
        api.MapPost("/maintenance/schedules", async (HttpContext http, PreventiveScheduleRequest r, AssetRepairService s) => Results.Ok(await s.SaveScheduleAsync(await http.SessionAsync(), r.Id, r.AssetId, r.TaskName, r.FrequencyDays, r.NextDueDate, r.IsActive)));
        api.MapPost("/maintenance/schedules/{id:guid}/work-order", async (HttpContext http, Guid id, DateOnly date, AssetRepairService s) => Results.Ok(await s.CreateFromScheduleAsync(await http.SessionAsync(), id, date)));
        api.MapGet("/maintenance/kpi", async (HttpContext http, DateOnly from, DateOnly to, AssetRepairService s) => Results.Ok(await s.KpiAsync(await http.SessionAsync(), from, to)));
        api.MapGet("/{id:guid}/journal", async (HttpContext http, Guid id, AssetJournalService s) => Results.Ok(await s.GetAsync(await http.SessionAsync(), id)));

        api.MapGet("/depreciation-runs", async (HttpContext http, AssetService s) => Results.Ok(await s.RunsAsync(await http.SessionAsync())));
        api.MapPost("/depreciation-runs", async (HttpContext http, Guid periodId, AssetService s) => Results.Ok(await s.RunAsync(await http.SessionAsync(), periodId)));
        api.MapPost("/depreciation-runs/{id:guid}/reverse", async (HttpContext http, Guid id, ReasonRequest r, AssetService s) =>
        { await s.ReverseRunAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
    }
}
