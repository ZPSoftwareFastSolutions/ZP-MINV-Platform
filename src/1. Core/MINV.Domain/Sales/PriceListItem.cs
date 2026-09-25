using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>Precio de una variante en una lista.</summary>
public sealed class PriceListItem : BaseEntity
{
    private PriceListItem()
    {
    }

    public PriceListItem(Guid tenantId, Guid priceListId, Guid variantId, decimal unitPrice)
        : base(tenantId)
    {
        PriceListId = Guard.NotEmpty(priceListId, nameof(priceListId));
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        UnitPrice = Guard.NonNegative(unitPrice, "El precio");
    }

    public Guid PriceListId { get; private set; }

    public Guid VariantId { get; private set; }

    public decimal UnitPrice { get; private set; }
}
