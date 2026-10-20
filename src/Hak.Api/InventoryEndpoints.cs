using Hak.Application.Inventory;
using Hak.Data;
using Hak.Domain.Inventory;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Api;

public sealed record WarehouseRequest(string Code, string Name);
public sealed record ItemCommercialRequest(decimal? SalePrice, decimal? DefaultVatRate, decimal? MinStock, decimal? ReorderQty);
public sealed record ItemAltUnitRequest(string? AltUnit, decimal? AltFactor);
public sealed record ItemRequest(string Code, string Name, string Unit, string InventoryAccountCode);
public sealed record CostingPolicyRequest(Guid WarehouseId, CostingMethod Method, DateOnly EffectiveFrom, string Note);
public sealed record CostingRunRequest(Guid WarehouseId, Guid PeriodId);

public sealed record RequiresQcRequest(bool Requires);

public static class InventoryEndpoints
{
    private static object Dto(StockDocument d) => new
    {
        d.Id, d.Number, type = d.Type.ToString(), d.WarehouseId, d.ToWarehouseId, d.DocumentDate, d.PostingDate, d.Description, d.PartnerId,
        status = d.Status.ToString(), d.VoucherId, d.ReversesDocumentId, d.CreatedBy, d.RowVersion,
        lines = d.Lines.OrderBy(l => l.LineNo).Select(l => new { l.LineNo, l.ItemId, l.Quantity, l.Amount, l.CounterAccountId, l.Description, l.OrderId, l.OrderLineNo }),
    };

    public static void MapInventoryEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/warehouses", async (HttpContext http, HakDbContext db) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            var whs = await db.Warehouses.AsNoTracking().Where(w => w.CompanyId == ctx.CompanyId).OrderBy(w => w.Code).ToListAsync();
            var policies = await db.CostingPolicies.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToListAsync();
            return Results.Ok(whs.Select(w => new
            {
                w.Id, w.Code, w.Name, w.IsActive,
                policies = policies.Where(p => p.WarehouseId == w.Id).OrderBy(p => p.EffectiveFrom).Select(p => new { method = p.Method.ToString(), p.EffectiveFrom, p.Note }),
            }));
        });
        api.MapPost("/warehouses", async (HttpContext http, WarehouseRequest r, StockService s) => Results.Ok(new { (await s.CreateWarehouseAsync(await http.SessionAsync(), r.Code, r.Name)).Id }));
        api.MapPost("/warehouses/costing-policy", async (HttpContext http, CostingPolicyRequest r, StockService s) =>
            Results.Ok(new { (await s.SetCostingPolicyAsync(await http.SessionAsync(), r.WarehouseId, r.Method, r.EffectiveFrom, r.Note)).Id }));
        api.MapGet("/items", async (HttpContext http, HakDbContext db) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            return Results.Ok(await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).OrderBy(i => i.Code)
                .Select(i => new { i.Id, i.Code, i.Name, i.Unit, i.InventoryAccountId, i.IsActive, i.TracksDryness, i.RequiresQc, i.IssueDrynessBasis, i.AltUnit, i.AltFactor, i.SalePrice, i.DefaultVatRate, i.MinStock, i.ReorderQty }).ToListAsync());
        });
        api.MapPut("/items/{id:guid}/commercial", async (HttpContext http, Guid id, ItemCommercialRequest r, StockService s) =>
        { await s.UpdateItemCommercialAsync(await http.SessionAsync(), id, r.SalePrice, r.DefaultVatRate, r.MinStock, r.ReorderQty); return Results.NoContent(); });
        api.MapPut("/items/{id:guid}/alt-unit", async (HttpContext http, Guid id, ItemAltUnitRequest r, StockService s) =>
        { await s.SetItemAltUnitAsync(await http.SessionAsync(), id, r.AltUnit, r.AltFactor); return Results.NoContent(); });
        api.MapPost("/items/{id:guid}/requires-qc", async (HttpContext http, Guid id, RequiresQcRequest r, StockService s) => { await s.SetItemRequiresQcAsync(await http.SessionAsync(), id, r.Requires); return Results.NoContent(); });
        api.MapPost("/items", async (HttpContext http, ItemRequest r, StockService s) =>
            Results.Ok(new { (await s.CreateItemAsync(await http.SessionAsync(), r.Code, r.Name, r.Unit, r.InventoryAccountCode)).Id }));

        api.MapGet("/stock-documents", async (HttpContext http, HakDbContext db, DateOnly? from, DateOnly? to) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            var q = db.StockDocuments.AsNoTracking().Include(d => d.Lines).Where(d => d.CompanyId == ctx.CompanyId);
            if (from is not null) q = q.Where(d => d.PostingDate >= from);
            if (to is not null) q = q.Where(d => d.PostingDate <= to);
            return Results.Ok((await q.OrderBy(d => d.PostingDate).ThenBy(d => d.Number).Take(1000).ToListAsync()).Select(Dto));
        });
        api.MapPost("/stock-documents", async (HttpContext http, StockDocumentInput r, StockService s) => Results.Ok(Dto(await s.CreateDocumentAsync(await http.SessionAsync(), r))));
        api.MapPost("/stock-documents/{id:guid}/post", async (HttpContext http, Guid id, StockService s) => Results.Ok(Dto(await s.PostDocumentAsync(await http.SessionAsync(), id))));
        api.MapPost("/stock-documents/{id:guid}/reverse", async (HttpContext http, Guid id, ReverseRequest r, StockService s) =>
            Results.Ok(Dto(await s.ReverseDocumentAsync(await http.SessionAsync(), id, r.ReversalDate, r.Reason))));

        api.MapGet("/costing-runs", async (HttpContext http, HakDbContext db) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            return Results.Ok(await db.CostingRuns.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId).OrderByDescending(r => r.PeriodStart)
                .Select(r => new { r.Id, r.WarehouseId, r.PeriodId, r.PeriodStart, r.PeriodEnd, status = r.Status.ToString(), r.VoucherId, r.ResultJson, r.CreatedAt }).ToListAsync());
        });
        api.MapPost("/costing-runs", async (HttpContext http, CostingRunRequest r, StockService s) =>
        {
            var run = await s.RunMonthlyCostingAsync(await http.SessionAsync(), r.WarehouseId, r.PeriodId);
            return Results.Ok(new { run.Id, run.VoucherId, run.ResultJson });
        });
        api.MapPost("/costing-runs/{id:guid}/reverse", async (HttpContext http, Guid id, ReasonRequest r, StockService s) =>
        { await s.ReverseCostingRunAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });

        api.MapGet("/reports/inventory-summary", async (HttpContext http, DateOnly from, DateOnly to, Guid? warehouseId, StockService s) =>
            Results.Ok(await s.SummaryAsync(await http.SessionAsync(), from, to, warehouseId)));
        api.MapGet("/reports/stock-card", async (HttpContext http, Guid warehouseId, Guid itemId, DateOnly from, DateOnly to, StockService s) =>
            Results.Ok(await s.StockCardAsync(await http.SessionAsync(), warehouseId, itemId, from, to)));
        api.MapGet("/reports/inventory-reconciliation", async (HttpContext http, DateOnly asOf, StockService s) =>
            Results.Ok(await s.ReconcileAsync(await http.SessionAsync(), asOf)));
    }
}
