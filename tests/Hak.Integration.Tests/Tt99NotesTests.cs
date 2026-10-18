using Hak.Application.Accounting;
using Hak.Application.Assets;
using Hak.Application.Reports;
using Hak.Domain.Assets;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>
/// Thuyết minh BCTC (B09-DN) templates from legal/TT99-2025 run on the TT99 chart: multi-column tables (TSCĐ theo nhóm, thuế,
/// vay, vốn chủ sở hữu), the asset module's depreciation and disposal vouchers, and single-column notes VII / VIII.
/// </summary>
public class Tt99NotesTests
{
    [Fact]
    public async Task MultiColumnTemplates_RefuseWrongSlotCounts_AndRoundTrip()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        async Task<string> Refused(ReportDefinitionInput input) =>
            (await Assert.ThrowsAsync<Hak.Domain.Common.DomainException>(() => s.Reports.SaveDraftAsync(h.Preparer, input))).Message;
        Assert.Contains("2 phần", await Refused(new("X", "X", "T", null, [new("1", "a", "BAL_D(CASH) | OPEN_D(CASH)")], null, ["A", "B", "C"])));
        Assert.Contains("không khai báo cột", await Refused(new("X", "X", "T", null, [new("1", "a", "BAL_D(CASH) | OPEN_D(CASH)")])));
        Assert.Contains("[B]", await Refused(new("X", "X", "T", null, [new("1", "a", "BAL_D(CASH) | LINE(9)")], null, ["A", "B"])));

