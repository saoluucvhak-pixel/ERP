using Hak.Domain.Common;
using Hak.Domain.Loans;

namespace Hak.Domain.Tests;

public class InterestCalculatorTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1), Jan31 = new(2026, 1, 31);

    [Fact]
    public void DrawdownDayBearsInterest_RoundedOncePerRange()
        // 1,000,000,000 from 10/01 (22 days to 31/01) at 10%/365: 6,027,397.26
        => Assert.Equal(6_027_397m, InterestCalculator.Accrue([(new(2026, 1, 10), 1_000_000_000m)], [(Jan1, 10m)], Jan1, Jan31, DayCountBasis.Actual365, 0));

    [Fact]
    public void RateChange_AppliesFromItsDate()
        // 10 days at 10% + 12 days at 12%: 244e9 / 36,500 = 6,684,931.51
        => Assert.Equal(6_684_932m, InterestCalculator.Accrue([(new(2026, 1, 10), 1_000_000_000m)], [(Jan1, 10m), (new(2026, 1, 20), 12m)], Jan1, Jan31, DayCountBasis.Actual365, 0));

    [Fact]
    public void RepaymentDayBearsNoInterestOnTheRepaidPart()
        // 15 days on 1e9, 7 days on 5e8 at 10%: 185e9 / 36,500 = 5,068,493.15
        => Assert.Equal(5_068_493m, InterestCalculator.Accrue([(new(2026, 1, 10), 1_000_000_000m), (new(2026, 1, 25), -500_000_000m)], [(Jan1, 10m)],
            Jan1, Jan31, DayCountBasis.Actual365, 0));

    [Fact]
    public void Actual360_AndForeignDecimals()
    {
        Assert.Equal(6_111_111m, InterestCalculator.Accrue([(new(2026, 1, 10), 1_000_000_000m)], [(Jan1, 10m)], Jan1, Jan31, DayCountBasis.Actual360, 0));
        Assert.Equal(516.67m, InterestCalculator.Accrue([(Jan1, 100_000m)], [(Jan1, 6m)], Jan1, Jan31, DayCountBasis.Actual360, 2));
    }

    [Fact]
    public void OpeningPrincipal_AndMissingRate()
    {
        Assert.Equal(0m, InterestCalculator.Accrue([], [], Jan1, Jan31, DayCountBasis.Actual365, 0));
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() =>
            InterestCalculator.Accrue([(new(2025, 12, 1), 1m)], [(new(2026, 1, 15), 10m)], Jan1, Jan31, DayCountBasis.Actual365, 0)).Code);
    }
}
