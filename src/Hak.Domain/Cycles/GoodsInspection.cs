namespace Hak.Domain.Cycles;

/// <summary>
/// Biên bản kiểm nghiệm hàng nhận (tệp phân tích ERP, mục V.2.2): ghi số lượng thực nhận đạt yêu cầu của từng dòng phiếu nhập kho.
/// Không ghi Nhật ký. Làm căn cứ đối chiếu hóa đơn mua (hóa đơn – đơn đặt hàng – phiếu nhập – biên bản kiểm nghiệm) trước khi chấp nhận thanh toán.
/// Phần không đạt (nhận − đạt) xử lý bằng hàng mua trả lại hoặc giảm giá hàng mua đã có.
/// </summary>
public sealed class GoodsInspection
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>BBKN{năm TC}-{số}.</summary>
    public string Number { get; set; } = "";
    public Guid StockDocumentId { get; set; }
    public Guid? PartnerId { get; set; }
    public DateOnly InspectionDate { get; set; }
    /// <summary>Người hoặc ban kiểm nghiệm.</summary>
    public string Inspector { get; set; } = "";
    public string? Conclusion { get; set; }
    public bool IsCancelled { get; set; }
    public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public long RowVersion { get; set; }
    public List<GoodsInspectionLine> Lines { get; set; } = [];
}

public sealed class GoodsInspectionLine
{
    public Guid Id { get; set; }
    public Guid InspectionId { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>Số dòng của phiếu nhập kho được kiểm nghiệm.</summary>
    public int StockLineNo { get; set; }
    public Guid ItemId { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal AcceptedQty { get; set; }
    public string? Note { get; set; }
}
