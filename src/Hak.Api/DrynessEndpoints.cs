using Hak.Application.Inventory;
using Hak.Data;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Api;

public sealed record DrynessMethodRequest(string Code, string Description, int DrynessDecimals, int BdmtDecimals, DateOnly EffectiveFrom, string ApprovalNote);
public sealed record TracksDrynessRequest(bool Tracks);
public sealed record SupersedeMeasurementRequest(MeasurementInput Measurement, string Reason);

public static class DrynessEndpoints
{
    public static void MapDrynessEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/dryness-methods", async (HttpContext http, HakDbContext db) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            return Results.Ok(await db.DrynessMethods.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId).OrderBy(m => m.EffectiveFrom).ToListAsync());
        });
        api.MapPost("/dryness-methods", async (HttpContext http, DrynessMethodRequest r, DrynessService s) =>
            Results.Ok(new { (await s.CreateMethodAsync(await http.SessionAsync(), r.Code, r.Description, r.DrynessDecimals, r.BdmtDecimals, r.EffectiveFrom, r.ApprovalNote)).Id }));
        api.MapPost("/items/{id:guid}/tracks-dryness", async (HttpContext http, Guid id, TracksDrynessRequest r, DrynessService s) =>
        { await s.SetItemTracksDrynessAsync(await http.SessionAsync(), id, r.Tracks); return Results.NoContent(); });
        api.MapPost("/measurements", async (HttpContext http, MeasurementInput r, DrynessService s) =>
        {
            var m = await s.RecordAsync(await http.SessionAsync(), r);
            return Results.Ok(new { m.Id, m.Bdmt });
        });
        api.MapPost("/measurements/{id:guid}/supersede", async (HttpContext http, Guid id, SupersedeMeasurementRequest r, DrynessService s) =>
        {
            var m = await s.SupersedeAsync(await http.SessionAsync(), id, r.Measurement, r.Reason);
            return Results.Ok(new { m.Id, m.Bdmt });
        });
        api.MapGet("/measurements", async (HttpContext http, Guid? documentId, DrynessService s) => Results.Ok(await s.HistoryAsync(await http.SessionAsync(), documentId)));
        api.MapGet("/measurements/pending", async (HttpContext http, DrynessService s) => Results.Ok(await s.PendingAsync(await http.SessionAsync())));
        api.MapGet("/reports/dryness-summary", async (HttpContext http, DateOnly from, DateOnly to, Guid? warehouseId, DrynessService s) =>
            Results.Ok(await s.SummaryAsync(await http.SessionAsync(), from, to, warehouseId)));
    }
}
