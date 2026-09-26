using System.Globalization;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Application.Billing;

// =====================================================================================================================
// V4.1 · Catálogos sincronizados del SIN y homologación (investigación 04 §5-§7): producto de M-INV → (actividad
// económica, producto genérico del SIN); unidad → unidad SIN; medio de pago → método de pago SIN. Todo se valida contra
// lo SINCRONIZADO (regla F-07): nada de enums fijos. Sin homologar no se factura.
// =====================================================================================================================

/// <summary>V4.1 · Texto normalizado para buscar en los catálogos del SIN: sin acentos, en minúsculas, sin signos ni
/// palabras vacías del español.</summary>
internal static class SiatWords
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "al", "ante", "bajo", "con", "como", "contra", "de", "del", "desde", "e", "el", "ella", "en", "entre", "es", "esta", "este", "etc",
        "etcetera", "hacia", "hasta", "incluidas", "incluidos", "la", "las", "lo", "los", "mas", "no", "o", "otra", "otras", "otro", "otros", "para",
        "pero", "por", "que", "se", "segun", "si", "similares", "sin", "sobre", "su", "sus", "tal", "tales", "u", "un", "una", "unas", "uno", "unos",
        "y", "ya", "cada", "todo", "todos", "tipo", "tipos", "articulos", "articulo", "productos", "producto", "venta", "caja", "bolsa", "paquete",
        "unidad", "unidades", "kilo", "litro", "metro", "galon", "par", "rollo", "pieza", "piezas",
    };

    /// <summary>Sin acentos y en minúsculas («Teclado mecánico» → «teclado mecanico»).</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Palabras significativas: al menos 3 letras, sin palabras vacías ni números sueltos.</summary>
    public static IReadOnlyList<string> Words(string? text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && w.Any(char.IsLetter) && !StopWords.Contains(w)).Distinct().ToList();

    /// <summary>Raíz aproximada para comparar singular y plural («monitores» ~ «monitor», «auriculares» ~ «auricular»).</summary>
    public static string Stem(string word) =>
        word.Length > 5 && word.EndsWith("es", StringComparison.Ordinal) ? word[..^2]
        : word.Length > 4 && word.EndsWith('s') ? word[..^1]
        : word;

    public static bool Match(string a, string b)
    {
        if (a == b)
        {
            return true;
        }
        var (sa, sb) = (Stem(a), Stem(b));
        return sa == sb || (sa.Length >= 5 && sb.Length >= 5 && (sa.StartsWith(sb, StringComparison.Ordinal) || sb.StartsWith(sa, StringComparison.Ordinal)));
    }
}

public sealed class GetSiatCatalogHandler(IMinvDbContext db) : IRequestHandler<GetSiatCatalogQuery, IReadOnlyList<SiatCatalogItemView>>
{
    public async Task<IReadOnlyList<SiatCatalogItemView>> Handle(GetSiatCatalogQuery r, CancellationToken ct)
    {
        var catalog = (r.Catalog ?? string.Empty).Trim().ToUpperInvariant();
        switch (catalog)
        {
            case SiatCatalogNames.Products:
                return (await db.Set<SiatProduct>().AsNoTracking().OrderBy(p => p.ActivityCode).ThenBy(p => p.ProductCode).ToListAsync(ct))
                    .Select(p => new SiatCatalogItemView(catalog, p.ProductCode, $"{p.Description} (actividad {p.ActivityCode})", p.IsCurrent)).ToList();
            case SiatCatalogNames.Activities:
                return (await db.Set<SiatActivity>().AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct))
                    .Select(a => new SiatCatalogItemView(catalog, int.TryParse(a.Code, NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c : 0,
                        $"{a.Code} · {a.Description}", a.IsCurrent)).ToList();
            case SiatCatalogNames.ActivitySectors:
                return (await db.Set<SiatActivitySector>().AsNoTracking().OrderBy(a => a.ActivityCode).ThenBy(a => a.DocumentSector).ToListAsync(ct))
                    .Select(a => new SiatCatalogItemView(catalog, a.DocumentSector, $"{a.ActivityCode} · {a.SectorType ?? "sector " + a.DocumentSector}",
                        a.IsCurrent)).ToList();
            case SiatCatalogNames.Legends:
                return (await db.Set<SiatLegend>().AsNoTracking().OrderBy(l => l.ActivityCode).ThenBy(l => l.Text).ToListAsync(ct))
                    .Select((l, i) => new SiatCatalogItemView(catalog, i + 1, $"{l.ActivityCode} · {l.Text}", l.IsCurrent)).ToList();
            default:
                Guard.That(SiatCatalogNames.Parametric.Contains(catalog), "siat.catalog_unknown",
                    $"El catálogo «{r.Catalog}» no es una de las paramétricas del SIN.");
                return await db.Set<SiatCatalogItem>().AsNoTracking().Where(i => i.Catalog == catalog)
                    .OrderByDescending(i => i.IsCurrent).ThenBy(i => i.Code)
                    .Select(i => new SiatCatalogItemView(i.Catalog, i.Code, i.Description, i.IsCurrent)).ToListAsync(ct);
        }
    }
}

