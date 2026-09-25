using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Usuarios habilitados en cada sucursal.</summary>
public sealed class BranchUser : BaseEntity
{
    private BranchUser()
    {
    }

    public BranchUser(Guid tenantId, Guid branchId, Guid userId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        UserId = Guard.NotEmpty(userId, nameof(userId));
    }

    public Guid BranchId { get; private set; }

    public Guid UserId { get; private set; }
}
