namespace Hak.Domain.Reports;

public enum ReportDefinitionStatus { Draft = 1, Verified = 2 }

/// <summary>Multi-column templates (<see cref="ReportDefinition.Columns"/>): splitting a line's formula into its column slots.</summary>
public static class ReportColumns
{
    public const int Max = 10;
    public const int MaxNameLength = 50;

    public static IReadOnlyList<string> Parse(string? columns) =>
        string.IsNullOrWhiteSpace(columns) ? [] : columns.Split('|').Select(c => c.Trim()).ToList();

    /// <summary>
    /// The formula of each column: without columns the formula itself; a formula without '|' applies to every column; otherwise
    /// exactly one slot per column (blank slot = no value). Null formula (heading) → nulls.
    /// </summary>
    public static IReadOnlyList<string?> Slots(string? formula, int columns)
    {
        var n = Math.Max(columns, 1);
        if (string.IsNullOrWhiteSpace(formula)) return Enumerable.Repeat<string?>(null, n).ToList();
        if (!formula.Contains('|')) return Enumerable.Repeat<string?>(formula.Trim(), n).ToList();
        var parts = formula.Split('|').Select(p => string.IsNullOrWhiteSpace(p) ? null : p.Trim()).ToList();
        if (columns == 0) throw new Common.DomainException(Common.ErrorCodes.ReportFormulaInvalid, $"Mẫu không khai báo cột nên công thức không được có dấu '|': {formula.Trim()}");
        if (parts.Count != columns)
            throw new Common.DomainException(Common.ErrorCodes.ReportFormulaInvalid, $"Công thức có {parts.Count} phần ngăn bởi '|' nhưng mẫu có {columns} cột: {formula.Trim()}");
        return parts;
    }
}

/// <summary>
/// A report template (mẫu báo cáo) of one company: ordered lines with formulas (<see cref="ReportFormula"/>) and check rules.
/// Templates are entered by the company — legal statements (TT 99/2025 Phụ lục IV) are never seeded. A Draft can be edited;
/// a Verified version is frozen (database trigger) and changed only by saving a new version.
/// </summary>
public sealed class ReportDefinition
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public int Version { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Free grouping, e.g. "BCTC", "Quản trị".</summary>
    public string Category { get; set; } = "";
    /// <summary>Legal basis the template claims to follow (form code, appendix) — informational until verified.</summary>
    public string? LegalReference { get; set; }
    /// <summary>
    /// Value-column headers joined by '|' (e.g. "Đầu năm|Tăng|Giảm|Cuối năm"); null = one value column. With columns, a line's
    /// formula holds one formula per column separated by '|' (an empty slot = blank cell), or a single formula for every column.
    /// </summary>
    public string? Columns { get; set; }
    public ReportDefinitionStatus Status { get; set; } = ReportDefinitionStatus.Draft;
    public string? VerificationNote { get; set; }
    public Guid? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
    public List<ReportLine> Lines { get; set; } = [];
    public List<ReportCheck> Checks { get; set; } = [];
}

public sealed class ReportLine
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public int LineNo { get; set; }
    /// <summary>Mã số chỉ tiêu, referenced by LINE(code).</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Null = heading / text line without an amount.</summary>
    public string? Formula { get; set; }
    public int Indent { get; set; }
    public bool Bold { get; set; }
    /// <summary>Thuyết minh reference or remark.</summary>
    public string? Note { get; set; }
}

public sealed class ReportCheck
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public int LineNo { get; set; }
    /// <summary>"expression = expression", e.g. LINE(270) = LINE(440).</summary>
    public string Rule { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>
/// A finalised report result (chốt số liệu): parameters, full result JSON and its SHA-256. Append-only — a later run that differs
/// is a new snapshot, so what was submitted or presented can always be shown again exactly.
/// </summary>
public sealed class ReportSnapshot
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DefinitionId { get; set; }
    public string DefinitionCode { get; set; } = "";
    public int DefinitionVersion { get; set; }
    public bool DefinitionVerified { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public DateOnly? CompareFrom { get; set; }
    public DateOnly? CompareTo { get; set; }
    public string ResultJson { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}
