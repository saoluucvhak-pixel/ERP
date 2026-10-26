using Hak.Application.Trade;
using Hak.Application.Tax;
using Hak.Domain.Trade;
using Hak.Domain.Tax;

namespace Hak.Integration.Tests;

public partial class TradeTests
{
    [Fact]
    public async Task Review26_SignedDecreaseInvoiceMustReconcileWithItsCorrectlyPostedDecrease()
    {
        var (fx, _, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var original = await IssuedSaleAsync(fx, svc);
        var adjustment = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(original.InvoiceId!.Value, InvoicePurpose.Discount, "1C26TBB", "REV26-SIGNED", D(3, 22), D(3, 22), "Signed decrease fixture", [new AdjustmentLineInput(1, Amount: 100_000)], DiscountKind.Trade));
        await fx.Inv.PostAsync(fx.H.Approver, adjustment.Id);
        var vat = new VatService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        var imported = await vat.ImportEInvoicesAsync(fx.H.Approver, "signed-decrease-fixture", "review26-signed", [new EInvoiceInput(TaxDirection.Output, "0312345678", "Fixture company", "1C26TBB", "REV26-SIGNED", D(3, 22), -100_000, -10_000, -110_000, EInvoiceStatus.Adjustment, [new EInvoiceRateInput(10, -100_000, -10_000)], RelatedSeries: original.Series, RelatedNumber: original.Number)]);
        Assert.Equal(1, imported.Imported);
        var rec = await vat.ReconcileAsync(fx.H.Auditor, D(3, 1), D(3, 31));
        Assert.DoesNotContain(rec.Differences, d => d.InvoiceId == adjustment.Id && (d.Type == TaxDiffType.AmountDifference || d.Type == TaxDiffType.VatDifference));
    }

    [Fact]
    public async Task Review26_SettledStatementMustNotChangeWhenAnOlderPeriodGetsANewInvoice()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        await PostedSaleAsync(fx, "REV26-01", 10, D(3, 3));
        var agreements = new DiscountAgreementService(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock, fx.S.Engine);
        var agr = await agreements.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "REV26-AGR", "Review snapshot", D(3, 1), D(3, 31), [new AgreementTierInput(0, 2)]));
        var run = await agreements.SettleByVoucherAsync(fx.H.Preparer, agr.Id, agr.RowVersion, D(4, 1));
        await PostVoucherAsync(fx, run.VoucherId!.Value);
        Assert.Equal(20_000m, (await agreements.StatementAsync(fx.H.Auditor, agr.Id)).TotalDiscount);
        // March remains open: a late-entered invoice must not rewrite the statement of a posted settlement.
        await PostedSaleAsync(fx, "REV26-02", 10, D(3, 20));
        var statement = await agreements.StatementAsync(fx.H.Auditor, agr.Id);
        Assert.True(statement.Settled);
        Assert.Equal(run.Amount, statement.TotalDiscount);
    }

    [Fact]
    public async Task Review26_TurnoverRebateAfterFiledPeriodMustNotBeClassifiedAsCorrectionOfAnInvoiceError()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(7, 1), fx.Add);
        await PostedSaleAsync(fx, "REV26-VAT", 10, D(7, 3));
        var vat = new VatService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        var decl = await vat.SaveDeclarationAsync(fx.H.Admin, D(7, 1), D(7, 31), [new DeclarationLineInput("CT29", "Output VAT fixture", 100_000)]);
        await vat.MarkFiledAsync(fx.H.Admin, decl.Id, "REVIEW-JULY-FILED", D(8, 20));
        var agreements = new DiscountAgreementService(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock, fx.S.Engine);
        var agr = await agreements.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "REV26-VAT-AGR", "Turnover rebate, not error correction", D(7, 1), D(7, 31), [new AgreementTierInput(0, 2)]));
        var run = await agreements.SettleAsync(fx.H.Preparer, agr.Id, agr.RowVersion, "1C26TBB", "REV26-ADJ", D(9, 1), D(9, 1));
        foreach(var row in run.Invoices) await fx.Inv.PostAsync(fx.H.Approver, row.AdjustmentInvoiceId);
        // TT91/2026 Article10(5)(b), (6)(d): turnover rebate belongs to the adjustment/receipt period, not an automatic original-period supplement.
        var rec = await vat.ReconcileAsync(fx.H.Auditor, D(9, 1), D(9, 30));
        Assert.DoesNotContain(rec.Differences, d => d.Type == TaxDiffType.SupplementalFilingNeeded);
    }
}
