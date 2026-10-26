namespace Hak.Domain.Trade;

/// <summary>
/// Thỏa thuận chiết khấu thương mại theo doanh số (chiết khấu cuối kỳ / rebate), cho một đối tượng trong một khoảng ngày. Chiều bán: khách đạt doanh số thì được giảm;
/// chiều mua: nhà cung cấp giảm cho công ty khi mua đạt mức. Doanh số tính theo tiền hàng chưa thuế của hóa đơn thường đã ghi sổ trong khoảng ngày, trừ hàng trả lại / giảm giá
/// đã ghi sổ. Mặc định bậc chiết khấu áp cho TOÀN BỘ doanh số trong kỳ (hồi tố); có thể chọn lũy tiến từng phần (Progressive). Quyết toán sinh hóa đơn điều chỉnh giảm (nháp) chia theo từng hóa đơn gốc để thuế GTGT
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
    /// <summary>false = hồi tố: bậc đạt áp cho TOÀN BỘ doanh số; true = lũy tiến từng phần: mỗi phần doanh số thuộc một bậc nhận tỷ lệ của bậc đó (bậc k phủ [ngưỡng k; ngưỡng k+1), không hở không chồng).</summary>
    public bool Progressive { get; set; }
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
    /// <summary>Quyết toán bằng hạch toán điều chỉnh giảm (không phát hành hóa đơn điều chỉnh): chứng từ kế toán sinh ra; null = quyết toán bằng hóa đơn điều chỉnh.</summary>
    public Guid? VoucherId { get; set; }
    /// <summary>Số đã trích trước (ghi sổ) tại thời điểm quyết toán; quyết toán bằng chứng từ bù trừ số này, bằng hóa đơn điều chỉnh thì có chứng từ hoàn nhập <see cref="AccrualReversalVoucherId"/>.</summary>
    public decimal AccruedAmount { get; set; }
    public Guid? AccrualReversalVoucherId { get; set; }
    /// <summary>Immutable settlement statement, preview and allocation policy. Null means a legacy run whose detail cannot be reconstructed safely.</summary>
    public string? SettlementSnapshot { get; set; }
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

/// <summary>
/// Trích trước chiết khấu thương mại theo doanh số (chiều bán): ước tính theo doanh số đến ngày <see cref="AsOf"/> với bậc đang đạt; mỗi lần trích là một chứng từ điều chỉnh
/// (Nợ giảm trừ doanh thu / Có phải trả chiết khấu, âm = hoàn nhập). Tổng đã trích = tổng <see cref="Adjustment"/> của các chứng từ đã ghi sổ.
/// </summary>
public sealed class DiscountAccrual
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AgreementId { get; set; }
    public DateOnly AsOf { get; set; }
    public decimal Estimated { get; set; }
    public decimal Adjustment { get; set; }
    public Guid VoucherId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

