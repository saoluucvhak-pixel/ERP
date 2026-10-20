using Hak.Application.Accounting;
using Hak.Application.Inventory;
using Hak.Application.Production;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Production;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Production costing: logs chipped into wood chips, finished goods received at planned cost, period-end actual cost.</summary>
public class ProductionCostTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;
    private static DateOnly D(int m, int d) => new(2026, m, d);

    private sealed record Fx(TestHost H, Guid Wh, Guid Log, Guid Chip, Guid Object, Guid March);

    private static async Task<Fx> SetupAsync(Scope s, TestHost h)
    {
        foreach (var (code, name) in new[] { ("RAW", "Nguyên liệu gỗ"), ("FG", "Thành phẩm dăm"), ("WIP", "Chi phí SX dở dang – dăm"), ("COGS", "Giá vốn"), ("LABOUR", "Phải trả lương SX") })
            h.Acc[code] = (await s.Setup.CreateAccountAsync(h.Admin, code, name + " (mã thử)")).Id;
        await s.Rules.SetRoleMappingAsync(h.Admin, "COGS", "COGS");
        var wh = (await s.Stock.CreateWarehouseAsync(h.Admin, "XUONG", "Xưởng băm")).Id;
        await s.Stock.SetCostingPolicyAsync(h.Admin, wh, CostingMethod.MovingAverage, D(1, 1), "QĐ test");
        var log = (await s.Stock.CreateItemAsync(h.Admin, "GOKEO", "Gỗ keo", "tấn", "RAW")).Id;
        var chip = (await s.Stock.CreateItemAsync(h.Admin, "DAM", "Dăm gỗ", "MT", "FG")).Id;
        var obj = await new ProductionCostService(s.Db, s.Engine, s.Stock, s.Audit, h.Clock)
            .CreateCostObjectAsync(h.Admin, new CostObjectInput("SX-DAM", "Sản xuất dăm", chip, h.Acc["WIP"], 700_000));
        var march = await s.Db.AccountingPeriods.Where(p => p.CompanyId == h.CompanyId && p.StartDate == D(3, 1)).Select(p => p.Id).SingleAsync();
        return new Fx(h, wh, log, chip, obj.Id, march);
    }

    private static async Task PostDocAsync(Scope s, TestHost h, StockDocumentType type, Guid wh, Guid item, decimal qty, decimal? value, Guid counter, DateOnly d)
    {
        var doc = await s.Stock.CreateDocumentAsync(h.Preparer, new StockDocumentInput(type, wh, d, d, type == StockDocumentType.Receipt ? "Nhập" : "Xuất",
            [new StockLineInput(item, qty, value, counter)]));
        await s.Stock.PostDocumentAsync(h.Approver, doc.Id);
    }

    private static async Task<decimal> BalanceAsync(Scope s, Guid account)
    {
        var lines = await s.Db.JournalLines.AsNoTracking().Where(l => l.AccountId == account).Select(l => new { l.Side, l.Amount }).ToListAsync();
        return lines.Sum(l => l.Side == Side.Debit ? l.Amount : -l.Amount);
    }

    [Fact]
    public async Task ProductionOrder_Bom_Issue_Confirm_Complete_PostsAllocationAndReceivesAtActualCost()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var fx = await SetupAsync(s, h);
        var svc = new ProductionOrderService(s.Db, s.Engine, s.Stock, s.Audit, h.Clock);
        foreach (var (code, name) in new[] { ("LAB_APPL", "Nhân công phân bổ"), ("OH_APPL", "Sản xuất chung phân bổ") })
            h.Acc[code] = (await s.Setup.CreateAccountAsync(h.Admin, code, name + " (mã thử)")).Id;

        // Định mức: 1 tấn gỗ + hao hụt 10% cho mỗi MT dăm; thiếu dòng, trùng dòng, tự tham chiếu bị từ chối.
        Assert.Equal(ErrorCodes.VoucherNoLines, await CodeOf(() => svc.SaveBomAsync(h.Preparer, fx.Chip, [], null)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.SaveBomAsync(h.Preparer, fx.Chip, [new(fx.Chip, 1, 0)], null)));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => svc.SaveBomAsync(h.Auditor, fx.Chip, [new(fx.Log, 1, 10)], null)));
        var bom = await svc.SaveBomAsync(h.Preparer, fx.Chip, [new(fx.Log, 1, 10)], "Định mức dăm");
        Assert.Equal((1, 1.1m), (bom.Lines.Count, bom.Lines[0].QuantityPerUnit * (1 + bom.Lines[0].ScrapRate / 100)));
        var wc = await svc.SaveWorkCenterAsync(h.Preparer, null, "BAM", "Xưởng băm", 50_000, 100_000);

        await PostDocAsync(s, h, StockDocumentType.Receipt, fx.Wh, fx.Log, 150, 90_000_000, h.Acc["EQUITY"], D(3, 1));   // 600,000/tấn
        var order = await svc.CreateAsync(h.Preparer, new ProductionOrderInput(fx.Chip, fx.Object, fx.Wh, 100, D(3, 2)));
        Assert.StartsWith("LSX2026-", order.Number);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.CreateIssueAsync(h.Preparer, order.Id, null, D(3, 3))));   // chưa ban hành
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => svc.ReleaseAsync(h.Preparer with { Permissions = new HashSet<string>(h.Preparer.Permissions) { "voucher.approve" } }, order.Id)));
        await svc.ReleaseAsync(h.Approver, order.Id);

        var need = Assert.Single(await svc.RequirementsAsync(h.Auditor, order.Id));
        Assert.Equal((110m, 110m, 0m), (need.Required, need.Remaining, need.Shortage));       // 100 MT × 1,1 tấn; kho có 150
        var issue = await svc.CreateIssueAsync(h.Preparer, order.Id, null, D(3, 3));
        Assert.Equal(StockDocumentStatus.Draft, issue.Status);
        await svc.ConfirmWorkAsync(h.Preparer, order.Id, wc.Id, D(3, 4), 40, 20, "Ca 1");
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.CompleteAsync(h.Approver, order.Id, 98, D(3, 10))));            // phiếu xuất chưa ghi sổ
        await s.Stock.PostDocumentAsync(h.Approver, issue.Id);
        Assert.Equal(0m, Assert.Single(await svc.RequirementsAsync(h.Auditor, order.Id)).Remaining);

        var running = await svc.CostSheetAsync(h.Auditor, order.Id);
        Assert.Equal((66_000_000m, 2_000_000m, 2_000_000m, 70_000_000m, 700_000m), (running.MaterialCost, running.LaborCost, running.OverheadCost, running.TotalCost, running.UnitCost));
        Assert.Equal((40m, 20m), (running.LaborHours, running.MachineHours));

        // Hoàn thành khi chưa ánh xạ vai trò phân bổ → từ chối; ánh xạ rồi mới ghi Nhật ký.
        Assert.Equal(ErrorCodes.RuleRoleUnmapped, await CodeOf(() => svc.CompleteAsync(h.Approver, order.Id, 98, D(3, 10))));
        await s.Rules.SetRoleMappingAsync(h.Admin, "PROD_LABOR_APPLIED", "LAB_APPL");
        await s.Rules.SetRoleMappingAsync(h.Admin, "PROD_OVERHEAD_APPLIED", "OH_APPL");
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.CancelAsync(h.Preparer, order.Id, "x")));                   // đã phát sinh sản xuất
        var done = await svc.CompleteAsync(h.Approver, order.Id, 98, D(3, 10));
        Assert.Equal((ProductionOrderStatus.Completed, 70_000_000m, 714_285.7143m), (done.Status, done.TotalCost, done.UnitCost));
        Assert.Equal(70_000_000m, await BalanceAsync(s, h.Acc["WIP"]));   // 66 triệu nguyên liệu + 4 triệu chế biến; phiếu nhập thành phẩm chưa ghi sổ
        Assert.Equal(-2_000_000m, await BalanceAsync(s, h.Acc["LAB_APPL"]));
        Assert.Equal(-2_000_000m, await BalanceAsync(s, h.Acc["OH_APPL"]));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.CloseAsync(h.Preparer, order.Id)));                               // phiếu nhập chưa ghi sổ

        await s.Stock.PostDocumentAsync(h.Preparer, done.ReceiptDocumentId!.Value);   // người hoàn thành lệnh lập phiếu nhập nên người khác ghi sổ
        Assert.Equal(0m, await BalanceAsync(s, h.Acc["WIP"]));
        Assert.Equal(70_000_000m, await BalanceAsync(s, h.Acc["FG"]));
        var sheet = await svc.CostSheetAsync(h.Auditor, order.Id);
        var usage = Assert.Single(sheet.Materials);
        Assert.Equal((107.8m, 110m, 2.2m), (usage.StandardQuantity, usage.IssuedQuantity, usage.Variance));   // hao hụt vượt định mức 2,2 tấn
        Assert.Equal(ProductionOrderStatus.Closed, (await svc.CloseAsync(h.Preparer, order.Id)).Status);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.ConfirmWorkAsync(h.Preparer, order.Id, wc.Id, D(3, 11), 1, 0, null)));
    }

    [Fact]
    public async Task ActualCost_ReplacesPlannedCost_SplitBetweenStockAndCogs_LeavesClosingWip_AndReverses()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var fx = await SetupAsync(s, h);
        var svc = new ProductionCostService(s.Db, s.Engine, s.Stock, s.Audit, h.Clock);

        await PostDocAsync(s, h, StockDocumentType.Receipt, fx.Wh, fx.Log, 100, 50_000_000, h.Acc["EQUITY"], D(3, 1)); // buy 100 t of logs
        await PostDocAsync(s, h, StockDocumentType.Issue, fx.Wh, fx.Log, 100, null, h.Acc["WIP"], D(3, 2));         // into production: WIP 50,000,000
        await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, D(3, 31), D(3, 31), "Kết chuyển lương + chi phí chung SX",
            [new VoucherLineInput(h.Acc["WIP"], Side.Debit, 15_000_000), new VoucherLineInput(h.Acc["LABOUR"], Side.Credit, 15_000_000)]));
        await PostDocAsync(s, h, StockDocumentType.Receipt, fx.Wh, fx.Chip, 80, 56_000_000, h.Acc["WIP"], D(3, 20));  // 80 MT at planned 700,000
        await PostDocAsync(s, h, StockDocumentType.Issue, fx.Wh, fx.Chip, 30, null, h.Acc["COGS"], D(3, 25));         // 30 MT sold: COGS 21,000,000

        var preview = await svc.PreviewAsync(h.Auditor, fx.Object, fx.March, 3_000_000);
        // WIP before = 65,000,000 − 56,000,000 = 9,000,000; actual = 9,000,000 + 56,000,000 − 3,000,000 = 62,000,000 → 775,000/MT.
        Assert.Equal((9_000_000m, 56_000_000m, 80m, 62_000_000m, 775_000m, 6_000_000m),
            (preview.WipBalanceBefore, preview.ProvisionalValue, preview.OutputQuantity, preview.ActualCost, preview.UnitCost, preview.Difference));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.PreviewAsync(h.Auditor, fx.Object, fx.March, 70_000_000)));

        var run = await svc.RunAsync(h.Approver, fx.Object, fx.March, 3_000_000, "Dở dang theo NVL còn lại trên dây chuyền");
        Assert.Equal(3_000_000m, await BalanceAsync(s, h.Acc["WIP"]));
        Assert.Equal(21_000_000m + 2_250_000m, await BalanceAsync(s, h.Acc["COGS"]));            // 30/80 of 6,000,000
        Assert.Equal(56_000_000m - 21_000_000m + 3_750_000m, await BalanceAsync(s, h.Acc["FG"])); // 50/80 of 6,000,000
        Assert.All(await s.Stock.ReconcileAsync(h.Auditor, D(12, 31)), r => Assert.Equal(0m, r.Difference));
        Assert.Equal(ErrorCodes.PeriodCosted, await CodeOf(() => svc.RunAsync(h.Approver, fx.Object, fx.March, 3_000_000, "")));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.Engine.ReverseAsync(h.Approver, run.VoucherId!.Value, D(3, 31), "x")));

        await svc.ReverseAsync(h.Approver, run.Id, "Kiểm kê lại dở dang");
        Assert.Equal(9_000_000m, await BalanceAsync(s, h.Acc["WIP"]));
        Assert.Equal(35_000_000m, await BalanceAsync(s, h.Acc["FG"]));
        Assert.All(await s.Stock.ReconcileAsync(h.Auditor, D(12, 31)), r => Assert.Equal(0m, r.Difference));
        await svc.RunAsync(h.Approver, fx.Object, fx.March, 0, "Không còn dở dang");
        Assert.Equal(0m, await BalanceAsync(s, h.Acc["WIP"]));
    }

    [Fact]
    public void Spread_AddsUpExactly()
    {
        Assert.Equal([3_333_334m, 3_333_333m, 3_333_333m], ProductionCostService.Spread(10_000_000, [1, 1, 1], 0));
        Assert.Equal([-2m, -1m], ProductionCostService.Spread(-3, [50, 30], 0));
        Assert.Equal(-1_000m, ProductionCostService.Spread(-1_000, [7, 11, 13], 0).Sum());
    }

    [Fact]
    public async Task CostObject_NeedsItsOwnWipAccount()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var fx = await SetupAsync(s, h);
        var svc = new ProductionCostService(s.Db, s.Engine, s.Stock, s.Audit, h.Clock);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.CreateCostObjectAsync(h.Admin, new CostObjectInput("SX2", "Khác", fx.Chip, h.Acc["WIP"], 1))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.CreateCostObjectAsync(h.Admin, new CostObjectInput("SX3", "Khác", fx.Chip, h.Acc["CASH"], 1))));
    }
}
