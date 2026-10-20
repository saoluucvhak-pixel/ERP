using Hak.Data;
using Hak.Domain.Common;
using Hak.Domain.Organization;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Common;

/// <summary>Một loại chứng từ có số thứ tự: khóa bộ đếm (không đổi), tiền tố mặc định, tên, chu trình.</summary>
public sealed record DocKind(string Key, string Prefix, string Name, string Cycle);

/// <summary>
/// Danh mục MỌI loại chứng từ có số, theo quy tắc ở docs/11-QUY-TAC-MA.md: số có dạng <c>{TIỀN TỐ}{NĂM TÀI CHÍNH}-{SỐ THỨ TỰ 6 chữ số}</c> (ví dụ HDB2026-000123),
/// tiền tố chữ hoa không dấu 2–5 ký tự là chữ cái đầu tên tiếng Việt của chứng từ, mỗi loại một tiền tố và không tiền tố nào là phần đầu của tiền tố khác.
/// Loại mới phải khai báo ở đây (test giữ quy tắc); công ty đổi tiền tố / độ rộng bằng <see cref="NumberingRule"/> mà không làm đứt số thứ tự.
/// </summary>
public static class DocumentKinds
{
    public const int DefaultWidth = 6;

    private static DocKind V(string type, string name, string cycle) => new(type, type, name, cycle);

    public static readonly IReadOnlyList<DocKind> All =
    [
        // Kế toán tổng hợp — loại chứng từ ghi sổ (tiền tố theo mã quốc tế đã dùng từ đầu, giữ nguyên)
        V(VoucherTypes.General, "Chứng từ ghi sổ chung", "Kế toán tổng hợp"), V(VoucherTypes.Closing, "Bút toán kết chuyển", "Kế toán tổng hợp"),
        V(VoucherTypes.Allocation, "Bút toán phân bổ", "Kế toán tổng hợp"), V(VoucherTypes.Adjustment, "Bút toán điều chỉnh", "Kế toán tổng hợp"),
        V(VoucherTypes.Reversal, "Bút toán đảo", "Kế toán tổng hợp"), V(VoucherTypes.Revaluation, "Đánh giá lại ngoại tệ", "Kế toán tổng hợp"),
        V(VoucherTypes.OpeningBalance, "Số dư đầu kỳ", "Kế toán tổng hợp"),
        // Tiền
        V(VoucherTypes.CashReceipt, "Phiếu thu", "Tiền"), V(VoucherTypes.CashPayment, "Phiếu chi", "Tiền"),
        V(VoucherTypes.BankReceipt, "Giấy báo Có", "Tiền"), V(VoucherTypes.BankPayment, "Giấy báo Nợ", "Tiền"),
        new("UNC", "UNC", "Ủy nhiệm chi (lệnh thanh toán)", "Tiền"), new("TU", "TU", "Đề nghị tạm ứng", "Tiền"), new("KKQ", "KKQ", "Biên bản kiểm kê quỹ", "Tiền"),
        V(VoucherTypes.LoanInterest, "Trích lãi vay", "Tiền"),
        new("CTC", "CTC", "Phương án chia cổ tức, lợi nhuận", "Tiền"),
        // Mua hàng, bán hàng
        new("ORD-DMH", "DMH", "Đơn mua hàng", "Mua hàng"), new("ORD-DBH", "DBH", "Đơn bán hàng", "Bán hàng"),
        new("CTR-PUR", "HDKM", "Hợp đồng mua", "Mua hàng"), new("CTR-SAL", "HDKB", "Hợp đồng bán", "Bán hàng"),
        new("INV-HDM", "HDM", "Hóa đơn mua", "Mua hàng"), new("INV-HMTL", "HMTL", "Hóa đơn mua trả lại", "Mua hàng"),
        new("INV-HMGG", "HMGG", "Hóa đơn mua giảm giá", "Mua hàng"), new("INV-HMDT", "HMDT", "Hóa đơn mua điều chỉnh tăng", "Mua hàng"),
        new("INV-HDB", "HDB", "Hóa đơn bán", "Bán hàng"), new("INV-HDTL", "HDTL", "Hóa đơn bán trả lại", "Bán hàng"),
        new("INV-HDGG", "HDGG", "Hóa đơn bán giảm giá", "Bán hàng"), new("INV-HDDT", "HDDT", "Hóa đơn bán điều chỉnh tăng", "Bán hàng"),
        new("LBH", "LBH", "Lệnh bán hàng", "Bán hàng"), new("PGH", "PGH", "Phiếu giao hàng", "Bán hàng"),
        new("YCTH", "YCTH", "Yêu cầu trả hàng bán", "Bán hàng"), new("YCGG", "YCGG", "Yêu cầu giảm giá hàng bán", "Bán hàng"), new("XND", "XND", "Phiếu xóa nợ khó đòi", "Bán hàng"),
        V(VoucherTypes.PurchaseInvoice, "Chứng từ ghi sổ hóa đơn mua", "Mua hàng"), V(VoucherTypes.SalesInvoice, "Chứng từ ghi sổ hóa đơn bán", "Bán hàng"),
        new("BBKN", "BBKN", "Biên bản kiểm nghiệm", "Mua hàng"),
        // Kho
        new("STK-PN", "PN", "Phiếu nhập kho", "Kho"), new("STK-PX", "PX", "Phiếu xuất kho", "Kho"), new("STK-DC", "DC", "Phiếu điều chuyển kho", "Kho"), new("STK-DP", "DP", "Phiếu đảo (kho)", "Kho"),
        V(VoucherTypes.InventoryReceipt, "Chứng từ ghi sổ nhập kho", "Kho"), V(VoucherTypes.InventoryIssue, "Chứng từ ghi sổ xuất kho", "Kho"), V(VoucherTypes.InventoryCosting, "Chứng từ tính giá xuất kho", "Kho"),
        // Sản xuất
        new("PROD-LSX", "LSX", "Lệnh sản xuất", "Sản xuất"),
        // Tài sản
        new("AST-DNMS", "DNMS", "Đề nghị mua sắm tài sản", "Tài sản"), new("AST-KKTS", "KKTS", "Phiếu kiểm kê tài sản", "Tài sản"),
        new("AST-YCSC", "YCSC", "Yêu cầu sửa chữa / báo sự cố", "Tài sản"), new("AST-SCTS", "SCTS", "Phiếu thi công sửa chữa", "Tài sản"),
        V(VoucherTypes.Depreciation, "Khấu hao, phân bổ", "Tài sản"), V(VoucherTypes.AssetDisposal, "Ghi giảm tài sản", "Tài sản"),
    ];

