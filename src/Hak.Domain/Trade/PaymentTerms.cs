using System.Globalization;
using Hak.Domain.Common;

namespace Hak.Domain.Trade;

/// <summary>Một bậc chiết khấu thanh toán: giảm <see cref="Percent"/>% nếu tiền về trong <see cref="Days"/> ngày kể từ ngày hóa đơn.</summary>
public sealed record PaymentDiscountTier(decimal Percent, int Days);

/// <summary>
/// Điều khoản chiết khấu thanh toán đầy đủ: bậc chính (P%, N ngày) + các bậc phụ, cơ sở tính (chưa thuế / gồm thuế) và dung sai ngày. Bậc chính lưu ở cột riêng của hóa đơn / đối tượng;
/// phần mở rộng lưu ở một chuỗi tùy chọn, ví dụ <c>base=incl;grace=1;tier=3:5</c> (tier = tỷ lệ:số ngày, có thể lặp). Chuỗi được SAO vào hóa đơn lúc lập (snapshot), hóa đơn đã ghi sổ không đổi theo danh mục.
/// </summary>
public sealed record PaymentTerms(IReadOnlyList<PaymentDiscountTier> Tiers, bool IncludesVat, int GraceDays)
{
    public static PaymentTerms From(decimal? percent, int? days, string? options)
    {
        var tiers = new List<PaymentDiscountTier>();
        if (percent is { } p && days is { } d) tiers.Add(new(p, d));
        var includesVat = false; var grace = 0;
        foreach (var part in (options ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2) throw new DomainException(ErrorCodes.ValidationFailed, $"Tùy chọn chiết khấu thanh toán không hợp lệ: '{part}'.");
            switch (kv[0])
            {
                case "base": includesVat = kv[1] switch { "incl" => true, "excl" => false, _ => throw new DomainException(ErrorCodes.ValidationFailed, "base phải là excl (chưa thuế) hoặc incl (gồm thuế).") }; break;
                case "grace":
                    if (!int.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out grace) || grace > 10) throw new DomainException(ErrorCodes.ValidationFailed, "Dung sai ngày từ 0 đến 10.");
                    break;
                case "tier":
                    var t = kv[1].Split(':');
                    if (t.Length != 2 || !decimal.TryParse(t[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var tp) || !int.TryParse(t[1], NumberStyles.None, CultureInfo.InvariantCulture, out var td))
                        throw new DomainException(ErrorCodes.ValidationFailed, $"Bậc chiết khấu '{kv[1]}' phải dạng tỷ lệ:số ngày.");
                    tiers.Add(new(tp, td)); break;
                default: throw new DomainException(ErrorCodes.ValidationFailed, $"Tùy chọn chiết khấu thanh toán không biết: '{kv[0]}'.");
            }
        }
        var ordered = tiers.OrderBy(t => t.Days).ToList();
        foreach (var t in ordered)
            if (t.Percent <= 0 || t.Percent > 100 || t.Days < 1 || t.Days > 365) throw new DomainException(ErrorCodes.ValidationFailed, "Bậc chiết khấu thanh toán: tỷ lệ lớn hơn 0 đến 100%, số ngày từ 1 đến 365.");
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Days == ordered[i - 1].Days) throw new DomainException(ErrorCodes.ValidationFailed, "Hai bậc chiết khấu thanh toán không được cùng số ngày.");
            if (ordered[i].Percent >= ordered[i - 1].Percent) throw new DomainException(ErrorCodes.ValidationFailed, "Bậc có hạn dài hơn phải có tỷ lệ thấp hơn bậc ngắn hơn (vd 3/5, 2/10).");
        }
        if (ordered.Count == 0 && (includesVat || grace > 0)) throw new DomainException(ErrorCodes.ValidationFailed, "Cơ sở tính / dung sai chỉ có nghĩa khi có điều khoản chiết khấu (tỷ lệ và số ngày).");
        return new PaymentTerms(ordered, includesVat, grace);
    }

    public bool HasTerms => Tiers.Count > 0;

    /// <summary>Bậc áp dụng cho ngày tiền về: bậc có tỷ lệ cao nhất mà hạn (ngày hóa đơn + số ngày + dung sai) chưa qua; null nếu đã quá mọi hạn.</summary>
    public (PaymentDiscountTier Tier, DateOnly Deadline)? Applicable(DateOnly invoiceDate, DateOnly payDate)
    {
        foreach (var t in Tiers.OrderByDescending(t => t.Percent))
        {
            var deadline = invoiceDate.AddDays(t.Days + GraceDays);
            if (payDate <= deadline) return (t, deadline);
        }
        return null;
    }

    /// <summary>Bậc cuối cùng (hạn dài nhất) — dùng hiển thị khi đã quá hạn.</summary>
    public (PaymentDiscountTier Tier, DateOnly Deadline) Last(DateOnly invoiceDate)
    {
        var t = Tiers.OrderBy(x => x.Days).Last();
        return (t, invoiceDate.AddDays(t.Days + GraceDays));
    }

    public string Describe() => string.Join(", ", Tiers.OrderBy(t => t.Days).Select(t => $"{t.Percent.ToString("0.####", CultureInfo.InvariantCulture)}/{t.Days}"))
        + (IncludesVat ? " (cơ sở gồm thuế)" : "") + (GraceDays > 0 ? $" (+{GraceDays} ngày dung sai)" : "");
}
