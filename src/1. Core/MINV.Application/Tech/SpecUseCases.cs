using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Inventory;

namespace MINV.Application.Tech;

// =====================================================================================================================
// V4.2 · Fichas técnicas (regla T-01): especificaciones por categoría con herencia, valores tipados de cada producto,
// búsqueda y facetas por especificación.
// =====================================================================================================================

public sealed class GetSpecDefinitionsHandler(IMinvDbContext db) : IRequestHandler<GetSpecDefinitionsQuery, IReadOnlyList<SpecDefinitionView>>
{
    public async Task<IReadOnlyList<SpecDefinitionView>> Handle(GetSpecDefinitionsQuery request, CancellationToken ct)
    {
        var reader = new TechCatalogReader(db);
        var categories = await db.Set<Category>().ToDictionaryAsync(c => c.Id, ct);
        IReadOnlyList<(SpecDefinition Definition, int Depth)> definitions;
        if (string.IsNullOrWhiteSpace(request.CategoryCode))
        {
            definitions = (await db.Set<SpecDefinition>().ToListAsync(ct))
                .OrderBy(d => categories.GetValueOrDefault(d.CategoryId)?.Code, StringComparer.Ordinal).ThenBy(d => d.SortOrder).ThenBy(d => d.Name)
                .Select(d => (d, 0)).ToList();
        }
        else
        {
            var category = await reader.CategoryAsync(request.CategoryCode, ct);
            definitions = await reader.ApplicableAsync(category.Id, ct);
        }
        var options = await reader.OptionsAsync(definitions.Select(d => d.Definition.Id).ToList(), ct);
        return definitions.Select(x =>
        {
            var d = x.Definition;
            var category = categories.GetValueOrDefault(d.CategoryId);
            return new SpecDefinitionView(d.Id, category?.Code ?? "?", category?.Name ?? "?", d.Code, d.Name, d.Unit, d.DataType, d.IsMultiValued,
                d.IsFilterable, d.IsRequired, d.CompatibilityKey, d.SortOrder, options[d.Id].Select(o => o.Value).ToList(), x.Depth > 0);
        }).ToList();
    }
}

public sealed class SaveSpecDefinitionValidator : AbstractValidator<SaveSpecDefinitionCommand>
{
    public SaveSpecDefinitionValidator()
    {
        RuleFor(x => x.CategoryCode).NotEmpty().WithMessage("Elija la categoría.");
        RuleFor(x => x.Code).NotEmpty().WithMessage("Indique el código de la especificación.").MaximumLength(40);
        RuleFor(x => x.Name).NotEmpty().WithMessage("Indique el nombre de la especificación.").MaximumLength(80);
        RuleFor(x => x.Unit).MaximumLength(20);
        RuleFor(x => x.Options).NotNull();
        RuleForEach(x => x.Options).NotEmpty().WithMessage("Una opción no puede estar vacía.").MaximumLength(60);
    }
}

