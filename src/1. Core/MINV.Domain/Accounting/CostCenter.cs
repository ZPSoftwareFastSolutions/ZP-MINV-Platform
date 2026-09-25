using MINV.Domain.Common;

namespace MINV.Domain.Accounting;

/// <summary>Centro de costo.</summary>
public sealed class CostCenter : Entity
{
    private CostCenter()
    {
    }

    public CostCenter(Guid tenantId, string code, string name, Guid? branchId)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código", 20);
        Name = Guard.Text(name, "El nombre", 80);
        BranchId = Guard.NotEmptyIfPresent(branchId, nameof(branchId));
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid? BranchId { get; private set; }

    public bool IsActive { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
