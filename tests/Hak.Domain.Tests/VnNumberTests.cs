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

public class VnDateTests
{
    [Fact]
    public void Vn_DateOnly_IsDayMonthYear() => Assert.Equal("19/10/2026", new DateOnly(2026, 10, 19).Vn());

    [Fact]
    public void Vn_NullableDateOnly_IsEmptyWhenNull() => Assert.Equal("", ((DateOnly?)null).Vn());
}

public class ExpenseSplitterTests
{
    [Fact]
    public void Split_RemainderGoesToLargestPercent_TotalIsExact()
    {
        var parts = Hak.Domain.Assets.ExpenseSplitter.Split(1_000_000m, [33.3333m, 33.3333m, 33.3334m], 0);
        Assert.Equal([333_333m, 333_333m, 333_334m], parts.Select(p => p.Amount));
        Assert.Equal(1_000_000m, parts.Sum(p => p.Amount));
    }

    [Fact]
    public void Split_TiesGoToFirstLine()
    {
        var parts = Hak.Domain.Assets.ExpenseSplitter.Split(100m, [50m, 25m, 25m], 0);
        Assert.Equal([50m, 25m, 25m], parts.Select(p => p.Amount));
        var odd = Hak.Domain.Assets.ExpenseSplitter.Split(101m, [50m, 50m], 0);   // 50,5 → 51 và 51 làm tròn, chênh −1 dồn vào dòng đầu
        Assert.Equal(101m, odd.Sum(p => p.Amount));
    }

    [Fact]
    public void Split_RequiresTotalOf100() => Assert.Throws<Hak.Domain.Common.DomainException>(() => Hak.Domain.Assets.ExpenseSplitter.Split(100m, [60m, 30m], 0));
}
