using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Tabla de clausura del árbol de categorías (ancestro, descendiente, profundidad).</summary>
public sealed class CategoryHierarchy : BaseEntity
{
    private CategoryHierarchy()
    {
    }

    public CategoryHierarchy(Guid tenantId, Guid ancestorId, Guid descendantId, int depth)
        : base(tenantId)
    {
        AncestorId = Guard.NotEmpty(ancestorId, nameof(ancestorId));
        DescendantId = Guard.NotEmpty(descendantId, nameof(descendantId));
        Depth = Guard.NonNegative(depth, "La profundidad");
    }

    public Guid AncestorId { get; private set; }

    public Guid DescendantId { get; private set; }

    public int Depth { get; private set; }
}
