using MINV.Domain.Common;

namespace MINV.Domain.Accounting;

/// <summary>Historial del costo promedio ponderado por variante y almacén (append-only).</summary>
/// <remarks>Origen en la V2.1: tblProductos: CostoUnitario.</remarks>
public sealed class AverageCostHistory : Entity, IAppendOnly
{
    private AverageCostHistory()
    {
    }

    public AverageCostHistory(Guid tenantId, Guid variantId, Guid warehouseId, DateTimeOffset effectiveAt, decimal averageCost, Guid? stockMovementId)
        : base(tenantId)
    {
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        WarehouseId = Guard.NotEmpty(warehouseId, nameof(warehouseId));
        EffectiveAt = effectiveAt.ToUniversalTime();
        AverageCost = Guard.NonNegative(averageCost, "El costo promedio");
        StockMovementId = Guard.NotEmptyIfPresent(stockMovementId, nameof(stockMovementId));
    }

    public Guid VariantId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public DateTimeOffset EffectiveAt { get; private set; }

    public decimal AverageCost { get; private set; }

    public Guid? StockMovementId { get; private set; }
}
