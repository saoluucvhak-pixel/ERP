using Microsoft.EntityFrameworkCore;

namespace Hak.Api;

public sealed record AccountUpdateRequest(string Name, bool IsActive);
public sealed record MoveAccountRequest(string? ParentCode);
public sealed record AccountTagsRequest(string? Tags);
public sealed record ChildAccountRequest(string Code, string Name, bool? RequiresPartner = null, Hak.Domain.Catalog.OpenItemNature? OpenItemNature = null,
    Hak.Domain.Catalog.CashKind? CashKind = null, Hak.Domain.Catalog.NegativeBalancePolicy? NegativeBalancePolicy = null, string? ReportTags = null);
public sealed record AccountAttributesRequest(bool RequiresPartner, Hak.Domain.Catalog.OpenItemNature OpenItemNature, Hak.Domain.Catalog.CashKind CashKind,
    Hak.Domain.Catalog.NegativeBalancePolicy NegativeBalancePolicy);

/// <summary>Chart of accounts maintenance (rename, move, tags, children, attributes) and the account-role map.</summary>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapPut("/api/accounts/{id:guid}", async (HttpContext http, Guid id, AccountUpdateRequest r, Hak.Application.Setup.SetupService s) =>
        { await s.UpdateAccountAsync(await http.SessionAsync(), id, r.Name, r.IsActive); return Results.NoContent(); });
        app.MapPost("/api/accounts/{id:guid}/parent", async (HttpContext http, Guid id, MoveAccountRequest r, Hak.Application.Setup.SetupService s) =>
        { await s.MoveAccountAsync(await http.SessionAsync(), id, r.ParentCode); return Results.NoContent(); });
        app.MapPost("/api/accounts/{id:guid}/tags", async (HttpContext http, Guid id, AccountTagsRequest r, Hak.Application.Setup.SetupService s) =>
        { await s.SetAccountTagsAsync(await http.SessionAsync(), id, r.Tags); return Results.NoContent(); });
        app.MapPost("/api/accounts/{id:guid}/children", async (HttpContext http, Guid id, ChildAccountRequest r, Hak.Application.Setup.SetupService s) =>
        {
            var a = await s.AddChildAccountAsync(await http.SessionAsync(), id, r.Code, r.Name,
                new Hak.Application.Setup.AccountAttributes(r.RequiresPartner, r.OpenItemNature, r.CashKind, r.NegativeBalancePolicy, r.ReportTags));
            return Results.Ok(new { a.Id, a.Code });
        });
        app.MapPost("/api/accounts/{id:guid}/attributes", async (HttpContext http, Guid id, AccountAttributesRequest r, Hak.Application.Setup.SetupService s) =>
        { await s.UpdateAccountAttributesAsync(await http.SessionAsync(), id, r.RequiresPartner, r.OpenItemNature, r.CashKind, r.NegativeBalancePolicy); return Results.NoContent(); });
        app.MapGet("/api/rules/account-roles", async (HttpContext http, Hak.Data.HakDbContext db) =>
        {
            var ctx = await http.SessionAsync(); ctx.Require(Hak.Domain.Security.Permissions.VoucherView);
            var mapped = await (from m in db.AccountRoleMappings join a in db.Accounts on m.AccountId equals a.Id
                                where m.CompanyId == ctx.CompanyId select new { m.RoleCode, a.Code, a.Name }).ToListAsync();
            var known = Hak.Application.Rules.AccountRoles.Known.Select(k => k.Code).ToHashSet();
            return Results.Ok(Hak.Application.Rules.AccountRoles.Known
                .Select(k => new { k.Code, k.Module, k.Description, k.Optional, account = mapped.Where(m => m.RoleCode == k.Code).Select(m => m.Code + " – " + m.Name).FirstOrDefault() })
                .Concat(mapped.Where(m => !known.Contains(m.RoleCode)).Select(m => new { Code = m.RoleCode, Module = "Khác", Description = "(vai trò do quy tắc riêng sử dụng)", Optional = true, account = (string?)(m.Code + " – " + m.Name) })));
        });
    }
}
