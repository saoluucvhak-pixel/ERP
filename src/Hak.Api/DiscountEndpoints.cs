using Hak.Application.Trade;
using Hak.Domain.Trade;

namespace Hak.Api;

public sealed record AgreementRequest(InvoiceKind Kind, Guid PartnerId, string Code, string Name, DateOnly FromDate, DateOnly ToDate, IReadOnlyList<AgreementTierInput> Tiers);
public sealed record AgreementUpdateRequest(long RowVersion, InvoiceKind Kind, Guid PartnerId, string Code, string Name, DateOnly FromDate, DateOnly ToDate, IReadOnlyList<AgreementTierInput> Tiers);
public sealed record AgreementActiveRequest(long RowVersion, bool Active);
public sealed record AgreementSettleRequest(long RowVersion, string InvoiceSeries, string FirstInvoiceNo, DateOnly InvoiceDate, DateOnly PostingDate);

/// <summary>Chiết khấu thương mại theo doanh số (thỏa thuận, quyết toán cuối kỳ) và báo cáo chiết khấu.</summary>
public static class DiscountEndpoints
{
    public static void MapDiscountEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/discounts");
        api.MapGet("/agreements", async (HttpContext http, InvoiceKind kind, DiscountAgreementService s) => Results.Ok(await s.ListAsync(await http.SessionAsync(), kind)));
        api.MapPost("/agreements", async (HttpContext http, AgreementRequest r, DiscountAgreementService s) =>
            Results.Ok(new { (await s.CreateAsync(await http.SessionAsync(), new AgreementInput(r.Kind, r.PartnerId, r.Code, r.Name, r.FromDate, r.ToDate, r.Tiers))).Id }));
        api.MapPut("/agreements/{id:guid}", async (HttpContext http, Guid id, AgreementUpdateRequest r, DiscountAgreementService s) =>
            Results.Ok(new { (await s.UpdateAsync(await http.SessionAsync(), id, r.RowVersion, new AgreementInput(r.Kind, r.PartnerId, r.Code, r.Name, r.FromDate, r.ToDate, r.Tiers))).RowVersion }));
        api.MapPost("/agreements/{id:guid}/active", async (HttpContext http, Guid id, AgreementActiveRequest r, DiscountAgreementService s) =>
            Results.Ok(new { (await s.SetActiveAsync(await http.SessionAsync(), id, r.RowVersion, r.Active)).RowVersion }));
        api.MapGet("/agreements/{id:guid}/preview", async (HttpContext http, Guid id, DiscountAgreementService s) => Results.Ok(await s.PreviewAsync(await http.SessionAsync(), id)));
        api.MapPost("/agreements/{id:guid}/settle", async (HttpContext http, Guid id, AgreementSettleRequest r, DiscountAgreementService s) =>
        {
            var run = await s.SettleAsync(await http.SessionAsync(), id, r.RowVersion, r.InvoiceSeries, r.FirstInvoiceNo, r.InvoiceDate, r.PostingDate);
            return Results.Ok(new { run.Id, run.Amount, run.Percent, run.EligibleSales });
        });
        api.MapPost("/agreements/{id:guid}/void-run", async (HttpContext http, Guid id, ReasonRequest r, DiscountAgreementService s) => { await s.VoidRunAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        api.MapGet("/report", async (HttpContext http, InvoiceKind kind, DateOnly from, DateOnly to, DiscountReportService s) => Results.Ok(await s.ReportAsync(await http.SessionAsync(), kind, from, to)));
    }
}
