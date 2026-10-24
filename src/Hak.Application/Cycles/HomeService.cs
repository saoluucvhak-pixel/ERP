using Hak.Application.Assets;
using Hak.Application.Loans;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Advances;
using Hak.Domain.Assets;
using Hak.Domain.Banking;
using Hak.Domain.Common;
using Hak.Domain.Cycles;
using Hak.Domain.Inventory;
using Hak.Domain.Production;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

/// <summary>Số việc đang chờ ở một phân hệ (màn hình chính dạng ô ứng dụng). Hint nói rõ việc gì để người dùng không phải đoán con số.</summary>
public sealed record WorkItem(string Screen, int Count, string Hint, string? Kind = null);   // Kind: Sales / Purchase cho màn dùng chung (đơn, hóa đơn, nghiệm thu): mỗi chu trình đếm riêng

/// <summary>
/// Một việc cụ thể trên một chứng từ cụ thể: dẫn thẳng tới đúng chứng từ (EntityType + EntityId) và nói rõ hành động kế tiếp, người lập, tuổi việc,
/// và — nếu người đang xem chưa làm được — lý do bị chặn (thiếu quyền hoặc quy tắc tách người lập / người duyệt).
/// </summary>
public sealed record WorkTask(string Screen, string? Kind, string EntityType, Guid EntityId, string Number, string Title, string Action, Guid OwnerId, string OwnerName,
    DateOnly Date, int AgeDays, string? BlockedReason, DateOnly? Due = null);

/// <summary>
/// Việc đang chờ xử lý theo phân hệ: mỗi chu trình tự nhận việc từ khâu trước (ví dụ hóa đơn mua chờ chấp nhận thanh toán, lệnh chi chờ duyệt).
/// Tính khi xem từ chính chứng từ nguồn, không lưu. Phân hệ nào người dùng không có quyền xem thì bỏ qua, không báo lỗi.
/// </summary>
public sealed class HomeService(HakDbContext db, PaymentAcceptanceService acceptances, SalesReportService sales, PayrollPaymentService payroll, ReorderService reorder, LoanScheduleService loans, IClock clock, Hak.Application.Trade.ContractServicePlanService? servicePlans = null)
{
    /// <summary>Quyền xem của từng màn hình, giống quyền mà API danh sách của màn đó yêu cầu (người không xem được danh sách thì không thấy số đếm).</summary>
    public static readonly IReadOnlyDictionary<string, string> ViewPermission = new Dictionary<string, string>
    {
        ["orders"] = Permissions.VoucherView, ["revenue"] = Permissions.VoucherView, ["procurement"] = Permissions.ReportView,
        ["services"] = Permissions.VoucherView, ["trade"] = Permissions.VoucherView, ["stock"] = Permissions.VoucherView,
        ["payroll"] = Permissions.ReportView, ["cash"] = Permissions.VoucherView, ["bank"] = Permissions.VoucherView,
        ["assets"] = Permissions.ReportView, ["production"] = Permissions.ReportView, ["loans"] = Permissions.ReportView,
        ["vouchers"] = Permissions.VoucherView,
    };

    public async Task<IReadOnlyList<WorkItem>> WorkQueueAsync(SessionContext ctx)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var cid = ctx.CompanyId;
        var result = new List<WorkItem>();

        async Task Add(string screen, Func<Task<(int Count, string Hint)>> compute, InvoiceKind? kind = null)
        {
            // Nhiều mục đếm thẳng trên bảng (không qua service có kiểm quyền), nên kiểm quyền xem của màn hình ở đây, trước khi đếm.
            if (!ctx.Permissions.Contains(ViewPermission[screen])) return;
            try { var (n, hint) = await compute(); if (n > 0) result.Add(new WorkItem(screen, n, hint, kind?.ToString())); }
            catch (DomainException e) when (e.Code == ErrorCodes.Forbidden) { /* không có quyền xem phân hệ này */ }
        }
        static string Join(params (int n, string text)[] parts) => string.Join("; ", parts.Where(p => p.n > 0).Select(p => $"{p.n} {p.text}"));

