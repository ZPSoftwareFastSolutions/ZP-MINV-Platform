using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Marca.</summary>
public sealed class Brand : Entity
{
    private Brand()
    {
    }

    public Brand(Guid tenantId, string name)
        : base(tenantId)
    {
        Name = Guard.Text(name, "El nombre de la marca", 80);
    }

    public string Name { get; private set; } = string.Empty;
}
