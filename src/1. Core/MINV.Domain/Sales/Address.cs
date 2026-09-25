using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Dirección normalizada (calle → código postal → ciudad → estado → país).</summary>
public sealed class Address : Entity
{
    private Address()
    {
    }

    public Address(Guid tenantId, Guid postalCodeId, string street, string? reference)
        : base(tenantId)
    {
        PostalCodeId = Guard.NotEmpty(postalCodeId, nameof(postalCodeId));
        Street = Guard.Text(street, "La calle", 200);
        Reference = Guard.OptionalText(reference, "La referencia", 200);
    }

    public Guid PostalCodeId { get; private set; }

    public string Street { get; private set; } = string.Empty;

    public string? Reference { get; private set; }
}
