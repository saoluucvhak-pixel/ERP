using Hak.Application.Accounting;
using Hak.Application.Tax;
using Hak.Application.Trade;
using Hak.Domain.Common;
using Hak.Domain.Tax;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

public partial class TradeTests
{
    [Fact]
    public async Task Lifecycle27_LinkedZeroVatInvoiceLeavesEveryBalanceUnchanged()
    {
        using var fx = await SetupAsync();
        var draft = await fx.Inv.CreateAsync(fx.H.Preparer, new InvoiceInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "T", "ZERO", D(3, 3), D(3, 3), "Non VAT fixture",
            [new InvoiceLineInput(1_000_000, Quantity: 1, AccountId: fx.A("SALESREV"))]));
        var original = await fx.Inv.PostAsync(fx.H.Approver, draft.Id);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "ZERO", "Non VAT rebate", D(3, 1), D(3, 31), [new(0, 2)]));
        var run = await service.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1));
        await PostVoucherAsync(fx, run.VoucherId!.Value);
        var invoice = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(original.Id, InvoicePurpose.Discount, "T", "ZERO-ADJ", D(4, 2), D(4, 2), "Non VAT legal evidence", [new(1, Amount: 20_000)], DiscountKind.Trade));
        await service.LinkInvoiceAsync(fx.H.Preparer, a.Id, a.RowVersion, invoice.Id, null);
        var posted = await fx.Inv.PostAsync(fx.H.Approver, invoice.Id);
        Assert.NotNull(posted.VoucherId);
        Assert.Equal(20_000, await fx.Bal("SALESDISC"));
        Assert.Equal(980_000, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Equal(0, await fx.Bal("VATOUT"));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Lifecycle27_VoidRunCancelsLinkedDraftsAfterSourceVoucherWasReversed()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var original = await PostedSaleAsync(fx, "VOID-LINK", 10, D(3, 3));
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "VOID-LINK", "Cancel linked draft", D(3, 1), D(3, 31), [new(0, 2)]));
        var run = await service.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1));
        await PostVoucherAsync(fx, run.VoucherId!.Value);
        var invoice = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(original.Id, InvoicePurpose.Discount, "T", "VOID-LINK-ADJ", D(4, 2), D(4, 2), "Cancel fixture", [new(1, Amount: 20_000)], DiscountKind.Trade));
        await service.LinkInvoiceAsync(fx.H.Preparer, a.Id, a.RowVersion, invoice.Id, null);
        await fx.S.Engine.ReverseAsync(fx.H.Approver, run.VoucherId.Value, D(4, 3), "Reverse before void");
        await service.VoidRunAsync(fx.H.Preparer, a.Id, "Cancel settlement and unissued linked drafts");
        Assert.Equal(InvoiceStatus.Cancelled, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id)).Status);
        Assert.False((await service.PreviewAsync(fx.H.Auditor, a.Id)).Settled);
        Assert.Single(await fx.S.Db.DiscountSettlementInvoiceLinks.ToListAsync()); // Evidence remains append-only.
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Lifecycle27_SourceCannotReverseWhileLinkedInvoiceHasAnUncertainExternalIssue()
    {
        var (fx, sim, issuing, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var original = await IssuedSaleAsync(fx, issuing);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "PENDING-LINK", "Pending external issue", D(3, 1), D(3, 31), [new(0, 2)]));
        var run = await service.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1));
        await PostVoucherAsync(fx, run.VoucherId!.Value);
        var invoice = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(original.InvoiceId!.Value, InvoicePurpose.Discount, "T", "PENDING-LINK-ADJ", D(4, 2), D(4, 2), "Network failure fixture", [new(1, Amount: 20_000)], DiscountKind.Trade));
        await service.LinkInvoiceAsync(fx.H.Preparer, a.Id, a.RowVersion, invoice.Id, null);
        sim.FailTransientCalls = 1;
        Assert.Equal("Issuing", (await issuing.IssueAsync(fx.H.Approver, invoice.Id)).Status);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.Inv.CancelAsync(fx.H.Preparer, invoice.Id, "Cannot cancel unknown provider result")));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.S.Engine.ReverseAsync(fx.H.Approver, run.VoucherId.Value, D(4, 3), "Must wait for provider reconciliation")));
        Assert.Equal(1, sim.AcceptedCount);
        var document = await fx.S.Db.EInvoiceDocuments.AsNoTracking().SingleAsync(d => d.InvoiceId == invoice.Id);
        var recovered = await issuing.RetryAsync(fx.H.Approver, document.Id);
        Assert.Null(recovered.PostError);
        Assert.Equal("Issued", recovered.Status);
        Assert.Equal(2, sim.AcceptedCount);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    private static async Task<DiscountAgreementService> RebateService27Async(Fx fx, bool purchase = false)
    {
        fx.H.Acc["REB27"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "REB27", "Accrual balance fixture")).Id;
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, purchase ? "REBATE_RECEIVABLE" : "REBATE_PAYABLE", "REB27");
        if (purchase)
        {
            fx.H.Acc["PURDISC"] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, "PURDISC", "Purchase rebate fixture")).Id;
            await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "PURCHASE_TRADE_DISCOUNT", "PURDISC");
        }
        return new(fx.S.Db, fx.Inv, fx.S.Audit, fx.H.Clock, fx.S.Engine);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Lifecycle27_AccrualClearsOnlyThePostedPortion_AndReversalRestoresIt(decimal initialPercent)
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        await PostedSaleAsync(fx, "PART-1", 10, D(3, 3));
        await PostedSaleAsync(fx, "PART-2", 10, D(3, 4));
        var service = await RebateService27Async(fx);
        var input = new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "PART", "Partial settlement fixture", D(3, 1), D(3, 31), [new(0, initialPercent)]);
        var agreement = await service.CreateAsync(fx.H.Approver, input);
        var accrued = await service.AccrueAsync(fx.H.Preparer, agreement.Id, agreement.RowVersion, D(3, 31));
        await PostVoucherAsync(fx, accrued.VoucherId);
        agreement = await service.UpdateAsync(fx.H.Approver, agreement.Id, agreement.RowVersion, input with { Tiers = [new(0, 2)] });
        var run = await service.SettleAsync(fx.H.Preparer, agreement.Id, agreement.RowVersion, "T", "PART-ADJ", D(4, 1), D(4, 1));
        Assert.Null(run.AccrualReversalVoucherId);
        Assert.Equal(accrued.Estimated, run.Invoices.Sum(i => i.AccruedAmount));
        var first = run.Invoices[0]; var second = run.Invoices[1];
        await fx.Inv.PostAsync(fx.H.Approver, first.AdjustmentInvoiceId);
        Assert.Equal(-(run.AccruedAmount - first.AccruedAmount), await fx.Bal("REB27", fx.H.Partner["KH01"]));
        Assert.Equal(run.AccruedAmount + first.Amount - first.AccruedAmount, await fx.Bal("SALESDISC"));
        var progress = await service.LifecycleAsync(fx.H.Auditor, agreement.Id);
        Assert.Equal("Partial", progress.AccountingStatus);
        Assert.Equal(first.AccruedAmount, progress.AccrualCleared);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.S.Engine.ReverseAsync(fx.H.Approver, accrued.VoucherId, D(4, 2), "Must reverse settlement first")));
        await fx.Inv.PostAsync(fx.H.Approver, second.AdjustmentInvoiceId);
        Assert.Equal(0, await fx.Bal("REB27", fx.H.Partner["KH01"]));
        Assert.Equal(40_000, await fx.Bal("SALESDISC"));
        Assert.Equal("Posted", (await service.LifecycleAsync(fx.H.Auditor, agreement.Id)).AccountingStatus);
        await fx.Inv.ReverseAsync(fx.H.Approver, first.AdjustmentInvoiceId, D(4, 2), "Reverse one portion");
        Assert.Equal(-first.AccruedAmount, await fx.Bal("REB27", fx.H.Partner["KH01"]));
        Assert.Equal(second.AccruedAmount, (await service.LifecycleAsync(fx.H.Auditor, agreement.Id)).AccrualCleared);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Lifecycle27_VoucherThenSalesInvoiceDoesNotDiscountOrReduceReceivableTwice()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var original = await PostedSaleAsync(fx, "LINK-ORIG", 10, D(3, 3));
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "LINK", "Voucher then invoice", D(3, 1), D(3, 31), [new(0, 2)]));
        var run = await service.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1));
        await PostVoucherAsync(fx, run.VoucherId!.Value);
        Assert.Equal("AccountingOnly", (await service.LifecycleAsync(fx.H.Auditor, a.Id)).SettlementStatus);
        var invoice = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(original.Id, InvoicePurpose.Discount, "T", "LINK-ADJ", D(4, 2), D(4, 2), "Later legal invoice", [new(1, Amount: 20_000)], DiscountKind.Trade));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.Inv.PostAsync(fx.H.Approver, invoice.Id)));
        await service.LinkInvoiceAsync(fx.H.Preparer, a.Id, a.RowVersion, invoice.Id, null);
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => service.LinkInvoiceAsync(fx.H.Preparer, a.Id, a.RowVersion, invoice.Id, null)));
        await fx.Inv.PostAsync(fx.H.Approver, invoice.Id);
        Assert.Equal(20_000, await fx.Bal("SALESDISC"));
        Assert.Equal(1_078_000, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Equal(98_000, await fx.Bal("VATOUT") * -1);
        var obligation = Assert.Single(await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AR"), fx.H.Partner["KH01"], D(12, 31)));
        Assert.Equal(1_078_000, obligation.Open);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.S.Engine.ReverseAsync(fx.H.Approver, run.VoucherId.Value, D(4, 3), "Blocked while invoice posted")));
        Assert.Equal("Missing", (await service.LifecycleAsync(fx.H.Auditor, a.Id)).LegalInvoiceStatus); // Manual posting alone is not legal invoice evidence.
        await fx.Inv.ReverseAsync(fx.H.Approver, invoice.Id, D(4, 3), "Reverse linked invoice");
        Assert.Equal(1_080_000, await fx.Bal("AR", fx.H.Partner["KH01"]));
        Assert.Equal(20_000, await fx.Bal("SALESDISC"));
        await fx.S.Engine.ReverseAsync(fx.H.Approver, run.VoucherId.Value, D(4, 4), "Reverse settlement");
        Assert.Equal(1_100_000, await fx.Bal("AR", fx.H.Partner["KH01"]));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Lifecycle27_LinkQuotaIncludesDraftsAndFrozenVoucherRejectsBalancedTampering()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var original = await PostedSaleAsync(fx, "QUOTA", 10, D(3, 3));
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "QUOTA", "Quota", D(3, 1), D(3, 31), [new(0, 2)]));
        var run = await service.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1));
        var first = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(original.Id, InvoicePurpose.Discount, "T", "Q1", D(4, 2), D(4, 2), "First", [new(1, Amount: 15_000)], DiscountKind.Trade));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => service.LinkInvoiceAsync(fx.H.Preparer, a.Id, a.RowVersion, first.Id, null)));
        var lines = await fx.S.Db.VoucherLines.Where(l => l.VoucherId == run.VoucherId).ToListAsync();
        foreach (var line in lines) line.Amount += 1;
        await fx.S.Db.SaveChangesAsync();
        Assert.Contains(await fx.S.Engine.ValidateAsync(fx.H.Preparer, run.VoucherId!.Value), e => e.Code == ErrorCodes.ValidationFailed);
        foreach (var line in lines) line.Amount -= 1;
        await fx.S.Db.SaveChangesAsync();
        await PostVoucherAsync(fx, run.VoucherId.Value);
        await service.LinkInvoiceAsync(fx.H.Preparer, a.Id, a.RowVersion, first.Id, null);
        var second = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(original.Id, InvoicePurpose.Discount, "T", "Q2", D(4, 2), D(4, 2), "Second", [new(1, Amount: 6_000)], DiscountKind.Trade));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => service.LinkInvoiceAsync(fx.H.Preparer, a.Id, a.RowVersion, second.Id, null)));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => service.LinkInvoiceAsync(fx.H.Auditor, a.Id, a.RowVersion, second.Id, null)));
        await Assert.ThrowsAnyAsync<Exception>(() => fx.S.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM discount_settlement_invoice_links WHERE adjustment_invoice_id = {first.Id}"));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Lifecycle27_PurchaseVoucherThenInvoiceReclassifiesStockAndCogsAndOnlyAddsVatToPayable()
    {
        using var fx = await SetupAsync();
        var original = await PostedPurchaseOfAdditiveAsync(fx);
        await PostedIssueAsync(fx, 40, D(3, 5), fx.Add);
        var service = await RebateService27Async(fx, true);
        var a = await service.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Purchase, fx.H.Partner["NCC01"], "BUY-LINK", "Purchase reclassification", D(3, 1), D(3, 31), [new(0, 2)]));
        var run = await service.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1));
        await PostVoucherAsync(fx, run.VoucherId!.Value);
        Assert.Equal("PendingLegalInvoiceAllocation", (await service.LifecycleAsync(fx.H.Auditor, a.Id)).ValuationStatus);
        var invoice = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new AdjustmentInput(original.Id, InvoicePurpose.Discount, "1C26TAA", "SUP-LINK", D(4, 2), D(4, 2), "Supplier rebate", [new(1, Amount: 100_000)], DiscountKind.Trade));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => service.LinkInvoiceAsync(fx.H.Preparer, a.Id, a.RowVersion, invoice.Id, D(4, 1))));
        await service.LinkInvoiceAsync(fx.H.Preparer, a.Id, a.RowVersion, invoice.Id, D(4, 2));
        await fx.Inv.PostAsync(fx.H.Approver, invoice.Id);
        Assert.Equal(0, await fx.Bal("PURDISC"));
        Assert.Equal(2_940_000, await fx.Bal("INV"));
        Assert.Equal(1_960_000, await fx.Bal("COGS"));
        Assert.Equal(490_000, await fx.Bal("VATIN"));
        Assert.Equal(-5_390_000, await fx.Bal("AP", fx.H.Partner["NCC01"]));
        Assert.Equal(5_390_000, Assert.Single(await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AP"), fx.H.Partner["NCC01"], D(12, 31))).Open);
        Assert.Equal(0, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Lifecycle27_PurchaseReceiptDateIsEvidenceNotAnAdditionalDiscountOffset()
    {
        using var fx = await SetupAsync();
        await fx.S.Db.Companies.Where(c => c.Id == fx.H.CompanyId).ExecuteUpdateAsync(s => s.SetProperty(c => c.TaxCode, "0312345678"));
        await fx.S.Db.BusinessPartners.Where(p => p.Id == fx.H.Partner["NCC01"]).ExecuteUpdateAsync(s => s.SetProperty(p => p.TaxCode, "0400111222"));
        var original = await PostedPurchaseOfAdditiveAsync(fx);
        var service = await RebateService27Async(fx, true);
        var a = await service.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Purchase, fx.H.Partner["NCC01"], "RECEIVED", "Receipt date", D(3, 1), D(3, 31), [new(0, 2)]));
        var run = await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "1C26TAA", "SUP-RECEIVED", D(4, 1), D(4, 1));
        var adjustment = await fx.Inv.PostAsync(fx.H.Approver, run.Invoices.Single().AdjustmentInvoiceId);
        var vat = new VatService(fx.S.Db, fx.S.Audit, fx.H.Clock);
        await vat.ImportEInvoicesAsync(fx.H.Approver, "supplier fixture", "received fixture", [SupplierInvoice(adjustment.InvoiceNo, EInvoiceStatus.Adjustment, original.InvoiceNo, 100_000, 10_000) with { IssueDate = D(4, 1) }]);
        Assert.Equal("PendingReceiptDate", (await service.LifecycleAsync(fx.H.Auditor, a.Id)).TaxStatus);
        await service.RecordReceiptAsync(fx.H.Preparer, a.Id, a.RowVersion, adjustment.Id, D(5, 2));
        var progress = await service.LifecycleAsync(fx.H.Auditor, a.Id);
        Assert.Equal("ReadyForReview", progress.TaxStatus);
        var may = await vat.ReconcileAsync(fx.H.Auditor, D(5, 1), D(5, 31));
        Assert.Equal(1, may.Matched);
        Assert.DoesNotContain(may.Differences, d => d.InvoiceId == adjustment.Id && d.Type == TaxDiffType.PeriodDifference);
        Assert.Contains(await vat.LedgerListingAsync(fx.H.Auditor, TaxDirection.Input, D(5, 1), D(5, 31)), i => i.InvoiceId == adjustment.Id && i.ReceivedDate == D(5, 2));
        Assert.DoesNotContain(await vat.LedgerListingAsync(fx.H.Auditor, TaxDirection.Input, D(4, 1), D(4, 30)), i => i.InvoiceId == adjustment.Id);
        Assert.Equal(100_000, progress.PostedInvoiceAmount);
        Assert.Equal(0, (await fx.S.Db.DiscountSettlementInvoiceLinks.SingleAsync()).Amount);
        Assert.Equal(-5_390_000, await fx.Bal("AP", fx.H.Partner["NCC01"]));
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => service.RecordReceiptAsync(fx.H.Preparer, a.Id, a.RowVersion, adjustment.Id, D(5, 3))));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }
}
