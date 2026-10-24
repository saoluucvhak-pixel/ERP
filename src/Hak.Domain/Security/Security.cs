namespace Hak.Domain.Security;

public sealed class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = "";
    /// <summary>Lower-cased user name for case-insensitive uniqueness.</summary>
    public string NormalizedUserName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    /// <summary>System administrators manage companies/users; they get no accounting rights implicitly.</summary>
    public bool IsSystemAdmin { get; set; }
    /// <summary>Consecutive failed logins; reset by a successful login or a password reset.</summary>
    public int FailedLoginCount { get; set; }
    /// <summary>Temporary lockout after too many failed logins (brute-force protection).</summary>
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public long RowVersion { get; set; }
}

public sealed class Role
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<RolePermission> Permissions { get; set; } = [];
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public string Permission { get; set; } = "";
}

/// <summary>Grants a role to a user inside one company only (data isolation per legal entity).</summary>
public sealed class UserCompanyRole
{
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RoleId { get; set; }
}

public sealed class LoginSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>SHA-256 of the bearer token; the token itself is never stored.</summary>
    public string TokenHash { get; set; } = "";
    public string? Device { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// Lần đăng nhập / nhập mật khẩu sai liên tiếp của một tài khoản TỪ MỘT NGUỒN (máy chạy API + địa chỉ gửi yêu cầu, do server xác định).
/// Khóa theo nguồn để người dò mật khẩu ở một máy không khóa được tài khoản của người khác ở máy khác (SEC-07); khóa chung của
/// <see cref="User.LockedUntil"/> chỉ còn là lưới an toàn với ngưỡng cao hơn, cho kiểu dò phân tán.
/// </summary>
public sealed class LoginThrottle
{
    public Guid UserId { get; set; }
    public string Source { get; set; } = "";
    public int FailedCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Mốc chuyển chuỗi nhật ký của một phạm vi (công ty, hoặc Guid.Empty = hệ thống) sang chuỗi có khóa (SEC-09): bản ghi đến
/// <see cref="LegacySeq"/> dùng SHA-256 thường (ghi trước khi có khóa chủ), mọi bản ghi sau bắt buộc có khóa. Mac = HMAC bằng khóa chủ,
/// nên không giả được mốc; thiếu mốc trong một chuỗi đang có bản ghi thì coi là bị sửa (kẻ tấn công không thể "hạ" cả đuôi chuỗi về
/// SHA-256 thường mà không bị phát hiện). Chỉ thêm, không sửa / xóa (trigger).
/// </summary>
public sealed class AuditChainAnchor
{
    public Guid CompanyId { get; set; }
    public long LegacySeq { get; set; }
    public string LegacyHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string Mac { get; set; } = "";
}

/// <summary>Hash-chained audit record (docs/02 ADR-08). Chain is per company; Guid.Empty = system scope.</summary>
public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public long Seq { get; set; }
    public DateTime At { get; set; }
    public Guid? UserId { get; set; }
    public Guid? SessionId { get; set; }
    public string? Device { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string Result { get; set; } = "OK";
    public long? EntityRowVersion { get; set; }
    public string? DataJson { get; set; }
    public string PrevHash { get; set; } = "";
    public string Hash { get; set; } = "";
}

public static class Permissions
{
    public const string VoucherView = "voucher.view";
    public const string VoucherCreate = "voucher.create";
    public const string VoucherEditDraft = "voucher.edit_draft";
    public const string VoucherSubmit = "voucher.submit";
    public const string VoucherApprove = "voucher.approve";
    public const string VoucherPost = "voucher.post";
    public const string VoucherReverse = "voucher.reverse";
    public const string PeriodLock = "period.lock";
    public const string PeriodReopen = "period.reopen";
    public const string DataExport = "data.export";
    public const string DataImport = "data.import";
    public const string SensitiveView = "sensitive.view";
    public const string ConfigManage = "config.manage";
    public const string CatalogManage = "catalog.manage";
    public const string ReportView = "report.view";
    public const string AuditView = "audit.view";
    public const string SettlementManage = "settlement.manage";
    public const string CashDocument = "cash.document";
    public const string BankDocument = "bank.document";
    public const string PaymentOrderApprove = "payment_order.approve";
    /// <summary>Chấp nhận thanh toán hóa đơn mua (bàn giao Cung ứng → Thanh toán). Không phát sinh bút toán.</summary>
    public const string PaymentAccept = "payment.accept";
    /// <summary>Duyệt sai lệch đối chiếu hóa đơn mua (lượng, giá, kiểm nghiệm) — tách khỏi quyền chấp nhận thanh toán.</summary>
    public const string PaymentVariance = "payment.variance";
    /// <summary>Chấp thuận xác nhận đơn bán vượt hạn mức tín dụng của khách hàng (người có thẩm quyền, kèm lý do).</summary>
    public const string CreditOverride = "credit.override";
    /// <summary>Cố ý lập hóa đơn từ phiếu kho của đối tượng khác (bắt buộc ghi lý do; có nhật ký).</summary>
    public const string InvoicePartnerOverride = "invoice.partner_override";
    public const string BankReconcile = "bank.reconcile";
    public const string StockDocument = "stock.document";
    public const string InvoiceDocument = "invoice.document";
    /// <summary>Create, activate and dispose fixed assets / tools (thẻ TSCĐ, CCDC).</summary>
    public const string AssetManage = "asset.manage";
    /// <summary>Create and activate loan contracts, link drawdowns/repayments, change rates.</summary>
    public const string LoanManage = "loan.manage";
    /// <summary>Lập định mức, tổ xưởng, lệnh sản xuất, báo công (chu trình sản xuất). Ban hành lệnh cần voucher.approve; hoàn thành lệnh cần voucher.post và stock.document.</summary>
    public const string ProductionManage = "production.manage";
}
