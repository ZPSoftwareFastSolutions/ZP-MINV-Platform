using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>
/// V4 · Línea de una transferencia: variante y cantidad. Lo recibido NO se guarda como columna (sería derivable): sale
/// de los faltantes (<see cref="StockTransferDiscrepancy"/>). El costo unitario es el promedio del origen al despachar (un
/// hecho). El manifiesto (<see cref="StockTransferLineBatch"/>) dice qué lotes viajan: lo ven origen y destino (los
/// movimientos de cada sucursal no son visibles para la otra) y así la recepción entra con los mismos lotes.
/// </summary>
public sealed class StockTransferLine : Entity, IInterBranch
{
    private readonly List<StockTransferMovement> _movements = new();
    private readonly List<StockTransferDiscrepancy> _discrepancies = new();
    private readonly List<StockTransferLineBatch> _batches = new();

    private StockTransferLine()
    {
    }

    internal StockTransferLine(Guid tenantId, Guid fromBranchId, Guid toBranchId, Guid stockTransferId, Guid variantId, decimal quantity)
        : base(tenantId)
    {
        FromBranchId = Guard.NotEmpty(fromBranchId, nameof(fromBranchId));
        ToBranchId = Guard.NotEmpty(toBranchId, nameof(toBranchId));
        StockTransferId = Guard.NotEmpty(stockTransferId, nameof(stockTransferId));
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        Quantity = Quantities.Round6(Guard.Positive(quantity, "La cantidad"));
    }

    public Guid FromBranchId { get; private set; }

    public Guid ToBranchId { get; private set; }

    public Guid StockTransferId { get; private set; }

    public Guid VariantId { get; private set; }

    /// <summary>Cantidad solicitada = despachada (el despacho es completo).</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Costo promedio del origen al despachar (null mientras está pendiente).</summary>
    public decimal? UnitCost { get; private set; }

    public IReadOnlyCollection<StockTransferMovement> Movements => _movements;

    public IReadOnlyCollection<StockTransferDiscrepancy> Discrepancies => _discrepancies;

    /// <summary>Manifiesto de despacho: cantidad de cada lote que viaja.</summary>
    public IReadOnlyCollection<StockTransferLineBatch> Batches => _batches;

    /// <summary>Faltante total registrado al recibir.</summary>
    public decimal Shortage => Quantities.Round6(_discrepancies.Sum(d => d.Quantity));

    /// <summary>Recibido = despachado − faltantes (válido cuando la transferencia está recibida).</summary>
    public decimal ReceivedQuantity => Quantities.Round6(Quantity - Shortage);

    internal void Link(Guid stockMovementId, TransferDirection direction, Guid branchId)
    {
        Guard.That(_movements.All(m => m.StockMovementId != stockMovementId), "transfer.movement_linked", "El movimiento ya está vinculado.");
        _movements.Add(new StockTransferMovement(TenantId, branchId, Id, stockMovementId, direction));
    }

    internal void SetUnitCost(decimal unitCost)
    {
        Guard.That(UnitCost is null, "transfer.cost_set", "El costo de la línea ya fue fijado al despachar.");
        UnitCost = Guard.NonNegative(unitCost, "El costo unitario");
    }

    internal void AddDiscrepancy(StockTransferDiscrepancy discrepancy) => _discrepancies.Add(discrepancy);

    internal void Ship(Guid batchId, decimal quantity)
    {
        var existing = _batches.FirstOrDefault(b => b.BatchId == batchId);
        Guard.That(existing is null, "transfer.batch_twice", "El lote ya está en el manifiesto de la línea.");
        _batches.Add(new StockTransferLineBatch(TenantId, FromBranchId, ToBranchId, Id, batchId, quantity));
    }
}

/// <summary>V4 · Manifiesto de despacho (append-only): lote y cantidad que viajan en una línea. Visible para origen y
/// destino; la recepción no puede ingresar de un lote más de lo que viajó.</summary>
public sealed class StockTransferLineBatch : BaseEntity, IInterBranch, IAppendOnly
{
    private StockTransferLineBatch()
    {
    }

