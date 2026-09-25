using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Caja del punto de venta (toma stock de un almacén).</summary>
public sealed class PosRegister : Entity
{
    private PosRegister()
    {
    }

    public PosRegister(Guid tenantId, Guid warehouseId, string code, string name, Guid? hardwareTokenId)
        : base(tenantId)
    {
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
