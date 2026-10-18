using Hak.Domain.Common;
using Hak.Domain.Security;

namespace Hak.Application.Security;

/// <summary>
/// Who is acting, in which company, with which permissions. Resolved server-side per request;
/// every engine operation takes one and checks it (master prompt §42: backend enforcement).
/// </summary>
public sealed record SessionContext(
    Guid UserId,
    Guid CompanyId,
    IReadOnlySet<string> Permissions,
    Guid? SessionId = null,
    string? Device = null)
{
    public void Require(string permission)
    {
        if (!Permissions.Contains(permission))
            throw new DomainException(ErrorCodes.Forbidden, $"Người dùng không có quyền '{permission}' tại công ty này.");
    }

    public void RequireCompany(Guid companyId)
    {
        if (companyId != CompanyId)
            throw new DomainException(ErrorCodes.Forbidden, "Dữ liệu không thuộc phạm vi công ty của phiên làm việc.");
    }
}

/// <summary>
/// Default roles. Three kinds of people use the software: whoever maintains the master data, whoever prepares and approves
/// documents, and the administrator. Whether preparing and approving may be one person or must be two is a per-company switch
/// (<c>Company.RequireDistinctApprover</c>), not a role split, so the same role works for both set-ups.
/// </summary>
public static class RoleCatalog
{
    public const string Admin = "ADMIN";
    public const string DataKeeper = "DATA_KEEPER";
    public const string Accountant = "ACCOUNTANT";

    private static readonly string[] Viewer = [Permissions.VoucherView, Permissions.ReportView];

    public static readonly IReadOnlyDictionary<string, (string Name, string[] Perms)> Defaults =
        new Dictionary<string, (string, string[])>
        {
            // Settings, users' access to the company, audit trail. No accounting rights implicitly.
            [Admin] = ("Quản trị", [Permissions.ConfigManage, Permissions.CatalogManage, Permissions.AuditView, Permissions.DataExport, Permissions.DataImport]),
            // Maintains catalogues (accounts, partners, items, warehouses) and imports/exports data; cannot touch vouchers.
            [DataKeeper] = ("Người duy trì dữ liệu", [.. Viewer, Permissions.CatalogManage, Permissions.DataImport, Permissions.DataExport]),
            // Prepares, submits, approves, posts and reverses documents; locks periods; verifies report templates.
            [Accountant] = ("Người lập / duyệt", [.. Viewer, Permissions.VoucherCreate, Permissions.VoucherEditDraft, Permissions.VoucherSubmit,
                Permissions.VoucherApprove, Permissions.VoucherPost, Permissions.VoucherReverse, Permissions.PeriodLock, Permissions.PeriodReopen,
                Permissions.CatalogManage, Permissions.DataExport, Permissions.DataImport, Permissions.SensitiveView, Permissions.AuditView,
                Permissions.SettlementManage, Permissions.CashDocument, Permissions.BankDocument, Permissions.PaymentOrderApprove, Permissions.BankReconcile,
                Permissions.StockDocument, Permissions.InvoiceDocument, Permissions.AssetManage, Permissions.LoanManage]),
        };

    /// <summary>
    /// The finer 9-role set used before the 3-role simplification. Never created on a new installation; databases that still have
    /// these roles keep them (and their permissions are topped up on upgrade). <c>SetupService.SeedLegacyRolesAsync</c>
    /// recreates them for installations that want per-function roles.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Name, string[] Perms)> Legacy =
        new Dictionary<string, (string, string[])>
        {
            ["CHIEF_ACCOUNTANT"] = ("Kế toán trưởng", [.. Defaults[Accountant].Perms]),
            ["GL_ACCOUNTANT"] = ("Kế toán tổng hợp", [.. Viewer, Permissions.VoucherCreate, Permissions.VoucherEditDraft, Permissions.VoucherSubmit,
                Permissions.VoucherPost, Permissions.CatalogManage, Permissions.DataExport, Permissions.DataImport,
                Permissions.SettlementManage, Permissions.CashDocument, Permissions.BankDocument, Permissions.BankReconcile, Permissions.StockDocument, Permissions.InvoiceDocument, Permissions.AssetManage, Permissions.LoanManage]),
            ["ARAP_ACCOUNTANT"] = ("Kế toán công nợ", [.. Viewer, Permissions.VoucherCreate, Permissions.VoucherEditDraft, Permissions.VoucherSubmit, Permissions.SettlementManage, Permissions.InvoiceDocument]),
            ["BANK_ACCOUNTANT"] = ("Kế toán ngân hàng", [.. Viewer, Permissions.VoucherCreate, Permissions.VoucherEditDraft, Permissions.VoucherSubmit,
                Permissions.BankDocument, Permissions.SettlementManage, Permissions.BankReconcile]),
            ["INVENTORY_ACCOUNTANT"] = ("Kế toán kho", [.. Viewer, Permissions.VoucherCreate, Permissions.VoucherEditDraft, Permissions.VoucherSubmit, Permissions.StockDocument, Permissions.InvoiceDocument, Permissions.AssetManage, Permissions.LoanManage]),
            ["CASHIER"] = ("Thủ quỹ", [Permissions.VoucherView, Permissions.CashDocument, Permissions.VoucherCreate, Permissions.VoucherEditDraft, Permissions.VoucherSubmit]),
            ["APPROVER"] = ("Người duyệt", [.. Viewer, Permissions.VoucherApprove, Permissions.PaymentOrderApprove]),
            ["DIRECTOR"] = ("Giám đốc", [.. Viewer, Permissions.VoucherApprove, Permissions.SensitiveView, Permissions.PaymentOrderApprove]),
            ["INTERNAL_AUDITOR"] = ("Kiểm toán nội bộ", [.. Viewer, Permissions.AuditView]),
        };
}
