using Hak.Domain.Assets;
using Hak.Domain.Common;

namespace Hak.Domain.Tests;

public class DepreciationTests
{
    [Fact]
    public void FullMonth_CountsTheStartMonth_AndStopsExactlyAtBase()
    {
        var start = new DateOnly(2026, 1, 15);
        Assert.Equal(0m, DepreciationCalculator.CumulativeTarget(120_000_000, 60, start, new(2025, 12, 31), ProrationPolicy.FullMonth, 0));
        Assert.Equal(2_000_000m, DepreciationCalculator.CumulativeTarget(120_000_000, 60, start, new(2026, 1, 31), ProrationPolicy.FullMonth, 0));
        Assert.Equal(118_000_000m, DepreciationCalculator.CumulativeTarget(120_000_000, 60, start, new(2030, 11, 30), ProrationPolicy.FullMonth, 0));
        Assert.Equal(120_000_000m, DepreciationCalculator.CumulativeTarget(120_000_000, 60, start, new(2030, 12, 31), ProrationPolicy.FullMonth, 0));
        Assert.Equal(120_000_000m, DepreciationCalculator.CumulativeTarget(120_000_000, 60, start, new(2035, 1, 31), ProrationPolicy.FullMonth, 0));
    }

    [Fact]
    public void Daily_ProratesFirstAndLastMonth_AndEndsTheDayBeforeTheAnniversary()
    {
        var start = new DateOnly(2026, 1, 17);
        Assert.Equal(1_250_000m, DepreciationCalculator.CumulativeTarget(31_000_000, 12, start, new(2026, 1, 31), ProrationPolicy.Daily, 0));
        Assert.Equal(3_833_333m, DepreciationCalculator.CumulativeTarget(31_000_000, 12, start, new(2026, 2, 28), ProrationPolicy.Daily, 0));
        Assert.True(DepreciationCalculator.CumulativeTarget(31_000_000, 12, start, new(2027, 1, 15), ProrationPolicy.Daily, 0) < 31_000_000m);
        Assert.Equal(31_000_000m, DepreciationCalculator.CumulativeTarget(31_000_000, 12, start, new(2027, 1, 16), ProrationPolicy.Daily, 0));
    }

    [Fact]
    public void MonthlyIncrements_SumExactlyToBase_WithoutResidue()
    {
        decimal previous = 0, total = 0;
        for (var m = 0; m < 3; m++)
        {
            var end = new DateOnly(2026, 1, 1).AddMonths(m + 1).AddDays(-1);
            var c = DepreciationCalculator.CumulativeTarget(1_000_000, 3, new(2026, 1, 1), end, ProrationPolicy.FullMonth, 0);
            total += c - previous;
            previous = c;
        }
        Assert.Equal(1_000_000m, total);
    }

    [Fact]
    public void LastDepreciationDay_FollowsPolicy()
    {
        Assert.Equal(new DateOnly(2026, 2, 28), DepreciationCalculator.LastDepreciationDay(new(2026, 3, 20), ProrationPolicy.FullMonth));
        Assert.Equal(new DateOnly(2026, 3, 19), DepreciationCalculator.LastDepreciationDay(new(2026, 3, 20), ProrationPolicy.Daily));
    }

    [Fact]
    public void ZeroOrNegativeLife_IsRejected()
        => Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() =>
            DepreciationCalculator.CumulativeTarget(1, 0, new(2026, 1, 1), new(2026, 1, 31), ProrationPolicy.FullMonth, 0)).Code);
}
