using Hak.Domain.Common;
using Hak.Domain.Catalog;
using Hak.Application.Cycles;
using Hak.Domain.Cycles;

namespace Hak.Api;

public sealed record AcceptPaymentRequest(string? Note, bool AcknowledgeMismatch = false);
public sealed record MatchPolicyRequest(Hak.Domain.Cycles.MatchPolicyScope Scope, Guid? PartnerId, decimal PricePercent, decimal PriceAbsolute, decimal QuantityPercent, DateOnly EffectiveFrom, string? Note);
public sealed record SupplierStatusRequest(SupplierStatus Status, string? Reason);
public sealed record SupplierPolicyRequest(bool Require);
public sealed record ReorderOrderRequest(Guid PartnerId, IReadOnlyList<ReorderOrderLine> Lines, DateOnly? ExpectedDate);
public sealed record PayrollOrdersRequest(Guid BankAccountId, IReadOnlyList<Guid> JournalLineIds, DateOnly? AsOf);
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
        // Đợt 4: hồ sơ chu trình của đơn (bốn trục trạng thái, chứng từ liên quan, hành động kế tiếp) và việc cần làm theo đơn.
        // Đường dẫn: /api/cycles/orders/{id}/flow và /api/cycles/order-worklist, đúng như OrderFlow.tsx gọi (trước đây lệch nên luôn 404);
        // test hợp đồng giao diện–API giữ hai bên khớp nhau.
        api.MapGet("/orders/{id:guid}/flow", async (HttpContext http, Guid id, OrderFlowService s) => Results.Ok(await s.FlowAsync(await http.SessionAsync(), id)));
        api.MapGet("/order-worklist", async (HttpContext http, Hak.Domain.Trade.InvoiceKind kind, OrderFlowService s, int? skip, int? take) => Results.Ok(await s.WorklistAsync(await http.SessionAsync(), kind, skip ?? 0, take ?? 60)));
        // Đợt 3: duyệt sai lệch đối chiếu (quyền riêng), chính sách dung sai có phiên bản, theo dõi nhận hàng của đơn mua.
        api.MapPost("/procurement/invoices/{id:guid}/approve-variance", async (HttpContext http, Guid id, ReasonRequest r, PurchaseVarianceService s) => Results.Ok(await s.ApproveAsync(await http.SessionAsync(), id, r.Reason)));
        api.MapGet("/procurement/invoices/{id:guid}/variance", async (HttpContext http, Guid id, PurchaseVarianceService s) => Results.Ok(await s.CurrentAsync(await http.SessionAsync(), id)));
        api.MapPost("/procurement/variance/{id:guid}/revoke", async (HttpContext http, Guid id, ReasonRequest r, PurchaseVarianceService s) => { await s.RevokeAsync(await http.SessionAsync(), id, r.Reason); return Results.NoContent(); });
        api.MapGet("/procurement/match-policies", async (HttpContext http, PurchaseVarianceService s) => Results.Ok(await s.ListPoliciesAsync(await http.SessionAsync())));
        api.MapPost("/procurement/match-policies", async (HttpContext http, MatchPolicyRequest r, PurchaseVarianceService s) =>
            Results.Ok(await s.SetPolicyAsync(await http.SessionAsync(), new MatchPolicyInput(r.Scope, r.PartnerId, r.PricePercent, r.PriceAbsolute, r.QuantityPercent, r.EffectiveFrom, r.Note))));
        api.MapGet("/procurement/orders/{id:guid}/receiving", async (HttpContext http, Guid id, PurchaseReceivingService s) => Results.Ok(await s.ForOrderAsync(await http.SessionAsync(), id)));
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

        // Màn hình chính: việc đang chờ theo phân hệ.
        api.MapGet("/home/tasks", async (HttpContext http, int? take, HomeService s) => Results.Ok(await s.TasksAsync(await http.SessionAsync(), take ?? 100)));
        api.MapGet("/home/work-queue", async (HttpContext http, HomeService s) => Results.Ok(await s.WorkQueueAsync(await http.SessionAsync())));

        // Cung ứng: duyệt và xếp hạng nhà cung cấp; tạo đơn mua nháp từ danh mục cần đặt.
        api.MapGet("/procurement/suppliers/rating", async (HttpContext http, DateOnly fromDate, DateOnly toDate, SupplierService s) =>
            Results.Ok(await s.RatingAsync(await http.SessionAsync(), fromDate, toDate)));
        api.MapPut("/procurement/suppliers/{id:guid}/status", async (HttpContext http, Guid id, SupplierStatusRequest r, SupplierService s) =>
        { await s.SetStatusAsync(await http.SessionAsync(), id, r.Status, r.Reason); return Results.NoContent(); });
        api.MapGet("/procurement/suppliers/policy", async (HttpContext http, SupplierService s) => Results.Ok(new { require = await s.ApprovalPolicyAsync(await http.SessionAsync()) }));
        api.MapPut("/procurement/suppliers/policy", async (HttpContext http, SupplierPolicyRequest r, SupplierService s) =>
        { await s.SetApprovalPolicyAsync(await http.SessionAsync(), r.Require); return Results.NoContent(); });
        api.MapPost("/procurement/reorder-list/create-order", async (HttpContext http, ReorderOrderRequest r, SupplierService s) =>
        {
            var o = await s.CreateOrderFromReorderAsync(await http.SessionAsync(), r.PartnerId, r.Lines, r.ExpectedDate);
            return Results.Ok(new { o.Id, o.Number });
        });

        // Nhân sự – lương → thanh toán: hàng chờ trả lương và lệnh thanh toán lương Nháp.
        api.MapGet("/finance/payroll-payable", async (HttpContext http, DateOnly asOf, PayrollPaymentService s) => Results.Ok(await s.QueueAsync(await http.SessionAsync(), asOf)));
        api.MapPost("/finance/payroll-payable/orders", async (HttpContext http, PayrollOrdersRequest r, PayrollPaymentService s, IClock clock) =>
        {
            var created = await s.CreateOrdersAsync(await http.SessionAsync(), r.BankAccountId, r.JournalLineIds, r.AsOf ?? DateOnly.FromDateTime(clock.UtcNow));
            return Results.Ok(created.Select(o => new { o.Id, o.Number, o.Amount }));
        });

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
        api.MapGet("/revenue/overdue", async (HttpContext http, DateOnly asOf, SalesReportService s) => Results.Ok(await s.OverdueNoticesAsync(await http.SessionAsync(), asOf)));
        api.MapGet("/finance/cash-plan", async (HttpContext http, DateOnly asOf, int? weeks, CashPlanService s) => Results.Ok(await s.PlanAsync(await http.SessionAsync(), asOf, weeks ?? 8)));
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
