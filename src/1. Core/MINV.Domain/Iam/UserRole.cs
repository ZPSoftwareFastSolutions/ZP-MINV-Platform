using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Roles asignados a cada usuario.</summary>
public sealed class UserRole : BaseEntity
{
    private UserRole()
    {
    }

    public UserRole(Guid tenantId, Guid userId, Guid roleId)
        : base(tenantId)
    {
        UserId = Guard.NotEmpty(userId, nameof(userId));
        RoleId = Guard.NotEmpty(roleId, nameof(roleId));
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }
}
