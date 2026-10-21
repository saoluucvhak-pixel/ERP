using Hak.Application.Accounting;
using Hak.Domain.Common;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Hai người dùng thao tác cùng lúc (mỗi người một kết nối riêng) trên cùng cơ sở dữ liệu.</summary>
public class ConcurrencyTests
{
    private static VoucherInput Move(TestHost h, DateOnly d, string debit, string credit, decimal amount, string text)
        => new(VoucherTypes.General, d, d, text, [new VoucherLineInput(h.Acc[debit], Side.Debit, amount), new VoucherLineInput(h.Acc[credit], Side.Credit, amount)]);

    internal static async Task<(int Ok, List<Exception> Failed)> RaceAsync(params Func<Task>[] actions)
    {
        var start = new TaskCompletionSource();
        var tasks = actions.Select(a => Task.Run(async () => { await start.Task; try { await a(); return (Exception?)null; } catch (Exception ex) { return ex; } })).ToList();
        start.SetResult();
        var results = await Task.WhenAll(tasks);
        return (results.Count(r => r is null), results.Where(r => r is not null).Select(r => r!).ToList());
    }

    [Fact]
    public async Task TwoUsersSpendingTheSameCash_OnlyOneFits_AndTheFundNeverGoesNegative()
    {
        using var h = await TestHost.CreateAsync();
        using var admin = h.NewScope();
        await h.PostedAsync(admin, Move(h, new(2026, 3, 1), "CASH", "EQUITY", 100, "Góp vốn"));

        // Hai phiếu chi 70 trên quỹ chỉ có 100: mỗi phiếu đã duyệt, cùng ghi sổ một lúc ở hai kết nối.
        var ids = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var v = await admin.Engine.CreateDraftAsync(h.Preparer, Move(h, new(2026, 3, 2), "EXP", "CASH", 70, $"Chi {i + 1}"));
            await admin.Engine.SubmitAsync(h.Preparer, v.Id);
            await admin.Engine.ApproveAsync(h.Approver, v.Id);
            ids.Add(v.Id);
        }
        using var s1 = h.NewScope();
        using var s2 = h.NewScope();
        var (ok, failed) = await RaceAsync(() => s1.Engine.PostAsync(h.Approver, ids[0]), () => s2.Engine.PostAsync(h.Approver, ids[1]));

        Assert.Equal(1, ok);
        Assert.All(failed, f => Assert.IsType<DomainException>(f));
        using var check = h.NewScope();
        Assert.Equal(30m, await check.Ledger.AccountBalanceAsync(h.Auditor, h.Acc["CASH"], new(2026, 12, 31)));
        Assert.Equal(2, await check.Db.JournalEntries.CountAsync());   // góp vốn + đúng một phiếu chi
    }

    [Fact]
    public async Task TwoUsersPostingTheSameVoucher_OnlyOneJournalEntryIsWritten()
    {
        using var h = await TestHost.CreateAsync();
        using var admin = h.NewScope();
        var v = await admin.Engine.CreateDraftAsync(h.Preparer, Move(h, new(2026, 3, 3), "CASH", "EQUITY", 500, "Góp vốn"));
        await admin.Engine.SubmitAsync(h.Preparer, v.Id);
        await admin.Engine.ApproveAsync(h.Approver, v.Id);

        using var s1 = h.NewScope();
        using var s2 = h.NewScope();
        var (ok, failed) = await RaceAsync(() => s1.Engine.PostAsync(h.Approver, v.Id), () => s2.Engine.PostAsync(h.Approver, v.Id));

        Assert.Equal(1, ok);
        Assert.All(failed, f => Assert.IsType<DomainException>(f));
        using var check = h.NewScope();
        Assert.Equal(1, await check.Db.JournalEntries.CountAsync());
        Assert.Equal(2, await check.Db.JournalLines.CountAsync());
        Assert.Null(await check.Audit.VerifyChainAsync(h.CompanyId));   // chuỗi nhật ký kiểm toán vẫn liền sau khi hai người tranh nhau
    }

    [Fact]
    public async Task TwoUsersCancellingAndPostingTheSameDraft_EndInOneConsistentState()
    {
        using var h = await TestHost.CreateAsync();
        using var admin = h.NewScope();
        var v = await admin.Engine.CreateDraftAsync(h.Preparer, Move(h, new(2026, 3, 4), "CASH", "EQUITY", 200, "Góp vốn"));
        await admin.Engine.SubmitAsync(h.Preparer, v.Id);
        await admin.Engine.ApproveAsync(h.Approver, v.Id);

        using var s1 = h.NewScope();
        using var s2 = h.NewScope();
        var (ok, failed) = await RaceAsync(() => s1.Engine.PostAsync(h.Approver, v.Id), () => s2.Engine.RejectAsync(h.Approver, v.Id, "Trả lại để sửa"));

        Assert.Equal(1, ok);
        Assert.All(failed, f => Assert.IsType<DomainException>(f));
        using var check = h.NewScope();
        var status = (await check.Db.Vouchers.AsNoTracking().SingleAsync(x => x.Id == v.Id)).Status;
        var journals = await check.Db.JournalEntries.CountAsync();
        Assert.True((status == VoucherStatus.Posted && journals == 1) || (status != VoucherStatus.Posted && journals == 0), $"Trạng thái {status} nhưng có {journals} bút toán.");
    }
}
