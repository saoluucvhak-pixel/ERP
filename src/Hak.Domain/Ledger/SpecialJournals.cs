using Hak.Domain.Vouchers;

namespace Hak.Domain.Ledger;

/// <summary>
/// Sổ nhật ký đặc biệt (hình thức Nhật ký chung có các sổ nhật ký đặc biệt). Thứ tự giá trị là thứ tự ưu tiên khi một bút toán thuộc nhiều sổ:
/// thu tiền → chi tiền → bán hàng → mua hàng (chị chốt 20/10/2026: ưu tiên tiền mặt); bút toán không thuộc sổ đặc biệt nào ghi ở Nhật ký chung.
/// </summary>
public enum SpecialJournal { CashReceipts = 1, CashPayments = 2, Sales = 3, Purchases = 4, General = 9 }

/// <summary>Các nhóm tài khoản dùng để nhận biết bút toán thuộc sổ nào (lấy từ danh mục và vai trò TK của công ty, không cố định số hiệu).</summary>
public sealed record SpecialJournalContext(IReadOnlySet<Guid> Cash, IReadOnlySet<Guid> Inventory, IReadOnlySet<Guid> Revenue, IReadOnlySet<Guid> PayableLike);

/// <summary>
/// Bút toán trùng (Nguyễn Mạnh Toàn, Huỳnh Thị Hồng Hạnh, Tạp chí Kế toán 87/2010): một bút toán thuộc nhiều sổ nhật ký đặc biệt — ví dụ rút tiền gửi nhập quỹ (vừa là thu tiền vừa là chi tiền),
/// hoàn ứng mua vật tư kèm hoàn tiền (vừa là thu tiền vừa là mua hàng) — nếu mỗi sổ cộng cả bút toán thì Nhật ký chung dồn từ các sổ bị cộng hai lần. Cách xử lý: mỗi bút toán chỉ có MỘT sổ chính
/// (theo thứ tự ưu tiên của <see cref="SpecialJournal"/>), được tính vào tổng cộng của sổ đó; ở các sổ còn lại bút toán vẫn hiện để tra cứu nhưng đánh dấu "trùng" và không cộng.
/// Tổng các sổ sau khi loại trùng luôn bằng tổng Nhật ký.
/// </summary>
public static class SpecialJournals
{
    public static IReadOnlyList<SpecialJournal> Touched(string voucherType, IReadOnlyCollection<(Guid AccountId, Side Side)> lines, SpecialJournalContext c)
    {
        // Số dư đầu kỳ và kết chuyển không phải nghiệp vụ phát sinh trong sổ đặc biệt.
        if (voucherType is VoucherTypes.OpeningBalance or VoucherTypes.Closing) return [];
        var touched = new List<SpecialJournal>();
        bool Any(Side side, IReadOnlySet<Guid> set) => lines.Any(l => l.Side == side && set.Contains(l.AccountId));
        if (Any(Side.Debit, c.Cash)) touched.Add(SpecialJournal.CashReceipts);
        if (Any(Side.Credit, c.Cash)) touched.Add(SpecialJournal.CashPayments);
        if (voucherType == VoucherTypes.SalesInvoice || Any(Side.Credit, c.Revenue)) touched.Add(SpecialJournal.Sales);
        if (voucherType == VoucherTypes.PurchaseInvoice || (Any(Side.Debit, c.Inventory) && (Any(Side.Credit, c.PayableLike) || Any(Side.Credit, c.Cash)))) touched.Add(SpecialJournal.Purchases);
        return touched;
    }

    /// <summary>Sổ chính của bút toán: sổ ưu tiên cao nhất trong các sổ nó thuộc; không thuộc sổ nào thì Nhật ký chung.</summary>
    public static SpecialJournal Main(IReadOnlyList<SpecialJournal> touched) => touched.Count == 0 ? SpecialJournal.General : touched.Min();

    public static string Name(SpecialJournal j) => j switch
    {
        SpecialJournal.Sales => "Nhật ký bán hàng",
        SpecialJournal.Purchases => "Nhật ký mua hàng",
        SpecialJournal.CashReceipts => "Nhật ký thu tiền",
        SpecialJournal.CashPayments => "Nhật ký chi tiền",
        _ => "Nhật ký chung (nghiệp vụ khác)",
    };
}
