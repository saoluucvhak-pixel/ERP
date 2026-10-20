using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record ServiceAcceptanceLineInput(int OrderLineNo, decimal Quantity, string? Note = null);
public sealed record ServiceAcceptanceInput(Guid OrderId, DateOnly AcceptanceDate, IReadOnlyList<ServiceAcceptanceLineInput> Lines, string? Note = null);
public sealed record ServiceAcceptanceLineView(int OrderLineNo, Guid ItemId, string ItemCode, string ItemName, string Unit, decimal Quantity, string? Note);
public sealed record ServiceAcceptanceRow(Guid Id, string Number, InvoiceKind Kind, Guid OrderId, string OrderNumber, Guid PartnerId, string PartnerCode, string PartnerName, DateOnly AcceptanceDate,
    ApprovalStatus Status, string? Note, string? DecisionNote, DateTime CreatedAt, IReadOnlyList<ServiceAcceptanceLineView> Lines);
/// <summary>Dòng dịch vụ của đơn hàng: đã đặt, đã nghiệm thu (đã xác nhận), đã lập hóa đơn, còn có thể lập hóa đơn — kèm giá, thuế suất, tài khoản mặc định để điền vào hóa đơn.</summary>
public sealed record ServiceInvoiceCandidate(Guid OrderId, string OrderNumber, int OrderLineNo, Guid ItemId, string ItemCode, string ItemName, string Unit, decimal Ordered, decimal Accepted, decimal Invoiced,
    decimal Remaining, decimal UnitPrice, decimal? VatRate, Guid? AccountId, string? Description);

