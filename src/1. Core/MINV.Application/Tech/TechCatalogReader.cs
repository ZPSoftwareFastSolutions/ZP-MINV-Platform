using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;

namespace MINV.Application.Tech;

/// <summary>V4.2 · Valor de una especificación de un producto (número, texto u opción ya resuelta).</summary>
internal sealed record SpecValueRow(Guid ProductId, SpecDefinition Definition, decimal? Number, string? Text, string? Option, int OptionOrder)
{
    /// <summary>Valor para comparar y para el armador: números con punto decimal (cultura invariante).</summary>
    public string Raw => Number is { } n ? n.ToString("0.####", CultureInfo.InvariantCulture) : Text ?? Option ?? string.Empty;

    /// <summary>Valor para mostrar (números con coma decimal y su unidad).</summary>
    public string Display => Number is { } n
        ? n.ToString("0.##", CultureInfo.GetCultureInfo("es-BO")) + (Definition.Unit is null ? string.Empty : " " + Definition.Unit)
        : Text ?? Option ?? string.Empty;
}

/// <summary>
/// V4.2 · Lecturas del catálogo técnico (regla T-01): árbol de categorías (tabla de clausura), especificaciones aplicables a
/// una categoría (propias y heredadas de sus madres), valores de los productos, filtros por especificación, precio de la
/// lista por defecto y stock disponible de la sucursal activa. Todo acotado a la empresa por los filtros globales.
/// </summary>
internal sealed class TechCatalogReader(IMinvDbContext db)
{
    public async Task<Category> CategoryAsync(string code, CancellationToken ct)
    {
        var c = code.Trim().ToUpperInvariant();
        return await db.Set<Category>().FirstOrDefaultAsync(x => x.Code == c, ct) ?? throw new NotFoundException($"La categoría {c} no existe.");
    }

    /// <summary>La categoría y sus madres con la distancia (0 = ella misma).</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> AncestorsAsync(Guid categoryId, CancellationToken ct)
    {
        var rows = await db.Set<CategoryHierarchy>().Where(h => h.DescendantId == categoryId).Select(h => new { h.AncestorId, h.Depth }).ToListAsync(ct);
        var result = rows.GroupBy(r => r.AncestorId).ToDictionary(g => g.Key, g => g.Min(r => r.Depth));
        result.TryAdd(categoryId, 0);
        return result;
    }

    /// <summary>La categoría y todas sus subcategorías.</summary>
    public async Task<IReadOnlyList<Guid>> SubtreeAsync(Guid categoryId, CancellationToken ct)
    {
        var ids = await db.Set<CategoryHierarchy>().Where(h => h.AncestorId == categoryId).Select(h => h.DescendantId).ToListAsync(ct);
        return ids.Append(categoryId).Distinct().ToList();
    }

    /// <summary>Especificaciones de la categoría y de sus madres (de la raíz hacia la hoja, luego por orden); con la distancia
    /// de la categoría que las define.</summary>
    public async Task<IReadOnlyList<(SpecDefinition Definition, int Depth)>> ApplicableAsync(Guid categoryId, CancellationToken ct)
    {
        var ancestors = await AncestorsAsync(categoryId, ct);
        var ids = ancestors.Keys.ToList();
        var definitions = await db.Set<SpecDefinition>().Where(d => ids.Contains(d.CategoryId)).ToListAsync(ct);
        return definitions.Select(d => (d, ancestors[d.CategoryId])).OrderByDescending(x => x.Item2).ThenBy(x => x.d.SortOrder).ThenBy(x => x.d.Name)
            .Select(x => (x.d, x.Item2)).ToList();
    }

    /// <summary>Opciones de las especificaciones, en su orden.</summary>
    public async Task<ILookup<Guid, SpecOption>> OptionsAsync(IReadOnlyCollection<Guid> definitionIds, CancellationToken ct) =>
        (await db.Set<SpecOption>().Where(o => definitionIds.Contains(o.SpecDefinitionId)).ToListAsync(ct))
        .OrderBy(o => o.SortOrder).ThenBy(o => o.Value, StringComparer.CurrentCultureIgnoreCase).ToLookup(o => o.SpecDefinitionId);

    /// <summary>Valores de especificación de los productos (todas o las indicadas).</summary>
    public async Task<IReadOnlyList<SpecValueRow>> ValuesAsync(IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<Guid>? definitionIds,
        CancellationToken ct)
    {
        var query = from v in db.Set<ProductSpecValue>()
                    join d in db.Set<SpecDefinition>() on v.SpecDefinitionId equals d.Id
                    join o in db.Set<SpecOption>() on v.OptionId equals (Guid?)o.Id into oj
                    from o in oj.DefaultIfEmpty()
                    where productIds.Contains(v.ProductId)
                    select new { v.ProductId, Definition = d, v.NumberValue, v.TextValue, Option = o == null ? null : o.Value, Order = o == null ? 0 : o.SortOrder };
        if (definitionIds is not null)
        {
            query = query.Where(x => definitionIds.Contains(x.Definition.Id));
        }
        return (await query.ToListAsync(ct))
            .Select(x => new SpecValueRow(x.ProductId, x.Definition, x.NumberValue, x.TextValue, x.Option, x.Order)).ToList();
    }

