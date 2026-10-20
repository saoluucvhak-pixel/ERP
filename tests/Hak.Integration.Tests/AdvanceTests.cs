using Hak.Application.Advances;
using Hak.Application.Accounting;
using Hak.Domain.Advances;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Tạm ứng / hoàn ứng và kiểm kê quỹ.</summary>
public class AdvanceTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    private static async Task<(TestHost H, Scope S, AdvanceService Adv, Guid Emp)> SetupAsync()
    {
        var h = await TestHost.CreateAsync();
        var s = h.NewScope();
        h.Acc["ADV"] = (await s.Setup.CreateAccountAsync(h.Admin, "ADV", "Tạm ứng (thử)", null, true, true, BalanceDisplay.GrossByPartner, OpenItemNature.Receivable)).Id;
        await s.Rules.SetRoleMappingAsync(h.Admin, "ADVANCE", "ADV");
        var emp = (await s.Setup.CreatePartnerAsync(h.Admin, "NV01", "Nguyễn Văn A", PartnerRoles.Employee)).Id;
        h.Clock.UtcNow = new DateTime(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc);
        // Quỹ có sẵn tiền để chi.
        await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, new(2026, 3, 1), new(2026, 3, 1), "Góp vốn", [new(h.Acc["CASH"], Side.Debit, 100_000_000), new(h.Acc["EQUITY"], Side.Credit, 100_000_000)]));
        return (h, s, new AdvanceService(s.Db, s.Engine, s.Audit, h.Clock), emp);
    }

    private static async Task PostAsync(TestHost h, Scope s, Guid voucherId)
    {
        await s.Engine.SubmitAsync(h.Preparer, voucherId);
        await s.Engine.ApproveAsync(h.Approver, voucherId);
        await s.Engine.PostAsync(h.Approver, voucherId);
    }

    [Fact]
    public async Task Advance_Request_Approve_Pay_Settle_WithRefund()
    {
        var (h, s, adv, emp) = await SetupAsync();
        using var _ = h; using var __ = s;
        Assert.Equal(ErrorCodes.AmountNotPositive, await CodeOf(() => adv.CreateAsync(h.Preparer, new(emp, "Công tác", 0, new(2026, 3, 10), new(2026, 3, 20)))));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => adv.CreateAsync(h.Preparer, new(h.Partner["KH01"], "Công tác", 1, new(2026, 3, 10), new(2026, 3, 20)))));  // không phải nhân viên
        var a = await adv.CreateAsync(h.Preparer, new(emp, "Công tác Hà Nội", 10_000_000, new(2026, 3, 10), new(2026, 3, 20)));
        Assert.Equal("TU-2026-00001", a.Number);

        // Chưa duyệt: chưa lập chứng từ chi; người lập không tự duyệt; duyệt xong mới lập.
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => adv.CreatePaymentVoucherAsync(h.Preparer, a.Id, h.Acc["CASH"], new(2026, 3, 11))));
        await adv.DecideAsync(h.Approver, a.Id, true, null);
        var pay = await adv.CreatePaymentVoucherAsync(h.Preparer, a.Id, h.Acc["CASH"], new(2026, 3, 11));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => adv.CreatePaymentVoucherAsync(h.Preparer, a.Id, h.Acc["CASH"], new(2026, 3, 11))));   // đã có chứng từ chờ ghi sổ
        var row = (await adv.ListAsync(h.Auditor, new(2026, 3, 11))).Single();
        Assert.Equal((AdvanceStatus.Approved, true, 0m), (row.Status, row.PaymentPending, row.Outstanding));

        // Ghi sổ chứng từ chi → Đã chi, số dư mở = số tạm ứng; chưa hết hạn.
        await PostAsync(h, s, pay.Id);
        row = (await adv.ListAsync(h.Auditor, new(2026, 3, 15))).Single();
        Assert.Equal((AdvanceStatus.Paid, 10_000_000m, 0), (row.Status, row.Outstanding, row.OverdueDays));
        // Quá hạn 12 ngày, rơi vào cột 1–30.
        var aging = (await adv.AgingAsync(h.Auditor, new(2026, 4, 1))).Single();
        Assert.Equal((0m, 10_000_000m, 10_000_000m), (aging.NotDue, aging.Over1To30, aging.Total));

        // Quyết toán chi 7,5 triệu: hoàn lại 2,5 triệu vào quỹ; nhập thiếu TK tiền thì lỗi; TK chi phí sai bị chặn.
        var lines = new AdvanceLineInput[] { new(h.Acc["EXP"], 5_000_000, "Vé máy bay", "HĐ-001", new(2026, 3, 12)), new(h.Acc["EXP"], 2_500_000, "Khách sạn", "HĐ-002", new(2026, 3, 13)) };
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => adv.SettleAsync(h.Preparer, a.Id, new(2026, 3, 16), lines, null)));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => adv.SettleAsync(h.Preparer, a.Id, new(2026, 3, 16), [new(h.Acc["AR"], 1_000, null)], h.Acc["CASH"])));
        var set = await adv.SettleAsync(h.Preparer, a.Id, new(2026, 3, 16), lines, h.Acc["CASH"]);
        row = (await adv.ListAsync(h.Auditor, new(2026, 3, 16))).Single();
        Assert.Equal((AdvanceStatus.Paid, true, 7_500_000m), (row.Status, row.SettlementPending, row.SpentAmount));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => adv.SettleAsync(h.Preparer, a.Id, new(2026, 3, 16), lines, h.Acc["CASH"])));
        await PostAsync(h, s, set.Id);

        row = (await adv.ListAsync(h.Auditor, new(2026, 3, 16))).Single();
        Assert.Equal((AdvanceStatus.Settled, 0m), (row.Status, row.Outstanding));
        // Sổ cái: TK tạm ứng của nhân viên về 0, chi phí 7,5 triệu, quỹ giảm đúng 7,5 triệu.
        Assert.Equal(0m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["ADV"], new(2026, 3, 31), emp));
        Assert.Equal(7_500_000m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["EXP"], new(2026, 3, 31)));
        Assert.Equal(92_500_000m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["CASH"], new(2026, 3, 31)));
        var view = await adv.GetAsync(h.Auditor, a.Id);
        Assert.Equal(2, view.Lines.Count);
        Assert.Empty(await adv.AgingAsync(h.Auditor, new(2026, 4, 1)));
    }

    [Fact]
    public async Task Settlement_SpendingMoreThanAdvance_PaysTheDifference_AndRoleMustBeMapped()
    {
        var (h, s, adv, emp) = await SetupAsync();
        using var _ = h; using var __ = s;
        var a = await adv.CreateAsync(h.Preparer, new(emp, "Mua vật tư lặt vặt", 4_000_000, new(2026, 3, 10), new(2026, 3, 15)));
        await adv.DecideAsync(h.Approver, a.Id, true, null);
        await PostAsync(h, s, (await adv.CreatePaymentVoucherAsync(h.Preparer, a.Id, h.Acc["CASH"], new(2026, 3, 10))).Id);
        var set = await adv.SettleAsync(h.Preparer, a.Id, new(2026, 3, 14), [new(h.Acc["EXP"], 5_000_000, "Vật tư")], h.Acc["CASH"]);
        await PostAsync(h, s, set.Id);
        Assert.Equal(0m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["ADV"], new(2026, 3, 31), emp));
        Assert.Equal(5_000_000m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["EXP"], new(2026, 3, 31)));
        Assert.Equal(95_000_000m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["CASH"], new(2026, 3, 31)));   // chi 4tr tạm ứng + 1tr bổ sung

        // Chưa ánh xạ vai trò: không lập được chứng từ chi.
        var h2 = await TestHost.CreateAsync(); using var _h2 = h2; using var s3 = h2.NewScope();
        var emp2 = (await s3.Setup.CreatePartnerAsync(h2.Admin, "NV01", "NV", PartnerRoles.Employee)).Id;
        h2.Clock.UtcNow = new DateTime(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc);
        var adv2 = new AdvanceService(s3.Db, s3.Engine, s3.Audit, h2.Clock);
        var b = await adv2.CreateAsync(h2.Preparer, new(emp2, "x", 1000, new(2026, 3, 10), new(2026, 3, 12)));
        await adv2.DecideAsync(h2.Approver, b.Id, true, null);
        Assert.Equal(ErrorCodes.RuleRoleUnmapped, await CodeOf(() => adv2.CreatePaymentVoucherAsync(h2.Preparer, b.Id, h2.Acc["CASH"], new(2026, 3, 10))));
    }

    [Fact]
    public async Task Rejection_NeedsReason_And_CancelBeforePayment()
    {
        var (h, s, adv, emp) = await SetupAsync();
        using var _ = h; using var __ = s;
        var a = await adv.CreateAsync(h.Preparer, new(emp, "Thử", 1_000_000, new(2026, 3, 10), new(2026, 3, 12)));
        var own = await adv.CreateAsync(h.Approver, new(emp, "Tự lập", 1_000_000, new(2026, 3, 10), new(2026, 3, 12)));
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => adv.DecideAsync(h.Approver, own.Id, true, null)));   // người lập không tự duyệt
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => adv.DecideAsync(h.Approver, a.Id, false, " ")));
        await adv.DecideAsync(h.Approver, a.Id, false, "Chưa cần");
        Assert.Equal(AdvanceStatus.Rejected, (await adv.ListAsync(h.Auditor, new(2026, 3, 10))).Single(r => r.Id == a.Id).Status);
        var b = await adv.CreateAsync(h.Preparer, new(emp, "Thử 2", 1_000_000, new(2026, 3, 10), new(2026, 3, 12)));
        await adv.CancelAsync(h.Preparer, b.Id, "Nhập nhầm");
        Assert.Contains(await adv.ListAsync(h.Auditor, new(2026, 3, 10)), r => r.Id == b.Id && r.Status == AdvanceStatus.Cancelled);
    }

    [Fact]
    public async Task CashCount_Confirm_FreezesBookBalance_And_AdjustsShortageAndSurplus()
    {
        var (h, s, _, _) = await SetupAsync();
        using var _h = h; using var __ = s;
        h.Acc["SHORT"] = (await s.Setup.CreateAccountAsync(h.Admin, "SHORT", "Tài sản thiếu chờ xử lý (thử)")).Id;
        h.Acc["OVER"] = (await s.Setup.CreateAccountAsync(h.Admin, "OVER", "Tài sản thừa chờ xử lý (thử)")).Id;
        var counts = new CashCountService(s.Db, s.Engine, s.Ledger, s.Audit, h.Clock);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => counts.CreateAsync(h.Preparer, new(h.Acc["BANK"], new(2026, 3, 31), [new(500_000, 1)]))));   // không phải quỹ tiền mặt
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => counts.CreateAsync(h.Preparer, new(h.Acc["CASH"], new(2026, 3, 31), [new(500_000, 1), new(500_000, 2)]))));

        // Sổ quỹ 100 triệu; đếm được 99.5 triệu → thiếu 500.000.
        var c = await counts.CreateAsync(h.Preparer, new(h.Acc["CASH"], new(2026, 3, 31), [new(500_000, 198), new(200_000, 2), new(100_000, 1)], "Kiểm kê cuối tháng", "Thủ quỹ, KTT"));
        Assert.Equal(99_500_000m, c.CountedTotal);
        var own = await counts.CreateAsync(h.Approver, new(h.Acc["CASH"], new(2026, 3, 31), [new(500_000, 1)]));
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => counts.ConfirmAsync(h.Approver, own.Id)));
        await counts.CancelAsync(h.Approver, own.Id, "Thử");
        await counts.ConfirmAsync(h.Approver, c.Id);
        var row = (await counts.ListAsync(h.Auditor, null, null)).Single(r => r.Id == c.Id);
        Assert.Equal((CashCountStatus.Confirmed, 100_000_000m, -500_000m), (row.Status, row.BookBalance, row.Difference));
        Assert.Equal(ErrorCodes.RuleRoleUnmapped, await CodeOf(() => counts.CreateAdjustmentVoucherAsync(h.Preparer, c.Id, new(2026, 3, 31))));

        await s.Rules.SetRoleMappingAsync(h.Admin, CashCountService.ShortageRole, "SHORT");
        await s.Rules.SetRoleMappingAsync(h.Admin, CashCountService.SurplusRole, "OVER");
        var v = await counts.CreateAdjustmentVoucherAsync(h.Preparer, c.Id, new(2026, 3, 31));
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => counts.CreateAdjustmentVoucherAsync(h.Preparer, c.Id, new(2026, 3, 31))));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => counts.CancelAsync(h.Preparer, c.Id, "x")));
        await PostAsync(h, s, v.Id);
        Assert.Equal(99_500_000m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["CASH"], new(2026, 3, 31)));
        Assert.Equal(500_000m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["SHORT"], new(2026, 3, 31)));
        var view = await counts.GetAsync(h.Auditor, c.Id);
        Assert.Equal(3, view.Lines.Count);
        Assert.Equal("Kế toán trưởng", view.ConfirmedByName);

        // Đếm lại sau khi đã khớp sổ (99,5 triệu): khớp thì không có chênh lệch để xử lý.
        var c2 = await counts.CreateAsync(h.Preparer, new(h.Acc["CASH"], new(2026, 4, 1), [new(500_000, 199)]));
        await counts.ConfirmAsync(h.Approver, c2.Id);
        Assert.Equal(0m, (await counts.GetAsync(h.Auditor, c2.Id)).Row.Difference);
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => counts.CreateAdjustmentVoucherAsync(h.Preparer, c2.Id, new(2026, 4, 1))));
        await counts.CancelAsync(h.Preparer, c2.Id, "Đếm nhầm");
        Assert.Equal(CashCountStatus.Cancelled, (await counts.GetAsync(h.Auditor, c2.Id)).Row.Status);
    }
}