/// <summary>Crea o modifica una especificación con sus opciones. Una opción que ya usa un producto no se quita.</summary>
public sealed class SaveSpecDefinitionHandler(IMinvDbContext db) : IRequestHandler<SaveSpecDefinitionCommand, string>
{
    public async Task<string> Handle(SaveSpecDefinitionCommand request, CancellationToken ct)
    {
        var reader = new TechCatalogReader(db);
        var category = await reader.CategoryAsync(request.CategoryCode, ct);
        var code = request.Code.Trim().ToLowerInvariant();
        var family = (await reader.AncestorsAsync(category.Id, ct)).Keys.Concat(await reader.SubtreeAsync(category.Id, ct)).Distinct().ToList();
        var existing = await db.Set<SpecDefinition>().Where(d => d.Code == code && family.Contains(d.CategoryId)).ToListAsync(ct);
        var clash = existing.FirstOrDefault(d => d.CategoryId != category.Id);
        if (clash is not null)
        {
            var other = await db.Set<Category>().Where(c => c.Id == clash.CategoryId).Select(c => c.Name).FirstAsync(ct);
            throw new DomainException("spec.duplicate", $"La especificación «{code}» ya existe en la categoría {other} (se hereda entre madres e hijas).");
        }
        var definition = existing.FirstOrDefault();
        if (definition is null)
        {
            definition = new SpecDefinition(category.TenantId, category.Id, code, request.Name.Trim(), request.Unit?.Trim(), request.DataType,
                request.IsMultiValued, request.IsFilterable, request.IsRequired, request.CompatibilityKey, request.SortOrder);
            db.Set<SpecDefinition>().Add(definition);
        }
        else
        {
            Guard.That(definition.DataType == request.DataType && definition.IsMultiValued == request.IsMultiValued, "spec.type_change",
                $"La especificación «{code}» es de tipo {definition.DataType}{(definition.IsMultiValued ? " multivalor" : string.Empty)}: " +
                "el tipo no cambia después de crearla (cree otra especificación).");
            definition.Update(request.Name.Trim(), request.Unit?.Trim(), request.IsFilterable, request.IsRequired, request.CompatibilityKey,
                request.SortOrder);
        }

        var wanted = request.Options.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Guard.That(request.DataType == SpecDataType.Option ? wanted.Count > 0 : wanted.Count == 0, "spec.options",
            request.DataType == SpecDataType.Option
                ? "Una especificación de opciones necesita al menos una opción."
                : "Solo las especificaciones de opciones llevan una lista de opciones.");
        var current = await db.Set<SpecOption>().Where(o => o.SpecDefinitionId == definition.Id).ToListAsync(ct);
        var removed = current.Where(o => !wanted.Contains(o.Value, StringComparer.OrdinalIgnoreCase)).ToList();
        if (removed.Count > 0)
        {
            var removedIds = removed.Select(o => (Guid?)o.Id).ToList();
            var used = await db.Set<ProductSpecValue>().Where(v => removedIds.Contains(v.OptionId)).Select(v => v.OptionId).Distinct().ToListAsync(ct);
            Guard.That(used.Count == 0, "spec.option_in_use",
                $"No se puede quitar {string.Join(", ", removed.Where(o => used.Contains(o.Id)).Select(o => $"«{o.Value}»"))}: hay productos con esa opción.");
            db.Set<SpecOption>().RemoveRange(removed);
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            var option = current.FirstOrDefault(o => string.Equals(o.Value, wanted[i], StringComparison.OrdinalIgnoreCase));
            if (option is null)
            {
                db.Set<SpecOption>().Add(new SpecOption(category.TenantId, definition.Id, wanted[i], i + 1));
            }
            else
            {
                option.Reorder(i + 1);
            }
        }
        await db.SaveChangesAsync(ct);
        return definition.Code;
    }
}

public sealed class GetProductTechHandler(IMinvDbContext db) : IRequestHandler<GetProductTechQuery, ProductTechView>
{
    public async Task<ProductTechView> Handle(GetProductTechQuery request, CancellationToken ct)
    {
        var reader = new TechCatalogReader(db);
        var item = await new InventoryLookups(db).VariantBySkuAsync(request.Sku, ct);
        var product = item.Product;
        var category = await db.Set<Category>().FirstAsync(c => c.Id == product.CategoryId, ct);
        var brand = (await reader.BrandsAsync([product.Id], ct)).GetValueOrDefault(product.Id);
        var profile = (await reader.ProfilesAsync([product.Id], ct)).GetValueOrDefault(product.Id);
        var applicable = await reader.ApplicableAsync(product.CategoryId, ct);
        var values = (await reader.ValuesAsync([product.Id], null, ct)).ToLookup(v => v.Definition.Id);
        // Las aplicables a la categoría y, al final, las que quedaron de otra categoría (producto recategorizado)
        var definitions = applicable.Select(a => a.Definition).Concat(values.SelectMany(g => g).Select(v => v.Definition))
            .DistinctBy(d => d.Id).ToList();
        var specs = definitions.Select(d =>
        {
            var mine = values[d.Id].OrderBy(v => v.OptionOrder).ToList();
            return new ProductSpecView(d.Code, d.Name, d.Unit, d.DataType, mine.Select(v => v.Raw).ToList(), string.Join(", ", mine.Select(v => v.Display)),
                d.CompatibilityKey, d.IsRequired);
        }).ToList();
        var variantIds = await db.Set<ProductVariant>().Where(v => v.ProductId == product.Id).Select(v => v.Id).ToListAsync(ct);
        var inStock = await db.Set<SerialNumber>().CountAsync(s => variantIds.Contains(s.VariantId) && s.Status == SerialNumberStatus.InStock, ct);
        return new ProductTechView(item.Variant.Sku, product.Name, category.Name, brand, SerialLedger.Tracks(product), profile?.SerialKind ?? SerialKind.Serial,
            profile?.WarrantyMonths ?? 0, specs, inStock);
    }
}