        // Đơn hàng, hóa đơn, nghiệm thu dịch vụ dùng chung màn hình cho hai chu trình: đếm RIÊNG từng chiều bán / mua để không lẫn việc của chu trình kia.
        foreach (var kind in new[] { InvoiceKind.Sales, InvoiceKind.Purchase })
        {
            var k = kind; var word = k == InvoiceKind.Sales ? "bán" : "mua";
            await Add("orders", async () => { var n = await db.TradeOrders.CountAsync(o => o.CompanyId == cid && o.Kind == k && o.Status == OrderStatus.Draft); return (n, $"{n} đơn {word} hàng chờ xác nhận"); }, k);
        }
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
        foreach (var kind in new[] { InvoiceKind.Sales, InvoiceKind.Purchase })
        {
            var k = kind; var word = k == InvoiceKind.Sales ? "bán" : "mua";
            await Add("services", async () =>
            {
                var waiting = await db.ServiceAcceptances.CountAsync(a => a.CompanyId == cid && a.Kind == k && a.Status == ApprovalStatus.Requested);
                var live = db.ServiceAcceptances.Where(a => a.CompanyId == cid && (a.Status == ApprovalStatus.Requested || a.Status == ApprovalStatus.Approved)).Select(a => a.Id);
                var lateMilestones = await db.ServiceMilestones.CountAsync(m => m.CompanyId == cid && m.DueDate < today && (m.AcceptanceId == null || !live.Contains(m.AcceptanceId.Value))
                                                                                 && db.TradeOrders.Any(o => o.Id == m.OrderId && o.Kind == k));
                var latePlans = servicePlans is null ? 0 : (await servicePlans.OverdueAsync(ctx, k, today)).Count;
                var n = waiting + lateMilestones + latePlans;
                return (n, Join((waiting, $"biên bản nghiệm thu dịch vụ {word} chờ xác nhận"), (lateMilestones, "mốc dịch vụ quá hạn"), (latePlans, "đợt nghiệm thu theo hợp đồng quá hạn")));
            }, k);
        }
        foreach (var kind in new[] { InvoiceKind.Sales, InvoiceKind.Purchase })
        {
            var k = kind; var word = k == InvoiceKind.Sales ? "bán" : "mua";
            await Add("trade", async () => { var n = await db.CommercialInvoices.CountAsync(i => i.CompanyId == cid && i.Kind == k && i.Status == InvoiceStatus.Draft); return (n, $"{n} hóa đơn {word} nháp chờ ghi sổ"); }, k);
        }
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