    internal StockTransferLineBatch(Guid tenantId, Guid fromBranchId, Guid toBranchId, Guid transferLineId, Guid batchId, decimal quantity)
        : base(tenantId)
    {
        FromBranchId = Guard.NotEmpty(fromBranchId, nameof(fromBranchId));
        ToBranchId = Guard.NotEmpty(toBranchId, nameof(toBranchId));
        TransferLineId = Guard.NotEmpty(transferLineId, nameof(transferLineId));
        BatchId = Guard.NotEmpty(batchId, nameof(batchId));
        Quantity = Quantities.Round6(Guard.Positive(quantity, "La cantidad del lote"));
    }

    public Guid FromBranchId { get; private set; }

    public Guid ToBranchId { get; private set; }

    public Guid TransferLineId { get; private set; }

    public Guid BatchId { get; private set; }

    public decimal Quantity { get; private set; }
}

/// <summary>V4 · Vínculo (append-only) entre una línea y un movimiento de stock: salida en el origen o entrada en el
/// destino. La sucursal del vínculo es la del movimiento (FK compuesta: no puede apuntar a otra sucursal).</summary>
public sealed class StockTransferMovement : BaseEntity, IBranchScoped, IAppendOnly
{
    private StockTransferMovement()
    {
    }

    internal StockTransferMovement(Guid tenantId, Guid branchId, Guid transferLineId, Guid stockMovementId, TransferDirection direction)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        TransferLineId = Guard.NotEmpty(transferLineId, nameof(transferLineId));
        StockMovementId = Guard.NotEmpty(stockMovementId, nameof(stockMovementId));
        Direction = Guard.Defined(direction, "La dirección");
    }

    public Guid BranchId { get; private set; }

    public Guid TransferLineId { get; private set; }

    public Guid StockMovementId { get; private set; }

    public TransferDirection Direction { get; private set; }
}

/// <summary>V4 · Faltante al recibir (delta compensatorio append-only): lo despachado que no llegó, con su motivo. Lo
/// registra el destino y lo ven ambas sucursales.</summary>
public sealed class StockTransferDiscrepancy : Entity, IInterBranch, IAppendOnly
{
    private StockTransferDiscrepancy()
    {
    }

    internal StockTransferDiscrepancy(Guid tenantId, Guid fromBranchId, Guid toBranchId, Guid transferLineId, decimal quantity, string reason,
        Guid userId, DateTimeOffset recordedAt)
        : base(tenantId)
    {
        FromBranchId = Guard.NotEmpty(fromBranchId, nameof(fromBranchId));
        ToBranchId = Guard.NotEmpty(toBranchId, nameof(toBranchId));
        TransferLineId = Guard.NotEmpty(transferLineId, nameof(transferLineId));
        Quantity = Quantities.Round6(Guard.Positive(quantity, "El faltante"));
        Reason = Guard.Text(reason, "El motivo del faltante", 200);
        RecordedByUserId = Guard.NotEmpty(userId, nameof(userId));
        RecordedAt = recordedAt.ToUniversalTime();
    }

    public Guid FromBranchId { get; private set; }

    /// <summary>Sucursal que recibió (y registró el faltante).</summary>
    public Guid ToBranchId { get; private set; }

    public Guid TransferLineId { get; private set; }

    public decimal Quantity { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public Guid RecordedByUserId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}

/// <summary>V4 · Bitácora append-only de la máquina de estados (quién, cuándo y a qué estado).</summary>
public sealed class StockTransferEvent : Entity, IInterBranch, IAppendOnly
{
    private StockTransferEvent()
    {
    }

    internal StockTransferEvent(Guid tenantId, Guid fromBranchId, Guid toBranchId, Guid transferId, TransferStatus status, Guid userId,
        DateTimeOffset occurredAt, string detail)
        : base(tenantId)
    {
        FromBranchId = Guard.NotEmpty(fromBranchId, nameof(fromBranchId));
        ToBranchId = Guard.NotEmpty(toBranchId, nameof(toBranchId));
        TransferId = Guard.NotEmpty(transferId, nameof(transferId));
        Status = Guard.Defined(status, "El estado");
        UserId = Guard.NotEmpty(userId, nameof(userId));
        OccurredAt = occurredAt.ToUniversalTime();
        Detail = Guard.Text(detail, "El detalle", 250);
    }

    public Guid FromBranchId { get; private set; }

    public Guid ToBranchId { get; private set; }

    public Guid TransferId { get; private set; }

    public TransferStatus Status { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string Detail { get; private set; } = string.Empty;
}
