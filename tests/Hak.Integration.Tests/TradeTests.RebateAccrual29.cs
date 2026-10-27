using Hak.Application.Trade;
using Hak.Domain.Common;
using Hak.Domain.Trade;

namespace Hak.Integration.Tests;

/// <summary>Trích trước CKTM không lùi ngày (rà soát main 26/10/2026: DSC-09).</summary>
public partial class TradeTests
{
    [Fact]
    public async Task RebateAccrual29_AccrualDateCannotMoveBackwards()
    {
        using var fx = await SetupAsync();
        await Sale28Async(fx, "ACC", 100_000_000);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "ACC", "Accrual order", D(3, 1), D(3, 31), [new(0, 2)]));
        var later = await service.AccrueAsync(fx.H.Preparer, a.Id, a.RowVersion, D(3, 20));
        await PostVoucherAsync(fx, later.VoucherId);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => service.AccrueAsync(fx.H.Preparer, a.Id, a.RowVersion, D(3, 10))));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }
}
