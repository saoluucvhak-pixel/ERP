namespace Hak.Domain.Equity;

/// <summary>Chủ sở hữu / cổ đông / thành viên góp vốn: một đối tượng trong danh mục, kèm vốn cam kết và loại (cá nhân chịu thuế TNCN trên cổ tức).</summary>
public sealed class Shareholder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PartnerId { get; set; }
    public decimal CommittedCapital { get; set; }
    public bool IsIndividual { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

public enum CapitalMovementKind { Contribution = 1, Withdrawal = 2 }

/// <summary>Một lần góp vốn / rút vốn thực tế của chủ sở hữu, kèm chứng từ nháp ghi nhận (Nợ tiền / Có vốn góp; rút vốn ngược lại).</summary>
public sealed class CapitalMovement
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ShareholderId { get; set; }
    public CapitalMovementKind Kind { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public Guid? VoucherId { get; set; }
    public Guid? CashAccountId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

public enum DividendStatus
{
    /// <summary>Dự thảo phương án chia: tính theo vốn thực góp đến ngày nghị quyết.</summary>
    Draft = 1,
    /// <summary>Đã duyệt (người khác người lập), chờ lập chứng từ ghi nhận phải trả.</summary>
    Approved = 2,
    /// <summary>Đã lập chứng từ ghi nhận cổ tức phải trả (Nợ lợi nhuận chưa phân phối / Có phải trả cổ đông, thuế TNCN khấu trừ).</summary>
    Declared = 3,
    Cancelled = 4,
}

public sealed class DividendResolution
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = "";
    public DateOnly ResolutionDate { get; set; }
    public string Description { get; set; } = "";
    public decimal TotalAmount { get; set; }
    /// <summary>Thuế suất khấu trừ TNCN trên cổ tức của cổ đông cá nhân (do kế toán khai báo, [CẦN XÁC MINH] theo luật thuế TNCN hiện hành).</summary>
    public decimal PitRatePercent { get; set; }
    public DividendStatus Status { get; set; } = DividendStatus.Draft;
    public Guid? DeclarationVoucherId { get; set; }
    public string? DecisionNote { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public ICollection<DividendLine> Lines { get; set; } = [];
}

public sealed class DividendLine
{
    public Guid Id { get; set; }
    public Guid ResolutionId { get; set; }
    public Guid ShareholderId { get; set; }
    /// <summary>Vốn thực góp của chủ sở hữu đến ngày nghị quyết (căn cứ tính tỷ lệ).</summary>
    public decimal CapitalBasis { get; set; }
    public decimal Gross { get; set; }
    public decimal PitWithheld { get; set; }
    public decimal Net { get; set; }
    /// <summary>Chứng từ chi nháp trả phần Net cho người này (nếu đã lập).</summary>
    public Guid? PaymentVoucherId { get; set; }
}
