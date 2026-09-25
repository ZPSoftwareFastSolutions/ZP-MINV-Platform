using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>
/// Movimiento de inventario: el event store inmutable de M-INV. Solo lo crea <see cref="StockLevel"/>; nunca se
/// actualiza ni se borra (lo impiden EF Core y un trigger de PostgreSQL). Los errores se corrigen con un AJUSTE.
/// No guarda la cantidad con signo: el signo lo da el tipo de movimiento (sin dependencias transitivas).
/// </summary>
/// <remarks>Origen en la V2.1: 10A_ENTRADAS y 10B_SALIDAS (tblEntradas, tblSalidas).</remarks>
public sealed class StockMovement : Entity, IAppendOnly
{
    private StockMovement()
    {
    }

    internal StockMovement(Guid tenantId, Guid stockLevelId, Guid movementTypeId, decimal quantity, MovementContext context)
        : base(tenantId)
    {
        ArgumentNullException.ThrowIfNull(context);
        Id = UuidV7.NewGuid(context.RecordedAt);
        StockLevelId = Guard.NotEmpty(stockLevelId, nameof(stockLevelId));
        MovementTypeId = Guard.NotEmpty(movementTypeId, nameof(movementTypeId));
        Quantity = Quantities.Round6(Guard.Positive(quantity, "La cantidad"));
        var recorded = context.RecordedAt.ToUniversalTime();
        // Tolerancia de 14 h: la fecha de negocio es local y puede ir por delante de la fecha UTC.
        Guard.That(context.BusinessDate.ToDateTime(TimeOnly.MinValue) <= recorded.UtcDateTime.AddHours(14),
            "movement.future_date", "La fecha del movimiento no puede ser futura.");
        BusinessDate = context.BusinessDate;
        RecordedAt = recorded;
        RecordedByUserId = Guard.NotEmpty(context.UserId, "El usuario que registra");
        DocumentReference = Guard.OptionalText(context.DocumentReference, "El documento", 30);
        Notes = Guard.OptionalText(context.Notes, "Las observaciones", 250);
        AdjustmentReasonId = Guard.NotEmptyIfPresent(context.AdjustmentReasonId, nameof(context.AdjustmentReasonId));
        LegacyReference = Guard.OptionalText(context.LegacyReference, "La referencia de la V2.1", 40);
        CorrelationId = Guard.NotEmptyIfPresent(context.CorrelationId, nameof(context.CorrelationId));
    }

    public Guid StockLevelId { get; private set; }

    public Guid MovementTypeId { get; private set; }

    /// <summary>Cantidad siempre positiva; el signo lo aporta <see cref="MovementType.StockFactor"/>.</summary>
    public decimal Quantity { get; private set; }

    public DateOnly BusinessDate { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public string? DocumentReference { get; private set; }

    public string? Notes { get; private set; }

    public Guid? AdjustmentReasonId { get; private set; }

    /// <summary>ID del registro de origen en la V2.1 (trazabilidad de la migración).</summary>
    public string? LegacyReference { get; private set; }

    public Guid? CorrelationId { get; private set; }
}
