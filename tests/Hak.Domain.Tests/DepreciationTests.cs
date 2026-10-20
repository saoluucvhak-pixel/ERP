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

    [Theory]
    [InlineData(AllocationFrequency.Monthly, 2026, 2, false, true)]
    [InlineData(AllocationFrequency.Quarterly, 2026, 2, false, false)]
    [InlineData(AllocationFrequency.Quarterly, 2026, 3, false, true)]
    [InlineData(AllocationFrequency.Quarterly, 2026, 5, true, true)]      // kế hoạch hoàn tất giữa quý: ghi nốt
    [InlineData(AllocationFrequency.Yearly, 2026, 11, false, false)]
    [InlineData(AllocationFrequency.Yearly, 2026, 12, false, true)]
    [InlineData(AllocationFrequency.Yearly, 2026, 6, true, true)]
    public void IsBookingPeriod_FollowsFrequencyAndFiscalYear(AllocationFrequency f, int year, int month, bool complete, bool expected)
        => Assert.Equal(expected, DepreciationCalculator.IsBookingPeriod(f, new(2026, 1, 1), new(2026, 12, 31), new DateOnly(year, month, DateTime.DaysInMonth(year, month)), complete));

    [Fact]
    public void IsBookingPeriod_UsesFiscalYearStart_NotCalendar()
    {
        // Năm tài chính 01/02/2026 – 31/01/2027: quý kết thúc cuối tháng 4, 7, 10, 1 (không phải 3, 6, 9, 12).
        var s = new DateOnly(2026, 2, 1); var e = new DateOnly(2027, 1, 31);
        Assert.True(DepreciationCalculator.IsBookingPeriod(AllocationFrequency.Quarterly, s, e, new DateOnly(2026, 4, 30), false));
        Assert.False(DepreciationCalculator.IsBookingPeriod(AllocationFrequency.Quarterly, s, e, new DateOnly(2026, 3, 31), false));
        Assert.False(DepreciationCalculator.IsBookingPeriod(AllocationFrequency.Quarterly, s, e, new DateOnly(2026, 12, 31), false));
        Assert.True(DepreciationCalculator.IsBookingPeriod(AllocationFrequency.Yearly, s, e, new DateOnly(2027, 1, 31), false));
        Assert.False(DepreciationCalculator.IsBookingPeriod(AllocationFrequency.Yearly, s, e, new DateOnly(2026, 12, 31), false));
    }

    // ---------------------------------------------------------------- các phương pháp khác

    [Fact]
    public void Declining_FiveYears_Factor2_SwitchesToStraightLineWhenBetter()
    {
        var years = DepreciationCalculator.DecliningYears(100_000_000m, 60, 2.0m);
        Assert.Equal([40_000_000m, 24_000_000m, 14_400_000m, 10_800_000m, 10_800_000m], years.Select(y => y.Amount));
        Assert.Equal(100_000_000m, years.Sum(y => y.Amount));
        var start = new DateOnly(2026, 1, 1);
        decimal T(DateOnly through) => DepreciationCalculator.CumulativeTargetDeclining(100_000_000m, 60, start, through, ProrationPolicy.FullMonth, 2.0m, 0);
        Assert.Equal(20_000_000m, T(new(2026, 6, 30)));          // nửa năm đầu
        Assert.Equal(40_000_000m, T(new(2026, 12, 31)));
        Assert.Equal(64_000_000m, T(new(2027, 12, 31)));
        Assert.Equal(78_400_000m, T(new(2028, 12, 31)));
        Assert.Equal(89_200_000m, T(new(2029, 12, 31)));
        Assert.Equal(100_000_000m, T(new(2030, 12, 31)));
        Assert.Equal(100_000_000m, T(new(2035, 1, 31)));
        Assert.Equal(0m, T(new(2025, 12, 31)));
    }

    [Fact]
    public void Declining_PartialLastYear_SumsExactlyToBase()
    {
        var years = DepreciationCalculator.DecliningYears(90_000_000m, 30, 1.5m);
        Assert.Equal(6, years[^1].Months);
        Assert.Equal(90_000_000m, years.Sum(y => y.Amount));
    }

    [Theory]
    [InlineData(36, 1.5)]
    [InlineData(48, 1.5)]
    [InlineData(60, 2.0)]
    [InlineData(72, 2.0)]
    [InlineData(73, 2.5)]
    public void Declining_DefaultFactorByLife(int months, double expected) => Assert.Equal((decimal)expected, DepreciationCalculator.DefaultDecliningFactor(months));

    [Fact]
    public void TimeBased_AllocatesByExactDays()
    {
        var (s, e) = (new DateOnly(2026, 3, 15), new DateOnly(2027, 3, 14));   // 365 ngày
        Assert.Equal(1_700_000m, DepreciationCalculator.CumulativeTargetByTime(36_500_000m, s, e, new(2026, 3, 31), 0));   // 17 ngày
        Assert.Equal(4_700_000m, DepreciationCalculator.CumulativeTargetByTime(36_500_000m, s, e, new(2026, 4, 30), 0));   // 47 ngày
        Assert.Equal(36_500_000m, DepreciationCalculator.CumulativeTargetByTime(36_500_000m, s, e, new(2027, 3, 14), 0));
        Assert.Equal(36_500_000m, DepreciationCalculator.CumulativeTargetByTime(36_500_000m, s, e, new(2028, 1, 1), 0));
        Assert.Equal(0m, DepreciationCalculator.CumulativeTargetByTime(36_500_000m, s, e, new(2026, 3, 14), 0));
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => DepreciationCalculator.CumulativeTargetByTime(1, e, s, e, 0)).Code);
    }

    [Fact]
    public void UnitsOfProduction_FollowsActualOutput()
    {
        Assert.Equal(2_500_000m, DepreciationCalculator.CumulativeTargetByUnits(10_000_000m, 1_000m, 250m, 0));
        Assert.Equal(0m, DepreciationCalculator.CumulativeTargetByUnits(10_000_000m, 1_000m, 0m, 0));
        Assert.Equal(10_000_000m, DepreciationCalculator.CumulativeTargetByUnits(10_000_000m, 1_000m, 1_000m, 0));
        Assert.Equal(10_000_000m, DepreciationCalculator.CumulativeTargetByUnits(10_000_000m, 1_000m, 1_200m, 0));
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => DepreciationCalculator.CumulativeTargetByUnits(1, 0, 1, 0)).Code);
    }

    [Fact]
    public void Target_DispatchesByMethod_AndLastDayFollowsMethod()
    {
        var a = new LongTermAsset { Code = "T1", OriginalCost = 36_500_000m, Method = DepreciationMethod.TimeBased, ScheduleStartDate = new(2026, 3, 15), ScheduleEndDate = new(2027, 3, 14) };
        Assert.Equal(1_700_000m, DepreciationCalculator.Target(a, new(2026, 3, 31), 0));
        Assert.Equal(new DateOnly(2026, 5, 19), DepreciationCalculator.LastDay(a, new(2026, 5, 20)));
        a.Method = DepreciationMethod.UnitsOfProduction; a.TotalUnits = 100;
        Assert.Equal(7_300_000m, DepreciationCalculator.Target(a, new(2026, 12, 31), 0, 20m));
        Assert.Equal(new DateOnly(2026, 4, 30), DepreciationCalculator.LastDay(a, new(2026, 5, 20)));
        a.Method = DepreciationMethod.DecliningBalance; a.UsefulLifeMonths = 60; a.ScheduleStartDate = new(2026, 1, 1); a.OriginalCost = 100_000_000m;
        Assert.Equal(40_000_000m, DepreciationCalculator.Target(a, new(2026, 12, 31), 0));
        a.Method = DepreciationMethod.StraightLine; a.Proration = ProrationPolicy.FullMonth;
        Assert.Equal(new DateOnly(2026, 4, 30), DepreciationCalculator.LastDay(a, new(2026, 5, 20)));
    }
}
