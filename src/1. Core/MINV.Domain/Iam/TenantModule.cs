using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Módulos que la empresa tiene licenciados (habilitan funciones: POS y hardware, RBAC…).</summary>
public sealed class TenantModule : BaseEntity
{
    private TenantModule()
    {
    }

    public TenantModule(Guid tenantId, Guid moduleId, DateTimeOffset activatedAt, DateTimeOffset? expiresAt)
        : base(tenantId)
    {
        ModuleId = Guard.NotEmpty(moduleId, nameof(moduleId));
        ActivatedAt = activatedAt.ToUniversalTime();
        ExpiresAt = expiresAt?.ToUniversalTime();
        IsActive = true;
    }

    public Guid ModuleId { get; private set; }

    public DateTimeOffset ActivatedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public bool IsActive { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
