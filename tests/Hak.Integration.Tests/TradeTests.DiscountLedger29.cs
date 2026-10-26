using Hak.Application.Trade;
using Hak.Domain.Common;
using Hak.Domain.Trade;

namespace Hak.Integration.Tests;

/// <summary>Toàn vẹn sổ cái của chiết khấu (rà soát 26/10/2026: DSC-01).</summary>
public partial class TradeTests
{
    [Fact]
    public async Task DiscountLedger29_AccrualCannotBeReversed_WhileASettlementVoucherDraftStillClearsIt()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        await PostedSaleAsync(fx, "LEDGER-1", 10, D(3, 3));
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "LEDGER", "Accrual then settlement draft", D(3, 1), D(3, 31), [new(0, 2)]));
        var accrued = await service.AccrueAsync(fx.H.Preparer, a.Id, a.RowVersion, D(3, 31));
        await PostVoucherAsync(fx, accrued.VoucherId);
        var run = await service.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1));      // nháp bù trừ số đã trích trước

        // Đảo trích trước khi nháp quyết toán còn dùng nó sẽ làm sai giảm trừ doanh thu và TK phải trả chiết khấu.
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.S.Engine.ReverseAsync(fx.H.Approver, accrued.VoucherId, D(4, 2), "Reverse accrual under a live settlement draft")));

        // Hủy quyết toán nháp thì đảo được.
        await service.VoidRunAsync(fx.H.Approver, a.Id, "Làm lại quyết toán", run.Id);
        await fx.S.Engine.ReverseAsync(fx.H.Approver, accrued.VoucherId, D(4, 2), "Reverse accrual after the draft was voided");
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    // DSC-02: hóa đơn gốc nằm trong quyết toán CKTM còn hiệu lực không đảo / thay thế được; hủy quyết toán xong thì đảo được.
    [Fact]
    public async Task DiscountLedger29_SourceInvoiceOfALiveSettlement_CannotBeReversed_UntilTheSettlementIsUndone()
    {
        using var fx = await SetupAsync();
        await PostedReceiptAsync(fx, 100, 5_000_000, D(3, 1), fx.Add);
        var original = await PostedSaleAsync(fx, "SRC-1", 10, D(3, 3));
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new AgreementInput(InvoiceKind.Sales, fx.H.Partner["KH01"], "SRC", "Source invoice guard", D(3, 1), D(3, 31), [new(0, 2)]));
        var run = await service.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1));      // còn nháp vẫn tính

        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => fx.Inv.ReverseAsync(fx.H.Approver, original.Id, D(4, 2), "Source of a live rebate settlement")));

        await service.VoidRunAsync(fx.H.Approver, a.Id, "Làm lại", run.Id);
        await fx.Inv.ReverseAsync(fx.H.Approver, original.Id, D(4, 2), "Settlement was undone");
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }
}