public sealed class SaveProductTechValidator : AbstractValidator<SaveProductTechCommand>
{
    public SaveProductTechValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().WithMessage("Elija el producto.");
        RuleFor(x => x.WarrantyMonths).InclusiveBetween(0, 120).WithMessage("La garantía va de 0 a 120 meses.");
        RuleFor(x => x.Specs).NotNull();
        RuleForEach(x => x.Specs).ChildRules(s => s.RuleFor(x => x.Code).NotEmpty().WithMessage("Cada valor necesita el código de su especificación."));
    }
}

/// <summary>
/// Guarda la ficha técnica completa (los valores que no vienen se quitan), el perfil (serie o IMEI y garantía) y el control
/// por serie. Cada valor se valida contra su especificación: número, opción de ESA especificación o texto; un solo valor
/// salvo en las multivalor; las obligatorias no pueden faltar.
/// </summary>
public sealed class SaveProductTechHandler(IMinvDbContext db) : IRequestHandler<SaveProductTechCommand, string>
{
    public async Task<string> Handle(SaveProductTechCommand request, CancellationToken ct)
    {
        var reader = new TechCatalogReader(db);
        var item = await new InventoryLookups(db).VariantBySkuAsync(request.Sku, ct);
        var product = item.Product;
        var variantIds = await db.Set<ProductVariant>().Where(v => v.ProductId == product.Id).Select(v => v.Id).ToListAsync(ct);

        // Perfil técnico: el tipo de identificador no cambia si ya hay series registradas
        var profile = await db.Set<ProductTechProfile>().FirstOrDefaultAsync(p => p.ProductId == product.Id, ct);
        var hasSerials = await db.Set<SerialNumber>().AnyAsync(s => variantIds.Contains(s.VariantId), ct);
        Guard.That(!hasSerials || (profile?.SerialKind ?? SerialKind.Serial) == request.SerialKind, "tech.serial_kind",
            $"{item.Variant.Sku} ya tiene series registradas como {(profile?.SerialKind ?? SerialKind.Serial) switch { SerialKind.Imei => "IMEI", _ => "número de serie" }}: " +
            "el tipo de identificador no se puede cambiar.");
        if (profile is null)
        {
            db.Set<ProductTechProfile>().Add(new ProductTechProfile(product.TenantId, product.Id, request.SerialKind, request.WarrantyMonths));
        }
        else
        {
            profile.Update(request.SerialKind, request.WarrantyMonths);
        }

        // Control por serie (afecta a todas las sucursales)
        var tracking = request.TrackSerials ? TrackingMode.Serial : product.TrackingMode == TrackingMode.Serial ? TrackingMode.None : product.TrackingMode;
        if (tracking != product.TrackingMode)
        {
            Guard.That(db.Branches.AllBranches, "tech.tracking_scope",
                "Cambiar el control por serie afecta a todas las sucursales: hágalo con un usuario de gerencia global (todas las sucursales).");
            var (withoutSerial, onHand) = await SerialCoverageAsync(variantIds, ct);
            product.ChangeTracking(tracking, withoutSerial, onHand);
        }

        // Ficha técnica: valores tipados contra las especificaciones de la categoría (propias y heredadas)
        var applicable = (await reader.ApplicableAsync(product.CategoryId, ct)).Select(a => a.Definition).ToList();
        var options = await reader.OptionsAsync(applicable.Select(d => d.Id).ToList(), ct);
        var desired = new List<ProductSpecValue>();
        foreach (var input in request.Specs.GroupBy(s => s.Code.Trim().ToLowerInvariant()))
        {
            var definition = applicable.FirstOrDefault(d => d.Code == input.Key)
                             ?? throw new DomainException("spec.unknown", $"La especificación «{input.Key}» no es de la categoría del producto.");
            var values = input.SelectMany(s => s.Values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Guard.That(values.Count <= 1 || definition.IsMultiValued, "spec.multi", $"{definition.Name} admite un solo valor.");
            foreach (var value in values)
            {
                desired.Add(definition.DataType switch
                {
                    SpecDataType.Number => ProductSpecValue.Number(product.TenantId, product.Id, definition,
                        TechCatalogReader.ParseNumber(value) ?? throw new DomainException("spec.number", $"{definition.Name}: «{value}» no es un número.")),
                    SpecDataType.Option => ProductSpecValue.Option(product.TenantId, product.Id, definition,
                        options[definition.Id].FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase))
                        ?? throw new DomainException("spec.option",
                            $"{definition.Name}: «{value}» no es una opción válida ({string.Join(", ", options[definition.Id].Select(o => o.Value))}).")),
                    _ => ProductSpecValue.Text(product.TenantId, product.Id, definition, value),
                });
            }
        }
        var missing = applicable.Where(d => d.IsRequired && desired.All(v => v.SpecDefinitionId != d.Id)).Select(d => d.Name).ToList();
        Guard.That(missing.Count == 0, "spec.required", $"Faltan especificaciones obligatorias de {item.Variant.Sku}: {string.Join(", ", missing)}.");

