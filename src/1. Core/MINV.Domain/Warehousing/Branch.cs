using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Sucursal. V4: dimensión corporativa de la empresa (la ven todas las sesiones); lo que ocurre en ella
/// (existencias, movimientos, ventas, compras, caja, asientos) lleva su <c>branch_id</c> y se filtra por el alcance.</summary>
/// <remarks>Origen en la V2.1: 01_CONFIG: cfgBodega.</remarks>
public sealed class Branch : Entity
{
    private Branch()
    {
    }

    public Branch(Guid tenantId, string code, string name, Guid? addressId)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código de la sucursal", 20);
        Name = Guard.Text(name, "El nombre", 100);
        AddressId = Guard.NotEmptyIfPresent(addressId, nameof(addressId));
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid? AddressId { get; private set; }

    public bool IsActive { get; private set; }

    public void Rename(string name) => Name = Guard.Text(name, "El nombre", 100);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
