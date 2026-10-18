using Hak.Domain.Common;
using Hak.Domain.Inventory;

namespace Hak.Domain.Tests;

public class DrynessTests
{
    private static readonly DrynessMethod Method = new() { Code = "DO-KHO", DrynessDecimals = 2, BdmtDecimals = 3 };

    [Theory]
    [InlineData("10", "52.35", "5.235")]          // 10 MT × 52.35% = 5.235 BDMT
    [InlineData("33.333", "50", "16.667")]         // 16.6665 → 16.667 (half away from zero)
    [InlineData("1000", "100", "1000")]
    [InlineData("12.5", "48.4", "6.05")]
    public void Bdmt_IsMtTimesDryness(string mt, string dryness, string expected)
        => Assert.Equal(decimal.Parse(expected), DrynessCalculator.Bdmt(decimal.Parse(mt), decimal.Parse(dryness), Method));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100.01")]
    [InlineData("50.123")] // more decimals than the method allows
    public void InvalidDryness_IsRejected(string dryness)
        => Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => DrynessCalculator.Bdmt(10, decimal.Parse(dryness), Method)).Code);

    [Fact]
    public void NonPositiveMt_IsRejected()
        => Assert.Throws<DomainException>(() => DrynessCalculator.Bdmt(0, 50, Method));
}