        var current = await db.Set<ProductSpecValue>().Where(v => v.ProductId == product.Id).ToListAsync(ct);
        static bool Same(ProductSpecValue a, ProductSpecValue b) =>
            a.SpecDefinitionId == b.SpecDefinitionId && a.NumberValue == b.NumberValue && a.TextValue == b.TextValue && a.OptionId == b.OptionId;
        var removed = current.Where(c => !desired.Any(d => Same(c, d))).ToList();
        var added = desired.Where(d => !current.Any(c => Same(c, d))).ToList();

        // Primero se quitan los valores que cambian y luego se agregan (una especificación de un solo valor nunca tiene dos filas)
        await using var transaction = await db.BeginTransactionAsync(ct);
        db.Set<ProductSpecValue>().RemoveRange(removed);
        await db.SaveChangesAsync(ct);
        db.Set<ProductSpecValue>().AddRange(added);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return item.Variant.Sku;
    }

    /// <summary>Unidades sin serie (en stock por sucursal y en tránsito) y unidades con serie en stock o en tránsito.</summary>
    private async Task<(int WithoutSerial, int OnHand)> SerialCoverageAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct)
    {
        var stock = await (from l in db.Set<StockLevel>()
                           join b in db.Set<Batch>() on l.BatchId equals b.Id
                           where variantIds.Contains(b.VariantId)
                           group l.QuantityOnHand by l.BranchId into g
                           select new { Branch = g.Key, OnHand = g.Sum() }).ToListAsync(ct);
        var serials = await (from s in db.Set<SerialNumber>()
                             join l in db.Set<StockLevel>() on s.StockLevelId equals (Guid?)l.Id
                             where variantIds.Contains(s.VariantId) && (s.Status == SerialNumberStatus.InStock || s.Status == SerialNumberStatus.Reserved)
                             group s by l.BranchId into g
                             select new { Branch = g.Key, Count = g.Count() }).ToListAsync(ct);
        var withoutSerial = stock.Sum(s => Math.Max(0, (int)Math.Ceiling(s.OnHand) - (serials.FirstOrDefault(x => x.Branch == s.Branch)?.Count ?? 0)));
        var inTransitLines = await (from line in db.Set<StockTransferLine>()
                                    join t in db.Set<StockTransfer>() on line.StockTransferId equals t.Id
                                    where t.Status == TransferStatus.Dispatched && variantIds.Contains(line.VariantId)
                                    select new { line.Id, line.Quantity }).ToListAsync(ct);
        var lineIds = inTransitLines.Select(l => l.Id).ToList();
        var travelling = await db.Set<StockTransferLineSerial>().CountAsync(x => lineIds.Contains(x.StockTransferLineId), ct);
        withoutSerial += Math.Max(0, (int)Math.Ceiling(inTransitLines.Sum(l => l.Quantity)) - travelling);
        var onHand = await db.Set<SerialNumber>().CountAsync(s => variantIds.Contains(s.VariantId)
                                                                  && (s.Status == SerialNumberStatus.InStock || s.Status == SerialNumberStatus.Reserved
                                                                      || s.Status == SerialNumberStatus.InTransit), ct);
        return (withoutSerial, onHand);
    }
}

