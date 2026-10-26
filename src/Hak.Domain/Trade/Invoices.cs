namespace Hak.Domain.Trade;

public enum InvoiceKind { Purchase = 1, Sales = 2 }

public enum InvoiceStatus { Draft = 1, Posted = 4, Reversed = 5, Cancelled = 9 }

/// <summary>Quantity the unit price applies to. Wood chips are bought and sold per BDMT while the stock ledger runs in MT.</summary>
public enum PricingBasis { Quantity = 1, Bdmt = 2 }

/// <summary>
/// Why the invoice exists. Return/Discount/Increase documents adjust a posted Normal invoice (AdjustsInvoiceId).
/// The legal form of adjustment e-invoices is to be verified against current invoice regulations [CẦN XÁC MINH].
/// </summary>
public enum InvoicePurpose
{
    Normal = 1,
    /// <summary>Hàng bán bị trả lại: goods come back into stock at their original cost; revenue reduced.</summary>
    Return = 2,
    /// <summary>Giảm giá / chiết khấu sau bán: revenue reduced, no stock movement.</summary>
    Discount = 3,
    /// <summary>Điều chỉnh tăng: additional amount on the original sale.</summary>
    Increase = 4,
}

/// <summary>
/// Bản chất của hóa đơn điều chỉnh giảm (Purpose = Discount): chiết khấu thương mại (giảm giá bán cho khách mua nhiều / đạt doanh số — TK giảm trừ doanh thu chiết khấu thương mại)
/// hay giảm giá hàng bán (hàng sai quy cách, kém chất lượng — TK giảm giá hàng bán). Hai loại cùng làm giảm doanh thu và thuế GTGT nhưng báo cáo và tài khoản tách riêng.
/// Bản ghi cũ (null) coi là giảm giá hàng bán.
/// </summary>
public enum DiscountKind { Allowance = 1, Trade = 2 }

/// <summary>Tax treatment follows the economic reason, never just the sign or document type. Unknown legacy rows require review.</summary>
public enum InvoiceAdjustmentReason { Unspecified = 0, InvoiceError = 1, CommercialRebate = 2, GoodsReturn = 3, PriceChange = 4 }

/// <summary>
/// Purchase or sales invoice (master prompt §12–13). Three dates are kept apart: invoice date (on the invoice),
/// the receipt/delivery date (on the linked stock document) and the posting date (accounting period).
/// </summary>
public sealed class CommercialInvoice
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public InvoiceKind Kind { get; set; }
    public InvoicePurpose Purpose { get; set; } = InvoicePurpose.Normal;
    public Guid? AdjustsInvoiceId { get; set; }
    public InvoiceAdjustmentReason AdjustmentReason { get; set; }
    /// <summary>Hóa đơn thay thế: hóa đơn gốc bị thay (đã ghi sổ). Khi hóa đơn này ghi sổ, hóa đơn gốc được đảo trong cùng giao dịch.</summary>
    public Guid? ReplacesInvoiceId { get; set; }
    /// <summary>Chỉ dùng khi Purpose = Discount: chiết khấu thương mại hay giảm giá hàng bán. Null = giảm giá hàng bán (bản ghi cũ).</summary>
    public DiscountKind? DiscountKind { get; set; }
    /// <summary>Lý do cố ý lập hóa đơn từ phiếu kho của đối tượng khác (người lập có quyền invoice.partner_override); null = không có ngoại lệ. Kiểm lại lúc ghi sổ.</summary>
    public string? PartnerOverrideReason { get; set; }
    /// <summary>Internal document number (HDM/HDB…); the legal invoice identity is Series + InvoiceNo.</summary>
    public string Number { get; set; } = "";
    public string InvoiceSeries { get; set; } = "";
    public string InvoiceNo { get; set; } = "";
    public DateOnly InvoiceDate { get; set; }
    public DateOnly PostingDate { get; set; }
    public DateOnly? DueDate { get; set; }
    /// <summary>Chiết khấu thanh toán: trả đủ trong N ngày kể từ ngày hóa đơn thì được giảm P% tiền hàng (chưa thuế). Điều khoản ghi lúc lập hóa đơn.</summary>
    public decimal? PaymentDiscountPercent { get; set; }
    public int? PaymentDiscountDays { get; set; }
    /// <summary>Snapshot phần mở rộng điều khoản (cơ sở tính, dung sai, bậc phụ) lúc lập hóa đơn.</summary>
    public string? PaymentDiscountOptions { get; set; }
    public Guid PartnerId { get; set; }
    /// <summary>ISO currency of the prices; functional currency when null.</summary>
    public string? Currency { get; set; }
    public decimal? ExchangeRate { get; set; }
    public string Description { get; set; } = "";
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public Guid? VoucherId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? PostedBy { get; set; }
    public DateTime? PostedAt { get; set; }
    public long RowVersion { get; set; }
    public List<CommercialInvoiceLine> Lines { get; set; } = [];
}

public sealed class CommercialInvoiceLine
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid CompanyId { get; set; }
    public int LineNo { get; set; }
    /// <summary>Line of the original invoice this adjustment line refers to.</summary>
    public int? AdjustsLineNo { get; set; }
    public Guid? ItemId { get; set; }
    /// <summary>Linked stock document line (receipt for purchases, issue for sales).</summary>
    public Guid? StockDocumentId { get; set; }
    public int? StockLineNo { get; set; }
    /// <summary>Stock quantity (main unit) this line covers from the linked stock line.</summary>
    public decimal? StockQuantity { get; set; }
    /// <summary>Expense/revenue account for lines without stock (services) or to override the default.</summary>
    public Guid? AccountId { get; set; }
    public PricingBasis Basis { get; set; } = PricingBasis.Quantity;
    /// <summary>Quantity × price base: stock quantity, or BDMT from the active measurement of the linked line.</summary>
    public decimal BasisQuantity { get; set; }
    /// <summary>Price in invoice currency per basis unit.</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>
    /// Chiết khấu thương mại GHI TRÊN HÓA ĐƠN: tỷ lệ % (null nếu nhập số tiền). Số tiền hàng sau chiết khấu là <see cref="Amount"/>; mọi nơi dùng Amount/FunctionalAmount
    /// (doanh thu, thuế, công nợ, giá nhập kho) đều tính trên số đã trừ chiết khấu. Tổng trước chiết khấu = Amount + DiscountAmount.
    /// </summary>
    public decimal? DiscountPercent { get; set; }
    /// <summary>Số tiền chiết khấu trên hóa đơn, theo tiền hóa đơn (0 nếu không có).</summary>
    public decimal DiscountAmount { get; set; }
    /// <summary>Số tiền chiết khấu trên hóa đơn, theo tiền hạch toán.</summary>
    public decimal FunctionalDiscountAmount { get; set; }
    /// <summary>Amount in invoice currency, AFTER the on-invoice trade discount (= round(BasisQuantity × UnitPrice) − DiscountAmount).</summary>
    public decimal Amount { get; set; }
    /// <summary>Amount in functional currency.</summary>
    public decimal FunctionalAmount { get; set; }
    /// <summary>VAT rate in percent; null = not subject to VAT. Rates are company configuration [CẦN XÁC MINH].</summary>
    public decimal? VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public string? Description { get; set; }
    /// <summary>Order line covered (inherited from the stock line, or set directly for service lines).</summary>
    public Guid? OrderId { get; set; }
    public int? OrderLineNo { get; set; }
}
