namespace Hak.Domain.Regime;

/// <summary>An accounting regime family, e.g. enterprise regime per Circular 99/2025/TT-BTC.</summary>
public sealed class AccountingRegime
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<AccountingRegimeVersion> Versions { get; set; } = [];
}

public enum VerificationStatus { Unverified = 0, Verified = 1 }

/// <summary>
/// A dated version of a regime. Production companies may only post under a Verified version,
/// i.e. one a named person checked against the original legal text (docs/03 §8).
/// </summary>
public sealed class AccountingRegimeVersion
{
    public Guid Id { get; set; }
    public Guid RegimeId { get; set; }
    public string VersionCode { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public VerificationStatus VerificationStatus { get; set; }
    public Guid? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerificationNote { get; set; }
    public List<LegalSource> LegalSources { get; set; } = [];
}

public sealed class LegalSource
{
    public Guid Id { get; set; }
    public Guid RegimeVersionId { get; set; }
    /// <summary>Which rule group this source backs: CHART_OF_ACCOUNTS, FINANCIAL_STATEMENTS, POSTING_RULES, ...</summary>
    public string RuleGroup { get; set; } = "";
    public string DocumentNo { get; set; } = "";
    public DateOnly? IssuedDate { get; set; }
    public string? Article { get; set; }
    public string? Url { get; set; }
    /// <summary>SHA-256 of the archived copy of the official text, if archived.</summary>
    public string? CopySha256 { get; set; }
}
