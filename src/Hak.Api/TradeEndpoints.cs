using Hak.Application.Trade;
using Hak.Data;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Api;

public sealed record ReturnReceiptRequest(int OriginalLineNo, decimal Quantity, DateOnly Date, Guid? WarehouseId, string? Description);

public sealed record ConfirmOrderRequest(string? CreditOverrideReason);

/// <summary>Yêu cầu lập hóa đơn điều chỉnh. Cố ý KHÔNG có cờ "theo thỏa thuận" của AdjustmentInput: cờ đó bỏ qua phê duyệt từng dòng nên chỉ dịch vụ thỏa thuận chiết khấu được đặt.</summary>
public sealed record AdjustmentRequest(Guid OriginalInvoiceId, InvoicePurpose Purpose, string InvoiceSeries, string InvoiceNo, DateOnly InvoiceDate, DateOnly PostingDate,
    string Description, IReadOnlyList<AdjustmentLineInput> Lines, DiscountKind? DiscountKind = null);

public static class TradeEndpoints
{
    private static object Dto(CommercialInvoice i) => new
    {
        i.Id, i.Number, kind = i.Kind.ToString(), purpose = i.Purpose.ToString(), discountKind = i.DiscountKind?.ToString(), i.AdjustsInvoiceId, i.InvoiceSeries, i.InvoiceNo, i.InvoiceDate, i.PostingDate, i.DueDate, i.PartnerId, i.Currency, i.ExchangeRate,
        i.Description, status = i.Status.ToString(), i.VoucherId, i.CreatedBy, i.RowVersion,
        total = i.Lines.Sum(l => l.FunctionalAmount), vat = i.Lines.Sum(l => l.VatAmount), tradeDiscount = i.Lines.Sum(l => l.FunctionalDiscountAmount),
        lines = i.Lines.OrderBy(l => l.LineNo).Select(l => new
        {
            l.LineNo, l.AdjustsLineNo, l.ItemId, l.StockDocumentId, l.StockLineNo, l.StockQuantity, l.AccountId, basis = l.Basis.ToString(), l.BasisQuantity, l.UnitPrice,
            l.Amount, l.FunctionalAmount, l.DiscountPercent, l.DiscountAmount, l.FunctionalDiscountAmount, l.VatRate, l.VatAmount, l.Description,
        }),
    };

    private const int DefaultCap = 500, MaxPageSize = 200;

    private static async Task<IResult> PagedAsync<T, R>(HttpContext http, IQueryable<T> ordered, Task<int> totalTask, int? page, int? pageSize, Func<T, R> map)
    {
        var total = await totalTask;
        if (page is null)
        {
            var rows = await ordered.Take(DefaultCap).ToListAsync();
            http.Response.Headers["X-Total-Count"] = total.ToString();
            if (total > DefaultCap) http.Response.Headers["X-Truncated"] = "true";
            return Results.Ok(rows.Select(map));
        }
        var size = Math.Clamp(pageSize ?? 50, 1, MaxPageSize);
        var no = Math.Max(page.Value, 1);
        var items = await ordered.Skip((no - 1) * size).Take(size).ToListAsync();
        return Results.Ok(new { items = items.Select(map), total, page = no, pageSize = size });
    }

