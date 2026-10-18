using Microsoft.EntityFrameworkCore;
using Hak.Application.Accounting;
using Hak.Domain.Vouchers;

namespace Hak.Integration.Tests;

public class ReportTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<Hak.Domain.Common.DomainException>(act)).Code;

    [Fact]
    public async Task Account_IsRenamed_AndDeactivatedOnlyWhenNothingDependsOnIt()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        await h.PostedAsync(s, h.Sale(1_000));
        await s.Rules.SetRoleMappingAsync(h.Admin, "AR_TRADE", "AR");
        Assert.Equal("VALIDATION_FAILED", await CodeOf(() => s.Setup.UpdateAccountAsync(h.Admin, h.Acc["REV"], "Doanh thu", false)));     // balance ≠ 0
        Assert.Equal("VALIDATION_FAILED", await CodeOf(() => s.Setup.UpdateAccountAsync(h.Admin, h.Acc["ASSETS"], "Tài sản", false)));   // active children
        await s.Setup.UpdateAccountAsync(h.Admin, h.Acc["BANK2"], "Tiền gửi ngân hàng phụ", false);
        var bank2 = await s.Db.Accounts.AsNoTracking().SingleAsync(a => a.Id == h.Acc["BANK2"]);
        Assert.Equal(("Tiền gửi ngân hàng phụ", false), (bank2.Name, bank2.IsActive));
        Assert.Equal("FORBIDDEN", await CodeOf(() => s.Setup.UpdateAccountAsync(h.Auditor, h.Acc["CASH"], "x", true)));
    }

    [Fact]
    public async Task Account_MovesUnderAnotherSummaryAccount_RefusingLoopsAndDetailParents()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        await h.PostedAsync(s, h.Sale(1_000));
        var group = (await s.Setup.CreateAccountAsync(h.Admin, "TIEN", "Tiền (tổng hợp)", "ASSETS", isPostable: false)).Id;
        await s.Setup.MoveAccountAsync(h.Admin, h.Acc["CASH"], "tien");
        Assert.Equal(group, (await s.Db.Accounts.AsNoTracking().SingleAsync(a => a.Id == h.Acc["CASH"])).ParentId);
        Assert.Equal("VALIDATION_FAILED", await CodeOf(() => s.Setup.MoveAccountAsync(h.Admin, h.Acc["ASSETS"], "TIEN")));   // loop
        Assert.Equal("VALIDATION_FAILED", await CodeOf(() => s.Setup.MoveAccountAsync(h.Admin, h.Acc["AR"], "CASH")));      // detail parent
        Assert.Equal("FORBIDDEN", await CodeOf(() => s.Setup.MoveAccountAsync(h.Auditor, h.Acc["AR"], null)));
        await s.Setup.MoveAccountAsync(h.Admin, h.Acc["AR"], null);                                                         // top level
        var tb = await s.Ledger.TrialBalanceAsync(h.Auditor, new(2026, 1, 1), new(2026, 12, 31), rollup: true);
        Assert.Equal(0, tb.Rows.Single(r => r.AccountCode == "AR").Level);
        Assert.True(await s.Db.AuditLogs.AnyAsync(a => a.Action == "account.move"));
    }

    [Fact]
    public async Task TrialBalance_RollsUpParentAccounts_ColumnByColumn_WithoutChangingTheTotals()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        await h.PostedAsync(s, h.Sale(1_000));
        await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, new(2026, 3, 12), new(2026, 3, 12), "Thu tiền mặt",
            [new VoucherLineInput(h.Acc["CASH"], Side.Debit, 300), new VoucherLineInput(h.Acc["AR"], Side.Credit, 300, h.Partner["KH01"])]));

        var flat = await s.Ledger.TrialBalanceAsync(h.Auditor, new(2026, 1, 1), new(2026, 12, 31));
        var tb = await s.Ledger.TrialBalanceAsync(h.Auditor, new(2026, 1, 1), new(2026, 12, 31), rollup: true);
        Assert.Empty(flat.Groups);
        Assert.Equal(flat.Rows.Count, tb.Rows.Count);
        Assert.True(tb.IsBalanced);
        Assert.Equal(flat.TotalClosingDebit, tb.TotalClosingDebit);                 // groups never enter the totals

        var assets = Assert.Single(tb.Groups);
        Assert.Equal(("ASSETS", 0, true), (assets.AccountCode, assets.Level, assets.IsGroup));
        Assert.Equal((1_300m, 300m, 1_000m, 0m), (assets.PeriodDebit, assets.PeriodCredit, assets.ClosingDebit, assets.ClosingCredit));
        Assert.Equal(1, tb.Rows.Single(r => r.AccountCode == "CASH").Level);
    }
}
