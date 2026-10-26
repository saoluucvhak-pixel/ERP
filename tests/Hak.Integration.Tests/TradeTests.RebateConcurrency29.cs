using Hak.Application.Trade;
using Hak.Domain.Common;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Hai người cùng bấm quyết toán một thỏa thuận CKTM (hai kết nối riêng): chỉ một lần quyết toán gốc hiệu lực (rà soát main 26/10/2026, mục 5).</summary>
public partial class TradeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RebateConcurrency29_TwoSettlementsOfTheSameAgreement_OnlyOneRootRunSurvives(bool byVoucher)
    {
        using var fx = await SetupAsync();
        await Sale28Async(fx, "RACE", 100_000_000);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "RACE", "Concurrent settlement", D(3, 1), D(3, 31), [new(0, 2)]));

        using var s1 = fx.H.NewScope();
        using var s2 = fx.H.NewScope();
        DiscountAgreementService Svc(Scope s) => new(s.Db, new InvoiceService(s.Db, s.Engine, s.Stock, s.Audit, fx.H.Clock), s.Audit, fx.H.Clock, s.Engine);
        var (svc1, svc2) = (Svc(s1), Svc(s2));
        Task Settle(DiscountAgreementService svc, string no) => byVoucher
            ? svc.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1))
            : svc.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T29", no, D(4, 1), D(4, 1));
        var (ok, failed) = await ConcurrencyTests.RaceAsync(() => Settle(svc1, "RACE-1"), () => Settle(svc2, "RACE-2"));

        Assert.Equal(1, ok);
        Assert.All(failed, f => Assert.IsType<DomainException>(f));
        using var check = fx.H.NewScope();
        Assert.Equal(1, await check.Db.DiscountAgreementRuns.CountAsync(r => r.AgreementId == a.Id && r.VoidedAt == null && r.ParentRunId == null));
        await LedgerInvariants.AssertAsync(check.Db);
    }
}
