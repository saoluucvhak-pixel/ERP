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
        // Tình trạng sao lưu tự động + bản sao lưu gần nhất (kể cả làm tay); cảnh báo khi quá 48 giờ chưa có bản nào.
        api.MapGet("/auto-status", async (HttpContext http, BackupService s, AutoBackupStatus st) =>
        {
            var list = await s.ListAsync((await http.AuthAsync()).User.Id);
            var latest = list.OrderByDescending(b => b.CreatedAt).FirstOrDefault();
            var stale = latest is null || (DateTime.UtcNow - latest.CreatedAt).TotalHours > 48;
            return Results.Ok(new { st.Enabled, st.Reason, st.Time, st.CopyPath, st.KeepDays, st.NextRunLocal, st.LastSuccessUtc, st.LastFile, st.LastCopyOk, st.LastError, latestBackupUtc = latest?.CreatedAt, latestFile = latest?.FileName, stale, count = list.Count });
        });
        api.MapPost("/verify", async (HttpContext http, BackupCheckRequest r, BackupService s) =>
            Results.Ok(await s.VerifyAsync((await http.AuthAsync()).User.Id, r.FileName, r.Passphrase)));
        api.MapPost("/restore", async (HttpContext http, BackupCheckRequest r, BackupService s) =>
            Results.Ok(await s.VerifyAsync((await http.AuthAsync()).User.Id, r.FileName, r.Passphrase, restore: true)));
    }
}
