using Hak.Application.Audit;
using Hak.Application.Banking;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Application.Settlement;
using Hak.Data;
using Hak.Domain.Banking;
using Hak.Domain.Common;
using Hak.Domain.Cycles;
using Hak.Domain.Ledger;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Cycles;

/// <summary>Một hóa đơn mua còn nợ trong hàng chờ thanh toán theo ngày đáo hạn (chu trình Cung ứng).</summary>
public sealed record PayableQueueRow(Guid InvoiceId, string Number, string InvoiceSeries, string InvoiceNo, DateOnly InvoiceDate, DateOnly? DueDate,
    Guid PartnerId, string PartnerCode, string PartnerName, decimal Total, decimal Open, bool HasReceiptLink, bool FromOrder,
    Guid? AcceptanceId, string Stage);

/// <summary>Một bàn giao trong hộp thư của chu trình thanh toán.</summary>
public sealed record PaymentInboxRow(Guid AcceptanceId, Guid InvoiceId, string InvoiceNumber, string InvoiceSeries, string InvoiceNo, Guid PartnerId, string PartnerName,
    decimal Amount, decimal Open, DateOnly? DueDate, string Stage, Guid? PaymentOrderId, string? PaymentOrderNumber, string? PaymentOrderStatus,
    DateTime AcceptedAt, string? Note, string? CloseReason);

/// <summary>
/// Cung ứng → Thanh toán (docs/10, mục 3). Chấp nhận thanh toán không ghi Nhật ký; chu trình thanh toán nhận bàn giao và tự tạo
/// lệnh thanh toán Nháp khi công ty đã ánh xạ vai trò BANK_PAYMENT_DEFAULT và người chấp nhận có quyền lập lệnh; nếu không, bàn giao
/// nằm trong hộp thư để người thanh toán tạo lệnh. Duyệt lệnh vẫn là người khác (tách nhiệm vụ trong PaymentOrderService).
/// </summary>
public sealed class PaymentAcceptanceService(HakDbContext db, PaymentOrderService orders, SettlementService settlement, AuditService audit, IClock clock)
{
    public const string BankRole = "BANK_PAYMENT_DEFAULT";

    // Stage codes shown to users; "Paid" is derived from the payable line, never stored.
    public static class Stages
    {
        public const string NotAccepted = "NOT_ACCEPTED";
        public const string AwaitingOrder = "AWAITING_ORDER";
        public const string OrderCreated = "ORDER_CREATED";
        public const string OrderApproved = "ORDER_APPROVED";
        public const string SentToBank = "SENT_TO_BANK";
        public const string BankExecuted = "BANK_EXECUTED";
        public const string Paid = "PAID";
        public const string Rejected = "REJECTED";
        public const string Cancelled = "CANCELLED";
    }

    // ---------------------------------------------------------------- queries

    /// <summary>Hóa đơn mua (VND, thường) đã ghi sổ còn nợ đến <paramref name="asOf"/>, xếp theo ngày đáo hạn.</summary>
    public async Task<IReadOnlyList<PayableQueueRow>> PayableQueueAsync(SessionContext ctx, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var ap = await RoleAccountAsync(ctx.CompanyId, "AP_TRADE") ?? throw Unmapped("AP_TRADE");
        var invoices = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.CompanyId == ctx.CompanyId && i.Kind == InvoiceKind.Purchase && i.Purpose == InvoicePurpose.Normal
                        && i.Status == InvoiceStatus.Posted && i.VoucherId != null && i.Currency == null && i.PostingDate <= asOf).ToListAsync();
        var lines = await PayableLinesAsync(ctx.CompanyId, ap, invoices);
        var open = (await settlement.OpenItemsAsync(ctx, ap, null, asOf)).ToDictionary(o => o.JournalLineId);
        var accept = await ActiveAcceptancesAsync(ctx.CompanyId);
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var orderStatus = await OrderStatusesAsync(accept.Values.Select(a => a.PaymentOrderId).OfType<Guid>().ToList());

