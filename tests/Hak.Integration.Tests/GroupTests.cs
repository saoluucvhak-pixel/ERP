using Hak.Application.Assets;
using Hak.Application.Setup;
using Hak.Domain.Catalog;
using Hak.Domain.Common;

namespace Hak.Integration.Tests;

/// <summary>Nhóm danh mục nhiều cấp, mỗi mục thuộc nhiều nhóm.</summary>
public class GroupTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    [Fact]
    public async Task Tree_MultiLevel_MembershipInManyGroups_Descendants_AndRules()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var svc = new GroupService(s.Db, s.Audit, h.Clock, new AssetService(s.Db, s.Engine, s.Audit, h.Clock), s.Stock);
        // Nhóm khách hàng: theo khu vực (nhiều cấp) và theo ngành — khách thuộc cả hai.
        var mien = await svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "kv-mien-trung", "Miền Trung", null));
        Assert.Equal("KV-MIEN-TRUNG", mien.Code);
        var dn = await svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "KV-DN", "Đà Nẵng", mien.Id));
        var qn = await svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "KV-QN", "Quảng Nam", mien.Id));
        var xk = await svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "NG-XK", "Ngành xuất khẩu", null));
        Assert.Equal(ErrorCodes.DuplicateCode, await CodeOf(() => svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "KV-DN", "Trùng", null))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "kv dn", "Mã sai", null))));
        Assert.Equal(ErrorCodes.NotFound, await CodeOf(() => svc.CreateAsync(h.Admin, new(GroupDomain.Item, "VT", "Cha khác loại", dn.Id))));       // cha thuộc loại danh mục khác
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => svc.CreateAsync(h.Auditor, new(GroupDomain.Partner, "ZZ", "x", null))));

        await svc.SetGroupsOfAsync(h.Admin, GroupDomain.Partner, h.Partner["KH01"], [dn.Id, xk.Id]);   // KH01: Đà Nẵng + xuất khẩu
        await svc.SetGroupsOfAsync(h.Admin, GroupDomain.Partner, h.Partner["KH02"], [qn.Id]);
        Assert.Equal(ErrorCodes.NotFound, await CodeOf(() => svc.SetGroupsOfAsync(h.Admin, GroupDomain.Item, h.Partner["KH01"], [])));            // đối tượng không phải vật tư
        Assert.Equal(ErrorCodes.NotFound, await CodeOf(() => svc.SetGroupsOfAsync(h.Admin, GroupDomain.Partner, h.Partner["KH01"], [Guid.NewGuid()])));

        var tree = await svc.TreeAsync(h.Auditor, GroupDomain.Partner);
        Assert.Equal(["KV-MIEN-TRUNG", "KV-DN", "KV-QN", "NG-XK"], tree.Select(r => r.Code).ToArray());
        Assert.Equal([0, 1, 1, 0], tree.Select(r => r.Level).ToArray());
        var mienRow = tree[0];
        Assert.Equal((0, 2, 2), (mienRow.DirectMembers, mienRow.AllMembers, mienRow.Children));          // nhóm cha gồm cả hai khách qua nhóm con
        Assert.Equal(2, (await svc.MembersAsync(h.Auditor, mien.Id, true)).Count);
        Assert.Empty(await svc.MembersAsync(h.Auditor, mien.Id, false));
        var kh01 = (await svc.MembersAsync(h.Auditor, dn.Id, true)).Single();
        Assert.Equal("KH01", kh01.Code);
        Assert.Equal([xk.Id], kh01.OtherGroupIds.ToArray());                       // thuộc thêm nhóm xuất khẩu

        // Không tạo vòng; không xóa nhóm còn con / thành viên; thêm / bỏ thành viên hàng loạt.
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.UpdateAsync(h.Admin, mien.Id, "Miền Trung", dn.Id, null, true)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.DeleteAsync(h.Admin, mien.Id)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.DeleteAsync(h.Admin, dn.Id)));
        Assert.Equal(1, await svc.AddMembersAsync(h.Admin, xk.Id, [h.Partner["KH01"], h.Partner["NCC01"]]));   // KH01 đã có
        await svc.RemoveMembersAsync(h.Admin, xk.Id, [h.Partner["NCC01"]]);
        await svc.SetGroupsOfAsync(h.Admin, GroupDomain.Partner, h.Partner["KH01"], [xk.Id]);                // bỏ khỏi Đà Nẵng
        await svc.DeleteAsync(h.Admin, dn.Id);
        Assert.Equal(["KV-MIEN-TRUNG", "KV-QN", "NG-XK"], (await svc.TreeAsync(h.Auditor, GroupDomain.Partner)).Select(r => r.Code).ToArray());
        // Ngừng dùng thì không gán thêm được.
        await svc.UpdateAsync(h.Admin, qn.Id, "Quảng Nam", mien.Id, null, false);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.SetGroupsOfAsync(h.Admin, GroupDomain.Partner, h.Partner["KH01"], [qn.Id])));
        Assert.Equal(2, (await svc.LinksAsync(h.Auditor, GroupDomain.Partner)).Count(l => l.GroupId == xk.Id) + 1);   // KH01 + KH02 còn đúng liên kết
    }

    [Fact]
    public async Task Summary_ByGroup_CountsAnItemInEveryGroupItBelongsTo_AndReportsUngrouped()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var svc = new GroupService(s.Db, s.Audit, h.Clock, new AssetService(s.Db, s.Engine, s.Audit, h.Clock), s.Stock);
        h.Clock.UtcNow = new DateTime(2026, 3, 31, 8, 0, 0, DateTimeKind.Utc);
        // Phải thu theo nhóm khách hàng: KH01 nợ 1.000, KH02 nợ 500 (HĐ bán ghi sổ), KH01 thuộc hai nhóm.
        await s.Rules.SetRoleMappingAsync(h.Admin, "AR_TRADE", "AR");
        await h.PostedAsync(s, h.Sale(1_000, new(2026, 3, 10), "KH01"));
        await h.PostedAsync(s, h.Sale(500, new(2026, 3, 11), "KH02"));
        var a = await svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "A", "Nhóm A", null));
        var b = await svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "B", "Nhóm B", null));
        await svc.SetGroupsOfAsync(h.Admin, GroupDomain.Partner, h.Partner["KH01"], [a.Id, b.Id]);
        await svc.SetGroupsOfAsync(h.Admin, GroupDomain.Partner, h.Partner["KH02"], [b.Id]);
        var sum = await svc.SummaryAsync(h.Auditor, GroupDomain.Partner, new(2026, 3, 31));
        Assert.Equal(["receivable", "payable"], sum.Columns.Select(c => c.Key).ToArray());
        Assert.Equal((1, 1_000m), (sum.Rows.Single(r => r.Code == "A").Count, sum.Rows.Single(r => r.Code == "A").Values["receivable"]));
        Assert.Equal((2, 1_500m), (sum.Rows.Single(r => r.Code == "B").Count, sum.Rows.Single(r => r.Code == "B").Values["receivable"]));
        Assert.Equal((1, 0m), (sum.UngroupedCount, sum.UngroupedValues["receivable"]));           // chỉ NCC01 chưa thuộc nhóm nào
    }

    [Fact]
    public async Task PartnerDefaults_OwnValueWins_ThenDeepestGroup_ThenStrictest()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var svc = new GroupService(s.Db, s.Audit, h.Clock, new AssetService(s.Db, s.Engine, s.Audit, h.Clock), s.Stock);
        var cha = await svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "KV", "Khu vực", null, null, 500_000_000m, 30));
        var con = await svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "KV-DN", "Đà Nẵng", cha.Id, null, 200_000_000m, null));
        var nganh = await svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "NG", "Ngành", null, null, 100_000_000m, 15));
        var ngCon = await svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "NG-A", "Ngành A", nganh.Id, null, 150_000_000m, 45));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.CreateAsync(h.Admin, new(GroupDomain.Item, "VT", "Vật tư", null, null, 1m, null))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "AM", "Âm", null, null, -1m, null))));

        var p = await s.Db.BusinessPartners.FindAsync(h.Partner["KH01"]);
        p!.CreditLimit = null; p.PaymentTermsDays = null;
        await s.Db.SaveChangesAsync();
        // Chưa thuộc nhóm nào: không có mặc định.
        var none = await GroupDefaults.PartnerTermsAsync(s.Db, h.Admin.CompanyId, p);
        Assert.Null(none.CreditLimit); Assert.Null(none.PaymentTermsDays);

        await svc.SetGroupsOfAsync(h.Admin, GroupDomain.Partner, p.Id, [con.Id, ngCon.Id]);       // cùng độ sâu 1: lấy giá trị chặt nhất
        var t = await GroupDefaults.PartnerTermsAsync(s.Db, h.Admin.CompanyId, p);
        Assert.Equal(150_000_000m, t.CreditLimit);         // min(200tr, 150tr)
        Assert.Equal(30, t.PaymentTermsDays);              // KV-DN không khai ngày → kế thừa nhóm cha KV (30); min(30, 45)
        await svc.SetPrimaryGroupAsync(h.Admin, GroupDomain.Partner, p.Id, con.Id);               // nhóm chính thắng, không xét nhóm khác
        t = await GroupDefaults.PartnerTermsAsync(s.Db, h.Admin.CompanyId, p);
        Assert.Equal(200_000_000m, t.CreditLimit); Assert.Equal(30, t.PaymentTermsDays);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.SetPrimaryGroupAsync(h.Admin, GroupDomain.Partner, p.Id, nganh.Id)));   // không thuộc nhóm đó
        await svc.SetPrimaryGroupAsync(h.Admin, GroupDomain.Partner, p.Id, null);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => svc.CreateAsync(h.Admin, new(GroupDomain.Partner, "VAT", "Sai loại", null, null, null, null, 10m))));   // thuế suất chỉ cho nhóm vật tư
        await svc.SetGroupsOfAsync(h.Admin, GroupDomain.Partner, p.Id, [cha.Id, ngCon.Id]);      // nhóm sâu hơn thắng nhóm cha
        t = await GroupDefaults.PartnerTermsAsync(s.Db, h.Admin.CompanyId, p);
        Assert.Equal(150_000_000m, t.CreditLimit); Assert.Equal(45, t.PaymentTermsDays);
        p.CreditLimit = 900_000_000m;                      // giá trị của chính đối tượng luôn thắng
        t = await GroupDefaults.PartnerTermsAsync(s.Db, h.Admin.CompanyId, p);
        Assert.Equal(900_000_000m, t.CreditLimit); Assert.Equal("đối tượng", t.LimitSource);
        await svc.UpdateAsync(h.Admin, ngCon.Id, "Ngành A", nganh.Id, null, false, 150_000_000m, 45);   // nhóm ngừng dùng bị bỏ qua
        t = await GroupDefaults.PartnerTermsAsync(s.Db, h.Admin.CompanyId, p);
        Assert.Equal(30, t.PaymentTermsDays);
    }
}
