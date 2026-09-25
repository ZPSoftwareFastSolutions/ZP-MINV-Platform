using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Valor de un atributo (Rojo, M…).</summary>
public sealed class CatalogAttributeValue : Entity
{
    private CatalogAttributeValue()
    {
    }

    public CatalogAttributeValue(Guid tenantId, Guid attributeId, string value)
        : base(tenantId)
    {
        AttributeId = Guard.NotEmpty(attributeId, nameof(attributeId));
        Value = Guard.Text(value, "El valor", 60);
    }

    public Guid AttributeId { get; private set; }

    public string Value { get; private set; } = string.Empty;
}
