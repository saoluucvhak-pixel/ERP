using Hak.Domain.Common;
using Hak.Domain.Vouchers;

namespace Hak.Domain.Fx;

public enum FxRunStatus { Active = 1, Reversed = 2 }

/// <summary>
/// Period-end revaluation of foreign-currency monetary balances at one date, with the rates used (evidence) and the
/// FXR voucher produced. Reversed only latest first.
/// </summary>
public sealed class FxRevaluationRun
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public DateOnly RevaluationDate { get; set; }
    public FxRunStatus Status { get; set; } = FxRunStatus.Active;
    public Guid? VoucherId { get; set; }
    /// <summary>Rates applied per currency (JSON), and where they came from (e.g. bank and time of quotation).</summary>
    public string RatesJson { get; set; } = "";
    public string RateSource { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? ReversedAt { get; set; }
    public Guid? ReversedBy { get; set; }
}

/// <summary>
/// Closing rates for one currency. Which rate applies to a debit (asset-like) or credit (liability-like) balance follows
/// the company's policy — [CẦN XÁC MINH căn cứ TT99]; the user enters both, they may be equal.
/// </summary>
public sealed record FxRate(string Currency, decimal DebitBalanceRate, decimal CreditBalanceRate);

/// <summary>Foreign and functional balance of one (account, partner, currency) bucket. Signed: debit positive.</summary>
public sealed record FxBucket(Guid AccountId, Guid? PartnerId, string Currency, decimal ForeignBalance, decimal BookBalance)
{
    /// <summary>Functional amount per unit of foreign currency currently carried in the books (null when the foreign balance is zero).</summary>
    public decimal? BookRate => ForeignBalance == 0 ? null : Math.Round(BookBalance / ForeignBalance, MoneyMath.RateScale, MidpointRounding.AwayFromZero);
}

/// <summary>Carrying amounts of foreign-currency open items (receivables/payables settled or revalued per item).</summary>
public static class FxSettlementMath
{
    /// <summary>
    /// Carrying (book) amount of <paramref name="foreign"/> taken out of an item whose open part is
    /// <paramref name="openForeign"/> / <paramref name="openAmount"/>. Settling the whole foreign remainder takes the whole
    /// functional remainder, so no rounding residue is left on the item.
    /// </summary>
    public static decimal BookValue(decimal openForeign, decimal openAmount, decimal foreign, int functionalDecimals)
    {
        if (foreign <= 0 || foreign > openForeign)
            throw new DomainException(ErrorCodes.SettlementExceedsOpen, $"Số nguyên tệ thanh toán {foreign} vượt số nguyên tệ còn lại {openForeign}.");
        return foreign == openForeign ? openAmount : MoneyMath.Round(openAmount * foreign / openForeign, functionalDecimals);
    }

    /// <summary>
    /// A rate with at most 6 decimals such that round(foreign × rate) = amount, so a line booked at a carrying amount still
    /// satisfies "amount = foreign × rate". Null when no such rate exists (only for very large foreign amounts).
    /// </summary>
    public static decimal? ExactRate(decimal foreign, decimal amount, int functionalDecimals)
    {
        if (foreign <= 0 || amount <= 0) return null;
        var r = Math.Round(amount / foreign, MoneyMath.RateScale, MidpointRounding.AwayFromZero);
        foreach (var candidate in new[] { r, r - 0.000001m, r + 0.000001m })
            if (candidate > 0 && MoneyMath.Convert(foreign, candidate, functionalDecimals) == amount) return candidate;
        return null;
    }
}

public static class FxCalculator
{
    /// <summary>
    /// Revalued functional balance of a bucket at the closing rate, and the adjustment (target − book). A bucket whose foreign
    /// balance is zero but still carries a functional residue (rounding, realised differences) is brought to zero.
    /// </summary>
    public static (decimal Target, decimal Adjustment) Revalue(FxBucket bucket, FxRate rate, int functionalDecimals)
    {
        if (rate.DebitBalanceRate <= 0 || rate.CreditBalanceRate <= 0)
            throw new DomainException(ErrorCodes.AmountNotPositive, $"Tỷ giá {rate.Currency} phải lớn hơn 0.");
        var target = bucket.ForeignBalance switch
        {
            > 0 => MoneyMath.Convert(bucket.ForeignBalance, rate.DebitBalanceRate, functionalDecimals),
            < 0 => -MoneyMath.Convert(-bucket.ForeignBalance, rate.CreditBalanceRate, functionalDecimals),
            _ => 0m,
        };
        return (target, target - bucket.BookBalance);
    }

    /// <summary>
    /// Folds journal lines into buckets. Lines tagged with a currency count toward its functional balance; only lines with a
    /// foreign amount change the foreign balance (revaluation lines carry the currency tag without a foreign amount).
    /// </summary>
    public static List<FxBucket> Buckets(IEnumerable<(Guid AccountId, Guid? PartnerId, string Currency, Side Side, decimal Amount, decimal? ForeignAmount)> lines)
        => lines.GroupBy(l => (l.AccountId, l.PartnerId, l.Currency))
            .Select(g => new FxBucket(g.Key.AccountId, g.Key.PartnerId, g.Key.Currency,
                g.Sum(l => (int)l.Side * (l.ForeignAmount ?? 0)), g.Sum(l => (int)l.Side * l.Amount)))
            .Where(b => b.ForeignBalance != 0 || b.BookBalance != 0)
            .ToList();
}
