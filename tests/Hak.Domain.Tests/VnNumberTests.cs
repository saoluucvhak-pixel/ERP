using Hak.Domain.Common;

namespace Hak.Domain.Tests;

public class VnNumberTests
{
    [Theory]
    [InlineData("540000000", 0, "540.000.000")]
    [InlineData("1234567.5", 4, "1.234.567,5")]
    [InlineData("1000.0000", 4, "1.000")]
    [InlineData("5.5555", 2, "5,56")]
    [InlineData("-2500.25", 2, "-2.500,25")]
    [InlineData("0", 0, "0")]
    public void Vn_GroupsWithDotsAndUsesCommaDecimals(string value, int decimals, string expected)
        => Assert.Equal(expected, decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture).Vn(decimals));

    [Fact]
    public void Vn_NullIsEmpty() => Assert.Equal("", ((decimal?)null).Vn());
}
