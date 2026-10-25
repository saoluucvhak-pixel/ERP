namespace Hak.Domain.Catalog;

/// <summary>How the account balance is presented in trial balance / statements.</summary>
public enum BalanceDisplay
{
    /// <summary>Net signed balance shown on the side it falls.</summary>
    Net = 0,
    /// <summary>Dual-nature account (e.g. receivables/payables): debit and credit balances shown gross per partner.</summary>
    GrossByPartner = 1,
}

/// <summary>Open-item (subledger) tracking: which side creates obligations.</summary>
public enum OpenItemNature
{
    None = 0,
    /// <summary>Debit lines are obligations (invoices to customers); credit lines settle them or are advances.</summary>
    Receivable = 1,
    /// <summary>Credit lines are obligations (supplier invoices); debit lines settle them or are advances.</summary>
    Payable = 2,
}

/// <summary>Cash-equivalent classification used by cash book, negative-balance control and internal-transfer rules.</summary>
public enum CashKind { None = 0, Cash = 1, Bank = 2, InTransit = 3 }

public enum NegativeBalancePolicy { None = 0, Warn = 1, Block = 2 }

public sealed class Account
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Only leaf/detail accounts can receive postings.</summary>
    public bool IsPostable { get; set; } = true;
    public bool RequiresPartner { get; set; }
    public BalanceDisplay BalanceDisplay { get; set; }
    public OpenItemNature OpenItemNature { get; set; }
    public CashKind CashKind { get; set; }
    /// <summary>For debit-nature balances such as cash funds (master prompt §10: warn or block negative cash).</summary>
    public NegativeBalancePolicy NegativeBalancePolicy { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Report tags, comma separated (e.g. "DH" long-term part, "TDT" cash equivalent, "HCSD" restricted cash, "LV" borrowing
    /// cost): financial-statement formulas filter detail accounts by them, e.g. 131*[DH] → Mã số 211. Company configuration.
    /// </summary>
    public string? ReportTags { get; set; }
    /// <summary>Regime version this account template line came from (null = company-specific detail).</summary>
    public Guid? RegimeVersionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
}

[Flags]
public enum PartnerRoles { None = 0, Customer = 1, Supplier = 2, Employee = 4, Other = 8 }

/// <summary>Trạng thái phê duyệt nhà cung cấp (tệp phân tích ERP, mục V.2: danh mục nhà cung cấp được duyệt). Chỉ nhà cung cấp Approved mới được đặt hàng.</summary>
public enum SupplierStatus { Approved = 0, Pending = 1, Blocked = 2 }

public sealed class BusinessPartner
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    /// <summary>Hạn mức tín dụng (VND) cho bán chịu; null = không kiểm tra (danh mục khách hàng theo tệp phân tích ERP, mục V.1.2.1).</summary>
    public decimal? CreditLimit { get; set; }
    /// <summary>Điều kiện thanh toán: số ngày từ ngày hóa đơn đến hạn thanh toán.</summary>
    public int? PaymentTermsDays { get; set; }
    /// <summary>Điều khoản chiết khấu thanh toán mặc định của đối tượng (điền sẵn khi lập hóa đơn): P% nếu trả trong N ngày.</summary>
    public decimal? PaymentDiscountPercent { get; set; }
    public int? PaymentDiscountDays { get; set; }
    /// <summary>Phần mở rộng của điều khoản chiết khấu thanh toán: cơ sở tính, dung sai ngày, bậc phụ (xem <c>PaymentTerms</c>), ví dụ <c>base=incl;grace=1;tier=3:5</c>.</summary>
    public string? PaymentDiscountOptions { get; set; }
    public PartnerRoles Roles { get; set; }
    public SupplierStatus SupplierStatus { get; set; } = SupplierStatus.Approved;
    public string? SupplierStatusReason { get; set; }
    public Guid? SupplierDecidedBy { get; set; }
    public DateTime? SupplierDecidedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
