using Hak.Domain.Common;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Login lockout, user administration, own password change, default-role permission sync.</summary>
public class UserAdminTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    [Fact]
    public async Task RepeatedWrongPasswords_LockTheAccount_UntilTheLockExpires()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        for (var i = 0; i < 5; i++)
            Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Auth.LoginAsync("lap", "sai-mat-khau", "PC1")));
        // Locked: even the right password is refused, without saying whether it was right.
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Auth.LoginAsync("lap", TestHost.Password, "PC1")));
        Assert.True(await s.Db.AuditLogs.AnyAsync(a => a.Action == "auth.lockout"));

        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(16);
        await s.Auth.LoginAsync("lap", TestHost.Password, "PC1");
        Assert.Equal(0, (await s.Db.Users.AsNoTracking().SingleAsync(u => u.Id == h.PreparerId)).FailedLoginCount);
    }

    [Fact]
    public async Task Admin_RevokesRoles_LocksAccounts_ResetsPasswords_WithSafeguards()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var users = await s.Setup.ListUsersAsync(h.AdminId);
        Assert.Contains(users.Single(u => u.UserName == "lap").Grants, g => g.RoleCode == "GL_ACCOUNTANT");
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Setup.ListUsersAsync(h.ApproverId)));
        Assert.Contains(await s.Setup.ListRolesAsync(h.AdminId), r => r.Code == "CHIEF_ACCOUNTANT" && r.Permissions.Contains(Permissions.LoanManage));

        await s.Setup.RevokeRoleAsync(h.AdminId, h.AuditorId, h.CompanyId, "INTERNAL_AUDITOR", "Chuyển bộ phận");
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Auth.ResolveContextAsync(h.AuditorId, h.CompanyId)));

        var login = await s.Auth.LoginAsync("lap", TestHost.Password, "PC1");
        await s.Setup.SetUserActiveAsync(h.AdminId, h.PreparerId, false, "Nghỉ việc");
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Auth.AuthenticateAsync(login.Token)));     // session ended at once
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Auth.LoginAsync("lap", TestHost.Password, "PC1")));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Setup.SetUserActiveAsync(h.AdminId, h.AdminId, false, "x")));
        await s.Setup.SetUserActiveAsync(h.AdminId, h.PreparerId, true, "Quay lại làm việc");

        await s.Setup.ResetPasswordAsync(h.AdminId, h.PreparerId, "Mat-khau-moi-2026");
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Auth.LoginAsync("lap", TestHost.Password, "PC1")));
        var again = await s.Auth.LoginAsync("lap", "Mat-khau-moi-2026", "PC1");

        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Setup.ChangeOwnPasswordAsync(h.PreparerId, again.SessionId, "sai", "Mat-khau-rieng-2026")));
        var other = await s.Auth.LoginAsync("lap", "Mat-khau-moi-2026", "PC2");
        await s.Setup.ChangeOwnPasswordAsync(h.PreparerId, again.SessionId, "Mat-khau-moi-2026", "Mat-khau-rieng-2026");
        await s.Auth.AuthenticateAsync(again.Token);                                                                   // current session kept
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Auth.AuthenticateAsync(other.Token)));            // others ended
        Assert.DoesNotContain("Mat-khau-rieng-2026", string.Join("|", await s.Db.AuditLogs.Select(a => a.DataJson ?? "").ToListAsync()));
    }

    [Fact]
    public async Task FirstRun_BootstrapOnlyWhileNoUserExists_RegimeVersionsNeedLegalSources()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        Assert.False(await s.Setup.NeedsBootstrapAsync());
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Setup.BootstrapAdminAsync("admin2", "Quản trị 2", TestHost.Password)));

        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Setup.CreateRegimeVersionAsync(h.AdminId, "R2", "Chế độ 2", "V1", new(2026, 1, 1), null, [])));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Setup.CreateRegimeVersionAsync(h.AdminId, "R2", "Chế độ 2", "V1", new(2026, 1, 1), new(2025, 1, 1),
            [new Hak.Application.Setup.LegalSourceInput("Nhóm", "Số 1", null, null, null)])));
        Assert.Equal(ErrorCodes.DuplicateCode, await CodeOf(() => s.Setup.CreateRegimeVersionAsync(h.AdminId, "test-regime", "x", "TEST-1", new(2026, 1, 1), null,
            [new Hak.Application.Setup.LegalSourceInput("Nhóm", "Số 1", null, null, null)])));
        var list = await s.Setup.ListRegimeVersionsAsync(h.AdminId);
        var v = Assert.Single(list);
        Assert.Equal(("TEST-REGIME", "Unverified", "TEST-FIXTURE"), (v.Regime, v.Status, Assert.Single(v.Sources).DocumentNo));
    }

    [Fact]
    public async Task FirstRun_EmptyDatabase_CreatesTheFirstAdministratorOnce()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        // Same database, users wiped as on a fresh install (sessions/roles reference users, so clear them first).
        await s.Db.LoginSessions.ExecuteDeleteAsync();
        await s.Db.UserCompanyRoles.ExecuteDeleteAsync();
        await s.Db.Users.ExecuteDeleteAsync();
        Assert.True(await s.Setup.NeedsBootstrapAsync());
        var admin = await s.Setup.BootstrapAdminAsync("quantri", "Quản trị", TestHost.Password);
        Assert.True(admin.IsSystemAdmin);
        Assert.False(await s.Setup.NeedsBootstrapAsync());
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Setup.BootstrapAdminAsync("khac", "Khác", TestHost.Password)));
    }

    [Fact]
    public async Task LocalRecovery_ResetsOnlyAnActiveSystemAdministrator_AndUnlocksIt()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        for (var i = 0; i < 5; i++) await CodeOf(() => s.Auth.LoginAsync("admin", "sai-mat-khau", "PC1"));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Setup.RecoverAdminPasswordAsync("lap", "Mat-khau-moi-2026")));
        Assert.Equal(ErrorCodes.NotFound, await CodeOf(() => s.Setup.RecoverAdminPasswordAsync("khongco", "Mat-khau-moi-2026")));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Setup.RecoverAdminPasswordAsync("admin", "ngan")));
        await s.Setup.RecoverAdminPasswordAsync("ADMIN", "Mat-khau-moi-2026");
        await s.Auth.LoginAsync("admin", "Mat-khau-moi-2026", "PC1");                       // unlocked at once
        Assert.True(await s.Db.AuditLogs.AnyAsync(a => a.Action == "user.password_recover_local"));
    }

    [Fact]
    public async Task Upgrade_GrantsNewDefaultPermissions_ToExistingRoles_AndAuditsIt()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        // Simulate a database created before loan.manage existed.
        var chief = await s.Db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Code == "CHIEF_ACCOUNTANT");
        s.Db.RolePermissions.Remove(chief.Permissions.Single(p => p.Permission == Permissions.LoanManage));
        await s.Db.SaveChangesAsync();
        Assert.DoesNotContain(Permissions.LoanManage, (await s.Auth.ResolveContextAsync(h.ApproverId, h.CompanyId)).Permissions);

        await s.Setup.EnsureReferenceDataAsync();
        Assert.Contains(Permissions.LoanManage, (await s.Auth.ResolveContextAsync(h.ApproverId, h.CompanyId)).Permissions);
        Assert.True(await s.Db.AuditLogs.AnyAsync(a => a.Action == "role.sync_defaults"));
    }

    [Fact]
    public async Task Admin_BuildsCustomRole_FromCatalogue_AndDefaultRolesStayReadOnly()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var catalog = await s.Setup.PermissionCatalogAsync(h.AdminId);
        Assert.Contains(catalog, m => m.Permissions.Any(p => p.Code == Permissions.CashDocument));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Setup.PermissionCatalogAsync(h.ApproverId)));

        await s.Setup.CreateRoleAsync(h.AdminId, "KT_QUY", "Kế toán quỹ", [Permissions.VoucherView, Permissions.CashDocument]);
        Assert.Contains(await s.Setup.ListRolesAsync(h.AdminId), r => r.Code == "KT_QUY" && r.Permissions.Count == 2);
        await s.Setup.GrantRoleAsync(h.AdminId, h.AuditorId, h.CompanyId, "KT_QUY");
        var granted = (await s.Auth.ResolveContextAsync(h.AuditorId, h.CompanyId)).Permissions;
        Assert.Contains(Permissions.CashDocument, granted);

        await s.Setup.UpdateRoleAsync(h.AdminId, "KT_QUY", "Kế toán quỹ", [Permissions.VoucherView]);
        Assert.DoesNotContain(Permissions.CashDocument, (await s.Auth.ResolveContextAsync(h.AuditorId, h.CompanyId)).Permissions);

        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Setup.UpdateRoleAsync(h.AdminId, "GL_ACCOUNTANT", "x", [Permissions.VoucherView])));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Setup.CreateRoleAsync(h.AdminId, "SAI", "x", ["khong.co"])));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Setup.CreateRoleAsync(h.AdminId, "TRONG", "x", [])));
        Assert.Equal(ErrorCodes.DuplicateCode, await CodeOf(() => s.Setup.CreateRoleAsync(h.AdminId, "ACCOUNTANT", "x", [Permissions.VoucherView])));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Setup.CreateRoleAsync(h.ApproverId, "KHAC", "x", [Permissions.VoucherView])));
    }

    [Fact]
    public async Task DefaultCatalogue_HasAccountantRole_WithoutApprovalOrPeriodRights()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var role = (await s.Setup.ListRolesAsync(h.AdminId)).Single(r => r.Code == "ACCOUNTANT");
        Assert.Contains(Permissions.VoucherCreate, role.Permissions);
        Assert.DoesNotContain(Permissions.VoucherApprove, role.Permissions);
        Assert.DoesNotContain(Permissions.PeriodLock, role.Permissions);
    }
}
