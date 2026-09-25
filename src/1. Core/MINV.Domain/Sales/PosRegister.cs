using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Caja del punto de venta (toma stock de un almacén).</summary>
public sealed class PosRegister : Entity, IBranchScoped
{
    private PosRegister()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public PosRegister(Guid tenantId, Guid branchId, Guid warehouseId, string code, string name, Guid? hardwareTokenId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        WarehouseId = Guard.NotEmpty(warehouseId, nameof(warehouseId));
        Code = Guard.Code(code, "El código de la caja", 20);
        Name = Guard.Text(name, "El nombre", 80);
        HardwareTokenId = Guard.NotEmptyIfPresent(hardwareTokenId, nameof(hardwareTokenId));
        IsActive = true;
    }

    public Guid WarehouseId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid? HardwareTokenId { get; private set; }

    public bool IsActive { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
