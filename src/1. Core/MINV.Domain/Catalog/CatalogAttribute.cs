using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Atributo de variante (Color, Talla…).</summary>
public sealed class CatalogAttribute : Entity
{
    private CatalogAttribute()
    {
    }

    public CatalogAttribute(Guid tenantId, string name)
        : base(tenantId)
    {
        Name = Guard.Text(name, "El nombre del atributo", 60);
    }

    public string Name { get; private set; } = string.Empty;
}
