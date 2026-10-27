using Hak.Application.EInvoice;
using Hak.Domain.Common;
using Hak.Domain.Trade;

namespace Hak.Integration.Tests;

/// <summary>Kiểm soát điều chỉnh thông tin, hóa đơn thay thế khác ngày và đăng ký hóa đơn phát hành ngoài (rà soát main 26/10/2026: EIN-02, 03, 09).</summary>
public partial class TradeTests
{
    [Fact]
    public async Task EInvoice29_InformationAdjustment_RejectsTaxIdAndMoneyFields_AndPendingDuplicates()
    {
        var (fx, sim, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var doc = await IssuedSaleAsync(fx, svc);

        foreach (var field in new[] { "Mã số thuế", "Tổng tiền", "Thuế suất", "Đơn giá", "Số lượng", "MST người mua" })
            Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.AdjustInformationAsync(fx.H.Approver, doc.Id, "Sai thông tin", [new InformationChange(field, "1", "2")])));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.AdjustInformationAsync(fx.H.Approver, doc.Id, "Không đổi gì", [new InformationChange("Địa chỉ", "Số 1", "Số 1")])));

        sim.FailTransientCalls = 1;                                                                // lệnh đầu chưa rõ kết quả → còn Issuing
        var first = await svc.AdjustInformationAsync(fx.H.Approver, doc.Id, "Sai địa chỉ", [new InformationChange("Địa chỉ", "Số 1", "Số 2")]);
        Assert.Equal("Issuing", first.Status);
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => svc.AdjustInformationAsync(fx.H.Approver, doc.Id, "Sai địa chỉ lần nữa", [new InformationChange("Địa chỉ", "Số 2", "Số 3")])));
    }

    [Fact]
    public async Task EInvoice29_ReplacementCanUseANewInvoiceAndPostingDate_ButNotEarlierThanTheOriginal()
    {
        var (fx, _, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var original = await IssuedSaleAsync(fx, svc);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.CreateReplacementDraftAsync(fx.H.Preparer, original.InvoiceId!.Value, "Lùi ngày", null, null, D(3, 1), D(3, 1))));
        var draft = await svc.CreateReplacementDraftAsync(fx.H.Preparer, original.InvoiceId!.Value, "Phát hiện sai ở kỳ sau", [new ReplacementLineChange(1, UnitPrice: 90_000)], null, D(4, 5), D(4, 5));
        Assert.Equal((D(4, 5), D(4, 5)), (draft.InvoiceDate, draft.PostingDate));
    }

    [Fact]
    public async Task EInvoice29_RegisteringAnExternalInvoice_NeedsPostingRightsAndAWrittenEvidence()
    {
        var (fx, _, svc, _) = await EInvoiceSetupAsync(); using var _fx = fx;
        var manual = await PostedSaleAsync(fx, "EXT-1", 10, D(3, 3));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.RegisterExternalAsync(fx.H.Approver, manual.Id, " ")));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => svc.RegisterExternalAsync(fx.H.Preparer, manual.Id, "Phát hành trên cổng nhà cung cấp, mã tra cứu ABC123")));
        var reg = await svc.RegisterExternalAsync(fx.H.Approver, manual.Id, "Phát hành trên cổng nhà cung cấp, mã tra cứu ABC123");
        Assert.Equal("Issued", reg.Status);
    }
}
