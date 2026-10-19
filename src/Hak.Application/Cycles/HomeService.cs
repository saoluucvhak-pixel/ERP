using Hak.Application.Assets;
using Hak.Application.Loans;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Assets;
using Hak.Domain.Banking;
using Hak.Domain.Common;
using Hak.Domain.Cycles;
using Hak.Domain.Inventory;
using Hak.Domain.Production;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

/// <summary>Số việc đang chờ ở một phân hệ (màn hình chính dạng ô ứng dụng). Hint nói rõ việc gì để người dùng không phải đoán con số.</summary>
public sealed record WorkItem(string Screen, int Count, string Hint);

/// <summary>
/// Việc đang chờ xử lý theo phân hệ: mỗi chu trình tự nhận việc từ khâu trước (ví dụ hóa đơn mua chờ chấp nhận thanh toán, lệnh chi chờ duyệt).
/// Tính khi xem từ chính chứng từ nguồn, không lưu. Phân hệ nào người dùng không có quyền xem thì bỏ qua, không báo lỗi.
/// </summary>
public sealed class HomeService(HakDbContext db, PaymentAcceptanceService acceptances, SalesReportService sales, PayrollPaymentService payroll, ReorderService reorder, LoanScheduleService loans, IClock clock)
{
    public async Task<IReadOnlyList<WorkItem>> WorkQueueAsync(SessionContext ctx)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var cid = ctx.CompanyId;
        var result = new List<WorkItem>();

        async Task Add(string screen, Func<Task<(int Count, string Hint)>> compute)
        {
            try { var (n, hint) = await compute(); if (n > 0) result.Add(new WorkItem(screen, n, hint)); }
            catch (DomainException e) when (e.Code == ErrorCodes.Forbidden) { /* không có quyền xem phân hệ này */ }
        }
        static string Join(params (int n, string text)[] parts) => string.Join("; ", parts.Where(p => p.n > 0).Select(p => $"{p.n} {p.text}"));

        await Add("orders", async () => { var n = await db.TradeOrders.CountAsync(o => o.CompanyId == cid && o.Status == OrderStatus.Draft); return (n, $"{n} đơn hàng chờ xác nhận"); });
        await Add("revenue", async () =>
        {
            var drafts = await db.SalesDispatchOrders.CountAsync(o => o.CompanyId == cid && o.Status == DispatchOrderStatus.Draft);
            var overdue = (await sales.ReceivableQueueAsync(ctx, today)).Count(r => r.DaysOverdue > 0);
            return (drafts + overdue, Join((drafts, "lệnh bán hàng đang lập"), (overdue, "hóa đơn bán quá hạn thu")));
        });
        await Add("procurement", async () =>
        {
            var queue = await acceptances.PayableQueueAsync(ctx, today);
            var notAccepted = queue.Count(r => r.Stage == "NOT_ACCEPTED");
            var awaiting = (await acceptances.InboxAsync(ctx)).Count(r => r.Stage == "AWAITING_ORDER");
            var low = (await reorder.ListAsync(ctx)).Count;
            return (notAccepted + awaiting + low, Join((notAccepted, "hóa đơn mua chờ chấp nhận thanh toán"), (awaiting, "bàn giao chờ tạo lệnh chi"), (low, "vật tư dưới tồn tối thiểu")));
        });
        await Add("trade", async () => { var n = await db.CommercialInvoices.CountAsync(i => i.CompanyId == cid && i.Status == InvoiceStatus.Draft); return (n, $"{n} hóa đơn nháp chờ ghi sổ"); });
        await Add("stock", async () => { var n = await db.StockDocuments.CountAsync(d => d.CompanyId == cid && d.Status == StockDocumentStatus.Draft); return (n, $"{n} phiếu kho nháp chờ ghi sổ"); });
        await Add("payroll", async () => { var n = (await payroll.QueueAsync(ctx, today)).Count(r => r.PaymentOrderNumber == null); return (n, $"{n} khoản lương chưa có lệnh thanh toán"); });
        await Add("cash", async () =>
        {
            var ruleTypes = VoucherTypes.RuleDriven.ToArray();
            var n = await db.Vouchers.CountAsync(v => v.CompanyId == cid && ruleTypes.Contains(v.VoucherType) && (v.Status == VoucherStatus.Draft || v.Status == VoucherStatus.Submitted));
            return (n, $"{n} phiếu thu / chi chưa ghi sổ");
        });
        await Add("bank", async () =>
        {
            var toApprove = await db.PaymentOrders.CountAsync(p => p.CompanyId == cid && p.Status == PaymentOrderStatus.Created);
            var toPost = await db.PaymentOrders.CountAsync(p => p.CompanyId == cid && p.Status == PaymentOrderStatus.ExecutedByBank);
            return (toApprove + toPost, Join((toApprove, "lệnh thanh toán chờ duyệt"), (toPost, "lệnh ngân hàng đã chi chờ ghi sổ")));
        });
        await Add("assets", async () =>
        {
            var requests = await db.AssetRequests.CountAsync(r => r.CompanyId == cid && r.Status == AssetRequestStatus.Pending);
            var cards = await db.LongTermAssets.CountAsync(a => a.CompanyId == cid && a.Status == AssetStatus.Draft);
            var counts = await db.AssetCounts.CountAsync(c => c.CompanyId == cid && c.Status == AssetCountStatus.Draft);
            return (requests + cards + counts, Join((requests, "đề nghị mua sắm chờ duyệt"), (cards, "thẻ tài sản chờ ghi tăng"), (counts, "phiếu kiểm kê chưa chốt")));
        });
        await Add("production", async () =>
        {
            var toRelease = await db.ProductionOrders.CountAsync(o => o.CompanyId == cid && o.Status == ProductionOrderStatus.Draft);
            var running = await db.ProductionOrders.CountAsync(o => o.CompanyId == cid && (o.Status == ProductionOrderStatus.Released || o.Status == ProductionOrderStatus.InProgress));
            var toClose = await db.ProductionOrders.CountAsync(o => o.CompanyId == cid && o.Status == ProductionOrderStatus.Completed);
            return (toRelease + running + toClose, Join((toRelease, "lệnh sản xuất chờ ban hành"), (running, "lệnh đang sản xuất"), (toClose, "lệnh chờ ghi sổ nhập thành phẩm / đóng")));
        });
        await Add("loans", async () =>
        {
            var overdue = (await loans.UpcomingAsync(ctx, today)).Count(r => r.Status == ScheduleStatus.Overdue);
            return (overdue, $"{overdue} kỳ trả nợ vay quá hạn");
        });
        await Add("vouchers", async () =>
        {
            var submitted = await db.Vouchers.CountAsync(v => v.CompanyId == cid && v.Status == VoucherStatus.Submitted);
            var draft = await db.Vouchers.CountAsync(v => v.CompanyId == cid && v.Status == VoucherStatus.Draft);
            return (submitted + draft, Join((submitted, "chứng từ chờ duyệt"), (draft, "chứng từ nháp")));
        });
        return result;
    }
}
