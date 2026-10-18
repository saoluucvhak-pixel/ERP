namespace Hak.Domain.Rules;

public enum RuleStatus { Draft = 1, Active = 2, Retired = 3 }

/// <summary>
/// Company-specific mapping from an abstract account role (e.g. AR_TRADE, CASH_DEFAULT) to a detail
/// account, so posting rules never hard-code detail accounts (master prompt §25).
/// </summary>
public sealed class AccountRoleMapping
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string RoleCode { get; set; } = "";
    public Guid AccountId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
}

/// <summary>
/// Versioned posting rule: (document type, business event) → line templates. A version becomes
/// immutable once Active; changing it means creating the next version. Activation requires its
/// test cases to pass (stored in <see cref="LastTestRunJson"/>).
/// </summary>
public sealed class PostingRule
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public int Version { get; set; }
    public string DocumentType { get; set; } = "";
    public string BusinessEvent { get; set; } = "";
    public int Priority { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public RuleStatus Status { get; set; } = RuleStatus.Draft;
    /// <summary>JSON: {"lines":[{"side":"D","account":"DOC:MAIN_ACCOUNT","partner":"DOC:PARTNER","settle":"LINE:SETTLE","constraint":"CASH_EQUIVALENT"}]}</summary>
    public string DefinitionJson { get; set; } = "";
    /// <summary>JSON array of test cases: [{"name":..., "document":{...}, "expected":[{"side","account_code","amount","partner_code"}]}]</summary>
    public string TestCasesJson { get; set; } = "[]";
    public string? LastTestRunJson { get; set; }
    public string? Description { get; set; }
    public Guid? RegimeVersionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public Guid? ActivatedBy { get; set; }
    public long RowVersion { get; set; }
}

/// <summary>
/// Versioned period-end closing rule set (kết chuyển). Each step moves the balance of every postable
/// account whose code starts with one of <see cref="ClosingStep.SourcePrefixes"/> into the target account.
/// The actual legal closing scheme must be configured from the verified regime; nothing is seeded.
/// </summary>
public sealed class ClosingRuleSet
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public int Version { get; set; }
    public RuleStatus Status { get; set; } = RuleStatus.Draft;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Description { get; set; }
    public List<ClosingStep> Steps { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public Guid? ActivatedBy { get; set; }
}

public sealed class ClosingStep
{
    public Guid Id { get; set; }
    public Guid RuleSetId { get; set; }
    public int Sequence { get; set; }
    public string Description { get; set; } = "";
    /// <summary>Comma-separated account-code prefixes.</summary>
    public string SourcePrefixes { get; set; } = "";
    public Guid TargetAccountId { get; set; }
}
