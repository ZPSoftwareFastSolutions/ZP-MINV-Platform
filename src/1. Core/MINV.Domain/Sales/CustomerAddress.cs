using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Direcciones de un cliente.</summary>
public sealed class CustomerAddress : BaseEntity
{
    private CustomerAddress()
    {
    }

    public CustomerAddress(Guid tenantId, Guid customerId, Guid addressId, AddressType addressType, bool isDefault)
        : base(tenantId)
    {
        CustomerId = Guard.NotEmpty(customerId, nameof(customerId));
        AddressId = Guard.NotEmpty(addressId, nameof(addressId));
        AddressType = Guard.Defined(addressType, "El tipo");
        IsDefault = isDefault;
    }

    public Guid CustomerId { get; private set; }

    public Guid AddressId { get; private set; }

    public AddressType AddressType { get; private set; }

    public bool IsDefault { get; private set; }
}
