using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Sesión de trabajo en el cliente de escritorio. V4: guarda la sucursal activa (sin filtro por sucursal: la
/// sesión es de la empresa) y, en modo nube, el hash del token con el que el servidor reconoce al escritorio.</summary>
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

    /// <summary>V4 · Sucursal activa (null = todas, gerencia global).</summary>
    public Guid? ActiveBranchId { get; private set; }

    /// <summary>V4 · SHA-256 del token de sesión del servidor en la nube (null en conexión directa local).</summary>
    public string? TokenHash { get; private set; }

    /// <summary>V4 · Vencimiento del token (se renueva con la actividad).</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public bool IsOpen => EndedAt is null;

    public bool IsValidAt(DateTimeOffset now) => IsOpen && (ExpiresAt is null || ExpiresAt > now);

    /// <summary>V4 · Cambia la sucursal activa (el alcance lo valida quien llama).</summary>
    public void SelectBranch(Guid? branchId)
    {
        Guard.That(IsOpen, "session.closed", "La sesión ya terminó.");
        ActiveBranchId = Guard.NotEmptyIfPresent(branchId, nameof(branchId));
    }

    /// <summary>V4 · Emite el token del servidor en la nube (solo se guarda su hash).</summary>
    public void IssueToken(string tokenHash, DateTimeOffset expiresAt)
    {
        Guard.That(tokenHash is { Length: 64 } && tokenHash.All(char.IsAsciiHexDigit), "session.token", "Hash de token inválido.");
        Guard.That(expiresAt > StartedAt, "session.expiry", "El vencimiento debe ser posterior al inicio.");
        TokenHash = tokenHash.ToLowerInvariant();
        ExpiresAt = expiresAt.ToUniversalTime();
    }

    /// <summary>V4 · Renueva el vencimiento deslizante del token.</summary>
    public void Extend(DateTimeOffset expiresAt)
    {
        Guard.That(IsOpen && TokenHash is not null, "session.token", "La sesión no tiene token.");
        if (expiresAt > ExpiresAt)
        {
            ExpiresAt = expiresAt.ToUniversalTime();
        }
    }

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
