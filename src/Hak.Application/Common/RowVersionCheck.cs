using Hak.Domain.Common;

namespace Hak.Application.Common;

/// <summary>Kiểm phiên bản người dùng đã xem trước khi duyệt / ghi sổ; null = lệnh nội bộ không kiểm (hợp đồng riêng, xem docs về lệnh nội bộ).</summary>
public static class RowVersionCheck
{
    public static void Require(long? expected, long current, string what)
    {
        if (expected is { } rv && rv != current)
            throw new DomainException(ErrorCodes.ConcurrencyConflict, $"{what} đã được thay đổi (phiên bản {current}, bạn đang xem phiên bản {rv}). Vui lòng tải lại.");
    }
}
