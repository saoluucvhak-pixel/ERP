using Hak.Application.Trade;
using Hak.Domain.Trade;

namespace Hak.Integration.Tests;

/// <summary>Cảnh báo (không cấm) khi hai thỏa thuận CKTM của cùng đối tượng có kỳ giao nhau (rà soát main 26/10/2026, mục 4).</summary>
public partial class TradeTests
{
    [Fact]
    public async Task RebateOverlap29_PreviewListsOverlappingAgreements_WithoutBlockingEitherOne()
    {
        using var fx = await SetupAsync();
        await Sale28Async(fx, "OVL", 100_000_000);
        var service = await RebateService27Async(fx);
        var first = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "OVL-A", "Tháng 3 – volume", D(3, 1), D(3, 31), [new(0, 2)]));
        var second = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "OVL-B", "Quý 1 – loyalty", D(1, 1), D(3, 31), [new(0, 1)]));   // vẫn lập được
        var apart = await service.CreateAsync(fx.H.Approver, new(InvoiceKind.Sales, fx.H.Partner["KH01"], "OVL-C", "Quý 2", D(4, 1), D(6, 30), [new(0, 1)]));          // không giao nhau

        var preview = await service.PreviewAsync(fx.H.Auditor, first.Id);
        Assert.Equal("OVL-B", Assert.Single(preview.Overlaps!).Code);
        Assert.Equal(2_000_000, preview.Amount);                                                  // cảnh báo không đổi số tính của thỏa thuận này
        Assert.Null((await service.PreviewAsync(fx.H.Auditor, apart.Id)).Overlaps);

        await service.SettleAsync(fx.H.Preparer, second.Id, second.RowVersion, "T29", "OVL-ROOT", D(4, 1), D(4, 1));
        var after = (await service.PreviewAsync(fx.H.Auditor, first.Id)).Overlaps!.Single();
        Assert.True(after.Settled);
        Assert.Equal(1_000_000, after.SettledAmount);                                             // lịch sử đã cấp của chương trình kia hiển thị cho người xem
    }
}
