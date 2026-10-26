namespace Hak.Domain.Tax;

/// <summary>Unknown identifies legacy evidence without a declared sign convention; never infer a decrease from the books being matched.</summary>
public enum EInvoiceAmountConvention { Unknown = 0, Signed = 1, MagnitudeWithNature = 2 }

public enum TaxDirection { Input = 1, Output = 2 }

/// <summary>Status of an e-invoice as reported by the e-invoice system [CẦN XÁC MINH theo quy định HĐĐT hiện hành].</summary>
public enum EInvoiceStatus { Original = 1, Replacement = 2, Adjustment = 3, Replaced = 4, Cancelled = 5 }

/// <summary>
/// One e-invoice as received from the e-invoice system / tax portal (source 1 of the 3-way reconciliation).
/// Imported, never typed in; identity = direction + seller tax code + series + number.
/// </summary>
public sealed class EInvoiceRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public TaxDirection Direction { get; set; }
    public string SellerTaxCode { get; set; } = "";
    public string SellerName { get; set; } = "";
    public string? BuyerTaxCode { get; set; }
    public string? BuyerName { get; set; }
    public string Series { get; set; } = "";
    public string Number { get; set; } = "";
    public DateOnly IssueDate { get; set; }
    public string? Currency { get; set; }
    public decimal? ExchangeRate { get; set; }
    /// <summary>Amounts in functional currency (VND).</summary>
    public decimal AmountBeforeVat { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalPayment { get; set; }
    public EInvoiceStatus Status { get; set; }
    public EInvoiceAmountConvention AmountConvention { get; set; }
    public EInvoiceAdjustNature AdjustmentNature { get; set; }
    /// <summary>For replacement/adjustment: the invoice it relates to.</summary>
    public string? RelatedSeries { get; set; }
    public string? RelatedNumber { get; set; }
    public string? LookupCode { get; set; }
    public Guid ImportBatchId { get; set; }
    public DateTime ImportedAt { get; set; }
    public Guid ImportedBy { get; set; }
    public List<EInvoiceRateLine> RateLines { get; set; } = [];
}

/// <summary>VAT breakdown by rate on an e-invoice (null rate = not subject to VAT).</summary>
public sealed class EInvoiceRateLine
{
    public Guid Id { get; set; }
    public Guid EInvoiceId { get; set; }
    public decimal? VatRate { get; set; }
    public decimal AmountBeforeVat { get; set; }
    public decimal VatAmount { get; set; }
}

/// <summary>A later import reporting a new status for an already-imported e-invoice (e.g. cancelled, replaced). Append-only.</summary>
public sealed class EInvoiceStatusChange
{
    public Guid Id { get; set; }
    public Guid EInvoiceId { get; set; }
    public EInvoiceStatus NewStatus { get; set; }
    public Guid ImportBatchId { get; set; }
    public DateTime At { get; set; }
}

public sealed class EInvoiceImportBatch
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string SourceName { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public int Imported { get; set; }
    public int Duplicates { get; set; }
    public DateTime ImportedAt { get; set; }
    public Guid ImportedBy { get; set; }
}

public enum DeclarationStatus { Draft = 1, Filed = 2 }

/// <summary>
/// Figures the company actually declared for a VAT period (source 3). Indicator codes and labels are entered from the
/// official form in use — the form itself is not reproduced here [CẦN XÁC MINH mẫu tờ khai GTGT hiện hành].
/// Each indicator can be mapped to a computed measure so the system can compare it with the books.
/// </summary>
public sealed class TaxDeclaration
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TaxType { get; set; } = "GTGT";
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public DeclarationStatus Status { get; set; } = DeclarationStatus.Draft;
    public string? FilingReference { get; set; }
    public DateOnly? FiledDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
    public List<TaxDeclarationLine> Lines { get; set; } = [];
}

/// <summary>Computed measures a declaration indicator can be compared against.</summary>
public enum TaxMeasure
{
    None = 0,
    InputAmountBeforeVat = 1,
    InputVat = 2,
    OutputAmountBeforeVat = 3,
    OutputVat = 4,
}

public sealed class TaxDeclarationLine
{
    public Guid Id { get; set; }
    public Guid DeclarationId { get; set; }
    public string IndicatorCode { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal DeclaredAmount { get; set; }
    public TaxMeasure Measure { get; set; }
    /// <summary>Restricts an output/input measure to one VAT rate (e.g. the 8% line); null = all rates.</summary>
    public decimal? VatRate { get; set; }
}
