using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Mantenimiento de la tabla de clausura del árbol de categorías (CategoryHierarchies).</summary>
public static class CategoryTree
{
    /// <summary>
    /// Filas de clausura de una categoría nueva: la fila reflexiva (profundidad 0) y una fila por cada ancestro del padre.
    /// <paramref name="parentAncestors"/> son las filas cuyo descendiente es el padre (incluida su fila reflexiva);
    /// vacío si la categoría es raíz.
    /// </summary>
    public static IReadOnlyList<CategoryHierarchy> ForNewCategory(Category category, IEnumerable<CategoryHierarchy> parentAncestors)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(parentAncestors);
        var rows = new List<CategoryHierarchy> { new(category.TenantId, category.Id, category.Id, 0) };
        var parents = parentAncestors.ToList();
        Guard.That(parents.Count == 0 || parents.Select(p => p.DescendantId).Distinct().Count() == 1, "category.parent",
            "Las filas del padre deben pertenecer a una sola categoría.");
        foreach (var p in parents)
        {
            Guard.That(p.TenantId == category.TenantId, "tenant.mismatch", "La categoría padre pertenece a otra empresa.");
            rows.Add(new CategoryHierarchy(category.TenantId, p.AncestorId, category.Id, p.Depth + 1));
        }
        return rows;
    }
}
