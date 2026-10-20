using Hak.Domain.Common;

namespace Hak.Domain.Inventory;

/// <summary>
/// Approved MT → BDMT conversion method (master prompt §15: no assumed standard dryness).
/// The business uses dry content (độ khô): BDMT = MT × dryness% / 100, with dryness measured at receipt and,
/// for export shipments, at the ship (evidence: inspection certificate / chứng thư).
/// </summary>
public sealed class DrynessMethod
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public int DrynessDecimals { get; set; } = 2;
    public int BdmtDecimals { get; set; } = 3;
    public DateOnly EffectiveFrom { get; set; }
    /// <summary>Who approved the method and on what basis (decision, contract clause...).</summary>
    public string ApprovalNote { get; set; } = "";
    public Guid ApprovedBy { get; set; }
    public DateTime ApprovedAt { get; set; }
}

/// <summary>
/// Độ khô công bố theo ngày của một vật tư (đơn vị tính phụ BDMT), dùng như tỷ giá: ngày không có số thì lấy số của ngày gần nhất trước đó.
/// Chỉ dùng để tạm tính BDMT cho dòng chưa có kết quả đo; kết quả đo/chứng thư luôn được ưu tiên. Không ghi sổ cái.
/// </summary>
public sealed class DrynessDailyRate
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ItemId { get; set; }
    public DateOnly Date { get; set; }
    public decimal DrynessPercent { get; set; }
    public string? Note { get; set; }
    public Guid UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum MeasurementKind
{
    /// <summary>Dryness measured when goods are received.</summary>
    Receipt = 1,
    /// <summary>Dryness measured at the ship on export; evidence must be a certificate (chứng thư).</summary>
    Shipment = 2,
    /// <summary>Stock count or other re-measurement.</summary>
    Other = 3,
}

public enum EvidenceType
{
    /// <summary>Chứng thư giám định.</summary>
    Certificate = 1,
    /// <summary>Phiếu/biên bản đo độ khô nội bộ.</summary>
    InternalReport = 2,
}

/// <summary>
/// One dryness measurement of a stock-document line. Never edited: a correction supersedes it with a new row
/// (history kept, reason required). At most one active measurement per document line.
/// </summary>
public sealed class StockMeasurement
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public int LineNo { get; set; }
    public Guid ItemId { get; set; }
    public MeasurementKind Kind { get; set; }
    public decimal MeasuredMt { get; set; }
    public decimal DrynessPercent { get; set; }
    public decimal Bdmt { get; set; }
    public Guid MethodId { get; set; }
    public EvidenceType EvidenceType { get; set; }
    public string EvidenceNo { get; set; } = "";
    public string? EvidenceIssuer { get; set; }
    public DateOnly EvidenceDate { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? SupersededAt { get; set; }
    public Guid? SupersededBy { get; set; }
    public string? SupersedeReason { get; set; }
}

public static class DrynessCalculator
{
    /// <summary>BDMT = MT × dryness% / 100, rounded half away from zero to the method's BDMT decimals.</summary>
    public static decimal Bdmt(decimal mt, decimal drynessPercent, DrynessMethod method)
    {
        if (mt <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Khối lượng MT phải lớn hơn 0.");
        if (drynessPercent <= 0 || drynessPercent > 100)
            throw new DomainException(ErrorCodes.ValidationFailed, "Độ khô phải lớn hơn 0% và không vượt quá 100%.");
        if (!MoneyMath.HasAtMostDecimals(drynessPercent, method.DrynessDecimals))
            throw new DomainException(ErrorCodes.ValidationFailed, $"Độ khô tối đa {method.DrynessDecimals} chữ số thập phân theo phương pháp {method.Code}.");
        return MoneyMath.Round(mt * drynessPercent / 100m, method.BdmtDecimals);
    }
}
