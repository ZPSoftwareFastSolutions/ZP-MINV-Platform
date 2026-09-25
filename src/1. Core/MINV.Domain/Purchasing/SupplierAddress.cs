using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Direcciones de un proveedor.</summary>
public sealed class SupplierAddress : BaseEntity
{
    private SupplierAddress()
    {
    }

    public SupplierAddress(Guid tenantId, Guid supplierId, Guid addressId, AddressType addressType)
        : base(tenantId)
    {
        SupplierId = Guard.NotEmpty(supplierId, nameof(supplierId));
        AddressId = Guard.NotEmpty(addressId, nameof(addressId));
        AddressType = Guard.Defined(addressType, "El tipo");
    }

    public Guid SupplierId { get; private set; }

    public Guid AddressId { get; private set; }

    public AddressType AddressType { get; private set; }
}
