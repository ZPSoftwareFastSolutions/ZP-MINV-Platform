using MINV.Domain.Common;

namespace MINV.Domain.Accounting;

/// <summary>Historial del costo promedio ponderado por variante y almacén (append-only). V4: cada fila lleva una
/// secuencia por (variante, almacén) con índice único: si dos recepciones concurrentes (compra y transferencia) calculan
/// el promedio sobre el mismo estado, la segunda choca, se deshace y se reintenta con el promedio real.</summary>
/// <remarks>Origen en la V2.1: tblProductos: CostoUnitario.</remarks>
public sealed class AverageCostHistory : Entity, IAppendOnly, IBranchScoped
{
    private AverageCostHistory()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public AverageCostHistory(Guid tenantId, Guid branchId, Guid variantId, Guid warehouseId, int sequence, DateTimeOffset effectiveAt,
        decimal averageCost, Guid? stockMovementId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        WarehouseId = Guard.NotEmpty(warehouseId, nameof(warehouseId));
        Guard.That(sequence >= 1, "cost.sequence", "La secuencia del costo debe ser 1 o mayor.");
        Sequence = sequence;
        EffectiveAt = effectiveAt.ToUniversalTime();
        AverageCost = Guard.NonNegative(averageCost, "El costo promedio");
        StockMovementId = Guard.NotEmptyIfPresent(stockMovementId, nameof(stockMovementId));
    }

    public Guid VariantId { get; private set; }

    public Guid WarehouseId { get; private set; }

    /// <summary>Orden de las filas de la variante en el almacén (1, 2, 3…): el costo vigente es el de mayor secuencia.</summary>
    public int Sequence { get; private set; }

    public DateTimeOffset EffectiveAt { get; private set; }

    public decimal AverageCost { get; private set; }

    public Guid? StockMovementId { get; private set; }
}
