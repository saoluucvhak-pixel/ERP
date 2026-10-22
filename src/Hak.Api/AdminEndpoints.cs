
namespace Hak.Api;

public sealed record RevokeRequest(Guid CompanyId, string RoleCode, string Reason);
public sealed record ActiveRequest(bool Active, string Reason);
public sealed record ResetPasswordRequest(string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Audit Viewer, user / role administration (system administrators), own password and profile, first-run status.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        // Audit Viewer (company scope, audit.view).
        app.MapGet("/api/audit", async (HttpContext http, Hak.Application.Audit.AuditService a, DateOnly? from, DateOnly? to, string? action, string? entityType,
                string? entityId, string? result, int? skip, int? take) =>
            Results.Ok(await a.QueryAsync(await http.SessionAsync(), new Hak.Application.Audit.AuditFilter(from, to, action, entityType, entityId, null, result, skip ?? 0, take ?? 100))));

        // User administration (system administrators) and own password change.
        var admin = app.MapGroup("/api/admin");
        admin.MapGet("/users", async (HttpContext http, Hak.Application.Setup.SetupService s) => Results.Ok(await s.ListUsersAsync((await http.AuthAsync()).User.Id)));
        admin.MapGet("/companies", async (HttpContext http, Hak.Application.Setup.SetupService s) =>
            Results.Ok((await s.ListCompaniesAsync((await http.AuthAsync()).User.Id)).Select(c => new { id = c.Id, code = c.Code, name = c.Name })));
        admin.MapGet("/regime-versions", async (HttpContext http, Hak.Application.Setup.SetupService s) =>
            Results.Ok(await s.ListRegimeVersionsAsync((await http.AuthAsync()).User.Id)));
        // First run: the login screen offers to create the first administrator while no user exists.
        app.MapGet("/api/setup/status", async (Hak.Application.Setup.SetupService s, Hak.Application.Common.HakRuntimeOptions o) =>
            Results.Ok(new { needsBootstrap = await s.NeedsBootstrapAsync(), environment = o.Environment }));

        admin.MapGet("/roles",async (HttpContext http, Hak.Application.Setup.SetupService s) => Results.Ok(await s.ListRolesAsync((await http.AuthAsync()).User.Id)));
        admin.MapGet("/permission-catalog", async (HttpContext http, Hak.Application.Setup.SetupService s) => Results.Ok(await s.PermissionCatalogAsync((await http.AuthAsync()).User.Id)));
        admin.MapPost("/roles", async (HttpContext http, RoleRequest r, Hak.Application.Setup.SetupService s) =>
        { await s.CreateRoleAsync((await http.AuthAsync()).User.Id, r.Code, r.Name, r.Permissions); return Results.NoContent(); });
        admin.MapPut("/roles/{code}", async (HttpContext http, string code, RoleRequest r, Hak.Application.Setup.SetupService s) =>
        { await s.UpdateRoleAsync((await http.AuthAsync()).User.Id, code, r.Name, r.Permissions); return Results.NoContent(); });
        admin.MapPost("/users/{id:guid}/revoke", async (HttpContext http, Guid id, RevokeRequest r, Hak.Application.Setup.SetupService s) =>
        { await s.RevokeRoleAsync((await http.AuthAsync()).User.Id, id, r.CompanyId, r.RoleCode, r.Reason); return Results.NoContent(); });
        admin.MapPost("/users/{id:guid}/active", async (HttpContext http, Guid id, ActiveRequest r, Hak.Application.Setup.SetupService s) =>
        { await s.SetUserActiveAsync((await http.AuthAsync()).User.Id, id, r.Active, r.Reason); return Results.NoContent(); });
        admin.MapPost("/users/{id:guid}/reset-password", async (HttpContext http, Guid id, ResetPasswordRequest r, Hak.Application.Setup.SetupService s) =>
        { await s.ResetPasswordAsync((await http.AuthAsync()).User.Id, id, r.NewPassword); return Results.NoContent(); });
        app.MapPost("/api/me/password", async (HttpContext http, ChangePasswordRequest r, Hak.Application.Setup.SetupService s) =>
        {
            var (user, session) = await http.AuthAsync();
            await s.ChangeOwnPasswordAsync(user.Id, session.Id, r.CurrentPassword, r.NewPassword, http.ClientSource());
            return Results.NoContent();
        });
        app.MapGet("/api/me", async (HttpContext http) =>
        {
            var (user, _) = await http.AuthAsync();
            return Results.Ok(new { user.Id, user.UserName, user.FullName, user.IsSystemAdmin });
        });
    }
}