public sealed class GetSiatActivitiesHandler(IMinvDbContext db) : IRequestHandler<GetSiatActivitiesQuery, IReadOnlyList<SiatActivityView>>
{
    public Task<IReadOnlyList<SiatActivityView>> Handle(GetSiatActivitiesQuery request, CancellationToken ct) => SiatActivityViews.AllAsync(db, ct);
}

internal static class SiatActivityViews
{
    /// <summary>Actividades del NIT (vigentes primero) con los documentos sector que tienen habilitados.</summary>
    public static async Task<IReadOnlyList<SiatActivityView>> AllAsync(IMinvDbContext db, CancellationToken ct)
    {
        var activities = await db.Set<SiatActivity>().AsNoTracking().ToListAsync(ct);
        var sectors = (await db.Set<SiatActivitySector>().AsNoTracking().Where(s => s.IsCurrent).ToListAsync(ct)).ToLookup(s => s.ActivityCode);
        return activities.OrderByDescending(a => a.IsCurrent).ThenBy(a => a.ActivityType == "P" ? 0 : 1).ThenBy(a => a.Code)
            .Select(a => new SiatActivityView(a.Code, a.Description, a.ActivityType, a.IsCurrent,
                sectors[a.Code].Select(s => s.DocumentSector).Distinct().Order().ToList())).ToList();
    }
}