    private static readonly Dictionary<string, DocKind> ByKey = All.ToDictionary(k => k.Key, StringComparer.Ordinal);

    /// <summary>Loại theo khóa; khóa lạ (chưa khai báo) dùng chính khóa làm tiền tố để không làm hỏng việc cấp số.</summary>
    public static DocKind Find(string key) => ByKey.TryGetValue(key, out var k) ? k : new DocKind(key, key, key, "Khác");

    /// <summary>Lỗi quy tắc của một tiền tố (null nếu hợp lệ): 2–5 chữ in hoa không dấu.</summary>
    public static string? PrefixProblem(string prefix)
        => prefix.Length is < 2 or > 5 || !prefix.All(c => c is >= 'A' and <= 'Z') ? "Tiền tố gồm 2–5 chữ cái in hoa không dấu (A–Z)." : null;
}

/// <summary>Cấp số chứng từ theo danh mục và tùy chỉnh của công ty — một chỗ duy nhất, gọi trong giao dịch của người gọi.</summary>
public static class Numbering
{
    public static async Task<string> NextAsync(HakDbContext db, Guid companyId, DateOnly date, string key)
    {
        var fy = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.CompanyId == companyId && f.StartDate <= date && f.EndDate >= date)
                 ?? throw new DomainException(ErrorCodes.FiscalYearNotFound, $"Không có năm tài chính chứa ngày {date.Vn()}.");
        return await NextAsync(db, companyId, fy, key);
    }

    public static async Task<string> NextAsync(HakDbContext db, Guid companyId, FiscalYear fy, string key)
    {
        var seq = await db.VoucherNumberSequences.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.FiscalYearId == fy.Id && s.VoucherType == key);
        if (seq is null) db.VoucherNumberSequences.Add(seq = new VoucherNumberSequence { Id = Ids.New(), CompanyId = companyId, FiscalYearId = fy.Id, VoucherType = key });
        seq.LastNumber++;
        seq.RowVersion++;
        var rule = await db.NumberingRules.AsNoTracking().FirstOrDefaultAsync(r => r.CompanyId == companyId && r.Key == key);
        return Format(rule?.Prefix ?? DocumentKinds.Find(key).Prefix, fy.Code, seq.LastNumber, rule?.Width ?? DocumentKinds.DefaultWidth);
    }

    public static string Format(string prefix, string fiscalYearCode, long number, int width) => $"{prefix}{fiscalYearCode}-{number.ToString().PadLeft(width, '0')}";
}