        var def = await s.Reports.SaveDraftAsync(h.Preparer, new("X", "X", "T", null,
            [new("1", "Tiền", "BAL_D(CASH) | OPEN_D(CASH)"), new("2", "Cộng", "LINE(1) * 2")], null, ["Cuối kỳ", "Đầu kỳ"]));
        Assert.Equal(["Cuối kỳ", "Đầu kỳ"], (await s.Reports.ExportAsync(h.Auditor, def.Id)).Columns!);
        var run = await s.Reports.RunAsync(h.Auditor, def.Id, new(2026, 1, 1), new(2026, 12, 31), new(2025, 1, 1), new(2025, 12, 31));
        Assert.Equal(2, run.Lines[1].Values!.Count);
        Assert.Equal(2, run.Lines[1].CompareValues!.Count);
        Assert.Null(run.Lines[1].Value);
    }

    [Fact]
    public async Task B09Templates_Validate_Run_AndTieOutToTheLedger()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var csv = File.ReadAllLines(Path.Combine(ReportEngineTests.LegalDir(), "nhap-he-thong-tk-de-xuat.csv")).Skip(1).Select(l => l.Split(';'))
            .Select((c, i) => new Hak.Application.Setup.AccountImportRow(i + 2, c[0], c[1], c[2], c[3], c[4], c[5], c[6], c[7], c[8])).ToList();
        Assert.True((await s.CatalogImport.ImportAccountsAsync(h.Admin, csv, commit: true)).Committed);
        foreach (var (code, name, tags) in new[]
                 {
                     ("21101", "Nhà cửa (thử)", "NHA"), ("21102", "Máy móc (thử)", "MAY"), ("21411", "Hao mòn nhà cửa (thử)", "NHA"),
                     ("21412", "Hao mòn máy móc (thử)", "MAY"), ("8118", "Giá trị còn lại TSCĐ thanh lý (thử)", "TLTS"),
                 })
        {
            var a = await s.Setup.CreateAccountAsync(h.Admin, code, name);
            await s.Setup.SetAccountTagsAsync(h.Admin, a.Id, tags);
        }
        var acc = await s.Db.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Code, a => a.Id);
        VoucherLineInput D(string a, decimal x, string? p = null) => new(acc[a], Side.Debit, x, p is null ? null : h.Partner[p]);
        VoucherLineInput C(string a, decimal x, string? p = null) => new(acc[a], Side.Credit, x, p is null ? null : h.Partner[p]);
        async Task<Voucher> Post(int month, int day, string type, params VoucherLineInput[] lines) =>
            await h.PostedAsync(s, new VoucherInput(type, new(2026, month, day), new(2026, month, day), "Nghiệp vụ mẫu", lines));
        async Task<Guid> DebitLine(Voucher v, string account) => await (from l in s.Db.JournalLines join e in s.Db.JournalEntries on l.JournalEntryId equals e.Id
            where e.VoucherId == v.Id && l.AccountId == acc[account] && l.Side == Side.Debit select l.Id).SingleAsync();
        Task<Guid> Period(int month) => s.Db.AccountingPeriods.Where(p => p.CompanyId == h.CompanyId && p.StartDate == new DateOnly(2026, month, 1)).Select(p => p.Id).SingleAsync();
        const string G = VoucherTypes.General;

        await Post(1, 2, G, D("112", 5_000), C("41111", 5_000));                                     // góp vốn
        var building = await Post(1, 5, G, D("21101", 1_200), C("331", 1_200, "NCC01"));              // mua nhà (chưa trả)
        var machine = await Post(1, 5, G, D("21102", 600), C("112", 600));                            // mua máy (trả tiền)
        async Task<LongTermAsset> Asset(string code, string cost, string accumulated, Guid line, decimal amount, int months)
        {
            var a = await s.Assets.CreateAsync(h.Preparer, new AssetInput(code, $"Tài sản {code}", AssetClass.FixedAsset, acc[cost], acc[accumulated], acc["6424"],
                ProrationPolicy.FullMonth, 0, months, new(2026, 1, 15), [new AssetCostSourceInput(line, amount)]));
            return await s.Assets.ActivateAsync(h.Approver, a.Id);
        }
        await Asset("NHA01", "21101", "21411", await DebitLine(building, "21101"), 1_200, 120);      // 10 / tháng
        var may = await Asset("MAY01", "21102", "21412", await DebitLine(machine, "21102"), 600, 60); // 10 / tháng
        for (var m = 1; m <= 3; m++) await s.Assets.RunAsync(h.Approver, await Period(m));
        await s.Assets.DisposeAsync(h.Approver, may.Id, new(2026, 4, 10), acc["8118"], "Thanh lý máy");  // HM 30, GTCL 570
        for (var m = 4; m <= 12; m++) await s.Assets.RunAsync(h.Approver, await Period(m));
        await Post(2, 10, G, D("131", 1_100, "KH01"), C("511", 1_000), C("33311", 100));              // bán chịu
        await Post(3, 10, G, D("33311", 60), C("112", 60));                                          // nộp thuế GTGT
        await Post(6, 10, G, D("112", 500), C("3411", 500));                                         // vay
        await Post(11, 10, G, D("3411", 200), C("112", 200));                                        // trả gốc vay
        await Post(12, 31, VoucherTypes.Closing, D("511", 1_000), C("911", 1_000), D("911", 720), C("6424", 150), C("8118", 570),
            D("911", 280), C("4212", 280));                                                          // kết chuyển: lãi 280

        var runs = new Dictionary<string, ReportRun>();
        foreach (var file in Directory.GetFiles(ReportEngineTests.LegalDir(), "mau-B09-DN-*.json"))
        {
            var input = System.Text.Json.JsonSerializer.Deserialize<ReportDefinitionInput>(File.ReadAllText(file), ReportService.Json)!;
            var def = await s.Reports.SaveDraftAsync(h.Preparer, input);
            var run = await s.Reports.RunAsync(h.Auditor, def.Id, new(2026, 1, 1), new(2026, 12, 31));
            Assert.All(run.Lines, l => Assert.True(l.Error is null, $"{input.Code} {l.Code}: {l.Error}"));
            Assert.All(run.Checks, c => Assert.True(c.Passed, $"{input.Code}: {c.Message} {c.Left} ≠ {c.Right}"));
            runs[input.Code] = run;
        }
        Assert.Equal(8, runs.Count);
        decimal?[] Row(string report, string code) => runs[report].Lines.Single(l => l.Code == code) is var l && l.Values is { } v ? [.. v] : [l.Value];

        // V.9 TSCĐ hữu hình — cột: Nhà cửa | Máy móc | Phương tiện | Cây lâu năm | Khác | Tổng cộng.
        Assert.Equal(["Nhà cửa, vật kiến trúc", "Máy móc, thiết bị", "Phương tiện vận tải, truyền dẫn", "Cây lâu năm cho sản phẩm định kỳ", "TSCĐ hữu hình khác", "Tổng cộng"],
            runs["B09-DN-V9"].Columns!);
        Assert.Equal([1_200m, 600m, 0m, 0m, 0m, 1_800m], Row("B09-DN-V9", "V9.NGT1"));      // mua trong năm
        Assert.Equal([0m, 600m, 0m, 0m, 0m, 600m], Row("B09-DN-V9", "V9.NGG2"));            // thanh lý (chứng từ FAD)
        Assert.Equal([0m, 0m, 0m, 0m, 0m, 0m], Row("B09-DN-V9", "V9.NGG3"));
        Assert.Equal([1_200m, 0m, 0m, 0m, 0m, 1_200m], Row("B09-DN-V9", "V9.NG9"));
        Assert.Equal([120m, 30m, 0m, 0m, 0m, 150m], Row("B09-DN-V9", "V9.HMT1"));           // khấu hao trong năm
        Assert.Equal([0m, 30m, 0m, 0m, 0m, 30m], Row("B09-DN-V9", "V9.HMG2"));
        Assert.Equal([1_080m, 0m, 0m, 0m, 0m, 1_080m], Row("B09-DN-V9", "V9.CL9"));

        // V.19 thuế — Đầu năm | Số phải nộp | Số đã thực nộp | Cuối năm.
        Assert.Equal([0m, 100m, 60m, 40m], Row("B09-DN-V19", "V19.3331"));
        // V.16 vay — Đầu năm | Tăng | Giảm | Cuối năm.
        Assert.Equal([0m, 500m, 200m, 300m], Row("B09-DN-V-BD", "V16.A"));
        // V.27a vốn chủ sở hữu — cột 1 vốn góp, cột 7 LNST chưa phân phối, cột 9 cộng.
        var eq = runs["B09-DN-V27"];
        decimal? Eq(string code, int col) => eq.Lines.Single(l => l.Code == code).Values![col];
        Assert.Equal((5_000m, 280m, 5_280m), (Eq("V27A.9", 0), Eq("V27A.9", 6), Eq("V27A.9", 8)));
        Assert.Equal((280m, 0m, 5_000m), (Eq("V27A.2", 6), Eq("V27A.3", 6), Eq("V27A.1", 8)));
        // V số dư — Cuối năm | Đầu năm.
        Assert.Equal([4_640m, 0m], Row("B09-DN-V", "V1.2"));
        Assert.Equal([1_100m, 0m], Row("B09-DN-V", "V3.1"));
        Assert.Equal([1_200m, 0m], Row("B09-DN-V", "V17.1"));
        // VII — một cột (Năm nay).
        Assert.Equal([1_000m], Row("B09-DN-VII", "VII1.1"));
        Assert.Equal([570m], Row("B09-DN-VII", "VII8.1"));
        Assert.Equal([150m], Row("B09-DN-VII", "VII10.3"));
        Assert.Equal([280m], Row("B09-DN-VII", "VII11.1"));
        Assert.Equal([500m], Row("B09-DN-VII", "VIII3"));
        Assert.Equal([200m], Row("B09-DN-VII", "VIII4"));
    }
}
