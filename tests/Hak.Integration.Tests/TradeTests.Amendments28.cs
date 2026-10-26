using Hak.Application.Accounting;
using Hak.Application.EInvoice;
using Hak.Application.Fx;
using Hak.Application.Trade;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

public partial class TradeTests
{
    private static async Task<CommercialInvoice> Sale28Async(Fx fx, string no, decimal amount, decimal vat = 10)
    {
        var draft = await fx.Inv.CreateAsync(fx.H.Preparer, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "T28", no, D(3, 3), D(3, 3), "Service fixture",
            [new(amount, Quantity: 1, AccountId: fx.A("SALESREV"), VatRate: vat)]));
        return await fx.Inv.PostAsync(fx.H.Approver, draft.Id);
    }
    private static async Task PostRun28Async(Fx fx, Guid runId)
    {
        var rows = await fx.S.Db.DiscountAgreementRunInvoices.Where(i => i.RunId == runId).OrderBy(i => i.Amount).ToListAsync();
        foreach (var row in rows) await fx.Inv.PostAsync(fx.H.Approver, row.AdjustmentInvoiceId, true);
    }
    private static Task<DiscountAgreementRun> Amend28Async(Fx fx, DiscountAgreementService service, RebateAmendmentPreview p, string no = "AMEND")
        => service.AmendAsync(fx.H.Preparer, p.AgreementId, p.RowVersion, p.PreviousRunId, p.SourceHash, "New evidence fixture", "T28", no, D(4, 2), D(4, 2));

    [Theory]
    [InlineData(false, 4_000_000)]
    [InlineData(true, 700_000)]
    public async Task Phase28_SupplementUsesFrozenPolicyAndOnlyBooksTheDifference(bool progressive, decimal expected)
    {
        using var fx = await SetupAsync();
        await Sale28Async(fx, "FIRST", 100_000_000);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "AMEND", "Tier fixture", D(3, 1), D(3, 31), [new(0, 2), new(110_000_000, 5)], progressive));
        var root = await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T28", "ROOT", D(4, 1), D(4, 1));
        await PostRun28Async(fx, root.Id);
        var frozen = root.SettlementSnapshot;
        await Sale28Async(fx, "LATE", 20_000_000);
        var p = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id);
        Assert.Equal(expected, p.Difference);
        var child = await Amend28Async(fx, service, p);
        Assert.Equal(root.Id, child.ParentRunId);
        Assert.Equal(expected, child.Amount);
        await Assert.ThrowsAsync<DomainException>(() => Amend28Async(fx, service, p));
        await PostRun28Async(fx, child.Id);
        Assert.Equal(2_000_000 + expected, await fx.Bal("SALESDISC"));
        Assert.Equal(120_000_000, (await service.StatementAsync(fx.H.Auditor, a.Id, child.Id)).TotalSales);
        Assert.Equal(-120_000_000, await fx.Bal("SALESREV"));
        Assert.Equal(frozen, (await fx.S.Db.DiscountAgreementRuns.AsNoTracking().SingleAsync(r => r.Id == root.Id)).SettlementSnapshot);
        Assert.Empty((await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id)).Lines);
        Assert.Equal(2, (await service.HistoryAsync(fx.H.Auditor, a.Id)).Count);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Phase28_ClawbackAfterTierDropCreditsDiscountNotSalesRevenue()
    {
        using var fx = await SetupAsync();
        await Sale28Async(fx, "A", 100_000_000);
        var b = await Sale28Async(fx, "B", 20_000_000, 8);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "CLAW", "Clawback fixture", D(3, 1), D(3, 31), [new(0, 2), new(110_000_000, 5)]));
        var root = await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T28", "ROOT", D(4, 1), D(4, 1));
        await PostRun28Async(fx, root.Id);
        var allowance = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new(b.Id, InvoicePurpose.Discount, "T28", "ALLOW", D(4, 2), D(4, 2), "Later price allowance", [new(1, Amount: 19_000_000)], DiscountKind.Allowance));
        await fx.Inv.PostAsync(fx.H.Approver, allowance.Id);
        var p = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id);
        Assert.Equal((6_000_000m, 2_020_000m, -3_980_000m), (p.PreviousAmount, p.TargetAmount, p.Difference));
        var child = await Amend28Async(fx, service, p);
        var ids = child.Invoices.Select(i => i.AdjustmentInvoiceId).ToList();
        Assert.All(await fx.S.Db.CommercialInvoices.Where(i => ids.Contains(i.Id)).ToListAsync(), i => {
            Assert.Equal(InvoicePurpose.Increase, i.Purpose); Assert.Equal(InvoiceAdjustmentReason.CommercialRebate, i.AdjustmentReason);
        });
        await PostRun28Async(fx, child.Id);
        var report = await new DiscountReportService(fx.S.Db).ReportAsync(fx.H.Auditor, InvoiceKind.Sales, D(1, 1), D(12, 31));
        Assert.Equal(2_020_000, report.Total.AfterSaleTradeDiscount);
        Assert.Equal(0, report.Total.Increase);
        Assert.Equal(-120_000_000, await fx.Bal("SALESREV"));
        Assert.Equal(21_020_000, await fx.Bal("SALESDISC")); // allowance + remaining rebate
        Assert.Equal("Posted", (await service.LifecycleAsync(fx.H.Auditor, a.Id, child.Id)).AccountingStatus);
        Assert.Equal("PendingInvoice", (await service.LifecycleAsync(fx.H.Auditor, a.Id, child.Id)).TaxStatus);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Phase28_NetZeroRedistributionStillCreatesBothDirectionsAndEnforcesReversalOrder()
    {
        using var fx = await SetupAsync();
        var original = await Sale28Async(fx, "A", 100_000_000);
        await Sale28Async(fx, "B", 100_000_000, 8);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "ZERO28", "Net zero fixture", D(3, 1), D(3, 31), [new(0, 2)]));
        var root = await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T28", "ROOT", D(4, 1), D(4, 1));
        await PostRun28Async(fx, root.Id);
        var allowance = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new(original.Id, InvoicePurpose.Discount, "T28", "ALLOW", D(4, 2), D(4, 2), "Change source", [new(1, Amount: 10_000_000)]));
        await fx.Inv.PostAsync(fx.H.Approver, allowance.Id);
        await Sale28Async(fx, "C", 10_000_000, 8);
        var p = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id);
        Assert.Equal(0, p.Difference); Assert.Equal(2, p.Lines.Count);
        var child = await Amend28Async(fx, service, p);
        var plus = child.Invoices.Single(i => i.Amount > 0); var minus = child.Invoices.Single(i => i.Amount < 0);
        Assert.Equal("Draft", (await service.LifecycleAsync(fx.H.Auditor, a.Id, child.Id)).AccountingStatus);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.Inv.PostAsync(fx.H.Approver, plus.AdjustmentInvoiceId)));
        var beforeVat = await fx.Bal("VATOUT");
        await fx.Inv.PostAsync(fx.H.Approver, minus.AdjustmentInvoiceId);
        await fx.Inv.PostAsync(fx.H.Approver, plus.AdjustmentInvoiceId);
        Assert.Equal(beforeVat - 4_000, await fx.Bal("VATOUT")); // 200k recovery at 10% vs 200k supplement at 8%.
        Assert.Equal("Posted", (await service.LifecycleAsync(fx.H.Auditor, a.Id, child.Id)).AccountingStatus);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.Inv.ReverseAsync(fx.H.Approver, root.Invoices.First().AdjustmentInvoiceId, D(4, 3), "Must retain source")));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.Inv.ReverseAsync(fx.H.Approver, minus.AdjustmentInvoiceId, D(4, 3), "Wrong reversal order")));
        await fx.Inv.ReverseAsync(fx.H.Approver, plus.AdjustmentInvoiceId, D(4, 3), "Undo supplement");
        await fx.Inv.ReverseAsync(fx.H.Approver, minus.AdjustmentInvoiceId, D(4, 3), "Undo clawback");
        await service.VoidRunAsync(fx.H.Preparer, a.Id, "Void latest only", child.Id);
        Assert.Equal(2, (await service.HistoryAsync(fx.H.Auditor, a.Id)).Count);
        Assert.Equal(root.Id, (await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id)).PreviousRunId);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Phase28_RejectsStaleSourcesNoDifferenceAndUnlinkedClawback()
    {
        using var fx = await SetupAsync();
        var original = await Sale28Async(fx, "A", 100_000_000);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "SAFE28", "Guard fixture", D(3, 1), D(3, 31), [new(0, 2)]));
        var root = await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T28", "ROOT", D(4, 1), D(4, 1));
        await PostRun28Async(fx, root.Id);
        var zero = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => Amend28Async(fx, service, zero)));
        await Sale28Async(fx, "B", 20_000_000);
        var p = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id);
        await Sale28Async(fx, "C", 1_000_000);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, await CodeOf(() => Amend28Async(fx, service, p)));
        Assert.Single(await service.HistoryAsync(fx.H.Auditor, a.Id));
        var unlinked = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new(original.Id, InvoicePurpose.Increase, "T28", "UNLINKED", D(4, 2), D(4, 2), "Cannot invent rebate rights", [new(1, Amount: 100_000)], AdjustmentReason: InvoiceAdjustmentReason.CommercialRebate));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.Inv.PostAsync(fx.H.Approver, unlinked.Id)));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Phase28_PurchaseClawbackRestoresStockAndCogsWithoutClearingAccrualTwice()
    {
        using var fx = await SetupAsync();
        var original = await PostedPurchaseOfAdditiveAsync(fx);
        await PostedIssueAsync(fx, 40, D(3, 5), fx.Add);
        var service = await RebateService27Async(fx, true);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Purchase, fx.H.Partner["NCC01"], "PUR28", "Purchase clawback", D(3, 1), D(3, 31), [new(0, 2)]));
        var root = await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T28", "ROOT", D(4, 1), D(4, 1));
        await PostRun28Async(fx, root.Id);
        var allowance = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new(original.Id, InvoicePurpose.Discount, "T28", "ALLOW", D(4, 2), D(4, 2), "Purchase price reduction", [new(1, Amount: 500_000)]));
        await fx.Inv.PostAsync(fx.H.Approver, allowance.Id);
        var p = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id);
        Assert.Equal(-10_000, p.Difference);
        var child = await Amend28Async(fx, service, p);
        await PostRun28Async(fx, child.Id);
        Assert.Equal(2_646_000, await fx.Bal("INV")); Assert.Equal(1_764_000, await fx.Bal("COGS"));
        Assert.Equal(441_000, await fx.Bal("VATIN"));
        Assert.Equal(0, Assert.Single(await fx.S.Stock.ReconcileAsync(fx.H.Auditor, D(12, 31))).Difference);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    private static async Task ConfigureFx28Async(Fx fx)
    {
        foreach (var code in new[] { "FXGAIN", "FXLOSS", "FXREVAL", "BANKUSD" })
            fx.H.Acc[code] = (await fx.S.Setup.CreateAccountAsync(fx.H.Admin, code, code + " fixture", cashKind: code == "BANKUSD" ? CashKind.Bank : CashKind.None)).Id;
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "FX_REALIZED_GAIN", "FXGAIN");
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "FX_REALIZED_LOSS", "FXLOSS");
        await fx.S.Rules.SetRoleMappingAsync(fx.H.Admin, "FX_REVALUATION", "FXREVAL");
    }

    [Fact]
    public async Task Phase28_SupplementNeverClearsTheOriginalAccrualAgain()
    {
        using var fx = await SetupAsync();
        await Sale28Async(fx, "A", 100_000_000);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "ACCR28", "Accrual follow-up", D(3, 1), D(3, 31), [new(0, 2)]));
        var accrual = await service.AccrueAsync(fx.H.Preparer, a.Id, a.RowVersion, D(3, 31));
        await PostVoucherAsync(fx, accrual.VoucherId);
        var root = await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T28", "ROOT", D(4, 1), D(4, 1));
        await PostRun28Async(fx, root.Id);
        Assert.Equal(0, await fx.Bal("REB27", fx.H.Partner["KH01"]));
        await Sale28Async(fx, "B", 20_000_000);
        var child = await Amend28Async(fx, service, await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id));
        await PostRun28Async(fx, child.Id);
        Assert.Equal(0, child.AccruedAmount); Assert.Equal(0, child.Invoices.Sum(i => i.AccruedAmount));
        Assert.Equal(0, await fx.Bal("REB27", fx.H.Partner["KH01"])); Assert.Equal(2_400_000, await fx.Bal("SALESDISC"));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Phase28_AmendmentCannotPredateItsSourceOrTheReversalOfAVoidedRun()
    {
        using var fx = await SetupAsync(); await Sale28Async(fx, "A", 100_000_000);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "DATES28", "Chronology", D(3, 1), D(3, 31), [new(0, 2)]));
        var root = await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T28", "ROOT", D(5, 1), D(5, 1));
        await PostRun28Async(fx, root.Id); await Sale28Async(fx, "B", 20_000_000);
        var p = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => Amend28Async(fx, service, p)));
        var child = await service.AmendAsync(fx.H.Preparer, a.Id, p.RowVersion, p.PreviousRunId, p.SourceHash, "Correct later date", "T28", "MAY", D(5, 2), D(5, 2));
        await PostRun28Async(fx, child.Id);
        await fx.Inv.ReverseAsync(fx.H.Approver, child.Invoices.Single().AdjustmentInvoiceId, D(5, 3), "Reverse latest");
        await service.VoidRunAsync(fx.H.Preparer, a.Id, "Void latest", child.Id);
        var next = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => service.AmendAsync(fx.H.Preparer, a.Id, next.RowVersion, next.PreviousRunId, next.SourceHash, "Must follow reversal", "T28", "NEXT", D(5, 2), D(5, 2))));
        Assert.Equal(2, (await service.HistoryAsync(fx.H.Auditor, a.Id)).Count);
    }

    [Fact]
    public async Task Phase28_PurchaseAmendmentRefusesToPutWorkInProgressIntoCogs()
    {
        using var fx = await SetupAsync();
        var original = await PostedPurchaseOfAdditiveAsync(fx);
        var service = await RebateService27Async(fx, true);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Purchase, fx.H.Partner["NCC01"], "WIP28", "WIP guard", D(3, 1), D(3, 31), [new(0, 2)]));
        var root = await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T28", "ROOT", D(4, 1), D(4, 1));
        await PostRun28Async(fx, root.Id);
        var allowance = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new(original.Id, InvoicePurpose.Discount, "T28", "ALLOW", D(4, 2), D(4, 2), "New evidence", [new(1, Amount: 500_000)]));
        await fx.Inv.PostAsync(fx.H.Approver, allowance.Id);
        var issue = await PostedIssueAsync(fx, 40, D(4, 3), fx.Add);
        var cost = new Hak.Domain.Production.CostObject { Id = Ids.New(), CompanyId = fx.H.CompanyId, Code = "WIP28", Name = "WIP fixture", ItemId = fx.Dam, WipAccountId = fx.A("INV"), CreatedAt = fx.H.Clock.UtcNow, CreatedBy = fx.H.AdminId };
        var order = new Hak.Domain.Production.ProductionOrder { Id = Ids.New(), CompanyId = fx.H.CompanyId, Number = "WIP28", ItemId = fx.Dam, CostObjectId = cost.Id, WarehouseId = fx.Wh, PlannedQuantity = 10, PlannedStart = D(3, 2), Status = Hak.Domain.Production.ProductionOrderStatus.InProgress, CreatedAt = fx.H.Clock.UtcNow, CreatedBy = fx.H.AdminId };
        fx.S.Db.CostObjects.Add(cost); fx.S.Db.ProductionOrders.Add(order);
        fx.S.Db.ProductionOrderIssues.Add(new() { Id = Ids.New(), CompanyId = fx.H.CompanyId, OrderId = order.Id, StockDocumentId = issue.Id });
        await fx.S.Db.SaveChangesAsync();
        var journals = await fx.S.Db.JournalEntries.CountAsync();
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => service.AmendmentPreviewAsync(fx.H.Auditor, a.Id)));
        Assert.Equal(journals, await fx.S.Db.JournalEntries.CountAsync()); Assert.Single(await service.HistoryAsync(fx.H.Auditor, a.Id));
    }

    [Fact]
    public async Task Phase28_VoucherRootIsNotCountedTwiceWhenLegalInvoicesAndSupplementArrive()
    {
        using var fx = await SetupAsync();
        var original = await Sale28Async(fx, "A", 100_000_000);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "VOUCH28", "Voucher root", D(3, 1), D(3, 31), [new(0, 2)]));
        var root = await service.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1));
        await PostVoucherAsync(fx, root.VoucherId!.Value);
        var legal = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new(original.Id, InvoicePurpose.Discount, "T28", "LEGAL", D(4, 1), D(4, 1), "Legal evidence", [new(1, Amount: 2_000_000)], DiscountKind.Trade));
        await service.LinkInvoiceAsync(fx.H.Preparer, a.Id, a.RowVersion, legal.Id, null);
        await fx.Inv.PostAsync(fx.H.Approver, legal.Id);
        await Sale28Async(fx, "B", 20_000_000);
        var p = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id);
        Assert.Equal(400_000, p.Difference);
        var child = await Amend28Async(fx, service, p);
        await PostRun28Async(fx, child.Id);
        var report = await new DiscountReportService(fx.S.Db).ReportAsync(fx.H.Auditor, InvoiceKind.Sales, D(1, 1), D(12, 31));
        Assert.Equal(2_400_000, report.Total.AfterSaleTradeDiscount);
        Assert.Equal(2_400_000, await fx.Bal("SALESDISC"));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Phase28_ForeignRebateSupplementAndClawbackUseOriginalCurrenciesAndRates()
    {
        using var fx = await SetupAsync();
        var service = await RebateService27Async(fx);
        var draft = await fx.Inv.CreateAsync(fx.H.Preparer, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "T28", "USD-A", D(3, 3), D(3, 3), "USD rebate source", [new(1_000, Quantity: 1, AccountId: fx.A("SALESREV"), VatRate: 10)], Currency: "USD", ExchangeRate: 25_000));
        var original = await fx.Inv.PostAsync(fx.H.Approver, draft.Id);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "FXREB28", "FX rebate", D(3, 1), D(3, 31), [new(0, 2)]));
        var root = await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T28", "ROOT", D(4, 1), D(4, 1));
        await PostRun28Async(fx, root.Id);
        var late = await fx.Inv.CreateAsync(fx.H.Preparer, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "T28", "USD-B", D(3, 3), D(3, 3), "Second rate", [new(200, Quantity: 1, AccountId: fx.A("SALESREV"), VatRate: 8)], Currency: "USD", ExchangeRate: 26_000));
        await fx.Inv.PostAsync(fx.H.Approver, late.Id);
        var child = await Amend28Async(fx, service, await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id));
        Assert.Equal(104_000, child.Amount);
        Assert.Equal(5_200_000, (await service.StatementAsync(fx.H.Auditor, a.Id, child.Id)).TotalSales);
        await PostRun28Async(fx, child.Id);
        var allowance = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new(original.Id, InvoicePurpose.Discount, "T28", "USD-ALLOW", D(4, 2), D(4, 2), "Reduce base", [new(1, Amount: 100)]));
        await fx.Inv.PostAsync(fx.H.Approver, allowance.Id);
        var recovery = await Amend28Async(fx, service, await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id), "RECOVERY");
        Assert.Equal(-50_000, recovery.Amount);
        await PostRun28Async(fx, recovery.Id);
        var actual = await fx.S.Db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.Id == recovery.Invoices.Single().AdjustmentInvoiceId);
        Assert.Equal("USD", actual.Currency); Assert.Equal(25_000, actual.ExchangeRate); Assert.Equal(2, actual.Lines.Single().Amount);
        Assert.Equal(3_054_000, await fx.Bal("SALESDISC")); // 2.5m allowance + 554k rebate
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Phase28_FrozenAmendmentMetadataCannotBeChangedAndLockedPeriodCannotCreateARun()
    {
        using var fx = await SetupAsync();
        await Sale28Async(fx, "A", 100_000_000);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "IMM28", "Immutability", D(3, 1), D(3, 31), [new(0, 2)]));
        var root = await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T28", "ROOT", D(4, 1), D(4, 1));
        await PostRun28Async(fx, root.Id); await Sale28Async(fx, "B", 20_000_000);
        var p = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id);
        await Assert.ThrowsAsync<DomainException>(() => service.AmendAsync(fx.H.Auditor, a.Id, p.RowVersion, p.PreviousRunId, p.SourceHash, "No permission", "T", "X", D(4, 2), D(4, 2)));
        await fx.S.Db.AccountingPeriods.Where(x => x.CompanyId == fx.H.CompanyId && x.PeriodNo == 4).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, Hak.Domain.Organization.PeriodStatus.Locked));
        Assert.Equal(ErrorCodes.PeriodLocked, await CodeOf(() => Amend28Async(fx, service, p)));
        await fx.S.Db.AccountingPeriods.Where(x => x.CompanyId == fx.H.CompanyId && x.PeriodNo == 4).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, Hak.Domain.Organization.PeriodStatus.Open));
        var child = await Amend28Async(fx, service, p);
        await Assert.ThrowsAnyAsync<Exception>(() => fx.S.Db.DiscountAgreementRuns.Where(r => r.Id == child.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.AmendmentReason, "tampered")));
        await Assert.ThrowsAnyAsync<Exception>(() => fx.S.Db.DiscountAgreementRuns.Where(r => r.Id == child.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.ParentRunId, (Guid?)null)));
        Assert.Equal(root.Id, (await fx.S.Db.DiscountAgreementRuns.AsNoTracking().SingleAsync(r => r.Id == child.Id)).ParentRunId);
    }

    [Fact]
    public async Task Phase28_PurchaseFxReplacementKeepsOriginalPaymentLossAndSeparatesVat()
    {
        using var fx = await SetupAsync();
        await ConfigureFx28Async(fx);
        await fx.H.PostedAsync(fx.S, new(VoucherTypes.General, D(3, 1), D(3, 1), "USD opening fixture", [new(fx.A("BANKUSD"), Side.Debit, 26_000_000, ForeignCurrency: "USD", ForeignAmount: 1_000, ExchangeRate: 26_000), new(fx.A("EQUITY"), Side.Credit, 26_000_000)]));
        var created = await fx.Inv.CreateAsync(fx.H.Preparer, new(InvoiceKind.Purchase, fx.H.Partner["NCC01"], "T28", "PUR-USD", D(3, 3), D(3, 3), "USD expense", [new(1_000, Quantity: 1, AccountId: fx.A("COGS"), VatRate: 10)], Currency: "USD", ExchangeRate: 25_000));
        var original = await fx.Inv.PostAsync(fx.H.Approver, created.Id);
        var open = (await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AP"), fx.H.Partner["NCC01"], D(3, 10))).Single(i => i.ForeignCurrency == "USD");
        var pay = await fx.S.FxPayments.CreateDraftAsync(fx.H.Preparer, new(D(3, 10), fx.A("BANKUSD"), 25_000, "Pay supplier", [new(open.JournalLineId, 400)]));
        await PostVoucherAsync(fx, pay.Id);
        var cash = await fx.Bal("BANKUSD"); var loss = await fx.Bal("FXLOSS");
        var draft = await fx.Inv.CreateAsync(fx.H.Preparer, new(InvoiceKind.Purchase, fx.H.Partner["NCC01"], "T28", "PUR-REPLACE", D(3, 3), D(3, 3), "USD replacement", [new(900, Quantity: 1, AccountId: fx.A("COGS"), VatRate: 10)], Currency: "USD", ExchangeRate: 25_000, ReplacesInvoiceId: original.Id));
        await fx.Inv.PostAsync(fx.H.Approver, draft.Id);
        Assert.Equal(400_000, loss); Assert.Equal(loss, await fx.Bal("FXLOSS")); Assert.Equal(cash, await fx.Bal("BANKUSD"));
        var items = await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AP"), fx.H.Partner["NCC01"], D(12, 31));
        Assert.Equal(500, Assert.Single(items, i => i.ForeignCurrency == "USD").OpenForeign);
        Assert.Equal(2_250_000, Assert.Single(items, i => i.ForeignCurrency == null).Open);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }
    private static async Task<CommercialInvoice> IssuedUsd28Async(Fx fx, EInvoiceIssueService svc)
    {
        var inv = await fx.Inv.CreateAsync(fx.H.Preparer, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "", "", D(3, 3), D(3, 3), "USD fixture", [new(1_000, Quantity: 1, AccountId: fx.A("SALESREV"), VatRate: 10)], Currency: "USD", ExchangeRate: 25_000, AwaitIssue: true));
        var result = await svc.IssueAsync(fx.H.Approver, inv.Id);
        Assert.Null(result.PostError);
        return await fx.S.Db.CommercialInvoices.AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.Id == inv.Id);
    }

    [Theory]
    [InlineData(400, 900)]
    [InlineData(1_000, 900)]
    [InlineData(1_000, 1_100)]
    public async Task Phase28_FxReplacementTransfersBothCurrenciesWithoutRepeatingCashOrGain(decimal payment, decimal price)
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        await ConfigureFx28Async(fx);
        var original = await IssuedUsd28Async(fx, svc);
        var open = (await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AR"), fx.H.Partner["KH01"], D(3, 10))).Single(i => i.ForeignCurrency == "USD");
        // Multiple receipts with different cash rates; their AR carrying rate remains 25,000.
        foreach (var part in new[] { payment / 2, payment / 2 })
        {
            var v = await fx.S.FxPayments.CreateDraftAsync(fx.H.Preparer, new(D(3, 10), fx.A("BANKUSD"), 25_200, "Receipt fixture", [new(open.JournalLineId, part)]));
            await PostVoucherAsync(fx, v.Id);
        }
        var cash = await fx.Bal("BANKUSD"); var gain = await fx.Bal("FXGAIN"); var entries = await fx.S.Db.JournalEntries.CountAsync();
        var draft = await svc.CreateReplacementDraftAsync(fx.H.Preparer, original.Id, "Correct USD price", [new(1, UnitPrice: price)]);
        var plan = await fx.Inv.ReplacementAllocationsAsync(fx.H.Auditor, draft.Id);
        Assert.Equal(Math.Min(payment, price), plan.Sum(p => p.ForeignAmount ?? 0));
        Assert.Equal(Math.Max(payment - price, 0), plan.Sum(p => p.UnappliedForeign ?? 0));
        var result = await svc.IssueAsync(fx.H.Approver, draft.Id);
        Assert.Null(result.PostError);
        Assert.Equal(cash, await fx.Bal("BANKUSD")); Assert.Equal(gain, await fx.Bal("FXGAIN"));
        Assert.Equal(entries + 2, await fx.S.Db.JournalEntries.CountAsync()); // reverse old + new invoice, no new payment/FX voucher
        Assert.Equal(2, sim.AcceptedCount);
        var items = await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AR"), fx.H.Partner["KH01"], D(12, 31));
        Assert.Equal(Math.Max(price - payment, 0), items.Where(i => i.ForeignCurrency == "USD" && i.Side == Side.Debit).Sum(i => i.OpenForeign ?? 0));
        Assert.Equal(Math.Max(payment - price, 0), items.Where(i => i.ForeignCurrency == "USD" && i.Side == Side.Credit).Sum(i => i.OpenForeign ?? 0));
        Assert.Equal(price * 25_000 * 0.1m, items.Where(i => i.ForeignCurrency == null).Sum(i => i.Open));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Phase28_RevaluedInvoiceIsRejectedBeforeAnotherExternalIssue()
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        await ConfigureFx28Async(fx);
        var original = await IssuedUsd28Async(fx, svc);
        await fx.S.Fx.RevalueAsync(fx.H.Approver, D(3, 10), [new("USD", 25_300, 25_300)], "Fixture closing rate");
        var draft = await svc.CreateReplacementDraftAsync(fx.H.Preparer, original.Id, "Unsupported revaluation", null);
        await Assert.ThrowsAsync<DomainException>(() => svc.IssueAsync(fx.H.Approver, draft.Id));
        Assert.Equal(1, sim.AcceptedCount);
        Assert.Equal(InvoiceStatus.Posted, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == original.Id)).Status);
    }

    [Theory]
    [InlineData(1_000, true)]
    [InlineData(900, false)]
    public async Task Phase28_PaymentDiscountIsPreservedOnlyWhenReplacementDoesNotChangeItsBasis(decimal price, bool allowed)
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        await ConfigureFx28Async(fx); await MapPaymentDiscountExpenseAsync(fx);
        var original = await fx.Inv.CreateAsync(fx.H.Preparer, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "", "", D(3, 3), D(3, 3), "FX discount source", [new(1_000, Quantity: 1, AccountId: fx.A("SALESREV"), VatRate: 0)], Currency: "USD", ExchangeRate: 25_000, PaymentDiscountPercent: 2, PaymentDiscountDays: 10, AwaitIssue: true));
        await svc.IssueAsync(fx.H.Approver, original.Id);
        var open = Assert.Single(await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AR"), fx.H.Partner["KH01"], D(3, 10)));
        var payment = await fx.S.FxPayments.CreateDraftAsync(fx.H.Preparer, new(D(3, 10), fx.A("BANKUSD"), 25_200, "Discount receipt", [new(open.JournalLineId, 1_000, true)]));
        await PostVoucherAsync(fx, payment.Id);
        var cash = await fx.Bal("BANKUSD"); var discount = await fx.Bal("FINEXP"); var gain = await fx.Bal("FXGAIN");
        var draft = await svc.CreateReplacementDraftAsync(fx.H.Preparer, original.Id, "Check discount rights", [new(1, UnitPrice: price)]);
        if (allowed) { var result = await svc.IssueAsync(fx.H.Approver, draft.Id); Assert.Null(result.PostError); Assert.Equal(2, sim.AcceptedCount); }
        else { Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => svc.IssueAsync(fx.H.Approver, draft.Id))); Assert.Equal(1, sim.AcceptedCount); }
        Assert.Equal(cash, await fx.Bal("BANKUSD")); Assert.Equal(discount, await fx.Bal("FINEXP")); Assert.Equal(gain, await fx.Bal("FXGAIN"));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task Phase28_FailedFinalAllocationRollsBackBothInvoicesAndCanRecoverWithoutReissuing()
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        if (!fx.S.Db.Database.IsSqlite()) return; // SQLite-specific failure injection, not a PostgreSQL execution claim.
        await ConfigureFx28Async(fx);
        var original = await IssuedUsd28Async(fx, svc);
        var open = (await fx.S.Settlement.OpenItemsAsync(fx.H.Auditor, fx.A("AR"), fx.H.Partner["KH01"], D(3, 10))).Single(i => i.ForeignCurrency == "USD");
        var payment = await fx.S.FxPayments.CreateDraftAsync(fx.H.Preparer, new(D(3, 10), fx.A("BANKUSD"), 25_200, "Receipt fixture", [new(open.JournalLineId, 400)]));
        await PostVoucherAsync(fx, payment.Id);
        var draft = await svc.CreateReplacementDraftAsync(fx.H.Preparer, original.Id, "Failure injection", [new(1, UnitPrice: 900)]);
        await fx.S.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER test_fail_reallocation BEFORE INSERT ON settlement_allocations WHEN NEW.origin = 'INVOICE_REPLACEMENT' BEGIN SELECT RAISE(ABORT, 'TEST_FAILURE'); END;");
        var failed = await svc.IssueAsync(fx.H.Approver, draft.Id);
        Assert.NotNull(failed.PostError);
        Assert.Equal(InvoiceStatus.Posted, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == original.Id)).Status);
        Assert.Equal(InvoiceStatus.Draft, (await fx.S.Db.CommercialInvoices.AsNoTracking().SingleAsync(i => i.Id == draft.Id)).Status);
        Assert.Single(await fx.S.Db.SettlementAllocations.Where(a => a.VoidedAt == null && a.ForeignAmount > 0).ToListAsync());
        await fx.S.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER test_fail_reallocation;");
        fx.S.Db.ChangeTracker.Clear();
        var recovered = await svc.RetryAsync(fx.H.Approver, failed.Id);
        Assert.Null(recovered.PostError); Assert.Equal(2, sim.AcceptedCount);
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }
}
