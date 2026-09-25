using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Turno de caja del punto de venta: apertura con fondo, movimientos de efectivo y cierre con arqueo.
/// Solo puede haber un turno abierto por caja (índice único parcial en PostgreSQL).</summary>
public sealed class PosSession : Entity, IConcurrencyAware, IAggregateRoot, IBranchScoped
{
    private PosSession()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    private PosSession(Guid tenantId, Guid branchId, Guid posRegisterId, Guid openedByUserId, DateTimeOffset openedAt, decimal openingCash)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        PosRegisterId = Guard.NotEmpty(posRegisterId, nameof(posRegisterId));
        OpenedByUserId = Guard.NotEmpty(openedByUserId, nameof(openedByUserId));
        OpenedAt = openedAt.ToUniversalTime();
        OpeningCash = Guard.NonNegative(openingCash, "El fondo inicial");
        Status = PosSessionStatus.Open;
    }

    public Guid PosRegisterId { get; private set; }

    public Guid OpenedByUserId { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public decimal OpeningCash { get; private set; }

    public Guid? ClosedByUserId { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public decimal? ClosingCashCounted { get; private set; }

    public PosSessionStatus Status { get; private set; }

    public uint RowVersion { get; private set; }

    public static PosSession Open(Guid tenantId, Guid branchId, Guid posRegisterId, Guid userId, decimal openingCash, DateTimeOffset now) =>
        new(tenantId, branchId, posRegisterId, userId, now, openingCash);

    /// <summary>Ingreso o retiro de efectivo (append-only).</summary>
    public CashMovement RegisterCash(CashDirection direction, decimal amount, string reason, Guid userId, DateTimeOffset now)
    {
        EnsureOpen();
        return new CashMovement(TenantId, BranchId, Id, direction, amount, reason, now, userId);
    }

    /// <summary>Cierra el turno con el efectivo contado (arqueo).</summary>
    public void Close(Guid userId, decimal countedCash, DateTimeOffset now)
    {
        EnsureOpen();
        var closedAt = now.ToUniversalTime();
        Guard.That(closedAt >= OpenedAt, "pos.close_date", "El cierre no puede ser anterior a la apertura.");
        ClosingCashCounted = Guard.NonNegative(countedCash, "El efectivo contado");
        ClosedByUserId = Guard.NotEmpty(userId, nameof(userId));
        ClosedAt = closedAt;
        Status = PosSessionStatus.Closed;
    }

    /// <summary>Efectivo esperado en caja: fondo + ventas en efectivo + ingresos − retiros.</summary>
    public decimal ExpectedCash(decimal cashSales, decimal cashIn, decimal cashOut) => OpeningCash + cashSales + cashIn - cashOut;

    /// <summary>Diferencia del arqueo (positiva = sobrante, negativa = faltante).</summary>
    public decimal? CashDifference(decimal expectedCash) => ClosingCashCounted is { } counted ? counted - expectedCash : null;

    private void EnsureOpen() => Guard.That(Status == PosSessionStatus.Open, "pos.closed", "El turno de caja está cerrado.");
}