public sealed class SearchSiatProductsHandler(IMinvDbContext db) : IRequestHandler<SearchSiatProductsQuery, IReadOnlyList<SiatProductView>>
{
    public async Task<IReadOnlyList<SiatProductView>> Handle(SearchSiatProductsQuery r, CancellationToken ct)
    {
        var max = Math.Clamp(r.Max, 1, 1000);
        var activity = r.ActivityCode?.Trim();
        var query = db.Set<SiatProduct>().AsNoTracking();
        if (!string.IsNullOrEmpty(activity))
        {
            query = query.Where(p => p.ActivityCode == activity);
        }
        var products = await query.ToListAsync(ct);
        // Búsqueda sin acentos: todas las palabras del texto (o su raíz) deben aparecer en la descripción o en el código
        var words = SiatWords.Normalize(r.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return products
            .Select(p => new { Product = p, Text = SiatWords.Normalize(p.Description) + " " + p.ProductCode.ToString(CultureInfo.InvariantCulture) })
            .Where(x => words.All(w => x.Text.Contains(w, StringComparison.Ordinal) || x.Text.Contains(SiatWords.Stem(w), StringComparison.Ordinal)))
            .OrderByDescending(x => x.Product.IsCurrent).ThenBy(x => x.Product.ActivityCode).ThenBy(x => x.Product.ProductCode)
            .Take(max)
            .Select(x => new SiatProductView(x.Product.ActivityCode, x.Product.ProductCode, x.Product.Description, x.Product.IsCurrent)).ToList();
    }
}

public sealed class GetHomologationHandler(IMinvDbContext db) : IRequestHandler<GetHomologationQuery, HomologationView>
{
    public async Task<HomologationView> Handle(GetHomologationQuery request, CancellationToken ct)
    {
        var products = await (from p in db.Set<Product>().AsNoTracking()
                              join c in db.Set<Category>().AsNoTracking() on p.CategoryId equals c.Id
                              select new { p.Id, p.Code, p.Name, Category = c.Name, p.IsActive }).ToListAsync(ct);
        var homologated = await db.Set<ProductSiatCode>().AsNoTracking().ToDictionaryAsync(h => h.ProductId, ct);
        // Solo las descripciones de los códigos SIN usados (el catálogo real tiene más de 21 000 filas)
        var usedCodes = homologated.Values.Select(h => h.SinProductCode).Distinct().ToList();
        var sinProducts = (await db.Set<SiatProduct>().AsNoTracking().Where(p => usedCodes.Contains(p.ProductCode)).ToListAsync(ct))
            .GroupBy(p => (p.ActivityCode, p.ProductCode)).ToDictionary(g => g.Key, g => g.First().Description);
        var sinUnits = await CatalogAsync(SiatCatalogNames.UnitsOfMeasure, ct);
        var sinMethods = await CatalogAsync(SiatCatalogNames.PaymentMethods, ct);
        var unitCodes = await db.Set<UnitSiatCode>().AsNoTracking().ToDictionaryAsync(u => u.UnitId, u => u.SinUnitCode, ct);
        var methodCodes = await db.Set<PaymentMethodSiatCode>().AsNoTracking().ToDictionaryAsync(m => m.PaymentMethodId, m => m.SinPaymentMethodCode, ct);
        var units = await db.Set<UnitOfMeasure>().AsNoTracking().OrderBy(u => u.Code).ToListAsync(ct);
        var methods = await db.Set<PaymentMethod>().AsNoTracking().OrderBy(m => m.Code).ToListAsync(ct);
        string? Description(IReadOnlyList<SiatCatalogItemView> items, int? code) => code is { } c ? items.FirstOrDefault(i => i.Code == c)?.Description : null;

        var productRows = products.OrderByDescending(p => p.IsActive).ThenBy(p => homologated.ContainsKey(p.Id) ? 1 : 0).ThenBy(p => p.Code)
            .Select(p =>
            {
                var h = homologated.GetValueOrDefault(p.Id);
                return new ProductHomologationRow(p.Id, p.Code, p.Name, p.Category, h?.ActivityCode, h?.SinProductCode,
                    h is null ? null : sinProducts.GetValueOrDefault((h.ActivityCode, h.SinProductCode)), p.IsActive);
            }).ToList();
        return new HomologationView(productRows,
            units.Select(u =>
            {
                int? code = unitCodes.TryGetValue(u.Id, out var c) ? c : null;
                return new UnitHomologationRow(u.Id, u.Code, u.Name, code, Description(sinUnits, code));
            }).ToList(),
            methods.Select(m =>
            {
                int? code = methodCodes.TryGetValue(m.Id, out var c) ? c : null;
                return new PaymentMethodHomologationRow(m.Id, m.Code, m.Name, code, Description(sinMethods, code));
            }).ToList(),
            sinUnits.Where(i => i.IsCurrent).ToList(), sinMethods.Where(i => i.IsCurrent).ToList(), await SiatActivityViews.AllAsync(db, ct),
            products.Count(p => p.IsActive && !homologated.ContainsKey(p.Id)));
    }

    private async Task<IReadOnlyList<SiatCatalogItemView>> CatalogAsync(string catalog, CancellationToken ct) =>
        await db.Set<SiatCatalogItem>().AsNoTracking().Where(i => i.Catalog == catalog).OrderBy(i => i.Code)
            .Select(i => new SiatCatalogItemView(i.Catalog, i.Code, i.Description, i.IsCurrent)).ToListAsync(ct);
}

public sealed class SaveProductHomologationValidator : AbstractValidator<SaveProductHomologationCommand>
{
    public SaveProductHomologationValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Elija al menos un producto para homologar.");
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.Sku).NotEmpty().WithMessage("Cada fila necesita el SKU del producto.");
            i.RuleFor(x => x.ActivityCode).NotEmpty().WithMessage("Cada producto necesita su actividad económica.");
            i.RuleFor(x => x.SinProductCode).GreaterThan(0).WithMessage("Cada producto necesita su código de producto del SIN.");
        });
    }
}

