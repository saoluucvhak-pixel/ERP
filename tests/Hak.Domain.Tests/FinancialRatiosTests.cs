using Hak.Domain.Reports;

namespace Hak.Domain.Tests;

public class FinancialRatiosTests
{
    private static Func<string, decimal?> Data(params (string, decimal)[] v)
    {
        var map = v.ToDictionary(x => x.Item1, x => (decimal?)x.Item2);
        return c => map.GetValueOrDefault(c);
    }

    [Fact]
    public void Ratios_FromTemplateLineCodes_BlankWhenDenominatorMissing()
    {
        var b01 = Data(("280", 1000), ("300", 600), ("400", 400), ("330", 100), ("100", 500), ("110", 50), ("140", 200), ("130", 150), ("310", 500));
        var b02 = Data(("10", 800), ("11", 600), ("20", 200), ("60", 80), ("50", 100), ("24", 20));
        var none = Data();
        var rows = FinancialRatios.Compute(none, b01, none, b02, 30, 30).ToDictionary(r => r.Name);

        Assert.Equal(60m, rows["Hệ số nợ"].Current);
        Assert.Equal(40m, rows["Hệ số vốn tự có"].Current);
        Assert.Equal(150m, rows["Hệ số nợ trên vốn chủ sở hữu"].Current);
        Assert.Equal(1m, rows["Hệ số khả năng thanh toán hiện hành"].Current);
        Assert.Equal(0.6m, rows["Hệ số khả năng thanh toán nhanh"].Current);
        Assert.Equal(0.1m, rows["Hệ số khả năng thanh toán tức thời"].Current);
        Assert.Equal(6m, rows["Hệ số khả năng thanh toán lãi vay"].Current);       // (100 + 20) / 20
        Assert.Equal(3m, rows["Vòng quay hàng tồn kho"].Current);                   // 600 / 200
        Assert.Equal(10m, rows["Số ngày lưu kho"].Current);                         // 30 / 3
        Assert.Equal(25m, rows["Tỷ suất lợi nhuận gộp trên doanh thu"].Current);
        Assert.Equal(10m, rows["Tỷ suất lợi nhuận sau thuế trên doanh thu (ROS)"].Current);
        Assert.Equal(8m, rows["Tỷ suất lợi nhuận sau thuế trên tổng tài sản (ROA)"].Current);
        Assert.Equal(20m, rows["Tỷ suất lợi nhuận sau thuế trên vốn chủ sở hữu (ROE)"].Current);
        Assert.All(rows.Values, r => Assert.Null(r.Previous));                     // kỳ trước không có số liệu → để trống, không báo 0

        var empty = FinancialRatios.Compute(none, none, none, none, 30, 30);
        Assert.All(empty, r => Assert.Null(r.Current));
    }
}
