using Hak.Application.Tax;
using Hak.Application.Trade;
using Hak.Domain.Common;
using Hak.Domain.Tax;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

public class VatTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;
    private static DateOnly D(int m, int d) => new(2026, m, d);
    private const string Own = "0101234567";
    private const string Supplier = "0309999999";

    private sealed class Fx : IDisposable
    {
        public required TestHost H;
        public required Scope S;
        public required InvoiceService Inv;
        public required VatService Vat;
        public Guid Sup, SupNoMst, Cus;
        public void Dispose() { S.Dispose(); H.Dispose(); }
    }

    private static async Task<Fx> SetupAsync()
    {
        var h = await TestHost.CreateAsync();
        var s = h.NewScope();
        await s.Setup.UpdateCompanyProfileAsync(h.AdminId, h.CompanyId, "Công ty kiểm thử", Own);
        foreach (var (code, name) in new[] { ("VATIN", "Thuế GTGT đầu vào"), ("VATOUT", "Thuế GTGT đầu ra"), ("SALESREV", "Doanh thu"), ("GRNI", "Hàng về chưa có HĐ") })
            h.Acc[code] = (await s.Setup.CreateAccountAsync(h.Admin, code, name + " (thử)")).Id;
        foreach (var (role, code) in new[] { ("AR_TRADE", "AR"), ("AP_TRADE", "AP"), ("VAT_INPUT", "VATIN"), ("VAT_OUTPUT", "VATOUT"), ("SALES_REVENUE", "SALESREV"), ("GRNI", "GRNI") })
            await s.Rules.SetRoleMappingAsync(h.Admin, role, code);
        var fx = new Fx { H = h, S = s, Inv = new InvoiceService(s.Db, s.Engine, s.Stock, s.Audit, h.Clock), Vat = new VatService(s.Db, s.Audit, h.Clock) };
        fx.Sup = (await s.Setup.CreatePartnerAsync(h.Admin, "NCC-MST", "NCC có MST", Domain.Catalog.PartnerRoles.Supplier, Supplier)).Id;
        fx.SupNoMst = h.Partner["NCC01"];
        fx.Cus = (await s.Setup.CreatePartnerAsync(h.Admin, "KH-MST", "Khách có MST", Domain.Catalog.PartnerRoles.Customer, "0207777777")).Id;
        return fx;
    }

    private static async Task<CommercialInvoice> Book(Fx fx, InvoiceKind kind, Guid partner, string series, string no, decimal amount, decimal vatRate, DateOnly date)
    {
        var acc = kind == InvoiceKind.Purchase ? fx.H.Acc["EXP"] : fx.H.Acc["SALESREV"];
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(kind, partner, series, no, date, date, "Dịch vụ",
            [new InvoiceLineInput(amount, Quantity: 1, AccountId: acc, VatRate: vatRate)]));
        return await fx.Inv.PostAsync(fx.H.Approver, inv.Id);
    }

    private static EInvoiceInput EIn(string seller, string series, string no, decimal amount, decimal vat, DateOnly date, EInvoiceStatus st = EInvoiceStatus.Original, decimal rate = 10)
        => new(TaxDirection.Input, seller, "Người bán", series, no, date, amount, vat, amount + vat, st, [new EInvoiceRateInput(rate, amount, vat)], BuyerTaxCode: Own);

    private static EInvoiceInput EOut(string series, string no, decimal amount, decimal vat, DateOnly date, decimal rate = 8)
        => new(TaxDirection.Output, Own, "Công ty kiểm thử", series, no, date, amount, vat, amount + vat, EInvoiceStatus.Original, [new EInvoiceRateInput(rate, amount, vat)], BuyerTaxCode: "0207777777");

    [Fact]
    public async Task ThreeWayReconciliation_ListsEveryDiscrepancyWithAction()
    {
        using var fx = await SetupAsync();
        await Book(fx, InvoiceKind.Purchase, fx.Sup, "1C26TAA", "0000123", 10_000_000, 10, D(3, 5));      // clean match
        await Book(fx, InvoiceKind.Purchase, fx.Sup, "1C26TAA", "0000124", 2_000_000, 10, D(3, 6));       // cancelled on e-invoice system
        await Book(fx, InvoiceKind.Purchase, fx.SupNoMst, "1C26TXX", "0000009", 500_000, 10, D(3, 7));    // supplier without tax code
        await Book(fx, InvoiceKind.Purchase, fx.Sup, "1C26TAA", "0000130", 3_000_000, 10, D(4, 2));       // issued in March, booked in April
        await Book(fx, InvoiceKind.Sales, fx.Cus, "1C26TBB", "0000001", 20_000_000, 8, D(3, 10));         // VAT differs on e-invoice
        await Book(fx, InvoiceKind.Sales, fx.Cus, "1C26TBB", "0000002", 5_000_000, 8, D(3, 11));          // no e-invoice data

        var r = await fx.Vat.ImportEInvoicesAsync(fx.H.Approver, "hddt-03.json", "file-1", [
            EIn(Supplier, "1C26TAA", "0000123", 10_000_000, 1_000_000, D(3, 5)),
            EIn(Supplier, "1C26TAA", "0000124", 2_000_000, 200_000, D(3, 6), EInvoiceStatus.Cancelled),
            EIn(Supplier, "1C26TAA", "0000125", 7_000_000, 700_000, D(3, 8)),                             // not booked
            EIn(Supplier, "1C26TAA", "0000130", 3_000_000, 300_000, D(3, 30)),
            EOut("1C26TBB", "0000001", 20_000_000, 1_700_000, D(3, 10)),
            EOut("1C26TBB", "0000003", 1, 1, D(3, 12)) with { TotalPayment = 3 },                          // rejected: totals do not add up
            EOut("1C26TBB", "0000004", 1, 0, D(3, 12)) with { SellerTaxCode = "0999999999" },              // rejected: not our own invoice
        ]);
        Assert.Equal((5, 0, 2), (r.Imported, r.Duplicates, r.Rejected.Count));

        var rec = await fx.Vat.ReconcileAsync(fx.H.Auditor, D(3, 1), D(3, 31));
        var types = rec.Differences.Select(d => d.Type).ToList();
        Assert.Contains(TaxDiffType.CancelledButRecorded, types);
        Assert.Contains(TaxDiffType.MissingInBooks, types);
        Assert.Contains(TaxDiffType.PartnerTaxCodeMissing, types);
        Assert.Contains(TaxDiffType.PeriodDifference, types);
        Assert.Contains(TaxDiffType.VatDifference, types);
        Assert.Contains(TaxDiffType.MissingEInvoice, types);
        var vat = rec.Differences.Single(d => d.Type == TaxDiffType.VatDifference);
        Assert.Equal((1_700_000m, 1_600_000m, -100_000m), (vat.EInvoiceVat!.Value, vat.BooksVat!.Value, vat.Difference));
        Assert.All(rec.Differences, d => Assert.False(string.IsNullOrWhiteSpace(d.Suggestion)));
        Assert.Equal(3, rec.Matched); // 0000123, 0000130 (period diff), 0000001 (VAT diff)
    }

    [Fact]
    public async Task Import_IsIdempotentPerFile_AndRecordsLaterStatusChanges()
    {
        using var fx = await SetupAsync();
        await fx.Vat.ImportEInvoicesAsync(fx.H.Approver, "a.json", "A", [EIn(Supplier, "1C26TAA", "1", 100, 10, D(3, 1))]);
        Assert.Equal(ErrorCodes.PackageReplayed, await CodeOf(() => fx.Vat.ImportEInvoicesAsync(fx.H.Approver, "a.json", "A", [])));
        var again = await fx.Vat.ImportEInvoicesAsync(fx.H.Approver, "b.json", "B", [EIn(Supplier, "1C26TAA", "1", 100, 10, D(3, 1))]);
        Assert.Equal((0, 1, 0), (again.Imported, again.Duplicates, again.StatusChanges));
        var cancel = await fx.Vat.ImportEInvoicesAsync(fx.H.Approver, "c.json", "C", [EIn(Supplier, "1C26TAA", "1", 100, 10, D(3, 1), EInvoiceStatus.Cancelled)]);
        Assert.Equal(1, cancel.StatusChanges);
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => fx.Vat.ImportEInvoicesAsync(fx.H.Auditor, "d.json", "D", [])));
        Assert.Contains("LEDGER_IMMUTABLE", (await Assert.ThrowsAnyAsync<Exception>(() =>
            Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(fx.S.Db.Database, "UPDATE e_invoice_records SET vat_amount = 0"))).Message);
    }

    [Fact]
    public async Task Declaration_ComparedWithBooks_AndFrozenOnceFiled()
    {
        using var fx = await SetupAsync();
        await Book(fx, InvoiceKind.Purchase, fx.Sup, "1C26TAA", "0000123", 10_000_000, 10, D(3, 5));
        await Book(fx, InvoiceKind.Sales, fx.Cus, "1C26TBB", "0000001", 20_000_000, 8, D(3, 10));
        await fx.Vat.ImportEInvoicesAsync(fx.H.Approver, "x.json", "X", [EIn(Supplier, "1C26TAA", "0000123", 10_000_000, 1_000_000, D(3, 5)), EOut("1C26TBB", "0000001", 20_000_000, 1_600_000, D(3, 10))]);
        Assert.True((await fx.Vat.ReconcileAsync(fx.H.Auditor, D(3, 1), D(3, 31))).IsClean);

        var decl = await fx.Vat.SaveDeclarationAsync(fx.H.Admin, D(3, 1), D(3, 31), [
            new DeclarationLineInput("IN-VAT", "Thuế GTGT đầu vào được khấu trừ (nhập theo mẫu đang dùng)", 1_000_000, TaxMeasure.InputVat),
            new DeclarationLineInput("OUT-8", "Thuế GTGT đầu ra 8%", 1_500_000, TaxMeasure.OutputVat, 8),
        ]);
        var rec = await fx.Vat.ReconcileAsync(fx.H.Auditor, D(3, 1), D(3, 31));
        var d = Assert.Single(rec.Differences);
        Assert.Equal((TaxDiffType.DeclarationDifference, 100_000m), (d.Type, d.Difference));

        var summary = await fx.Vat.PayableSummaryAsync(fx.H.Auditor, D(3, 1), D(3, 31));
        Assert.Equal((1_600_000m, 1_000_000m, 600_000m), (summary.OutputVat, summary.InputVat, summary.NetPayable));

        await fx.Vat.MarkFiledAsync(fx.H.Admin, decl.Id, "11320260300001", D(4, 20));
        Assert.Equal(ErrorCodes.LedgerImmutable, await CodeOf(() => fx.Vat.SaveDeclarationAsync(fx.H.Admin, D(3, 1), D(3, 31), [new DeclarationLineInput("X", "x", 1)])));
    }

    [Fact]
    public async Task PeriodEndOffset_DebitsOutputVat_CreditsInputVat_ForTheSmallerBalance_AndIsOncePerPeriod()
    {
        using var fx = await SetupAsync();
        var offset = new VatOffsetService(fx.S.Db, fx.S.Engine, fx.S.Audit);
        var period = await fx.S.Db.AccountingPeriods.AsNoTracking().FirstAsync(p => p.CompanyId == fx.H.CompanyId && p.StartDate == D(3, 1));
        await Book(fx, InvoiceKind.Purchase, fx.Sup, "1C26TAA", "0000901", 10_000_000, 10, D(3, 5));    // input VAT 1.000.000
        await Book(fx, InvoiceKind.Sales, fx.Cus, "1C26TBB", "0000902", 20_000_000, 8, D(3, 10));        // output VAT 1.600.000

        var preview = await offset.PreviewAsync(fx.H.Auditor, period.Id);
        Assert.Equal((1_000_000m, 1_600_000m, 1_000_000m, 600_000m, 0m), (preview.InputBalance, preview.OutputBalance, preview.OffsetAmount, preview.NetPayable, preview.CarryForward));
        Assert.Contains(preview.Lines, l => l.AccountCode == "VATOUT" && l.Side == Side.Debit && l.Amount == 1_000_000m);
        Assert.Contains(preview.Lines, l => l.AccountCode == "VATIN" && l.Side == Side.Credit && l.Amount == 1_000_000m);

        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => offset.GenerateAsync(fx.H.Auditor, period.Id)));
        var voucher = await offset.GenerateAsync(fx.H.Preparer, period.Id);
        Assert.Equal(VoucherStatus.Draft, voucher.Status);
        Assert.Equal(D(3, 31), voucher.PostingDate);
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => offset.GenerateAsync(fx.H.Preparer, period.Id)));

        await fx.S.Engine.SubmitAsync(fx.H.Preparer, voucher.Id);
        await fx.S.Engine.ApproveAsync(fx.H.Approver, voucher.Id);
        await fx.S.Engine.PostAsync(fx.H.Approver, voucher.Id);

        var after = await offset.PreviewAsync(fx.H.Auditor, period.Id);
        Assert.Equal((0m, 600_000m, 0m), (after.InputBalance, after.OutputBalance, after.OffsetAmount));
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => offset.GenerateAsync(fx.H.Preparer, period.Id)));
    }

    [Fact]
    public async Task PeriodEndOffset_UsesTheAccountsTheUserPicks_AndRejectsOverlap()
    {
        using var fx = await SetupAsync();
        var offset = new VatOffsetService(fx.S.Db, fx.S.Engine, fx.S.Audit);
        var period = await fx.S.Db.AccountingPeriods.AsNoTracking().FirstAsync(p => p.CompanyId == fx.H.CompanyId && p.StartDate == D(3, 1));
        await Book(fx, InvoiceKind.Purchase, fx.Sup, "1C26TAA", "0000911", 10_000_000, 10, D(3, 5));
        await Book(fx, InvoiceKind.Sales, fx.Cus, "1C26TBB", "0000912", 20_000_000, 8, D(3, 10));
        var vatIn = await fx.S.Db.Accounts.AsNoTracking().FirstAsync(a => a.CompanyId == fx.H.CompanyId && a.Code == "VATIN");
        var vatOut = await fx.S.Db.Accounts.AsNoTracking().FirstAsync(a => a.CompanyId == fx.H.CompanyId && a.Code == "VATOUT");

        var picked = await offset.PreviewAsync(fx.H.Auditor, period.Id, [vatIn.Id], [vatOut.Id]);
        Assert.Equal(1_000_000m, picked.OffsetAmount);
        Assert.Equal(["VATOUT"], picked.OutputAccounts.Select(a => a.Code));
        Assert.Equal(["VATIN"], picked.InputAccounts.Select(a => a.Code));

        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => offset.PreviewAsync(fx.H.Auditor, period.Id, [vatIn.Id], [vatIn.Id])));
        Assert.Equal(ErrorCodes.NotFound, await CodeOf(() => offset.PreviewAsync(fx.H.Auditor, period.Id, [vatIn.Id], [Guid.NewGuid()])));
    }

    [Fact]
    public async Task PeriodEndOffset_Quarterly_CoversTheCalendarQuarter_AndSharesTheOncePerPeriodGuard()
    {
        using var fx = await SetupAsync();
        var offset = new VatOffsetService(fx.S.Db, fx.S.Engine, fx.S.Audit);
        var jan = await fx.S.Db.AccountingPeriods.AsNoTracking().FirstAsync(p => p.CompanyId == fx.H.CompanyId && p.StartDate == D(1, 1));
        var mar = await fx.S.Db.AccountingPeriods.AsNoTracking().FirstAsync(p => p.CompanyId == fx.H.CompanyId && p.StartDate == D(3, 1));
        await Book(fx, InvoiceKind.Purchase, fx.Sup, "1C26TAA", "0000921", 10_000_000, 10, D(1, 15));
        await Book(fx, InvoiceKind.Sales, fx.Cus, "1C26TBB", "0000922", 20_000_000, 8, D(3, 10));

        var q = await offset.PreviewAsync(fx.H.Auditor, jan.Id, quarterly: true);   // any month of the quarter selects the same quarter
        Assert.Equal((D(1, 1), D(3, 31), mar.Id, 1_000_000m), (q.PeriodStart, q.PeriodEnd, q.PeriodId, q.OffsetAmount));
        Assert.Equal(0m, (await offset.PreviewAsync(fx.H.Auditor, jan.Id)).OffsetAmount);   // by month January has no output VAT yet

        var voucher = await offset.GenerateAsync(fx.H.Preparer, jan.Id, quarterly: true);
        Assert.Equal(D(3, 31), voucher.PostingDate);
        Assert.Contains("quý 1/", voucher.Description);
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => offset.GenerateAsync(fx.H.Preparer, mar.Id)));
    }

    [Fact]
    public async Task AddingMissingPartnerTaxCode_MakesTheInvoiceMatch()
    {
        using var fx = await SetupAsync();
        await Book(fx, InvoiceKind.Purchase, fx.SupNoMst, "1C26TAA", "0000777", 1_000_000, 10, D(3, 5));
        await fx.Vat.ImportEInvoicesAsync(fx.H.Approver, "m.json", "M", [EIn("0311111111", "1C26TAA", "0000777", 1_000_000, 100_000, D(3, 5))]);
        Assert.Contains((await fx.Vat.ReconcileAsync(fx.H.Auditor, D(3, 1), D(3, 31))).Differences, d => d.Type == TaxDiffType.PartnerTaxCodeMissing);
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => fx.S.Setup.UpdatePartnerAsync(fx.H.Auditor, fx.SupNoMst, "NCC 01", "0311111111", true)));
        await fx.S.Setup.UpdatePartnerAsync(fx.H.Admin, fx.SupNoMst, "Nhà cung cấp 1", "0311111111", true);
        Assert.True((await fx.Vat.ReconcileAsync(fx.H.Auditor, D(3, 1), D(3, 31))).IsClean);
    }
}