public sealed class SaveProductHomologationHandler(IMinvDbContext db, ITenantContext tenant) : IRequestHandler<SaveProductHomologationCommand, string>
{
    public async Task<string> Handle(SaveProductHomologationCommand r, CancellationToken ct)
    {
        var activities = await db.Set<SiatActivity>().AsNoTracking().ToDictionaryAsync(a => a.Code, ct);
        var created = 0;
        var changed = 0;
        foreach (var item in r.Items)
        {
            var sku = item.Sku.Trim().ToUpperInvariant();
            var product = await db.Set<Product>().FirstOrDefaultAsync(p => p.Code == sku, ct)
                          ?? await (from v in db.Set<ProductVariant>()
                                    join p in db.Set<Product>() on v.ProductId equals p.Id
                                    where v.Sku == sku
                                    select p).FirstOrDefaultAsync(ct)
                          ?? throw new NotFoundException($"El producto {sku} no existe en el catálogo.");
            var activity = item.ActivityCode.Trim();
            // La actividad debe ser del NIT (catálogo «Actividades» sincronizado; errores 1016/2010 del SIN)
            if (!activities.TryGetValue(activity, out var known))
            {
                throw new DomainException("siat.activity_unknown",
                    $"{sku}: la actividad {activity} no es una actividad económica de la empresa (sincronice los catálogos del SIN).");
            }
            Guard.That(known.IsCurrent, "siat.activity_retired", $"{sku}: la actividad {activity} ya no está vigente en el Padrón.");
            // El producto SIN debe existir EN ESA actividad (errores 1017/2011: producto no relacionado con la actividad)
            var sinProduct = await db.Set<SiatProduct>().AsNoTracking().FirstOrDefaultAsync(p => p.ActivityCode == activity && p.ProductCode == item.SinProductCode, ct);
            if (sinProduct is null)
            {
                var elsewhere = await db.Set<SiatProduct>().AsNoTracking().Where(p => p.ProductCode == item.SinProductCode)
                    .Select(p => p.ActivityCode).Distinct().OrderBy(a => a).Take(5).ToListAsync(ct);
                throw new DomainException("siat.product_activity", elsewhere.Count == 0
                    ? $"{sku}: el producto {item.SinProductCode} no existe en el catálogo del SIN."
                    : $"{sku}: el producto SIN {item.SinProductCode} no pertenece a la actividad {activity} (es de {string.Join(", ", elsewhere)}).");
            }
            Guard.That(sinProduct.IsCurrent, "siat.product_retired", $"{sku}: el producto SIN {item.SinProductCode} ya no está vigente en el catálogo.");
            var current = await db.Set<ProductSiatCode>().FirstOrDefaultAsync(h => h.ProductId == product.Id, ct);
            if (current is null)
            {
                db.Set<ProductSiatCode>().Add(new ProductSiatCode(tenant.TenantId, product.Id, activity, item.SinProductCode));
                created++;
            }
            else if (current.ActivityCode != activity || current.SinProductCode != item.SinProductCode)
            {
                current.Assign(activity, item.SinProductCode);
                changed++;
            }
        }
        await db.SaveChangesAsync(ct);
        return $"✔ Homologación guardada: {created} producto(s) nuevo(s), {changed} reasignado(s), {r.Items.Count - created - changed} sin cambios.";
    }
}

public sealed class SaveUnitHomologationHandler(IMinvDbContext db, ITenantContext tenant) : IRequestHandler<SaveUnitHomologationCommand, string>
{
    public async Task<string> Handle(SaveUnitHomologationCommand r, CancellationToken ct)
    {
        var code = (r.UnitCode ?? string.Empty).Trim().ToUpperInvariant();
        var unit = await db.Set<UnitOfMeasure>().FirstOrDefaultAsync(u => u.Code == code, ct)
                   ?? throw new NotFoundException($"La unidad {code} no existe.");
        var sin = await SinCatalog.RequireAsync(db, SiatCatalogNames.UnitsOfMeasure, r.SinUnitCode, "unidad de medida", ct);
        var current = await db.Set<UnitSiatCode>().FirstOrDefaultAsync(u => u.UnitId == unit.Id, ct);
        if (current is null)
        {
            db.Set<UnitSiatCode>().Add(new UnitSiatCode(tenant.TenantId, unit.Id, r.SinUnitCode));
        }
        else
        {
            current.Assign(r.SinUnitCode);
        }
        await db.SaveChangesAsync(ct);
        return $"✔ Unidad {unit.Code} · {unit.Name} → {r.SinUnitCode} · {sin.Description} del SIN.";
    }
}

