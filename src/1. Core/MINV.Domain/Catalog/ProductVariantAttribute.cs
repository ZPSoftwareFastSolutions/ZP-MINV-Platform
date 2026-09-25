using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Valores de atributo de cada variante (un valor por atributo: lo exige un trigger).</summary>
public sealed class ProductVariantAttribute : BaseEntity
{
    private ProductVariantAttribute()
    {
    }

    public ProductVariantAttribute(Guid tenantId, Guid variantId, Guid attributeValueId)
        : base(tenantId)
    {
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        AttributeValueId = Guard.NotEmpty(attributeValueId, nameof(attributeValueId));
    }

    public Guid VariantId { get; private set; }

    public Guid AttributeValueId { get; private set; }
}
