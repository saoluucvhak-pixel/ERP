using Hak.Application.Accounting;
using Hak.Domain.Common;
using Hak.Domain.Ledger;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hak.Integration.Tests;

public class PostingEngineTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    [Fact]
    public async Task Voucher_To_Journal_To_TrialBalance()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        await h.PostedAsync(s, h.Sale(1_500_000));
        await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, new(2026, 3, 12), new(2026, 3, 12), "Thu tiền khách hàng",
            [new(h.Acc["CASH"], Side.Debit, 1_000_000), new(h.Acc["AR"], Side.Credit, 1_000_000, h.Partner["KH01"])]));

        var entries = await s.Db.JournalEntries.Include(e => e.Lines).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(0m, e.Lines.Sum(l => (int)l.Side * l.Amount)));

        var tb = await s.Ledger.TrialBalanceAsync(h.Auditor, new(2026, 3, 1), new(2026, 3, 31));
        Assert.True(tb.IsBalanced);
        var ar = tb.Rows.Single(r => r.AccountCode == "AR");
        Assert.Equal((1_500_000m, 1_000_000m, 500_000m, 0m), (ar.PeriodDebit, ar.PeriodCredit, ar.ClosingDebit, ar.ClosingCredit));
        Assert.Equal(500_000m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["AR"], new(2026, 3, 31)));
        Assert.Equal(-1_500_000m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["REV"], new(2026, 3, 31)));
        Assert.Equal(4, (await s.Ledger.GeneralJournalAsync(h.Auditor, new(2026, 3, 1), new(2026, 3, 31))).Count);
    }

    [Fact]
    public async Task TrialBalance_OpeningPlusPeriod_EqualsClosing_AcrossPeriods()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        await h.PostedAsync(s, h.Sale(2_000_000, new(2026, 2, 5)));
        await h.PostedAsync(s, h.Sale(3_000_000, new(2026, 3, 5)));

        var tb = await s.Ledger.TrialBalanceAsync(h.Auditor, new(2026, 3, 1), new(2026, 3, 31));
        var rev = tb.Rows.Single(r => r.AccountCode == "REV");
        Assert.Equal((0m, 2_000_000m), (rev.OpeningDebit, rev.OpeningCredit));
        Assert.Equal(3_000_000m, rev.PeriodCredit);
        Assert.Equal(5_000_000m, rev.ClosingCredit);
        Assert.True(tb.IsBalanced);
    }

    [Fact]
    public async Task TrialBalance_DualNatureAccount_ShowsGrossBalancesPerPartner()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        await h.PostedAsync(s, h.Sale(100_000_000, partner: "KH01"));
        // KH02 pays in advance: credit balance on the receivable account.
        await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, new(2026, 3, 11), new(2026, 3, 11), "Khách hàng ứng trước",
            [new(h.Acc["BANK"], Side.Debit, 30_000_000), new(h.Acc["AR"], Side.Credit, 30_000_000, h.Partner["KH02"])]));

        var ar = (await s.Ledger.TrialBalanceAsync(h.Auditor, new(2026, 3, 1), new(2026, 3, 31))).Rows.Single(r => r.AccountCode == "AR");
        Assert.Equal(100_000_000m, ar.ClosingDebit);   // not netted to 70,000,000
        Assert.Equal(30_000_000m, ar.ClosingCredit);
    }

    [Fact]
    public async Task LargeAmounts_AggregateExactly()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        const decimal big = 99_999_999_999_999m; // largest amount allowed by the exchange schema
        await h.PostedAsync(s, h.Sale(big));
        await h.PostedAsync(s, h.Sale(big));
        await h.PostedAsync(s, h.Sale(1));
        Assert.Equal(2 * big + 1, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["AR"], new(2026, 12, 31)));
    }

    [Fact]
    public async Task Unbalanced_IsRejectedAtSubmit()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await s.Engine.CreateDraftAsync(h.Preparer, new VoucherInput(VoucherTypes.General, new(2026, 3, 1), new(2026, 3, 1), "Lệch",
            [new(h.Acc["CASH"], Side.Debit, 1000), new(h.Acc["REV"], Side.Credit, 999)]));
        Assert.Equal(ErrorCodes.VoucherUnbalanced, await CodeOf(() => s.Engine.SubmitAsync(h.Preparer, v.Id)));
        Assert.Contains(await s.Engine.ValidateAsync(h.Preparer, v.Id), e => e.Code == ErrorCodes.VoucherUnbalanced);
    }

    [Fact]
    public async Task VndFraction_IsRejected()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000.5m));
        Assert.Equal(ErrorCodes.AmountPrecision, await CodeOf(() => s.Engine.SubmitAsync(h.Preparer, v.Id)));
    }

    [Fact]
    public async Task LockedPeriod_BlocksPosting_ReopenNeedsReasonAndPermission()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000));
        await s.Engine.SubmitAsync(h.Preparer, v.Id);
        await s.Engine.ApproveAsync(h.Approver, v.Id);

        var march = await s.Db.AccountingPeriods.SingleAsync(p => p.PeriodNo == 3);
        // An approved but unposted voucher blocks closing the period.
        Assert.Equal(ErrorCodes.PeriodHasPending, await CodeOf(() => s.Setup.LockPeriodAsync(h.Approver, march.Id)));
        await s.Engine.RejectAsync(h.Approver, v.Id, "Chờ chứng từ gốc");
        await s.Setup.LockPeriodAsync(h.Approver, march.Id);
        Assert.Equal(ErrorCodes.PeriodLocked, await CodeOf(() => s.Engine.SubmitAsync(h.Preparer, v.Id)));
        Assert.False(await s.Db.JournalEntries.AnyAsync());

        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Setup.ReopenPeriodAsync(h.Preparer, march.Id, "x")));
        Assert.Equal(ErrorCodes.ReasonRequired, await CodeOf(() => s.Setup.ReopenPeriodAsync(h.Approver, march.Id, " ")));
        await s.Setup.ReopenPeriodAsync(h.Approver, march.Id, "Bổ sung chứng từ thiếu theo yêu cầu KTT");
        await s.Engine.SubmitAsync(h.Preparer, v.Id);
        await s.Engine.ApproveAsync(h.Approver, v.Id);
        await s.Engine.PostAsync(h.Approver, v.Id);
        Assert.True(await s.Db.AuditLogs.AnyAsync(a => a.Action == "period.reopen"));
    }

    [Fact]
    public async Task PostTwice_IsRejected_AndLedgerHasOneEntry()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await h.PostedAsync(s, h.Sale(1000));
        Assert.Equal(ErrorCodes.AlreadyPosted, await CodeOf(() => s.Engine.PostAsync(h.Approver, v.Id)));
        using var other = h.NewScope(); // a second client retrying the same request
        Assert.Equal(ErrorCodes.AlreadyPosted, await CodeOf(() => other.Engine.PostAsync(h.Approver, v.Id)));
        Assert.Equal(1, await s.Db.JournalEntries.CountAsync(e => e.VoucherId == v.Id));
    }

    private sealed class FailOnJournalInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> r, CancellationToken ct = default)
        {
            if (e.Context!.ChangeTracker.Entries<JournalEntry>().Any(x => x.State == EntityState.Added))
                throw new IOException("Simulated power loss / disk failure during posting");
            return ValueTask.FromResult(r);
        }
    }

    [Fact]
    public async Task Post_IsAtomic_WhenFailureOccursInsideTransaction()
    {
        using var h = await TestHost.CreateAsync();
        Guid id;
        using (var s = h.NewScope())
        {
            var v = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000));
            await s.Engine.SubmitAsync(h.Preparer, v.Id);
            await s.Engine.ApproveAsync(h.Approver, v.Id);
            id = v.Id;
        }
        using (var failing = h.NewScope(new FailOnJournalInsert()))
            await Assert.ThrowsAsync<IOException>(() => failing.Engine.PostAsync(h.Approver, id));

        using var check = h.NewScope();
        var after = await check.Db.Vouchers.SingleAsync(x => x.Id == id);
        Assert.Equal(VoucherStatus.Approved, after.Status);
        Assert.False(await check.Db.JournalEntries.AnyAsync());
        Assert.False(await check.Db.AuditLogs.AnyAsync(a => a.Action == "voucher.post"));
        await check.Engine.PostAsync(h.Approver, id); // recoverable once the fault is gone
    }

    [Fact]
    public async Task UncommittedTransaction_LeavesNoTrace()
    {
        using var h = await TestHost.CreateAsync();
        using (var s = h.NewScope())
        {
            await using var tx = await s.Db.Database.BeginTransactionAsync();
            await s.Db.Database.ExecuteSqlRawAsync("INSERT INTO currencies(code,name,decimal_places) VALUES ('XAU','Gold',4)");
            // connection closed without commit, as in a crash
        }
        using var check = h.NewScope();
        Assert.False(await check.Db.Currencies.AnyAsync(c => c.Code == "XAU"));
    }

    [Fact]
    public async Task Ledger_DirectSqlUpdateOrDelete_IsBlockedByTriggers()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        await h.PostedAsync(s, h.Sale(1000));
        var ex1 = await Assert.ThrowsAnyAsync<Exception>(() => s.Db.Database.ExecuteSqlRawAsync("UPDATE journal_lines SET amount = 1"));
        Assert.Contains("LEDGER_IMMUTABLE", ex1.Message);
        var ex2 = await Assert.ThrowsAnyAsync<Exception>(() => s.Db.Database.ExecuteSqlRawAsync("DELETE FROM journal_entries"));
        Assert.Contains("LEDGER_IMMUTABLE", ex2.Message);
        var ex3 = await Assert.ThrowsAnyAsync<Exception>(() => s.Db.Database.ExecuteSqlRawAsync("UPDATE vouchers SET description = 'sửa lén' WHERE status = 4"));
        Assert.Contains("LEDGER_IMMUTABLE", ex3.Message);
        var ex4 = await Assert.ThrowsAnyAsync<Exception>(() => s.Db.Database.ExecuteSqlRawAsync("UPDATE voucher_lines SET amount = 1"));
        Assert.Contains("LEDGER_IMMUTABLE", ex4.Message);
    }

    [Fact]
    public async Task Ledger_ModificationThroughEf_IsBlockedByInterceptor()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await h.PostedAsync(s, h.Sale(1000));
        var line = await s.Db.JournalLines.FirstAsync();
        line.Amount = 1;
        Assert.Equal(ErrorCodes.LedgerImmutable, (await Assert.ThrowsAsync<DomainException>(() => s.Db.SaveChangesAsync())).Code);
        s.Db.ChangeTracker.Clear();

        var posted = await s.Db.Vouchers.SingleAsync(x => x.Id == v.Id);
        posted.Description = "sửa trực tiếp";
        Assert.Equal(ErrorCodes.LedgerImmutable, (await Assert.ThrowsAsync<DomainException>(() => s.Db.SaveChangesAsync())).Code);
        s.Db.ChangeTracker.Clear();

        Assert.Equal(ErrorCodes.LedgerImmutable, await CodeOf(() => s.Engine.UpdateDraftAsync(h.Preparer, v.Id, 4, h.Sale(2000))));
    }

    [Fact]
    public async Task Reverse_CreatesMirrorVoucher_NetsToZero_AndOnlyOnce()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await h.PostedAsync(s, h.Sale(1_200_000));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Engine.ReverseAsync(h.Preparer, v.Id, new(2026, 3, 20), "sai đối tượng")));

        var rev = await s.Engine.ReverseAsync(h.Approver, v.Id, new(2026, 3, 20), "Sai đối tượng");
        Assert.Equal(VoucherTypes.Reversal, rev.VoucherType);
        Assert.Equal(VoucherStatus.Posted, rev.Status);
        Assert.Equal(v.Id, rev.ReversesVoucherId);
        Assert.Equal(VoucherStatus.Reversed, (await s.Db.Vouchers.AsNoTracking().SingleAsync(x => x.Id == v.Id)).Status);
        Assert.Equal(0m, await s.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["AR"], new(2026, 12, 31)));
        Assert.Equal(2, await s.Db.JournalEntries.CountAsync()); // original journal untouched + reversal

        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.Engine.ReverseAsync(h.Approver, v.Id, new(2026, 3, 21), "lần 2")));
    }

    [Fact]
    public async Task Adjustment_IsLinkedDraft_ThatFollowsNormalApproval()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await h.PostedAsync(s, h.Sale(1_000_000));
        var adj = await s.Engine.CreateAdjustmentAsync(h.Preparer, v.Id, h.Sale(50_000, new(2026, 3, 25)));
        Assert.Equal((VoucherTypes.Adjustment, VoucherStatus.Draft, (Guid?)v.Id), (adj.VoucherType, adj.Status, adj.AdjustsVoucherId));
    }

    [Fact]
    public async Task SegregationOfDuties_CreatorCannotApproveOwnVoucher()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await s.Engine.CreateDraftAsync(h.Approver, h.Sale(1000)); // chief accountant prepares
        await s.Engine.SubmitAsync(h.Approver, v.Id);
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => s.Engine.ApproveAsync(h.Approver, v.Id)));
    }

    [Fact]
    public async Task Permissions_AreEnforcedInBackend()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Engine.CreateDraftAsync(h.Auditor, h.Sale(1000))));
        var v = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000));
        await s.Engine.SubmitAsync(h.Preparer, v.Id);
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Engine.ApproveAsync(h.Preparer, v.Id)));
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Setup.LockPeriodAsync(h.Preparer, Guid.NewGuid())));
    }

    [Fact]
    public async Task InvalidTransitions_AreRejected()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.Engine.ApproveAsync(h.Approver, v.Id)));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.Engine.PostAsync(h.Approver, v.Id)));
        await s.Engine.SubmitAsync(h.Preparer, v.Id);
        await s.Engine.RejectAsync(h.Approver, v.Id, "Thiếu hóa đơn");
        await s.Engine.CancelAsync(h.Preparer, v.Id, "Lập nhầm");
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.Engine.SubmitAsync(h.Preparer, v.Id)));
        var history = await s.Db.VoucherHistory.Where(x => x.VoucherId == v.Id).OrderBy(x => x.At).ThenBy(x => x.VoucherRowVersion).Select(x => x.Action).ToListAsync();
        Assert.Equal(["create", "submit", "reject", "cancel"], history);
    }

    [Fact]
    public async Task DuplicateSource_IsRejected()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000, sourceId: "INV-0001"));
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000, sourceId: "INV-0001"))));
    }

    [Fact]
    public async Task ConcurrentEdit_SecondWriterGetsConflict()
    {
        using var h = await TestHost.CreateAsync();
        Guid id;
        using (var s = h.NewScope()) id = (await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1000))).Id;

        using var a = h.NewScope();
        using var b = h.NewScope();
        await a.Engine.UpdateDraftAsync(h.Preparer, id, 1, h.Sale(2000));
        Assert.Equal(ErrorCodes.ConcurrencyConflict, await CodeOf(() => b.Engine.UpdateDraftAsync(h.Preparer, id, 1, h.Sale(3000))));
        using var c = h.NewScope();
        Assert.Equal(2000m, (await c.Db.Vouchers.Include(v => v.Lines).SingleAsync(v => v.Id == id)).Lines[0].Amount);
    }

    [Fact]
    public async Task VoucherNumbers_AreSequentialPerTypeAndYear()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var n1 = (await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1))).Number;
        var n2 = (await s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1))).Number;
        Assert.Equal(("GJ2026-000001", "GJ2026-000002"), (n1, n2));
    }

    [Fact]
    public async Task PostingOutsideFiscalYear_IsRejected()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        Assert.Equal(ErrorCodes.FiscalYearNotFound, await CodeOf(() => s.Engine.CreateDraftAsync(h.Preparer, h.Sale(1, new(2027, 1, 5)))));
    }

    [Fact]
    public async Task HeaderAccount_AndMissingPartner_AreRejected()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = await s.Engine.CreateDraftAsync(h.Preparer, new VoucherInput(VoucherTypes.General, new(2026, 3, 1), new(2026, 3, 1), "x",
            [new(h.Acc["ASSETS"], Side.Debit, 10), new(h.Acc["AR"], Side.Credit, 10)]));
        var codes = (await s.Engine.ValidateAsync(h.Preparer, v.Id)).Select(e => e.Code).ToList();
        Assert.Contains(ErrorCodes.AccountNotPostable, codes);
        Assert.Contains(ErrorCodes.PartnerRequired, codes);
    }
}