    /// <summary>
    /// Danh sách việc cụ thể theo chứng từ (tối đa <paramref name="take"/> dòng): chứng từ nháp / chờ duyệt / chờ ghi sổ mà người dùng được xem.
    /// Việc làm được ngay xếp trước, việc bị chặn (thiếu quyền, người lập không tự duyệt) xếp sau kèm lý do; trong mỗi nhóm việc cũ xếp trước.
    /// </summary>
    public async Task<IReadOnlyList<WorkTask>> TasksAsync(SessionContext ctx, int take = 100)
    {
        take = Math.Clamp(take, 1, 300);
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var cid = ctx.CompanyId;
        var distinct = await db.Companies.AsNoTracking().Where(c => c.Id == cid).Select(c => c.RequireDistinctApprover).FirstAsync();
        var rows = new List<(string Screen, string? Kind, string Type, Guid Id, string Number, string Title, string Action, Guid Owner, DateOnly Date, string? Blocked)>();
        var dues = new Dictionary<Guid, DateOnly>();   // hạn xử lý theo chứng từ (nếu chứng từ có hạn)
        bool Can(string screen) => ctx.Permissions.Contains(ViewPermission[screen]);
        string? Blocked(string permission, string label, Guid owner, bool separate)
            => !ctx.Permissions.Contains(permission) ? $"Cần quyền {label}"
             : separate && distinct && owner == ctx.UserId ? "Người lập không được tự thực hiện bước này; cần người khác" : null;
        const int Per = 60;

        if (Can("vouchers"))
        {
            var vs = await db.Vouchers.AsNoTracking().Where(v => v.CompanyId == cid && (v.Status == VoucherStatus.Draft || v.Status == VoucherStatus.Submitted || v.Status == VoucherStatus.Approved))
                .OrderBy(v => v.PostingDate).ThenBy(v => v.Number).Take(Per).Select(v => new { v.Id, v.Number, v.Description, v.PostingDate, v.CreatedBy, v.Status }).ToListAsync();
            foreach (var v in vs)
            {
                var (action, perm, label, sep) = v.Status switch
                {
                    VoucherStatus.Draft => ("Gửi duyệt", Permissions.VoucherSubmit, "gửi duyệt", false),
                    VoucherStatus.Submitted => ("Duyệt", Permissions.VoucherApprove, "duyệt chứng từ", true),
                    _ => ("Ghi sổ", Permissions.VoucherPost, "ghi sổ", false),
                };
                rows.Add(("vouchers", null, "voucher", v.Id, v.Number, v.Description, action, v.CreatedBy, v.PostingDate, Blocked(perm, label, v.CreatedBy, sep)));
            }
        }
        if (Can("trade"))
        {
            var invs = await db.CommercialInvoices.AsNoTracking().Where(i => i.CompanyId == cid && i.Status == InvoiceStatus.Draft)
                .OrderBy(i => i.PostingDate).ThenBy(i => i.Number).Take(Per).Select(i => new { i.Id, i.Number, i.Description, i.PostingDate, i.CreatedBy, i.Kind }).ToListAsync();
            foreach (var i in invs)
                rows.Add(("trade", i.Kind.ToString(), "invoice", i.Id, i.Number, i.Description, "Ghi sổ hóa đơn", i.CreatedBy, i.PostingDate, Blocked(Permissions.VoucherPost, "ghi sổ", i.CreatedBy, true)));
        }
        if (Can("stock"))
        {
            var docs = await db.StockDocuments.AsNoTracking().Where(d => d.CompanyId == cid && d.Status == StockDocumentStatus.Draft)
                .OrderBy(d => d.PostingDate).ThenBy(d => d.Number).Take(Per).Select(d => new { d.Id, d.Number, d.Description, d.PostingDate, d.CreatedBy }).ToListAsync();
            foreach (var d in docs)
                rows.Add(("stock", null, "stockDocument", d.Id, d.Number, d.Description, "Ghi sổ phiếu kho", d.CreatedBy, d.PostingDate, Blocked(Permissions.VoucherPost, "ghi sổ", d.CreatedBy, true)));
        }
        if (Can("bank"))
        {
            var pos = await db.PaymentOrders.AsNoTracking().Where(p => p.CompanyId == cid && (p.Status == PaymentOrderStatus.Created || p.Status == PaymentOrderStatus.ExecutedByBank))
                .OrderBy(p => p.RequestedDate).ThenBy(p => p.Number).Take(Per).Select(p => new { p.Id, p.Number, p.Description, p.RequestedDate, p.CreatedBy, p.Status }).ToListAsync();
            foreach (var p in pos)
                rows.Add(p.Status == PaymentOrderStatus.Created
                    ? ("bank", null, "paymentOrder", p.Id, p.Number, p.Description, "Duyệt lệnh thanh toán", p.CreatedBy, p.RequestedDate, Blocked(Permissions.PaymentOrderApprove, "duyệt lệnh chi", p.CreatedBy, true))
                    : ("bank", null, "paymentOrder", p.Id, p.Number, p.Description, "Lập chứng từ cho lệnh đã chi", p.CreatedBy, p.RequestedDate, Blocked(Permissions.BankDocument, "nghiệp vụ ngân hàng", p.CreatedBy, false)));
        }
        if (Can("orders"))
        {
            var os = await db.TradeOrders.AsNoTracking().Where(o => o.CompanyId == cid && o.Status == OrderStatus.Draft)
                .OrderBy(o => o.OrderDate).ThenBy(o => o.Number).Take(Per).Select(o => new { o.Id, o.Number, o.Description, o.OrderDate, o.CreatedBy, o.Kind }).ToListAsync();
            foreach (var o in os)
                rows.Add(("orders", o.Kind.ToString(), "order", o.Id, o.Number, o.Description ?? "", "Xác nhận đơn hàng", o.CreatedBy, o.OrderDate, Blocked(Permissions.InvoiceDocument, "đơn hàng", o.CreatedBy, false)));
        }
        if (ctx.Permissions.Contains(Permissions.ReportView))
        {
            var cs = await db.Contracts.AsNoTracking().Where(c => c.CompanyId == cid && c.Status == ContractStatus.PendingApproval)
                .OrderBy(c => c.EffectiveDate).ThenBy(c => c.Code).Take(Per).Select(c => new { c.Id, c.Code, c.Name, c.EffectiveDate, c.CreatedBy, c.Kind }).ToListAsync();
            foreach (var c in cs)
                rows.Add(("contracts", c.Kind.ToString(), "contract", c.Id, c.Code, c.Name, "Duyệt hợp đồng", c.CreatedBy, c.EffectiveDate, Blocked(Permissions.VoucherApprove, "duyệt hợp đồng", c.CreatedBy, true)));
        }

        if (ctx.Permissions.Contains(Permissions.VoucherView))   // màn Tạm ứng dùng quyền xem chứng từ
        {
            var advs = await db.AdvanceRequests.AsNoTracking().Where(a => a.CompanyId == cid && a.Status == AdvanceStatus.Requested)
                .OrderBy(a => a.DueDate).ThenBy(a => a.Number).Take(Per).Select(a => new { a.Id, a.Number, a.Purpose, a.RequestDate, a.DueDate, a.CreatedBy }).ToListAsync();
            foreach (var a in advs)
            {
                rows.Add(("advances", null, "advance", a.Id, a.Number, a.Purpose, "Duyệt đề nghị tạm ứng", a.CreatedBy, a.RequestDate, Blocked(Permissions.VoucherApprove, "duyệt", a.CreatedBy, true)));
                dues[a.Id] = a.DueDate;
            }
        }
        if (Can("services"))
        {
            var sas = await db.ServiceAcceptances.AsNoTracking().Where(a => a.CompanyId == cid && a.Status == ApprovalStatus.Requested)
                .OrderBy(a => a.AcceptanceDate).ThenBy(a => a.Number).Take(Per).Select(a => new { a.Id, a.Number, a.Note, a.AcceptanceDate, a.CreatedBy }).ToListAsync();
            foreach (var a in sas)
                rows.Add(("services", null, "serviceAcceptance", a.Id, a.Number, a.Note ?? "", "Xác nhận nghiệm thu dịch vụ", a.CreatedBy, a.AcceptanceDate, Blocked(Permissions.VoucherApprove, "duyệt", a.CreatedBy, true)));
        }

        var ordered = rows.OrderBy(r => r.Blocked is null ? 0 : 1).ThenBy(r => r.Date).ThenBy(r => r.Number, StringComparer.Ordinal).Take(take).ToList();
        var ids = ordered.Select(r => r.Owner).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        return ordered.Select(r => new WorkTask(r.Screen, r.Kind, r.Type, r.Id, r.Number, r.Title, r.Action, r.Owner, names.GetValueOrDefault(r.Owner, ""),
            r.Date, Math.Max(0, today.DayNumber - r.Date.DayNumber), r.Blocked, dues.TryGetValue(r.Id, out var due) ? due : null)).ToList();
    }
}
