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
}
