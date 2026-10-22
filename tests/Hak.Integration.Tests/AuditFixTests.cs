using Hak.Api;
using Hak.Domain.Common;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Hồi quy cho các lỗi tìm được trong đợt kiểm toán 21/10/2026 (docs/audit/BUG_REGISTER.md).</summary>
public class AuditFixTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    // SEC-01: phản hồi chứa khóa ký / băm nội dung chứa mật khẩu không được lưu vào bảng idempotency (đi theo bản sao lưu).
    [Theory]
    [InlineData("/api/me/password")]
    [InlineData("/api/admin/users/3f0efecc-2-0000-0000-000000000000/reset-password")]
    [InlineData("/api/admin/users")]
    [InlineData("/api/admin/backups")]
    [InlineData("/api/admin/backups/verify")]
    [InlineData("/api/exchange/keys")]
    [InlineData("/api/exchange/keys/3f0efecc/deactivate")]
    [InlineData("/api/exchange/keys/3f0efecc/rotate")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/setup/bootstrap")]
    public void Idempotency_NeverRecordsPathsThatCarrySecrets(string path) => Assert.True(IdempotencyMiddleware.ExcludedPath(path));

    [Theory]
    [InlineData("/api/vouchers")]
    [InlineData("/api/invoices")]
    [InlineData("/api/payroll/import")]
    public void Idempotency_StillAppliesToBusinessWrites(string path) => Assert.False(IdempotencyMiddleware.ExcludedPath(path));

    // SEC-03: người sửa nội dung nháp trở thành người lập, nên không tự gửi và duyệt nháp của người khác.
    [Fact]
    public async Task EditingSomeoneElsesDraft_MakesTheEditorThePreparer_SoTheyCannotApproveIt()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000));
        Assert.Equal(h.PreparerId, v.CreatedBy);

        // Kế toán trưởng (có quyền sửa nháp, gửi và duyệt) viết lại nội dung rồi tự gửi duyệt.
        var edited = await s.Engine.UpdateDraftAsync(h.Approver, v.Id, v.RowVersion, h.Sale(9_000_000));
        Assert.Equal(h.ApproverId, edited.CreatedBy);
        await s.Engine.SubmitAsync(h.Approver, v.Id);
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => s.Engine.ApproveAsync(h.Approver, v.Id)));
    }

    [Fact]
    public async Task EditingYourOwnDraft_KeepsYouAsThePreparer()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000));
        var edited = await s.Engine.UpdateDraftAsync(h.Preparer, v.Id, v.RowVersion, h.Sale(2000));
        Assert.Equal(h.PreparerId, edited.CreatedBy);
        await s.Engine.SubmitAsync(h.Preparer, v.Id);
        Assert.Equal(VoucherStatus.Approved, (await s.Engine.ApproveAsync(h.Approver, v.Id)).Status);
    }

    // ACC-01: bút toán đảo không được đảo lại (làm sổ cái lệch với phân hệ và chi tiết công nợ).
    [Fact]
    public async Task AReversalVoucher_CannotBeReversedAgain()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await h.PostedAsync(s, h.Sale(1_200_000));
        var rev = await s.Engine.ReverseAsync(h.Approver, v.Id, new(2026, 3, 20), "Sai đối tượng");

        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.Engine.ReverseAsync(h.Approver, rev.Id, new(2026, 3, 21), "Đảo lại")));
        Assert.Equal(VoucherStatus.Posted, (await s.Db.Vouchers.AsNoTracking().SingleAsync(x => x.Id == rev.Id)).Status);
        Assert.Equal(0m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["AR"], new(2026, 12, 31)));
    }

    // ACC-02 (chị chốt 21/10/2026): chứng từ do phân hệ sinh ra chỉ tính lại ở phân hệ, không sửa tay.
    [Theory]
    [InlineData("HAK.ADVANCE")]
    [InlineData("HAK.EQUITY")]
    [InlineData("HAK.CLOSING")]
    [InlineData("HAK.VAT.OFFSET")]
    public async Task ModuleGeneratedDraft_CannotBeEditedByHand(string source)
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000) with { SourceSystem = source, SourceId = "audit-1" });
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Engine.UpdateDraftAsync(h.Preparer, v.Id, v.RowVersion, h.Sale(9000))));
        Assert.Equal(1000m, (await s.Db.Vouchers.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == v.Id)).TotalDebit);
    }

    [Fact]
    public async Task ImportedAndManualDrafts_StayEditable()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var imported = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000) with { SourceSystem = "HAK.IMPORT", SourceId = "audit-2" });
        Assert.Equal(2000m, (await s.Engine.UpdateDraftAsync(h.Preparer, imported.Id, imported.RowVersion, h.Sale(2000))).TotalDebit);
        var manual = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000));
        Assert.Equal(3000m, (await s.Engine.UpdateDraftAsync(h.Preparer, manual.Id, manual.RowVersion, h.Sale(3000))).TotalDebit);
    }
}
