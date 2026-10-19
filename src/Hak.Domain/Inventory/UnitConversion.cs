using Hak.Domain.Common;

namespace Hak.Domain.Inventory;

/// <summary>Quy đổi giữa đơn vị chính và đơn vị quy đổi cố định: 1 đơn vị quy đổi = <c>factor</c> đơn vị chính. Làm tròn 4 chữ số thập phân (cùng độ chính xác lưu số lượng).</summary>
public static class UnitConversion
{
    public static decimal ToAlt(decimal main, decimal factor)
    {
        if (factor <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Hệ số quy đổi phải lớn hơn 0.");
        return Math.Round(main / factor, MoneyMath.StorageScale, MidpointRounding.AwayFromZero);
    }

    public static decimal ToMain(decimal alt, decimal factor)
    {
        if (factor <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Hệ số quy đổi phải lớn hơn 0.");
        return Math.Round(alt * factor, MoneyMath.StorageScale, MidpointRounding.AwayFromZero);
    }
}
