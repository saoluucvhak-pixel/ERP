using Hak.Application.Inventory;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

public class DrynessIntegrationTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;
    private static DateOnly D(int m, int d) => new(2026, m, d);

    private sealed class Fx : IDisposable
    {
        public required TestHost H;
        public required Scope S;
        public Guid Yard, Port, Dam;
        public void Dispose() { S.Dispose(); H.Dispose(); }

        public async Task<StockDocument> Receive(DateOnly date, decimal mt, decimal amount, Guid? wh = null)
        {
            var d = await S.Stock.CreateDocumentAsync(H.Preparer, new StockDocumentInput(StockDocumentType.Receipt, wh ?? Yard, date, date, "Nhập dăm",
                [new StockLineInput(Dam, mt, amount, H.Acc["AP"])], PartnerId: H.Partner["NCC01"]));
            return await S.Stock.PostDocumentAsync(H.Approver, d.Id);
        }

        public async Task<StockDocument> Ship(DateOnly date, decimal mt, Guid? wh = null)
        {
            var d = await S.Stock.CreateDocumentAsync(H.Preparer, new StockDocumentInput(StockDocumentType.Issue, wh ?? Yard, date, date, "Xuất tàu",
                [new StockLineInput(Dam, mt, CounterAccountId: H.Acc["EXP"])]));
            return await S.Stock.PostDocumentAsync(H.Approver, d.Id);
        }

        public Task<StockMeasurement> Measure(StockDocument doc, decimal dryness, EvidenceType ev, string no, decimal? mt = null)
            => S.Dryness.RecordAsync(H.Preparer, new MeasurementInput(doc.Id, 1, dryness, ev, no, doc.PostingDate, "Vinacontrol (thử)", mt));
    }

    private static async Task<Fx> SetupAsync(bool withMethod = true)
    {
        var h = await TestHost.CreateAsync();
        var s = h.NewScope();
        await s.Setup.CreateAccountAsync(h.Admin, "INV", "Hàng tồn kho (thử)");
        var fx = new Fx { H = h, S = s };
        fx.Yard = (await s.Stock.CreateWarehouseAsync(h.Admin, "BAI", "Bãi dăm")).Id;
        fx.Port = (await s.Stock.CreateWarehouseAsync(h.Admin, "CANG", "Kho cảng")).Id;
        await s.Stock.SetCostingPolicyAsync(h.Admin, fx.Yard, CostingMethod.MovingAverage, D(1, 1), "QĐ test");
        await s.Stock.SetCostingPolicyAsync(h.Admin, fx.Port, CostingMethod.MovingAverage, D(1, 1), "QĐ test");
        fx.Dam = (await s.Stock.CreateItemAsync(h.Admin, "DAM", "Dăm gỗ", "tấn", "INV")).Id;
        await s.Dryness.SetItemTracksDrynessAsync(h.Admin, fx.Dam, true);
        if (withMethod)
            await s.Dryness.CreateMethodAsync(h.Admin, "DO-KHO", "BDMT = MT × độ khô%; đo khi nhập và đo ở tàu khi xuất (chứng thư)", 2, 3, D(1, 1),
                "Doanh nghiệp xác nhận 14/10/2026");
        return fx;
    }

    [Fact]
    public async Task UnmeasuredIssuesAndTransfers_AreEstimatedAtTheAverageDrynessOfTheTime_AndReportedSeparately()
    {
        using var fx = await SetupAsync();
        var r1 = await fx.Receive(D(3, 1), 100, 150_000_000);
        var r2 = await fx.Receive(D(3, 5), 50, 80_000_000);
        await fx.Measure(r1, 52.35m, EvidenceType.InternalReport, "PĐ-001");          // 52,35 BDMT
        await fx.Measure(r2, 50m, EvidenceType.InternalReport, "PĐ-002");             // 25 BDMT → tồn 150 MT / 77,35 BDMT
        await fx.Ship(D(3, 10), 30);                                                  // xuất chưa có chứng thư
        var transfer = await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Transfer, fx.Yard, D(3, 12), D(3, 12), "Chuyển ra cảng",
            [new StockLineInput(fx.Dam, 20)], ToWarehouseId: fx.Port));
        await fx.S.Stock.PostDocumentAsync(fx.H.Approver, transfer.Id);

        var rows = (await fx.S.Dryness.SummaryAsync(fx.H.Auditor, D(3, 1), D(3, 31))).ToDictionary(r => r.WarehouseCode);
        var yard = rows["BAI"]; var port = rows["CANG"];
        // Cột chính chỉ gồm phần đã đo; phần xuất chưa đo vẫn báo là chưa đo.
        Assert.Equal((150m, 77.35m, 50m, 0m, 50m), (yard.InMt, yard.InBdmt, yard.OutMt, yard.OutBdmt, yard.UnmeasuredOutMt));
        // Tạm tính theo bình quân thời điểm: 30 MT × 77,35/150 = 15,47; 20 MT × 61,88/120 = 10,3133 (kho nhận mang đúng số đó).
        Assert.Equal((25.7833m, 100m, 51.5667m, 51.57m), (yard.EstimatedOutBdmt, yard.ClosingMt, yard.EstimatedClosingBdmt, yard.ClosingDrynessPercent));
        Assert.Equal((20m, 20m, 10.3133m, 10.3133m, 51.57m), (port.InMt, port.UnmeasuredInMt, port.EstimatedInBdmt, port.EstimatedClosingBdmt, port.ClosingDrynessPercent));

        // Có chứng thư thì số đo thay số tạm tính.
        var ship = await fx.S.Db.StockDocuments.AsNoTracking().FirstAsync(d => d.Type == StockDocumentType.Issue);
        await fx.Measure(ship, 55m, EvidenceType.Certificate, "CT-1");
        var after = (await fx.S.Dryness.SummaryAsync(fx.H.Auditor, D(3, 1), D(3, 31))).ToDictionary(r => r.WarehouseCode)["BAI"];
        Assert.Equal((16.5m, 20m), (after.OutBdmt, after.UnmeasuredOutMt));
    }

    [Fact]
    public async Task DailyRate_ActsLikeAnExchangeRate_ForUnmeasuredReceipts_AndMeasurementWins()
    {
        using var fx = await SetupAsync();
        await fx.S.Dryness.SetDailyRateAsync(fx.H.Preparer, fx.Dam, D(3, 1), 52m, "Độ khô công bố ngày 1/3");
        await fx.S.Dryness.SetDailyRateAsync(fx.H.Preparer, fx.Dam, D(3, 4), 50m, null);
        var r1 = await fx.Receive(D(3, 2), 100, 150_000_000);   // lấy độ khô ngày 1/3 (52%)
        var r2 = await fx.Receive(D(3, 6), 40, 60_000_000);     // lấy độ khô ngày gần nhất 4/3 (50%)
        var row = (await fx.S.Dryness.SummaryAsync(fx.H.Auditor, D(3, 1), D(3, 31)))[0];
        Assert.Equal((0m, 140m, 72m, 72m), (row.InBdmt, row.UnmeasuredInMt, row.EstimatedInBdmt, row.EstimatedClosingBdmt));
        // Kết quả đo thay số theo ngày.
        await fx.Measure(r2, 51m, EvidenceType.InternalReport, "PĐ-9");
        row = (await fx.S.Dryness.SummaryAsync(fx.H.Auditor, D(3, 1), D(3, 31)))[0];
        Assert.Equal((20.4m, 52m, 72.4m), (row.InBdmt, row.EstimatedInBdmt, row.EstimatedClosingBdmt));
        // Ghi lại cùng ngày thì thay số; độ khô sai bị chặn.
        await fx.S.Dryness.SetDailyRateAsync(fx.H.Preparer, fx.Dam, D(3, 1), 53m, null);
        Assert.Single((await fx.S.Dryness.DailyRatesAsync(fx.H.Auditor, fx.Dam, D(3, 1), D(3, 1))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.S.Dryness.SetDailyRateAsync(fx.H.Preparer, fx.Dam, D(3, 2), 101m, null)));
    }

    [Fact]
    public async Task ReceiptAndShipment_ProduceMtAndBdmtBalances()
    {
        using var fx = await SetupAsync();
        var r1 = await fx.Receive(D(3, 1), 100, 150_000_000);
        var r2 = await fx.Receive(D(3, 5), 50, 80_000_000);
        await fx.Measure(r1, 52.35m, EvidenceType.InternalReport, "PĐ-001");
        await fx.Measure(r2, 50m, EvidenceType.InternalReport, "PĐ-002");
        var ship = await fx.Ship(D(3, 20), 120);
        // Shipment dryness must come from the ship certificate.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.Measure(ship, 55m, EvidenceType.InternalReport, "X")));
        Assert.Contains(await fx.S.Dryness.PendingAsync(fx.H.Auditor), p => p.DocumentId == ship.Id && p.ExpectedKind == MeasurementKind.Shipment);
        var cert = await fx.Measure(ship, 55.5m, EvidenceType.Certificate, "CT-2026-0456");
        Assert.Equal(66.6m, cert.Bdmt);
        Assert.Empty(await fx.S.Dryness.PendingAsync(fx.H.Auditor));

        var row = Assert.Single(await fx.S.Dryness.SummaryAsync(fx.H.Auditor, D(3, 1), D(3, 31)));
        Assert.Equal((150m, 77.35m, 120m, 66.6m, 30m, 10.75m), (row.InMt, row.InBdmt, row.OutMt, row.OutBdmt, row.ClosingMt, row.ClosingBdmt));
        Assert.Equal((0m, 0m), (row.UnmeasuredInMt, row.UnmeasuredOutMt));

        // BDMT has no GL effect: only the stock documents' value entries exist.
        Assert.Equal(0, await fx.S.Db.Vouchers.CountAsync(v => v.Description.Contains("BDMT")));
    }

    [Fact]
    public async Task PendingList_IncludesDraftReceipts_ThatMustBeMeasuredBeforePricing()
    {
        using var fx = await SetupAsync();
        var draft = await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Receipt, fx.Yard, D(3, 1), D(3, 1), "Chờ HĐ",
            [new StockLineInput(fx.Dam, 10, 0, fx.H.Acc["AP"])], PartnerId: fx.H.Partner["NCC01"]));
        Assert.Contains(await fx.S.Dryness.PendingAsync(fx.H.Auditor), p => p.DocumentId == draft.Id);
    }

    [Fact]
    public async Task UnmeasuredLines_AreReportedNotGuessed()
    {
        using var fx = await SetupAsync();
        await fx.Receive(D(3, 1), 40, 60_000_000);
        var row = Assert.Single(await fx.S.Dryness.SummaryAsync(fx.H.Auditor, D(3, 1), D(3, 31)));
        Assert.Equal((40m, 0m, 40m), (row.InMt, row.InBdmt, row.UnmeasuredInMt));
    }

    [Fact]
    public async Task CorrectionSupersedes_KeepsHistory_AndNeedsReason()
    {
        using var fx = await SetupAsync();
        var r = await fx.Receive(D(3, 1), 10, 15_000_000);
        var first = await fx.Measure(r, 50m, EvidenceType.InternalReport, "PĐ-1");
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => fx.Measure(r, 51m, EvidenceType.InternalReport, "PĐ-1b")));
        var input = new MeasurementInput(r.Id, 1, 48.5m, EvidenceType.InternalReport, "PĐ-1-LAI", D(3, 2));
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => fx.S.Dryness.SupersedeAsync(fx.H.Preparer, first.Id, input, " ")));
        var second = await fx.S.Dryness.SupersedeAsync(fx.H.Preparer, first.Id, input, "Đo lại do mẫu lần 1 bị ướt");
        Assert.Equal(4.85m, second.Bdmt);

        var history = await fx.S.Dryness.HistoryAsync(fx.H.Auditor, r.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal("Đo lại do mẫu lần 1 bị ướt", history.Single(h => h.Id == first.Id).SupersedeReason);
        Assert.Equal(4.85m, Assert.Single(await fx.S.Dryness.SummaryAsync(fx.H.Auditor, D(3, 1), D(3, 31))).ClosingBdmt);

        foreach (var sql in new[] { "DELETE FROM stock_measurements", "UPDATE stock_measurements SET dryness_percent = 1" })
            Assert.Contains("LEDGER_IMMUTABLE", (await Assert.ThrowsAnyAsync<Exception>(() => fx.S.Db.Database.ExecuteSqlRawAsync(sql))).Message);
    }

    [Fact]
    public async Task ShipMt_MayDifferFromWarehouseMt_AndIsKeptSeparately()
    {
        using var fx = await SetupAsync();
        var r = await fx.Receive(D(3, 1), 100, 150_000_000);
        await fx.Measure(r, 50m, EvidenceType.InternalReport, "PĐ-1");
        var ship = await fx.Ship(D(3, 10), 60);
        var cert = await fx.Measure(ship, 55m, EvidenceType.Certificate, "CT-1", mt: 59.2m); // draft survey on board
        Assert.Equal(32.56m, cert.Bdmt);
        var view = (await fx.S.Dryness.HistoryAsync(fx.H.Auditor, ship.Id)).Single();
        Assert.Equal((60m, 59.2m), (view.LineMt, view.MeasuredMt));
    }

    [Fact]
    public async Task TransferAndReversal_CarryBdmtConsistently()
    {
        using var fx = await SetupAsync();
        var r = await fx.Receive(D(3, 1), 20, 30_000_000);
        await fx.Measure(r, 50m, EvidenceType.InternalReport, "PĐ-1");
        var t = await fx.S.Stock.CreateDocumentAsync(fx.H.Preparer, new StockDocumentInput(StockDocumentType.Transfer, fx.Yard, D(3, 2), D(3, 2), "Chuyển ra cảng",
            [new StockLineInput(fx.Dam, 8)], ToWarehouseId: fx.Port));
        t = await fx.S.Stock.PostDocumentAsync(fx.H.Approver, t.Id);
        await fx.Measure(t, 52m, EvidenceType.InternalReport, "PĐ-2");
        var rows = await fx.S.Dryness.SummaryAsync(fx.H.Auditor, D(3, 1), D(3, 31));
        Assert.Equal(4.16m, rows.Single(x => x.WarehouseCode == "CANG").ClosingBdmt);
        Assert.Equal(10m - 4.16m, rows.Single(x => x.WarehouseCode == "BAI").ClosingBdmt);

        var r2 = await fx.Receive(D(3, 3), 5, 7_000_000, fx.Port);
        await fx.Measure(r2, 40m, EvidenceType.InternalReport, "PĐ-3");
        await fx.S.Stock.ReverseDocumentAsync(fx.H.Approver, r2.Id, D(3, 4), "Nhập nhầm kho");
        Assert.Equal(4.16m, (await fx.S.Dryness.SummaryAsync(fx.H.Auditor, D(3, 1), D(3, 31))).Single(x => x.WarehouseCode == "CANG").ClosingBdmt);
    }

    [Fact]
    public async Task MethodRequired_Approved_AndItemMustTrackDryness()
    {
        using var fx = await SetupAsync(withMethod: false);
        var r = await fx.Receive(D(3, 1), 10, 1_000);
        Assert.Equal(ErrorCodes.CostingPolicyMissing, await CodeOf(() => fx.Measure(r, 50m, EvidenceType.InternalReport, "PĐ")));
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => fx.S.Dryness.CreateMethodAsync(fx.H.Admin, "M", "x", 2, 3, D(1, 1), "")));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => fx.S.Dryness.CreateMethodAsync(fx.H.Preparer, "M", "x", 2, 3, D(1, 1), "ok")));
        await fx.S.Dryness.CreateMethodAsync(fx.H.Admin, "DO-KHO", "x", 2, 3, D(1, 1), "Doanh nghiệp xác nhận");
        await fx.Measure(r, 50m, EvidenceType.InternalReport, "PĐ");
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => fx.S.Dryness.SetItemTracksDrynessAsync(fx.H.Admin, fx.Dam, false)));
    }
}