    /// <summary>
    /// Productos que cumplen TODOS los filtros (y entre los valores de un filtro, cualquiera): la especificación se busca por
    /// su código en cualquier categoría (p. ej. «socket» de procesadores y de placas); los números admiten valores exactos y
    /// rango. Un filtro de una especificación inexistente no deja pasar ningún producto.
    /// </summary>
    public async Task<HashSet<Guid>> MatchAsync(IReadOnlyList<SpecFilter> filters, IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        var result = productIds.ToHashSet();
        foreach (var filter in filters.Where(f => !string.IsNullOrWhiteSpace(f.Code)))
        {
            if (result.Count == 0)
            {
                break;
            }
            var code = filter.Code.Trim().ToLowerInvariant();
            var definitionIds = await db.Set<SpecDefinition>().Where(d => d.Code == code).Select(d => d.Id).ToListAsync(ct);
            var candidates = result.ToList();
            var values = await ValuesAsync(candidates, definitionIds, ct);
            var wanted = (filter.Values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
            var numbers = wanted.Select(ParseNumber).Where(n => n is not null).Select(n => n!.Value).ToList();
            bool Matches(SpecValueRow v) =>
                (wanted.Count == 0
                 || (v.Option is not null && wanted.Any(w => string.Equals(w, v.Option, StringComparison.OrdinalIgnoreCase)))
                 || (v.Text is not null && wanted.Any(w => string.Equals(w, v.Text, StringComparison.OrdinalIgnoreCase)))
                 || (v.Number is { } n && numbers.Contains(n)))
                && (filter.Min is null || (v.Number is { } min && min >= filter.Min))
                && (filter.Max is null || (v.Number is { } max && max <= filter.Max));
            result = values.Where(Matches).Select(v => v.ProductId).ToHashSet();
        }
        return result;
    }

    /// <summary>Número escrito con punto o coma decimal (null si no es un número).</summary>
    public static decimal? ParseNumber(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Contains(',', StringComparison.Ordinal) && !value.Contains('.', StringComparison.Ordinal))
        {
            value = value.Replace(',', '.');
        }
        return decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>Precio de cada variante en la lista de precios por defecto.</summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> PricesAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct) =>
        (await (from i in db.Set<PriceListItem>()
                join l in db.Set<PriceList>() on i.PriceListId equals l.Id
                where l.IsDefault && variantIds.Contains(i.VariantId)
                select new { i.VariantId, i.UnitPrice }).ToListAsync(ct))
        .GroupBy(x => x.VariantId).ToDictionary(g => g.Key, g => g.First().UnitPrice);

    /// <summary>Stock disponible (existencia − reservas) de cada variante en la sucursal activa (o en las visibles si no hay
    /// una activa).</summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> StockAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct)
    {
        var active = db.Branches.ActiveBranchId;
        return (await (from l in db.Set<StockLevel>()
                       join b in db.Set<Batch>() on l.BatchId equals b.Id
                       where variantIds.Contains(b.VariantId) && (active == null || l.BranchId == active)
                       select new { b.VariantId, l.QuantityOnHand, l.QuantityReserved }).ToListAsync(ct))
            .GroupBy(x => x.VariantId).ToDictionary(g => g.Key, g => Quantities.Round6(g.Sum(x => x.QuantityOnHand - x.QuantityReserved)));
    }

    /// <summary>Imagen de cada variante (id de la imagen).</summary>
    public async Task<IReadOnlyDictionary<Guid, Guid>> ImagesAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct) =>
        (await db.Set<ProductImage>().Where(i => variantIds.Contains(i.VariantId)).Select(i => new { i.VariantId, i.Id }).ToListAsync(ct))
        .GroupBy(x => x.VariantId).ToDictionary(g => g.Key, g => g.First().Id);

    /// <summary>Marca de cada producto (por su modelo).</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> BrandsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
        (await (from p in db.Set<Product>()
                join m in db.Set<BrandModel>() on p.ModelId equals (Guid?)m.Id
                join b in db.Set<Brand>() on m.BrandId equals b.Id
                where productIds.Contains(p.Id)
                select new { p.Id, b.Name }).ToListAsync(ct))
        .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Name);

    /// <summary>Perfil técnico (tipo de serie y garantía) de cada producto.</summary>
    public async Task<IReadOnlyDictionary<Guid, ProductTechProfile>> ProfilesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
        await db.Set<ProductTechProfile>().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);

    /// <summary>Especificaciones destacadas de un producto (las filtrables, sin condición ni plataforma), para las tarjetas.</summary>
    public static IReadOnlyList<string> KeySpecs(IEnumerable<SpecValueRow> values, int max = 4) =>
        values.Where(v => v.Definition.IsFilterable && v.Definition.Code != TechSpecCodes.Condition && !TechSpecCodes.IsPlatform(v.Definition.Code))
            .GroupBy(v => v.Definition.Id).OrderBy(g => g.First().Definition.SortOrder)
            .Select(g => $"{g.First().Definition.Name}: {string.Join(", ", g.OrderBy(v => v.OptionOrder).Select(v => v.Display))}")
            .Take(max).ToList();

    /// <summary>Plataformas del producto (especificaciones «plataforma» y «plataformas»).</summary>
    public static IReadOnlyList<string> Platforms(IEnumerable<SpecValueRow> values) =>
        values.Where(v => TechSpecCodes.IsPlatform(v.Definition.Code)).OrderBy(v => v.OptionOrder).Select(v => v.Raw).Distinct().ToList();
}
