using Hak.Application.Security;
using Hak.Application.Trade;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

/// <summary>Một trục trạng thái của đơn: Approval (duyệt) / Fulfilment (thực hiện) / Invoicing (hóa đơn) / Payment (tiền). State: None, Partial, Full, NA, Closed.</summary>
public sealed record FlowAxis(string Axis, string State, string Label, string Detail);

/// <param name="Screen">Khóa màn hình của ứng dụng để đi tới chứng từ.</param>
public sealed record FlowDocument(string Kind, Guid Id, string Number, string Status, DateOnly? Date, decimal? Quantity, decimal? Amount, string Screen);

public sealed record FlowNextAction(string Code, string Label, string Screen, Guid OwnerId, string OwnerName, DateOnly? Due);

public sealed record OrderFlow(Guid OrderId, string Number, string Kind, string Status, Guid PartnerId, string PartnerName, IReadOnlyList<FlowAxis> Axes,
    IReadOnlyList<FlowDocument> Documents, FlowNextAction? Next);

public sealed record WorklistRow(Guid OrderId, string Number, string Kind, string PartnerName, string ActionCode, string Action, string Screen, Guid OwnerId, string OwnerName,
    DateOnly? Due, int AgeDays, bool Overdue);

/// <summary>
/// Hồ sơ chu trình của một đơn (Đợt 4): bốn trục trạng thái độc lập — duyệt, thực hiện (giao / nhận), hóa đơn, tiền — cộng danh sách chứng từ liên quan để đi từ đơn xuống phiếu kho,
/// hóa đơn, bút toán, phân bổ tiền và quay lại; và hành động kế tiếp có người phụ trách (người xác nhận đơn, không có thì người lập) cùng hạn xử lý.
/// Chỉ đọc từ dữ liệu sẵn có (tiến độ đơn, phiếu kho, hóa đơn, phân bổ): không lưu trạng thái riêng nên không bao giờ lệch sổ.
/// </summary>
public sealed class OrderFlowService(HakDbContext db, OrderService orders, IClock clock)
{
    public async Task<OrderFlow> FlowAsync(SessionContext ctx, Guid orderId)
    {
        ctx.Require(Permissions.ReportView);
        var order = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == ctx.CompanyId)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Đơn hàng không tồn tại.");
        var names = await UserNamesAsync(ctx.CompanyId, [order.CreatedBy, order.ConfirmedBy ?? order.CreatedBy]);
        return await BuildAsync(ctx, order, names, withDocuments: true);
    }

    /// <summary>Việc cần làm theo đơn: mỗi đơn chưa xong một dòng kèm hành động kế tiếp, người phụ trách và hạn; đơn quá hạn xếp trước.</summary>
    public async Task<IReadOnlyList<WorklistRow>> WorklistAsync(SessionContext ctx, InvoiceKind kind, int take = 60)
    {
        ctx.Require(Permissions.ReportView);
        var list = await db.TradeOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == ctx.CompanyId && o.Kind == kind && (o.Status == OrderStatus.Draft || o.Status == OrderStatus.Confirmed))
            .OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Number).Take(Math.Clamp(take, 1, 100)).ToListAsync();
        var names = await UserNamesAsync(ctx.CompanyId, list.SelectMany(o => new[] { o.CreatedBy, o.ConfirmedBy ?? o.CreatedBy }));
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var rows = new List<WorklistRow>();
        foreach (var o in list)
        {
            var flow = await BuildAsync(ctx, o, names, withDocuments: false);
            if (flow.Next is not { } n) continue;
            rows.Add(new WorklistRow(o.Id, o.Number, kind.ToString(), flow.PartnerName, n.Code, n.Label, n.Screen, n.OwnerId, n.OwnerName, n.Due,
                Math.Max(0, today.DayNumber - o.OrderDate.DayNumber), n.Due is { } d && d < today));
        }
        return rows.OrderByDescending(r => r.Overdue).ThenBy(r => r.Due ?? DateOnly.MaxValue).ThenByDescending(r => r.AgeDays).ToList();
    }

    private async Task<OrderFlow> BuildAsync(SessionContext ctx, TradeOrder order, IReadOnlyDictionary<Guid, string> names, bool withDocuments)
    {
        var f = await orders.FulfilmentAsync(ctx, order.Id);
        var partner = await db.BusinessPartners.AsNoTracking().Where(p => p.Id == order.PartnerId).Select(p => p.Name).FirstAsync();
        var itemIds = order.Lines.Select(l => l.ItemId).Distinct().ToList();
        var services = (await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id) && i.IsService).Select(i => i.Id).ToListAsync()).ToHashSet();
        var sales = order.Kind == InvoiceKind.Sales;
        var owner = order.ConfirmedBy ?? order.CreatedBy;
        string Who(Guid id) => names.GetValueOrDefault(id, "");

        // ---- duyệt
        var approval = order.Status switch
        {
            OrderStatus.Draft => new FlowAxis("Approval", "None", "Chờ xác nhận", $"Lập bởi {Who(order.CreatedBy)}"),
            OrderStatus.Confirmed => new FlowAxis("Approval", "Full", "Đã xác nhận", $"{Who(order.ConfirmedBy ?? order.CreatedBy)} · {order.ConfirmedAt:dd/MM/yyyy}"),
            OrderStatus.Closed => new FlowAxis("Approval", "Closed", "Đã đóng", order.StatusReason ?? ""),
            _ => new FlowAxis("Approval", "Closed", "Đã hủy", order.StatusReason ?? ""),
        };

        // ---- thực hiện (hàng hóa; dịch vụ không qua kho)
        var goods = f.Lines.Where(l => !services.Contains(order.Lines.First(x => x.LineNo == l.LineNo).ItemId)).ToList();
        var ordered = goods.Sum(l => l.Ordered); var delivered = goods.Sum(l => l.StockPosted); var drafts = goods.Sum(l => l.StockDraft);
        var returned = goods.Sum(l => l.ReturnedQty);
        var verb = sales ? "giao" : "nhận";
        FlowAxis fulfilment = goods.Count == 0
            ? new FlowAxis("Fulfilment", "NA", "Không qua kho", "Dịch vụ: theo biên bản nghiệm thu")
            : delivered <= 0 ? new FlowAxis("Fulfilment", "None", $"Chưa {verb}", $"Đặt {ordered}" + (drafts > 0 ? $"; có phiếu nháp {drafts}" : ""))
            : delivered < ordered ? new FlowAxis("Fulfilment", "Partial", $"Đã {verb} một phần", $"{delivered}/{ordered}; còn {ordered - delivered}" + (returned > 0 ? $"; trả lại {returned}" : ""))
            : new FlowAxis("Fulfilment", "Full", $"Đã {verb} đủ", $"{delivered}/{ordered}" + (delivered > ordered ? " (vượt, trong dung sai)" : "") + (returned > 0 ? $"; trả lại {returned}" : ""));

        // ---- hóa đơn
        var normal = f.Invoices.Where(i => i.Purpose == InvoicePurpose.Normal).ToList();
        var invoicedQty = goods.Sum(l => l.Invoiced);
        var invoicedAmount = f.Lines.Sum(l => l.InvoicedAmount);
        FlowAxis invoicing;
        if (goods.Count == 0)
        {
            var expected = f.Lines.Sum(l => l.Ordered * l.UnitPrice * (100 - l.DiscountPercent) / 100m);   // sau chiết khấu thương mại đã thỏa thuận trên đơn
            invoicing = invoicedAmount <= 0 ? new FlowAxis("Invoicing", "None", "Chưa lập hóa đơn", "")
                : invoicedAmount < expected ? new FlowAxis("Invoicing", "Partial", "Lập một phần", $"{invoicedAmount}/{expected}")
                : new FlowAxis("Invoicing", "Full", "Đã lập đủ", $"{invoicedAmount}");
        }
        else if (invoicedQty <= 0) invoicing = new FlowAxis("Invoicing", "None", "Chưa lập hóa đơn", delivered > 0 ? $"Đã {verb} {delivered} chưa có hóa đơn" : "Chưa có hàng để lập");
        else if (invoicedQty < ordered || invoicedQty < delivered)
            invoicing = new FlowAxis("Invoicing", "Partial", "Lập một phần", $"Hóa đơn {invoicedQty}; đã {verb} {delivered}; {verb} chưa lập {Math.Max(0, delivered - invoicedQty)}");
        else invoicing = new FlowAxis("Invoicing", "Full", "Đã lập đủ", $"{invoicedQty}/{ordered}" + (f.Invoices.Any(i => i.Purpose != InvoicePurpose.Normal) ? "; có hóa đơn điều chỉnh" : ""));

        // ---- tiền
        var total = normal.Sum(i => i.Total); var settled = normal.Sum(i => i.Settled); var open = normal.Sum(i => i.Open);
        var money = sales ? "thu" : "trả";
        FlowAxis payment = normal.Count == 0 ? new FlowAxis("Payment", "None", "Chưa có công nợ", "")
            : open <= 0 ? new FlowAxis("Payment", "Full", $"Đã {money} đủ", $"{settled}/{total}")
            : settled <= 0 ? new FlowAxis("Payment", "None", $"Chưa {money}", $"Còn {open}")
            : new FlowAxis("Payment", "Partial", $"Đã {money} một phần", $"{settled}/{total}; còn {open}");

        // ---- hành động kế tiếp
        var firstDue = await FirstDueAsync(f, order);
        FlowNextAction? next = null;
        FlowNextAction Act(string code, string label, string screen, DateOnly? due) => new(code, label, screen, owner, Who(owner), due);
        if (order.Status == OrderStatus.Draft) next = Act("CONFIRM", "Xác nhận đơn", "orders", null);
        else if (order.Status == OrderStatus.Confirmed)
        {
            if (goods.Count > 0 && delivered < ordered) next = Act(sales ? "DELIVER" : "RECEIVE", sales ? "Giao hàng (lập phiếu xuất)" : "Nhận hàng (lập phiếu nhập)", "stock", order.ExpectedDate);
            else if (goods.Count > 0 && invoicedQty < delivered) next = Act("INVOICE", "Lập hóa đơn", "trade", null);
            else if (goods.Count == 0 && invoicing.State != "Full") next = Act("SERVICE_INVOICE", "Nghiệm thu và lập hóa đơn dịch vụ", "services", null);
            else if (open > 0) next = Act(sales ? "COLLECT" : "PAY", sales ? "Thu tiền" : "Chấp nhận và thanh toán", sales ? "revenue" : "procurement", firstDue);
            else if (invoicing.State == "Full" && payment.State == "Full") next = Act("CLOSE", "Đóng đơn", "orders", null);
        }

        var docs = withDocuments ? await DocumentsAsync(ctx, order, f) : [];
        return new OrderFlow(order.Id, order.Number, order.Kind.ToString(), order.Status.ToString(), order.PartnerId, partner, [approval, fulfilment, invoicing, payment], docs, next);
    }

    private async Task<DateOnly?> FirstDueAsync(OrderFulfilment f, TradeOrder order)
    {
        var ids = f.Invoices.Where(i => i.Purpose == InvoicePurpose.Normal && i.Open > 0).Select(i => i.InvoiceId).ToList();
        if (ids.Count == 0) return null;
        return (await db.CommercialInvoices.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => i.DueDate).ToListAsync()).Where(d => d != null).Min();
    }

    private async Task<List<FlowDocument>> DocumentsAsync(SessionContext ctx, TradeOrder order, OrderFulfilment f)
    {
        var docs = new List<FlowDocument>();
        var stock = await (from l in db.StockDocumentLines join d in db.StockDocuments on l.DocumentId equals d.Id
                           where l.OrderId == order.Id && d.Status != StockDocumentStatus.Cancelled
                           select new { d.Id, d.Number, d.Type, d.Status, d.PostingDate, l.Quantity }).AsNoTracking().ToListAsync();
        foreach (var g in stock.GroupBy(s => s.Id))
        {
            var first = g.First();
            docs.Add(new FlowDocument(first.Type == StockDocumentType.Issue ? "Phiếu xuất" : "Phiếu nhập", first.Id, first.Number, first.Status.ToString(), first.PostingDate, g.Sum(x => x.Quantity), null, "stock"));
        }
        var stockIds = stock.Select(s => s.Id).Distinct().ToList();
        foreach (var bb in await db.GoodsInspections.AsNoTracking().Where(i => stockIds.Contains(i.StockDocumentId) && !i.IsCancelled).ToListAsync())
            docs.Add(new FlowDocument("Biên bản kiểm nghiệm", bb.Id, bb.Number, "Active", bb.InspectionDate, null, null, "procurement"));
        foreach (var r in await db.StockReservations.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId && r.OrderId == order.Id).OrderBy(r => r.CreatedAt).ToListAsync())
            docs.Add(new FlowDocument("Giữ hàng", r.Id, $"Dòng {r.OrderLineNo}", r.Status.ToString(), DateOnly.FromDateTime(r.CreatedAt), r.Remaining, null, "orders"));

        var invoiceIds = f.Invoices.Select(x => x.InvoiceId).ToList();
        var invoices = await db.CommercialInvoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToListAsync();
        foreach (var iv in f.Invoices)
        {
            var inv = invoices.First(x => x.Id == iv.InvoiceId);
            docs.Add(new FlowDocument(iv.Purpose == InvoicePurpose.Normal ? "Hóa đơn" : $"Hóa đơn điều chỉnh ({iv.Purpose})", iv.InvoiceId, iv.Number, inv.Status.ToString(), inv.PostingDate, null, iv.Total, "trade"));
            if (inv.VoucherId is { } vid)
            {
                var v = await db.Vouchers.AsNoTracking().Where(x => x.Id == vid).Select(x => new { x.Number, x.PostingDate }).FirstOrDefaultAsync();
                if (v is not null) docs.Add(new FlowDocument("Chứng từ kế toán", vid, v.Number, "Posted", v.PostingDate, null, iv.Total, "vouchers"));
                // Tiền đã phân bổ vào công nợ của hóa đơn: chứng từ phía đối ứng (phiếu thu / chi, bù trừ).
                var partyLine = await (from jl in db.JournalLines join e in db.JournalEntries on jl.JournalEntryId equals e.Id
                                       join a in db.Accounts on jl.AccountId equals a.Id
                                       where e.VoucherId == vid && jl.PartnerId == inv.PartnerId && a.OpenItemNature != Domain.Catalog.OpenItemNature.None select jl.Id).FirstOrDefaultAsync();
                if (partyLine == Guid.Empty) continue;
                var allocs = await db.SettlementAllocations.AsNoTracking().Where(a => a.VoidedAt == null && (a.DebitLineId == partyLine || a.CreditLineId == partyLine)).ToListAsync();
                foreach (var a in allocs)
                {
                    var otherLine = a.DebitLineId == partyLine ? a.CreditLineId : a.DebitLineId;
                    var ov = await (from jl in db.JournalLines join e in db.JournalEntries on jl.JournalEntryId equals e.Id join vv in db.Vouchers on e.VoucherId equals vv.Id
                                    where jl.Id == otherLine select new { vv.Id, vv.Number, vv.PostingDate }).FirstOrDefaultAsync();
                    if (ov is not null) docs.Add(new FlowDocument("Thanh toán / bù trừ", ov.Id, ov.Number, "Posted", a.AllocationDate, null, a.Amount, "vouchers"));
                }
            }
        }
        return docs;
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(Guid companyId, IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
    }
}
