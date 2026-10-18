using Hak.Domain.Common;
using Hak.Domain.Inventory;

namespace Hak.Domain.Tests;

public class CostingTests
{
    private static CostedMovement In(long seq, decimal q, decimal v) => new(seq, q, v);
    private static CostedMovement Out(long seq, decimal q, decimal v) => new(seq, -q, -v);

    [Fact]
    public void Fifo_ConsumesOldestLayersFirst()
    {
        // 10 @ 100 = 1,000 then 10 @ 120 = 1,200; issue 15 → 1,000 + 5/10 × 1,200 = 1,600.
        var r = CostingCalculator.Fifo([In(1, 10, 1000), In(2, 10, 1200)], 15, 0);
        Assert.Equal(1600m, r.Value);
        Assert.Equal((20m, 2200m), (r.QuantityOnHandBefore, r.ValueOnHandBefore));
        // Remaining 5 units carry 600; issuing them takes exactly 600.
        Assert.Equal(600m, CostingCalculator.Fifo([In(1, 10, 1000), In(2, 10, 1200), Out(3, 15, 1600)], 5, 0).Value);
    }

    [Fact]
    public void Fifo_PartialLayerRounding_LeavesNoResidue()
    {
        // 3 units worth 1,000: issue 1 → 333; the last two units carry 667 and are issued at 667 in total.
        var h = new List<CostedMovement> { In(1, 3, 1000) };
        var a = CostingCalculator.Fifo(h, 1, 0).Value;
        h.Add(Out(2, 1, a));
        var b = CostingCalculator.Fifo(h, 2, 0).Value;
        Assert.Equal((333m, 667m), (a, b));
    }

    [Fact]
    public void MovingAverage_RevaluesAfterEachReceipt()
    {
        // 10 @ 1,000 + 10 @ 1,300 → average 115; issue 15 → 1,725; the last 5 → exactly 575.
        var h = new List<CostedMovement> { In(1, 10, 1000), In(2, 10, 1300) };
        var first = CostingCalculator.MovingAverage(h, 15, 0).Value;
        h.Add(Out(3, 15, first));
        Assert.Equal((1725m, 575m), (first, CostingCalculator.MovingAverage(h, 5, 0).Value));
    }

    [Fact]
    public void MovingAverage_RoundingResidueStaysInStockUntilLastIssue()
    {
        var h = new List<CostedMovement> { In(1, 3, 1000) };
        var values = new List<decimal>();
        for (var i = 0; i < 3; i++)
        {
            var v = CostingCalculator.MovingAverage(h, 1, 0).Value;
            values.Add(v);
            h.Add(Out(i + 2, 1, v));
        }
        Assert.Equal([333m, 334m, 333m], values.ToArray()); // 333.33 → 333; 667/2 = 333.5 → 334; last takes 333
        Assert.Equal(1000m, values.Sum());
    }

    [Fact]
    public void MonthlyAverage_IssuesAndClosingAddUpExactly()
    {
        // Opening 0; receipts 10 → 1,000 and 10 → 1,600 (total 20 → 2,600, unit 130); issues 4 and 6.
        var v = CostingCalculator.MonthlyAverage(0, 0, 20, 2600, [4, 6], 0);
        Assert.Equal([520m, 780m], v.ToArray());
    }

    [Fact]
    public void MonthlyAverage_LargestRemainderDistribution()
    {
        // 3 units worth 1,000, three issues of 1: closing 0, so issues share exactly 1,000.
        var v = CostingCalculator.MonthlyAverage(0, 0, 3, 1000, [1, 1, 1], 0);
        Assert.Equal(1000m, v.Sum());
        Assert.All(v, x => Assert.InRange(x, 333m, 334m));
        // With opening stock: 5 units 700 + 5 units 800 = 1,500 / 10; issue 7 → closing 3 → 450, issues 1,050.
        Assert.Equal([1050m], CostingCalculator.MonthlyAverage(5, 700, 5, 800, [7], 0).ToArray());
    }

    [Fact]
    public void NegativeStockAndPendingValues_AreRejected()
    {
        Assert.Equal(ErrorCodes.InsufficientStock, Assert.Throws<DomainException>(() => CostingCalculator.Fifo([In(1, 2, 10)], 3, 0)).Code);
        Assert.Equal(ErrorCodes.InsufficientStock, Assert.Throws<DomainException>(() => CostingCalculator.MovingAverage([], 1, 0)).Code);
        Assert.Equal(ErrorCodes.InsufficientStock, Assert.Throws<DomainException>(() => CostingCalculator.MonthlyAverage(1, 10, 0, 0, [2], 0)).Code);
        Assert.Equal(ErrorCodes.CostingPending, Assert.Throws<DomainException>(() => CostingCalculator.MovingAverage([new CostedMovement(1, 5, null)], 1, 0)).Code);
    }

    [Fact]
    public void FractionalQuantities_AreSupported()
    {
        // Wood chips are measured in tonnes with decimals: 12.5 t worth 25,000,000; issue 2.75 t.
        Assert.Equal(5_500_000m, CostingCalculator.MovingAverage([In(1, 12.5m, 25_000_000)], 2.75m, 0).Value);
    }
}
