using Hak.Application.Trade;
using Hak.Domain.Trade;

namespace Hak.Api;

public sealed record ContractDecisionRequest(bool Approve, string? Note);
public sealed record ContractCompleteRequest(string? Note);
public sealed record ContractAmendRequest(DateOnly Date, string Summary, decimal ValueChange, DateOnly? NewExpirationDate, IReadOnlyList<ContractLineChange>? LineChanges);
public sealed record ContractPaymentRequest(decimal Amount, DateOnly Date);
public sealed record ContractDocumentRequest(string FileName, string FilePath, string? FileType);

public static class ContractEndpoints
{
    public static void MapContractEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/contracts");
        api.MapGet("", async (HttpContext http, InvoiceKind? kind, ContractService s) => Results.Ok(await s.ListAsync(await http.SessionAsync(), kind)));
        api.MapPost("", async (HttpContext http, ContractInput i, ContractService s) => Results.Ok(new { (await s.CreateAsync(await http.SessionAsync(), i)).Id }));
        api.MapGet("/{id:guid}", async (HttpContext http, Guid id, ContractService s) => Results.Ok(await s.GetAsync(await http.SessionAsync(), id)));
        api.MapPut("/{id:guid}", async (HttpContext http, Guid id, ContractInput i, ContractService s) => Results.Ok(new { (await s.UpdateAsync(await http.SessionAsync(), id, i)).Id }));
        api.MapPost("/{id:guid}/submit", async (HttpContext http, Guid id, int version, ContractService s) => Results.Ok(new { (await s.SubmitAsync(await http.SessionAsync(), id, version)).Id }));   // version = phiên bản nội dung đã xem, bắt buộc
        api.MapPost("/{id:guid}/decide", async (HttpContext http, Guid id, int version, ContractDecisionRequest r, ContractService s) => Results.Ok(new { (await s.DecideAsync(await http.SessionAsync(), id, r.Approve, r.Note, version)).Id }));
        api.MapPost("/{id:guid}/complete", async (HttpContext http, Guid id, ContractCompleteRequest r, ContractService s) => Results.Ok(new { (await s.CompleteAsync(await http.SessionAsync(), id, r.Note)).Id }));
        api.MapPost("/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, ContractService s) => Results.Ok(new { (await s.CancelAsync(await http.SessionAsync(), id, r.Reason)).Id }));
        api.MapPost("/{id:guid}/amend", async (HttpContext http, Guid id, ContractAmendRequest r, ContractService s) =>
            Results.Ok(new { (await s.AmendAsync(await http.SessionAsync(), id, r.Date, r.Summary, r.ValueChange, r.NewExpirationDate, r.LineChanges)).Code }));
        api.MapPost("/{id:guid}/milestones/{milestoneId:guid}/pay", async (HttpContext http, Guid id, Guid milestoneId, ContractPaymentRequest r, ContractService s) =>
            Results.Ok(new { (await s.RecordPaymentAsync(await http.SessionAsync(), id, milestoneId, r.Amount, r.Date)).Id }));
        api.MapPost("/{id:guid}/documents", async (HttpContext http, Guid id, ContractDocumentRequest r, ContractService s) =>
            Results.Ok(new { (await s.AddDocumentAsync(await http.SessionAsync(), id, r.FileName, r.FilePath, r.FileType)).Id }));
        api.MapGet("/reports/fulfilment", async (HttpContext http, InvoiceKind? kind, ContractService s) => Results.Ok(await s.FulfilmentReportAsync(await http.SessionAsync(), kind)));
        api.MapGet("/reports/milestones", async (HttpContext http, InvoiceKind? kind, ContractService s) => Results.Ok(await s.MilestoneScheduleAsync(await http.SessionAsync(), kind)));
        api.MapGet("/reports/price-variance", async (HttpContext http, Guid? contractId, ContractService s) => Results.Ok(await s.PriceVarianceAsync(await http.SessionAsync(), contractId)));
        api.MapGet("/alerts", async (HttpContext http, ContractService s) => Results.Ok(await s.AlertsAsync(await http.SessionAsync())));
    }
}
