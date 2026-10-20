using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record MilestoneInput(string Name, decimal Percentage, DateOnly DueDate);
public sealed record MilestoneRow(Guid Id, Guid OrderId, string OrderNumber, int OrderLineNo, int MilestoneNo, string Name, decimal Percentage, decimal Quantity, string Unit, string ItemCode, DateOnly DueDate,
    Guid? AcceptanceId, string? AcceptanceNumber, ApprovalStatus? AcceptanceStatus, string Status);
public sealed record TimeEntryRow(Guid Id, Guid OrderId, string OrderNumber, int OrderLineNo, string ItemCode, DateOnly WorkDate, string Worker, decimal Hours, string? Note, Guid? AcceptanceId, string? AcceptanceNumber, bool Locked);

/// <summary>
/// Dịch vụ theo mốc và theo giờ công (Odoo: milestone / timesheet; ERPNext: Timesheet → hóa đơn; SAP: billing plan / service entry sheet). Cả hai chỉ là cách ghi nhận số lượng đã thực hiện
/// để lập biên bản nghiệm thu dịch vụ — hóa đơn vẫn chỉ lập trong phạm vi nghiệm thu đã xác nhận. Mốc: tỷ lệ % số lượng dòng đơn, hoàn thành mốc lập biên bản; chấm giờ: cộng giờ đến một ngày,
/// đổi ra số lượng theo số giờ mỗi đơn vị tính. Biên bản do hệ thống lập vẫn do người khác xác nhận.
/// </summary>
public sealed class ServiceProgressService(HakDbContext db, ServiceAcceptanceService acceptances, AuditService audit, IClock clock)
{
    // ------------------------------------------------------------------ mốc

    public async Task SetMilestonesAsync(SessionContext ctx, Guid orderId, int orderLineNo, IReadOnlyList<MilestoneInput> milestones)
    {
        ctx.Require(Permissions.InvoiceDocument);
        await db.InTransactionAsync(async () =>
        {
            var (order, _) = await ServiceLineAsync(ctx, orderId, orderLineNo);
            if (milestones.Any(m => string.IsNullOrWhiteSpace(m.Name))) throw new DomainException(ErrorCodes.ValidationFailed, "Tên mốc bắt buộc.");
            if (milestones.Any(m => m.Percentage <= 0 || m.Percentage > 100 || !MoneyMath.HasAtMostDecimals(m.Percentage, MoneyMath.StorageScale))) throw new DomainException(ErrorCodes.ValidationFailed, "Tỷ lệ mỗi mốc phải lớn hơn 0 và không quá 100%.");
            if (milestones.Sum(m => m.Percentage) > 100) throw new DomainException(ErrorCodes.ValidationFailed, $"Tổng tỷ lệ các mốc ({milestones.Sum(m => m.Percentage)}%) vượt 100%.");
            var existing = await db.ServiceMilestones.Where(m => m.OrderId == orderId && m.OrderLineNo == orderLineNo).ToListAsync();
            if (existing.Any(m => m.AcceptanceId != null))
                throw new DomainException(ErrorCodes.InvalidStateTransition, "Đã có mốc được hoàn thành (lập biên bản nghiệm thu): không sửa lại danh sách mốc.");
            db.ServiceMilestones.RemoveRange(existing);
            var n = 0;
            foreach (var m in milestones.OrderBy(x => x.DueDate))
                db.ServiceMilestones.Add(new ServiceMilestone { Id = Ids.New(), CompanyId = ctx.CompanyId, OrderId = orderId, OrderLineNo = orderLineNo, MilestoneNo = ++n, Name = m.Name.Trim(), Percentage = m.Percentage, DueDate = m.DueDate });
            await audit.AppendAsync(ctx.CompanyId, ctx, "service_milestones.set", "order", orderId.ToString(), new { order = order.Number, line = orderLineNo, count = milestones.Count });
            await db.SaveChangesAsync();
        });
    }

