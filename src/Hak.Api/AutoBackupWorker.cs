using Hak.Application.Backup;
using Hak.Domain.Common;

namespace Hak.Api;

/// <summary>Tình trạng sao lưu tự động (đọc ở Quản trị → Sao lưu).</summary>
public sealed class AutoBackupStatus
{
    public bool Enabled { get; set; }
    public string Reason { get; set; } = "";
    public string Time { get; set; } = "";
    public string? CopyPath { get; set; }
    public int KeepDays { get; set; }
    public DateTime? NextRunLocal { get; set; }
    public DateTime? LastSuccessUtc { get; set; }
    public string? LastFile { get; set; }
    public bool? LastCopyOk { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Sao lưu TỰ ĐỘNG hằng ngày (chỉ chế độ SQLite một máy; PostgreSQL dùng pg_dump). Cấu hình: Hak:AutoBackup:Enabled, Time (HH:mm giờ máy, mặc định 22:00), KeepDays (mặc định 30, luôn giữ ít nhất 7 bản mới nhất),
/// CopyPath (thư mục thứ hai NGOÀI máy này: ổ mạng, USB, thư mục đồng bộ đám mây). Mật khẩu sao lưu lấy từ biến môi trường HAK_BACKUP_PASSPHRASE (không ghi vào tệp cấu hình).
/// Mỗi lần: tạo bản mã hóa → giải mã kiểm tra toàn vẹn (cơ sở dữ liệu + chuỗi nhật ký kiểm toán) → chép sang CopyPath → xóa bản quá hạn. Lỗi được giữ trong tình trạng và nhật ký chạy, thử lại sau 30 phút.
/// </summary>
public sealed class AutoBackupWorker(IServiceScopeFactory scopes, IConfiguration config, BackupOptions options, AutoBackupStatus status, IClock clock, ILogger<AutoBackupWorker> log) : BackgroundService
{
    public const string PassphraseVariable = "HAK_BACKUP_PASSPHRASE";

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        Configure();
        if (!status.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
        {
            try
            {
                if (status.NextRunLocal is { } due && DateTime.Now >= due) { await RunOnceAsync(stop); }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Sao lưu tự động gặp lỗi."); }
        }
    }

    public void Configure()
    {
        status.Time = config["Hak:AutoBackup:Time"] ?? "22:00";
        status.CopyPath = string.IsNullOrWhiteSpace(config["Hak:AutoBackup:CopyPath"]) ? null : config["Hak:AutoBackup:CopyPath"];
        status.KeepDays = int.TryParse(config["Hak:AutoBackup:KeepDays"], out var k) && k >= 1 ? k : 30;
        if (!config.GetValue("Hak:AutoBackup:Enabled", false)) { status.Enabled = false; status.Reason = "Chưa bật (Hak:AutoBackup:Enabled=true)."; return; }
        if ((config["Hak:Provider"] ?? "Sqlite").Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)) { status.Enabled = false; status.Reason = "Chế độ PostgreSQL: dùng pg_dump trên máy chủ."; return; }
        if (!TimeOnly.TryParse(status.Time, out _)) { status.Enabled = false; status.Reason = $"Giờ sao lưu '{status.Time}' không hợp lệ (HH:mm)."; return; }
        var pass = Environment.GetEnvironmentVariable(PassphraseVariable);
        if (string.IsNullOrEmpty(pass) || pass.Length < options.MinPassphraseLength) { status.Enabled = false; status.Reason = $"Chưa đặt biến môi trường {PassphraseVariable} (từ {options.MinPassphraseLength} ký tự)."; return; }
        status.Enabled = true; status.Reason = "";
        status.NextRunLocal = NextOccurrence(DateTime.Now, TimeOnly.Parse(status.Time));
    }

    public static DateTime NextOccurrence(DateTime now, TimeOnly time)
    {
        var today = now.Date + time.ToTimeSpan();
        return today > now ? today : today.AddDays(1);
    }

    /// <summary>Một lần sao lưu: tạo, kiểm tra, chép, dọn. Thành công thì hẹn lần sau vào giờ đã cấu hình; lỗi thì thử lại sau 30 phút.</summary>
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        var pass = Environment.GetEnvironmentVariable(PassphraseVariable) ?? "";
        try
        {
            using var scope = scopes.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<BackupService>();
            var actor = await svc.ScheduledActorAsync();
            var info = await svc.CreateAsync(actor, pass, "Tự động theo lịch");
            var check = await svc.VerifyAsync(actor, info.FileName, pass);
            if (!check.IsValid) throw new DomainException(ErrorCodes.ValidationFailed, $"Bản sao lưu {info.FileName} không qua kiểm tra toàn vẹn.");
            status.LastFile = info.FileName; status.LastSuccessUtc = clock.UtcNow; status.LastError = null;
            if (status.CopyPath is { } copy)
            {
                try
                {
                    Directory.CreateDirectory(copy);
                    var dest = Path.Combine(copy, info.FileName);
                    File.Copy(Path.Combine(options.Directory, info.FileName), dest, overwrite: true);
                    status.LastCopyOk = new FileInfo(dest).Length == info.Size;
                    if (status.LastCopyOk != true) status.LastError = $"Bản chép sang {copy} không đủ kích thước.";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.LastCopyOk = false; status.LastError = $"Không chép được sang {copy}: {ex.Message}"; }
                Prune(copy);
            }
            Prune(options.Directory);
            status.NextRunLocal = status.LastError is null ? NextOccurrence(DateTime.Now, TimeOnly.Parse(status.Time)) : DateTime.Now.AddMinutes(30);
            if (status.LastError is not null) log.LogWarning("Sao lưu tự động: {Error}", status.LastError);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            status.LastError = ex.Message; status.NextRunLocal = DateTime.Now.AddMinutes(30);
            log.LogError(ex, "Sao lưu tự động thất bại; thử lại sau 30 phút.");
        }
    }

    /// <summary>Xóa bản quá KeepDays ngày nhưng luôn giữ ít nhất 7 bản mới nhất.</summary>
    private void Prune(string directory)
    {
        if (!Directory.Exists(directory)) return;
        var files = Directory.GetFiles(directory, "hak-backup-*.hakbk").OrderByDescending(f => f, StringComparer.Ordinal).ToList();
        var limit = DateTime.UtcNow.AddDays(-status.KeepDays);   // so với giờ ghi tệp của hệ thống tệp, không phải đồng hồ nghiệp vụ
        foreach (var f in files.Skip(7))
            if (File.GetLastWriteTimeUtc(f) < limit) { try { File.Delete(f); } catch (IOException) { /* bị khóa: lần sau */ } }
    }
}
