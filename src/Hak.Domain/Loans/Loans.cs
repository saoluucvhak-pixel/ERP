using Hak.Domain.Common;

namespace Hak.Domain.Loans;

public enum LoanStatus { Draft = 1, Active = 2, Closed = 3, Cancelled = 9 }

/// <summary>Days in the year used to turn an annual rate into a daily one. Which one applies is set by each contract [CẦN XÁC MINH].</summary>
public enum DayCountBasis { Actual365 = 1, Actual360 = 2 }

/// <summary>
/// Khế ước vay. Drawdowns and repayments are not booked by this module: they are posted journal lines on the principal
/// account for the lender (<see cref="LoanLink"/>), so the loan register reconciles to the ledger by construction.
/// Interest accrues from <see cref="InterestFrom"/> (the start date, or the go-live date for loans taken over with their
/// interest already accrued in the old system).
/// </summary>
public sealed class LoanContract
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid LenderId { get; set; }
    public Guid PrincipalAccountId { get; set; }
    public Guid InterestExpenseAccountId { get; set; }
    public Guid AccruedInterestAccountId { get; set; }
    /// <summary>Loan currency; null = functional currency.</summary>
    public string? Currency { get; set; }
    public decimal ContractAmount { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly MaturityDate { get; set; }
    public DateOnly InterestFrom { get; set; }
    public DayCountBasis DayCount { get; set; } = DayCountBasis.Actual365;
    public LoanStatus Status { get; set; } = LoanStatus.Draft;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public Guid? ActivatedBy { get; set; }
    public long RowVersion { get; set; }
    public List<LoanRate> Rates { get; set; } = [];
}

/// <summary>Annual interest rate (percent) applying from a date until the next one. Append-only.</summary>
public sealed class LoanRate
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid LoanId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public decimal AnnualRatePercent { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

/// <summary>A posted line on the principal account belonging to the loan: credit = drawdown, debit = repayment.</summary>
public sealed class LoanLink
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid LoanId { get; set; }
    public Guid JournalLineId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

public enum LoanRunStatus { Active = 1, Reversed = 2 }

/// <summary>Interest accrual of all active loans for one accounting period, with its GL voucher.</summary>
public sealed class LoanInterestRun
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PeriodId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public LoanRunStatus Status { get; set; } = LoanRunStatus.Active;
    public Guid? VoucherId { get; set; }
    public string RatesJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? ReversedAt { get; set; }
    public Guid? ReversedBy { get; set; }
}

/// <summary>Interest accrued for one loan in one run (functional amount; loan-currency amount for foreign loans). Void-once.</summary>
public sealed class LoanInterestEntry
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid LoanId { get; set; }
    public Guid RunId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal Amount { get; set; }
    public decimal? ForeignAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? VoidedAt { get; set; }
}

/// <summary>
/// Simple daily interest: Σ over each day of principal(day) × rate(day) ÷ 100 ÷ basis days, rounded once for the whole range
/// (MidpointRounding.AwayFromZero). principal(day) includes every movement dated on or before that day, so the drawdown day
/// bears interest and the repayment day does not [CẦN XÁC MINH theo hợp đồng].
/// </summary>
public static class InterestCalculator
{
    public static decimal Accrue(IReadOnlyList<(DateOnly Date, decimal Delta)> principalMoves, IReadOnlyList<(DateOnly From, decimal AnnualPercent)> rates,
        DateOnly from, DateOnly to, DayCountBasis basis, int decimals)
    {
        if (to < from) return 0;
        var moves = principalMoves.OrderBy(m => m.Date).ToList();
        var schedule = rates.OrderBy(r => r.From).ToList();
        decimal principal = moves.Where(m => m.Date < from).Sum(m => m.Delta);
        var mi = moves.FindIndex(m => m.Date >= from);
        if (mi < 0) mi = moves.Count;
        decimal numerator = 0;   // Σ principal × percent (exact)
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            while (mi < moves.Count && moves[mi].Date == d) principal += moves[mi++].Delta;
            if (principal == 0) continue;
            var rate = schedule.LastOrDefault(r => r.From <= d);
            if (rate == default) throw new DomainException(ErrorCodes.ValidationFailed, $"Chưa có lãi suất hiệu lực tại ngày {d:dd/MM/yyyy}.");
            numerator += principal * rate.AnnualPercent;
        }
        var days = basis == DayCountBasis.Actual360 ? 360m : 365m;
        return MoneyMath.Round(numerator / (100m * days), decimals);
    }
}

/// <summary>Một kỳ trả nợ theo lịch của khế ước (bảng debt_schedules): nợ gốc và lãi phải trả ngày đến hạn. Trạng thái Đã trả / Quá hạn suy ra từ số đã trả gốc, không lưu.</summary>
public sealed class LoanScheduleItem
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid LoanId { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal PrincipalDue { get; set; }
    public decimal InterestDue { get; set; }
    public string? Note { get; set; }
}

public sealed record ScheduleDraftItem(DateOnly DueDate, decimal PrincipalDue, decimal InterestDue);

/// <summary>Sinh lịch trả gốc đều theo tháng: lãi kỳ = dư nợ đầu kỳ × lãi suất năm × số ngày ÷ cơ sở ngày (365 hoặc 360), làm tròn từng kỳ; kỳ cuối trả hết phần gốc còn lại.</summary>
public static class LoanScheduleCalculator
{
    public static IReadOnlyList<ScheduleDraftItem> EqualPrincipal(decimal principal, int installments, DateOnly drawdown, DateOnly firstDue, decimal annualRatePercent, DayCountBasis basis, int decimals)
    {
        if (principal <= 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Số tiền vay phải lớn hơn 0.");
        if (installments is < 1 or > 600) throw new DomainException(ErrorCodes.ValidationFailed, "Số kỳ trả từ 1 đến 600.");
        if (annualRatePercent < 0) throw new DomainException(ErrorCodes.ValidationFailed, "Lãi suất không được âm.");
        if (firstDue <= drawdown) throw new DomainException(ErrorCodes.ValidationFailed, "Ngày trả đầu tiên phải sau ngày giải ngân.");
        var days = basis == DayCountBasis.Actual360 ? 360m : 365m;
        var each = Math.Round(principal / installments, decimals, MidpointRounding.AwayFromZero);
        var rows = new List<ScheduleDraftItem>();
        var balance = principal; var previous = drawdown;
        for (var i = 0; i < installments; i++)
        {
            var due = firstDue.AddMonths(i);
            var interest = Math.Round(balance * annualRatePercent / 100m * (due.DayNumber - previous.DayNumber) / days, decimals, MidpointRounding.AwayFromZero);
            var part = i == installments - 1 ? balance : Math.Min(each, balance);
            rows.Add(new ScheduleDraftItem(due, part, interest));
            balance -= part; previous = due;
        }
        return rows;
    }
}
