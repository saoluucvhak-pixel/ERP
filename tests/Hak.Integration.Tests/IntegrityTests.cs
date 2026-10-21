using Hak.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Tính toàn vẹn của mô hình dữ liệu và dữ liệu đầu vào độc hại (kiểm toán 21/10/2026: OPS-07, OPS-14).</summary>
public class IntegrityTests
{
    // Nếu ai đó đổi entity mà quên sinh migration (hoặc migration của hai nhà cung cấp lệch nhau), CSDL mới tạo sẽ lệch mô hình đang chạy.
    [Fact]
    public async Task TheMigrationsMatchTheModel_OnTheProviderUnderTest()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        Assert.False(s.Db.Database.HasPendingModelChanges(), "Mô hình có thay đổi chưa có migration.");
    }

    // Văn bản độc hại được lưu và trả về nguyên văn; không phá bảng, không bị hiểu thành lệnh hay HTML (giao diện thoát ký tự bằng React).
    [Theory]
    [InlineData("'; DROP TABLE vouchers; --")]
    [InlineData("\" OR 1=1 --")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("=HYPERLINK(\"http://x\",\"a\")")]
    public async Task HostileText_IsStoredVerbatim_AndNeverReachesTheSqlOrBreaksTheLedger(string payload)
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var partner = await s.Setup.CreatePartnerAsync(h.Admin, "XSS01", payload, PartnerRoles.Customer);
        var posted = await h.PostedAsync(s, h.Sale(1000) with { Description = payload });

        Assert.Equal(payload, (await s.Db.BusinessPartners.AsNoTracking().SingleAsync(p => p.Id == partner.Id)).Name);
        Assert.Equal(payload, (await s.Db.Vouchers.AsNoTracking().SingleAsync(v => v.Id == posted.Id)).Description);
        Assert.True(await s.Db.Vouchers.AnyAsync());                                         // bảng vẫn còn
        var found = await s.Db.BusinessPartners.AsNoTracking().Where(p => p.Name == payload).CountAsync();
        Assert.Equal(1, found);
        Assert.True((await s.Ledger.TrialBalanceAsync(h.Auditor, new(2026, 1, 1), new(2026, 12, 31))).IsBalanced);
    }
}
