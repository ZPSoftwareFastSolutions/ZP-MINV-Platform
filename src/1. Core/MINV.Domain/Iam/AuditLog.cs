using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Auditoría inmutable de cada comando (sucesora de 14_ACTIVIDAD de la V2.1).</summary>
/// <remarks>Origen en la V2.1: 14_ACTIVIDAD (tblActividad): ID, Timestamp, Usuario_O365, Script, Resultado, Detalle.</remarks>
public sealed class AuditLog : Entity, IAppendOnly
{
    private AuditLog()
    {
    }

    public AuditLog(Guid tenantId, Guid? userId, DateTimeOffset occurredAt, string action, AuditOutcome outcome, string? entityType, Guid? entityId, string? details, Guid correlationId, string? legacyReference)
        : base(tenantId)
    {
        UserId = Guard.NotEmptyIfPresent(userId, nameof(userId));
        OccurredAt = occurredAt.ToUniversalTime();
        Action = Guard.Text(action, "La acción", 100);
        Outcome = Guard.Defined(outcome, "El resultado");
        EntityType = Guard.OptionalText(entityType, "El tipo de entidad", 100);
        EntityId = Guard.NotEmptyIfPresent(entityId, nameof(entityId));
        Details = details;
        CorrelationId = Guard.NotEmpty(correlationId, nameof(correlationId));
        LegacyReference = Guard.OptionalText(legacyReference, "La referencia de la V2.1", 40);
    }

    public Guid? UserId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public AuditOutcome Outcome { get; private set; }

    public string? EntityType { get; private set; }

    public Guid? EntityId { get; private set; }

    public string? Details { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string? LegacyReference { get; private set; }
}
