using Hak.Domain.Common;
using Hak.Domain.Ledger;

namespace Hak.Domain.Tests;

public class MoneyMathTests
{
    [Theory]
    [InlineData("2.5", 0, "3")]
    [InlineData("-2.5", 0, "-3")]
    [InlineData("1234.565", 2, "1234.57")]
    [InlineData("0.4999", 0, "0")]
    public void Round_AwayFromZero_IsDefault(string value, int dp, string expected)
        => Assert.Equal(decimal.Parse(expected), MoneyMath.Round(decimal.Parse(value), dp));

    [Fact]
    public void Round_ToEven_IsConfigurable()
        => Assert.Equal(2m, MoneyMath.Round(2.5m, 0, MidpointRounding.ToEven));

    [Theory]
    [InlineData("1000", 0, true)]
    [InlineData("1000.5", 0, false)]
    [InlineData("12.34", 2, true)]
    [InlineData("12.345", 2, false)]
    public void HasAtMostDecimals(string value, int dp, bool expected)
        => Assert.Equal(expected, MoneyMath.HasAtMostDecimals(decimal.Parse(value), dp));

    [Fact]
    public void Convert_UsdToVnd_RoundsToFunctionalPrecision()
        // 1,234.56 × 25,345.5 = 31,290,540.48 → 31,290,540 VND
        => Assert.Equal(31_290_540m, MoneyMath.Convert(1234.56m, 25345.5m, 0));

    [Fact]
    public void Convert_RejectsRateWithMoreThanSixDecimals()
        => Assert.Equal(ErrorCodes.AmountPrecision,
            Assert.Throws<DomainException>(() => MoneyMath.Convert(1m, 1.1234567m, 0)).Code);

    [Fact]
    public void Storage_RoundTrips_WithoutFloatingPoint()
    {
        foreach (var v in new[] { 0.0001m, 1m, 99_999_999_999_999.9999m, 123_456_789.1234m })
            Assert.Equal(v, MoneyMath.FromStorage(MoneyMath.ToStorage(v)));
    }

    [Fact]
    public void Storage_RejectsSilentTruncation()
        => Assert.Throws<DomainException>(() => MoneyMath.ToStorage(1.00001m));

    [Fact]
    public void DecimalSum_HasNoBinaryDrift()
    {
        // 0.1 + 0.2 == 0.3 holds for decimal, not for double: the reason FLOAT/DOUBLE are banned (§4.3).
        Assert.Equal(0.3m, 0.1m + 0.2m);
        Assert.NotEqual(0.3, 0.1 + 0.2);
    }

    [Theory]
    [InlineData("100", "50", "30", "120")]
    [InlineData("-100", "0", "50", "-150")]
    [InlineData("0", "0", "0", "0")]
    public void Closing_IsOpeningPlusDebitMinusCredit(string o, string d, string c, string expected)
        => Assert.Equal(decimal.Parse(expected), BalanceMath.Closing(decimal.Parse(o), decimal.Parse(d), decimal.Parse(c)));

    [Fact]
    public void ToColumns_SplitsSignedBalance()
    {
        Assert.Equal((150m, 0m), BalanceMath.ToColumns(150m));
        Assert.Equal((0m, 150m), BalanceMath.ToColumns(-150m));
    }
}
