using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Línea de un ajuste.</summary>
public sealed class StockAdjustmentLine : Entity, IBranchScoped
{
    private StockAdjustmentLine()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public StockAdjustmentLine(Guid tenantId, Guid branchId, Guid stockAdjustmentId, Guid stockLevelId, Guid movementTypeId, decimal quantity)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
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
