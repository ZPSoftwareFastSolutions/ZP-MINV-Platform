using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Equipo autorizado (estación o terminal POS) identificado por la huella de su hardware.</summary>
public sealed class HardwareToken : Entity
{
    private HardwareToken()
    {
    }

    public HardwareToken(Guid tenantId, Guid? branchId, string name, HardwareTokenKind kind, string fingerprint, DateTimeOffset registeredAt)
        : base(tenantId)
    {
        BranchId = Guard.NotEmptyIfPresent(branchId, nameof(branchId));
        Name = Guard.Text(name, "El nombre del equipo", 100);
        Kind = Guard.Defined(kind, "El tipo");
        Fingerprint = Guard.Text(fingerprint, "La huella", 128);
        RegisteredAt = registeredAt.ToUniversalTime();
    }

    public Guid? BranchId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public HardwareTokenKind Kind { get; private set; }

    public string Fingerprint { get; private set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    /// <summary>Retira la autorización del equipo (queda el registro para la auditoría).</summary>
    public void Revoke(DateTimeOffset now)
    {
        Guard.That(IsActive, "hardware.revoked", "El equipo ya estaba revocado.");
        RevokedAt = now.ToUniversalTime();
    }
}