    public static void MapTradeEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        // Không có `page` → mảng như cũ (tối đa 500 dòng mới nhất; X-Total-Count / X-Truncated báo nếu bị cắt).
        // Có `page` (từ 1) → { items, total, page, pageSize } lọc và phân trang ở máy chủ.
        api.MapGet("/invoices", async (HttpContext http, HakDbContext db, InvoiceKind? kind, InvoiceStatus? status, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "from")] DateOnly? dateFrom, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "to")] DateOnly? dateTo, string? search, int? page, int? pageSize) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            var q = db.CommercialInvoices.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId);
            if (kind is not null) q = q.Where(i => i.Kind == kind);
            if (status is not null) q = q.Where(i => i.Status == status);
            if (dateFrom is not null) q = q.Where(i => i.PostingDate >= dateFrom);
            if (dateTo is not null) q = q.Where(i => i.PostingDate <= dateTo);
            if (!string.IsNullOrWhiteSpace(search)) { var t = search.Trim(); q = q.Where(i => i.Number.Contains(t) || i.InvoiceNo.Contains(t)); }
            var ordered = q.OrderByDescending(i => i.PostingDate).ThenByDescending(i => i.Number);
            return await PagedAsync(http, ordered.Include(i => i.Lines), q.CountAsync(), page, pageSize, i => Dto(i));
        });
        // Giá trị SAU điều chỉnh của hóa đơn gốc đã ghi sổ: hóa đơn điều chỉnh (đã ghi sổ) điều chỉnh số liệu của hóa đơn gốc — trả hàng / giảm giá / chiết khấu trừ, điều chỉnh tăng cộng. Hóa đơn gốc không bị sửa.
        api.MapGet("/invoices/adjusted", async (HttpContext http, HakDbContext db, string ids) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            var list = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Take(200).ToList();
            var adj = await (from i in db.CommercialInvoices.AsNoTracking()
                             join l in db.CommercialInvoiceLines.AsNoTracking() on i.Id equals l.InvoiceId
                             where i.CompanyId == ctx.CompanyId && i.Status == InvoiceStatus.Posted && i.AdjustsInvoiceId != null && list.Contains(i.AdjustsInvoiceId.Value)
                             select new { Orig = i.AdjustsInvoiceId!.Value, i.Purpose, l.FunctionalAmount, l.VatAmount }).ToListAsync();
            var result = adj.GroupBy(x => x.Orig).ToDictionary(g => g.Key, g => new
            {
                amount = g.Sum(x => x.Purpose == InvoicePurpose.Increase ? x.FunctionalAmount : -x.FunctionalAmount),
                vat = g.Sum(x => x.Purpose == InvoicePurpose.Increase ? x.VatAmount : -x.VatAmount),
            });
            return Results.Ok(result);
        });
        api.MapPost("/invoices", async (HttpContext http, InvoiceInput r, InvoiceService s) => Results.Ok(Dto(await s.CreateAsync(await http.SessionAsync(), r))));
        api.MapGet("/invoices/{id:guid}", async (HttpContext http, Guid id, HakDbContext db) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            var inv = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == ctx.CompanyId)
                      ?? throw new Hak.Domain.Common.DomainException(Hak.Domain.Common.ErrorCodes.NotFound, "Hóa đơn không tồn tại.");
            return Results.Ok(Dto(inv));
        });
        api.MapGet("/invoices/{id:guid}/price-differences", async (HttpContext http, Guid id, InvoiceService s) => Results.Ok(await s.PriceDifferencesAsync(await http.SessionAsync(), id)));
        api.MapPost("/invoices/{id:guid}/post", async (HttpContext http, Guid id, long rowVersion, bool? acknowledgePriceDifferences, InvoiceService s) =>   // rowVersion bắt buộc: thiếu → 400, cũ → 409
            Results.Ok(Dto(await s.PostAsync(await http.SessionAsync(), id, acknowledgePriceDifferences ?? false, rowVersion))));
        api.MapPost("/invoices/{id:guid}/cancel", async (HttpContext http, Guid id, long rowVersion, ReasonRequest r, InvoiceService s) => Results.Ok(Dto(await s.CancelAsync(await http.SessionAsync(), id, r.Reason, rowVersion))));
        api.MapPost("/invoices/{id:guid}/reverse", async (HttpContext http, Guid id, long rowVersion, ReverseRequest r, InvoiceService s) =>
            Results.Ok(Dto(await s.ReverseAsync(await http.SessionAsync(), id, r.ReversalDate, r.Reason, rowVersion))));
        // Purchase return, step 1: a Draft issue back to the supplier (valued by the warehouse costing method when posted).
        api.MapPost("/invoices/{id:guid}/return-issue", async (HttpContext http, Guid id, ReturnReceiptRequest r, InvoiceService s) =>
        {
            var d = await s.CreatePurchaseReturnIssueAsync(await http.SessionAsync(), id, r.OriginalLineNo, r.Quantity, r.Date, r.Description);
            return Results.Ok(new { d.Id, d.Number });
        });
        api.MapPost("/invoices/{id:guid}/return-receipt", async (HttpContext http, Guid id, ReturnReceiptRequest r, InvoiceService s) =>
        {
            var d = await s.CreateReturnReceiptAsync(await http.SessionAsync(), id, r.OriginalLineNo, r.Quantity, r.Date, r.WarehouseId, r.Description);
            return Results.Ok(new { d.Id, d.Number, value = d.Lines[0].Amount });
        });
        api.MapPost("/invoices/adjustments", async (HttpContext http, AdjustmentRequest r, InvoiceService s) => Results.Ok(Dto(await s.CreateAdjustmentAsync(await http.SessionAsync(),
            new AdjustmentInput(r.OriginalInvoiceId, r.Purpose, r.InvoiceSeries, r.InvoiceNo, r.InvoiceDate, r.PostingDate, r.Description, r.Lines, r.DiscountKind)))));
        api.MapGet("/orders", async (HttpContext http, HakDbContext db, InvoiceKind? kind, OrderStatus? status, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "from")] DateOnly? dateFrom, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "to")] DateOnly? dateTo, string? search, int? page, int? pageSize) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Permissions.VoucherView);
            var q = db.TradeOrders.AsNoTracking().Where(o => o.CompanyId == ctx.CompanyId);
            if (kind is not null) q = q.Where(o => o.Kind == kind);
            if (status is not null) q = q.Where(o => o.Status == status);
            if (dateFrom is not null) q = q.Where(o => o.OrderDate >= dateFrom);
            if (dateTo is not null) q = q.Where(o => o.OrderDate <= dateTo);
            if (!string.IsNullOrWhiteSpace(search)) { var t = search.Trim(); q = q.Where(o => o.Number.Contains(t) || (o.ContractNo != null && o.ContractNo.Contains(t))); }
            var ordered = q.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Number);
            return await PagedAsync(http, ordered.Include(o => o.Lines), q.CountAsync(), page, pageSize, o => new
            {
                o.Id, o.Number, kind = o.Kind.ToString(), o.PartnerId, o.ContractNo, o.OrderDate, o.ExpectedDate, o.Currency, o.Description,
                o.OverDeliveryTolerancePercent, status = o.Status.ToString(), o.StatusReason, o.CreatedBy,
                lines = o.Lines.OrderBy(l => l.LineNo).Select(l => new { l.LineNo, l.ItemId, l.Quantity, basis = l.Basis.ToString(), l.UnitPrice, l.VatRate, l.Description }),
            });
        });
        api.MapPost("/orders", async (HttpContext http, OrderInput r, OrderService s) => Results.Ok(new { (await s.CreateAsync(await http.SessionAsync(), r)).Id }));
        api.MapPost("/orders/{id:guid}/confirm", async (HttpContext http, Guid id, ConfirmOrderRequest? r, OrderService s) => { await s.ConfirmAsync(await http.SessionAsync(), id, r?.CreditOverrideReason); return Results.NoContent(); });
        api.MapPost("/orders/{id:guid}/close", async (HttpContext http, Guid id, ReasonRequest r, OrderService s) => { await s.CloseAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        api.MapPost("/orders/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, OrderService s) => { await s.CancelAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        api.MapGet("/orders/{id:guid}/fulfilment", async (HttpContext http, Guid id, OrderService s) => Results.Ok(await s.FulfilmentAsync(await http.SessionAsync(), id)));
        api.MapPost("/stock-documents/{id:guid}/cancel", async (HttpContext http, Guid id, long rowVersion, ReasonRequest r, Hak.Application.Inventory.StockService s) =>
        { await s.CancelDocumentAsync(await http.SessionAsync(), id, r.Reason, rowVersion); return Results.NoContent(); });
        api.MapGet("/reports/uninvoiced", async (HttpContext http, InvoiceKind kind, DateOnly asOf, InvoiceService s) =>
            Results.Ok(await s.UninvoicedAsync(await http.SessionAsync(), kind, asOf)));
    }
}