        var rows = new List<PayableQueueRow>();
        foreach (var inv in invoices)
        {
            if (!lines.TryGetValue(inv.Id, out var lineId) || !open.TryGetValue(lineId, out var item) || item.Open <= 0) continue;
            accept.TryGetValue(inv.Id, out var acc);
            var p = partners[inv.PartnerId];
            rows.Add(new PayableQueueRow(inv.Id, inv.Number, inv.InvoiceSeries, inv.InvoiceNo, inv.InvoiceDate, inv.DueDate, inv.PartnerId, p.Code, p.Name,
                item.Amount, item.Open, inv.Lines.Any(l => l.StockDocumentId != null), inv.Lines.Any(l => l.OrderId != null),
                acc?.Id, acc is null ? Stages.NotAccepted : StageOf(acc, acc.PaymentOrderId is { } o ? orderStatus.GetValueOrDefault(o) : null, item.Open)));
        }
        return rows.OrderBy(r => r.DueDate ?? DateOnly.MaxValue).ThenBy(r => r.InvoiceDate).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
    }

    /// <summary>Hộp thư chu trình thanh toán: mọi bàn giao chưa đóng (kể cả đã tạo lệnh) và các bàn giao bị từ chối/hủy gần đây.</summary>
    public async Task<IReadOnlyList<PaymentInboxRow>> InboxAsync(SessionContext ctx, bool includeClosed = false)
    {
        ctx.Require(Permissions.ReportView);
        var q = db.PaymentAcceptances.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId);
        if (!includeClosed) q = q.Where(a => a.Status == PaymentAcceptanceStatus.Pending || a.Status == PaymentAcceptanceStatus.OrderCreated);
        var list = await q.OrderBy(a => a.DueDate).ThenBy(a => a.AcceptedAt).ToListAsync();
        var invs = await db.CommercialInvoices.AsNoTracking().Where(i => list.Select(a => a.InvoiceId).Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var pos = await db.PaymentOrders.AsNoTracking().Where(o => list.Select(a => a.PaymentOrderId).Contains(o.Id)).ToDictionaryAsync(o => o.Id);
        var asOf = DateOnly.FromDateTime(clock.UtcNow);
        var ap = await RoleAccountAsync(ctx.CompanyId, "AP_TRADE");
        var open = ap is null ? [] : (await settlement.OpenItemsAsync(ctx, ap.Value, null, asOf, includeSettled: true)).ToDictionary(o => o.JournalLineId);
        return list.Select(a =>
        {
            var inv = invs[a.InvoiceId]; pos.TryGetValue(a.PaymentOrderId ?? Guid.Empty, out var po);
            var left = open.TryGetValue(a.SettleJournalLineId, out var it) ? it.Open : a.Amount;
            return new PaymentInboxRow(a.Id, a.InvoiceId, inv.Number, inv.InvoiceSeries, inv.InvoiceNo, a.PartnerId, partners[a.PartnerId].Name, a.Amount, left, a.DueDate,
                StageOf(a, po?.Status, left), po?.Id, po?.Number, po?.Status.ToString(), a.AcceptedAt, a.Note, a.CloseReason);
        }).ToList();
    }

    // ---------------------------------------------------------------- commands

    /// <summary>Chấp nhận thanh toán một hóa đơn mua đã ghi sổ. Không ghi Nhật ký. Bàn giao sang thanh toán trong cùng giao dịch.</summary>
    public async Task<PaymentAcceptance> AcceptAsync(SessionContext ctx, Guid invoiceId, string? note)
    {
        ctx.Require(Permissions.PaymentAccept);
        return await db.InTransactionAsync(async () =>
        {
            var inv = await db.CommercialInvoices.AsNoTracking().Include(i => i.Lines)
                          .FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Hóa đơn không tồn tại.");
            if (inv.Kind != InvoiceKind.Purchase || inv.Purpose != InvoicePurpose.Normal)
                throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ hóa đơn mua thông thường mới được chấp nhận thanh toán.");
            if (inv.Status != InvoiceStatus.Posted || inv.VoucherId is null)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Hóa đơn phải đã ghi sổ mới chấp nhận thanh toán.");
            if (inv.Currency is not null)
                throw new DomainException(ErrorCodes.ValidationFailed, "Hóa đơn ngoại tệ thanh toán qua chức năng Thu/chi ngoại tệ theo hóa đơn.");
            if (await db.PaymentAcceptances.AnyAsync(a => a.CompanyId == ctx.CompanyId && a.InvoiceId == invoiceId
                    && (a.Status == PaymentAcceptanceStatus.Pending || a.Status == PaymentAcceptanceStatus.OrderCreated)))
                throw new DomainException(ErrorCodes.DuplicateSource, "Hóa đơn đã được chấp nhận thanh toán.");

            var ap = await RoleAccountAsync(ctx.CompanyId, "AP_TRADE") ?? throw Unmapped("AP_TRADE");
            var lineId = (await PayableLinesAsync(ctx.CompanyId, ap, [inv])).GetValueOrDefault(inv.Id);
            if (lineId == Guid.Empty) throw new DomainException(ErrorCodes.NotFound, "Không tìm thấy dòng công nợ phải trả của hóa đơn.");
            var today = DateOnly.FromDateTime(clock.UtcNow);
            var asOf = today > inv.PostingDate ? today : inv.PostingDate;
            var item = (await settlement.OpenItemsAsync(ctx, ap, inv.PartnerId, asOf)).FirstOrDefault(o => o.JournalLineId == lineId);
            if (item is null || item.Open <= 0)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Hóa đơn không còn khoản phải trả.");

            var acc = new PaymentAcceptance
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, InvoiceId = inv.Id, PartnerId = inv.PartnerId, SettleJournalLineId = lineId, Amount = item.Open,
                DueDate = inv.DueDate, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), AcceptedBy = ctx.UserId, AcceptedAt = clock.UtcNow, RowVersion = 1,
            };
            db.PaymentAcceptances.Add(acc);
            await audit.AppendAsync(ctx.CompanyId, ctx, "payment_acceptance.accept", "payment_acceptance", acc.Id.ToString(),
                new { invoice = inv.Number, amount = acc.Amount, acc.DueDate }, acc.RowVersion);
            await db.SaveChangesAsync();

            // Chu trình thanh toán tự nhận: tạo lệnh Nháp nếu đã có tài khoản ngân hàng mặc định và người chấp nhận có quyền lập lệnh.
            var bank = await RoleAccountAsync(ctx.CompanyId, BankRole);
            if (bank is { } bankId && ctx.Permissions.Contains(Permissions.BankDocument))
                await CreateOrderCoreAsync(ctx, acc, inv, bankId);
            return acc;
        });
    }

    /// <summary>Người thanh toán tạo lệnh cho bàn giao đang chờ (khi chưa có tài khoản ngân hàng mặc định).</summary>
    public async Task<PaymentOrder> CreateOrderAsync(SessionContext ctx, Guid acceptanceId, Guid bankAccountId)
    {
        ctx.Require(Permissions.BankDocument);
        return await db.InTransactionAsync(async () =>
        {
            var acc = await LoadAsync(ctx, acceptanceId);
            if (acc.Status != PaymentAcceptanceStatus.Pending)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Bàn giao không còn ở trạng thái chờ tạo lệnh.");
            var inv = await db.CommercialInvoices.AsNoTracking().FirstAsync(i => i.Id == acc.InvoiceId);
            return await CreateOrderCoreAsync(ctx, acc, inv, bankAccountId);
        });
    }

    /// <summary>Chu trình thanh toán từ chối bàn giao (kèm lý do), trả về Cung ứng.</summary>
    public async Task<PaymentAcceptance> RejectAsync(SessionContext ctx, Guid acceptanceId, string reason)
    {
        ctx.Require(Permissions.BankDocument);
        return await CloseAsync(ctx, acceptanceId, reason, PaymentAcceptanceStatus.Rejected, "reject", requirePending: true);
    }

    /// <summary>Cung ứng rút lại chấp nhận; nếu lệnh đã tạo thì lệnh phải chưa duyệt và bị hủy theo.</summary>
    public async Task<PaymentAcceptance> CancelAsync(SessionContext ctx, Guid acceptanceId, string reason)
    {
        ctx.Require(Permissions.PaymentAccept);
        return await CloseAsync(ctx, acceptanceId, reason, PaymentAcceptanceStatus.Cancelled, "cancel", requirePending: false);
    }

    // ---------------------------------------------------------------- internals

    private async Task<PaymentOrder> CreateOrderCoreAsync(SessionContext ctx, PaymentAcceptance acc, CommercialInvoice inv, Guid bankAccountId)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var requested = acc.DueDate is { } d && d > today ? d : today;
        var po = await orders.CreateAsync(ctx, new PaymentOrderInput(bankAccountId, acc.PartnerId, acc.Amount, requested,
            $"Thanh toán hóa đơn {inv.InvoiceSeries}/{inv.InvoiceNo}", SettleJournalLineId: acc.SettleJournalLineId));
        var tracked = await db.PaymentAcceptances.FirstAsync(a => a.Id == acc.Id);
        tracked.PaymentOrderId = po.Id;
        tracked.Status = PaymentAcceptanceStatus.OrderCreated;
        tracked.RowVersion++;
        await audit.AppendAsync(ctx.CompanyId, ctx, "payment_acceptance.order_created", "payment_acceptance", acc.Id.ToString(),
            new { order = po.Number, invoice = inv.Number }, tracked.RowVersion);
        await db.SaveChangesAsync();
        return po;
    }

    private async Task<PaymentAcceptance> CloseAsync(SessionContext ctx, Guid id, string reason, PaymentAcceptanceStatus to, string action, bool requirePending)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do.");
        return await db.InTransactionAsync(async () =>
        {
            var acc = await LoadAsync(ctx, id);
            if (acc.Status is PaymentAcceptanceStatus.Rejected or PaymentAcceptanceStatus.Cancelled)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Bàn giao đã đóng.");
            if (requirePending && acc.Status != PaymentAcceptanceStatus.Pending)
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Chỉ từ chối được bàn giao chưa tạo lệnh.");
            if (acc.PaymentOrderId is { } poId)
            {
                var po = await db.PaymentOrders.AsNoTracking().FirstAsync(o => o.Id == poId);
                if (po.Status == PaymentOrderStatus.Created) await orders.CancelAsync(ctx, poId, reason);
                else if (po.Status != PaymentOrderStatus.Cancelled)
                    throw new DomainException(ErrorCodes.InvalidStateTransition, $"Lệnh {po.Number} đã {po.Status}; hủy lệnh ở màn Ngân hàng trước khi rút chấp nhận.");
            }
            var tracked = await db.PaymentAcceptances.FirstAsync(a => a.Id == id);
            tracked.Status = to; tracked.ClosedBy = ctx.UserId; tracked.ClosedAt = clock.UtcNow; tracked.CloseReason = reason.Trim(); tracked.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, $"payment_acceptance.{action}", "payment_acceptance", id.ToString(), new { reason }, tracked.RowVersion);
            await db.SaveChangesAsync();
            return tracked;
        });
    }

    private async Task<PaymentAcceptance> LoadAsync(SessionContext ctx, Guid id)
        => await db.PaymentAcceptances.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == ctx.CompanyId)
           ?? throw new DomainException(ErrorCodes.NotFound, "Bàn giao không tồn tại.");

    private static string StageOf(PaymentAcceptance a, PaymentOrderStatus? order, decimal open)
    {
        if (a.Status == PaymentAcceptanceStatus.Rejected) return Stages.Rejected;
        if (a.Status == PaymentAcceptanceStatus.Cancelled) return Stages.Cancelled;
        if (open <= 0 || order is PaymentOrderStatus.Posted or PaymentOrderStatus.Reconciled) return Stages.Paid;
        return order switch
        {
            null => Stages.AwaitingOrder,
            PaymentOrderStatus.Created => Stages.OrderCreated,
            PaymentOrderStatus.Approved => Stages.OrderApproved,
            PaymentOrderStatus.SentToBank => Stages.SentToBank,
            PaymentOrderStatus.ExecutedByBank => Stages.BankExecuted,
            _ => Stages.AwaitingOrder,   // order cancelled: back to the payment inbox
        };
    }

    private async Task<Guid?> RoleAccountAsync(Guid companyId, string role)
        => (await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == companyId && m.RoleCode == role))?.AccountId;

    private static DomainException Unmapped(string role) => new(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {role}.");

    /// <summary>invoice id → journal line id of its payable (AP, credit, same partner) on the voucher it was posted with.</summary>
    private async Task<Dictionary<Guid, Guid>> PayableLinesAsync(Guid companyId, Guid ap, IReadOnlyList<CommercialInvoice> invoices)
    {
        var voucherIds = invoices.Select(i => i.VoucherId!.Value).ToList();
        var rows = await (from e in db.JournalEntries.AsNoTracking().Where(e => e.CompanyId == companyId && voucherIds.Contains(e.VoucherId))
                          join l in db.JournalLines.AsNoTracking().Where(l => l.AccountId == ap && l.Side == Side.Credit) on e.Id equals l.JournalEntryId
                          select new { e.VoucherId, l.Id, l.PartnerId, l.LineNo }).ToListAsync();
        var result = new Dictionary<Guid, Guid>();
        foreach (var inv in invoices)
            if (rows.Where(r => r.VoucherId == inv.VoucherId && r.PartnerId == inv.PartnerId).OrderBy(r => r.LineNo).FirstOrDefault() is { } r) result[inv.Id] = r.Id;
        return result;
    }

    private async Task<Dictionary<Guid, PaymentAcceptance>> ActiveAcceptancesAsync(Guid companyId)
        => (await db.PaymentAcceptances.AsNoTracking().Where(a => a.CompanyId == companyId
                && (a.Status == PaymentAcceptanceStatus.Pending || a.Status == PaymentAcceptanceStatus.OrderCreated)).ToListAsync())
            .GroupBy(a => a.InvoiceId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AcceptedAt).First());

    private async Task<Dictionary<Guid, PaymentOrderStatus>> OrderStatusesAsync(IReadOnlyList<Guid> ids)
        => ids.Count == 0 ? [] : await db.PaymentOrders.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Status);
}
