namespace Hak.Domain.Banking;

/// <summary>Số tài khoản ngân hàng của đối tượng (nhà cung cấp, nhân viên, cổ đông…) để chi tiền: mỗi đối tượng có thể có nhiều tài khoản, một tài khoản mặc định.</summary>
public sealed class PartnerBankAccount
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PartnerId { get; set; }
    public string BankName { get; set; } = "";
    public string? Branch { get; set; }
    public string AccountNo { get; set; } = "";
    /// <summary>Tên chủ tài khoản đúng như ngân hàng ghi nhận (thường là tên pháp nhân / họ tên có dấu).</summary>
    public string AccountName { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

/// <summary>Số tài khoản ngân hàng của chính công ty, gắn với tài khoản tiền gửi trên sổ cái (TK có CashKind = Bank).</summary>
public sealed class CompanyBankAccount
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AccountId { get; set; }
    public string BankName { get; set; } = "";
    public string? Branch { get; set; }
    public string AccountNo { get; set; } = "";
    public string AccountName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}
