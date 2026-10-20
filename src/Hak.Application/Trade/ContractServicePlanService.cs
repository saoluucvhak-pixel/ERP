using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record AcceptancePlanInput(int ContractLineNo, string Name, DateOnly PlannedDate, decimal PlannedQty);
public sealed record AcceptancePlanRow(Guid Id, int ContractLineNo, int PlanNo, string Name, Guid ItemId, string ItemCode, string ItemName, string Unit, DateOnly PlannedDate, decimal PlannedQty,
    decimal CumulativePlanned, decimal Accepted, string Status);
public sealed record AcceptancePlanOverdue(Guid ContractId, string ContractCode, InvoiceKind Kind, string PartnerName, string Name, string ItemCode, DateOnly PlannedDate, int DaysLate, decimal Missing);

/// <summary>
/// Hợp đồng dịch vụ có lịch nghiệm thu (vận chuyển thuê, tư vấn…): khai các đợt nghiệm thu dự kiến theo dòng dịch vụ của hợp đồng, đối chiếu kế hoạch – thực tế theo lũy kế với biên bản nghiệm thu
/// đã xác nhận của các đơn kế thừa hợp đồng, thông báo đợt quá hạn. Tổng số lượng dự kiến mỗi dòng không vượt số lượng cam kết. Sửa lịch có nhật ký kiểm toán.
/// </summary>
public sealed class ContractServicePlanService(HakDbContext db, AuditService audit, IClock clock)
{
    /// <summary>Thay toàn bộ lịch của hợp đồng (hợp đồng chưa thanh lý / hủy).</summary>
    public async Task SetPlanAsync(SessionContext ctx, Guid contractId, IReadOnlyList<AcceptancePlanInput> plan)
    {
        ctx.Require(Permissions.InvoiceDocument);
        await db.InTransactionAsync(async () =>
        {
            var c = await db.Contracts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == contractId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Hợp đồng không tồn tại.");
            if (c.Status is ContractStatus.Completed or ContractStatus.Cancelled) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hợp đồng {c.Code} đã {(c.Status == ContractStatus.Completed ? "thanh lý" : "hủy")}.");
            var serviceItems = (await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId && i.IsService).Select(i => i.Id).ToListAsync()).ToHashSet();
            foreach (var g in plan.GroupBy(p => p.ContractLineNo))
            {
                var line = c.Lines.FirstOrDefault(l => l.LineNo == g.Key) ?? throw new DomainException(ErrorCodes.NotFound, $"Hợp đồng {c.Code} không có dòng {g.Key}.");
                if (!serviceItems.Contains(line.ItemId)) throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {g.Key} của hợp đồng {c.Code} không phải dịch vụ.");
                if (g.Sum(p => p.PlannedQty) > line.CommittedQty) throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {g.Key}: tổng số lượng các đợt ({g.Sum(p => p.PlannedQty)}) vượt số lượng cam kết ({line.CommittedQty}).");
            }
            foreach (var p in plan)
            {
                if (string.IsNullOrWhiteSpace(p.Name)) throw new DomainException(ErrorCodes.ValidationFailed, "Tên đợt nghiệm thu bắt buộc.");
                if (p.PlannedQty <= 0 || !MoneyMath.HasAtMostDecimals(p.PlannedQty, MoneyMath.StorageScale)) throw new DomainException(ErrorCodes.AmountNotPositive, $"Đợt \"{p.Name}\": số lượng phải lớn hơn 0, tối đa 4 chữ số thập phân.");
            }
            db.ContractAcceptancePlans.RemoveRange(db.ContractAcceptancePlans.Where(x => x.ContractId == contractId));
            foreach (var g in plan.GroupBy(p => p.ContractLineNo))
            {
                var n = 0;
                foreach (var p in g.OrderBy(x => x.PlannedDate))
                    db.ContractAcceptancePlans.Add(new ContractAcceptancePlan { Id = Ids.New(), ContractId = contractId, CompanyId = ctx.CompanyId, ContractLineNo = g.Key, PlanNo = ++n, Name = p.Name.Trim(), PlannedDate = p.PlannedDate, PlannedQty = p.PlannedQty });
            }
            await audit.AppendAsync(ctx.CompanyId, ctx, "contract.acceptance_plan", "contract", c.Id.ToString(), new { c.Code, steps = plan.Count });
            await db.SaveChangesAsync();
        });
    }

    public async Task<IReadOnlyList<AcceptancePlanRow>> PlanAsync(SessionContext ctx, Guid contractId, DateOnly? today = null)
    {
        ctx.Require(Permissions.VoucherView);
        var day = today ?? DateOnly.FromDateTime(clock.UtcNow);
        var c = await db.Contracts.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == contractId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Hợp đồng không tồn tại.");
        var plans = await db.ContractAcceptancePlans.AsNoTracking().Where(p => p.ContractId == contractId).ToListAsync();
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id);
        var accepted = (await (from l in db.ServiceAcceptanceLines join a in db.ServiceAcceptances on l.AcceptanceId equals a.Id join o in db.TradeOrders on a.OrderId equals o.Id
                               where o.ContractId == contractId && a.Status == ApprovalStatus.Approved select new { l.ItemId, l.Quantity }).ToListAsync())
            .GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var rows = new List<AcceptancePlanRow>();
        foreach (var g in plans.GroupBy(p => p.ContractLineNo).OrderBy(g => g.Key))
        {
            var line = c.Lines.First(l => l.LineNo == g.Key);
            var item = items[line.ItemId];
            var have = accepted.GetValueOrDefault(line.ItemId);
            decimal cum = 0;
            foreach (var p in g.OrderBy(x => x.PlanNo))
            {
                cum += p.PlannedQty;
                var status = have >= cum ? "DONE" : p.PlannedDate < day ? "OVERDUE" : have > cum - p.PlannedQty ? "PARTIAL" : "PENDING";
                rows.Add(new AcceptancePlanRow(p.Id, g.Key, p.PlanNo, p.Name, item.Id, item.Code, item.Name, item.Unit, p.PlannedDate, p.PlannedQty, cum, have, status));
            }
        }
        return rows;
    }

    /// <summary>Các đợt nghiệm thu quá hạn của hợp đồng đang hiệu lực, đợt trễ lâu nhất trước.</summary>
    public async Task<IReadOnlyList<AcceptancePlanOverdue>> OverdueAsync(SessionContext ctx, InvoiceKind? kind, DateOnly? today = null)
    {
        ctx.Require(Permissions.ReportView);
        var day = today ?? DateOnly.FromDateTime(clock.UtcNow);
        var contracts = await db.Contracts.AsNoTracking().Where(c => c.CompanyId == ctx.CompanyId && c.Status == ContractStatus.Active && (kind == null || c.Kind == kind)).ToListAsync();
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id, p => p.Name);
        var result = new List<AcceptancePlanOverdue>();
        foreach (var c in contracts)
            foreach (var r in (await PlanAsync(ctx, c.Id, day)).Where(r => r.Status == "OVERDUE"))
                result.Add(new AcceptancePlanOverdue(c.Id, c.Code, c.Kind, partners.GetValueOrDefault(c.PartnerId, ""), r.Name, r.ItemCode, r.PlannedDate, day.DayNumber - r.PlannedDate.DayNumber, r.CumulativePlanned - r.Accepted));
        return result.OrderByDescending(r => r.DaysLate).ToList();
    }
}
