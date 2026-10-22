using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Hak.Application.Audit;
using Hak.Application.Exchange;
using Hak.Application.Security;
using Hak.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Hồi quy cho nhóm bảo mật SEC-04, SEC-06, SEC-07 (phần còn lại), SEC-09, SEC-10 (docs/audit/BUG_REGISTER.md).</summary>
public class SecurityHardeningTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    private const string MachineA = "PC-KETOAN|10.0.0.11";
    private const string MachineB = "PC-GIAMDOC|10.0.0.12";

    // SEC-07: sai mật khẩu liên tiếp từ một máy chỉ khóa máy đó; chủ tài khoản ở máy khác vẫn đăng nhập được.
    [Fact]
    public async Task WrongPasswordsFromOneSource_LockOnlyThatSource_NotTheOwnerElsewhere()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        for (var i = 0; i < 5; i++)
            Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Auth.LoginAsync("lap", "sai-mat-khau", "x", MachineA)));
        // Máy A đã bị khóa với tài khoản này, kể cả khi đoán đúng.
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Auth.LoginAsync("lap", TestHost.Password, "x", MachineA)));
        // Chủ tài khoản ở máy B không bị ảnh hưởng.
        await s.Auth.LoginAsync("lap", TestHost.Password, "x", MachineB);
        Assert.True(await s.Db.AuditLogs.AnyAsync(a => a.Action == "auth.lockout" && a.DataJson!.Contains("PC-KETOAN")));
        // Hết thời gian khóa thì máy A đăng nhập lại được.
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(16);
        await s.Auth.LoginAsync("lap", TestHost.Password, "x", MachineA);
    }

    // SEC-07: lưới an toàn — dò phân tán từ nhiều máy (mỗi máy dưới ngưỡng) vẫn khóa cả tài khoản; quản trị đặt lại mật khẩu gỡ mọi khóa.
    [Fact]
    public async Task DistributedGuessing_LocksTheWholeAccount_AndAdminResetClearsEveryLock()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        for (var m = 0; m < 4; m++)
            for (var i = 0; i < 5; i++)
                await CodeOf(() => s.Auth.LoginAsync("lap", "sai-mat-khau", "x", $"PC-{m}|10.0.1.{m}"));
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Auth.LoginAsync("lap", TestHost.Password, "x", MachineB)));
        Assert.True(await s.Db.AuditLogs.AnyAsync(a => a.Action == "auth.lockout_all_sources"));

        await s.Setup.ResetPasswordAsync(h.AdminId, h.PreparerId, "Mat-khau-moi-2026");
        Assert.Empty(await s.Db.LoginThrottles.Where(t => t.UserId == h.PreparerId).ToListAsync());
        await s.Auth.LoginAsync("lap", "Mat-khau-moi-2026", "x", "PC-0|10.0.1.0");
    }

    // SEC-10: đổi mật khẩu bằng phiên đang mở không được thử mật khẩu hiện tại vô hạn; đến ngưỡng thì khóa và đóng mọi phiên.
    [Fact]
    public async Task ChangeOwnPassword_LimitsWrongCurrentPasswords_ThenEndsEverySession()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var login = await s.Auth.LoginAsync("lap", TestHost.Password, "x", MachineA);
        for (var i = 0; i < 4; i++)
            Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Setup.ChangeOwnPasswordAsync(h.PreparerId, login.SessionId, "doan-sai-" + i, "Mat-khau-moi-2026", MachineA)));
        await s.Auth.AuthenticateAsync(login.Token);   // dưới ngưỡng: phiên vẫn dùng được
        var last = await Assert.ThrowsAsync<DomainException>(() => s.Setup.ChangeOwnPasswordAsync(h.PreparerId, login.SessionId, "doan-sai-4", "Mat-khau-moi-2026", MachineA));
        Assert.Contains("đã đóng", last.Message);
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Auth.AuthenticateAsync(login.Token)));
        // Đang khóa: đúng mật khẩu hiện tại cũng không đổi được từ máy đó; mật khẩu cũ vẫn còn nguyên.
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Setup.ChangeOwnPasswordAsync(h.PreparerId, login.SessionId, TestHost.Password, "Mat-khau-moi-2026", MachineA)));
        await s.Auth.LoginAsync("lap", TestHost.Password, "x", MachineB);
        Assert.True(await s.Db.AuditLogs.AnyAsync(a => a.Action == "user.password_change" && a.Result == "DENIED"));
    }

    // SEC-04: dữ liệu người gửi ghi vào nhật ký / phiên được giới hạn độ dài; mật khẩu dài bất thường bị từ chối mà không băm.
    [Fact]
    public async Task OversizedLoginInput_IsBoundedInTheAuditLogAndSessions()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var huge = new string('x', 100_000);
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Auth.LoginAsync(huge, "sai-mat-khau", huge, huge)));
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(() => s.Auth.LoginAsync("lap", new string('p', 1_000_000), huge)));
        var denied = await s.Db.AuditLogs.AsNoTracking().Where(a => a.Action == "auth.login" && a.Result == "DENIED").ToListAsync();
        Assert.Equal(2, denied.Count);
        Assert.All(denied, a => Assert.True((a.EntityId?.Length ?? 0) <= 100 && (a.DataJson?.Length ?? 0) < 1_000, $"{a.EntityId?.Length} / {a.DataJson?.Length}"));
        var ok = await s.Auth.LoginAsync("lap", TestHost.Password, huge, huge);
        Assert.Equal(100, (await s.Db.LoginSessions.AsNoTracking().SingleAsync(x => x.Id == ok.SessionId)).Device!.Length);
        Assert.True(await s.Db.LoginThrottles.AllAsync(t => t.Source.Length <= LoginThrottling.MaxSourceLength));
    }

    // SEC-06: nguồn "HAK" / "HAK.*" là của các phân hệ nội bộ; không cấp khóa trao đổi cho nguồn này.
    [Theory]
    [InlineData("HAK.STOCK")]
    [InlineData("hak.import")]
    [InlineData("HAK")]
    public async Task ExchangeKeys_RefuseTheReservedInternalSource(string source)
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var keys = new ExchangeKeyService(s.Db, s.Audit);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => keys.IssueAsync(h.Admin, source, "2026-01")));
        await keys.IssueAsync(h.Admin, "HAKUNA", "2026-01");   // chỉ chặn đúng tiền tố HAK.
    }

    // SEC-10: kiểm chữ ký không bao giờ ném lỗi hệ thống vì chữ ký người gửi viết sai, dù schema có chặn trước hay không.
    [Theory]
    [InlineData("không-phải-base64!!")]
    [InlineData("QUJD")]
    [InlineData("")]
    [InlineData(null)]
    public void PackageSignature_MalformedValues_AreInvalidNotErrors(string? signature)
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var package = new JsonObject { ["package_id"] = "P1", ["payload"] = new JsonObject { ["x"] = "1" } };
        PackageCrypto.Seal(package, "k1", secret);
        Assert.True(PackageCrypto.Verify(package, secret));
        Assert.False(PackageCrypto.Verify(package, RandomNumberGenerator.GetBytes(32)));
        package["auth"]!["signature"] = signature;
        Assert.False(PackageCrypto.Verify(package, secret));
        package["auth"]!["signature"] = 12345;
        Assert.False(PackageCrypto.Verify(package, secret));
        package.Remove("auth");
        Assert.False(PackageCrypto.Verify(package, secret));
    }

    /// <summary>
    /// SEC-09: chuỗi nhật ký ký bằng khóa chủ (ngoài CSDL). Kẻ sửa được tệp CSDL mà không có khóa thì không viết lại được chuỗi:
    /// tính lại bằng SHA-256 thường (cách cũ) hay bằng khóa khác đều bị phát hiện. Bản ghi cũ trước khi có khóa vẫn kiểm được.
    /// </summary>
    [Fact]
    public async Task KeyedAuditChain_CannotBeRewrittenWithoutTheMasterKey()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var keyed = new AuditService(s.Db, h.Clock, new MasterKey(RandomNumberGenerator.GetBytes(32)));
        // Khởi động với khóa chủ: neo các chuỗi cũ (không khóa, do TestHost ghi) tại điểm cuối hiện có; chạy lại không neo thêm.
        Assert.Equal(1, await keyed.VerifyChainAsync(h.CompanyId));   // có bản ghi mà chưa có mốc
        Assert.True(await keyed.EnsureAnchorsAsync() >= 2);           // hệ thống + công ty
        Assert.Equal(0, await keyed.EnsureAnchorsAsync());
        Assert.Null(await keyed.VerifyChainAsync(h.CompanyId));
        var firstKeyed = (await s.Db.AuditLogs.Where(a => a.CompanyId == h.CompanyId).MaxAsync(a => a.Seq)) + 1;
        for (var i = 0; i < 3; i++) await keyed.AppendAsync(h.CompanyId, h.Preparer, "test.keyed", "x", i.ToString());
        await s.Db.SaveChangesAsync();
        Assert.Null(await keyed.VerifyChainAsync(h.CompanyId));   // cũ rồi mới: liền mạch
        Assert.StartsWith(AuditService.KeyedPrefix, (await s.Db.AuditLogs.SingleAsync(a => a.CompanyId == h.CompanyId && a.Seq == firstKeyed)).Hash);
        // Không có khóa chủ: không kiểm được — báo rõ, không kết luận là bị sửa.
        Assert.Equal(ErrorCodes.IntegrityViolation, await CodeOf(() => new AuditService(s.Db, h.Clock).VerifyChainAsync(h.CompanyId)));

        // Kẻ tấn công tắt trigger, sửa một bản ghi rồi viết lại toàn bộ phần đuôi chuỗi bằng một hàm băm hắn tính được.
        await s.Db.Database.ExecuteSqlRawAsync(h.DisableAuditUpdateTriggerSql);
        async Task RewriteTail(byte[]? attackerKey)
        {
            s.Db.ChangeTracker.Clear();
            var rows = await s.Db.AuditLogs.Where(a => a.CompanyId == h.CompanyId && a.Seq >= firstKeyed).OrderBy(a => a.Seq).ToListAsync();
            var prev = (await s.Db.AuditLogs.AsNoTracking().SingleAsync(a => a.CompanyId == h.CompanyId && a.Seq == firstKeyed - 1)).Hash;
            rows[0].Result = "DENIED";
            foreach (var r in rows) { r.PrevHash = prev; r.Hash = AuditService.ComputeHash(r, attackerKey); prev = r.Hash; }
            foreach (var r in rows)
                await s.Db.Database.ExecuteSqlAsync($"UPDATE audit_logs SET result = {r.Result}, prev_hash = {r.PrevHash}, hash = {r.Hash} WHERE id = {r.Id}");
            s.Db.ChangeTracker.Clear();
        }
        await RewriteTail(null);                                  // hạ về SHA-256 thường: cách cũ không phát hiện được
        Assert.Equal(firstKeyed, await keyed.VerifyChainAsync(h.CompanyId));
        await RewriteTail(RandomNumberGenerator.GetBytes(32));   // đoán khóa khác
        Assert.Equal(firstKeyed, await keyed.VerifyChainAsync(h.CompanyId));

        // Xóa luôn mốc rồi hạ cả chuỗi về SHA-256 thường: chuỗi có bản ghi mà không có mốc vẫn bị phát hiện.
        await s.Db.Database.ExecuteSqlRawAsync(h.IsPostgres
            ? "DROP TRIGGER IF EXISTS trg_audit_chain_anchors_append_only ON audit_chain_anchors"
            : "DROP TRIGGER IF EXISTS trg_audit_chain_anchors_no_delete");
        await s.Db.AuditChainAnchors.Where(a => a.CompanyId == h.CompanyId).ExecuteDeleteAsync();
        await RewriteTail(null);
        Assert.Equal(1, await keyed.VerifyChainAsync(h.CompanyId));
    }

    // SEC-09: mốc chỉ thêm, không sửa / xóa được qua SQL thường (trigger hai bên).
    [Fact]
    public async Task ChainAnchors_AreAppendOnly()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        await new AuditService(s.Db, h.Clock, new MasterKey(RandomNumberGenerator.GetBytes(32))).EnsureAnchorsAsync();
        Assert.Contains("LEDGER_IMMUTABLE", (await Assert.ThrowsAnyAsync<Exception>(() => s.Db.Database.ExecuteSqlRawAsync("UPDATE audit_chain_anchors SET legacy_seq = 0"))).ToString());
        Assert.Contains("LEDGER_IMMUTABLE", (await Assert.ThrowsAnyAsync<Exception>(() => s.Db.Database.ExecuteSqlRawAsync("DELETE FROM audit_chain_anchors"))).ToString());
    }
}
