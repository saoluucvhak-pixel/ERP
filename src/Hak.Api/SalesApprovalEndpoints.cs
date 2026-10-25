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

public sealed record StandalonePaymentDiscountRequest(Hak.Domain.Trade.InvoiceKind Kind, Guid PartnerId, Guid SettleJournalLineId, Guid CashAccountId, DateOnly Date, decimal Discount);

public sealed record MultiPaymentDiscountRequest(Hak.Domain.Trade.InvoiceKind Kind, Guid PartnerId, List<Guid> InvoiceIds, Guid CashAccountId, DateOnly Date);

public sealed record PaymentDiscountVoucherRequest(Guid CashAccountId, DateOnly Date);

public static class PaymentDiscountEndpoints
{
    public static void MapPaymentDiscountEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/payment-discounts");
        api.MapGet("", async (HttpContext http, Hak.Domain.Trade.InvoiceKind kind, DateOnly asOf, PaymentDiscountService s) => Results.Ok(await s.CandidatesAsync(await http.SessionAsync(), kind, asOf)));
        // Chiết khấu thanh toán độc lập: không gắn hóa đơn.
        api.MapGet("/open-items", async (HttpContext http, Hak.Domain.Trade.InvoiceKind kind, Guid partnerId, DateOnly asOf, PaymentDiscountService s) => Results.Ok(await s.OpenItemsAsync(await http.SessionAsync(), kind, partnerId, asOf)));
        // Báo cáo chiết khấu thanh toán: đã mất (quá hạn còn nợ), khoản dư nghi khấu trừ sai, DSO/DPO theo tháng, hiệu quả dòng tiền.
        api.MapGet("/reports/missed", async (HttpContext http, Hak.Domain.Trade.InvoiceKind kind, DateOnly asOf, PaymentDiscountReportService s) => Results.Ok(await s.MissedAsync(await http.SessionAsync(), kind, asOf)));
        api.MapGet("/reports/suspect", async (HttpContext http, Hak.Domain.Trade.InvoiceKind kind, DateOnly asOf, decimal? maxPercent, PaymentDiscountReportService s) => Results.Ok(await s.SuspectDeductionsAsync(await http.SessionAsync(), kind, asOf, maxPercent ?? 5m)));
        api.MapGet("/reports/days", async (HttpContext http, Hak.Domain.Trade.InvoiceKind kind, DateOnly from, DateOnly to, PaymentDiscountReportService s) => Results.Ok(await s.DaysAsync(await http.SessionAsync(), kind, from, to)));
        api.MapGet("/reports/effect", async (HttpContext http, Hak.Domain.Trade.InvoiceKind kind, DateOnly from, DateOnly to, PaymentDiscountReportService s) => Results.Ok(await s.EffectAsync(await http.SessionAsync(), kind, from, to)));
        api.MapPost("/multi", async (HttpContext http, MultiPaymentDiscountRequest r, PaymentDiscountService s) =>
        { var v = await s.CreateMultiVoucherAsync(await http.SessionAsync(), r.Kind, r.PartnerId, r.InvoiceIds, r.CashAccountId, r.Date); return Results.Ok(new { v.Id, v.Number }); });
        api.MapPost("/standalone", async (HttpContext http, StandalonePaymentDiscountRequest r, PaymentDiscountService s) =>
        { var v = await s.CreateStandaloneVoucherAsync(await http.SessionAsync(), r.Kind, r.PartnerId, r.SettleJournalLineId, r.CashAccountId, r.Date, r.Discount); return Results.Ok(new { v.Id, v.Number }); });
        api.MapPost("/{invoiceId:guid}/voucher", async (HttpContext http, Guid invoiceId, PaymentDiscountVoucherRequest r, PaymentDiscountService s) =>
        { var v = await s.CreateVoucherAsync(await http.SessionAsync(), invoiceId, r.CashAccountId, r.Date); return Results.Ok(new { v.Id, v.Number }); });
    }
}
