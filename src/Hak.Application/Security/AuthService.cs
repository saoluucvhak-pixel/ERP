using System.Security.Cryptography;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Security;

public sealed record LoginResult(string Token, Guid SessionId, Guid UserId, DateTime ExpiresAt);

public sealed class AuthService(HakDbContext db, AuditService audit, IClock clock, HakRuntimeOptions options)
{
    /// <summary>
    /// One answer for every refused login (unknown user, wrong password, inactive, locked), so the response never tells a
    /// guesser that a user name exists or that it is locked. The lock and its reason are in the audit log.
    /// </summary>
    public const string LoginDeniedMessage =
        "Tên đăng nhập hoặc mật khẩu không đúng. Đăng nhập sai nhiều lần liên tiếp sẽ tạm khóa tài khoản; cần mở ngay thì nhờ quản trị đặt lại mật khẩu.";

    /// <summary>Hash of a random password per iteration count: verified when the user does not exist, so that path costs the same PBKDF2 time.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> DummyHashes = new();

    /// <summary>Longest password that is hashed at all: PBKDF2 over a megabyte-long "password" is a cheap way to burn server CPU.</summary>
    public const int MaxPasswordLength = 1024;

    /// <param name="source">Where the attempt comes from, as the server sees it (API host + client address); see <see cref="LoginThrottling"/>.</param>
    public async Task<LoginResult> LoginAsync(string userName, string password, string? device, string? source = null)
    {
        // SEC-04: what the caller sent is stored in the audit log and the session, so it is bounded before it is used.
        var norm = LoginThrottling.Clip(userName.Trim().ToLowerInvariant());
        device = string.IsNullOrEmpty(device) ? null : LoginThrottling.Clip(device);
        source = LoginThrottling.NormalizeSource(source);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == norm);
        // Always run PBKDF2 (unknown, inactive and locked users too): response time must not reveal which case applied.
        var passwordOk = password.Length <= MaxPasswordLength && PasswordHasher.Verify(password, user?.PasswordHash
            ?? DummyHashes.GetOrAdd(options.PasswordIterations, n => PasswordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)), n)));
        if (user is not null && await LoginThrottling.LockedUntilAsync(db, user, source, clock.UtcNow) is { } until)
        {
            await db.InTransactionAsync(async () =>
            {
                await audit.AppendAsync(Guid.Empty, null, "auth.login", "user", user.Id.ToString(), new { device, source, lockedUntil = until }, result: "LOCKED");
                await db.SaveChangesAsync();
            });
            // Same answer as a wrong password while locked, so the lock does not reveal whether the password was right.
            throw new DomainException(ErrorCodes.Unauthenticated, LoginDeniedMessage);
        }
        if (user is null || !user.IsActive || !passwordOk)
        {
            await db.InTransactionAsync(async () =>
            {
                if (user is not null) await LoginThrottling.RegisterFailureAsync(db, audit, options, clock.UtcNow, user.Id, source);
                await audit.AppendAsync(Guid.Empty, null, "auth.login", "user", norm, new { device, source }, result: "DENIED");
                await db.SaveChangesAsync();
            });
            throw new DomainException(ErrorCodes.Unauthenticated, LoginDeniedMessage);
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var session = new LoginSession
        {
            Id = Ids.New(), UserId = user.Id, TokenHash = PasswordHasher.Sha256Hex(token), Device = device,
            CreatedAt = clock.UtcNow, ExpiresAt = clock.UtcNow.Add(options.SessionLifetime),
        };
        await db.InTransactionAsync(async () =>
        {
            await LoginThrottling.ResetAsync(db, user, source);
            db.LoginSessions.Add(session);
            await audit.AppendAsync(Guid.Empty, null, "auth.login", "user", user.Id.ToString(), new { device, session = session.Id }, userIdOverride: user.Id);
            await db.SaveChangesAsync();
        });
        return new LoginResult(token, session.Id, user.Id, session.ExpiresAt);
    }

    /// <summary>Validates the bearer token and returns the user (no company scope yet).</summary>
    public async Task<(User User, LoginSession Session)> AuthenticateAsync(string token)
    {
        var hash = PasswordHasher.Sha256Hex(token);
        var session = await db.LoginSessions.AsNoTracking().FirstOrDefaultAsync(s => s.TokenHash == hash);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= clock.UtcNow)
            throw new DomainException(ErrorCodes.Unauthenticated, "Phiên làm việc không hợp lệ hoặc đã hết hạn.");
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == session.UserId);
        if (!user.IsActive) throw new DomainException(ErrorCodes.Unauthenticated, "Tài khoản đã bị khóa.");
        return (user, session);
    }

    /// <summary>Builds the company-scoped context. Users without a role in the company are refused.</summary>
    public async Task<SessionContext> ResolveContextAsync(Guid userId, Guid companyId, Guid? sessionId = null, string? device = null)
    {
        var perms = await (from ucr in db.UserCompanyRoles
                           join rp in db.RolePermissions on ucr.RoleId equals rp.RoleId
                           where ucr.UserId == userId && ucr.CompanyId == companyId
                           select rp.Permission).Distinct().ToListAsync();
        var hasAnyRole = await db.UserCompanyRoles.AnyAsync(x => x.UserId == userId && x.CompanyId == companyId);
        if (!hasAnyRole) throw new DomainException(ErrorCodes.Forbidden, "Người dùng chưa được cấp quyền tại công ty này.");
        return new SessionContext(userId, companyId, perms.ToHashSet(), sessionId, device);
    }

    public async Task LogoutAsync(Guid sessionId)
    {
        var s = await db.LoginSessions.FirstOrDefaultAsync(x => x.Id == sessionId);
        if (s is null || s.RevokedAt is not null) return;
        s.RevokedAt = clock.UtcNow;
        await db.SaveChangesAsync();
    }
}