/// <summary>
/// Dịch vụ (vận chuyển thuê, tư vấn…) theo thông lệ các ERP (SAP: service master + service entry sheet; Odoo: sản phẩm loại dịch vụ, lập hóa đơn theo số lượng đã thực hiện; ERPNext: mặt hàng không tồn kho;
/// Dynamics: dịch vụ không qua kho): dịch vụ đi qua đơn hàng (mua / bán) → biên bản nghiệm thu dịch vụ (lập, người khác xác nhận) → hóa đơn trong phạm vi số lượng đã nghiệm thu → thu / chi tiền.
/// Nghiệm thu không ghi Nhật ký; Nhật ký phát sinh ở hóa đơn (bán: Có doanh thu; mua: Nợ chi phí). Số lượng nghiệm thu mỗi dòng không vượt số lượng đặt (cộng dung sai của đơn).
/// </summary>
public sealed class ServiceAcceptanceService(HakDbContext db, AuditService audit, IClock clock)
{
    public async Task<ServiceAcceptance> CreateAsync(SessionContext ctx, ServiceAcceptanceInput input)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (input.Lines.Count == 0) throw new DomainException(ErrorCodes.VoucherNoLines, "Biên bản chưa có dòng.");
        if (input.Lines.Select(l => l.OrderLineNo).Distinct().Count() != input.Lines.Count) throw new DomainException(ErrorCodes.ValidationFailed, "Mỗi dòng đơn hàng chỉ một lần trong biên bản.");
        return await db.InTransactionAsync(async () =>
        {
            var order = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == input.OrderId && o.CompanyId == ctx.CompanyId)
                        ?? throw new DomainException(ErrorCodes.NotFound, "Đơn hàng không tồn tại.");
            if (order.Status != OrderStatus.Confirmed) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Đơn {order.Number} phải đã xác nhận mới nghiệm thu dịch vụ.");
            var itemIds = order.Lines.Select(l => l.ItemId).Distinct().ToList();
            var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
            var counted = await CountedAsync(order.Id, [ApprovalStatus.Requested, ApprovalStatus.Approved]);
            foreach (var l in input.Lines)
            {
                var ol = order.Lines.FirstOrDefault(x => x.LineNo == l.OrderLineNo) ?? throw new DomainException(ErrorCodes.NotFound, $"Đơn {order.Number} không có dòng {l.OrderLineNo}.");
                if (!items[ol.ItemId].IsService) throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {l.OrderLineNo} của đơn {order.Number} không phải dịch vụ (hàng hóa đi qua kho).");
                if (l.Quantity <= 0 || !MoneyMath.HasAtMostDecimals(l.Quantity, MoneyMath.StorageScale)) throw new DomainException(ErrorCodes.AmountNotPositive, $"Dòng {l.OrderLineNo}: số lượng phải lớn hơn 0, tối đa 4 chữ số thập phân.");
                var total = counted.GetValueOrDefault(l.OrderLineNo) + l.Quantity;
                var limit = ol.Quantity * (1 + order.OverDeliveryTolerancePercent / 100m);
                if (total > limit)
                    throw new DomainException(ErrorCodes.OverDelivered, $"Dòng {l.OrderLineNo} của đơn {order.Number}: nghiệm thu đạt {total} so với đặt {ol.Quantity} (dung sai {order.OverDeliveryTolerancePercent}%).");
            }
            var fy = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.CompanyId == ctx.CompanyId && f.StartDate <= input.AcceptanceDate && f.EndDate >= input.AcceptanceDate)
                     ?? throw new DomainException(ErrorCodes.FiscalYearNotFound, $"Không có năm tài chính chứa ngày {input.AcceptanceDate.Vn()}.");
            var a = new ServiceAcceptance { Id = Ids.New(), CompanyId = ctx.CompanyId, Kind = order.Kind, PartnerId = order.PartnerId, OrderId = order.Id, AcceptanceDate = input.AcceptanceDate,
                Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(), CreatedBy = ctx.UserId, CreatedAt = clock.UtcNow };
            a.Number = await Numbering.NextAsync(db, ctx.CompanyId, fy.StartDate, order.Kind == InvoiceKind.Purchase ? "NTDM" : "NTDV");
            foreach (var l in input.Lines.OrderBy(x => x.OrderLineNo))
                a.Lines.Add(new ServiceAcceptanceLine { Id = Ids.New(), AcceptanceId = a.Id, OrderLineNo = l.OrderLineNo, ItemId = order.Lines.First(x => x.LineNo == l.OrderLineNo).ItemId, Quantity = l.Quantity, Note = l.Note });
            db.ServiceAcceptances.Add(a);
            await audit.AppendAsync(ctx.CompanyId, ctx, "service_acceptance.create", "service_acceptance", a.Id.ToString(), new { a.Number, order = order.Number, lines = a.Lines.Count });
            await db.SaveChangesAsync();
            return a;
        });
    }

    /// <summary>Xác nhận (hoặc từ chối) nghiệm thu: người xác nhận có quyền duyệt chứng từ, khác người lập khi công ty bật nguyên tắc người duyệt khác người lập.</summary>
    public async Task<ServiceAcceptance> DecideAsync(SessionContext ctx, Guid id, bool approve, string? note)
    {
        ctx.Require(Permissions.VoucherApprove);
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new DomainException(ErrorCodes.ReasonRequired, "Từ chối phải ghi lý do.");
        return await db.InTransactionAsync(async () =>
        {
            var a = await db.ServiceAcceptances.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Biên bản không tồn tại.");
            if (a.Status != ApprovalStatus.Requested) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Biên bản {a.Number} không ở trạng thái chờ xác nhận.");
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            if (company.RequireDistinctApprover && a.CreatedBy == ctx.UserId) throw new DomainException(ErrorCodes.SodViolation, "Người xác nhận phải khác người lập biên bản.");
            a.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected; a.DecisionNote = note; a.DecidedBy = ctx.UserId; a.DecidedAt = clock.UtcNow;
            await audit.AppendAsync(ctx.CompanyId, ctx, approve ? "service_acceptance.approve" : "service_acceptance.reject", "service_acceptance", a.Id.ToString(), new { a.Number, note });
            await db.SaveChangesAsync();
            return a;
        });
    }

    /// <summary>Hủy biên bản chờ xác nhận, hoặc biên bản đã xác nhận nếu phần dịch vụ còn lại sau khi hủy vẫn đủ cho số đã lập hóa đơn.</summary>
    public async Task CancelAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy.");
        await db.InTransactionAsync(async () =>
        {
            var a = await db.ServiceAcceptances.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Biên bản không tồn tại.");
            if (a.Status is not (ApprovalStatus.Requested or ApprovalStatus.Approved)) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Biên bản {a.Number} không còn hiệu lực để hủy.");
            if (a.Status == ApprovalStatus.Approved)
            {
                var approved = await CountedAsync(a.OrderId, [ApprovalStatus.Approved]);
                var invoiced = await InvoicedAsync(a.OrderId, null);
                foreach (var l in a.Lines)
                    if (approved.GetValueOrDefault(l.OrderLineNo) - l.Quantity < invoiced.GetValueOrDefault(l.OrderLineNo))
                        throw new DomainException(ErrorCodes.OverInvoiced, $"Không hủy được {a.Number}: dòng đơn {l.OrderLineNo} đã lập hóa đơn vượt phần nghiệm thu còn lại; hủy hoặc điều chỉnh hóa đơn trước.");
            }
            a.Status = ApprovalStatus.Cancelled; a.DecisionNote = reason;
            await audit.AppendAsync(ctx.CompanyId, ctx, "service_acceptance.cancel", "service_acceptance", a.Id.ToString(), new { a.Number, reason });
            await db.SaveChangesAsync();
        });
    }

    public async Task<IReadOnlyList<ServiceAcceptanceRow>> ListAsync(SessionContext ctx, InvoiceKind? kind, Guid? orderId)
    {
        ctx.Require(Permissions.VoucherView);
        var q = db.ServiceAcceptances.AsNoTracking().Include(a => a.Lines).Where(a => a.CompanyId == ctx.CompanyId);
        if (kind is { } k) q = q.Where(a => a.Kind == k);
        if (orderId is { } o) q = q.Where(a => a.OrderId == o);
        var list = await q.OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync();
        var orders = await db.TradeOrders.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId).ToDictionaryAsync(x => x.Id, x => x.Number);
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id);
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id);
        return list.Select(a => new ServiceAcceptanceRow(a.Id, a.Number, a.Kind, a.OrderId, orders.GetValueOrDefault(a.OrderId, ""), a.PartnerId, partners[a.PartnerId].Code, partners[a.PartnerId].Name,
            a.AcceptanceDate, a.Status, a.Note, a.DecisionNote, a.CreatedAt,
            a.Lines.OrderBy(l => l.OrderLineNo).Select(l => new ServiceAcceptanceLineView(l.OrderLineNo, l.ItemId, items[l.ItemId].Code, items[l.ItemId].Name, items[l.ItemId].Unit, l.Quantity, l.Note)).ToList())).ToList();
    }

    /// <summary>Dòng dịch vụ còn có thể lập hóa đơn (đã nghiệm thu xác nhận trừ đã lập hóa đơn), để điền vào hóa đơn mua / bán.</summary>
    public async Task<IReadOnlyList<ServiceInvoiceCandidate>> InvoiceCandidatesAsync(SessionContext ctx, InvoiceKind kind, Guid? partnerId)
    {
        ctx.Require(Permissions.VoucherView);
        var orders = await db.TradeOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == ctx.CompanyId && o.Kind == kind && o.Status == OrderStatus.Confirmed && (partnerId == null || o.PartnerId == partnerId)).ToListAsync();
        var itemIds = orders.SelectMany(o => o.Lines).Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id) && i.IsService).ToDictionaryAsync(i => i.Id);
        var result = new List<ServiceInvoiceCandidate>();
        foreach (var o in orders)
        {
            var accepted = await CountedAsync(o.Id, [ApprovalStatus.Approved]);
            var invoiced = await InvoicedAsync(o.Id, null);
            foreach (var l in o.Lines.Where(l => items.ContainsKey(l.ItemId)).OrderBy(l => l.LineNo))
            {
                var rem = accepted.GetValueOrDefault(l.LineNo) - invoiced.GetValueOrDefault(l.LineNo);
                if (rem <= 0) continue;
                var it = items[l.ItemId];
                result.Add(new ServiceInvoiceCandidate(o.Id, o.Number, l.LineNo, it.Id, it.Code, it.Name, it.Unit, l.Quantity, accepted.GetValueOrDefault(l.LineNo), invoiced.GetValueOrDefault(l.LineNo), rem,
                    l.UnitPrice, l.VatRate ?? it.DefaultVatRate, kind == InvoiceKind.Sales ? it.RevenueAccountId : it.ExpenseAccountId, l.Description ?? it.Name));
            }
        }
        return result.OrderBy(r => r.OrderNumber, StringComparer.Ordinal).ThenBy(r => r.OrderLineNo).ToList();
    }

    /// <summary>Kiểm soát khi lập hóa đơn dòng dịch vụ gắn đơn: số lượng không vượt phần nghiệm thu đã xác nhận còn lại (không tính hóa đơn <paramref name="exceptInvoiceId"/> đang sửa / ghi sổ).</summary>
    public static async Task CheckInvoiceAgainstAcceptanceAsync(HakDbContext db, Guid orderId, int orderLineNo, string orderNumber, decimal quantity, Guid? exceptInvoiceId)
    {
        var svc = new ServiceAcceptanceService(db, null!, null!);
        var accepted = (await svc.CountedAsync(orderId, [ApprovalStatus.Approved])).GetValueOrDefault(orderLineNo);
        var invoiced = (await svc.InvoicedAsync(orderId, exceptInvoiceId)).GetValueOrDefault(orderLineNo);
        if (invoiced + quantity > accepted)
            throw new DomainException(ErrorCodes.OverInvoiced, $"Dòng {orderLineNo} của đơn {orderNumber}: hóa đơn dịch vụ đạt {invoiced + quantity} vượt phần đã nghiệm thu xác nhận ({accepted}); lập và xác nhận biên bản nghiệm thu trước.");
    }

    private async Task<Dictionary<int, decimal>> CountedAsync(Guid orderId, ApprovalStatus[] statuses)
        => (await (from l in db.ServiceAcceptanceLines join a in db.ServiceAcceptances on l.AcceptanceId equals a.Id where a.OrderId == orderId && statuses.Contains(a.Status) select new { l.OrderLineNo, l.Quantity }).ToListAsync())
            .GroupBy(x => x.OrderLineNo).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

    private async Task<Dictionary<int, decimal>> InvoicedAsync(Guid orderId, Guid? exceptInvoiceId)
        => (await (from l in db.CommercialInvoiceLines join i in db.CommercialInvoices on l.InvoiceId equals i.Id
                   where l.OrderId == orderId && l.StockDocumentId == null && i.Purpose == InvoicePurpose.Normal && i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Reversed && (exceptInvoiceId == null || i.Id != exceptInvoiceId)
                   select new { l.OrderLineNo, l.BasisQuantity, i.Purpose }).ToListAsync())
            .GroupBy(x => x.OrderLineNo ?? 0).ToDictionary(g => g.Key, g => g.Sum(x => x.BasisQuantity));
}
