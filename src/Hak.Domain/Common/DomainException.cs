namespace Hak.Domain.Common;

/// <summary>Business-rule violation with a stable error code (see docs/03 §4).</summary>
public class DomainException(string code, string message, IReadOnlyList<ValidationError>? errors = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyList<ValidationError> Errors { get; } = errors ?? [];
}

public sealed record ValidationError(string Code, string Message, int? LineNo = null);

public static class ErrorCodes
{
    public const string VoucherNoLines = "VOUCHER_NO_LINES";
    public const string VoucherUnbalanced = "VOUCHER_UNBALANCED";
    public const string AmountNotPositive = "AMOUNT_NOT_POSITIVE";
    public const string AmountPrecision = "AMOUNT_PRECISION";
    public const string AccountNotFound = "ACCOUNT_NOT_FOUND";
    public const string AccountWrongCompany = "ACCOUNT_WRONG_COMPANY";
    public const string AccountNotPostable = "ACCOUNT_NOT_POSTABLE";
    public const string AccountInactive = "ACCOUNT_INACTIVE";
    public const string PartnerRequired = "PARTNER_REQUIRED";
    public const string PartnerWrongCompany = "PARTNER_WRONG_COMPANY";
    public const string PeriodNotFound = "PERIOD_NOT_FOUND";
    public const string PeriodLocked = "PERIOD_LOCKED";
    public const string InvalidStateTransition = "INVALID_STATE_TRANSITION";
    public const string SodViolation = "SOD_VIOLATION";
    public const string AlreadyPosted = "ALREADY_POSTED";
    public const string DuplicateSource = "DUPLICATE_SOURCE";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string RegimeNotVerified = "REGIME_NOT_VERIFIED";
    public const string RegimeNotEffective = "REGIME_NOT_EFFECTIVE";
    public const string FiscalYearNotFound = "FISCAL_YEAR_NOT_FOUND";
    public const string ReasonRequired = "REASON_REQUIRED";
    public const string SettlementMismatch = "SETTLEMENT_MISMATCH";
    public const string SettlementExceedsOpen = "SETTLEMENT_EXCEEDS_OPEN";
    public const string RuleMissing = "RULE_MISSING";
    public const string RuleAmbiguous = "RULE_AMBIGUOUS";
    public const string RuleRoleUnmapped = "RULE_ROLE_UNMAPPED";
    public const string RuleInvalid = "RULE_INVALID";
    public const string RuleTestFailed = "RULE_TEST_FAILED";
    public const string RuleConstraint = "RULE_CONSTRAINT";
    public const string NegativeBalance = "NEGATIVE_BALANCE";
    /// <summary>Warnings end in _WARNING and never block a transition.</summary>
    public const string NegativeBalanceWarning = "NEGATIVE_BALANCE_WARNING";
    public const string PeriodHasPending = "PERIOD_HAS_PENDING";
    public const string AlreadyClosed = "ALREADY_CLOSED";
    public const string StatementUnbalanced = "STATEMENT_UNBALANCED";
    public const string StatementDuplicate = "STATEMENT_DUPLICATE";
    public const string MatchInvalid = "MATCH_INVALID";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string CostingPending = "COSTING_PENDING";
    public const string StockBackdated = "STOCK_BACKDATED";
    public const string PeriodCosted = "PERIOD_COSTED";
    public const string CostingPolicyMissing = "COSTING_POLICY_MISSING";
    public const string MeasurementPending = "MEASUREMENT_PENDING";
    public const string PriceDifference = "PRICE_DIFFERENCE";
    public const string OverInvoiced = "OVER_INVOICED";
    public const string DuplicateInvoice = "DUPLICATE_INVOICE";
    public const string OverDelivered = "OVER_DELIVERED";
    public const string OrderMismatch = "ORDER_MISMATCH";
    public const string DepreciationPending = "DEPRECIATION_PENDING";
    public const string PeriodDepreciated = "PERIOD_DEPRECIATED";
    public const string AssetCostMismatch = "ASSET_COST_MISMATCH";
    /// <summary>A settlement of a foreign-currency item must be booked at its carrying amount; the difference is an explicit FX gain/loss.</summary>
    public const string FxBookValue = "FX_BOOK_VALUE";
    public const string InterestPending = "INTEREST_PENDING";
    public const string BackupInvalid = "BACKUP_INVALID";
    public const string NotSupported = "NOT_SUPPORTED";
    /// <summary>A report formula does not parse, references an unknown line, or lines reference each other in a loop.</summary>
    public const string ReportFormulaInvalid = "REPORT_FORMULA_INVALID";
    public const string PeriodAccrued = "PERIOD_ACCRUED";

    public static bool IsWarning(string code) => code.EndsWith("_WARNING", StringComparison.Ordinal);
    public const string LedgerImmutable = "LEDGER_IMMUTABLE";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string DuplicateCode = "DUPLICATE_CODE";
    public const string FiscalYearOverlap = "FISCAL_YEAR_OVERLAP";
    public const string CurrencyMismatch = "CURRENCY_MISMATCH";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string PackageSchemaInvalid = "PACKAGE_SCHEMA_INVALID";
    public const string PackageChecksumMismatch = "PACKAGE_CHECKSUM_MISMATCH";
    public const string PackageSignatureInvalid = "PACKAGE_SIGNATURE_INVALID";
    public const string PackageReplayed = "PACKAGE_REPLAYED";
    public const string UnknownSource = "UNKNOWN_SOURCE_SYSTEM";
}
