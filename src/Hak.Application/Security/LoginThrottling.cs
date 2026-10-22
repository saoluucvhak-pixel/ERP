using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Data;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Security;

/// <summary>
/// Khóa sau nhiều lần nhập mật khẩu sai, theo NGUỒN (SEC-07): nguồn là máy chạy API + địa chỉ gửi yêu cầu, do server xác định (client không tự
/// khai được). Sai <see cref="HakRuntimeOptions.MaxFailedLogins"/> lần từ một nguồn chỉ khóa nguồn đó; chủ tài khoản ở máy khác vẫn đăng nhập.
/// Khóa cả tài khoản chỉ khi tổng số lần sai từ mọi nguồn đạt <see cref="HakRuntimeOptions.MaxFailedLoginsAllSources"/> (dò phân tán).
/// Dùng chung cho đăng nhập và đổi mật khẩu (SEC-10). Các hàm ghi chạy trong giao dịch của người gọi.
/// </summary>
public static class LoginThrottling
{
    /// <summary>Nguồn khi người gọi không biết (test, công cụ dòng lệnh).</summary>
    public const string DefaultSource = "local";
    public const int MaxSourceLength = 200;
    /// <summary>Độ dài tối đa của tên đăng nhập / thiết bị ghi vào nhật ký (SEC-04: nhật ký không phình theo dữ liệu người gửi).</summary>
    public const int MaxLoggedText = 100;

    public static string Clip(string? text, int max = MaxLoggedText) => text is null ? "" : text.Length <= max ? text : text[..max];

    public static string NormalizeSource(string? source) => Clip(string.IsNullOrWhiteSpace(source) ? DefaultSource : source.Trim(), MaxSourceLength);

    /// <summary>Đến khi nào tài khoản còn khóa với nguồn này (khóa theo nguồn hoặc khóa chung); null nếu không khóa.</summary>
    public static async Task<DateTime?> LockedUntilAsync(HakDbContext db, User user, string source, DateTime now)
    {
        DateTime? until = user.LockedUntil > now ? user.LockedUntil : null;
        var row = await db.LoginThrottles.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == user.Id && t.Source == source);
        if (row?.LockedUntil > now && (until is null || row.LockedUntil > until)) until = row.LockedUntil;
        return until;
    }

    /// <summary>Ghi một lần sai; trả true nếu lần này làm khóa (nguồn hoặc cả tài khoản).</summary>
    public static async Task<bool> RegisterFailureAsync(HakDbContext db, AuditService audit, HakRuntimeOptions options, DateTime now, Guid userId, string source)
    {
        var locked = false;
        var row = db.LoginThrottles.Local.FirstOrDefault(t => t.UserId == userId && t.Source == source)
                  ?? await db.LoginThrottles.FirstOrDefaultAsync(t => t.UserId == userId && t.Source == source);
        if (row is null) db.LoginThrottles.Add(row = new LoginThrottle { UserId = userId, Source = source });
        if (row.LockedUntil <= now) row.LockedUntil = null;
        row.FailedCount++;
        row.UpdatedAt = now;
        if (row.FailedCount >= options.MaxFailedLogins)
        {
            row.LockedUntil = now.Add(options.LockoutDuration);
            row.FailedCount = 0;
            locked = true;
            await audit.AppendAsync(Guid.Empty, null, "auth.lockout", "user", userId.ToString(), new { until = row.LockedUntil, source }, result: "LOCKED");
        }
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.FailedLoginCount++;
        if (user.FailedLoginCount >= options.MaxFailedLoginsAllSources)
        {
            user.LockedUntil = now.Add(options.LockoutDuration);
            user.FailedLoginCount = 0;
            locked = true;
            await audit.AppendAsync(Guid.Empty, null, "auth.lockout_all_sources", "user", userId.ToString(), new { until = user.LockedUntil }, result: "LOCKED");
        }
        user.RowVersion++;
        return locked;
    }

    /// <summary>Đăng nhập / xác nhận mật khẩu đúng: xóa bộ đếm của nguồn này và bộ đếm chung (khóa đang hiệu lực ở nguồn khác giữ nguyên).</summary>
    public static async Task ResetAsync(HakDbContext db, User trackedUser, string source)
    {
        Forget(db, t => t.UserId == trackedUser.Id && t.Source == source);
        await db.LoginThrottles.Where(t => t.UserId == trackedUser.Id && t.Source == source).ExecuteDeleteAsync();
        if (trackedUser.FailedLoginCount != 0 || trackedUser.LockedUntil is not null)
        {
            trackedUser.FailedLoginCount = 0;
            trackedUser.LockedUntil = null;
            trackedUser.RowVersion++;
        }
    }

    /// <summary>Quản trị đặt lại mật khẩu / mở lại tài khoản: gỡ mọi khóa của tài khoản ở mọi nguồn.</summary>
    public static Task ClearAllAsync(HakDbContext db, Guid userId)
    {
        Forget(db, t => t.UserId == userId);
        return db.LoginThrottles.Where(t => t.UserId == userId).ExecuteDeleteAsync();
    }

    /// <summary>ExecuteDelete bỏ qua bộ theo dõi của EF: gỡ bản đang theo dõi để lần ghi sai kế tiếp trong cùng DbContext không trùng khóa.</summary>
    private static void Forget(HakDbContext db, Func<LoginThrottle, bool> match)
    {
        foreach (var e in db.ChangeTracker.Entries<LoginThrottle>().Where(e => match(e.Entity)).ToList()) e.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
    }
}