public sealed class SavePaymentMethodHomologationHandler(IMinvDbContext db, ITenantContext tenant) : IRequestHandler<SavePaymentMethodHomologationCommand, string>
{
    public async Task<string> Handle(SavePaymentMethodHomologationCommand r, CancellationToken ct)
    {
        var code = (r.PaymentMethodCode ?? string.Empty).Trim().ToUpperInvariant();
        var method = await db.Set<PaymentMethod>().FirstOrDefaultAsync(m => m.Code == code, ct)
                     ?? throw new NotFoundException($"El medio de pago {code} no existe.");
        var sin = await SinCatalog.RequireAsync(db, SiatCatalogNames.PaymentMethods, r.SinCode, "método de pago", ct);
        var current = await db.Set<PaymentMethodSiatCode>().FirstOrDefaultAsync(m => m.PaymentMethodId == method.Id, ct);
        if (current is null)
        {
            db.Set<PaymentMethodSiatCode>().Add(new PaymentMethodSiatCode(tenant.TenantId, method.Id, r.SinCode));
        }
        else
        {
            current.Assign(r.SinCode);
        }
        await db.SaveChangesAsync(ct);
        return $"✔ Medio de pago {method.Code} · {method.Name} → {r.SinCode} · {sin.Description} del SIN.";
    }
}

internal static class SinCatalog
{
    /// <summary>Código vigente de una paramétrica sincronizada (nunca un enum fijo: regla F-07).</summary>
    public static async Task<SiatCatalogItem> RequireAsync(IMinvDbContext db, string catalog, int code, string what, CancellationToken ct)
    {
        var item = await db.Set<SiatCatalogItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Catalog == catalog && i.Code == code, ct);
        if (item is null)
        {
            var synced = await db.Set<SiatCatalogItem>().AnyAsync(i => i.Catalog == catalog, ct);
            throw new DomainException("siat.catalog_code", synced
                ? $"El código {code} no existe en el catálogo de {what} del SIN."
                : $"El catálogo de {what} del SIN no está sincronizado: ejecute «Sincronizar catálogos».");
        }
        Guard.That(item.IsCurrent, "siat.catalog_retired", $"El código {code} ({item.Description}) ya no está vigente en el catálogo de {what} del SIN.");
        return item;
    }
}

public sealed class SuggestProductHomologationHandler(IMinvDbContext db) : IRequestHandler<SuggestProductHomologationQuery, IReadOnlyList<ProductHomologationInput>>
{
    public async Task<IReadOnlyList<ProductHomologationInput>> Handle(SuggestProductHomologationQuery r, CancellationToken ct)
    {
        var activity = (r.ActivityCode ?? string.Empty).Trim();
        Guard.That(await db.Set<SiatActivity>().AnyAsync(a => a.Code == activity && a.IsCurrent, ct), "siat.activity_unknown",
            $"La actividad {activity} no es una actividad vigente de la empresa (sincronice los catálogos del SIN).");
        var candidates = (await db.Set<SiatProduct>().AsNoTracking().Where(p => p.ActivityCode == activity && p.IsCurrent).ToListAsync(ct))
            .Select(p => new { p.ProductCode, Words = SiatWords.Words(p.Description) }).Where(c => c.Words.Count > 0).ToList();
        var pending = await (from p in db.Set<Product>().AsNoTracking()
                             join c in db.Set<Category>().AsNoTracking() on p.CategoryId equals c.Id
                             where p.IsActive && !db.Set<ProductSiatCode>().Any(h => h.ProductId == p.Id)
                             orderby p.Code
                             select new { p.Code, p.Name, Category = c.Name }).ToListAsync(ct);
        var suggestions = new List<ProductHomologationInput>();
        foreach (var product in pending)
        {
            // Las palabras del nombre pesan el doble que las de la categoría; a igual puntaje gana la descripción más específica
            var nameWords = SiatWords.Words(product.Name);
            var categoryWords = SiatWords.Words(product.Category).Except(nameWords).ToList();
            var best = candidates
                .Select(c => new
                {
                    c.ProductCode,
                    Score = (2 * nameWords.Count(w => c.Words.Any(x => SiatWords.Match(w, x)))) + categoryWords.Count(w => c.Words.Any(x => SiatWords.Match(w, x))),
                    Coverage = (double)c.Words.Count(x => nameWords.Concat(categoryWords).Any(w => SiatWords.Match(w, x))) / c.Words.Count,
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score).ThenByDescending(x => x.Coverage).ThenBy(x => x.ProductCode)
                .FirstOrDefault();
            if (best is not null)
            {
                suggestions.Add(new ProductHomologationInput(product.Code, activity, best.ProductCode));
            }
        }
        return suggestions;
    }
}
