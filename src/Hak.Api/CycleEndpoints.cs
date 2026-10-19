using Hak.Application.Cycles;
using Hak.Domain.Cycles;

namespace Hak.Api;

public sealed record AcceptPaymentRequest(string? Note, bool AcknowledgeMismatch = false);
public sealed record CreatePaymentOrderRequest(Guid BankAccountId);
public sealed record DeliveryInvoiceRequest(string InvoiceSeries, string InvoiceNo, DateOnly InvoiceDate);

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
            Results.Ok(Dto(await s.AcceptAsync(await http.SessionAsync(), id, r.Note, r.AcknowledgeMismatch))));
        api.MapPost("/procurement/acceptances/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, PaymentAcceptanceService s) =>
            Results.Ok(Dto(await s.CancelAsync(await http.SessionAsync(), id, r.Reason))));

        // Cung ứng: biên bản kiểm nghiệm và đối chiếu nhiều bên của hóa đơn mua.
        api.MapGet("/procurement/receipts", async (HttpContext http, bool? onlyPending, GoodsInspectionService s) => Results.Ok(await s.ReceiptsAsync(await http.SessionAsync(), onlyPending ?? false)));
        api.MapPost("/procurement/inspections", async (HttpContext http, InspectionInput r, GoodsInspectionService s) =>
        {
            var b = await s.CreateAsync(await http.SessionAsync(), r);
            return Results.Ok(new { b.Id, b.Number });
        });
        api.MapPost("/procurement/inspections/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, GoodsInspectionService s) =>
        {
            var b = await s.CancelAsync(await http.SessionAsync(), id, r.Reason);
            return Results.Ok(new { b.Id, b.Number });
        });
        api.MapGet("/procurement/invoices/{id:guid}/match", async (HttpContext http, Guid id, PurchaseMatchService s) => Results.Ok(await s.MatchAsync(await http.SessionAsync(), id)));

        // Doanh thu: kiểm tra tín dụng và tồn kho khi nhận đơn bán. Cung ứng: danh mục cần đặt hàng từ tồn tối thiểu.
        api.MapGet("/revenue/orders/{id:guid}/check", async (HttpContext http, Guid id, SalesOrderCheckService s) => Results.Ok(await s.CheckAsync(await http.SessionAsync(), id)));
        api.MapGet("/procurement/reorder-list", async (HttpContext http, ReorderService s) => Results.Ok(await s.ListAsync(await http.SessionAsync())));

        // Doanh thu (tệp phân tích ERP, mục V.1): lệnh bán hàng → phiếu giao hàng → hóa đơn bán → thu tiền.
        api.MapGet("/revenue/dispatch-orders", async (HttpContext http, Guid? orderId, bool? includeCancelled, SalesDispatchService s) =>
            Results.Ok(await s.ListAsync(await http.SessionAsync(), orderId, includeCancelled ?? false)));
        api.MapPost("/revenue/dispatch-orders", async (HttpContext http, DispatchInput r, SalesDispatchService s) =>
        {
            var d = await s.CreateDispatchAsync(await http.SessionAsync(), r);
            return Results.Ok(new { d.Id, d.Number, status = d.Status.ToString() });
        });
        api.MapPost("/revenue/dispatch-orders/{id:guid}/issue", async (HttpContext http, Guid id, SalesDispatchService s) =>
        {
            var d = await s.IssueAsync(await http.SessionAsync(), id);
            return Results.Ok(new { d.Id, d.Number, status = d.Status.ToString() });
        });
        api.MapPost("/revenue/dispatch-orders/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, SalesDispatchService s) =>
        {
            var d = await s.CancelDispatchAsync(await http.SessionAsync(), id, r.Reason);
            return Results.Ok(new { d.Id, d.Number, status = d.Status.ToString() });
        });
        api.MapPost("/revenue/deliveries", async (HttpContext http, DeliveryInput r, SalesDispatchService s) =>
        {
            var d = await s.CreateDeliveryAsync(await http.SessionAsync(), r);
            return Results.Ok(new { d.Id, d.Number, d.StockDocumentId });
        });
        api.MapPost("/revenue/deliveries/{id:guid}/cancel", async (HttpContext http, Guid id, ReasonRequest r, SalesDispatchService s) =>
        {
            var d = await s.CancelDeliveryAsync(await http.SessionAsync(), id, r.Reason);
            return Results.Ok(new { d.Id, d.Number });
        });
        api.MapPost("/revenue/deliveries/{id:guid}/invoice", async (HttpContext http, Guid id, DeliveryInvoiceRequest r, SalesDispatchService s) =>
        {
            var inv = await s.CreateInvoiceAsync(await http.SessionAsync(), id, r.InvoiceSeries, r.InvoiceNo, r.InvoiceDate);
            return Results.Ok(new { inv.Id, inv.Number, status = inv.Status.ToString(), inv.DueDate });
        });
        api.MapGet("/revenue/receivable-queue", async (HttpContext http, DateOnly asOf, SalesReportService s) => Results.Ok(await s.ReceivableQueueAsync(await http.SessionAsync(), asOf)));
        api.MapGet("/revenue/sales-analysis", async (HttpContext http, DateOnly from, DateOnly to, SalesReportService s) => Results.Ok(await s.AnalyseAsync(await http.SessionAsync(), from, to)));
        api.MapGet("/revenue/collections", async (HttpContext http, DateOnly from, DateOnly to, SalesReportService s) => Results.Ok(await s.CollectionsAsync(await http.SessionAsync(), from, to)));

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
