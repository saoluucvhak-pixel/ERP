using Hak.Domain.Common;
using Hak.Domain.Fx;
using Hak.Domain.Vouchers;

namespace Hak.Domain.Tests;

public class FxCalculatorTests
{
    private static readonly Guid Acc = Guid.NewGuid();

    [Fact]
    public void Buckets_SumForeignOnlyFromLinesWithForeignAmount_AndDropEmptyBuckets()
    {
        var b = FxCalculator.Buckets([
            (Acc, null, "USD", Side.Debit, 25_000_000m, 1_000m),
            (Acc, null, "USD", Side.Debit, 300_000m, null),          // revaluation line: currency tag only
            (Acc, null, "USD", Side.Credit, 12_650_000m, 500m),
            (Acc, null, "EUR", Side.Debit, 10m, 1m), (Acc, null, "EUR", Side.Credit, 10m, 1m),
        ]);
        var usd = Assert.Single(b);
        Assert.Equal(500m, usd.ForeignBalance);
        Assert.Equal(12_650_000m, usd.BookBalance);
        Assert.Equal(25_300m, usd.BookRate);
    }

    [Fact]
    public void Revalue_UsesDebitRateForDebitBalances_CreditRateForCreditBalances()
    {
        var rate = new FxRate("USD", 25_300, 25_500);
        Assert.Equal((37_950_000m, 250_000m), FxCalculator.Revalue(new FxBucket(Acc, null, "USD", 1_500, 37_700_000), rate, 0));
        Assert.Equal((-51_000_000m, -800_000m), FxCalculator.Revalue(new FxBucket(Acc, null, "USD", -2_000, -50_200_000), rate, 0));
        Assert.Equal((0m, -300_000m), FxCalculator.Revalue(new FxBucket(Acc, null, "USD", 0, 300_000), rate, 0));
    }

    [Fact]
    public void BookValue_IsProportional_AndTheLastPartTakesTheRemainder()
    {
        Assert.Equal(10_000_000m, FxSettlementMath.BookValue(1_000, 25_000_000, 400, 0));
        Assert.Equal(8_377_778m, FxSettlementMath.BookValue(3, 25_133_333, 1, 0));            // 8,377,777.67 rounded
        Assert.Equal(16_755_555m, FxSettlementMath.BookValue(2, 16_755_555, 2, 0));           // remainder, no residue
        Assert.Equal(ErrorCodes.SettlementExceedsOpen, Assert.Throws<DomainException>(() => FxSettlementMath.BookValue(1, 1, 2, 0)).Code);
    }

    [Fact]
    public void ExactRate_ReproducesTheCarryingAmount()
    {
        foreach (var (f, v) in new[] { (1_000m, 25_000_000m), (3m, 75_400_001m), (5_914.08m, 147_852_000m), (999_999.99m, 25_300_000_123m) })
        {
            var r = FxSettlementMath.ExactRate(f, v, 0);
            Assert.NotNull(r);
            Assert.Equal(v, MoneyMath.Convert(f, r!.Value, 0));
        }
        Assert.Null(FxSettlementMath.ExactRate(0, 1, 0));
    }

    [Fact]
    public void Revalue_RejectsNonPositiveOrOverPreciseRates()
    {
        Assert.Equal(ErrorCodes.AmountNotPositive, Assert.Throws<DomainException>(() =>
            FxCalculator.Revalue(new FxBucket(Acc, null, "USD", 1, 1), new FxRate("USD", 0, 1), 0)).Code);
        Assert.Equal(ErrorCodes.AmountPrecision, Assert.Throws<DomainException>(() =>
            FxCalculator.Revalue(new FxBucket(Acc, null, "USD", 1, 1), new FxRate("USD", 1.0000001m, 1), 0)).Code);
    }
}
