namespace Hak.Domain.Settlement;

/// <summary>
/// Links a posted debit line to a posted credit line on the same open-item account and partner
/// (master prompt §14). The remaining balance of each obligation is its amount minus its active
/// allocations — never a naive sum of vouchers. Rows are never deleted; an allocation is undone
/// by voiding it (who, when, why), which keeps the history auditable.
/// </summary>
public sealed class SettlementAllocation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AccountId { get; set; }
    public Guid PartnerId { get; set; }
    public Guid DebitLineId { get; set; }
    public Guid CreditLineId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>
    /// Foreign-currency part settled, when both lines are in the same foreign currency (null for functional-currency lines).
    /// Open foreign amount of a line = its foreign amount − Σ active allocations' foreign amounts.
    /// </summary>
    public decimal? ForeignAmount { get; set; }
    /// <summary>Effective date: not earlier than either line's posting date.</summary>
    public DateOnly AllocationDate { get; set; }
    /// <summary>Voucher whose posting created this allocation (null for manual matching).</summary>
    public Guid? SourceVoucherId { get; set; }
    public string Origin { get; set; } = SettlementOrigins.Manual;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }

    public DateTime? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public string? VoidReason { get; set; }

    public bool IsActive => VoidedAt is null;
}

public static class SettlementOrigins
{
    public const string Voucher = "VOUCHER";
    public const string Manual = "MANUAL";
    public const string Auto = "AUTO_FIFO";
    public const string Reversal = "REVERSAL";
}
