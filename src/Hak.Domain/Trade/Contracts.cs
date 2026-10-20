using Hak.Domain.Common;

namespace Hak.Domain.Trade;

public enum ContractCategory { Quantity = 1, Value = 2, Standard = 3 }

/// <summary>Draft → PendingApproval → Active → Completed; hoặc Cancelled. "Hết hạn" không lưu: Active mà quá ngày hết hạn thì tính là hết hạn khi xem và khi kiểm soát đơn.</summary>
public enum ContractStatus { Draft = 1, PendingApproval = 2, Active = 3, Completed = 4, Cancelled = 9 }

/// <summary>
/// Hợp đồng mua bán (tệp phân tích quản lý hợp đồng trong ERP). Là khung cam kết: đơn mua / đơn bán kế thừa từ hợp đồng, lấy giá cam kết và trừ lùi số lượng / giá trị còn lại.
/// Không ghi Nhật ký (hợp đồng chưa phải nghiệp vụ kế toán); sổ sách vẫn phát sinh từ hóa đơn, phiếu kho, thu chi.
/// Mã: CTR-SAL-{năm}-{số 5 chữ số} (bán), CTR-PUR-{năm}-{số} (mua).
/// </summary>
public sealed class Contract
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public InvoiceKind Kind { get; set; }
    public ContractCategory Category { get; set; } = ContractCategory.Quantity;
    public Guid PartnerId { get; set; }
    public string Currency { get; set; } = "VND";
    public decimal ExchangeRate { get; set; } = 1;
    public DateOnly? SignedDate { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public string? PaymentTerms { get; set; }
    /// <summary>Tổng giá trị trước thuế. Hợp đồng khung giá trị: hạn mức giá trị; hợp đồng khung số lượng: tổng các dòng (cộng phụ lục).</summary>
    public decimal TotalAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
    public string? StatusReason { get; set; }
    /// <summary>Số phiên bản nội dung, tăng mỗi lần sửa khi còn Nháp.</summary>
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? SubmittedBy { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long RowVersion { get; set; }
    public List<ContractLine> Lines { get; set; } = [];
    public List<ContractMilestone> Milestones { get; set; } = [];

    public bool IsExpiredAt(DateOnly day) => day > ExpirationDate;

    public void Move(ContractStatus from, ContractStatus to)
    {
        if (Status != from) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hợp đồng {Code} đang ở trạng thái {Status}, không chuyển sang {to} được.");
        Status = to; RowVersion++;
    }
}

public sealed class ContractLine
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public Guid CompanyId { get; set; }
    public int LineNo { get; set; }
    public Guid ItemId { get; set; }
    public decimal CommittedQty { get; set; }
    /// <summary>Đơn giá thỏa thuận (trước chiết khấu).</summary>
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal TaxRate { get; set; }
    public string? Notes { get; set; }

    /// <summary>Đơn giá sau chiết khấu — giá áp cho đơn kế thừa.</summary>
    public decimal NetPrice => UnitPrice * (100 - DiscountRate) / 100;
    public decimal CommittedAmount => CommittedQty * NetPrice;
}

/// <summary>Mốc thanh toán / tạm ứng / nghiệm thu theo hợp đồng.</summary>
public sealed class ContractMilestone
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public Guid CompanyId { get; set; }
    public int LineNo { get; set; }
    public string Name { get; set; } = "";
    public decimal? Percentage { get; set; }
    public decimal DueAmount { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal PaidAmount { get; set; }
    public DateOnly? LastPaidDate { get; set; }

    /// <summary>PENDING / PARTIAL / PAID / OVERDUE theo ngày xem.</summary>
    public string PaymentStatus(DateOnly today)
        => PaidAmount >= DueAmount ? "PAID" : DueDate < today ? "OVERDUE" : PaidAmount > 0 ? "PARTIAL" : "PENDING";
}

/// <summary>Phụ lục điều chỉnh giá trị, số lượng hoặc gia hạn. Mã {mã hợp đồng}-A{số 2 chữ số}. Bất biến sau khi lập.</summary>
public sealed class ContractAmendment
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public DateOnly AmendmentDate { get; set; }
    public string Summary { get; set; } = "";
    public decimal ValueChange { get; set; }
    public DateOnly? NewExpirationDate { get; set; }
    /// <summary>Thay đổi số lượng cam kết theo dòng: "mã vật tư:+số lượng" lưu dạng JSON.</summary>
    public string? LineChangesJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

/// <summary>Tài liệu đính kèm (tham chiếu tệp bản ký / scan; phần mềm lưu đường dẫn, không lưu nội dung tệp).</summary>
public sealed class ContractDocument
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public Guid CompanyId { get; set; }
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? FileType { get; set; }
    public DateTime UploadedAt { get; set; }
    public Guid UploadedBy { get; set; }
}
