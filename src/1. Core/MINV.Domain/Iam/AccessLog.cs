using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Intentos de inicio de sesión (append-only).</summary>
public sealed class AccessLog : Entity, IAppendOnly
{
    private AccessLog()
    {
    }

    public AccessLog(Guid tenantId, Guid? userId, string attemptedEmail, bool succeeded, string? failureReason, DateTimeOffset occurredAt, string machineName, Guid? hardwareTokenId)
        : base(tenantId)
    {
        UserId = Guard.NotEmptyIfPresent(userId, nameof(userId));
        AttemptedEmail = Guard.Text(attemptedEmail, "El correo", 254);
        Succeeded = succeeded;
        FailureReason = Guard.OptionalText(failureReason, "El motivo", 200);
        OccurredAt = occurredAt.ToUniversalTime();
        MachineName = Guard.Text(machineName, "El equipo", 100);
        HardwareTokenId = Guard.NotEmptyIfPresent(hardwareTokenId, nameof(hardwareTokenId));
    }

    public Guid? UserId { get; private set; }

    public string AttemptedEmail { get; private set; } = string.Empty;

    public bool Succeeded { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string MachineName { get; private set; } = string.Empty;

    public Guid? HardwareTokenId { get; private set; }
}