/// <summary>Búsqueda del catálogo técnico con filtros y stock de la sucursal activa.</summary>
public sealed class SearchTechProductsHandler(IMinvDbContext db) : IRequestHandler<SearchTechProductsQuery, IReadOnlyList<TechProductRow>>
{
    public async Task<IReadOnlyList<TechProductRow>> Handle(SearchTechProductsQuery request, CancellationToken ct)
    {
        var reader = new TechCatalogReader(db);
        var query = from v in db.Set<ProductVariant>()
                    join p in db.Set<Product>() on v.ProductId equals p.Id
                    join c in db.Set<Category>() on p.CategoryId equals c.Id
                    where p.IsActive && v.IsActive
                    select new { Variant = v, Product = p, CategoryCode = c.Code, Category = c.Name };
        if (!string.IsNullOrWhiteSpace(request.CategoryCode))
        {
            var subtree = await reader.SubtreeAsync((await reader.CategoryAsync(request.CategoryCode, ct)).Id, ct);
            query = query.Where(x => subtree.Contains(x.Product.CategoryId));
        }
        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            var text = request.Text.Trim().ToUpperInvariant();
            var byBarcode = await db.Set<ProductBarcode>().Where(b => b.Code == request.Text.Trim()).Select(b => b.VariantId).ToListAsync(ct);
            query = query.Where(x => x.Variant.Sku.ToUpper().Contains(text) || x.Product.Name.ToUpper().Contains(text) || byBarcode.Contains(x.Variant.Id));
        }
        var rows = await query.ToListAsync(ct);
        var filters = (request.Filters ?? []).ToList();
        if (!string.IsNullOrWhiteSpace(request.Platform))
        {
            filters.Add(new SpecFilter(TechSpecCodes.Platform, [request.Platform]));
        }
        if (filters.Count > 0)
        {
            var productIds = rows.Select(r => r.Product.Id).Distinct().ToList();
            // La plataforma vale en «plataforma» (juegos y consolas) o en «plataformas» (accesorios)
            var platform = filters.Where(f => f.Code == TechSpecCodes.Platform && !string.IsNullOrWhiteSpace(request.Platform)).ToList();
            var matched = await reader.MatchAsync(filters.Except(platform).ToList(), productIds, ct);
            if (platform.Count > 0)
            {
                var single = await reader.MatchAsync(platform, matched, ct);
                var multi = await reader.MatchAsync([new SpecFilter(TechSpecCodes.Platforms, platform[0].Values)], matched, ct);
                matched = single.Union(multi).ToHashSet();
            }
            rows = rows.Where(r => matched.Contains(r.Product.Id)).ToList();
        }
        var variantIds = rows.Select(r => r.Variant.Id).ToList();
        var ids = rows.Select(r => r.Product.Id).Distinct().ToList();
        var prices = await reader.PricesAsync(variantIds, ct);
        var stock = await reader.StockAsync(variantIds, ct);
        var images = await reader.ImagesAsync(variantIds, ct);
        var brands = await reader.BrandsAsync(ids, ct);
        var profiles = await reader.ProfilesAsync(ids, ct);
        var values = (await reader.ValuesAsync(ids, null, ct)).ToLookup(v => v.ProductId);
        return rows.Select(r => new TechProductRow(r.Variant.Sku, r.Variant.Name is { Length: > 0 } name ? $"{r.Product.Name} · {name}" : r.Product.Name,
                r.CategoryCode, r.Category, brands.GetValueOrDefault(r.Product.Id), prices.GetValueOrDefault(r.Variant.Id), stock.GetValueOrDefault(r.Variant.Id),
                SerialLedger.Tracks(r.Product), profiles.GetValueOrDefault(r.Product.Id)?.WarrantyMonths ?? 0,
                string.Join(" · ", TechCatalogReader.KeySpecs(values[r.Product.Id])), TechCatalogReader.Platforms(values[r.Product.Id]),
                images.TryGetValue(r.Variant.Id, out var image) ? image : null, profiles.GetValueOrDefault(r.Product.Id)?.SerialKind ?? SerialKind.Serial))
            .Where(r => !request.OnlyInStock || r.Stock > 0)
            .OrderBy(r => r.Category, StringComparer.CurrentCulture).ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .Take(Math.Clamp(request.Max, 1, 2000)).ToList();
    }
}

