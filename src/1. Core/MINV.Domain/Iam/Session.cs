using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Sesión de trabajo en el cliente de escritorio.</summary>
public sealed class Session : Entity, IConcurrencyAware
{
    private Session()
    {
    }

    public Session(Guid tenantId, Guid userId, Guid? hardwareTokenId, DateTimeOffset startedAt, string machineName, string clientVersion)
        : base(tenantId)
    {
        UserId = Guard.NotEmpty(userId, nameof(userId));
        HardwareTokenId = Guard.NotEmptyIfPresent(hardwareTokenId, nameof(hardwareTokenId));
        StartedAt = startedAt.ToUniversalTime();
        MachineName = Guard.Text(machineName, "El equipo", 100);
        ClientVersion = Guard.Text(clientVersion, "La versión del cliente", 30);
        LastSeenAt = StartedAt;
    }

    public Guid UserId { get; private set; }

    public Guid? HardwareTokenId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public string MachineName { get; private set; } = string.Empty;

    public string ClientVersion { get; private set; } = string.Empty;

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public bool IsOpen => EndedAt is null;

    public void Touch(DateTimeOffset now)
    {
        Guard.That(IsOpen, "session.closed", "La sesión ya terminó.");
        LastSeenAt = now.ToUniversalTime();
    }

    public void End(DateTimeOffset now)
    {
        Guard.That(IsOpen, "session.closed", "La sesión ya terminó.");
        EndedAt = now.ToUniversalTime();
        LastSeenAt = EndedAt.Value;
    }
}
