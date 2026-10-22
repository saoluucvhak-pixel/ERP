using Hak.Application.Inventory;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

public class InventoryTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;
    private static DateOnly D(int month, int day) => new(2026, month, day);

    private sealed class Inv : IDisposable
    {
        public required TestHost H;
        public required Scope S;
        public Guid Fifo, Avg, Monthly, Dam;
        public void Dispose() { S.Dispose(); H.Dispose(); }
    }

    /// <summary>Three warehouses, one per costing method, and one item (wood chips, tonnes) on account INV (test fixture code).</summary>
    private static async Task<Inv> SetupAsync()
    {
        var h = await TestHost.CreateAsync();
        var s = h.NewScope();
        h.Acc["INV"] = (await s.Setup.CreateAccountAsync(h.Admin, "INV", "Hàng tồn kho (thử)")).Id;
        var x = new Inv { H = h, S = s };
        x.Fifo = (await s.Stock.CreateWarehouseAsync(h.Admin, "K-FIFO", "Kho FIFO")).Id;
        x.Avg = (await s.Stock.CreateWarehouseAsync(h.Admin, "K-AVG", "Kho bình quân tức thời")).Id;
        x.Monthly = (await s.Stock.CreateWarehouseAsync(h.Admin, "K-MON", "Kho bình quân tháng")).Id;
        await s.Stock.SetCostingPolicyAsync(h.Admin, x.Fifo, CostingMethod.Fifo, D(1, 1), "QĐ test");
        await s.Stock.SetCostingPolicyAsync(h.Admin, x.Avg, CostingMethod.MovingAverage, D(1, 1), "QĐ test");
        await s.Stock.SetCostingPolicyAsync(h.Admin, x.Monthly, CostingMethod.MonthlyAverage, D(1, 1), "QĐ test");
        x.Dam = (await s.Stock.CreateItemAsync(h.Admin, "DAM", "Dăm gỗ", "tấn", "INV")).Id;
        return x;
    }

    private static async Task<StockDocument> ReceiveAsync(Inv x, Guid wh, DateOnly date, decimal qty, decimal amount)
    {
        var d = await x.S.Stock.CreateDocumentAsync(x.H.Preparer, new StockDocumentInput(StockDocumentType.Receipt, wh, date, date, "Nhập mua dăm",
            [new StockLineInput(x.Dam, qty, amount, x.H.Acc["AP"])], PartnerId: x.H.Partner["NCC01"]));
        return await x.S.Stock.PostDocumentAsync(x.H.Approver, d.Id);
    }

    private static async Task<StockDocument> IssueAsync(Inv x, Guid wh, DateOnly date, decimal qty)
    {
        var d = await x.S.Stock.CreateDocumentAsync(x.H.Preparer, new StockDocumentInput(StockDocumentType.Issue, wh, date, date, "Xuất sản xuất",
            [new StockLineInput(x.Dam, qty, CounterAccountId: x.H.Acc["EXP"])]));
        return await x.S.Stock.PostDocumentAsync(x.H.Approver, d.Id);
    }

    private static async Task<decimal> IssueValueAsync(Inv x, StockDocument doc)
    {
        var m = await x.S.Db.StockMovements.AsNoTracking().SingleAsync(m => m.DocumentId == doc.Id);
        return -(await x.S.Db.StockValuations.AsNoTracking().SingleAsync(v => v.MovementId == m.Id && v.VoidedAt == null)).Value;
    }

    private static async Task<decimal> Balance(Inv x, string acc, DateOnly asOf) => await x.S.Ledger.AccountBalanceAsync(x.H.Auditor, x.H.Acc[acc], asOf);

    [Fact]
    public async Task Fifo_EndToEnd_StockLedgerAndGeneralLedgerAgree()
    {
        using var x = await SetupAsync();
        await ReceiveAsync(x, x.Fifo, D(3, 1), 10, 1_000_000);
        await ReceiveAsync(x, x.Fifo, D(3, 3), 10, 1_200_000);
        var issue = await IssueAsync(x, x.Fifo, D(3, 5), 15);
        Assert.Equal(1_600_000m, await IssueValueAsync(x, issue));

        Assert.Equal(600_000m, await Balance(x, "INV", D(3, 31)));
        Assert.Equal(1_600_000m, await Balance(x, "EXP", D(3, 31)));
        Assert.Equal(-2_200_000m, await Balance(x, "AP", D(3, 31)));
        var row = Assert.Single(await x.S.Stock.SummaryAsync(x.H.Auditor, D(3, 1), D(3, 31)));
        Assert.Equal((20m, 2_200_000m, 15m, 1_600_000m, 5m, 600_000m), (row.InQty, row.InValue, row.OutQty, row.OutValue, row.ClosingQty, row.ClosingValue));
        var rec = Assert.Single(await x.S.Stock.ReconcileAsync(x.H.Auditor, D(3, 31)));
        Assert.Equal(0m, rec.Difference);
        Assert.Equal(3, await x.S.Db.Vouchers.CountAsync(v => v.SourceSystem == StockService.StockSource && v.Status == VoucherStatus.Posted));

        var card = await x.S.Stock.StockCardAsync(x.H.Auditor, x.Fifo, x.Dam, D(3, 1), D(3, 31));
        Assert.Equal((5m, 600_000m), (card[^1].BalanceQty, card[^1].BalanceValue!.Value));
    }

    [Fact]
    public async Task MovingAverage_RevaluesAfterEachReceipt_AndEmptiesExactly()
    {
        using var x = await SetupAsync();
        await ReceiveAsync(x, x.Avg, D(3, 1), 10, 1_000_000);
        await ReceiveAsync(x, x.Avg, D(3, 2), 10, 1_300_000);
        Assert.Equal(1_725_000m, await IssueValueAsync(x, await IssueAsync(x, x.Avg, D(3, 3), 15)));
        Assert.Equal(575_000m, await IssueValueAsync(x, await IssueAsync(x, x.Avg, D(3, 4), 5)));
        Assert.Equal(0m, await Balance(x, "INV", D(3, 31)));
    }

    // Chị chốt 21/10/2026: đóng sổ (khóa kỳ) thì không tính lại giá xuất kho của kỳ đó.
    [Fact]
    public async Task LockedPeriod_KeepsItsIssueCosts_CostingCannotBeRerunOrCancelled()
    {
        using var x = await SetupAsync();
        await ReceiveAsync(x, x.Monthly, D(3, 1), 10, 1_000_000);
        var issue = await IssueAsync(x, x.Monthly, D(3, 5), 4);
        var march = await x.S.Db.AccountingPeriods.SingleAsync(p => p.PeriodNo == 3);
        var run = await x.S.Stock.RunMonthlyCostingAsync(x.H.Approver, x.Monthly, march.Id);
        Assert.Equal(400_000m, await IssueValueAsync(x, issue));

        await x.S.Setup.LockPeriodAsync(x.H.Approver, march.Id);
        Assert.Equal(ErrorCodes.PeriodLocked, await CodeOf(() => x.S.Stock.ReverseCostingRunAsync(x.H.Approver, run.Id, "Tính lại giá")));
        Assert.Equal(ErrorCodes.PeriodLocked, await CodeOf(() => x.S.Stock.RunMonthlyCostingAsync(x.H.Approver, x.Monthly, march.Id)));

        await ReceiveAsync(x, x.Monthly, D(4, 2), 10, 2_000_000);        // hoạt động sau đó không đụng giá đã chốt
        Assert.Equal(400_000m, await IssueValueAsync(x, issue));
    }

    [Fact]
    public async Task MonthlyAverage_IssuesWaitForMonthEndRun_ThenPostOneVoucher()
    {
        using var x = await SetupAsync();
        await ReceiveAsync(x, x.Monthly, D(3, 1), 10, 1_000_000);
        var i1 = await IssueAsync(x, x.Monthly, D(3, 5), 4);
        await ReceiveAsync(x, x.Monthly, D(3, 20), 10, 1_600_000);
        var i2 = await IssueAsync(x, x.Monthly, D(3, 25), 6);
        Assert.Null(i1.VoucherId); // no value known yet → no GL entry
        var row = Assert.Single(await x.S.Stock.SummaryAsync(x.H.Auditor, D(3, 1), D(3, 31)));
        Assert.Equal(10m, row.PendingOutQty);
        Assert.Equal(0m, Assert.Single(await x.S.Stock.ReconcileAsync(x.H.Auditor, D(3, 31))).Difference);

        var march = await x.S.Db.AccountingPeriods.SingleAsync(p => p.PeriodNo == 3);
        var run = await x.S.Stock.RunMonthlyCostingAsync(x.H.Approver, x.Monthly, march.Id);
        Assert.Equal((520_000m, 780_000m), (await IssueValueAsync(x, i1), await IssueValueAsync(x, i2))); // 2,600,000 / 20 = 130,000 per tonne
        Assert.Equal(1_300_000m, await Balance(x, "INV", D(3, 31)));
        Assert.Equal(1_300_000m, await Balance(x, "EXP", D(3, 31)));
        Assert.NotNull(run.VoucherId);

        Assert.Equal(ErrorCodes.PeriodCosted, await CodeOf(() => x.S.Stock.RunMonthlyCostingAsync(x.H.Approver, x.Monthly, march.Id)));
        Assert.Equal(ErrorCodes.PeriodCosted, await CodeOf(() => ReceiveAsync(x, x.Monthly, D(3, 28), 1, 100)));
        await IssueAsync(x, x.Monthly, D(4, 2), 3); // the next month continues normally

        await x.S.Stock.ReverseCostingRunAsync(x.H.Approver, run.Id, "Bổ sung phiếu nhập tháng 3");
        Assert.Equal(2_600_000m, await Balance(x, "INV", D(3, 31))); // GL back to pre-costing state
        var april = await x.S.Db.AccountingPeriods.SingleAsync(p => p.PeriodNo == 4);
        Assert.Equal(ErrorCodes.CostingPending, await CodeOf(() => x.S.Stock.RunMonthlyCostingAsync(x.H.Approver, x.Monthly, april.Id)));
        var rerun = await x.S.Stock.RunMonthlyCostingAsync(x.H.Approver, x.Monthly, march.Id);
        Assert.NotEqual(run.Id, rerun.Id);
        Assert.Equal(520_000m, await IssueValueAsync(x, i1));
    }

    [Fact]
    public async Task MonthlyAverage_IssueReversedInSameMonth_IsNotCosted()
    {
        using var x = await SetupAsync();
        await ReceiveAsync(x, x.Monthly, D(3, 1), 10, 1_000_000);
        var wrong = await IssueAsync(x, x.Monthly, D(3, 5), 4);
        await x.S.Stock.ReverseDocumentAsync(x.H.Approver, wrong.Id, D(3, 6), "Xuất nhầm kho");
        await IssueAsync(x, x.Monthly, D(3, 7), 5);
        var march = await x.S.Db.AccountingPeriods.SingleAsync(p => p.PeriodNo == 3);
        await x.S.Stock.RunMonthlyCostingAsync(x.H.Approver, x.Monthly, march.Id);
        Assert.Equal(500_000m, await Balance(x, "INV", D(3, 31)));
        Assert.Equal(500_000m, await Balance(x, "EXP", D(3, 31)));
    }

    [Fact]
    public async Task Transfer_MovesCostBetweenWarehouses_WithoutGlEntry()
    {
        using var x = await SetupAsync();
        await ReceiveAsync(x, x.Fifo, D(3, 1), 10, 1_000_000);
        var t = await x.S.Stock.CreateDocumentAsync(x.H.Preparer, new StockDocumentInput(StockDocumentType.Transfer, x.Fifo, D(3, 2), D(3, 2), "Điều chuyển",
            [new StockLineInput(x.Dam, 4)], ToWarehouseId: x.Avg));
        t = await x.S.Stock.PostDocumentAsync(x.H.Approver, t.Id);
        Assert.Null(t.VoucherId);
        var rows = await x.S.Stock.SummaryAsync(x.H.Auditor, D(3, 1), D(3, 31));
        Assert.Equal(400_000m, rows.Single(r => r.WarehouseCode == "K-AVG").ClosingValue);
        Assert.Equal(600_000m, rows.Single(r => r.WarehouseCode == "K-FIFO").ClosingValue);
        Assert.Equal(1_000_000m, await Balance(x, "INV", D(3, 31)));

        var fromMonthly = await x.S.Stock.CreateDocumentAsync(x.H.Preparer, new StockDocumentInput(StockDocumentType.Transfer, x.Monthly, D(3, 3), D(3, 3), "x",
            [new StockLineInput(x.Dam, 1)], ToWarehouseId: x.Fifo));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => x.S.Stock.PostDocumentAsync(x.H.Approver, fromMonthly.Id)));
    }

    [Fact]
    public async Task Guards_NegativeStock_Backdating_SegregationOfDuties_ManualIssueValue()
    {
        using var x = await SetupAsync();
        await ReceiveAsync(x, x.Fifo, D(3, 10), 5, 500_000);
        Assert.Equal(ErrorCodes.InsufficientStock, await CodeOf(() => IssueAsync(x, x.Fifo, D(3, 11), 6)));
        Assert.Equal(ErrorCodes.StockBackdated, await CodeOf(() => IssueAsync(x, x.Fifo, D(3, 9), 1)));

        var own = await x.S.Stock.CreateDocumentAsync(x.H.Approver, new StockDocumentInput(StockDocumentType.Issue, x.Fifo, D(3, 12), D(3, 12), "x",
            [new StockLineInput(x.Dam, 1, CounterAccountId: x.H.Acc["EXP"])]));
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => x.S.Stock.PostDocumentAsync(x.H.Approver, own.Id)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => x.S.Stock.CreateDocumentAsync(x.H.Preparer, new StockDocumentInput(StockDocumentType.Issue,
            x.Fifo, D(3, 12), D(3, 12), "x", [new StockLineInput(x.Dam, 1, 999, x.H.Acc["EXP"])]))));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => x.S.Stock.CreateDocumentAsync(x.H.Auditor, new StockDocumentInput(StockDocumentType.Issue,
            x.Fifo, D(3, 12), D(3, 12), "x", [new StockLineInput(x.Dam, 1, CounterAccountId: x.H.Acc["EXP"])]))));
        Assert.Equal(0m, Assert.Single(await x.S.Stock.ReconcileAsync(x.H.Auditor, D(3, 31))).Difference);
    }

    [Fact]
    public async Task CostingPolicy_OnlyFromFiscalYearStart_AndNeverRetroactive()
    {
        using var x = await SetupAsync();
        var noPolicy = (await x.S.Stock.CreateWarehouseAsync(x.H.Admin, "K-NEW", "Kho chưa cấu hình")).Id;
        var d = await x.S.Stock.CreateDocumentAsync(x.H.Preparer, new StockDocumentInput(StockDocumentType.Receipt, noPolicy, D(3, 1), D(3, 1), "x",
            [new StockLineInput(x.Dam, 1, 100, x.H.Acc["AP"])], PartnerId: x.H.Partner["NCC01"]));
        Assert.Equal(ErrorCodes.CostingPolicyMissing, await CodeOf(() => x.S.Stock.PostDocumentAsync(x.H.Approver, d.Id)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => x.S.Stock.SetCostingPolicyAsync(x.H.Admin, noPolicy, CostingMethod.Fifo, D(3, 1), "giữa năm")));
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => x.S.Stock.SetCostingPolicyAsync(x.H.Admin, noPolicy, CostingMethod.Fifo, D(1, 1), "")));

        await ReceiveAsync(x, x.Fifo, D(3, 1), 1, 100);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => x.S.Stock.SetCostingPolicyAsync(x.H.Admin, x.Fifo, CostingMethod.MovingAverage, D(1, 1), "đổi")));
    }

    [Fact]
    public async Task Reversal_OfUnusedReceipt_RestoresBothLedgers_ButNotAfterLaterMovements()
    {
        using var x = await SetupAsync();
        var r1 = await ReceiveAsync(x, x.Fifo, D(3, 1), 10, 1_000_000);
        var rev = await x.S.Stock.ReverseDocumentAsync(x.H.Approver, r1.Id, D(3, 2), "Nhập trùng");
        Assert.Equal(StockDocumentStatus.Posted, rev.Status);
        Assert.Equal(0m, await Balance(x, "INV", D(3, 31)));
        Assert.Equal(0m, Assert.Single(await x.S.Stock.SummaryAsync(x.H.Auditor, D(3, 1), D(3, 31))).ClosingQty);

        var r2 = await ReceiveAsync(x, x.Fifo, D(3, 3), 10, 1_000_000);
        await IssueAsync(x, x.Fifo, D(3, 4), 2);
        Assert.Equal(ErrorCodes.StockBackdated, await CodeOf(() => x.S.Stock.ReverseDocumentAsync(x.H.Approver, r2.Id, D(3, 5), "sai")));

        // The GL voucher of a stock document cannot be reversed on its own.
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => x.S.Engine.ReverseAsync(x.H.Approver, r2.VoucherId!.Value, D(3, 6), "sai")));
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => x.S.Db.Database.ExecuteSqlRawAsync("UPDATE stock_movements SET quantity = 1"));
        Assert.Contains("LEDGER_IMMUTABLE", ex.Message);
        ex = await Assert.ThrowsAnyAsync<Exception>(() => x.S.Db.Database.ExecuteSqlRawAsync("UPDATE stock_valuations SET value = 1"));
        Assert.Contains("LEDGER_IMMUTABLE", ex.Message);
        Assert.Equal(0m, Assert.Single(await x.S.Stock.ReconcileAsync(x.H.Auditor, D(3, 31))).Difference);
    }
}
