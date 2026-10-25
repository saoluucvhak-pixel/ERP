using Hak.Application.Trade;

namespace Hak.Api;

public sealed record PolicyRequest(bool Require);
public sealed record WriteOffVoucherRequest(DateOnly Date);

public static class SalesApprovalEndpoints
{
    public static void MapSalesApprovalEndpoints(this WebApplication app)
    {
        var a = app.MapGroup("/api/sales-approvals");
        a.MapGet("/policy", async (HttpContext http, SalesApprovalService s) => Results.Ok(new { require = await s.PolicyAsync(await http.SessionAsync()) }));
        a.MapPost("/policy", async (HttpContext http, PolicyRequest r, SalesApprovalService s) => { await s.SetPolicyAsync(await http.SessionAsync(), r.Require); return Results.NoContent(); });
        a.MapGet("/adjustments", async (HttpContext http, Guid? invoiceId, SalesApprovalService s) => Results.Ok(await s.RequestsAsync(await http.SessionAsync(), invoiceId)));
        a.MapPost("/adjustments", async (HttpContext http, AdjustmentRequestInput i, SalesApprovalService s) => Results.Ok(new { (await s.RequestAsync(await http.SessionAsync(), i)).Id }));
        a.MapPost("/adjustments/{id:guid}/decide", async (HttpContext http, Guid id, ContractDecisionRequest r, SalesApprovalService s) => Results.Ok(new { (await s.DecideAsync(await http.SessionAsync(), id, r.Approve, r.Note)).Id }));
        a.MapPost("/adjustments/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, SalesApprovalService s) => { await s.CancelAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        a.MapGet("/write-offs/candidates", async (HttpContext http, Guid partnerId, DateOnly asOf, SalesApprovalService s) => Results.Ok(await s.CandidatesAsync(await http.SessionAsync(), partnerId, asOf)));
        a.MapGet("/write-offs", async (HttpContext http, SalesApprovalService s) => Results.Ok(await s.WriteOffsAsync(await http.SessionAsync())));
        a.MapPost("/write-offs", async (HttpContext http, WriteOffInput i, SalesApprovalService s) => Results.Ok(new { (await s.RequestWriteOffAsync(await http.SessionAsync(), i)).Id }));
        a.MapPost("/write-offs/{id:guid}/decide", async (HttpContext http, Guid id, ContractDecisionRequest r, SalesApprovalService s) => Results.Ok(new { (await s.DecideWriteOffAsync(await http.SessionAsync(), id, r.Approve, r.Note)).Id }));
        a.MapPost("/write-offs/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, SalesApprovalService s) => { await s.CancelWriteOffAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        a.MapPost("/write-offs/{id:guid}/voucher", async (HttpContext http, Guid id, WriteOffVoucherRequest r, SalesApprovalService s) => { var v = await s.CreateWriteOffVoucherAsync(await http.SessionAsync(), id, r.Date); return Results.Ok(new { v.Id, v.Number }); });
    }
}

public sealed record PaymentDiscountVoucherRequest(Guid CashAccountId, DateOnly Date);

public static class PaymentDiscountEndpoints
{
    public static void MapPaymentDiscountEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/payment-discounts");
        api.MapGet("", async (HttpContext http, Hak.Domain.Trade.InvoiceKind kind, DateOnly asOf, PaymentDiscountService s) => Results.Ok(await s.CandidatesAsync(await http.SessionAsync(), kind, asOf)));
        api.MapPost("/{invoiceId:guid}/voucher", async (HttpContext http, Guid invoiceId, PaymentDiscountVoucherRequest r, PaymentDiscountService s) =>
        { var v = await s.CreateVoucherAsync(await http.SessionAsync(), invoiceId, r.CashAccountId, r.Date); return Results.Ok(new { v.Id, v.Number }); });
    }
}
