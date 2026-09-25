using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Permisos de cada rol.</summary>
public sealed class RolePermission : BaseEntity
{
    private RolePermission()
    {
    }

    public RolePermission(Guid tenantId, Guid roleId, Guid permissionId)
        : base(tenantId)
    {
        RoleId = Guard.NotEmpty(roleId, nameof(roleId));
        PermissionId = Guard.NotEmpty(permissionId, nameof(permissionId));
    }

    public Guid RoleId { get; private set; }

    public Guid PermissionId { get; private set; }
}
