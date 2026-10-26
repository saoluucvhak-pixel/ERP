using Hak.Application.Trade;
using Hak.Domain.Trade;

namespace Hak.Api;

public sealed record AgreementRequest(InvoiceKind Kind, Guid PartnerId, string Code, string Name, DateOnly FromDate, DateOnly ToDate, IReadOnlyList<AgreementTierInput> Tiers, bool Progressive = false);
public sealed record AgreementUpdateRequest(long RowVersion, InvoiceKind Kind, Guid PartnerId, string Code, string Name, DateOnly FromDate, DateOnly ToDate, IReadOnlyList<AgreementTierInput> Tiers, bool Progressive = false);
public sealed record AgreementAccrueRequest(long RowVersion, DateOnly AsOf);
public sealed record AgreementActiveRequest(long RowVersion, bool Active);
public sealed record AgreementSettleRequest(long RowVersion, string InvoiceSeries, string FirstInvoiceNo, DateOnly InvoiceDate, DateOnly PostingDate, bool ByVoucher = false);
public sealed record DiscountLinkInvoiceRequest(long RowVersion, Guid InvoiceId, DateOnly? ReceivedDate);
public sealed record DiscountInvoiceReceivedRequest(long RowVersion, Guid InvoiceId, DateOnly ReceivedDate);
public sealed record RebateAmendRequest(long RowVersion, Guid PreviousRunId, string SourceHash, string Reason, string InvoiceSeries, string FirstInvoiceNo, DateOnly InvoiceDate, DateOnly PostingDate);

/// <summary>Chiết khấu thương mại theo doanh số (thỏa thuận, quyết toán cuối kỳ) và báo cáo chiết khấu.</summary>
public static class DiscountEndpoints
{
    public static void MapDiscountEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/discounts");
        api.MapGet("/agreements", async (HttpContext http, InvoiceKind kind, DiscountAgreementService s) => Results.Ok(await s.ListAsync(await http.SessionAsync(), kind)));
        api.MapPost("/agreements", async (HttpContext http, AgreementRequest r, DiscountAgreementService s) =>
            Results.Ok(new { (await s.CreateAsync(await http.SessionAsync(), new AgreementInput(r.Kind, r.PartnerId, r.Code, r.Name, r.FromDate, r.ToDate, r.Tiers, r.Progressive))).Id }));
        api.MapPut("/agreements/{id:guid}", async (HttpContext http, Guid id, AgreementUpdateRequest r, DiscountAgreementService s) =>
            Results.Ok(new { (await s.UpdateAsync(await http.SessionAsync(), id, r.RowVersion, new AgreementInput(r.Kind, r.PartnerId, r.Code, r.Name, r.FromDate, r.ToDate, r.Tiers, r.Progressive))).RowVersion }));
        api.MapPost("/agreements/{id:guid}/active", async (HttpContext http, Guid id, AgreementActiveRequest r, DiscountAgreementService s) =>
            Results.Ok(new { (await s.SetActiveAsync(await http.SessionAsync(), id, r.RowVersion, r.Active)).RowVersion }));
        api.MapGet("/agreements/{id:guid}/preview", async (HttpContext http, Guid id, DiscountAgreementService s) => Results.Ok(await s.PreviewAsync(await http.SessionAsync(), id)));
        api.MapPost("/agreements/{id:guid}/settle", async (HttpContext http, Guid id, AgreementSettleRequest r, DiscountAgreementService s) =>
        {
            var run = r.ByVoucher ? await s.SettleByVoucherAsync(await http.SessionAsync(), id, r.RowVersion, r.PostingDate)
                                  : await s.SettleAsync(await http.SessionAsync(), id, r.RowVersion, r.InvoiceSeries, r.FirstInvoiceNo, r.InvoiceDate, r.PostingDate);
            return Results.Ok(new { run.Id, run.Amount, run.Percent, run.EligibleSales });
        });
        api.MapGet("/agreements/{id:guid}/history", async (HttpContext http, Guid id, DiscountAgreementService s) => Results.Ok(await s.HistoryAsync(await http.SessionAsync(), id)));
        api.MapGet("/agreements/{id:guid}/amendment-preview", async (HttpContext http, Guid id, DiscountAgreementService s) => Results.Ok(await s.AmendmentPreviewAsync(await http.SessionAsync(), id)));
        api.MapPost("/agreements/{id:guid}/amend", async (HttpContext http, Guid id, RebateAmendRequest r, DiscountAgreementService s) => {
            var result = await s.AmendAsync(await http.SessionAsync(), id, r.RowVersion, r.PreviousRunId, r.SourceHash, r.Reason, r.InvoiceSeries, r.FirstInvoiceNo, r.InvoiceDate, r.PostingDate);
            return Results.Ok(new { result.Id, result.Amount, result.Sequence });
        });
        api.MapPost("/agreements/{id:guid}/runs/{runId:guid}/void", async (HttpContext http, Guid id, Guid runId, ReasonRequest r, DiscountAgreementService s) => { await s.VoidRunAsync(await http.SessionAsync(), id, r.Reason, runId); return Results.NoContent(); });
        api.MapGet("/agreements/{id:guid}/statement", async (HttpContext http, Guid id, Guid? runId, DiscountAgreementService s) => Results.Ok(await s.StatementAsync(await http.SessionAsync(), id, runId)));
        api.MapGet("/agreements/{id:guid}/lifecycle", async (HttpContext http, Guid id, Guid? runId, DiscountAgreementService s) => Results.Ok(await s.LifecycleAsync(await http.SessionAsync(), id, runId)));
        api.MapPost("/agreements/{id:guid}/link-invoice", async (HttpContext http, Guid id, DiscountLinkInvoiceRequest r, DiscountAgreementService s) =>
            Results.Ok(new { (await s.LinkInvoiceAsync(await http.SessionAsync(), id, r.RowVersion, r.InvoiceId, r.ReceivedDate)).Id }));
        api.MapPost("/agreements/{id:guid}/invoice-received", async (HttpContext http, Guid id, DiscountInvoiceReceivedRequest r, DiscountAgreementService s) =>
        { await s.RecordReceiptAsync(await http.SessionAsync(), id, r.RowVersion, r.InvoiceId, r.ReceivedDate); return Results.NoContent(); });
        api.MapPost("/agreements/{id:guid}/accrue", async (HttpContext http, Guid id, AgreementAccrueRequest r, DiscountAgreementService s) =>
        { var x = await s.AccrueAsync(await http.SessionAsync(), id, r.RowVersion, r.AsOf); return Results.Ok(new { x.Id, x.Estimated, x.Adjustment, x.VoucherId }); });
        api.MapPost("/agreements/{id:guid}/void-run", async (HttpContext http, Guid id, ReasonRequest r, DiscountAgreementService s) => { await s.VoidRunAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        api.MapGet("/report", async (HttpContext http, InvoiceKind kind, DateOnly from, DateOnly to, DiscountReportService s) => Results.Ok(await s.ReportAsync(await http.SessionAsync(), kind, from, to)));
    }
}