    public async Task<IReadOnlyList<MilestoneRow>> MilestonesAsync(SessionContext ctx, Guid? orderId, DateOnly? today = null)
    {
        ctx.Require(Permissions.VoucherView);
        var day = today ?? DateOnly.FromDateTime(clock.UtcNow);
        var ms = await db.ServiceMilestones.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && (orderId == null || m.OrderId == orderId)).ToListAsync();
        var orders = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.CompanyId == ctx.CompanyId).ToDictionaryAsync(o => o.Id);
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id);
        var accs = await db.ServiceAcceptances.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId).ToDictionaryAsync(a => a.Id);
        return ms.OrderBy(m => orders[m.OrderId].Number, StringComparer.Ordinal).ThenBy(m => m.OrderLineNo).ThenBy(m => m.MilestoneNo).Select(m =>
        {
            var o = orders[m.OrderId]; var ol = o.Lines.First(l => l.LineNo == m.OrderLineNo); var it = items[ol.ItemId];
            accs.TryGetValue(m.AcceptanceId ?? Guid.Empty, out var a);
            var live = a is { Status: ApprovalStatus.Requested or ApprovalStatus.Approved };
            var status = !live ? (m.DueDate < day ? "OVERDUE" : "PENDING") : a!.Status == ApprovalStatus.Approved ? "DONE" : "WAITING";
            return new MilestoneRow(m.Id, o.Id, o.Number, m.OrderLineNo, m.MilestoneNo, m.Name, m.Percentage, Math.Round(ol.Quantity * m.Percentage / 100m, MoneyMath.StorageScale, MidpointRounding.AwayFromZero), it.Unit, it.Code,
                m.DueDate, a?.Id, a?.Number, a?.Status, status);
        }).ToList();
    }

    /// <summary>Hoàn thành mốc: lập biên bản nghiệm thu (chờ người khác xác nhận) với số lượng = tỷ lệ × số lượng đặt.</summary>
    public async Task<ServiceAcceptance> CompleteMilestoneAsync(SessionContext ctx, Guid milestoneId, DateOnly date)
    {
        ctx.Require(Permissions.InvoiceDocument);
        return await db.InTransactionAsync(async () =>
        {
            var m = await db.ServiceMilestones.FirstOrDefaultAsync(x => x.Id == milestoneId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Mốc không tồn tại.");
            if (m.AcceptanceId is { } aid && await db.ServiceAcceptances.AnyAsync(a => a.Id == aid && (a.Status == ApprovalStatus.Requested || a.Status == ApprovalStatus.Approved)))
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Mốc \"{m.Name}\" đã hoàn thành.");
            var (_, line) = await ServiceLineAsync(ctx, m.OrderId, m.OrderLineNo);
            var qty = Math.Round(line.Quantity * m.Percentage / 100m, MoneyMath.StorageScale, MidpointRounding.AwayFromZero);
            var acc = await acceptances.CreateAsync(ctx, new ServiceAcceptanceInput(m.OrderId, date, [new ServiceAcceptanceLineInput(m.OrderLineNo, qty, m.Name)], $"Mốc {m.MilestoneNo}: {m.Name}"));
            m.AcceptanceId = acc.Id;
            await audit.AppendAsync(ctx.CompanyId, ctx, "service_milestone.complete", "service_milestone", m.Id.ToString(), new { m.Name, acceptance = acc.Number, qty });
            await db.SaveChangesAsync();
            return acc;
        });
    }

    // ------------------------------------------------------------------ chấm giờ

    public async Task<ServiceTimeEntry> AddTimeAsync(SessionContext ctx, Guid orderId, int orderLineNo, DateOnly workDate, string worker, decimal hours, string? note)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (string.IsNullOrWhiteSpace(worker)) throw new DomainException(ErrorCodes.ValidationFailed, "Người thực hiện bắt buộc.");
        if (hours <= 0 || hours > 24 || !MoneyMath.HasAtMostDecimals(hours, 2)) throw new DomainException(ErrorCodes.AmountNotPositive, "Số giờ phải lớn hơn 0, không quá 24 giờ một dòng, tối đa 2 chữ số thập phân.");
        return await db.InTransactionAsync(async () =>
        {
            await ServiceLineAsync(ctx, orderId, orderLineNo);
            var e = new ServiceTimeEntry { Id = Ids.New(), CompanyId = ctx.CompanyId, OrderId = orderId, OrderLineNo = orderLineNo, WorkDate = workDate, Worker = worker.Trim(), Hours = hours,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), CreatedBy = ctx.UserId, CreatedAt = clock.UtcNow };
            db.ServiceTimeEntries.Add(e);
            await audit.AppendAsync(ctx.CompanyId, ctx, "service_time.add", "service_time_entry", e.Id.ToString(), new { orderId, orderLineNo, workDate, worker = e.Worker, hours });
            await db.SaveChangesAsync();
            return e;
        });
    }

    public async Task DeleteTimeAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.InvoiceDocument);
        await db.InTransactionAsync(async () =>
        {
            var e = await db.ServiceTimeEntries.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Dòng chấm giờ không tồn tại.");
            if (await LockedAsync(e)) throw new DomainException(ErrorCodes.InvalidStateTransition, "Dòng chấm giờ đã chốt vào biên bản nghiệm thu còn hiệu lực: hủy biên bản trước.");
            db.ServiceTimeEntries.Remove(e);
            await audit.AppendAsync(ctx.CompanyId, ctx, "service_time.delete", "service_time_entry", e.Id.ToString(), new { e.OrderId, e.OrderLineNo, e.WorkDate, e.Worker, e.Hours });
            await db.SaveChangesAsync();
        });
    }

    public async Task<IReadOnlyList<TimeEntryRow>> TimeEntriesAsync(SessionContext ctx, Guid? orderId, bool openOnly)
    {
        ctx.Require(Permissions.VoucherView);
        var es = await db.ServiceTimeEntries.AsNoTracking().Where(e => e.CompanyId == ctx.CompanyId && (orderId == null || e.OrderId == orderId)).OrderByDescending(e => e.WorkDate).ThenBy(e => e.CreatedAt).Take(1000).ToListAsync();
        var orders = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.CompanyId == ctx.CompanyId).ToDictionaryAsync(o => o.Id);
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id);
        var accs = await db.ServiceAcceptances.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId).ToDictionaryAsync(a => a.Id);
        var rows = es.Select(e =>
        {
            accs.TryGetValue(e.AcceptanceId ?? Guid.Empty, out var a);
            var locked = a is { Status: ApprovalStatus.Requested or ApprovalStatus.Approved };
            return new TimeEntryRow(e.Id, e.OrderId, orders[e.OrderId].Number, e.OrderLineNo, items[orders[e.OrderId].Lines.First(l => l.LineNo == e.OrderLineNo).ItemId].Code, e.WorkDate, e.Worker, e.Hours, e.Note, a?.Id, a?.Number, locked);
        });
        return (openOnly ? rows.Where(r => !r.Locked) : rows).ToList();
    }

    /// <summary>Chốt chấm giờ: cộng giờ chưa chốt của dòng đơn đến hết <paramref name="through"/>, quy đổi ra số lượng (giờ ÷ số giờ mỗi đơn vị tính) và lập biên bản nghiệm thu.</summary>
    public async Task<ServiceAcceptance> CloseTimesheetAsync(SessionContext ctx, Guid orderId, int orderLineNo, DateOnly through, decimal hoursPerUnit, DateOnly acceptanceDate)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (hoursPerUnit <= 0 || !MoneyMath.HasAtMostDecimals(hoursPerUnit, 2)) throw new DomainException(ErrorCodes.ValidationFailed, "Số giờ mỗi đơn vị tính phải lớn hơn 0 (ví dụ 1 giờ, 8 giờ = 1 ngày công).");
        return await db.InTransactionAsync(async () =>
        {
            await ServiceLineAsync(ctx, orderId, orderLineNo);
            var es = (await db.ServiceTimeEntries.Where(e => e.CompanyId == ctx.CompanyId && e.OrderId == orderId && e.OrderLineNo == orderLineNo && e.WorkDate <= through).ToListAsync());
            var open = new List<ServiceTimeEntry>();
            foreach (var e in es) if (!await LockedAsync(e)) open.Add(e);
            if (open.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Không có giờ nào chưa chốt đến ngày này.");
            var hours = open.Sum(e => e.Hours);
            var qty = Math.Round(hours / hoursPerUnit, MoneyMath.StorageScale, MidpointRounding.AwayFromZero);
            if (qty <= 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Số lượng quy đổi bằng 0; giảm số giờ mỗi đơn vị tính.");
            var acc = await acceptances.CreateAsync(ctx, new ServiceAcceptanceInput(orderId, acceptanceDate, [new ServiceAcceptanceLineInput(orderLineNo, qty, $"{hours} giờ")], $"Chốt chấm giờ đến {through.Vn()}: {hours} giờ ÷ {hoursPerUnit} = {qty}"));
            foreach (var e in open) e.AcceptanceId = acc.Id;
            await audit.AppendAsync(ctx.CompanyId, ctx, "service_time.close", "service_acceptance", acc.Id.ToString(), new { acc.Number, hours, hoursPerUnit, qty, entries = open.Count });
            await db.SaveChangesAsync();
            return acc;
        });
    }

    private async Task<bool> LockedAsync(ServiceTimeEntry e)
        => e.AcceptanceId is { } id && await db.ServiceAcceptances.AnyAsync(a => a.Id == id && (a.Status == ApprovalStatus.Requested || a.Status == ApprovalStatus.Approved));

    private async Task<(TradeOrder Order, TradeOrderLine Line)> ServiceLineAsync(SessionContext ctx, Guid orderId, int orderLineNo)
    {
        var order = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Đơn hàng không tồn tại.");
        if (order.Status != OrderStatus.Confirmed) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Đơn {order.Number} phải đã xác nhận.");
        var line = order.Lines.FirstOrDefault(l => l.LineNo == orderLineNo) ?? throw new DomainException(ErrorCodes.NotFound, $"Đơn {order.Number} không có dòng {orderLineNo}.");
        if (!await db.Items.AnyAsync(i => i.Id == line.ItemId && i.IsService)) throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {orderLineNo} của đơn {order.Number} không phải dịch vụ.");
        return (order, line);
    }
}
