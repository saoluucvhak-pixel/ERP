using Hak.Application.Setup;
using Hak.Domain.Catalog;

namespace Hak.Api;

public sealed record PrimaryGroupRequest(Guid? GroupId);
public sealed record GroupUpdateRequest(string Name, Guid? ParentId, string? Note, bool IsActive, decimal? DefaultCreditLimit = null, int? DefaultPaymentTermsDays = null, decimal? DefaultVatRate = null, AssetGroupDefaults? Asset = null);
public sealed record EntityGroupsRequest(IReadOnlyList<Guid> GroupIds);
public sealed record MembersRequest(IReadOnlyList<Guid> EntityIds);

public static class GroupEndpoints
{
    public static void MapGroupEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/groups");
        api.MapGet("/{domain}", async (HttpContext http, GroupDomain domain, GroupService s) => Results.Ok(await s.TreeAsync(await http.SessionAsync(), domain)));
        api.MapGet("/{domain}/links", async (HttpContext http, GroupDomain domain, GroupService s) => Results.Ok(await s.LinksAsync(await http.SessionAsync(), domain)));
        api.MapGet("/{domain}/summary", async (HttpContext http, GroupDomain domain, DateOnly asOf, GroupService s) => Results.Ok(await s.SummaryAsync(await http.SessionAsync(), domain, asOf)));
        api.MapPost("", async (HttpContext http, GroupInput i, GroupService s) => Results.Ok(new { (await s.CreateAsync(await http.SessionAsync(), i)).Id }));
        api.MapPut("/by-id/{id:guid}", async (HttpContext http, Guid id, GroupUpdateRequest r, GroupService s) => { await s.UpdateAsync(await http.SessionAsync(), id, r.Name, r.ParentId, r.Note, r.IsActive, r.DefaultCreditLimit, r.DefaultPaymentTermsDays, r.DefaultVatRate, r.Asset); return Results.NoContent(); });
        api.MapDelete("/by-id/{id:guid}", async (HttpContext http, Guid id, GroupService s) => { await s.DeleteAsync(await http.SessionAsync(), id); return Results.NoContent(); });
        api.MapGet("/by-id/{id:guid}/members", async (HttpContext http, Guid id, bool? includeDescendants, GroupService s) => Results.Ok(await s.MembersAsync(await http.SessionAsync(), id, includeDescendants ?? true)));
        api.MapPost("/by-id/{id:guid}/members", async (HttpContext http, Guid id, MembersRequest r, GroupService s) => Results.Ok(new { added = await s.AddMembersAsync(await http.SessionAsync(), id, r.EntityIds) }));
        api.MapPost("/by-id/{id:guid}/members/remove", async (HttpContext http, Guid id, MembersRequest r, GroupService s) => { await s.RemoveMembersAsync(await http.SessionAsync(), id, r.EntityIds); return Results.NoContent(); });
        api.MapPut("/{domain}/entity/{entityId:guid}/primary", async (HttpContext http, GroupDomain domain, Guid entityId, PrimaryGroupRequest r, GroupService s) => { await s.SetPrimaryGroupAsync(await http.SessionAsync(), domain, entityId, r.GroupId); return Results.NoContent(); });
        api.MapPut("/{domain}/entity/{entityId:guid}", async (HttpContext http, GroupDomain domain, Guid entityId, EntityGroupsRequest r, GroupService s) => { await s.SetGroupsOfAsync(await http.SessionAsync(), domain, entityId, r.GroupIds); return Results.NoContent(); });
    }
}
