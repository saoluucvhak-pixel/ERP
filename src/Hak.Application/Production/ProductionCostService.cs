using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Inventory;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Organization;
using Hak.Domain.Production;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Production;

public sealed record CostObjectInput(string Code, string Name, Guid ItemId, Guid WipAccountId, decimal PlannedUnitCost);

public sealed record ProductionReceiptLine(Guid DocumentId, string DocumentNumber, int LineNo, DateOnly Date, decimal Quantity, decimal Value);

/// <summary>What a run would book, shown before posting.</summary>
public sealed record ProductionCostPreview(Guid CostObjectId, DateOnly PeriodStart, DateOnly PeriodEnd, decimal WipBalanceBefore, decimal ProvisionalValue,
    decimal ClosingWip, decimal OutputQuantity, decimal ActualCost, decimal UnitCost, decimal Difference, IReadOnlyList<ProductionReceiptLine> Receipts);

/// <summary>
/// Giá thành sản xuất (master prompt: COST). Costs reach the cost object's WIP account through the existing flows (material
/// issues against WIP, payroll / overhead moved by closing rules). Finished goods are received at the planned unit cost. The
/// period-end run computes actual cost = WIP balance at period end + provisional value of the period's receipts − closing WIP
/// (entered by the user: dở dang cuối kỳ, valuation method [CẦN XÁC MINH]) and spreads the difference to the provisional value
/// over the receipts by quantity; each receipt's share follows the confirmed price-difference policy (still in stock →
/// inventory value, already issued → COGS). WIP then equals the closing WIP. Runs reverse latest first.
/// </summary>
public sealed class ProductionCostService(HakDbContext db, AccountingEngine engine, StockService stock, AuditService audit, IClock clock)
{
    public const string ProductionSource = "HAK.PRODUCTION";

