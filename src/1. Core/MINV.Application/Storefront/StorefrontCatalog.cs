using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Application.Tech;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Storefront;

/// <summary>
/// V6 · Lecturas del catálogo web (regla S-01: una sola verdad, la base de datos). Arma la instantánea que consume la web
/// con la MISMA forma que el mock de la V5 (tools/generar_catalogo_web.py): categorías con ícono y conteo, marcas, productos
/// con slug, ficha técnica, precio de la lista por defecto, disponibilidad de la sucursal de la tienda (existencias −
/// reservado), popularidad por las ventas de 90 días, etiquetas, descripción generada y armados publicados. Todo acotado por
/// los filtros globales (empresa y sucursales del principal).
/// </summary>
internal sealed partial class StorefrontCatalogReader(IMinvDbContext db, ITenantContext tenant, IClock clock, StorefrontOptions? options = null)
{
    /// <summary>Ícono de Lucide por categoría (nombre del componente en lucide-react), como en la web de la V5.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Icons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["COMP"] = "Cpu", ["CPU"] = "Cpu", ["GPU"] = "Gpu", ["MB"] = "CircuitBoard", ["RAM"] = "MemoryStick", ["STO"] = "HardDrive",
        ["PSU"] = "Zap", ["CASE"] = "Box", ["COOL"] = "Fan", ["PC"] = "MonitorSmartphone", ["LAPG"] = "Laptop", ["LAPU"] = "Laptop",
        ["DESK"] = "PcCase", ["MON"] = "Monitor", ["PER"] = "Keyboard", ["KEY"] = "Keyboard", ["MOU"] = "Mouse", ["AUD"] = "Headphones",
        ["PAD"] = "Square", ["CAM"] = "Webcam", ["CHA"] = "Armchair", ["CON"] = "Gamepad2", ["CPS"] = "Gamepad2", ["CXB"] = "Gamepad2",
        ["CNS"] = "Gamepad2", ["JUE"] = "Disc3", ["ACC"] = "Plug", ["MAND"] = "Gamepad", ["CARG"] = "BatteryCharging", ["ALMC"] = "HardDrive",
        ["RED"] = "Router", ["CAB"] = "Cable", ["SOFT"] = "Package", ["LIC"] = "KeyRound", ["SRV"] = "Wrench",
    };

    internal static readonly IReadOnlyDictionary<string, string> RootDescriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["COMP"] = "Componentes para armar o actualizar tu PC",
        ["PC"] = "Computadoras de escritorio y portátiles listas para usar",
        ["MON"] = "Monitores gaming y profesionales",
        ["PER"] = "Teclados, mouse, audio y todo lo que va en tu escritorio",
        ["CON"] = "PlayStation, Xbox y Nintendo",
        ["JUE"] = "Videojuegos físicos y digitales",
        ["ACC"] = "Mandos, cargadores y almacenamiento para consola",
        ["RED"] = "Routers y conectividad",
        ["CAB"] = "Cables y adaptadores",
        ["SOFT"] = "Licencias y servicios técnicos",
    };

    /// <summary>Ranura del armador de la web por cada ranura del dominio.</summary>
    internal static readonly IReadOnlyDictionary<PcSlot, string> WebSlots = new Dictionary<PcSlot, string>
    {
        [PcSlot.Cpu] = "cpu", [PcSlot.Motherboard] = "motherboard", [PcSlot.Ram] = "ram", [PcSlot.Gpu] = "gpu", [PcSlot.Storage] = "storage",
        [PcSlot.Psu] = "psu", [PcSlot.Case] = "case", [PcSlot.Cooler] = "cooler", [PcSlot.Monitor] = "monitor", [PcSlot.Peripheral] = "peripherals",
        [PcSlot.Software] = "software", [PcSlot.Service] = "software",
    };

    private const int PopularityDays = 90;
    private const int NewDays = 30;
    private const string DefaultCondition = "Nuevo";

    public const string ImageRoute = "/storefront/v1/products/{0}/image";

    /// <summary>Sucursal de la tienda (la activa del principal, la única del alcance o la de la casa matriz) y fecha de hoy.</summary>
    public async Task<(Guid BranchId, DateOnly Today)> ContextAsync(CancellationToken ct)
    {
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        var branchId = await BranchContext.ResolveAsync(db, null, ct);
        return (branchId, clock.TodayIn(config.TimeZoneId));
    }

    public async Task<StorefrontCatalogView> SnapshotAsync(CancellationToken ct)
    {
        var (branchId, today) = await ContextAsync(ct);
        var company = await db.Set<Tenant>().Where(t => t.Id == tenant.TenantId).Select(t => new { t.Code, t.LegalName }).FirstAsync(ct);
        var branches = await db.Set<Branch>().Where(b => b.IsActive).OrderBy(b => b.Code).Select(b => new StorefrontBranch(b.Code, b.Name)).ToListAsync(ct);
        var store = branches.FirstOrDefault(b => b.Code == db.Set<Branch>().Local.FirstOrDefault(x => x.Id == branchId)?.Code)
                    ?? await db.Set<Branch>().Where(b => b.Id == branchId).Select(b => new StorefrontBranch(b.Code, b.Name)).FirstAsync(ct);
        var tree = await TreeAsync(ct);
        var set = await ProductsAsync(branchId, today, null, tree, ct);
        var products = set.Products;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in products)
        {
            foreach (var code in tree.Lineage(p.Category))
            {
                counts[code] = counts.GetValueOrDefault(code) + 1;
            }
        }
        var categories = tree.Ordered.Select(c => new StorefrontCategory(c.Code, c.Name, Slug(c.Name), tree.ParentOf(c.Code), Icons.GetValueOrDefault(c.Code, "Tag"),
            RootDescriptions.GetValueOrDefault(c.Code) ?? tree.Path(c.Code), counts.GetValueOrDefault(c.Code))).ToList();
        var brands = products.GroupBy(p => p.Brand, StringComparer.Ordinal).Select(g => new StorefrontBrand(BrandCode(g.Key), g.Key, g.Count()))
            .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var presets = Presets(await db.Set<PcBuild>().AsNoTracking().Include(b => b.Lines).Where(b => b.PublishedToWeb).ToListAsync(ct), set);
        // V7 · La vigencia de una reserva y los días que puede pedir quien reserva los fija el servidor (la web no los supone)
        var reservation = options ?? new StorefrontOptions();
        return new StorefrontCatalogView(new StorefrontCompany(company.Code, company.LegalName, branches), store, categories, brands, products, presets,
            clock.UtcNow, reservation.EffectiveHours, reservation.MaxHoldDays);
    }

    public async Task<StorefrontProduct> ProductAsync(string slug, CancellationToken ct)
    {
        var sku = slug.Trim().ToUpperInvariant();
        var variantId = await db.Set<ProductVariant>().Where(v => v.Sku == sku).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct)
                        ?? throw new NotFoundException($"El producto {slug} no está en el catálogo.");
        var (branchId, today) = await ContextAsync(ct);
        var set = await ProductsAsync(branchId, today, [variantId], await TreeAsync(ct), ct);
        return set.Products.FirstOrDefault() ?? throw new NotFoundException($"El producto {slug} no está publicado.");
    }

    public async Task<IReadOnlyList<StorefrontPreset>> PresetsAsync(CancellationToken ct)
    {
        var (branchId, today) = await ContextAsync(ct);
        var published = await db.Set<PcBuild>().AsNoTracking().Include(b => b.Lines).Where(b => b.PublishedToWeb).ToListAsync(ct);
        var variantIds = published.SelectMany(b => b.Lines.Select(l => l.VariantId)).Distinct().ToList();
        var set = variantIds.Count == 0 ? ProductSet.Empty : await ProductsAsync(branchId, today, variantIds, await TreeAsync(ct), ct);
        return Presets(published, set);
    }

    /// <summary>Armados publicados con sus piezas (solo las que siguen en el catálogo web); un armado sin piezas visibles no sale.
    /// <c>available</c>: todas sus piezas tienen disponible en la sucursal de la tienda. V7: un carrito nunca es un armado
    /// sugerido (regla P-05; el dominio no deja publicarlo y aquí se descarta igual).</summary>
    private static IReadOnlyList<StorefrontPreset> Presets(IReadOnlyList<PcBuild> published, ProductSet set) =>
        published
            .Where(b => !b.IsCart && b.Status is PcBuildStatus.Quoted or PcBuildStatus.Reserved or PcBuildStatus.Sold && b.Lines.Count > 0)
            .OrderBy(b => b.Total)
            .Select(b =>
            {
                var lines = b.Lines.OrderBy(l => l.Slot).ThenBy(l => l.Id).Select(l => (Line: l, Product: set.ByVariant.GetValueOrDefault(l.VariantId))).ToList();
                var available = lines.All(x => x.Product is not null && x.Product.Available >= x.Line.Quantity);
                return new StorefrontPreset(b.Number.ToLowerInvariant(), b.Number, b.Name, Tier(b.Name, b.Total), b.Total, available,
                    lines.Where(x => x.Product is not null && x.Line.Slot is not null)
                        .Select(x => new StorefrontPresetLine(WebSlots[x.Line.Slot!.Value], x.Product!.Sku, x.Line.Quantity, x.Line.QuotedUnitPrice)).ToList());
            })
            .Where(p => p.Lines.Count > 0)
            .ToList();

    /// <summary>Productos de una lectura y su índice por variante (los armados guardan el id de la variante).</summary>
    internal sealed record ProductSet(IReadOnlyList<StorefrontProduct> Products, IReadOnlyDictionary<Guid, StorefrontProduct> ByVariant)
    {
        public static readonly ProductSet Empty = new([], new Dictionary<Guid, StorefrontProduct>());
    }

    /// <summary>Perfil del armado sugerido para la web (la V5 lo traía del JSON): por el nombre y, si no, por el precio.</summary>
    internal static string Tier(string name, decimal total)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("oficina", StringComparison.Ordinal) || n.Contains("office", StringComparison.Ordinal))
        {
            return "oficina";
        }
        if (n.Contains("creaci", StringComparison.Ordinal) || n.Contains("creador", StringComparison.Ordinal) || n.Contains("stream", StringComparison.Ordinal)
            || n.Contains("edici", StringComparison.Ordinal))
        {
            return "creador";
        }
        if (n.Contains("entusiasta", StringComparison.Ordinal) || n.Contains("4k", StringComparison.Ordinal))
        {
            return "entusiasta";
        }
        if (n.Contains("entrada", StringComparison.Ordinal))
        {
            return "entrada";
        }
        if (n.Contains("alta", StringComparison.Ordinal))
        {
            return "alta";
        }
        if (n.Contains("media", StringComparison.Ordinal))
        {
            return "media";
        }
        return total < 6000 ? "entrada" : total < 10000 ? "media" : total < 15000 ? "alta" : "entusiasta";
    }

    // ---------------------------------------------------------------------------------------------- productos
    internal async Task<ProductSet> ProductsAsync(Guid branchId, DateOnly today, IReadOnlyCollection<Guid>? onlyVariantIds, CategoryTree tree,
        CancellationToken ct)
    {
        var query = from v in db.Set<ProductVariant>()
                    join p in db.Set<Product>() on v.ProductId equals p.Id
                    where p.IsActive && v.IsActive
                    select new { v, p, CreatedAt = EF.Property<DateTimeOffset>(p, "CreatedAt") };
        if (onlyVariantIds is not null)
        {
            query = query.Where(x => onlyVariantIds.Contains(x.v.Id));
        }
        var rows = await query.ToListAsync(ct);
        if (rows.Count == 0)
        {
            return ProductSet.Empty;
        }
        var variantIds = rows.Select(r => r.v.Id).ToList();
        var productIds = rows.Select(r => r.p.Id).Distinct().ToList();
        var reader = new TechCatalogReader(db);
        var prices = await reader.PricesAsync(variantIds, ct);
        var listPrices = await ListPricesAsync(variantIds, today, ct);
        var brands = await reader.BrandsAsync(productIds, ct);
        var profiles = await reader.ProfilesAsync(productIds, ct);
        var images = (await db.Set<ProductImage>().Where(i => variantIds.Contains(i.VariantId)).Select(i => i.VariantId).ToListAsync(ct)).ToHashSet();
        var values = (await reader.ValuesAsync(productIds, null, ct)).ToLookup(v => v.ProductId);
        var stock = (await (from l in db.Set<StockLevel>()
                            join b in db.Set<Batch>() on l.BatchId equals b.Id
                            where l.BranchId == branchId && variantIds.Contains(b.VariantId)
                            select new { b.VariantId, l.QuantityOnHand, l.QuantityReserved }).ToListAsync(ct))
            .GroupBy(x => x.VariantId).ToDictionary(g => g.Key, g => (OnHand: g.Sum(x => x.QuantityOnHand), Reserved: g.Sum(x => x.QuantityReserved)));
        var sold = await SoldAsync(variantIds, today, ct);
        var maxSold = sold.Count == 0 ? 0m : sold.Values.Max();
        var now = clock.UtcNow;
        var result = new List<StorefrontProduct>(rows.Count);
        var byVariant = new Dictionary<Guid, StorefrontProduct>(rows.Count);
        foreach (var r in rows.OrderBy(r => r.v.Sku, StringComparer.Ordinal))
        {
            var price = prices.GetValueOrDefault(r.v.Id);
            if (price <= 0)
            {
                continue;   // sin precio en la lista por defecto no se vende en la web
            }
            var category = tree.Get(r.p.CategoryId);
            var specs = Specs(values[r.p.Id], tree);
            var condition = values[r.p.Id].Where(v => v.Definition.Code == TechSpecCodes.Condition).Select(v => v.Option ?? v.Text).FirstOrDefault(x => x is not null)
                            ?? DefaultCondition;
            var name = r.v.Name is { Length: > 0 } vn ? $"{r.p.Name} · {vn}" : r.p.Name;
            var brand = brands.GetValueOrDefault(r.p.Id) ?? "Sin marca";
            var (onHand, reserved) = stock.GetValueOrDefault(r.v.Id);
            var available = Math.Max(0, Quantities.Round6(onHand - reserved));
            var quantitySold = sold.GetValueOrDefault(r.v.Id);
            var popularity = quantitySold <= 0 || maxSold <= 0 ? 1 : 1 + (int)Math.Round(9m * quantitySold / maxSold, MidpointRounding.AwayFromZero);
            var listPrice = listPrices.TryGetValue(r.v.Id, out var lp) && lp > price ? lp : (decimal?)null;
            var tags = new List<string>();
            if (popularity >= 8)
            {
                tags.Add("destacado");
            }
            if (listPrice is not null)
            {
                tags.Add("oferta");
            }
            if (r.CreatedAt >= now.AddDays(-NewDays))
            {
                tags.Add("nuevo");
            }
            var warranty = profiles.TryGetValue(r.p.Id, out var profile) ? profile.WarrantyMonths : 0;
            var serialized = r.p.TrackingMode == TrackingMode.Serial;
            var featured = specs.Where(s => s.Filterable && s.Key != TechSpecCodes.Condition).Take(4).ToList();
            var highlights = featured.Take(3).Select(s => $"{s.Label}: {s.Text}").ToList();
            var path = category is null ? string.Empty : tree.Path(category.Code);
            var description = string.IsNullOrWhiteSpace(r.p.Description)
                ? Describe(ShortName(name), brand, path, featured, warranty, serialized, condition)
                : r.p.Description.Trim();
            var product = new StorefrontProduct(r.v.Sku, Slug(r.v.Sku), name, ShortName(name), category?.Code ?? string.Empty, category?.Name ?? string.Empty,
                path, brand, price, listPrice, images.Contains(r.v.Id) ? string.Format(CultureInfo.InvariantCulture, ImageRoute, r.v.Sku) : null,
                available, Quantities.Round6(reserved), Quantities.Round6(onHand), condition, warranty, serialized, popularity, tags, description, highlights,
                specs);
            result.Add(product);
            byVariant[r.v.Id] = product;
        }
        return new ProductSet(result, byVariant);
    }

    /// <summary>Ficha de un producto: las especificaciones de la categoría madre primero (condición), luego las propias, en su orden.</summary>
    private static IReadOnlyList<StorefrontSpec> Specs(IEnumerable<SpecValueRow> values, CategoryTree tree) =>
        values.GroupBy(v => v.Definition.Id)
            .Select(g => (Definition: g.First().Definition, Values: g.OrderBy(v => v.OptionOrder).ThenBy(v => v.Raw, StringComparer.Ordinal).ToList()))
            .OrderBy(x => tree.DepthOf(x.Definition.CategoryId)).ThenBy(x => x.Definition.SortOrder).ThenBy(x => x.Definition.Name, StringComparer.CurrentCulture)
            .Select(x =>
            {
                var d = x.Definition;
                object value = d.DataType == SpecDataType.Number
                    ? Normalize(x.Values[0].Number ?? 0)
                    : d.IsMultiValued ? x.Values.Select(v => v.Option ?? v.Text ?? string.Empty).ToList()
                    : x.Values[0].Option ?? x.Values[0].Text ?? string.Empty;
                var text = string.Join(", ", x.Values.Select(v => v.Display));
                return new StorefrontSpec(d.Code, d.Name, value, text, d.Unit, d.IsFilterable);
            })
            .ToList();

    /// <summary>Descripción generada como en la web de la V5 cuando el producto no tiene una propia.</summary>
    internal static string Describe(string shortName, string brand, string path, IReadOnlyList<StorefrontSpec> featured, int warrantyMonths, bool serialized,
        string condition)
    {
        var text = new StringBuilder().Append(shortName).Append(" de ").Append(brand).Append(", en la categoría ").Append(path).Append(". ");
        if (featured.Count > 0)
        {
            var phrase = string.Join(" ", featured.Take(2).Select(s => $"{s.Label.ToLowerInvariant()} {s.Text},")).TrimEnd(',');
            text.Append(Capitalize(phrase)).Append(". ");
        }
        if (warrantyMonths > 0)
        {
            text.Append("Garantía oficial de ").Append(warrantyMonths.ToString(CultureInfo.InvariantCulture)).Append(" meses. ");
        }
        if (serialized)
        {
            text.Append("Se entrega con su número de serie registrado para garantía y trazabilidad. ");
        }
        return text.Append("Condición: ").Append(condition.ToLowerInvariant()).Append('.').ToString();

        static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }

    /// <summary>Ventas (unidades) de los últimos 90 días por variante (facturas vigentes de las sucursales visibles).</summary>
    private async Task<IReadOnlyDictionary<Guid, decimal>> SoldAsync(IReadOnlyCollection<Guid> variantIds, DateOnly today, CancellationToken ct)
    {
        var since = today.AddDays(-PopularityDays);
        return (await (from l in db.Set<SalesOrderLine>()
                       join o in db.Set<SalesOrder>() on l.SalesOrderId equals o.Id
                       join i in db.Set<Invoice>() on o.Id equals i.SalesOrderId
                       where i.Status == InvoiceStatus.Issued && o.OrderDate >= since && variantIds.Contains(l.VariantId)
                       select new { l.VariantId, l.Quantity }).ToListAsync(ct))
            .GroupBy(x => x.VariantId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
    }

    /// <summary>Precio «de lista» tachado: el de una lista de precios vigente NO predeterminada llamada como oferta o web (si
    /// la empresa no maneja el concepto, no hay precios tachados).</summary>
    private async Task<IReadOnlyDictionary<Guid, decimal>> ListPricesAsync(IReadOnlyCollection<Guid> variantIds, DateOnly today, CancellationToken ct)
    {
        var lists = await db.Set<PriceList>().Where(l => !l.IsDefault && l.ValidFrom <= today && (l.ValidTo == null || l.ValidTo >= today)).ToListAsync(ct);
        var web = lists.Where(l => IsWebList(l.Name)).OrderBy(l => l.ValidFrom).LastOrDefault();
        if (web is null)
        {
            return new Dictionary<Guid, decimal>();
        }
        return await db.Set<PriceListItem>().Where(i => i.PriceListId == web.Id && variantIds.Contains(i.VariantId))
            .ToDictionaryAsync(i => i.VariantId, i => i.UnitPrice, ct);

        static bool IsWebList(string name) =>
            name.Contains("web", StringComparison.OrdinalIgnoreCase) || name.Contains("oferta", StringComparison.OrdinalIgnoreCase)
            || name.Contains("lista", StringComparison.OrdinalIgnoreCase) && name.Contains("tienda", StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------- categorías
    internal async Task<CategoryTree> TreeAsync(CancellationToken ct)
    {
        var categories = await db.Set<Category>().OrderBy(c => c.Code).ToListAsync(ct);
        var links = await db.Set<CategoryHierarchy>().Select(h => new { h.AncestorId, h.DescendantId, h.Depth }).ToListAsync(ct);
        var parents = links.Where(h => h.Depth == 1).GroupBy(h => h.DescendantId).ToDictionary(g => g.Key, g => g.First().AncestorId);
        return new CategoryTree(categories, parents);
    }

    /// <summary>Árbol de categorías en memoria: padre, linaje (de la hoja a la raíz), ruta y profundidad.</summary>
    internal sealed class CategoryTree
    {
        private readonly Dictionary<Guid, Category> _byId;
        private readonly Dictionary<string, Category> _byCode;
        private readonly Dictionary<Guid, Guid> _parents;

        public CategoryTree(IReadOnlyList<Category> categories, Dictionary<Guid, Guid> parents)
        {
            _byId = categories.ToDictionary(c => c.Id);
            _byCode = categories.ToDictionary(c => c.Code, StringComparer.Ordinal);
            _parents = parents;
            // Orden del árbol: las raíces por código y, debajo de cada una, sus hijas (recorrido en profundidad)
            var children = categories.Where(c => parents.ContainsKey(c.Id)).ToLookup(c => parents[c.Id]);
            var ordered = new List<Category>();
            void Visit(Category c)
            {
                ordered.Add(c);
                foreach (var child in children[c.Id].OrderBy(x => x.Code, StringComparer.Ordinal))
                {
                    Visit(child);
                }
            }
            foreach (var root in categories.Where(c => !parents.ContainsKey(c.Id)).OrderBy(c => c.Code, StringComparer.Ordinal))
            {
                Visit(root);
            }
            Ordered = ordered;
        }

        public IReadOnlyList<Category> Ordered { get; }

        public Category? Get(Guid id) => _byId.GetValueOrDefault(id);

        public string? ParentOf(string code) =>
            _byCode.TryGetValue(code, out var c) && _parents.TryGetValue(c.Id, out var parent) && _byId.TryGetValue(parent, out var p) ? p.Code : null;

        /// <summary>La categoría y sus madres, de la hoja a la raíz.</summary>
        public IReadOnlyList<string> Lineage(string code)
        {
            var result = new List<string>();
            var current = _byCode.GetValueOrDefault(code);
            while (current is not null && result.Count < 32)
            {
                result.Add(current.Code);
                current = _parents.TryGetValue(current.Id, out var parent) ? _byId.GetValueOrDefault(parent) : null;
            }
            return result;
        }

        public string Path(string code) => string.Join(" > ", Lineage(code).Reverse().Select(c => _byCode[c].Name));

        public int DepthOf(Guid categoryId) => _byId.TryGetValue(categoryId, out var c) ? Lineage(c.Code).Count - 1 : 0;
    }

    // ---------------------------------------------------------------------------------------------- utilidades
    /// <summary>Slug como el generador de la V5: sin acentos, minúsculas, solo letras, números y guiones.</summary>
    internal static string Slug(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && ch < 128)
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }
        return NonSlug().Replace(sb.ToString(), "-").Trim('-');
    }

    /// <summary>Quita el paréntesis final de especificaciones: «Procesador AMD Ryzen 5 7600 (AM5, …)» → «Procesador AMD Ryzen 5 7600».</summary>
    internal static string ShortName(string name)
    {
        var shorter = TrailingParenthesis().Replace(name, string.Empty).Trim();
        return shorter.Length >= 8 ? shorter : name;
    }

    /// <summary>Código de marca para la web: letras y números del nombre en mayúsculas («Cooler Master» → COOLERMASTER).</summary>
    internal static string BrandCode(string name)
    {
        var plain = Slug(name).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return plain.Length > 0 ? plain : "MARCA";
    }

    private static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();

    [GeneratedRegex(@"\s*\([^)]*\)\s*$")]
    private static partial Regex TrailingParenthesis();
}

