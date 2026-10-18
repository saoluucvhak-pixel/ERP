namespace Hak.Domain.Production;

/// <summary>
/// Đối tượng tính giá thành: a finished item and the work-in-progress account its production costs are gathered on (one WIP
/// account per cost object, e.g. a detail account per product). Finished goods are received during the period at the planned
/// unit cost (Dr inventory / Cr WIP); the period-end run replaces that by the actual cost.
/// </summary>
public sealed class CostObject
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid ItemId { get; set; }
    public Guid WipAccountId { get; set; }
    /// <summary>Planned (provisional) cost per unit used to value finished-goods receipts during the period.</summary>
    public decimal PlannedUnitCost { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

public enum ProductionRunStatus { Active = 1, Reversed = 2 }

/// <summary>
/// Period-end product costing of one cost object: actual cost = WIP balance at period end + value already received
/// (provisional) − closing WIP entered by the user; the difference to the provisional value is spread over the period's
/// finished-goods receipts (in stock → inventory, already issued → COGS). Figures are kept as evidence.
/// </summary>
public sealed class ProductionCostRun
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CostObjectId { get; set; }
    public Guid PeriodId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal WipBalanceBefore { get; set; }
    public decimal ProvisionalValue { get; set; }
    public decimal ClosingWip { get; set; }
    public decimal OutputQuantity { get; set; }
    public decimal ActualCost { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Difference { get; set; }
    public ProductionRunStatus Status { get; set; } = ProductionRunStatus.Active;
    public Guid? VoucherId { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? ReversedAt { get; set; }
    public Guid? ReversedBy { get; set; }
}