/// <summary>Facetas de una categoría: especificaciones filtrables (propias y heredadas) con sus conteos.</summary>
public sealed class GetSpecFacetsHandler(IMinvDbContext db) : IRequestHandler<GetSpecFacetsQuery, IReadOnlyList<SpecFacet>>
{
    private const int MaxValues = 30;

    public async Task<IReadOnlyList<SpecFacet>> Handle(GetSpecFacetsQuery request, CancellationToken ct)
    {
        var reader = new TechCatalogReader(db);
        var category = await reader.CategoryAsync(request.CategoryCode, ct);
        var subtree = await reader.SubtreeAsync(category.Id, ct);
        var productIds = await db.Set<Product>().Where(p => p.IsActive && subtree.Contains(p.CategoryId)).Select(p => p.Id).ToListAsync(ct);
        var definitions = (await reader.ApplicableAsync(category.Id, ct)).Select(a => a.Definition).Where(d => d.IsFilterable).ToList();
        var options = await reader.OptionsAsync(definitions.Select(d => d.Id).ToList(), ct);
        var values = (await reader.ValuesAsync(productIds, definitions.Select(d => d.Id).ToList(), ct)).ToLookup(v => v.Definition.Id);
        var facets = new List<SpecFacet>();
        foreach (var d in definitions)
        {
            var mine = values[d.Id].ToList();
            if (mine.Count == 0)
            {
                continue;
            }
            IReadOnlyList<SpecFacetValue> counts = d.DataType switch
            {
                SpecDataType.Option => options[d.Id].Select(o => new SpecFacetValue(o.Value, mine.Where(v => v.Option == o.Value).Select(v => v.ProductId).Distinct().Count()))
                    .Where(v => v.Count > 0).ToList(),
                _ => mine.GroupBy(v => v.Raw, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new SpecFacetValue(g.First().Raw, g.Select(v => v.ProductId).Distinct().Count()))
                    .OrderBy(v => d.DataType == SpecDataType.Number ? TechCatalogReader.ParseNumber(v.Value) : null).ThenBy(v => v.Value, StringComparer.CurrentCulture)
                    .Take(MaxValues).ToList(),
            };
            var numbers = mine.Where(v => v.Number is not null).Select(v => v.Number!.Value).ToList();
            facets.Add(new SpecFacet(d.Code, d.Name, d.Unit, d.DataType, counts, numbers.Count > 0 ? numbers.Min() : null, numbers.Count > 0 ? numbers.Max() : null));
        }
        return facets;
    }
}
