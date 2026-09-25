using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Línea de un ajuste.</summary>
public sealed class StockAdjustmentLine : Entity
{
    private StockAdjustmentLine()
    {
    }

    public StockAdjustmentLine(Guid tenantId, Guid stockAdjustmentId, Guid stockLevelId, Guid movementTypeId, decimal quantity)
        : base(tenantId)
    {
        StockAdjustmentId = Guard.NotEmpty(stockAdjustmentId, nameof(stockAdjustmentId));
        StockLevelId = Guard.NotEmpty(stockLevelId, nameof(stockLevelId));
        MovementTypeId = Guard.NotEmpty(movementTypeId, nameof(movementTypeId));
        Quantity = Quantities.Round6(Guard.Positive(quantity, "La cantidad"));
    }

    public Guid StockAdjustmentId { get; private set; }

    public Guid StockLevelId { get; private set; }

    public Guid MovementTypeId { get; private set; }

    public decimal Quantity { get; private set; }

    public Guid? StockMovementId { get; private set; }
}
