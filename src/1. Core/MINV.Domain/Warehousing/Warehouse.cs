using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Almacén de una sucursal.</summary>
public sealed class Warehouse : Entity
{
    private Warehouse()
    {
    }

    public Warehouse(Guid tenantId, Guid branchId, string code, string name)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        Code = Guard.Code(code, "El código del almacén", 20);
        Name = Guard.Text(name, "El nombre", 100);
        IsActive = true;
    }

    public Guid BranchId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
