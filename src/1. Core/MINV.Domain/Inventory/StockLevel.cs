using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>
/// Existencia de un lote en una posición: el estado materializado del inventario. Es la raíz del agregado de stock:
/// toda variación pasa por aquí, genera un <see cref="StockMovement"/> inmutable y está protegida por control de
/// concurrencia optimista (<see cref="RowVersion"/> = xmin): dos cajas que venden la última unidad a la vez no pueden
/// dejar el stock en negativo; la segunda transacción se aborta y se reintenta con el valor real.
/// </summary>
/// <remarks>En la V2.1 el stock era una instantánea (15_STOCK) recalculada a demanda; en la V3 es transaccional.</remarks>
public sealed class StockLevel : Entity, IConcurrencyAware, IAggregateRoot, IBranchScoped
{
    private StockLevel()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    private StockLevel(Guid tenantId, Guid branchId, Guid binId, Guid batchId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        BinId = Guard.NotEmpty(binId, nameof(binId));
        BatchId = Guard.NotEmpty(batchId, nameof(batchId));
    }

    public Guid BinId { get; private set; }

    public Guid BatchId { get; private set; }

    public decimal QuantityOnHand { get; private set; }

    public decimal QuantityReserved { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>Disponible para vender o despachar: existencia menos reservas.</summary>
    public decimal Available => Quantities.Round6(QuantityOnHand - QuantityReserved);

    /// <summary>Abre una existencia vacía de un lote en una posición.</summary>
    public static StockLevel Open(Guid tenantId, Guid branchId, Guid binId, Guid batchId) => new(tenantId, branchId, binId, batchId);

    /// <summary>
    /// Registra un movimiento. Reglas (las mismas de la captura y de los Office Scripts de la V2.1):
    /// cantidad &gt; 0; decimales solo si la unidad los admite; observación obligatoria si el tipo la exige; y
    /// <b>poka-yoke</b>: una salida nunca puede superar el disponible (el stock no queda negativo).
    /// </summary>
    public StockMovement Register(MovementType type, decimal quantity, UnitRule unit, MovementContext context)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(context);
        EnsureSameTenant(type, "El tipo de movimiento");
        var q = Quantities.Round6(Guard.Positive(quantity, "La cantidad"));
        Quantities.EnsureAllowed(q, unit.AllowsDecimals, unit.UnitCode);
        if (type.RequiresNotes && string.IsNullOrWhiteSpace(context.Notes))
        {
            throw new DomainException("movement.notes_required", $"{type.Name} exige una observación (motivo).");
        }
        if (!type.Increases && q > Available)
        {
            throw new InsufficientStockException(Available, q);
        }
        QuantityOnHand = Quantities.Round6(QuantityOnHand + type.Signed(q));
        return new StockMovement(TenantId, BranchId, Id, type.Id, q, context);
    }

    /// <summary>El SALDO INICIAL se admite una sola vez: en una existencia sin movimientos.</summary>
    public static void EnsureInitialBalanceAllowed(MovementType type, bool hasMovements)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.IsInitialBalance && hasMovements)
        {
            throw new DomainException("movement.initial_balance_repeated",
                "La existencia ya tiene movimientos: use un AJUSTE en lugar de otro SALDO INICIAL.");
        }
    }

    /// <summary>Reserva stock (carrito del POS o pedido): baja el disponible sin mover la existencia.</summary>
    public StockReservation Reserve(decimal quantity, DateTimeOffset expiresAt, DateTimeOffset now,
        Guid? posSessionId = null, Guid? salesOrderLineId = null)
    {
        var q = Quantities.Round6(Guard.Positive(quantity, "La cantidad a reservar"));
        Guard.That(expiresAt > now, "reservation.expiry", "La reserva debe vencer en el futuro.");
        if (q > Available)
        {
            throw new InsufficientStockException(Available, q);
        }
        QuantityReserved = Quantities.Round6(QuantityReserved + q);
        return new StockReservation(TenantId, BranchId, Id, q, expiresAt, posSessionId, salesOrderLineId);
    }

    public void Release(StockReservation reservation)
    {
        EnsureOwn(reservation);
        reservation.MarkReleased();
        QuantityReserved = Quantities.Round6(QuantityReserved - reservation.Quantity);
    }

    public void Expire(StockReservation reservation, DateTimeOffset now)
    {
        EnsureOwn(reservation);
        reservation.MarkExpired(now);
        QuantityReserved = Quantities.Round6(QuantityReserved - reservation.Quantity);
    }

    /// <summary>Convierte una reserva en salida (la venta se concreta).</summary>
    public StockMovement Consume(StockReservation reservation, MovementType issueType, UnitRule unit, MovementContext context)
    {
        ArgumentNullException.ThrowIfNull(issueType);
        EnsureOwn(reservation);
        Guard.That(!issueType.Increases, "reservation.consume_type", "Una reserva solo se consume con un tipo de salida.");
        reservation.MarkConsumed();
        QuantityReserved = Quantities.Round6(QuantityReserved - reservation.Quantity);
        return Register(issueType, reservation.Quantity, unit, context);
    }

    /// <summary>
    /// Toma física: lleva la existencia a la cantidad contada. Sin movimientos previos registra SALDO INICIAL; si no,
    /// AJUSTE (+) o AJUSTE (-) por la diferencia. Devuelve <c>null</c> si cuadra (V2.1: GenerarAjustesConteo).
    /// </summary>
    public StockMovement? AdjustToCount(decimal countedQuantity, CountMovementTypes types, bool hasPriorMovements,
        UnitRule unit, MovementContext context)
    {
        ArgumentNullException.ThrowIfNull(types);
        var counted = Quantities.Round6(Guard.NonNegative(countedQuantity, "La cantidad contada"));
        Quantities.EnsureAllowed(counted, unit.AllowsDecimals, unit.UnitCode);
        if (!hasPriorMovements)
        {
            return counted > 0 ? Register(types.InitialBalance, counted, unit, context) : null;
        }
        var difference = Quantities.Round6(counted - QuantityOnHand);
        if (difference == 0)
        {
            return null;
        }
        return difference > 0
            ? Register(types.AdjustmentIn, difference, unit, context)
            : Register(types.AdjustmentOut, -difference, unit, context);
    }

    private void EnsureOwn(StockReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        Guard.That(reservation.StockLevelId == Id, "reservation.other_level", "La reserva pertenece a otra existencia.");
    }
}
