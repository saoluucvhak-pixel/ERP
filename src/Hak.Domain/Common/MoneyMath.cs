namespace Hak.Domain.Common;

/// <summary>
/// Decimal-only money arithmetic. Storage precision is 4 decimals (docs/02 ADR-05);
/// display/posting precision comes from the currency (VND = 0).
/// </summary>
public static class MoneyMath
{
    public const int StorageScale = 4;
    public const int RateScale = 6;
    public const decimal StorageFactor = 10_000m;

    public static decimal Round(decimal value, int decimals, MidpointRounding mode = MidpointRounding.AwayFromZero)
        => Math.Round(value, decimals, mode);

    /// <summary>True when <paramref name="value"/> has no more than <paramref name="decimals"/> fractional digits.</summary>
    public static bool HasAtMostDecimals(decimal value, int decimals)
        => Math.Round(value, decimals, MidpointRounding.ToZero) == value;

    /// <summary>Functional amount = round(foreign amount × rate, functional currency decimals).</summary>
    public static decimal Convert(decimal foreignAmount, decimal rate, int functionalDecimals,
        MidpointRounding mode = MidpointRounding.AwayFromZero)
    {
        if (!HasAtMostDecimals(rate, RateScale))
            throw new DomainException(ErrorCodes.AmountPrecision, $"Tỷ giá {rate} vượt quá {RateScale} chữ số thập phân.");
        return Round(foreignAmount * rate, functionalDecimals, mode);
    }

    /// <summary>Scaled integer used for SQLite storage. Throws rather than silently truncating.</summary>
    public static long ToStorage(decimal value)
    {
        if (!HasAtMostDecimals(value, StorageScale))
            throw new DomainException(ErrorCodes.AmountPrecision, $"Giá trị {value} vượt quá {StorageScale} chữ số thập phân lưu trữ.");
        return decimal.ToInt64(value * StorageFactor);
    }

    public static decimal FromStorage(long scaled) => scaled / StorageFactor;
}
