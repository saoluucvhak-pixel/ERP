namespace Hak.Domain.Payroll;

/// <summary>
/// A payroll sheet received from the payroll system (JSON package, document_type "payroll"). Append-only evidence: a corrected
/// payroll arrives as a new source_version after the previous voucher was cancelled or reversed. Whether the sheet counts in
/// reports follows the status of its voucher (only Posted counts).
/// </summary>
public sealed class PayrollSheet
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string SourceSystem { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string SourceVersion { get; set; } = "";
    public string PackageId { get; set; } = "";
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public DateOnly PostingDate { get; set; }
    public string Description { get; set; } = "";
    public Guid VoucherId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public List<PayrollSheetLine> Lines { get; set; } = [];
}

public enum PayrollExpenseGroup { General = 1, Production = 2, Selling = 3, Admin = 4 }

/// <summary>One employee on a payroll sheet. Gross − employee deductions = Net (checked on import).</summary>
public sealed class PayrollSheetLine
{
    public Guid Id { get; set; }
    public Guid SheetId { get; set; }
    public Guid CompanyId { get; set; }
    public int LineNo { get; set; }
    public Guid EmployeeId { get; set; }
    public PayrollExpenseGroup ExpenseGroup { get; set; }
    public decimal Gross { get; set; }
    public decimal SocialInsurance { get; set; }
    public decimal HealthInsurance { get; set; }
    public decimal UnemploymentInsurance { get; set; }
    public decimal PersonalIncomeTax { get; set; }
    public decimal OtherDeduction { get; set; }
    public decimal Net { get; set; }
    public decimal EmployerSocialInsurance { get; set; }
    public decimal EmployerHealthInsurance { get; set; }
    public decimal EmployerUnemploymentInsurance { get; set; }
    public decimal EmployerUnionFee { get; set; }

    public decimal EmployeeDeductions => SocialInsurance + HealthInsurance + UnemploymentInsurance + PersonalIncomeTax + OtherDeduction;
    public decimal EmployerContributions => EmployerSocialInsurance + EmployerHealthInsurance + EmployerUnemploymentInsurance + EmployerUnionFee;
}
