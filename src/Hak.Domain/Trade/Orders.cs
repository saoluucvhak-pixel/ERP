using Hak.Domain.Common;

namespace Hak.Domain.Trade;

public enum OrderStatus { Draft = 1, Confirmed = 2, Closed = 3, Cancelled = 9 }

/// <summary>
/// Purchase or sales order (master prompt §12–13: Đơn mua → Phiếu nhập → Hóa đơn → Công nợ → Thanh toán).
/// Orders carry no accounting entry; they control what may be received/delivered and are the key of the
/// fulfilment report. Stock lines point at an order line; invoices inherit the link through those stock lines.
/// </summary>
public sealed class TradeOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public InvoiceKind Kind { get; set; }
    public string Number { get; set; } = "";
    public Guid PartnerId { get; set; }
    public string? ContractNo { get; set; }
    /// <summary>Hợp đồng mà đơn kế thừa (call-off); đơn trừ lùi số lượng / giá trị còn lại của hợp đồng.</summary>
    public Guid? ContractId { get; set; }
    public DateOnly OrderDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public string? Currency { get; set; }
    public string Description { get; set; } = "";
    /// <summary>Allowed over-delivery in percent of the ordered quantity (bulk goods such as wood chips).</summary>
    public decimal OverDeliveryTolerancePercent { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public string? StatusReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public long RowVersion { get; set; }
    public List<TradeOrderLine> Lines { get; set; } = [];

    public void Confirm(Guid userId, DateTime at, bool requireDistinct)
    {
        if (Status != OrderStatus.Draft) throw Invalid("xác nhận");
        if (requireDistinct && userId == CreatedBy) throw new DomainException(ErrorCodes.SodViolation, "Người xác nhận đơn hàng không được trùng người lập.");
        Status = OrderStatus.Confirmed;
        ConfirmedBy = userId;
        ConfirmedAt = at;
    }

    public void Close(string reason)
    {
        if (Status != OrderStatus.Confirmed) throw Invalid("đóng");
        Require(reason);
        Status = OrderStatus.Closed;
        StatusReason = reason;
    }

    public void Cancel(string reason)
    {
        if (Status is not (OrderStatus.Draft or OrderStatus.Confirmed)) throw Invalid("hủy");
        Require(reason);
        Status = OrderStatus.Cancelled;
        StatusReason = reason;
    }

    private static void Require(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do.");
    }

    private DomainException Invalid(string action) => new(ErrorCodes.InvalidStateTransition, $"Không thể {action} đơn {Number} đang ở trạng thái {Status}.");
}

public sealed class TradeOrderLine
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CompanyId { get; set; }
    public int LineNo { get; set; }
    public Guid ItemId { get; set; }
    /// <summary>Ordered quantity in the item's main unit (MT for wood chips).</summary>
    public decimal Quantity { get; set; }
    public PricingBasis Basis { get; set; } = PricingBasis.Quantity;
    /// <summary>Agreed price per basis unit, in the order currency.</summary>
    public decimal UnitPrice { get; set; }
    public decimal? VatRate { get; set; }
    public string? Description { get; set; }
}
