using Hak.Domain.Vouchers;

namespace Hak.Domain.Ledger;

/// <summary>
/// Sổ nhật ký đặc biệt (hình thức Nhật ký chung có các sổ nhật ký đặc biệt). Thứ tự giá trị là thứ tự ưu tiên khi một bút toán thuộc nhiều sổ:
/// thu tiền → chi tiền → bán hàng → mua hàng (chị chốt 20/10/2026: ưu tiên tiền mặt); bút toán không thuộc sổ đặc biệt nào ghi ở Nhật ký chung.
/// </summary>
public enum SpecialJournal { CashReceipts = 1, CashPayments = 2, Sales = 3, Purchases = 4, General = 9 }

/// <summary>Các nhóm tài khoản dùng để nhận biết bút toán thuộc sổ nào (lấy từ danh mục và vai trò TK của công ty, không cố định số hiệu).</summary>
public sealed record SpecialJournalContext(IReadOnlySet<Guid> Cash, IReadOnlySet<Guid> Bank, IReadOnlySet<Guid> Inventory, IReadOnlySet<Guid> Revenue, IReadOnlySet<Guid> PayableLike)
{
    public bool IsMoney(Guid account) => Cash.Contains(account) || Bank.Contains(account);
}

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
        bool MoneyIn = lines.Any(l => l.Side == Side.Debit && c.IsMoney(l.AccountId)), MoneyOut = lines.Any(l => l.Side == Side.Credit && c.IsMoney(l.AccountId));
        // Tiền mặt ưu tiên hơn ngân hàng (chị chốt 20/10/2026): nếu bút toán có dòng TK tiền mặt thì chiều thu / chi của TK tiền mặt quyết định sổ chính
        // (rút tiền gửi nhập quỹ: Nợ tiền mặt → thu tiền; nộp tiền mặt vào ngân hàng: Có tiền mặt → chi tiền); dòng ngân hàng chỉ làm bút toán hiện thêm ở sổ còn lại (trùng).
        var cashFirst = new List<SpecialJournal>();
        if (Any(Side.Debit, c.Cash)) cashFirst.Add(SpecialJournal.CashReceipts);
        if (Any(Side.Credit, c.Cash)) cashFirst.Add(SpecialJournal.CashPayments);
        if (MoneyIn && !cashFirst.Contains(SpecialJournal.CashReceipts)) cashFirst.Add(SpecialJournal.CashReceipts);
        if (MoneyOut && !cashFirst.Contains(SpecialJournal.CashPayments)) cashFirst.Add(SpecialJournal.CashPayments);
        touched.AddRange(cashFirst);
        if (voucherType == VoucherTypes.SalesInvoice || Any(Side.Credit, c.Revenue)) touched.Add(SpecialJournal.Sales);
        if (voucherType == VoucherTypes.PurchaseInvoice || (Any(Side.Debit, c.Inventory) && (Any(Side.Credit, c.PayableLike) || MoneyOut))) touched.Add(SpecialJournal.Purchases);
        return touched;
    }

    /// <summary>Sổ chính của bút toán: sổ đầu tiên trong danh sách <see cref="Touched"/> (đã sắp theo ưu tiên); không thuộc sổ nào thì Nhật ký chung.</summary>
    public static SpecialJournal Main(IReadOnlyList<SpecialJournal> touched) => touched.Count == 0 ? SpecialJournal.General : touched[0];

    public static string Name(SpecialJournal j) => j switch
    {
        SpecialJournal.Sales => "Nhật ký bán hàng",
        SpecialJournal.Purchases => "Nhật ký mua hàng",
        SpecialJournal.CashReceipts => "Nhật ký thu tiền",
        SpecialJournal.CashPayments => "Nhật ký chi tiền",
        _ => "Nhật ký chung (nghiệp vụ khác)",
    };
}
