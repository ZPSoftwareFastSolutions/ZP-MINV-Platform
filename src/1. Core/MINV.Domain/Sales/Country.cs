using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>País.</summary>
public sealed class Country : Entity
{
    private Country()
    {
    }

    public Country(Guid tenantId, string isoCode, string name)
        : base(tenantId)
    {
        IsoCode = Guard.Code(isoCode, "El código ISO", 2);
        Name = Guard.Text(name, "El nombre", 80);
    }

    public string IsoCode { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
}
