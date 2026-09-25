using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Lista de precios.</summary>
public sealed class PriceList : Entity
{
    private PriceList()
    {
    }

    public PriceList(Guid tenantId, string name, Guid currencyId, DateOnly validFrom, DateOnly? validTo, bool isDefault)
        : base(tenantId)
    {
        Name = Guard.Text(name, "El nombre", 80);
        CurrencyId = Guard.NotEmpty(currencyId, nameof(currencyId));
        ValidFrom = validFrom;
        ValidTo = validTo;
        IsDefault = isDefault;
    }

    public string Name { get; private set; } = string.Empty;

    public Guid CurrencyId { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }

    public bool IsDefault { get; private set; }
}
