using Hak.Domain.Trade;

namespace Hak.Integration.Tests;

public partial class TradeTests
{
    [Theory]
    [InlineData(InvoicePurpose.Increase, 20, 2_400_000)]
    [InlineData(InvoicePurpose.Increase, 21, 2_000_000)]
    [InlineData(InvoicePurpose.Discount, 20, 1_600_000)]
    [InlineData(InvoicePurpose.Discount, 21, 2_000_000)]
    public async Task RebateCutoff30_AccrualOnlyIncludesAdjustmentsPostedByItsDate(InvoicePurpose purpose, int day, decimal expected)
    {
        using var fx = await SetupAsync();
        var original = await Sale28Async(fx, "CUTOFF30", 100_000_000);
        var correction = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer,
            new(original.Id, purpose, "T30", "CORRECTION", D(3, day), D(3, day),
                "Price correction", [new(1, Amount: 20_000_000)]));
        await fx.Inv.PostAsync(fx.H.Approver, correction.Id);
        var service = await RebateService27Async(fx);
        var agreement = await service.CreateAsync(fx.H.Approver,
            new(InvoiceKind.Sales, fx.H.Partner["KH01"], "CUTOFF30", "Accrual cutoff",
                D(3, 1), D(3, 31), [new(0, 2)]));
        var accrual = await service.AccrueAsync(fx.H.Preparer, agreement.Id, agreement.RowVersion, D(3, 20));
        Assert.Equal(expected, accrual.Adjustment);
        var finalAmount = purpose == InvoicePurpose.Increase ? 2_400_000m : 1_600_000m;
        Assert.Equal(finalAmount, (await service.PreviewAsync(fx.H.Auditor, agreement.Id)).Amount);
        if (day > 20)
        {
            await PostVoucherAsync(fx, accrual.VoucherId);
            var next = await service.AccrueAsync(fx.H.Preparer, agreement.Id, agreement.RowVersion, D(3, 31));
            Assert.Equal(finalAmount, next.Estimated);
            Assert.Equal(finalAmount - expected, next.Adjustment);
        }
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }
}
