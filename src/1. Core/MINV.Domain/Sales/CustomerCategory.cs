using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Categoría de cliente.</summary>
public sealed class CustomerCategory : Entity
{
    private CustomerCategory()
    {
    }

    public CustomerCategory(Guid tenantId, string code, string name, Guid? defaultPriceListId)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código", 20);
        Name = Guard.Text(name, "El nombre", 80);
        DefaultPriceListId = Guard.NotEmptyIfPresent(defaultPriceListId, nameof(defaultPriceListId));
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid? DefaultPriceListId { get; private set; }
}
