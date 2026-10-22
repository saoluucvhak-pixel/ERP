using Hak.Application.Backup;

namespace Hak.Api;

public sealed record BackupRequest(string Passphrase, string? Note);
public sealed record BackupCheckRequest(string FileName, string Passphrase);

/// <summary>System-administrator endpoints (no company context): backups contain every company's data.</summary>
public static class BackupEndpoints
{
    public static void MapBackupEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/admin/backups");
        api.MapGet("", async (HttpContext http, BackupService s) => Results.Ok(await s.ListAsync((await http.AuthAsync()).User.Id)));
        api.MapPost("", async (HttpContext http, BackupRequest r, BackupService s) => Results.Ok(await s.CreateAsync((await http.AuthAsync()).User.Id, r.Passphrase, r.Note)));
        api.MapPost("/verify", async (HttpContext http, BackupCheckRequest r, BackupService s) =>
            Results.Ok(await s.VerifyAsync((await http.AuthAsync()).User.Id, r.FileName, r.Passphrase)));
        api.MapPost("/restore", async (HttpContext http, BackupCheckRequest r, BackupService s) =>
            Results.Ok(await s.VerifyAsync((await http.AuthAsync()).User.Id, r.FileName, r.Passphrase, restore: true)));
    }
}
