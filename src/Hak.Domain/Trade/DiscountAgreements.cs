namespace Hak.Domain.Trade;

/// <summary>
/// Thỏa thuận chiết khấu thương mại theo doanh số (chiết khấu cuối kỳ / rebate), cho một đối tượng trong một khoảng ngày. Chiều bán: khách đạt doanh số thì được giảm;
/// chiều mua: nhà cung cấp giảm cho công ty khi mua đạt mức. Doanh số tính theo tiền hàng chưa thuế của hóa đơn thường đã ghi sổ trong khoảng ngày, trừ hàng trả lại / giảm giá
/// đã ghi sổ. Bậc chiết khấu áp cho TOÀN BỘ doanh số trong kỳ (không lũy tiến từng phần). Quyết toán sinh hóa đơn điều chỉnh giảm (nháp) chia theo từng hóa đơn gốc để thuế GTGT
/// giảm đúng thuế suất của từng dòng.
/// </summary>
public sealed class DiscountAgreement
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public InvoiceKind Kind { get; set; }
    public Guid PartnerId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
    public List<DiscountAgreementTier> Tiers { get; set; } = [];
}

/// <summary>Bậc: doanh số (chưa thuế, tiền hạch toán) từ <see cref="Threshold"/> trở lên thì chiết khấu <see cref="Percent"/>% trên toàn bộ doanh số.</summary>
public sealed class DiscountAgreementTier
{
    public Guid Id { get; set; }
    public Guid AgreementId { get; set; }
    public Guid CompanyId { get; set; }
    public decimal Threshold { get; set; }
    public decimal Percent { get; set; }
}

/// <summary>
/// Một lần quyết toán thỏa thuận: tổng doanh số, bậc đạt, số chiết khấu và các hóa đơn điều chỉnh nháp đã sinh. Mỗi thỏa thuận chỉ có một lần quyết toán còn hiệu lực
/// (chỉ mục duy nhất trên các bản ghi chưa hủy); hủy được khi chưa hóa đơn điều chỉnh nào đã ghi sổ.
/// </summary>
public sealed class DiscountAgreementRun
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AgreementId { get; set; }
    public decimal EligibleSales { get; set; }
    public decimal Percent { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public List<DiscountAgreementRunInvoice> Invoices { get; set; } = [];
}

/// <summary>Hóa đơn điều chỉnh giảm sinh ra cho một hóa đơn gốc trong lần quyết toán.</summary>
public sealed class DiscountAgreementRunInvoice
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OriginalInvoiceId { get; set; }
    public Guid AdjustmentInvoiceId { get; set; }
    public decimal Amount { get; set; }
}
