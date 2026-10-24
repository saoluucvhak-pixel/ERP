using Hak.Application.Trade;
using Hak.Domain.Trade;

namespace Hak.Integration.Tests;

/// <summary>ERP-R06: cảnh báo hợp đồng phải quét toàn bộ hợp đồng còn hiệu lực, không bị cắt theo 500 mã đầu của danh sách.</summary>
public class ContractAlertsScaleTests
{
    [Fact]
    public async Task Alerts_CoverActiveContracts_BeyondTheFirst500Codes()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        h.Acc["INV"] = (await s.Setup.CreateAccountAsync(h.Admin, "INV", "Hàng tồn kho (thử)")).Id;
        var dam = (await s.Stock.CreateItemAsync(h.Admin, "DAM", "Dăm gỗ", "tấn", "INV")).Id;
        var contracts = new ContractService(s.Db, s.Audit, h.Clock);
        h.Clock.UtcNow = new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc);

        ContractInput Input(string name, DateOnly expires) => new(InvoiceKind.Sales, name, ContractCategory.Quantity, h.Partner["KH01"], new(2026, 3, 1), expires, [new(dam, 1000, 2_000_000, 10, 8)]);

        // Mã nhỏ nhất (HDKB2026-000001) là hợp đồng Active sắp hết hạn; 500 hợp đồng sau đó còn là bản nháp → danh sách (mã giảm dần, 500 dòng) không chứa nó.
        var first = await contracts.CreateAsync(h.Preparer, Input("Sắp hết hạn", new(2026, 4, 1)));
        await contracts.SubmitAsync(h.Preparer, first.Id);
        await contracts.DecideAsync(h.Approver, first.Id, true, null);
        for (var i = 0; i < 500; i++) await contracts.CreateAsync(h.Preparer, Input($"Nháp {i}", new(2026, 12, 31)));

        var list = await contracts.ListAsync(h.Auditor);
        Assert.Equal(500, list.Count);
        Assert.DoesNotContain(list, r => r.Id == first.Id);   // chứng minh điều kiện: hợp đồng cần cảnh báo nằm ngoài trang đầu

        var alerts = await contracts.AlertsAsync(h.Auditor);
        var a = Assert.Single(alerts, x => x.ContractId == first.Id);
        Assert.Equal("expiring30", a.Kind);
        Assert.Equal(first.Code, a.ContractCode);
    }
}
