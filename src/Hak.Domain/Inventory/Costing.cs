using Hak.Domain.Common;

namespace Hak.Domain.Inventory;

/// <summary>
/// A movement already on the ledger, in processing order. Value is null while not yet costed (monthly average).
/// A value-only adjustment has Quantity 0 and AdjustsSequence = the receipt it revalues (purchase price difference).
/// </summary>
public sealed record CostedMovement(long Sequence, decimal Quantity, decimal? Value, long? AdjustsSequence = null);

/// <summary>
/// Pure issue-costing calculators (no I/O). They replay the warehouse/item history in processing order,
/// so the result is deterministic and can be re-derived for audit. Rounding: functional-currency decimals,
/// MidpointRounding.AwayFromZero; issuing the whole remaining quantity always takes the whole remaining value,
/// so no rounding residue is ever left in stock.
/// </summary>
public static class CostingCalculator
{
    public sealed record IssueCost(decimal Value, decimal QuantityOnHandBefore, decimal ValueOnHandBefore);

    public static IssueCost Fifo(IEnumerable<CostedMovement> history, decimal issueQty, int decimals)
    {
        RequirePositive(issueQty);
        var layers = BuildFifoLayers(history, decimals);
        var onHandQty = layers.Sum(l => l.Qty);
        var onHandValue = layers.Sum(l => l.Value);
        EnsureAvailable(onHandQty, issueQty);
        return new IssueCost(ConsumeFifo(layers, issueQty, decimals), onHandQty, onHandValue);
    }

    public static IssueCost MovingAverage(IEnumerable<CostedMovement> history, decimal issueQty, int decimals)
    {
        RequirePositive(issueQty);
        decimal qty = 0, value = 0;
        foreach (var m in history)
        {
            RequireValued(m);
            qty += m.Quantity;
            value += m.Value!.Value;
        }
        EnsureAvailable(qty, issueQty);
        return new IssueCost(AverageShare(value, qty, issueQty, decimals), qty, value);
    }

    /// <summary>
    /// Monthly weighted average: unit cost = (opening value + period receipt value) / (opening qty + period receipt qty).
    /// Closing value is rounded first and the issues share the rest, distributed by largest remainder, so
    /// opening + receipts − issues = closing holds exactly.
    /// </summary>
    public static IReadOnlyList<decimal> MonthlyAverage(decimal openingQty, decimal openingValue, decimal receiptQty, decimal receiptValue,
        IReadOnlyList<decimal> issueQuantities, int decimals)
    {
        var totalQty = openingQty + receiptQty;
        var totalValue = openingValue + receiptValue;
        var issued = issueQuantities.Sum();
        if (issueQuantities.Any(q => q <= 0)) throw new DomainException(ErrorCodes.ValidationFailed, "Số lượng xuất phải lớn hơn 0.");
        EnsureAvailable(totalQty, issued);
        if (issueQuantities.Count == 0) return [];

        var closingQty = totalQty - issued;
        var closingValue = closingQty == 0 ? 0 : MoneyMath.Round(totalValue * closingQty / totalQty, decimals);
        var issueTotal = totalValue - closingValue;

        // Largest-remainder distribution of issueTotal in units of 10^-decimals.
        var unit = 1m / (decimal)Math.Pow(10, decimals);
        var raw = issueQuantities.Select(q => issueTotal * q / issued).ToList();
        var floors = raw.Select(r => Math.Floor(r / unit) * unit).ToList();
        var leftoverUnits = (int)Math.Round((issueTotal - floors.Sum()) / unit);
        foreach (var i in raw.Select((r, i) => (Frac: r - floors[i], i)).OrderByDescending(x => x.Frac).ThenBy(x => x.i).Take(leftoverUnits).Select(x => x.i))
            floors[i] += unit;
        return floors;
    }

    /// <summary>Quantity of a receipt layer still in stock under FIFO (used to split a purchase price difference).</summary>
    public static decimal FifoRemaining(IEnumerable<CostedMovement> history, long receiptSequence, int decimals)
        => BuildFifoLayers(history, decimals).Where(l => l.Seq == receiptSequence).Sum(l => l.Qty);

    private static LinkedList<(long Seq, decimal Qty, decimal Value)> BuildFifoLayers(IEnumerable<CostedMovement> history, int decimals)
    {
        var layers = new LinkedList<(long Seq, decimal Qty, decimal Value)>();
        foreach (var m in history)
        {
            RequireValued(m);
            if (m.Quantity > 0) layers.AddLast((m.Sequence, m.Quantity, m.Value!.Value));
            else if (m.Quantity < 0) ConsumeFifo(layers, -m.Quantity, decimals);
            else
            {
                // Value-only adjustment: lands on the remaining part of its receipt layer.
                var node = layers.First;
                while (node is not null && node.Value.Seq != m.AdjustsSequence) node = node.Next;
                if (node is null || node.Value.Qty == 0)
                    throw new DomainException(ErrorCodes.ValidationFailed, "Điều chỉnh giá trị gắn với lô nhập đã xuất hết; phần này phải vào giá vốn.");
                node.Value = (node.Value.Seq, node.Value.Qty, node.Value.Value + m.Value!.Value);
            }
        }
        return layers;
    }

    private static decimal ConsumeFifo(LinkedList<(long Seq, decimal Qty, decimal Value)> layers, decimal qty, int decimals)
    {
        decimal cost = 0;
        while (qty > 0)
        {
            var first = layers.First ?? throw new DomainException(ErrorCodes.InsufficientStock, "Lịch sử kho bị xuất âm.");
            var (seq, lq, lv) = first.Value;
            if (lq <= qty)
            {
                cost += lv;
                qty -= lq;
                layers.RemoveFirst();
            }
            else
            {
                var part = MoneyMath.Round(lv * qty / lq, decimals);
                cost += part;
                first.Value = (seq, lq - qty, lv - part);
                qty = 0;
            }
        }
        return cost;
    }

    private static decimal AverageShare(decimal value, decimal qty, decimal issueQty, int decimals)
        => issueQty == qty ? value : MoneyMath.Round(value * issueQty / qty, decimals);

    private static void EnsureAvailable(decimal onHand, decimal needed)
    {
        if (needed > onHand)
            throw new DomainException(ErrorCodes.InsufficientStock, $"Không đủ tồn kho: cần {needed}, hiện có {onHand}.");
    }

    private static void RequirePositive(decimal q)
    {
        if (q <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Số lượng xuất phải lớn hơn 0.");
    }

    private static void RequireValued(CostedMovement m)
    {
        if (m.Value is null)
            throw new DomainException(ErrorCodes.CostingPending, "Có nghiệp vụ kho chưa được tính giá; chạy tính giá các kỳ trước.");
    }
}
