using Hak.Application.Security;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Three default roles (data keeper / preparer-approver / admin) and the per-company prepare-vs-approve switch.</summary>
public class ApprovalPolicyTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    [Fact]
    public async Task DefaultRoles_AreExactlyThree_AndExistInANewDatabase_LegacyRolesAreSeparate()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        Assert.True(RoleCatalog.Defaults.Keys.ToHashSet().SetEquals([RoleCatalog.Admin, RoleCatalog.DataKeeper, RoleCatalog.Accountant]));
        Assert.All(RoleCatalog.Legacy.Keys, k => Assert.False(RoleCatalog.Defaults.ContainsKey(k)));
        var codes = await s.Db.Roles.Select(r => r.Code).ToListAsync();
        Assert.All(RoleCatalog.Defaults.Keys, k => Assert.Contains(k, codes));
        // The accountant role can do what the preparer and the approver could do separately.
        var perms = (await s.Db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Code == RoleCatalog.Accountant)).Permissions.Select(p => p.Permission).ToHashSet();
        Assert.Superset(new HashSet<string> { Permissions.VoucherCreate, Permissions.VoucherSubmit, Permissions.VoucherApprove, Permissions.VoucherPost }, perms);
    }

    [Fact]
    public async Task DataKeeper_MaintainsCatalogs_ButCannotTouchVouchers()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var u = (await s.Setup.CreateUserAsync(h.AdminId, "dulieu", "Người duy trì dữ liệu", TestHost.Password)).Id;
        await s.Setup.GrantRoleAsync(h.AdminId, u, h.CompanyId, RoleCatalog.DataKeeper);
        var keeper = await s.Auth.ResolveContextAsync(u, h.CompanyId);
        Assert.Contains(Permissions.CatalogManage, keeper.Permissions);
        Assert.DoesNotContain(Permissions.VoucherCreate, keeper.Permissions);
        Assert.DoesNotContain(Permissions.VoucherApprove, keeper.Permissions);
        Assert.DoesNotContain(Permissions.ConfigManage, keeper.Permissions);
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Engine.CreateDraftAsync(keeper, h.Sale(1000))));
    }

    [Fact]
    public async Task Accountant_PreparesAndApproves_TwoPeopleByDefault_OnePersonWhenTheCompanySwitchesSeparationOff()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var a = (await s.Setup.CreateUserAsync(h.AdminId, "ke1", "Kế toán 1", TestHost.Password)).Id;
        var b = (await s.Setup.CreateUserAsync(h.AdminId, "ke2", "Kế toán 2", TestHost.Password)).Id;
        await s.Setup.GrantRoleAsync(h.AdminId, a, h.CompanyId, RoleCatalog.Accountant);
        await s.Setup.GrantRoleAsync(h.AdminId, b, h.CompanyId, RoleCatalog.Accountant);
        var one = await s.Auth.ResolveContextAsync(a, h.CompanyId);
        var two = await s.Auth.ResolveContextAsync(b, h.CompanyId);

        Assert.True(await s.Setup.GetApprovalPolicyAsync(h.Admin));
        var v1 = await s.Engine.CreateDraftAsync(one, h.Sale(1000));
        await s.Engine.SubmitAsync(one, v1.Id);
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => s.Engine.ApproveAsync(one, v1.Id)));
        await s.Engine.ApproveAsync(two, v1.Id);                       // the colleague with the same role approves

        await s.Setup.SetApprovalPolicyAsync(h.Admin, false, "Công ty chỉ có một kế toán");
        Assert.False(await s.Setup.GetApprovalPolicyAsync(h.Admin));
        var v2 = await s.Engine.CreateDraftAsync(one, h.Sale(2000));
        await s.Engine.SubmitAsync(one, v2.Id);
        await s.Engine.ApproveAsync(one, v2.Id);                       // allowed now
    }

    [Fact]
    public async Task ApprovalPolicy_NeedsConfigManage_ReasonWhenSwitchedOff_AndIsAudited()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Setup.SetApprovalPolicyAsync(h.Approver, false, "x")));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Setup.GetApprovalPolicyAsync(h.Auditor with { Permissions = new HashSet<string>() })));
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => s.Setup.SetApprovalPolicyAsync(h.Admin, false, "  ")));
        await s.Setup.SetApprovalPolicyAsync(h.Admin, false, "Công ty chỉ có một kế toán");
        Assert.True(await s.Db.AuditLogs.AnyAsync(a => a.Action == "company.approval_policy"));
        await s.Setup.SetApprovalPolicyAsync(h.Admin, true, null);     // switching back on needs no reason
        Assert.True(await s.Setup.GetApprovalPolicyAsync(h.Approver));
    }
}
