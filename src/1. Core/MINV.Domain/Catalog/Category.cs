using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Categoría del catálogo (árbol en CategoryHierarchies).</summary>
/// <remarks>Origen en la V2.1: 03_CATEGORIAS (tblCategorias): Código, Categoría.</remarks>
public sealed class Category : Entity
{
    private Category()
    {
    }

    public Category(Guid tenantId, string code, string name)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código de la categoría", 20);
        Name = Guard.Text(name, "El nombre", 80);
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public void Rename(string name) => Name = Guard.Text(name, "El nombre", 80);
}
