using Hak.Application.Cycles;
using Hak.Domain.Cycles;

namespace Hak.Api;

public sealed record AcceptPaymentRequest(string? Note);
public sealed record CreatePaymentOrderRequest(Guid BankAccountId);

/// <summary>Bàn giao giữa các chu trình (docs/10). Hiện có: Cung ứng → Thanh toán.</summary>
public static class CycleEndpoints
{
    private static object Dto(PaymentAcceptance a) => new
    {
        a.Id, a.InvoiceId, a.PartnerId, a.Amount, a.DueDate, status = a.Status.ToString(), a.Note, a.PaymentOrderId, a.AcceptedAt, a.ClosedAt, a.CloseReason, a.RowVersion,
    };

    public static void MapCycleEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/cycles");

        // Cung ứng: hàng chờ thanh toán theo ngày đáo hạn và bước "chấp nhận thanh toán".
        api.MapGet("/procurement/payable-queue", async (HttpContext http, DateOnly asOf, PaymentAcceptanceService s) =>
            Results.Ok(await s.PayableQueueAsync(await http.SessionAsync(), asOf)));
        api.MapPost("/procurement/invoices/{id:guid}/accept-payment", async (HttpContext http, Guid id, AcceptPaymentRequest r, PaymentAcceptanceService s) =>
            Results.Ok(Dto(await s.AcceptAsync(await http.SessionAsync(), id, r.Note))));
        api.MapPost("/procurement/acceptances/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, PaymentAcceptanceService s) =>
            Results.Ok(Dto(await s.CancelAsync(await http.SessionAsync(), id, r.Reason))));

        // Thanh toán: hộp thư nhận bàn giao.
        api.MapGet("/payment/inbox", async (HttpContext http, bool? includeClosed, PaymentAcceptanceService s) =>
            Results.Ok(await s.InboxAsync(await http.SessionAsync(), includeClosed ?? false)));
        api.MapPost("/payment/acceptances/{id:guid}/create-order", async (HttpContext http, Guid id, CreatePaymentOrderRequest r, PaymentAcceptanceService s) =>
        {
            var po = await s.CreateOrderAsync(await http.SessionAsync(), id, r.BankAccountId);
            return Results.Ok(new { po.Id, po.Number, status = po.Status.ToString(), po.Amount });
        });
        api.MapPost("/payment/acceptances/{id:guid}/reject", async (HttpContext http, Guid id, ReasonRequest r, PaymentAcceptanceService s) =>
            Results.Ok(Dto(await s.RejectAsync(await http.SessionAsync(), id, r.Reason))));
    }
}