    public async Task<CostObject> CreateCostObjectAsync(SessionContext ctx, CostObjectInput input)
    {
        ctx.Require(Permissions.CatalogManage);
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Name)) throw new DomainException(ErrorCodes.ValidationFailed, "Mã và tên đối tượng tính giá thành bắt buộc.");
        if (input.PlannedUnitCost < 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Giá thành kế hoạch không được âm.");
        return await db.InTransactionAsync(async () =>
        {
            var code = input.Code.Trim();
            if (await db.CostObjects.AnyAsync(o => o.CompanyId == ctx.CompanyId && o.Code == code))
                throw new DomainException(ErrorCodes.DuplicateCode, $"Đối tượng {code} đã tồn tại.");
            if (!await db.Items.AnyAsync(i => i.Id == input.ItemId && i.CompanyId == ctx.CompanyId))
                throw new DomainException(ErrorCodes.NotFound, "Thành phẩm không tồn tại.");
            var wip = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == input.WipAccountId && a.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.AccountNotFound, "TK chi phí dở dang không tồn tại.");
            if (!wip.IsPostable || wip.CashKind != CashKind.None || wip.OpenItemNature != OpenItemNature.None)
                throw new DomainException(ErrorCodes.ValidationFailed, $"TK {wip.Code} không dùng làm TK chi phí sản xuất dở dang.");
            if (await db.CostObjects.AnyAsync(o => o.CompanyId == ctx.CompanyId && o.WipAccountId == wip.Id))
                throw new DomainException(ErrorCodes.ValidationFailed, $"TK {wip.Code} đã dùng cho đối tượng tính giá thành khác; mỗi đối tượng một TK dở dang chi tiết.");
            var o = new CostObject
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, Code = code, Name = input.Name.Trim(), ItemId = input.ItemId, WipAccountId = wip.Id,
                PlannedUnitCost = input.PlannedUnitCost, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId,
            };
            db.CostObjects.Add(o);
            await audit.AppendAsync(ctx.CompanyId, ctx, "cost_object.create", "cost_object", o.Id.ToString(), input);
            await db.SaveChangesAsync();
            return o;
        });
    }

    public async Task<IReadOnlyList<CostObject>> CostObjectsAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        return await db.CostObjects.AsNoTracking().Where(o => o.CompanyId == ctx.CompanyId).OrderBy(o => o.Code).ToListAsync();
    }

    public async Task<ProductionCostPreview> PreviewAsync(SessionContext ctx, Guid costObjectId, Guid periodId, decimal closingWip)
    {
        ctx.Require(Permissions.ReportView);
        var (obj, period) = await LoadAsync(ctx, costObjectId, periodId);
        return await ComputeAsync(ctx.CompanyId, obj, period, closingWip);
    }

    public async Task<ProductionCostRun> RunAsync(SessionContext ctx, Guid costObjectId, Guid periodId, decimal closingWip, string note)
    {
        ctx.Require(Permissions.VoucherPost);
        if (closingWip < 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Chi phí dở dang cuối kỳ không được âm.");
        return await db.InTransactionAsync(async () =>
        {
            var (obj, period) = await LoadAsync(ctx, costObjectId, periodId);
            if (period.Status == PeriodStatus.Locked) throw new DomainException(ErrorCodes.PeriodLocked, $"Kỳ {period.StartDate:MM/yyyy} đã khóa sổ.");
            if (await db.ProductionCostRuns.AnyAsync(r => r.CostObjectId == obj.Id && r.Status == ProductionRunStatus.Active && r.PeriodEnd >= period.StartDate))
                throw new DomainException(ErrorCodes.PeriodCosted, "Đối tượng đã tính giá thành kỳ này hoặc kỳ sau; hủy từ kỳ gần nhất nếu cần tính lại.");
            var p = await ComputeAsync(ctx.CompanyId, obj, period, closingWip);
            if (p.OutputQuantity == 0 && p.Difference != 0)
                throw new DomainException(ErrorCodes.ValidationFailed,
                    $"Kỳ không có thành phẩm nhập kho; toàn bộ chi phí {p.WipBalanceBefore} là dở dang cuối kỳ (nhập đúng số này) hoặc kiểm tra phiếu nhập thành phẩm.");

            var run = new ProductionCostRun
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, CostObjectId = obj.Id, PeriodId = period.Id, PeriodStart = period.StartDate, PeriodEnd = period.EndDate,
                WipBalanceBefore = p.WipBalanceBefore, ProvisionalValue = p.ProvisionalValue, ClosingWip = closingWip, OutputQuantity = p.OutputQuantity,
                ActualCost = p.ActualCost, UnitCost = p.UnitCost, Difference = p.Difference, Note = note?.Trim() ?? "", CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId,
            };
            db.ProductionCostRuns.Add(run);
            if (p.Difference != 0)
            {
                var roles = await db.AccountRoleMappings.AsNoTracking().Where(m => m.CompanyId == ctx.CompanyId && m.RoleCode == "COGS").Select(m => (Guid?)m.AccountId).FirstOrDefaultAsync();
                var item = await db.Items.AsNoTracking().FirstAsync(i => i.Id == obj.ItemId);
                var shares = Spread(p.Difference, p.Receipts.Select(r => r.Quantity).ToList(), await DecimalsAsync(ctx.CompanyId));
                decimal inStock = 0, issued = 0;
                for (var i = 0; i < p.Receipts.Count; i++)
                {
                    if (shares[i] == 0) continue;
                    var split = await stock.AdjustReceiptValueAsync(ctx, p.Receipts[i].DocumentId, p.Receipts[i].LineNo, shares[i], period.EndDate, run.Id,
                        $"Giá thành thực tế {obj.Code} {period.StartDate:MM/yyyy}");
                    inStock += split.InStockPart;
                    issued += split.IssuedPart;
                }
                var label = $"Giá thành {obj.Code} {period.StartDate:MM/yyyy}";
                var gl = new List<VoucherLineInput>();
                if (inStock != 0) gl.Add(new VoucherLineInput(item.InventoryAccountId, inStock > 0 ? Side.Debit : Side.Credit, Math.Abs(inStock), Description: $"{label} – thành phẩm còn tồn"));
                if (issued != 0)
                    gl.Add(new VoucherLineInput(roles ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, "Chưa ánh xạ vai trò tài khoản COGS."),
                        issued > 0 ? Side.Debit : Side.Credit, Math.Abs(issued), Description: $"{label} – thành phẩm đã xuất"));
                gl.Add(new VoucherLineInput(obj.WipAccountId, p.Difference > 0 ? Side.Credit : Side.Debit, Math.Abs(p.Difference), Description: $"{label} – kết chuyển chênh lệch so với giá kế hoạch"));
                var v = await engine.PostSystemVoucherAsync(ctx, new VoucherInput(VoucherTypes.InventoryCosting, period.EndDate, period.EndDate,
                    $"Tính giá thành {obj.Code} kỳ {period.StartDate:MM/yyyy}: {p.OutputQuantity} × {p.UnitCost}", gl, SourceSystem: ProductionSource, SourceId: run.Id.ToString()));
                run.VoucherId = v.Id;
            }
            await audit.AppendAsync(ctx.CompanyId, ctx, "production.cost", "production_cost_run", run.Id.ToString(),
                new { obj.Code, period = period.StartDate, p.WipBalanceBefore, p.ProvisionalValue, closingWip, p.OutputQuantity, p.ActualCost, p.UnitCost, p.Difference });
            await db.SaveChangesAsync();
            return run;
        });
    }

    public async Task ReverseAsync(SessionContext ctx, Guid runId, string reason)
    {
        ctx.Require(Permissions.VoucherReverse);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy tính giá thành.");
        await db.InTransactionAsync(async () =>
        {
            var run = await db.ProductionCostRuns.FirstOrDefaultAsync(r => r.Id == runId && r.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Lần tính giá thành không tồn tại.");
            if (run.Status != ProductionRunStatus.Active) throw new DomainException(ErrorCodes.InvalidStateTransition, "Lần tính giá thành đã bị hủy.");
            if (await db.ProductionCostRuns.AnyAsync(r => r.CostObjectId == run.CostObjectId && r.Status == ProductionRunStatus.Active && r.PeriodStart > run.PeriodStart))
                throw new DomainException(ErrorCodes.PeriodCosted, "Kỳ sau đã tính giá thành; hủy từ kỳ gần nhất trở về trước.");
            if (run.VoucherId is { } vid) await engine.ReverseCoreAsync(ctx, vid, run.PeriodEnd, $"Hủy tính giá thành: {reason}", allowModuleOwned: true);
            await stock.ReverseValueAdjustmentsAsync(ctx, run.Id, run.PeriodEnd, $"Hủy tính giá thành: {reason}");
            run.Status = ProductionRunStatus.Reversed;
            run.ReversedAt = clock.UtcNow;
            run.ReversedBy = ctx.UserId;
            await audit.AppendAsync(ctx.CompanyId, ctx, "production.cost_reverse", "production_cost_run", run.Id.ToString(), new { reason });
            await db.SaveChangesAsync();
        });
    }

    public async Task<IReadOnlyList<ProductionCostRun>> RunsAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        return await db.ProductionCostRuns.AsNoTracking().Where(r => r.CompanyId == ctx.CompanyId)
            .OrderByDescending(r => r.PeriodStart).ThenByDescending(r => r.CreatedAt).ToListAsync();
    }

    // ------------------------------------------------------------------ helpers

    private async Task<ProductionCostPreview> ComputeAsync(Guid companyId, CostObject obj, AccountingPeriod period, decimal closingWip)
    {
        var receipts = await (from m in db.StockMovements
                              join d in db.StockDocuments on m.DocumentId equals d.Id
                              where m.CompanyId == companyId && d.Type == StockDocumentType.Receipt && d.Status == StockDocumentStatus.Posted
                                    && m.Quantity > 0 && m.ReversesMovementId == null && m.CounterAccountId == obj.WipAccountId && m.ItemId == obj.ItemId
                                    && m.MovementDate >= period.StartDate && m.MovementDate <= period.EndDate
                              orderby m.Sequence
                              select new { m.Id, m.DocumentId, d.Number, m.LineNo, m.MovementDate, m.Quantity }).AsNoTracking().ToListAsync();
        var ids = receipts.Select(r => r.Id).ToList();
        var valuations = await db.StockValuations.AsNoTracking().Where(v => ids.Contains(v.MovementId) && v.VoidedAt == null).ToDictionaryAsync(v => v.MovementId, v => v.Value);
        var adjustments = (await db.StockValueAdjustments.AsNoTracking().Where(a => ids.Contains(a.TargetMovementId) && a.AdjustmentDate <= period.EndDate)
            .Select(a => new { a.TargetMovementId, a.Value }).ToListAsync()).GroupBy(a => a.TargetMovementId).ToDictionary(g => g.Key, g => g.Sum(a => a.Value));
        var lines = receipts.Select(r => new ProductionReceiptLine(r.DocumentId, r.Number, r.LineNo, r.MovementDate, r.Quantity,
            valuations.GetValueOrDefault(r.Id) + adjustments.GetValueOrDefault(r.Id))).ToList();

        var wip = await db.JournalLines.AsNoTracking().Where(l => l.CompanyId == companyId && l.AccountId == obj.WipAccountId && l.PostingDate <= period.EndDate)
            .GroupBy(l => l.Side).Select(g => new { g.Key, Total = g.Sum(x => x.Amount) }).ToListAsync();
        var balance = wip.Sum(x => x.Key == Side.Debit ? x.Total : -x.Total);
        var provisional = lines.Sum(l => l.Value);
        var quantity = lines.Sum(l => l.Quantity);
        var actual = balance + provisional - closingWip;
        if (actual < 0) throw new DomainException(ErrorCodes.ValidationFailed, $"Dở dang cuối kỳ {closingWip} lớn hơn tổng chi phí sản xuất {balance + provisional}.");
        var dp = await DecimalsAsync(companyId);
        return new ProductionCostPreview(obj.Id, period.StartDate, period.EndDate, balance, provisional, closingWip, quantity, actual,
            quantity == 0 ? 0 : MoneyMath.Round(actual / quantity, dp + 2), actual - provisional, lines);
    }

    /// <summary>Spreads <paramref name="total"/> by quantity with the largest-remainder method, so the shares add up exactly.</summary>
    public static decimal[] Spread(decimal total, IReadOnlyList<decimal> quantities, int decimals)
    {
        var sum = quantities.Sum();
        var shares = quantities.Select(q => sum == 0 ? 0 : Math.Truncate(total * q / sum * Pow(decimals)) / Pow(decimals)).ToArray();
        var rest = total - shares.Sum();
        var unit = 1m / Pow(decimals) * Math.Sign(rest);
        var order = quantities.Select((q, i) => (i, frac: sum == 0 ? 0 : Math.Abs(total * q / sum - shares[i]))).OrderByDescending(x => x.frac).Select(x => x.i).ToList();
        for (var k = 0; rest != 0 && order.Count > 0; k = (k + 1) % order.Count) { shares[order[k]] += unit; rest -= unit; }
        return shares;
    }

    private static decimal Pow(int decimals) { decimal p = 1; for (var i = 0; i < decimals; i++) p *= 10; return p; }

    private async Task<(CostObject, AccountingPeriod)> LoadAsync(SessionContext ctx, Guid costObjectId, Guid periodId)
    {
        var obj = await db.CostObjects.AsNoTracking().FirstOrDefaultAsync(o => o.Id == costObjectId && o.CompanyId == ctx.CompanyId)
                  ?? throw new DomainException(ErrorCodes.NotFound, "Đối tượng tính giá thành không tồn tại.");
        var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == ctx.CompanyId)
                     ?? throw new DomainException(ErrorCodes.PeriodNotFound, "Kỳ kế toán không tồn tại.");
        return (obj, period);
    }

    private async Task<int> DecimalsAsync(Guid companyId)
    {
        var currency = await db.Companies.Where(c => c.Id == companyId).Select(c => c.FunctionalCurrency).FirstAsync();
        return await db.Currencies.Where(c => c.Code == currency).Select(c => c.DecimalPlaces).FirstAsync();
    }
}
