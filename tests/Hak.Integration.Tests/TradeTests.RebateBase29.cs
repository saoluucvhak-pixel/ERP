using Microsoft.EntityFrameworkCore;
using Hak.Application.Trade;
using Hak.Domain.Common;
using Hak.Domain.Trade;

namespace Hak.Integration.Tests;

/// <summary>Cơ sở doanh số CKTM tính cả tăng giá / sửa sai doanh thu đã ghi sổ (rà soát main 26/10/2026, mục 1).</summary>
public partial class TradeTests
{
    [Fact]
    public async Task RebateBase29_PostedPriceIncreaseRaisesTheSalesBase_DraftDoesNot()
    {
        using var fx = await SetupAsync();
        var sale = await Sale28Async(fx, "BASE", 100_000_000);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "BASE", "Increase base", D(3, 1), D(3, 31), [new(0, 2)]));
        var increase = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new(sale.Id, InvoicePurpose.Increase, "T29", "INC-1", D(3, 20), D(3, 20), "Sửa sai đơn giá", [new(1, Amount: 20_000_000)]));

        var draft = await service.PreviewAsync(fx.H.Auditor, a.Id);
        Assert.Equal((100_000_000m, 2_000_000m), (draft.EligibleSales, draft.Amount));            // nháp chưa tăng doanh số

        await fx.Inv.PostAsync(fx.H.Approver, increase.Id);
        var posted = await service.PreviewAsync(fx.H.Auditor, a.Id);
        Assert.Equal((120_000_000m, 2_400_000m), (posted.EligibleSales, posted.Amount));          // đã ghi sổ: 2% × 120 triệu
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task RebateBase29_IncreaseAfterSettlementIsSupplementedThroughAChildRun_RootRunStaysFrozen()
    {
        using var fx = await SetupAsync();
        var sale = await Sale28Async(fx, "LATEINC", 100_000_000);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "LATEINC", "Increase after settlement", D(3, 1), D(3, 31), [new(0, 2)]));
        var root = await service.SettleAsync(fx.H.Preparer, a.Id, a.RowVersion, "T29", "ROOT", D(4, 1), D(4, 1));
        await PostRun28Async(fx, root.Id);
        var frozen = root.SettlementSnapshot;
        var increase = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new(sale.Id, InvoicePurpose.Increase, "T29", "INC-2", D(4, 2), D(4, 2), "Sửa sai đơn giá sau khi chốt", [new(1, Amount: 20_000_000)]));
        await fx.Inv.PostAsync(fx.H.Approver, increase.Id);

        var p = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id);
        Assert.Equal((2_000_000m, 2_400_000m, 400_000m), (p.PreviousAmount, p.TargetAmount, p.Difference));
        var child = await Amend28Async(fx, service, p);
        Assert.Equal(root.Id, child.ParentRunId);
        await PostRun28Async(fx, child.Id);
        Assert.Equal(2_400_000, await fx.Bal("SALESDISC"));
        Assert.Equal(frozen, (await fx.S.Db.DiscountAgreementRuns.AsNoTracking().SingleAsync(r => r.Id == root.Id)).SettlementSnapshot);
        Assert.Empty((await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id)).Lines);             // thu hồi / bổ sung không tự sinh thêm quyền lợi
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }
}
