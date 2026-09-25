using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Ingreso o retiro de efectivo de la caja (append-only).</summary>
public sealed class CashMovement : Entity, IAppendOnly, IBranchScoped
{
    private CashMovement()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public CashMovement(Guid tenantId, Guid branchId, Guid posSessionId, CashDirection direction, decimal amount, string reason, DateTimeOffset occurredAt, Guid recordedByUserId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        PosSessionId = Guard.NotEmpty(posSessionId, nameof(posSessionId));
        Direction = Guard.Defined(direction, "La dirección");
        Amount = Guard.Positive(amount, "El monto");
        Reason = Guard.Text(reason, "El motivo", 200);
        OccurredAt = occurredAt.ToUniversalTime();
        RecordedByUserId = Guard.NotEmpty(recordedByUserId, nameof(recordedByUserId));
    }

    public Guid PosSessionId { get; private set; }

    public CashDirection Direction { get; private set; }

    public decimal Amount { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public Guid RecordedByUserId { get; private set; }
}