public sealed class GetStorefrontCatalogHandler(IMinvDbContext db, ITenantContext tenant, IClock clock, StorefrontOptions? options = null)
    : IRequestHandler<GetStorefrontCatalogQuery, StorefrontCatalogView>
{
    public Task<StorefrontCatalogView> Handle(GetStorefrontCatalogQuery request, CancellationToken ct) =>
        new StorefrontCatalogReader(db, tenant, clock, options).SnapshotAsync(ct);
}

public sealed class GetStorefrontProductHandler(IMinvDbContext db, ITenantContext tenant, IClock clock)
    : IRequestHandler<GetStorefrontProductQuery, StorefrontProduct>
{
    public Task<StorefrontProduct> Handle(GetStorefrontProductQuery request, CancellationToken ct) =>
        new StorefrontCatalogReader(db, tenant, clock).ProductAsync(request.Slug, ct);
}

public sealed class GetStorefrontProductImageHandler(IMinvDbContext db) : IRequestHandler<GetStorefrontProductImageQuery, StorefrontImage>
{
    public async Task<StorefrontImage> Handle(GetStorefrontProductImageQuery request, CancellationToken ct)
    {
        var sku = request.Sku.Trim().ToUpperInvariant();
        var image = await (from i in db.Set<ProductImage>()
                           join v in db.Set<ProductVariant>() on i.VariantId equals v.Id
                           where v.Sku == sku
                           select new { i.Id, i.Content, i.ContentType }).FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException($"El producto {sku} no tiene imagen.");
        return new StorefrontImage(image.Content, image.ContentType, $"\"{image.Id:N}\"");
    }
}

public sealed class GetStorefrontPresetsHandler(IMinvDbContext db, ITenantContext tenant, IClock clock)
    : IRequestHandler<GetStorefrontPresetsQuery, IReadOnlyList<StorefrontPreset>>
{
    public Task<IReadOnlyList<StorefrontPreset>> Handle(GetStorefrontPresetsQuery request, CancellationToken ct) =>
        new StorefrontCatalogReader(db, tenant, clock).PresetsAsync(ct);
}
