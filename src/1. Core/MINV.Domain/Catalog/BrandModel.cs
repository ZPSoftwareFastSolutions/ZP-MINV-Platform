using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Modelo de una marca.</summary>
public sealed class BrandModel : Entity
{
    private BrandModel()
    {
    }

    public BrandModel(Guid tenantId, Guid brandId, string name)
        : base(tenantId)
    {
        BrandId = Guard.NotEmpty(brandId, nameof(brandId));
        Name = Guard.Text(name, "El nombre del modelo", 80);
    }

    public Guid BrandId { get; private set; }

    public string Name { get; private set; } = string.Empty;
}
