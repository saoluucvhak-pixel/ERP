using Hak.Application.Common;
using Hak.Domain.Common;
using Hak.Domain.Vouchers;

namespace Hak.Integration.Tests;

/// <summary>Bộ mã chứng từ: quy tắc của danh mục (không trùng, dễ nhớ) và tùy chỉnh theo công ty.</summary>
public class NumberingTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    [Fact]
    public void Registry_FollowsTheRules_UniqueKeysAndPrefixes_NoPrefixIsTheStartOfAnother()
    {
        var all = DocumentKinds.All;
        Assert.Equal(all.Count, all.Select(k => k.Key).Distinct().Count());
        Assert.Equal(all.Count, all.Select(k => k.Prefix).Distinct().Count());
        foreach (var k in all) Assert.Null(DocumentKinds.PrefixProblem(k.Prefix));
        foreach (var a in all) foreach (var b in all)
            if (a != b) Assert.False(b.Prefix.StartsWith(a.Prefix, StringComparison.Ordinal), $"{a.Prefix} ({a.Name}) là phần đầu của {b.Prefix} ({b.Name})");
        // Mọi loại chứng từ ghi sổ đều có trong danh mục (không rơi vào nhánh "khác").
        foreach (var v in VoucherTypes.All) Assert.Contains(all, k => k.Key == v);
        Assert.Equal("HDB2026-000123", Numbering.Format("HDB", "2026", 123, 6));
    }

    [Fact]
    public async Task CompanyOverride_ChangesOnlyNewNumbers_KeepsTheSequence_AndRejectsClashes()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var svc = new NumberingService(s.Db, s.Audit, h.Clock);
        h.Clock.UtcNow = new DateTime(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc);
        var v1 = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1_000));
        Assert.Equal("GJ2026-000001", v1.Number);

        // Quy tắc tiền tố; trùng; là phần đầu của tiền tố khác (và ngược lại).
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.SetAsync(h.Admin, "GJ", "g", null)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.SetAsync(h.Admin, "GJ", "Gj1", null)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.SetAsync(h.Admin, "GJ", null, 3)));
        Assert.Equal(ErrorCodes.DuplicateCode, await CodeOf(() => svc.SetAsync(h.Admin, "GJ", "CLS", null)));
        Assert.Equal(ErrorCodes.DuplicateCode, await CodeOf(() => svc.SetAsync(h.Admin, "GJ", "HDB", null)));
        Assert.Equal(ErrorCodes.DuplicateCode, await CodeOf(() => svc.SetAsync(h.Admin, "GJ", "HD", null)));     // là phần đầu của HDB…
        Assert.Equal(ErrorCodes.DuplicateCode, await CodeOf(() => svc.SetAsync(h.Admin, "GJ", "HDBX", null)));   // HDB là phần đầu của HDBX
        Assert.Equal(ErrorCodes.NotFound, await CodeOf(() => svc.SetAsync(h.Admin, "ZZ", "ZZZ", null)));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => svc.SetAsync(h.Auditor, "GJ", "NKC", null)));

        // Đổi tiền tố và độ rộng: số mới theo quy tắc mới, số thứ tự tiếp tục (không về 1), số cũ giữ nguyên.
        await svc.SetAsync(h.Admin, "GJ", "nkc", 5);
        var v2 = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(2_000));
        Assert.Equal(("GJ2026-000001", "NKC2026-00002"), (v1.Number, v2.Number));
        var row = (await svc.ListAsync(h.Auditor, new(2026, 3, 10))).Single(r => r.Key == "GJ");
        Assert.Equal(("NKC", 5, true, 2L), (row.Prefix, row.Width, row.Customized, row.LastIssued));

        // Về mặc định.
        await svc.SetAsync(h.Admin, "GJ", null, null);
        Assert.Equal("GJ2026-000003", (await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(3_000))).Number);
    }
}
