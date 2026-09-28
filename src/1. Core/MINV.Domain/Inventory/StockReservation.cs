using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Reserva temporal de stock (bloqueo del POS mientras se cobra, de un pedido o, V6, de una línea de armado
/// reservado desde la tienda web o el escritorio: regla S-03). La crea y cierra <see cref="StockLevel"/>, que mantiene la
/// suma reservada. Origen exclusivo: caja | línea de pedido | línea de armado (a lo sumo uno).</summary>
public sealed class StockReservation : Entity, IConcurrencyAware, IBranchScoped
{
    private StockReservation()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    internal StockReservation(Guid tenantId, Guid branchId, Guid stockLevelId, decimal quantity, DateTimeOffset expiresAt,
        Guid? posSessionId, Guid? salesOrderLineId, Guid? pcBuildLineId = null)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        StockLevelId = Guard.NotEmpty(stockLevelId, nameof(stockLevelId));
        Quantity = Quantities.Round6(Guard.Positive(quantity, "La cantidad"));
        ExpiresAt = expiresAt.ToUniversalTime();
        var origins = (posSessionId is null ? 0 : 1) + (salesOrderLineId is null ? 0 : 1) + (pcBuildLineId is null ? 0 : 1);
        Guard.That(origins <= 1, "reservation.origin",
            "Una reserva pertenece a una sesión de caja, a una línea de pedido o a una línea de armado: solo a uno de ellos.");
        PosSessionId = Guard.NotEmptyIfPresent(posSessionId, nameof(posSessionId));
        SalesOrderLineId = Guard.NotEmptyIfPresent(salesOrderLineId, nameof(salesOrderLineId));
        PcBuildLineId = Guard.NotEmptyIfPresent(pcBuildLineId, nameof(pcBuildLineId));
        Status = ReservationStatus.Active;
    }

    public Guid StockLevelId { get; private set; }

    public decimal Quantity { get; private set; }

    public ReservationStatus Status { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public Guid? PosSessionId { get; private set; }

    public Guid? SalesOrderLineId { get; private set; }

    /// <summary>V6 · Línea del armado reservado (tienda web o escritorio) que originó la reserva.</summary>
    public Guid? PcBuildLineId { get; private set; }

    public uint RowVersion { get; private set; }

    public bool IsExpired(DateTimeOffset now) => Status == ReservationStatus.Active && ExpiresAt <= now;

    internal void MarkConsumed()
    {
        EnsureActive();
        Status = ReservationStatus.Consumed;
    }

    internal void MarkReleased()
    {
        EnsureActive();
        Status = ReservationStatus.Released;
    }

    internal void MarkExpired(DateTimeOffset now)
    {
        EnsureActive();
        Guard.That(ExpiresAt <= now, "reservation.not_expired", "La reserva aún no vence.");
        Status = ReservationStatus.Expired;
    }

    private void EnsureActive() =>
        Guard.That(Status == ReservationStatus.Active, "reservation.closed", "La reserva ya no está activa.");
}
