using Hak.Application.Accounting;
using Hak.Application.Loans;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Fx;
using Hak.Domain.Loans;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

/// <summary>Loan contracts: linked drawdowns/repayments, interest accrual runs, rate changes, foreign loans, reconciliation.</summary>
public class LoanTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    private static async Task<TestHost> HostAsync()
    {
        var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        foreach (var (code, name) in new[] { ("LOAN", "Vay ngắn hạn (mã thử)"), ("INT_EXP", "Chi phí lãi vay (mã thử)"), ("INT_ACC", "Lãi vay phải trả (mã thử)") })
            h.Acc[code] = (await s.Setup.CreateAccountAsync(h.Admin, code, name)).Id;
        h.Acc["USDBANK"] = (await s.Setup.CreateAccountAsync(h.Admin, "USDBANK", "Tiền gửi USD (mã thử)", cashKind: CashKind.Bank)).Id;
        h.Partner["VCB"] = (await s.Setup.CreatePartnerAsync(h.Admin, "VCB", "Ngân hàng cho vay (thử)", PartnerRoles.Other)).Id;
        return h;
    }

    private static Task<Guid> PeriodAsync(TestHost h, Scope s, int month)
        => s.Db.AccountingPeriods.Where(p => p.CompanyId == h.CompanyId && p.StartDate == new DateOnly(2026, month, 1)).Select(p => p.Id).SingleAsync();

    private static async Task<decimal> BalanceAsync(Scope s, Guid account)
    {
        var lines = await s.Db.JournalLines.AsNoTracking().Where(l => l.AccountId == account).Select(l => new { l.Side, l.Amount }).ToListAsync();
        return lines.Sum(l => l.Side == Side.Debit ? l.Amount : -l.Amount);
    }

    /// <summary>Posts a VND drawdown (Dr BANK / Cr LOAN) or repayment and returns the LOAN line.</summary>
    private static async Task<Guid> MoveAsync(TestHost h, Scope s, DateOnly d, decimal amount, bool drawdown)
    {
        var loanSide = drawdown ? Side.Credit : Side.Debit;
        var v = await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, d, d, drawdown ? "Giải ngân" : "Trả nợ gốc",
            [new VoucherLineInput(h.Acc["BANK"], drawdown ? Side.Debit : Side.Credit, amount), new VoucherLineInput(h.Acc["LOAN"], loanSide, amount, h.Partner["VCB"])]));
        return await LoanLineAsync(h, s, v.Id);
    }

    private static Task<Guid> LoanLineAsync(TestHost h, Scope s, Guid voucherId)
        => (from l in s.Db.JournalLines join e in s.Db.JournalEntries on l.JournalEntryId equals e.Id
            where e.VoucherId == voucherId && l.AccountId == h.Acc["LOAN"] select l.Id).SingleAsync();

    private static LoanInput Vnd(TestHost h, string code = "KU-01") => new(code, "Vay vốn lưu động", h.Partner["VCB"], h.Acc["LOAN"], h.Acc["INT_EXP"], h.Acc["INT_ACC"],
        2_000_000_000, new(2026, 1, 1), new(2026, 12, 31), 10m);

    [Fact]
    public async Task VndLoan_DrawdownRateChangeRepayment_InterestPerPeriod_AndReconciliation()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var loan = await s.Loans.CreateAsync(h.Preparer, Vnd(h));
        Assert.Equal(ErrorCodes.SodViolation, await CodeOf(() => s.Loans.ActivateAsync(h.Preparer, loan.Id)));
        await s.Loans.ActivateAsync(h.Approver, loan.Id);

        await MoveAsync(h, s, new(2026, 1, 10), 1_000_000_000, drawdown: true);
        var candidate = Assert.Single(await s.Loans.CandidatesAsync(h.Auditor, loan.Id));
        Assert.Equal("Giải ngân", candidate.Side);
        await s.Loans.LinkAsync(h.Preparer, loan.Id, [candidate.JournalLineId]);
        Assert.Equal(ErrorCodes.DuplicateSource, await CodeOf(() => s.Loans.LinkAsync(h.Preparer, loan.Id, [candidate.JournalLineId])));

        await s.Loans.AccrueAsync(h.Approver, await PeriodAsync(h, s, 1));
        Assert.Equal(6_027_397m, await BalanceAsync(s, h.Acc["INT_EXP"]));
        Assert.Equal(ErrorCodes.PeriodAccrued, await CodeOf(() => s.Loans.AddRateAsync(h.Preparer, loan.Id, new(2026, 1, 20), 12m, "Phụ lục 01")));
        await s.Loans.AddRateAsync(h.Preparer, loan.Id, new(2026, 2, 15), 12m, "Phụ lục 01");

        var repayment = await MoveAsync(h, s, new(2026, 2, 20), 500_000_000, drawdown: false);
        await s.Loans.LinkAsync(h.Preparer, loan.Id, [repayment]);
        await s.Loans.AccrueAsync(h.Approver, await PeriodAsync(h, s, 2));
        // 14 days × 1e9 × 10% + 5 days × 1e9 × 12% + 9 days × 5e8 × 12% = 254e9 / 36,500
        Assert.Equal(6_027_397m + 6_958_904m, await BalanceAsync(s, h.Acc["INT_EXP"]));
        Assert.Equal(-(6_027_397m + 6_958_904m), await BalanceAsync(s, h.Acc["INT_ACC"]));

        var row = Assert.Single(await s.Loans.RegisterAsync(h.Auditor, new(2026, 2, 28)));
        Assert.Equal((1_000_000_000m, 500_000_000m, 500_000_000m, 12m, true, 12_986_301m),
            (row.Drawn, row.Repaid, row.Outstanding, row.CurrentRatePercent!.Value, row.DueWithin12Months, row.InterestAccrued));
        Assert.Equal(0m, Assert.Single(await s.Loans.ReconcileAsync(h.Auditor, new(2026, 2, 28))).Difference);

        // A drawdown dated in an accrued period cannot be linked without recomputing that period; unlinked it shows as a difference.
        var late = await MoveAsync(h, s, new(2026, 2, 25), 100_000_000, drawdown: true);
        Assert.Equal(ErrorCodes.PeriodAccrued, await CodeOf(() => s.Loans.LinkAsync(h.Preparer, loan.Id, [late])));
        Assert.Equal(100_000_000m, Assert.Single(await s.Loans.ReconcileAsync(h.Auditor, new(2026, 2, 28))).Difference);

        // Runs reverse latest first; their vouchers belong to the loan module.
        var runs = await s.Loans.RunsAsync(h.Auditor);
        var feb = runs.First();
        Assert.Equal(ErrorCodes.PeriodAccrued, await CodeOf(() => s.Loans.ReverseRunAsync(h.Approver, runs.Last().Id, "x")));
        Assert.Equal(ErrorCodes.InvalidStateTransition, await CodeOf(() => s.Engine.ReverseAsync(h.Approver, feb.VoucherId!.Value, new(2026, 2, 28), "x")));
        await s.Loans.ReverseRunAsync(h.Approver, feb.Id, "Sót giải ngân 25/02");
        await s.Loans.LinkAsync(h.Preparer, loan.Id, [late]);
        await s.Loans.AccrueAsync(h.Approver, await PeriodAsync(h, s, 2));
        Assert.Equal(0m, Assert.Single(await s.Loans.ReconcileAsync(h.Auditor, new(2026, 2, 28))).Difference);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Loans.CloseAsync(h.Preparer, loan.Id)));
    }

    [Fact]
    public async Task UsdLoan_InterestInLoanCurrency_ConvertedAtRunRate_FirstRunGuard_AndClose()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        var loan = await s.Loans.CreateAsync(h.Preparer, new LoanInput("KU-USD", "Vay USD thanh toán nhập khẩu", h.Partner["VCB"], h.Acc["LOAN"], h.Acc["INT_EXP"],
            h.Acc["INT_ACC"], 200_000, new(2026, 1, 1), new(2027, 6, 30), 6m, DayCountBasis.Actual360, "USD"));
        await s.Loans.ActivateAsync(h.Approver, loan.Id);
        var v = await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, new(2026, 1, 1), new(2026, 1, 1), "Giải ngân USD",
        [
            new VoucherLineInput(h.Acc["USDBANK"], Side.Debit, 2_500_000_000, null, "USD", 100_000, 25_000),
            new VoucherLineInput(h.Acc["LOAN"], Side.Credit, 2_500_000_000, h.Partner["VCB"], "USD", 100_000, 25_000),
        ]));
        await s.Loans.LinkAsync(h.Preparer, loan.Id, [await LoanLineAsync(h, s, v.Id)]);

        Assert.Equal(ErrorCodes.InterestPending, await CodeOf(async () => await s.Loans.AccrueAsync(h.Approver, await PeriodAsync(h, s, 2), [new FxRate("USD", 25_400, 25_400)])));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(async () => await s.Loans.AccrueAsync(h.Approver, await PeriodAsync(h, s, 1))));
        await s.Loans.AccrueAsync(h.Approver, await PeriodAsync(h, s, 1), [new FxRate("USD", 25_400, 25_400)]);
        // 100,000 × 6% × 31 / 360 = 516.67 USD × 25,400
        Assert.Equal(13_123_418m, await BalanceAsync(s, h.Acc["INT_EXP"]));
        var accrued = await s.Db.JournalLines.AsNoTracking().SingleAsync(l => l.AccountId == h.Acc["INT_ACC"]);
        Assert.Equal(("USD", 516.67m), (accrued.ForeignCurrency, accrued.ForeignAmount!.Value));

        var row = Assert.Single(await s.Loans.RegisterAsync(h.Auditor, new(2026, 1, 31)));
        Assert.Equal((100_000m, false), (row.OutstandingForeign!.Value, row.DueWithin12Months));

        var repay = await h.PostedAsync(s, new VoucherInput(VoucherTypes.General, new(2026, 2, 5), new(2026, 2, 5), "Trả hết nợ gốc USD",
        [
            new VoucherLineInput(h.Acc["LOAN"], Side.Debit, 2_540_000_000, h.Partner["VCB"], "USD", 100_000, 25_400),
            new VoucherLineInput(h.Acc["USDBANK"], Side.Credit, 2_540_000_000, null, "USD", 100_000, 25_400),
        ]));
        await s.Loans.LinkAsync(h.Preparer, loan.Id, [await LoanLineAsync(h, s, repay.Id)]);
        Assert.Equal(LoanStatus.Closed, (await s.Loans.CloseAsync(h.Preparer, loan.Id)).Status);
        Assert.Equal(0m, Assert.Single(await s.Loans.ReconcileAsync(h.Auditor, new(2026, 2, 28))).Difference);
    }

    [Fact]
    public async Task ContractRules_AndImmutability()
    {
        using var h = await HostAsync();
        using var s = h.NewScope();
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Loans.CreateAsync(h.Preparer, Vnd(h) with { PrincipalAccountId = h.Acc["AP"] })));
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(() => s.Loans.CreateAsync(h.Preparer, Vnd(h) with { MaturityDate = new(2025, 1, 1) })));
        var loan = await s.Loans.CreateAsync(h.Preparer, Vnd(h));
        Assert.Equal(ErrorCodes.DuplicateCode, await CodeOf(() => s.Loans.CreateAsync(h.Preparer, Vnd(h))));
        await s.Loans.ActivateAsync(h.Approver, loan.Id);
        var line = await MoveAsync(h, s, new(2026, 1, 10), 1_000, drawdown: true);
        await s.Loans.LinkAsync(h.Preparer, loan.Id, [line]);

        using (var s2 = h.NewScope())
        {
            var tracked = await s2.Db.LoanContracts.SingleAsync(l => l.Id == loan.Id);
            tracked.ContractAmount = 1;
            Assert.Equal(ErrorCodes.LedgerImmutable, (await Assert.ThrowsAsync<DomainException>(() => s2.Db.SaveChangesAsync())).Code);
        }
        Assert.Contains("LEDGER_IMMUTABLE", (await Assert.ThrowsAnyAsync<Exception>(() => s.Db.Database.ExecuteSqlRawAsync("DELETE FROM loan_links"))).Message);
        Assert.Contains("LEDGER_IMMUTABLE", (await Assert.ThrowsAnyAsync<Exception>(() => s.Db.Database.ExecuteSqlRawAsync("UPDATE loan_rates SET annual_rate_percent = 0"))).Message);
        Assert.Contains("LEDGER_IMMUTABLE", (await Assert.ThrowsAnyAsync<Exception>(() => s.Db.Database.ExecuteSqlRawAsync("UPDATE loan_contracts SET maturity_date = start_date"))).Message);
    }
}
