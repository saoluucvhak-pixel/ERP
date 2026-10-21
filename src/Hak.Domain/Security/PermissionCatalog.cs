namespace Hak.Domain.Security;

/// <summary>One selectable function (a permission) shown on the role editor.</summary>
public sealed record PermissionInfo(string Code, string Label);

/// <summary>A module (phân hệ) grouping related functions, so an administrator can grant or withdraw a whole module at once.</summary>
public sealed record PermissionModule(string Key, string Label, IReadOnlyList<PermissionInfo> Permissions);

/// <summary>Every permission an administrator may put into a role, grouped by module. Source of truth for validation and the role editor.</summary>
public static class PermissionCatalog
{
    public static readonly IReadOnlyList<PermissionModule> Modules =
    [
        new("general", "Tổng hợp & chứng từ",
        [
            new(Permissions.VoucherView, "Xem chứng từ"),
            new(Permissions.VoucherCreate, "Lập chứng từ"),
            new(Permissions.VoucherEditDraft, "Sửa chứng từ nháp"),
            new(Permissions.VoucherSubmit, "Gửi duyệt"),
            new(Permissions.VoucherApprove, "Duyệt"),
            new(Permissions.VoucherPost, "Ghi sổ"),
            new(Permissions.VoucherReverse, "Đảo chứng từ"),
            new(Permissions.PeriodLock, "Khóa kỳ kế toán"),
            new(Permissions.PeriodReopen, "Mở lại kỳ đã khóa"),
        ]),
        new("cash", "Tiền mặt & Tiền gửi",
        [
            new(Permissions.CashDocument, "Phiếu thu / chi tiền mặt"),
            new(Permissions.BankDocument, "Chứng từ ngân hàng"),
            new(Permissions.PaymentOrderApprove, "Duyệt lệnh chi"),
            new(Permissions.PaymentAccept, "Chấp nhận thanh toán"),
            new(Permissions.PaymentVariance, "Duyệt sai lệch đối chiếu hóa đơn mua"),
            new(Permissions.BankReconcile, "Đối chiếu ngân hàng"),
        ]),
        new("trade", "Mua / Bán hàng & công nợ",
        [
            new(Permissions.InvoiceDocument, "Đơn hàng, hóa đơn mua / bán"),
            new(Permissions.SettlementManage, "Thanh toán, bù trừ công nợ"),
            new(Permissions.CreditOverride, "Vượt hạn mức tín dụng"),
        ]),
        new("stock", "Kho", [new(Permissions.StockDocument, "Phiếu nhập / xuất / điều chuyển kho")]),
        new("assets", "Tài sản, CCDC & Vay",
        [
            new(Permissions.AssetManage, "Tài sản cố định, công cụ dụng cụ"),
            new(Permissions.LoanManage, "Khế ước vay"),
            new(Permissions.ProductionManage, "Sản xuất, giá thành"),
        ]),
        new("reports", "Báo cáo",
        [
            new(Permissions.ReportView, "Xem sổ và báo cáo"),
            new(Permissions.DataExport, "Xuất dữ liệu"),
            new(Permissions.SensitiveView, "Xem thông tin nhạy cảm"),
        ]),
        new("catalog", "Danh mục & Thiết lập",
        [
            new(Permissions.CatalogManage, "Quản lý danh mục"),
            new(Permissions.ConfigManage, "Thiết lập hệ thống"),
            new(Permissions.DataImport, "Nhập dữ liệu"),
        ]),
        new("audit", "Nhật ký kiểm toán", [new(Permissions.AuditView, "Xem nhật ký kiểm toán")]),
    ];

    public static IReadOnlySet<string> AllCodes { get; } = Modules.SelectMany(m => m.Permissions).Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
}
