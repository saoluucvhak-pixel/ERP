using Hak.Application.Common;

namespace Hak.Api;

public sealed record NumberingRequest(string? Prefix, int? Width);

public static class NumberingEndpoints
{
    public static void MapNumberingEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/numbering");
        api.MapGet("", async (HttpContext http, NumberingService s) => Results.Ok(await s.ListAsync(await http.SessionAsync(), DateOnly.FromDateTime(DateTime.UtcNow))));
        api.MapPut("/{key}", async (HttpContext http, string key, NumberingRequest r, NumberingService s) => { await s.SetAsync(await http.SessionAsync(), key, r.Prefix, r.Width); return Results.NoContent(); });
    }
}
