using Hak.Application.Accounting;
using Hak.Application.EInvoice;
using Hak.Application.Tax;
using Hak.Application.Trade;
using Hak.Domain.Common;
using Hak.Domain.Organization;
using Hak.Domain.Tax;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

public partial class TradeTests
{
    [Fact]
    public void Fix26_LegacyUnsignedEvidenceNeedsIndependentNature()
    {
        var legacy = new EInvoiceRecord { AmountBeforeVat = 100_000, VatAmount = 10_000, TotalPayment = 110_000 };
        Assert.Null(EInvoiceAmounts.SignedAdjustment(legacy));
        Assert.Equal((-100_000m, -10_000m), EInvoiceAmounts.SignedAdjustment(legacy, EInvoiceAdjustNature.Decrease));
        Assert.Equal((100_000m, 10_000m), EInvoiceAmounts.SignedAdjustment(legacy, EInvoiceAdjustNature.Increase));
        Assert.Equal(100_000, legacy.AmountBeforeVat);
    }

    [Fact]
    public async Task Fix26_LockedPeriodPreventsExternalIssue()
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var draft = await AwaitingSaleAsync(fx);
        await fx.S.Db.AccountingPeriods.Where(p => p.CompanyId == fx.H.CompanyId && p.PeriodNo == 3).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PeriodStatus.Locked));
        Assert.Equal(ErrorCodes.PeriodLocked, await CodeOf(() => svc.IssueAsync(fx.H.Approver, draft.Id)));
        Assert.Equal(0, sim.AcceptedCount);
        Assert.Empty(await fx.S.Db.EInvoiceDocuments.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fix26_SettlementSnapshotAndPreviewSurviveLateInvoicesAndCannotBeRewritten(bool byVoucher)
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        await PostedSaleAsync(fx, "SNAP-1", 10, D(3, 3));
        var service = new DiscountAgreementService(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock, fx.S.Engine);
        var a = await service.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "SNAP", "Snapshot", D(3, 1), D(3, 31), [new(0, 2)]));
        var run = byVoucher ? await service.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1))
            : await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T", "SNAP-ADJ", D(4, 1), D(4, 1));
        var before = await service.StatementAsync(fx.H.Auditor, a.Id);
        Assert.Single(before.Rows);
        Assert.True(before.SnapshotAvailable);
        Assert.Equal(20_000, before.TotalDiscount);
        if (!byVoucher) Assert.Equal(2_000, before.Rows[0].AdjustmentVat);
        await PostedSaleAsync(fx, "SNAP-2", 10, D(3, 20));
        fx.S.Db.ChangeTracker.Clear();
        var after = await service.StatementAsync(fx.H.Auditor, a.Id);
        Assert.Equal(before.TotalDiscount, after.TotalDiscount);
        Assert.Equal(before.Rows, after.Rows);
        var preview = await service.PreviewAsync(fx.H.Auditor, a.Id);
        Assert.Equal(run.Amount, preview.Amount);
        Assert.Single(preview.Invoices);
        var stored = await fx.S.Db.DiscountAgreementRuns.SingleAsync(r => r.Id == run.Id);
        stored.SettlementSnapshot = "{}";
        Assert.Equal(ErrorCodes.LedgerImmutable, await CodeOf(() => fx.S.Db.SaveChangesAsync()));
        fx.S.Db.ChangeTracker.Clear();
        await Assert.ThrowsAnyAsync<Exception>(() => fx.S.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE discount_agreement_runs SET amount = amount + 1 WHERE id = {run.Id}"));
        await service.VoidRunAsync(fx.H.Preparer, a.Id, "Test cancellation keeps frozen evidence");
        Assert.NotNull((await fx.S.Db.DiscountAgreementRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id)).VoidedAt);
    }

    [Fact]
    public async Task Fix26_LegacySettlementDoesNotInventHistoricalDetail()
    {
        using var fx = await SetupAsync();
        var service = new DiscountAgreementService(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock, fx.S.Engine);
        var a = await service.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "LEGACY", "Legacy", D(3, 1), D(3, 31), [new(0, 2)]));
        fx.S.Db.DiscountAgreementRuns.Add(new DiscountAgreementRun { Id = Ids.New(), CompanyId = fx.H.CompanyId, AgreementId = a.Id, Amount = 20_000, EligibleSales = 1_000_000, Percent = 2, CreatedBy = fx.H.Preparer.UserId, CreatedAt = fx.H.Clock.UtcNow });
        await fx.S.Db.SaveChangesAsync();
        var statement = await service.StatementAsync(fx.H.Auditor, a.Id);
        Assert.False(statement.SnapshotAvailable);
        Assert.NotEmpty(statement.Warning!);
        Assert.Empty(statement.Rows);
        Assert.Equal(20_000, statement.TotalDiscount);
    }

    [Theory]
    [InlineData(EInvoiceAmountConvention.Signed, EInvoiceAdjustNature.None, 100_000, true)]
    [InlineData(EInvoiceAmountConvention.Signed, EInvoiceAdjustNature.None, -100_000, false)]
    [InlineData(EInvoiceAmountConvention.MagnitudeWithNature, EInvoiceAdjustNature.Decrease, 100_000, false)]
    [InlineData(EInvoiceAmountConvention.MagnitudeWithNature, EInvoiceAdjustNature.Increase, 100_000, true)]
    public async Task Fix26_ReconciliationDistinguishesIncreaseFromDecrease(EInvoiceAmountConvention convention, EInvoiceAdjustNature nature, decimal amount, bool difference)
    {
        var (fx, _, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var original = await IssuedSaleAsync(fx, svc);
        var adj = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(original.InvoiceId!.Value, InvoicePurpose.Discount, "T", "SIGNED", D(3, 22), D(3, 22), "Rebate", [new(1, Amount: 100_000)], DiscountKind.Trade));
        await fx.Inv.PostAsync(fx.H.Approver, adj.Id);
        var vat = new VatService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        await vat.ImportEInvoicesAsync(fx.H.Approver, "sign fixture", "signs", [new EInvoiceInput(TaxDirection.Output, "0312345678", "Company", "T", "SIGNED", D(3, 22), amount, amount / 10, amount * 1.1m, EInvoiceStatus.Adjustment, [], AmountConvention: convention, AdjustmentNature: nature)]);
        var result = await vat.ReconcileAsync(fx.H.Auditor, D(3, 1), D(3, 31));
        Assert.Equal(difference, result.Differences.Any(d => d.InvoiceId == adj.Id && d.Type == TaxDiffType.AmountDifference));
        Assert.Equal(difference, result.Differences.Any(d => d.InvoiceId == adj.Id && d.Type == TaxDiffType.VatDifference));
    }

    [Fact]
    public async Task Fix26_SignedSupplierDecreaseCanCreateDraftButCannotBeTurnedIntoIncrease()
    {
        var (fx, _, _, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        await fx.S.Db.BusinessPartners.Where(p => p.Id == fx.H.Partner["NCC01"]).ExecuteUpdateAsync(s => s.SetProperty(p => p.TaxCode, "0400111222"));
        await PostedPurchaseOfAdditiveAsync(fx, "SUP-ORIG");
        var vat = new VatService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        await vat.ImportEInvoicesAsync(fx.H.Approver, "supplier", "signed-supplier", [SupplierInvoice("SUP-NEG", EInvoiceStatus.Adjustment, "SUP-ORIG", -100_000, -10_000) with { AmountConvention = EInvoiceAmountConvention.Signed, AdjustmentNature = EInvoiceAdjustNature.Decrease }]);
        var record = await fx.S.Db.EInvoiceRecords.AsNoTracking().SingleAsync();
        var inbound = new EInvoiceInboundService(fx.S.Db, fx.Inv, fx.S.Audit);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => inbound.CreateAdjustmentDraftAsync(fx.H.Preparer, record.Id, InvoicePurpose.Increase, null, D(3, 22))));
        var draft = await inbound.CreateAdjustmentDraftAsync(fx.H.Preparer, record.Id, InvoicePurpose.Discount, DiscountKind.Trade, D(3, 22));
        Assert.Equal(100_000, draft.Lines.Sum(l => l.FunctionalAmount));
        await fx.Inv.PostAsync(fx.H.Approver, draft.Id);
        var reconciliation = await vat.ReconcileAsync(fx.H.Auditor, D(3, 1), D(3, 31));
        Assert.DoesNotContain(reconciliation.Differences, d => d.InvoiceId == draft.Id && (d.Type == TaxDiffType.AmountDifference || d.Type == TaxDiffType.VatDifference));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Theory]
    [InlineData(500_000)]
    [InlineData(1_100_000)]
    public async Task Fix26_ReplacementCarriesDomesticPaymentsAndLeavesExcessUnapplied(decimal payment)
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var original = await IssuedSaleAsync(fx, svc);
        var oldLine = await (from l in fx.S.Db.JournalLines join e in fx.S.Db.JournalEntries on l.JournalEntryId equals e.Id where e.VoucherId == (fx.S.Db.CommercialInvoices.Where(i => i.Id == original.InvoiceId).Select(i => i.VoucherId).First()) && l.AccountId == fx.A("AR") select l).SingleAsync();
        await fx.H.PostedAsync(fx.S, new VoucherInput(VoucherTypes.General, D(3, 10), D(3, 10), "Collected before replacement", [new(fx.A("CASH"), Side.Debit, payment), new(fx.A("AR"), Side.Credit, payment, fx.H.Partner["KH01"], SettleJournalLineId: oldLine.Id)]));
        var movements = await fx.S.Db.StockMovements.CountAsync();
        var draft = await svc.CreateReplacementDraftAsync(fx.H.Preparer, original.InvoiceId!.Value, "Correct unit price", [new ReplacementLineChange(1, UnitPrice: 90_000)]);
        var done = await svc.IssueAsync(fx.H.Approver, draft.Id);
        Assert.Null(done.PostError);
        var entry = await fx.S.Db.JournalEntries.AsNoTracking().SingleAsync(e => e.VoucherId == (fx.S.Db.CommercialInvoices.Where(i => i.Id == draft.Id).Select(i => i.VoucherId).First()));
        var line = await fx.S.Db.JournalLines.AsNoTracking().SingleAsync(l => l.JournalEntryId == entry.Id && l.AccountId == fx.A("AR"));
        var allocated = (await fx.S.Db.SettlementAllocations.AsNoTracking().Where(a => a.VoidedAt == null && (a.DebitLineId == line.Id || a.CreditLineId == line.Id)).Select(a => a.Amount).ToListAsync()).Sum();
        Assert.Equal(Math.Max(990_000 - payment, 0), line.Amount - allocated);
        Assert.Equal(payment, await fx.Bal("CASH"));
        Assert.Equal(990_000 - payment, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Equal(movements, await fx.S.Db.StockMovements.CountAsync());
        Assert.Equal(2, sim.AcceptedCount);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Fix26_ReplacementPreflightDoesNotIssueWhenDependenciesAlreadyBlockReversal()
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var original = await IssuedSaleAsync(fx, svc);
        var adj = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(original.InvoiceId!.Value, InvoicePurpose.Discount, "T", "BLOCK", D(3, 20), D(3, 20), "Allowance", [new(1, Amount: 100_000)]));
        await fx.Inv.PostAsync(fx.H.Approver, adj.Id);
        var draft = await svc.CreateReplacementDraftAsync(fx.H.Preparer, original.InvoiceId.Value, "Correction", [new ReplacementLineChange(1, UnitPrice: 90_000)]);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.IssueAsync(fx.H.Approver, draft.Id)));
        Assert.Equal(1, sim.AcceptedCount);
        Assert.Equal(1, await fx.S.Db.EInvoiceDocuments.CountAsync());
        Assert.Equal("Issued", (await svc.ViewAsync(fx.H.Auditor, original.Id)).Status);
    }
}
