using Hak.Application.Trade;
using Hak.Domain.Common;
using Hak.Domain.Trade;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Bổ sung / thu hồi CKTM bằng hạch toán điều chỉnh khi quyết toán gốc là chứng từ và chưa có hóa đơn liên kết (DSC-07, chị chốt 27/10/2026).</summary>
public partial class TradeTests
{
    [Fact]
    public async Task RebateVoucherAmend29_LateSaleSupplementsByVoucher_NoVatChange_RootStaysFrozen()
    {
        using var fx = await SetupAsync();
        await Sale28Async(fx, "ROOTV", 100_000_000);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "VAMEND", "Voucher chain", D(3, 1), D(3, 31), [new(0, 2)]));
        var root = await service.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1));
        await PostVoucherAsync(fx, root.VoucherId!.Value);
        var frozen = (await fx.S.Db.DiscountAgreementRuns.AsNoTracking().SingleAsync(r => r.Id == root.Id)).SettlementSnapshot;
        Assert.Equal(2_000_000, await fx.Bal("SALESDISC"));

        await Sale28Async(fx, "LATEV", 20_000_000);                                                       // hóa đơn trễ làm doanh số 120 triệu
        var vatBefore = await fx.Bal("VATOUT");
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => service.AmendmentPreviewAsync(fx.H.Auditor, a.Id)));   // chế độ hóa đơn: chưa có hóa đơn liên kết
        var p = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id, byVoucher: true);
        Assert.Equal(400_000, p.Difference);

        var child = await service.AmendByVoucherAsync(fx.H.Preparer, a.Id, p.RowVersion, p.PreviousRunId, p.SourceHash, "Hóa đơn bán trễ", D(4, 5));
        Assert.Equal(root.Id, child.ParentRunId);
        Assert.NotNull(child.VoucherId);
        await PostVoucherAsync(fx, child.VoucherId!.Value);
        Assert.Equal(2_400_000, await fx.Bal("SALESDISC"));
        Assert.Equal(vatBefore, await fx.Bal("VATOUT"));                                                  // chế độ chứng từ không đổi thuế GTGT
        Assert.Equal(frozen, (await fx.S.Db.DiscountAgreementRuns.AsNoTracking().SingleAsync(r => r.Id == root.Id)).SettlementSnapshot);
        Assert.Empty((await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id, byVoucher: true)).Lines);   // không tự sinh thêm quyền lợi
        Assert.Equal(ErrorCodes.ConcurrencyConflict, await CodeOf(() => service.AmendByVoucherAsync(fx.H.Preparer, a.Id, p.RowVersion, p.PreviousRunId, p.SourceHash, "Lần hai", D(4, 6))));
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }

    [Fact]
    public async Task RebateVoucherAmend29_ClawbackGoesToTheDiscountAccount_NotRevenue_AndPostingDateIsGuarded()
    {
        using var fx = await SetupAsync();
        await Sale28Async(fx, "A", 100_000_000);
        var b = await Sale28Async(fx, "B", 20_000_000);
        var service = await RebateService27Async(fx);
        var a = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "VCLAW", "Voucher clawback", D(3, 1), D(3, 31), [new(0, 2)]));
        var root = await service.SettleByVoucherAsync(fx.H.Preparer, a.Id, a.RowVersion, D(4, 1));
        await PostVoucherAsync(fx, root.VoucherId!.Value);
        Assert.Equal(2_400_000, await fx.Bal("SALESDISC"));
        var allowance = await fx.Inv.CreateAdjustmentAsync(fx.H.Preparer, new(b.Id, InvoicePurpose.Discount, "T29", "ALLOW", D(4, 2), D(4, 2), "Giảm giá sau bán", [new(1, Amount: 10_000_000)], DiscountKind.Allowance));
        await fx.Inv.PostAsync(fx.H.Approver, allowance.Id);

        var p = await service.AmendmentPreviewAsync(fx.H.Auditor, a.Id, byVoucher: true);
        Assert.Equal(-200_000, p.Difference);                                                              // doanh số 110 triệu × 2% = 2,2 triệu
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => service.AmendByVoucherAsync(fx.H.Preparer, a.Id, p.RowVersion, p.PreviousRunId, p.SourceHash, "Quá sớm", D(3, 15))));   // trước cuối kỳ thỏa thuận
        var child = await service.AmendByVoucherAsync(fx.H.Preparer, a.Id, p.RowVersion, p.PreviousRunId, p.SourceHash, "Giảm giá làm giảm doanh số", D(4, 5));
        await PostVoucherAsync(fx, child.VoucherId!.Value);
        Assert.Equal(12_200_000, await fx.Bal("SALESDISC"));                                                // 10 triệu giảm giá + 2,2 triệu CKTM; thu hồi ghi Có vào TK chiết khấu
        Assert.Equal(-120_000_000, await fx.Bal("SALESREV"));                                              // doanh thu không đổi
        await LedgerInvariants.AssertAsync(fx.S.Db);
    }
}
