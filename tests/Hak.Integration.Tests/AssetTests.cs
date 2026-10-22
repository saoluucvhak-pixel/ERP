using Hak.Application.Accounting;
using Hak.Application.Assets;
using Hak.Domain.Inventory;
using Hak.Domain.Assets;
using Hak.Domain.Common;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Fixed assets / tools: cost from posted lines, depreciation runs in period order, disposal, reversals, register ↔ ledger.</summary>
public class AssetTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    private static async Task<TestHost> HostAsync()
    {
        var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        foreach (var (code, name) in new[] { ("FA", "TSCĐ (mã thử)"), ("FA_ACC", "Hao mòn TSCĐ (mã thử)"), ("DEP_EXP", "Chi phí khấu hao (mã thử)"),
                     ("PREPAID", "Chi phí trả trước / CCDC (mã thử)"), ("LOSS", "Chi phí khác (mã thử)") })
            h.Acc[code] = (await s.Setup.CreateAccountAsync(h.Admin, code, name)).Id;
        return h;
    }

    /// <summary>Posts Dr <paramref name="account"/> / Cr AP and returns the debit journal line.</summary>
    private static async Task<Guid> BuyAsync(TestHost h, Scope s, string account, decimal amount, DateOnly date)
    {
        var v = await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, date, date, $"Mua tài sản {account}",
            [new VoucherLineInput(h.Acc[account], Side.Debit, amount), new VoucherLineInput(h.Acc["AP"], Side.Credit, amount, h.Partner["NCC01"])]));
        return await LineOfAsync(s, v.Id, h.Acc[account]);
    }

    private static Task<Guid> LineOfAsync(Scope s, Guid voucherId, Guid accountId)
        => (from l in s.Db.JournalLines join e in s.Db.JournalEntries on l.JournalEntryId equals e.Id
            where e.VoucherId == voucherId && l.AccountId == accountId && l.Side == Side.Debit select l.Id).SingleAsync();

    private static async Task<decimal> BalanceAsync(Scope s, Guid account, DateOnly? asOf = null)
    {
        var d = asOf ?? new DateOnly(2026, 12, 31);
        var lines = await s.Db.JournalLines.AsNoTracking().Where(l => l.AccountId == account && l.PostingDate <= d).Select(l => new { l.Side, l.Amount }).ToListAsync();
        return lines.Sum(l => l.Side == Side.Debit ? l.Amount : -l.Amount);
    }

    private static Task<Guid> PeriodAsync(TestHost h, Scope s, int month)
        => s.Db.AccountingPeriods.Where(p => p.CompanyId == h.CompanyId && p.StartDate == new DateOnly(2026, month, 1)).Select(p => p.Id).SingleAsync();

    private static AssetInput FixedAsset(TestHost h, string code, Guid line, decimal amount, DateOnly inService, int months = 60, ProrationPolicy proration = ProrationPolicy.FullMonth)
        => new(code, $"Tài sản {code}", AssetClass.FixedAsset, h.Acc["FA"], h.Acc["FA_ACC"], h.Acc["DEP_EXP"], proration, 0, months, inService,
            [new AssetCostSourceInput(line, amount)]);

    private static async Task<LongTermAsset> ActiveAsync(TestHost h, Scope s, AssetInput input)
    {
        var a = await s.Assets.CreateAsync(h.Preparer, input);
        return await s.Assets.ActivateAsync(h.Approver, a.Id);
    }

    [Fact]
    public async Task AssetCycle_Request_Assign_Count()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var cycle = new AssetCycleService(s.Db, s.Audit, h.Clock);

        // Đề nghị mua sắm: người duyệt khác người lập; từ chối cần lý do; chỉ đề nghị đã duyệt mới gắn được thẻ.
        var req = await cycle.RequestAsync(h.Preparer, new AssetRequestInput(AssetClass.FixedAsset, "Máy nghiền dăm", "Xưởng 1", 120_000_000, new(2026, 1, 5), "Thay máy cũ"));
        Assert.StartsWith("DNMS2026-", req.Number);
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => cycle.DecideAsync(h.Preparer with { Permissions = new HashSet<string>(h.Preparer.Permissions) { "voucher.approve" } }, req.Id, true, null)));
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => cycle.DecideAsync(h.Approver, req.Id, false, " ")));
        var line = await BuyAsync(h, s, "FA", 120_000_000, new(2026, 1, 10));
        var asset = await ActiveAsync(h, s, FixedAsset(h, "TS010", line, 120_000_000, new(2026, 1, 15)));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => cycle.LinkAssetAsync(h.Preparer, req.Id, asset.Id)));
        Assert.Equal(AssetRequestStatus.Approved, (await cycle.DecideAsync(h.Approver, req.Id, true, "Đồng ý")).Status);
        var done = await cycle.LinkAssetAsync(h.Preparer, req.Id, asset.Id);
        Assert.Equal((AssetRequestStatus.Fulfilled, asset.Id), (done.Status, done.AssetId));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => cycle.CancelRequestAsync(h.Preparer, req.Id, "x")));

        // Bàn giao: lần sau đóng lần trước.
        Assert.Null(Assert.Single(await cycle.CurrentAsync(h.Auditor)).Department);
        await cycle.AssignAsync(h.Preparer, asset.Id, "Xưởng 1", "Nguyễn Văn A", "Nhà máy", new(2026, 1, 16), null);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => cycle.AssignAsync(h.Preparer, asset.Id, "Xưởng 2", null, null, new(2026, 1, 1), null)));
        await cycle.AssignAsync(h.Preparer, asset.Id, "Xưởng 2", "Trần Văn B", null, new(2026, 3, 1), "Điều chuyển");
        Assert.Equal("Xưởng 2", Assert.Single(await cycle.CurrentAsync(h.Auditor)).Department);
        var hist = await cycle.AssignmentHistoryAsync(h.Auditor, asset.Id);
        Assert.Equal((2, new DateOnly(2026, 3, 1)), (hist.Count, hist[0].StartDate));
        Assert.Equal(new DateOnly(2026, 3, 1), hist[1].EndDate);

        // Kiểm kê tại 31/03: phải ghi nhận hết mới chốt; mất cần ghi chú; người chốt khác người lập.
        var cnt = await cycle.CreateCountAsync(h.Preparer, new(2026, 3, 31), "Kiểm kê quý 1");
        Assert.StartsWith("KKTS2026-", cnt.Number);
        Assert.Equal(("Xưởng 2", 1), (Assert.Single(cnt.Lines).BookDepartment, cnt.Total));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => cycle.CompleteAsync(h.Approver, cnt.Id)));   // chưa kiểm
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => cycle.RecordAsync(h.Preparer, cnt.Id, [new(asset.Id, AssetCountResult.Missing)])));
        var rec = await cycle.RecordAsync(h.Preparer, cnt.Id, [new(asset.Id, AssetCountResult.Found, "Xưởng 3", "Chuyển chưa làm thủ tục")]);
        Assert.Equal((1, 0, 1), (rec.Counted, rec.Missing, rec.Moved));
        var finished = await cycle.CompleteAsync(h.Approver, cnt.Id);
        Assert.Equal(AssetCountStatus.Completed, finished.Status);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => cycle.RecordAsync(h.Preparer, cnt.Id, [new(asset.Id, AssetCountResult.Found)])));
    }

    [Fact]
    public async Task Maintenance_WorkOrder_FullFlow_Schedule_Kpi_AndAssetJournal()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var repairs = new AssetRepairService(s.Db, s.Stock, s.Audit, h.Clock);
        var cycle = new AssetCycleService(s.Db, s.Audit, h.Clock);
        var journal = new AssetJournalService(s.Db);
        var line = await BuyAsync(h, s, "FA", 120_000_000, new(2026, 1, 10));
        var asset = await ActiveAsync(h, s, FixedAsset(h, "TS020", line, 120_000_000, new(2026, 1, 15)));
        await cycle.AssignAsync(h.Preparer, asset.Id, "Xưởng 1", "Nguyễn Văn A", null, new(2026, 1, 16), null);
        h.Acc["INV"] = (await s.Setup.CreateAccountAsync(h.Admin, "INV", "Phụ tùng (mã thử)")).Id;
        var wh = (await s.Stock.CreateWarehouseAsync(h.Admin, "PT", "Kho phụ tùng")).Id;
        var bearing = (await s.Stock.CreateItemAsync(h.Admin, "BD01", "Bạc đạn", "cái", "INV")).Id;

        // Yêu cầu sửa chữa (YCSC) → phiếu thi công Nháp.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => repairs.RequestAsync(h.Preparer, asset.Id, MaintenancePriority.High, " ")));
        var req = await repairs.RequestAsync(h.Preparer, asset.Id, MaintenancePriority.High, "Máy rung mạnh, kêu to");
        Assert.StartsWith("YCSC2026-", req.Number);
        var wo = await repairs.CreateAsync(h.Preparer, new(asset.Id, RepairType.Emergency, new(2026, 2, 3), "Hỏng bạc đạn trục chính", req.Id, null, "Cơ khí Minh Phát", "Trần Văn B",
            "Mòn bạc đạn", "Thay 2 bạc đạn, căn chỉnh trục", 1_000_000, [new(bearing, 2, 3_000_000)]));
        Assert.StartsWith("SCTS2026-", wo.Number);
        Assert.Equal((1_000_000m, 6_000_000m, 7_000_000m), (wo.EstimatedLaborCost, wo.EstimatedMaterialCost, wo.EstimatedTotal));
        Assert.Equal(MaintenanceRequestStatus.Surveying, (await repairs.RequestsAsync(h.Auditor)).Single().Status);

        // Gửi duyệt → duyệt (khác người lập) → thi công.
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => repairs.StartAsync(h.Preparer, wo.Id)));
        await repairs.SubmitAsync(h.Preparer, wo.Id);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => repairs.UpdatePlanAsync(h.Preparer, wo.Id, new(asset.Id, RepairType.Emergency, new(2026, 2, 3), "x"))));
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => repairs.DecideAsync(h.Approver, wo.Id, false, " ")));
        Assert.Equal(RepairStatus.Approved, (await repairs.DecideAsync(h.Approver, wo.Id, true, null)).Status);
        h.Clock.UtcNow = new DateTime(2026, 2, 3, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(RepairStatus.InProgress, (await repairs.StartAsync(h.Preparer, wo.Id)).Status);

        // Xuất vật tư: phiếu xuất kho Nháp đối ứng TK chi phí; còn vật tư chờ xuất thì chưa nghiệm thu.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => repairs.RequestAcceptanceAsync(h.Preparer, wo.Id, 900_000, null)));
        var keeper = h.Preparer with { Permissions = new HashSet<string>(h.Preparer.Permissions) { "stock.document" } };
        var doc = await repairs.IssueMaterialsAsync(keeper, wo.Id, wh, h.Acc["LOSS"], new(2026, 2, 4));
        Assert.Equal(StockDocumentType.Issue, doc.Type);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => repairs.IssueMaterialsAsync(keeper, wo.Id, wh, h.Acc["LOSS"], new(2026, 2, 4))));

        // Nghiệm thu: không đạt thì trả về thi công; đạt thì Completed; người có quyền duyệt đóng.
        h.Clock.UtcNow = new DateTime(2026, 2, 3, 14, 30, 0, DateTimeKind.Utc);
        var tested = await repairs.RequestAcceptanceAsync(h.Preparer, wo.Id, 900_000, "Đã thay bạc đạn");
        Assert.Equal((RepairStatus.Testing, 900_000m + 6_000_000m), (tested.Status, tested.ActualTotal));
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => repairs.AcceptAsync(h.Preparer, wo.Id, false, null)));
        Assert.Equal(RepairStatus.InProgress, (await repairs.AcceptAsync(h.Preparer, wo.Id, false, "Còn rung")).Status);
        await repairs.RequestAcceptanceAsync(h.Preparer, wo.Id, 900_000, null);
        Assert.Equal(RepairStatus.Completed, (await repairs.AcceptAsync(h.Preparer, wo.Id, true, null)).Status);
        Assert.Equal(RepairStatus.Closed, (await repairs.CloseAsync(h.Approver, wo.Id)).Status);
        Assert.Equal(MaintenanceRequestStatus.Done, (await repairs.RequestsAsync(h.Auditor)).Single().Status);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => repairs.CancelAsync(h.Preparer, wo.Id, "x")));

        // Lịch bảo trì: lập phiếu từ lịch đến hạn, đóng phiếu thì dời hạn kế tiếp.
        var sch = await repairs.SaveScheduleAsync(h.Preparer, null, asset.Id, "Tra mỡ, kiểm tra dây curoa", 30, new(2026, 2, 10));
        Assert.True(Assert.Single(await repairs.SchedulesAsync(h.Auditor, new(2026, 2, 20))).Overdue);
        var pm = await repairs.CreateFromScheduleAsync(h.Preparer, sch.Id, new(2026, 2, 20));
        Assert.Equal(RepairType.Preventive, pm.Type);
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => repairs.CreateFromScheduleAsync(h.Preparer, sch.Id, new(2026, 2, 21))));
        await repairs.SubmitAsync(h.Preparer, pm.Id); await repairs.DecideAsync(h.Approver, pm.Id, true, null);
        h.Clock.UtcNow = new DateTime(2026, 2, 20, 8, 0, 0, DateTimeKind.Utc); await repairs.StartAsync(h.Preparer, pm.Id);
        h.Clock.UtcNow = new DateTime(2026, 2, 20, 10, 0, 0, DateTimeKind.Utc); await repairs.RequestAcceptanceAsync(h.Preparer, pm.Id, 200_000, null);
        await repairs.AcceptAsync(h.Preparer, pm.Id, true, null); await repairs.CloseAsync(h.Approver, pm.Id);
        var after = Assert.Single(await repairs.SchedulesAsync(h.Auditor, new(2026, 2, 20)));
        Assert.Equal((new DateOnly(2026, 2, 20), new DateOnly(2026, 3, 22), false), (after.LastPerformedAt, after.NextDueDate, after.Overdue));

        // KPI tháng 2: 1 sự cố, sửa 6,5 giờ; MTBF = (672 − 6,5) ÷ 1; chi phí gồm cả phiếu bảo trì.
        var kpi = Assert.Single(await repairs.KpiAsync(h.Auditor, new(2026, 2, 1), new(2026, 2, 28)));
        Assert.Equal((1, 6.5m, 6.5m, 665.5m, 1), (kpi.Failures, kpi.RepairHours, kpi.Mttr, kpi.Mtbf, kpi.PreventiveCount));
        Assert.Equal((1_100_000m, 6_000_000m, 7_100_000m, "Xưởng 1"), (kpi.LaborCost, kpi.MaterialCost, kpi.TotalCost, kpi.Department));

        // Nhật ký tài sản có đủ mọi biến động.
        var j = await journal.GetAsync(h.Auditor, asset.Id);
        Assert.Equal(("Xưởng 1", 2, 7_100_000m), (j.Department, j.RepairCount, j.RepairCost));
        var titles = j.Events.Select(e => e.Title).ToList();
        foreach (var expect in new[] { "Lập thẻ tài sản", "Nguồn nguyên giá", "Ghi tăng, đưa vào sử dụng", "Bàn giao / điều chuyển", "Yêu cầu sửa chữa / báo sự cố", "Lập phiếu sửa chữa",
                     "Duyệt phương án và dự toán", "Bắt đầu thi công", "Xuất vật tư sửa chữa", "Hoàn thành nghiệm thu", "Đóng phiếu", "Lập phiếu bảo trì" })
            Assert.Contains(expect, titles);
        Assert.Equal(j.Events.OrderBy(e => e.Date).Select(e => e.Date), j.Events.Select(e => e.Date));
        Assert.Equal(2, (await repairs.ListAsync(h.Auditor, asset.Id)).Count);
    }

    [Fact]
    public async Task PrepaidCost_SplitAcrossDepartments_RoundingReportReverseAndDisposal()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        foreach (var code in new[] { "EXP_A", "EXP_B", "EXP_C" }) h.Acc[code] = (await s.Setup.CreateAccountAsync(h.Admin, code, $"Chi phí bộ phận {code} (mã thử)")).Id;
        var line = await BuyAsync(h, s, "PREPAID", 12_000_000, new(2026, 1, 5));
        var asset = await ActiveAsync(h, s, new AssetInput("BH001", "Bảo hiểm cháy nổ 12 tháng", AssetClass.Tool, h.Acc["PREPAID"], null, h.Acc["DEP_EXP"], ProrationPolicy.FullMonth, 0, 12,
            new(2026, 1, 15), [new AssetCostSourceInput(line, 12_000_000)]));

        // Kiểm tra đầu vào: tổng 100%, không trùng, tỷ lệ hợp lệ.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Assets.SetExpenseSplitsAsync(h.Preparer, asset.Id, [new(h.Acc["EXP_A"], "Xưởng 1", 60), new(h.Acc["EXP_B"], "Xưởng 2", 39)])));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Assets.SetExpenseSplitsAsync(h.Preparer, asset.Id, [new(h.Acc["EXP_A"], "Xưởng 1", 50), new(h.Acc["EXP_A"], "xưởng 1", 50)])));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Assets.SetExpenseSplitsAsync(h.Preparer, asset.Id, [new(h.Acc["EXP_A"], "Xưởng 1", 100), new(h.Acc["EXP_B"], "Xưởng 2", 0)])));

        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Assets.SetExpenseSplitsAsync(h.Preparer, asset.Id, [new(h.Acc["AP"], "Công nợ", 50), new(h.Acc["EXP_B"], "Xưởng 2", 50)])));   // TK công nợ không làm TK chi phí

        // Kỳ 1: chia 33,3333 / 33,3333 / 33,3334 → phần lẻ dồn vào dòng lớn nhất, tổng đúng 1.000.000.
        await s.Assets.SetExpenseSplitsAsync(h.Preparer, asset.Id, [new(h.Acc["EXP_A"], "Kho", 33.3333m), new(h.Acc["EXP_B"], "Xưởng", 33.3333m), new(h.Acc["EXP_C"], "Văn phòng", 33.3334m)]);
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 1));
        Assert.Equal((333_333m, 333_333m, 333_334m, 0m), (await BalanceAsync(s, h.Acc["EXP_A"]), await BalanceAsync(s, h.Acc["EXP_B"]), await BalanceAsync(s, h.Acc["EXP_C"]), await BalanceAsync(s, h.Acc["DEP_EXP"])));
        Assert.Equal(-1_000_000m + 12_000_000m, await BalanceAsync(s, h.Acc["PREPAID"]));

        // Kỳ 2: đổi cách chia (chỉ áp dụng từ kỳ này); kỳ 1 giữ nguyên.
        await s.Assets.SetExpenseSplitsAsync(h.Preparer, asset.Id, [new(h.Acc["EXP_A"], "Kho", 50), new(h.Acc["EXP_B"], "Xưởng", 50)]);
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 2));
        Assert.Equal((833_333m, 833_333m, 333_334m), (await BalanceAsync(s, h.Acc["EXP_A"]), await BalanceAsync(s, h.Acc["EXP_B"]), await BalanceAsync(s, h.Acc["EXP_C"])));
        var report = await s.Assets.ExpenseAllocationAsync(h.Auditor, new(2026, 1, 1), new(2026, 2, 28));
        Assert.Equal(2_000_000m, report.Sum(r => r.Amount));
        Assert.Equal(833_333m, report.Single(r => r.Label == "Kho").Amount);
        Assert.Equal(333_334m, report.Single(r => r.Label == "Văn phòng").Amount);
        Assert.Equal(1_000_000m, (await s.Assets.ExpenseAllocationAsync(h.Auditor, new(2026, 2, 1), new(2026, 2, 28))).Sum(r => r.Amount));

        // Hủy kỳ 2: số chia của kỳ 2 không còn trong báo cáo.
        var run2 = await s.Db.DepreciationRuns.SingleAsync(r => r.PeriodStart == new DateOnly(2026, 2, 1) && r.Status == DepreciationRunStatus.Active);
        await s.Assets.ReverseRunAsync(h.Approver, run2.Id, "Chạy nhầm kỳ");
        Assert.Equal(1_000_000m, (await s.Assets.ExpenseAllocationAsync(h.Auditor, new(2026, 1, 1), new(2026, 12, 31))).Sum(r => r.Amount));

        // Chạy lại kỳ 2 theo cách chia đang đặt: kết quả như cũ.
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 2));
        Assert.Equal((833_333m, 833_333m, 333_334m), (await BalanceAsync(s, h.Acc["EXP_A"]), await BalanceAsync(s, h.Acc["EXP_B"]), await BalanceAsync(s, h.Acc["EXP_C"])));
        Assert.Equal(2_000_000m, (await s.Assets.ExpenseAllocationAsync(h.Auditor, new(2026, 1, 1), new(2026, 12, 31))).Sum(r => r.Amount));
    }

    [Fact]
    public async Task DisposalPartialAllocation_FollowsTheExpenseSplit()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        foreach (var code in new[] { "EXP_A", "EXP_B" }) h.Acc[code] = (await s.Setup.CreateAccountAsync(h.Admin, code, $"Chi phí bộ phận {code} (mã thử)")).Id;
        var line = await BuyAsync(h, s, "PREPAID", 12_000_000, new(2026, 1, 2));
        var asset = await ActiveAsync(h, s, new AssetInput("TN001", "Tiền thuê nhà xưởng", AssetClass.Tool, h.Acc["PREPAID"], null, h.Acc["DEP_EXP"], ProrationPolicy.Daily, 0, 12,
            new(2026, 1, 1), [new AssetCostSourceInput(line, 12_000_000)]));
        await s.Assets.SetExpenseSplitsAsync(h.Preparer, asset.Id, [new(h.Acc["EXP_A"], "Xưởng 1", 50), new(h.Acc["EXP_B"], "Xưởng 2", 50)]);
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 1));
        Assert.Equal((500_000m, 500_000m), (await BalanceAsync(s, h.Acc["EXP_A"]), await BalanceAsync(s, h.Acc["EXP_B"])));

        // Ghi giảm 11/02: phân bổ theo ngày đến 10/02 = 1.357.143 lũy kế, phần cuối 357.143 chia 50/50 (phần lẻ dồn vào dòng đầu).
        await s.Assets.DisposeAsync(h.Approver, asset.Id, new(2026, 2, 11), h.Acc["LOSS"], "Chấm dứt thuê");
        var a = await BalanceAsync(s, h.Acc["EXP_A"]); var b = await BalanceAsync(s, h.Acc["EXP_B"]);
        Assert.Equal(1_357_143m, a + b);
        Assert.Equal(357_143m, (await s.Assets.ExpenseAllocationAsync(h.Auditor, new(2026, 2, 1), new(2026, 2, 28))).Sum(r => r.Amount));
        Assert.Equal(12_000_000m - 1_357_143m, await BalanceAsync(s, h.Acc["LOSS"]));
        Assert.Equal(0m, await BalanceAsync(s, h.Acc["PREPAID"]));
        Assert.Equal(0m, await BalanceAsync(s, h.Acc["DEP_EXP"]));
    }

    [Fact]
    public async Task AllocationFrequency_Quarterly_Yearly_BookOnlyAtBoundary_AndCatchUp()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var l1 = await BuyAsync(h, s, "PREPAID", 12_000_000, new(2026, 1, 2));
        var quarterly = await ActiveAsync(h, s, new AssetInput("Q001", "Bảo hiểm trả theo quý", AssetClass.Tool, h.Acc["PREPAID"], null, h.Acc["DEP_EXP"], ProrationPolicy.FullMonth, 0, 12,
            new(2026, 1, 1), [new AssetCostSourceInput(l1, 12_000_000)], Frequency: AllocationFrequency.Quarterly));
        var l2 = await BuyAsync(h, s, "PREPAID", 4_000_000, new(2026, 1, 3));
        var short4 = await ActiveAsync(h, s, new AssetInput("Q002", "Công cụ 4 tháng, ghi theo quý", AssetClass.Tool, h.Acc["PREPAID"], null, h.Acc["DEP_EXP"], ProrationPolicy.FullMonth, 0, 4,
            new(2026, 1, 1), [new AssetCostSourceInput(l2, 4_000_000)], Frequency: AllocationFrequency.Quarterly));
        var l3 = await BuyAsync(h, s, "PREPAID", 6_000_000, new(2026, 1, 4));
        var yearly = await ActiveAsync(h, s, new AssetInput("Y001", "Thuê nhà xưởng trả theo năm", AssetClass.Tool, h.Acc["PREPAID"], null, h.Acc["DEP_EXP"], ProrationPolicy.FullMonth, 0, 12,
            new(2026, 1, 1), [new AssetCostSourceInput(l3, 6_000_000)], Frequency: AllocationFrequency.Yearly));

        async Task<decimal> BookedAsync(Guid id) => (await s.Db.DepreciationEntries.Where(e => e.AssetId == id && e.VoidedAt == null).ToListAsync()).Sum(e => e.Amount);
        async Task RunMonth(int m) => await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, m));

        await RunMonth(1); await RunMonth(2);
        Assert.Equal((0m, 0m, 0m), (await BookedAsync(quarterly.Id), await BookedAsync(short4.Id), await BookedAsync(yearly.Id)));   // chưa đến cuối quý / năm
        await RunMonth(3);
        Assert.Equal((3_000_000m, 3_000_000m, 0m), (await BookedAsync(quarterly.Id), await BookedAsync(short4.Id), await BookedAsync(yearly.Id)));   // dồn đủ 3 tháng
        await RunMonth(4);
        Assert.Equal((3_000_000m, 4_000_000m, 0m), (await BookedAsync(quarterly.Id), await BookedAsync(short4.Id), await BookedAsync(yearly.Id)));   // thẻ 4 tháng hoàn tất giữa quý: ghi nốt
        for (var m = 5; m <= 11; m++) await RunMonth(m);
        Assert.Equal((9_000_000m, 4_000_000m, 0m), (await BookedAsync(quarterly.Id), await BookedAsync(short4.Id), await BookedAsync(yearly.Id)));   // quý 2, 3 đã ghi (tháng 6, 9)
        await RunMonth(12);
        Assert.Equal((12_000_000m, 4_000_000m, 6_000_000m), (await BookedAsync(quarterly.Id), await BookedAsync(short4.Id), await BookedAsync(yearly.Id)));
        // Tổng chi phí ghi sổ đúng nguyên giá các thẻ; tài khoản trả trước về 0.
        Assert.Equal(22_000_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));
        Assert.Equal(0m, await BalanceAsync(s, h.Acc["PREPAID"]));
    }

    [Fact]
    public async Task AllocationFrequency_DisposalInsideQuarter_BooksTheMissingMonths()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var line = await BuyAsync(h, s, "PREPAID", 12_000_000, new(2026, 1, 2));
        var a = await ActiveAsync(h, s, new AssetInput("Q003", "Ghi theo quý", AssetClass.Tool, h.Acc["PREPAID"], null, h.Acc["DEP_EXP"], ProrationPolicy.FullMonth, 0, 12,
            new(2026, 1, 1), [new AssetCostSourceInput(line, 12_000_000)], Frequency: AllocationFrequency.Quarterly));
        for (var m = 1; m <= 4; m++) await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, m));   // quý 1 đã ghi (3.000.000), tháng 4 chưa đến kỳ ghi
        await s.Assets.DisposeAsync(h.Approver, a.Id, new(2026, 5, 20), h.Acc["LOSS"], "Chấm dứt");     // tính tròn tháng: đến hết tháng 4
        Assert.Equal(4_000_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));                              // 3 tháng quý 1 + tháng 4 ghi nốt khi ghi giảm
        Assert.Equal(8_000_000m, await BalanceAsync(s, h.Acc["LOSS"]));
        Assert.Equal(0m, await BalanceAsync(s, h.Acc["PREPAID"]));
    }

    [Fact]
    public async Task Method_TimeBased_ByActualDays_AndDisposal()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var line = await BuyAsync(h, s, "PREPAID", 36_500_000, new(2026, 1, 2));
        var a = await ActiveAsync(h, s, new AssetInput("BH365", "Bảo hiểm 15/01/2026 – 14/01/2027", AssetClass.Tool, h.Acc["PREPAID"], null, h.Acc["DEP_EXP"], ProrationPolicy.FullMonth, 0, 1,
            new(2026, 1, 15), [new AssetCostSourceInput(line, 36_500_000)], Method: DepreciationMethod.TimeBased, ScheduleEndDate: new(2027, 1, 14)));
        Assert.Equal(13, a.UsefulLifeMonths);                                                     // 13 tháng dương lịch phủ khoảng ngày
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Assets.CreateAsync(h.Preparer, new AssetInput("X1", "thiếu ngày kết thúc", AssetClass.Tool, h.Acc["PREPAID"], null, h.Acc["DEP_EXP"], ProrationPolicy.FullMonth, 0, 1,
            new(2026, 1, 15), [new AssetCostSourceInput(line, 1)], Method: DepreciationMethod.TimeBased))));
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 1));
        Assert.Equal(1_700_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));                         // 17 ngày × 100.000
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 2));
        Assert.Equal(4_500_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));                         // + 28 ngày
        await s.Assets.DisposeAsync(h.Approver, a.Id, new(2026, 3, 10), h.Acc["LOSS"], "Hủy bảo hiểm");
        Assert.Equal(5_400_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));                         // thêm 9 ngày đến hết 09/03
        Assert.Equal(31_100_000m, await BalanceAsync(s, h.Acc["LOSS"]));
        Assert.Equal(0m, await BalanceAsync(s, h.Acc["PREPAID"]));
    }

    [Fact]
    public async Task Method_DecliningBalance_FiveYears_Factor2()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var line = await BuyAsync(h, s, "FA", 100_000_000, new(2026, 1, 2));
        var input = FixedAsset(h, "SD001", line, 100_000_000, new(2026, 1, 1), 60) with { Method = DepreciationMethod.DecliningBalance };
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Assets.CreateAsync(h.Preparer, input with { Code = "SD0", Class = AssetClass.Tool, AccumulatedAccountId = null })));   // chỉ TSCĐ
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Assets.CreateAsync(h.Preparer, input with { Code = "SD0", DecliningFactor = 9 })));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Assets.CreateAsync(h.Preparer, input with { Code = "SD0", OpeningAccumulated = 1_000_000, RemainingLifeMonths = 30, ScheduleStartDate = new(2026, 1, 1) })));
        var a = await ActiveAsync(h, s, input);
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 1));
        Assert.Equal(3_333_333m, await BalanceAsync(s, h.Acc["DEP_EXP"]));                         // 40.000.000 ÷ 12
        for (var m = 2; m <= 12; m++) await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, m));
        Assert.Equal(40_000_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));                        // năm 1: 100 triệu × 2 ÷ 5 năm
        Assert.Equal(-40_000_000m, await BalanceAsync(s, h.Acc["FA_ACC"]));
        var detail = await s.Assets.DetailAsync(h.Auditor, a.Id);
        Assert.Equal(64_000_000m, detail.Schedule.First(r => r.PeriodEnd == new DateOnly(2027, 12, 31)).Cumulative);
        Assert.Equal(100_000_000m, detail.Schedule.Last().Cumulative);
    }

    [Fact]
    public async Task Method_UnitsOfProduction_ByActualOutput_PerPeriod()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var line = await BuyAsync(h, s, "FA", 10_000_000, new(2026, 1, 2));
        var a = await ActiveAsync(h, s, FixedAsset(h, "SL001", line, 10_000_000, new(2026, 1, 1), 1) with { Method = DepreciationMethod.UnitsOfProduction, TotalUnits = 1_000, UnitName = "tấn" });
        var jan = await PeriodAsync(h, s, 1); var feb = await PeriodAsync(h, s, 2); var mar = await PeriodAsync(h, s, 3);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Assets.RunAsync(h.Approver, jan)));                   // chưa nhập sản lượng
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Assets.RecordUsageAsync(h.Preparer, a.Id, jan, 1_100)));   // vượt tổng dự kiến
        await s.Assets.RecordUsageAsync(h.Preparer, a.Id, jan, 100);
        await s.Assets.RunAsync(h.Approver, jan);
        Assert.Equal(1_000_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));
        Assert.Equal(ErrorCodes.PeriodDepreciated, await CodeOf(() => s.Assets.RecordUsageAsync(h.Preparer, a.Id, jan, 120)));   // kỳ đã chạy, không sửa
        await s.Assets.RecordUsageAsync(h.Preparer, a.Id, feb, 150);
        await s.Assets.RunAsync(h.Approver, feb);
        Assert.Equal(2_500_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));                                                  // lũy kế 250 ÷ 1.000
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Assets.RecordUsageAsync(h.Preparer, a.Id, mar, 800)));   // 250 + 800 > 1.000
        // Hủy kỳ 2 → sửa lại sản lượng → chạy lại.
        var run2 = await s.Db.DepreciationRuns.SingleAsync(r => r.PeriodId == feb && r.Status == DepreciationRunStatus.Active);
        await s.Assets.ReverseRunAsync(h.Approver, run2.Id, "Sai sản lượng");
        await s.Assets.RecordUsageAsync(h.Preparer, a.Id, feb, 200);
        await s.Assets.RunAsync(h.Approver, feb);
        Assert.Equal(3_000_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));                                                  // lũy kế 300
        var usage = await s.Assets.UsageAsync(h.Auditor, a.Id);
        Assert.Equal((300m, 700m, true), (usage[1].Cumulative, usage[1].Remaining ?? 0, usage[1].Locked));
        // Ghi giảm: giá trị còn lại 7.000.000 chuyển vào TK đã chọn.
        await s.Assets.DisposeAsync(h.Approver, a.Id, new(2026, 3, 20), h.Acc["LOSS"], "Thanh lý máy");
        Assert.Equal(7_000_000m, await BalanceAsync(s, h.Acc["LOSS"]));
        Assert.Equal(0m, await BalanceAsync(s, h.Acc["FA"]));
    }

    [Fact]
    public async Task SetFrequency_AfterActivation_CatchesUpWithoutLossOrDuplicate()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var line = await BuyAsync(h, s, "PREPAID", 12_000_000, new(2026, 1, 2));
        var a = await ActiveAsync(h, s, new AssetInput("F001", "Thuê kho", AssetClass.Tool, h.Acc["PREPAID"], null, h.Acc["DEP_EXP"], ProrationPolicy.FullMonth, 0, 12, new(2026, 1, 1), [new AssetCostSourceInput(line, 12_000_000)]));
        async Task Run(int m) => await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, m));
        await Run(1);
        Assert.Equal(1_000_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));                                        // hàng tháng
        await s.Assets.SetFrequencyAsync(h.Preparer, a.Id, AllocationFrequency.Quarterly);                          // đổi sau khi ghi tăng và đã chạy
        await Run(2);
        Assert.Equal(1_000_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));                                        // tháng 2 chưa đến cuối quý
        await Run(3);
        Assert.Equal(3_000_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));                                        // dồn đủ tháng 2 và 3
        await s.Assets.SetFrequencyAsync(h.Preparer, a.Id, AllocationFrequency.Monthly);
        await Run(4);
        Assert.Equal(4_000_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));
        Assert.Equal(AllocationFrequency.Monthly, (await s.Assets.DetailAsync(h.Auditor, a.Id)).Asset.AllocationFrequency);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Assets.SetFrequencyAsync(h.Preparer, a.Id, (AllocationFrequency)9)));
        await s.Assets.DisposeAsync(h.Approver, a.Id, new(2026, 5, 20), h.Acc["LOSS"], "Chấm dứt");
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.Assets.SetFrequencyAsync(h.Preparer, a.Id, AllocationFrequency.Yearly)));
    }

    [Fact]
    public async Task FixedAsset_Lifecycle_FullMonth_RunsInOrder_DisposeAndReverse()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var line = await BuyAsync(h, s, "FA", 120_000_000, new(2026, 1, 10));
        var asset = await s.Assets.CreateAsync(h.Preparer, FixedAsset(h, "TS001", line, 120_000_000, new(2026, 1, 15)));
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => s.Assets.ActivateAsync(h.Preparer, asset.Id)));
        asset = await s.Assets.ActivateAsync(h.Approver, asset.Id);
        Assert.Equal(AssetStatus.Active, asset.Status);
        Assert.Equal(120_000_000m, asset.OriginalCost);

        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 1));
        Assert.Equal(2_000_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));
        Assert.Equal(ErrorCodes.DepreciationPending, await CodeOf(async () => await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 3))));
        Assert.Equal(ErrorCodes.PeriodDepreciated, await CodeOf(async () => await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 1))));
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 2));
        Assert.Equal(-4_000_000m, await BalanceAsync(s, h.Acc["FA_ACC"]));

        var recon = await s.Assets.ReconcileAsync(h.Auditor, new(2026, 2, 28));
        Assert.All(recon, r => Assert.Equal(0, r.Difference));
        Assert.Equal(-4_000_000m, recon.Single(r => r.AccountId == h.Acc["FA_ACC"]).RegisterBalance);

        // Full-month policy: no depreciation in the disposal month. NBV 116,000,000 goes to the chosen account.
        await s.Assets.DisposeAsync(h.Approver, asset.Id, new(2026, 3, 20), h.Acc["LOSS"], "Thanh lý theo biên bản 01");
        Assert.Equal(0, await BalanceAsync(s, h.Acc["FA"]));
        Assert.Equal(0, await BalanceAsync(s, h.Acc["FA_ACC"]));
        Assert.Equal(116_000_000m, await BalanceAsync(s, h.Acc["LOSS"]));
        Assert.All(await s.Assets.ReconcileAsync(h.Auditor, new(2026, 3, 31)), r => Assert.Equal(0, r.Difference));

        // The disposal voucher belongs to the asset module.
        var disposalVoucher = (await s.Db.LongTermAssets.AsNoTracking().SingleAsync(a => a.Id == asset.Id)).DisposalVoucherId!.Value;
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.Engine.ReverseAsync(h.Approver, disposalVoucher, new(2026, 3, 31), "x")));

        await s.Assets.ReverseDisposalAsync(h.Approver, asset.Id, "Biên bản thanh lý bị hủy");
        Assert.Equal(120_000_000m, await BalanceAsync(s, h.Acc["FA"]));
        Assert.Equal(-4_000_000m, await BalanceAsync(s, h.Acc["FA_ACC"]));
        Assert.Equal(0, await BalanceAsync(s, h.Acc["LOSS"]));
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 3));
        Assert.Equal(-6_000_000m, await BalanceAsync(s, h.Acc["FA_ACC"]));
        Assert.Equal(6_000_000m, (await s.Assets.RegisterAsync(h.Auditor, new(2026, 3, 31))).Single().Accumulated);
    }

    [Fact]
    public async Task Tool_DailyProration_ScheduleClosesExactly_AndDisposalBooksPartialMonth()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var line = await BuyAsync(h, s, "PREPAID", 31_000_000, new(2026, 1, 17));
        var tool = await ActiveAsync(h, s, new AssetInput("CC001", "Máy cắt", AssetClass.Tool, h.Acc["PREPAID"], null, h.Acc["DEP_EXP"],
            ProrationPolicy.Daily, 0, 12, new(2026, 1, 17), [new AssetCostSourceInput(line, 31_000_000)]));

        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 1));
        Assert.Equal(1_250_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));  // 15/31 of 2,583,333.33
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 2));
        Assert.Equal(3_833_333m, await BalanceAsync(s, h.Acc["DEP_EXP"]));

        var detail = await s.Assets.DetailAsync(h.Auditor, tool.Id);
        Assert.Equal(31_000_000m, detail.Schedule.Sum(r => r.Planned));
        Assert.Equal(new DateOnly(2027, 1, 31), detail.Schedule[^1].PeriodEnd);
        Assert.Equal(1_250_000m, detail.Schedule[0].Booked);

        // Disposed on 11/03: daily policy books 1–10 March = 10/31 of a month; the rest goes to LOSS. The tool account closes to zero.
        await s.Assets.DisposeAsync(h.Approver, tool.Id, new(2026, 3, 11), h.Acc["LOSS"], "Hỏng không sửa được");
        var march = DepreciationCalculator.CumulativeTarget(31_000_000, 12, new(2026, 1, 17), new(2026, 3, 10), ProrationPolicy.Daily, 0) - 3_833_333m;
        Assert.Equal(833_334m, march);  // 4,666,666.67 → 4,666,667 cumulative
        Assert.Equal(3_833_333m + march, await BalanceAsync(s, h.Acc["DEP_EXP"]));
        Assert.Equal(0, await BalanceAsync(s, h.Acc["PREPAID"]));
        Assert.Equal(31_000_000m - 3_833_333m - march, await BalanceAsync(s, h.Acc["LOSS"]));
    }

    [Fact]
    public async Task CostSources_MustBePostedDebitsOnTheAssetAccount_AndCannotBeUsedTwice()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var line = await BuyAsync(h, s, "FA", 120_000_000, new(2026, 1, 10));
        await ActiveAsync(h, s, FixedAsset(h, "TS001", line, 70_000_000, new(2026, 1, 15)));
        await ActiveAsync(h, s, FixedAsset(h, "TS002", line, 50_000_000, new(2026, 1, 15)));
        Assert.Equal(ErrorCodes.AssetCostMismatch, await CodeOf(() => s.Assets.CreateAsync(h.Preparer, FixedAsset(h, "TS003", line, 1, new(2026, 1, 15)))));
        Assert.Empty(await s.Assets.CostCandidatesAsync(h.Auditor, h.Acc["FA"]));

        // A line on another account, and a line of a reversed voucher, are both refused.
        var prepaid = await BuyAsync(h, s, "PREPAID", 5_000_000, new(2026, 1, 12));
        Assert.Equal(ErrorCodes.AssetCostMismatch, await CodeOf(() => s.Assets.CreateAsync(h.Preparer, FixedAsset(h, "TS004", prepaid, 5_000_000, new(2026, 1, 15)))));
        var v = await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, new(2026, 1, 20), new(2026, 1, 20), "Mua nhầm",
            [new VoucherLineInput(h.Acc["FA"], Side.Debit, 9_000_000), new VoucherLineInput(h.Acc["AP"], Side.Credit, 9_000_000, h.Partner["NCC01"])]));
        var wrong = await LineOfAsync(s, v.Id, h.Acc["FA"]);
        Assert.Single(await s.Assets.CostCandidatesAsync(h.Auditor, h.Acc["FA"]));
        await s.Engine.ReverseAsync(h.Approver, v.Id, new(2026, 1, 21), "Mua nhầm");
        Assert.Equal(ErrorCodes.AssetCostMismatch, await CodeOf(() => s.Assets.CreateAsync(h.Preparer, FixedAsset(h, "TS005", wrong, 9_000_000, new(2026, 1, 25)))));

        // Reversed-voucher lines are no longer candidates; drafts never reserve a line.
        Assert.Empty(await s.Assets.CostCandidatesAsync(h.Auditor, h.Acc["FA"]));
    }

    [Fact]
    public async Task Runs_ReverseLatestFirst_AndRerunGivesSameResult_FirstRunMustCoverEarliestStart()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var line = await BuyAsync(h, s, "FA", 36_000_000, new(2026, 1, 5));
        await ActiveAsync(h, s, FixedAsset(h, "TS001", line, 36_000_000, new(2026, 1, 5), months: 36));
        Assert.Equal(ErrorCodes.DepreciationPending, await CodeOf(async () => await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 2))));

        var jan = await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 1));
        var feb = await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 2));
        Assert.Equal(ErrorCodes.PeriodDepreciated, await CodeOf(() => s.Assets.ReverseRunAsync(h.Approver, jan.Id, "x")));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.Engine.ReverseAsync(h.Approver, feb.VoucherId!.Value, new(2026, 2, 28), "x")));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Assets.ReverseRunAsync(h.Auditor, feb.Id, "x")));

        await s.Assets.ReverseRunAsync(h.Approver, feb.Id, "Sai thời gian sử dụng");
        Assert.Equal(1_000_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));
        Assert.Equal(1, await s.Db.DepreciationEntries.CountAsync(e => e.VoidedAt == null));
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 2));
        Assert.Equal(2_000_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));

        // An asset whose schedule starts inside an already-depreciated period cannot be activated silently.
        var line2 = await BuyAsync(h, s, "FA", 12_000_000, new(2026, 2, 10));
        var late = await s.Assets.CreateAsync(h.Preparer, FixedAsset(h, "TS002", line2, 12_000_000, new(2026, 2, 10)));
        Assert.Equal(ErrorCodes.PeriodDepreciated, await CodeOf(() => s.Assets.ActivateAsync(h.Approver, late.Id)));
    }

    [Fact]
    public async Task TakenOverAsset_DepreciatesRemainingValueOverRemainingLife_AndReconciles()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        // Opening balances from the previous system: cost 120,000,000, accumulated 30,000,000.
        var opb = await h.PostedAsync(s, new VoucherInput(VoucherTypes.OpeningBalance, new(2026, 1, 1), new(2026, 1, 1), "Số dư đầu kỳ TSCĐ",
        [
            new VoucherLineInput(h.Acc["FA"], Side.Debit, 120_000_000), new VoucherLineInput(h.Acc["FA_ACC"], Side.Credit, 30_000_000),
            new VoucherLineInput(h.Acc["EQUITY"], Side.Credit, 90_000_000),
        ]));
        var line = await LineOfAsync(s, opb.Id, h.Acc["FA"]);
        var input = FixedAsset(h, "TS-CU", line, 120_000_000, new(2023, 1, 1)) with
        {
            OpeningAccumulated = 30_000_000, RemainingLifeMonths = 36, ScheduleStartDate = new DateOnly(2026, 1, 1),
        };
        await ActiveAsync(h, s, input);
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 1));
        Assert.Equal(2_500_000m, await BalanceAsync(s, h.Acc["DEP_EXP"]));
        var recon = await s.Assets.ReconcileAsync(h.Auditor, new(2026, 1, 31));
        Assert.All(recon, r => Assert.Equal(0, r.Difference));
        Assert.Equal(-32_500_000m, recon.Single(r => r.AccountId == h.Acc["FA_ACC"]).LedgerBalance);

        // An unexplained posting on the asset account shows up as a difference.
        await BuyAsync(h, s, "FA", 7_000_000, new(2026, 1, 25));
        Assert.Equal(7_000_000m, (await s.Assets.ReconcileAsync(h.Auditor, new(2026, 1, 31))).Single(r => r.AccountId == h.Acc["FA"]).Difference);
    }

    [Fact]
    public async Task ActiveAsset_And_BookedDepreciation_AreImmutable()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var line = await BuyAsync(h, s, "FA", 60_000_000, new(2026, 1, 10));
        var asset = await ActiveAsync(h, s, FixedAsset(h, "TS001", line, 60_000_000, new(2026, 1, 10)));
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 1));

        using (var s2 = h.NewScope())
        {
            var tracked = await s2.Db.LongTermAssets.SingleAsync(a => a.Id == asset.Id);
            tracked.UsefulLifeMonths = 120;
            Assert.Equal(ErrorCodes.LedgerImmutable, (await Assert.ThrowsAsync<DomainException>(() => s2.Db.SaveChangesAsync())).Code);
        }
        var id = h.SqlGuidLiteral(asset.Id);
        Assert.Contains("LEDGER_IMMUTABLE", (await Assert.ThrowsAnyAsync<Exception>(() =>
            s.Db.Database.ExecuteSqlRawAsync($"UPDATE long_term_assets SET useful_life_months = 120 WHERE id = {id}"))).Message);
        Assert.Contains("LEDGER_IMMUTABLE", (await Assert.ThrowsAnyAsync<Exception>(() =>
            s.Db.Database.ExecuteSqlRawAsync("DELETE FROM depreciation_entries"))).Message);
        Assert.Contains("LEDGER_IMMUTABLE", (await Assert.ThrowsAnyAsync<Exception>(() =>
            s.Db.Database.ExecuteSqlRawAsync("UPDATE asset_cost_sources SET amount = 1"))).Message);
    }

    private static byte[] Jpeg(int extra = 100) => [0xFF, 0xD8, 0xFF, 0xE0, .. Enumerable.Range(0, extra).Select(i => (byte)(i % 251))];
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [Fact]
    public async Task IncidentPhotos_AcceptOnlyRealImages_WithinLimits_AreAuditedAndTamperEvident()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var repairs = new AssetRepairService(s.Db, s.Stock, s.Audit, h.Clock);
        var line = await BuyAsync(h, s, "FA", 120_000_000, new(2026, 1, 10));
        var asset = await ActiveAsync(h, s, FixedAsset(h, "TS021", line, 120_000_000, new(2026, 1, 15)));
        var req = await repairs.RequestAsync(h.Preparer, asset.Id, MaintenancePriority.Urgent, "Rò dầu thủy lực");
        var root = Path.Combine(Path.GetTempPath(), "hak-att-" + Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new Hak.Application.Attachments.AttachmentService(s.Db, new Hak.Application.Attachments.AttachmentStore(root), s.Audit, h.Clock);
            // Loại tệp theo chữ ký, không theo tên / Content-Type.
            Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.AddAsync(h.Preparer, "maintenance_request", req.Id, "anh.jpg", "<html>không phải ảnh</html>"u8.ToArray(), null)));
            Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.AddAsync(h.Preparer, "maintenance_request", req.Id, "anh.jpg", [], null)));
            Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.AddAsync(h.Preparer, "maintenance_request", req.Id, "anh.jpg", Jpeg(9 * 1024 * 1024), null)));   // quá 8 MB
            Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.AddAsync(h.Preparer, "invoice", req.Id, "anh.jpg", Jpeg(), null)));                                 // loại đối tượng lạ
            Assert.Equal(ErrorCodes.NotFound, await CodeOf(() => svc.AddAsync(h.Preparer, "maintenance_request", Guid.NewGuid(), "anh.jpg", Jpeg(), null)));
            Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => svc.AddAsync(h.Auditor, "maintenance_request", req.Id, "anh.jpg", Jpeg(), null)));                            // kiểm toán chỉ xem

            // Ảnh hợp lệ: tên gốc được làm sạch (không giữ đường dẫn), tên lưu là mã nội bộ.
            var a = await svc.AddAsync(h.Preparer, "maintenance_request", req.Id, @"..\..\Windows\hiện trường 1.jpg", Jpeg(), "Vết rò");
            Assert.Equal(("hiện trường 1.jpg", "image/jpeg"), (a.FileName, a.ContentType));
            Assert.DoesNotContain("hiện", a.StoragePath);
            await svc.AddAsync(h.Preparer, "maintenance_request", req.Id, "b.png", Png(), null);
            Assert.Equal(2, (await svc.ListAsync(h.Auditor, "maintenance_request", req.Id)).Count);
            Assert.Equal(2, (await svc.CountsAsync(h.Auditor, "maintenance_request"))[req.Id]);
            var (meta, data) = await svc.ReadAsync(h.Auditor, a.Id);
            Assert.Equal(Jpeg(), data);
            Assert.Equal("image/jpeg", meta.ContentType);

            // Chỉ thêm: sửa hoặc xóa bản ghi bị chặn.
            var tracked = await s.Db.FileAttachments.FirstAsync(x => x.Id == a.Id);
            tracked.Caption = "đổi";
            Assert.Equal(ErrorCodes.LedgerImmutable, (await Assert.ThrowsAsync<DomainException>(() => s.Db.SaveChangesAsync())).Code);
            s.Db.ChangeTracker.Clear();

            // Tệp bị sửa ngoài phần mềm thì phát hiện qua mã băm.
            await File.WriteAllBytesAsync(Path.Combine(root, a.StoragePath), Jpeg(150));
            Assert.Equal(ErrorCodes.IntegrityViolation, await CodeOf(() => svc.ReadAsync(h.Auditor, a.Id)));

            // Giới hạn số ảnh mỗi phiếu.
            for (var i = 2; i < Hak.Application.Attachments.AttachmentService.MaxPerEntity; i++) await svc.AddAsync(h.Preparer, "maintenance_request", req.Id, $"p{i}.jpg", Jpeg(10 + i), null);
            Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.AddAsync(h.Preparer, "maintenance_request", req.Id, "thua.jpg", Jpeg(5), null)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>
    /// DB-08, nâng cấp: một database đang dùng (ảnh đính kèm, khấu hao có chia chi phí) ở lược đồ của bản trước được nâng lên bản mới —
    /// dữ liệu giữ nguyên, các lỗ hổng bất biến được đóng ở tầng database (sửa thẳng bằng SQL, bỏ qua phần mềm, bị chặn),
    /// còn các thao tác hợp lệ (đổi kỳ ghi sổ, hủy lần chạy khấu hao) vẫn làm được.
    /// </summary>
    [Fact]
    public async Task UpgradeFromPreviousVersion_KeepsData_AndClosesTheImmutabilityGaps()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        foreach (var code in new[] { "EXP_A", "EXP_B" }) h.Acc[code] = (await s.Setup.CreateAccountAsync(h.Admin, code, $"Chi phí bộ phận {code} (mã thử)")).Id;
        var line = await BuyAsync(h, s, "FA", 120_000_000, new(2026, 1, 10));
        var asset = await ActiveAsync(h, s, FixedAsset(h, "TS901", line, 120_000_000, new(2026, 1, 15)));
        await s.Assets.SetExpenseSplitsAsync(h.Preparer, asset.Id, [new(h.Acc["EXP_A"], "Xưởng", 60), new(h.Acc["EXP_B"], "Văn phòng", 40)]);
        await s.Assets.RunAsync(h.Approver, await PeriodAsync(h, s, 1));
        var repairs = new AssetRepairService(s.Db, s.Stock, s.Audit, h.Clock);
        var req = await repairs.RequestAsync(h.Preparer, asset.Id, MaintenancePriority.High, "Máy rung mạnh");
        var root = Path.Combine(Path.GetTempPath(), "hak-att-" + Guid.NewGuid().ToString("N"));
        try
        {
            var photo = await new Hak.Application.Attachments.AttachmentService(s.Db, new Hak.Application.Attachments.AttachmentStore(root), s.Audit, h.Clock)
                .AddAsync(h.Preparer, "maintenance_request", req.Id, "hien-truong.jpg", Jpeg(), "Vết nứt");
            var split = await s.Db.DepreciationEntrySplits.AsNoTracking().FirstAsync();
            var entry = await s.Db.DepreciationEntries.AsNoTracking().FirstAsync(e => e.AssetId == asset.Id);
            async Task<(int, int, int, decimal, decimal)> Snapshot()
            {
                s.Db.ChangeTracker.Clear();
                return (await s.Db.FileAttachments.CountAsync(), await s.Db.DepreciationEntrySplits.CountAsync(), await s.Db.DepreciationEntries.CountAsync(),
                    (await s.Db.DepreciationEntrySplits.Select(x => x.Amount).ToListAsync()).Sum(), (await s.Db.DepreciationEntries.Select(x => x.Amount).ToListAsync()).Sum());
            }
            var before = await Snapshot();
            Assert.Equal((1, 2), (before.Item1, before.Item2));

            // Về lược đồ của bản trước (dữ liệu giữ nguyên) rồi nâng cấp như khi cài bản mới.
            var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(s.Db);
            // Bản ngay trước DB-08 (trước Phase15ImmutabilityParity), không phải "migration áp chót": các migration sau này không làm test này đổi nghĩa.
            var migrations = s.Db.Database.GetMigrations().ToList();
            var previous = migrations[migrations.FindIndex(m => m.EndsWith("_Phase15ImmutabilityParity", StringComparison.Ordinal)) - 1];
            await migrator.MigrateAsync(previous);
            Assert.Equal(previous, (await s.Db.Database.GetAppliedMigrationsAsync()).Last());
            if (!h.IsPostgres)   // bản cũ trên SQLite: lỗ hổng có thật (PostgreSQL đã chặn hai bảng này từ trước)
                Assert.Equal(1, await s.Db.Database.ExecuteSqlAsync($"UPDATE depreciation_entry_splits SET amount = amount WHERE id = {split.Id}"));
            Hak.Data.HakDb.Migrate(s.Db);
            Assert.Empty(await s.Db.Database.GetPendingMigrationsAsync());
            Assert.Equal(before, await Snapshot());

            // Sửa / xóa thẳng bằng SQL (bỏ qua phần mềm) nay bị database từ chối.
            async Task Blocked(FormattableString sql) =>
                Assert.Contains("LEDGER_IMMUTABLE", (await Assert.ThrowsAnyAsync<Exception>(() => s.Db.Database.ExecuteSqlAsync(sql))).ToString());
            await Blocked($"UPDATE file_attachments SET caption = 'sửa' WHERE id = {photo.Id}");
            await Blocked($"DELETE FROM file_attachments WHERE id = {photo.Id}");
            await Blocked($"UPDATE depreciation_entry_splits SET amount = amount + 1 WHERE id = {split.Id}");
            await Blocked($"DELETE FROM depreciation_entry_splits WHERE id = {split.Id}");
            await Blocked($"UPDATE long_term_assets SET unit_name = 'giờ máy' WHERE id = {asset.Id}");
            await Blocked($"UPDATE long_term_assets SET total_units = 1000 WHERE id = {asset.Id}");
            await Blocked($"UPDATE long_term_assets SET declining_factor = 2 WHERE id = {asset.Id}");
            await Blocked($"UPDATE long_term_assets SET schedule_end_date = {new DateOnly(2030, 12, 31)} WHERE id = {asset.Id}");
            await Blocked($"UPDATE depreciation_entries SET period_start = {new DateOnly(2025, 1, 1)} WHERE id = {entry.Id}");
            Assert.Equal(before, await Snapshot());

            // Thao tác hợp lệ vẫn chạy: đổi kỳ ghi sổ của thẻ đang dùng, hủy lần chạy (ghi voided_at một lần).
            Assert.Equal(AllocationFrequency.Quarterly, (await s.Assets.SetFrequencyAsync(h.Preparer, asset.Id, AllocationFrequency.Quarterly)).AllocationFrequency);
            await s.Assets.ReverseRunAsync(h.Approver, entry.RunId!.Value, "Chạy lại sau nâng cấp");
            s.Db.ChangeTracker.Clear();
            Assert.NotNull((await s.Db.DepreciationEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id)).VoidedAt);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
