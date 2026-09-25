using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Zona de un almacén.</summary>
public sealed class Zone : Entity
{
    private Zone()
    {
    }

    public Zone(Guid tenantId, Guid warehouseId, string code, string name, Guid? locationTypeId)
        : base(tenantId)
    {
        WarehouseId = Guard.NotEmpty(warehouseId, nameof(warehouseId));
        Code = Guard.Code(code, "El código de la zona", 20);
        Name = Guard.Text(name, "El nombre", 80);
        LocationTypeId = Guard.NotEmptyIfPresent(locationTypeId, nameof(locationTypeId));
    }

    public Guid WarehouseId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid? LocationTypeId { get; private set; }
}
