using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Sales;
using MINV.Application.Tech;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Service;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Seeding.Tecnologia;

namespace MINV.Infrastructure.Tests.Tech;

/// <summary>
/// V4.2 · La empresa de prueba Tech Zone Gaming S.R.L. (regla T-09): el catálogo embebido es válido (y uno alterado falla
/// al cargar con la lista de errores) y la carga usa TODO lo nuevo con los casos de uso: fichas técnicas con herencia y
/// marcas, series e IMEI en compras, transferencias, ventas y devoluciones (series en stock = stock en cada sucursal),
/// casos RMA en sus distintos estados, los 8 armados del catálogo (2 vendidos en la caja, los incompatibles marcados) y el
/// tablero del rubro.
/// </summary>
public sealed class V42TechSeedTests
{
    private static string CatalogPath => Path.Combine(V21MigrationTests.RepoRoot(), "src", "2. Infrastructure", "MINV.Infrastructure", "Seeding",
        "Tecnologia");

    [Fact]
    public void El_catalogo_de_tecnologia_embebido_es_valido_y_completo()
    {
        var catalog = TechSeedCatalog.Current;
        Assert.Equal("Tech Zone Gaming S.R.L.", catalog.Company.Name);
        Assert.Equal("TECHZONE", catalog.Company.Code);
        Assert.Equal(["CM", "CB", "SC"], catalog.Company.Branches.Select(b => b.Code));
        Assert.Equal([4741100, 4741200, 4742100], catalog.Company.Activities.Select(a => a.Code));
        Assert.Equal(35, catalog.Categories.Count);
        Assert.Equal(176, catalog.Specs.Count);
        Assert.Equal(159, catalog.Products.Count);
        Assert.Equal(103, catalog.Products.Count(p => p.TracksSerials && p.SerialKind == SerialKind.Serial));
        Assert.Equal(2, catalog.Products.Count(p => p.TracksSerials && p.SerialKind == SerialKind.Imei));
        Assert.Equal(4, catalog.Products.Count(p => p.IsService));
        Assert.Equal(8, catalog.Suppliers.Count);
        Assert.Equal(30, catalog.Customers.Count);
        Assert.Equal(8, catalog.Builds.Count);
        Assert.Equal(2, catalog.Builds.Count(b => b.MarkedIncompatible));
        Assert.Equal(31, catalog.SinProducts.Count);
        // El «costo» del JSON trae el IVA incluido: el producto y las compras usan el costo NETO, el 87 % del importe (en Bolivia
        // el crédito fiscal es el 13 % de la factura): 1702 × 0,87 = 1480,74; y así el margen sobre el precio neto (el 87 % del
        // precio, el que muestra el catálogo) es el «margen_pct» del JSON (16,9 %)
        Assert.True(catalog.PricesIncludeVat);
        Assert.Equal(0.13m, catalog.VatRate);
        var ryzen = catalog.Product("CPU-AMD-7600");
        Assert.Equal(1480.74m, ryzen.Cost);
        Assert.Equal(2049m, ryzen.Price);
        Assert.Equal(0.169m, ryzen.ListMargin);
        Assert.Equal(1702m, FiscalRules.InvoiceForNetCost(ryzen.Cost));   // la factura del proveedor de una unidad: el costo con IVA
        Assert.All(catalog.Products, p => Assert.InRange((p.Price * 0.87m - p.Cost) / (p.Price * 0.87m) - p.ListMargin, -0.001m, 0.001m));
        Assert.DoesNotContain(catalog.Products, p => p.ListMargin < 0.15m);   // ninguno con «margen bajo» (< 15 %) en el catálogo
        // Cada producto con su ilustración propia (dibujo sin marcas, regla T-09) y su proveedor (salvo los servicios)
        Assert.All(catalog.Products, p => Assert.True(ProductImageLibrary.Get(p.Image) is { Length: > 1000 }, p.Sku));
        Assert.All(catalog.Products.Where(p => !p.IsService), p => Assert.NotNull(catalog.SupplierOf(p)));
        Assert.Equal(catalog.Products.Select(p => p.Image).Distinct().Order(), ProductImageLibrary.Kinds.Intersect(catalog.Products.Select(p => p.Image)).Order());
        // Las claves de compatibilidad son las constantes del dominio (regla T-01) y los armados usan ranuras conocidas
        Assert.All(catalog.Specs.Where(s => s.CompatibilityKey is not null), s => Assert.Contains(s.CompatibilityKey, CompatibilityKeys.All));
        Assert.All(catalog.Builds.SelectMany(b => b.Lines), l => Assert.NotNull(catalog.SlotOf(l)));
    }

    [Fact]
    public void Un_catalogo_alterado_falla_al_cargar_con_la_lista_de_errores()
    {
        var json = File.ReadAllText(Path.Combine(CatalogPath, "catalogo-tecnologia.json"));
        var csv = File.ReadAllText(Path.Combine(CatalogPath, "catalogo-productos-sin-tecnologia.csv"));
        Assert.Equal(159, TechSeedCatalog.Load(json, csv).Products.Count);   // el archivo del repositorio es el embebido

        var broken = json.Replace("\"socket\": \"AM5\"", "\"socket\": \"AM9\"", StringComparison.Ordinal)
            .Replace("\"sku\": \"CPU-AMD-7600\"", "\"sku\": \"CPU-INT-14400F\"", StringComparison.Ordinal)
            .Replace("\"codigo_producto\": 1003591", "\"codigo_producto\": 9999999", StringComparison.Ordinal)
            .Replace("\"margen_pct\": 16.9,", "\"margen_pct\": 26.9,", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => TechSeedCatalog.Load(broken, csv));
        Assert.Contains("«AM9» no es un valor válido de «socket»", error.Message, StringComparison.Ordinal);
        Assert.Contains("SKU repetido: CPU-INT-14400F", error.Message, StringComparison.Ordinal);
        Assert.Contains("el producto SIN 9999999 no está en la actividad", error.Message, StringComparison.Ordinal);
        Assert.Contains("no coincide con «margen_pct»", error.Message, StringComparison.Ordinal);   // costo, precio y margen coherentes
    }

    [Fact]
    public async Task La_empresa_de_prueba_usa_fichas_series_rma_y_armados_con_los_casos_de_uso()
    {
        var (sp, result) = await LocalDataSeederTests.SeedAsync(days: 16, tenant: "TECH", seed: 11);
        var tech = Assert.IsType<SeedTech>(result.Tech);
        Assert.Equal(35, tech.Categories);
        Assert.Equal(176, tech.SpecDefinitions);
        Assert.Equal(TechSeedCatalog.Current.Products.Sum(p => p.Specs.Values.Sum(v => v.Count)), tech.SpecValues);
        Assert.Equal(40, tech.Brands);
        Assert.Equal(105, tech.SerializedProducts);
        Assert.True(tech.Serials > 1000 && tech.SerialsInStock > 500, $"{tech.Serials} series, {tech.SerialsInStock} en stock");
        Assert.InRange(tech.WarrantyClaims, 6, 8);
        // Los 8 del catálogo + 2 reservas web de la V6 (una activa y una vencida) + los 2 armados que reservaron las cuentas de cliente
        // (V7; los carritos no son armados: van en SeedWeb)
        Assert.Equal(12, tech.PcBuilds);
        Assert.Equal(2, result.Web!.AccountBuilds);
        Assert.Equal(2, tech.PcBuildsSold);
        Assert.Equal((6, 1, 1), (tech.PcBuildsPublished, tech.WebReservationsActive, tech.WebReservationsExpired));   // V6
        Assert.True(tech.Returns >= 3, $"Solo {tech.Returns} devoluciones");

        var admin = result.Users.First(u => u.RoleCode == MINV.Domain.Iam.RoleCodes.Admin);
        var (scope, m) = await LocalDataSeederTests.SignInAsync(sp, admin, "TECH");
        using (scope)
        {
            // En cada sucursal las series en stock de cada producto serializado son su stock (lo que vigila v_serial_breaches)
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            Assert.Equal(0, await SerialBreachesAsync(db));

            // Ficha técnica con marca, garantía y control por serie; IMEI válidos; facetas y filtro por plataforma (T-01, T-07)
            var cpu = await m.Send(new GetProductTechQuery("CPU-AMD-7600"));
            Assert.Equal("AMD", cpu.Brand);
            Assert.True(cpu.TrackSerials);
            Assert.Equal(36, cpu.WarrantyMonths);
            Assert.Contains(cpu.Specs, s => s.Code == "socket" && s.Values.SequenceEqual(["AM5"]) && s.CompatibilityKey == CompatibilityKeys.CpuSocket);
            Assert.Contains(cpu.Specs, s => s.Code == "condicion");   // heredada de Componentes
            var imeis = await m.Send(new SearchSerialsQuery(Sku: "RED-TPL-MR600"));
            Assert.NotEmpty(imeis);
            Assert.All(imeis, s => Assert.True(s.Kind == SerialKind.Imei && Imei.IsValid(s.Serial), s.Serial));
            var facets = await m.Send(new GetSpecFacetsQuery("CPU"));
            Assert.Contains(facets, f => f.Code == "socket" && f.Values.Any(v => v.Value == "AM5"));
            var ps5 = await m.Send(new SearchTechProductsQuery(Platform: "PS5"));
            Assert.NotEmpty(ps5);
            Assert.All(ps5, p => Assert.Contains("PS5", p.Platforms));

            // Garantías: casos en distintos estados, uno con reposición y la unidad defectuosa devuelta al proveedor (T-05)
            var claims = await m.Send(new GetWarrantyClaimsQuery());
            var states = claims.Select(c => c.Status).ToHashSet();
            Assert.Superset(new HashSet<WarrantyClaimStatus> { WarrantyClaimStatus.Received, WarrantyClaimStatus.Diagnosing, WarrantyClaimStatus.SentToSupplier,
                WarrantyClaimStatus.Rejected, WarrantyClaimStatus.Delivered }, states);
            Assert.Contains(claims, c => c.ReplacementSerial is not null);
            Assert.Contains(claims, c => c.Status == WarrantyClaimStatus.Rejected && c.Resolution!.Contains("mal uso", StringComparison.Ordinal));
            Assert.All(claims, c => Assert.Matches(@"^RMA-(CM|CB|SC)-\d{6}$", c.Number));
            Assert.NotEmpty(await m.Send(new SearchSerialsQuery(Status: SerialNumberStatus.ReturnedToSupplier)));

            // Armados: los 8 del catálogo con sus números, 2 cobrados en la caja, uno incompatible en borrador y otro cotizado
            // con la confirmación del vendedor (T-06)
            // V6: sin las reservas web; V7: sin los carritos (el de mostrador también es del canal del escritorio)
            var builds = await m.Send(new GetPcBuildsQuery(Channel: PcBuildChannel.Desktop, Kind: PcBuildKind.Build));
            Assert.Equal(TechSeedCatalog.Current.Builds.Select(b => b.Number).Order(), builds.Select(b => b.Number).Order());
            Assert.Equal(2, builds.Count(b => b.Status == PcBuildStatus.Sold && b.InvoiceNumber is not null));
            Assert.Contains(builds, b => b is { Status: PcBuildStatus.Draft, IsCompatible: false });
            Assert.Contains(builds, b => b is { Status: PcBuildStatus.Quoted, QuotedWithErrors: true, IsCompatible: false });
            var sold = builds.First(b => b.Status == PcBuildStatus.Sold);
            var lines = await m.Send(new GetSaleLinesQuery(sold.InvoiceNumber!));
            Assert.Contains(lines, l => l.Serials is { Count: > 0 });

            // Tablero del rubro
            var dashboard = await m.Send(new GetTechDashboardQuery(30));
            Assert.True(dashboard.SerialsInStock > 0 && dashboard.OpenClaims > 0);
            Assert.Equal(2, dashboard.BuildsSold);
            Assert.NotEmpty(dashboard.SalesByCategory);
            Assert.NotEmpty(dashboard.SalesByPlatform);
            Assert.Equal(0, dashboard.SerializedWithoutSerials);
        }
    }

    /// <summary>Variantes serializadas cuyas series en stock no coinciden con su stock en alguna sucursal.</summary>
    internal static async Task<int> SerialBreachesAsync(MinvWriteDbContext db)
    {
        var stock = await (from l in db.StockLevels
                           join b in db.Batches on l.BatchId equals b.Id
                           join v in db.ProductVariants on b.VariantId equals v.Id
                           join p in db.Products on v.ProductId equals p.Id
                           where p.TrackingMode == TrackingMode.Serial
                           group l.QuantityOnHand by new { l.BranchId, b.VariantId } into g
                           select new { g.Key.BranchId, g.Key.VariantId, OnHand = g.Sum() }).ToListAsync();
        var serials = await (from s in db.SerialNumbers
                             join l in db.StockLevels on s.StockLevelId equals (Guid?)l.Id
                             where s.Status == SerialNumberStatus.InStock
                             group s by new { l.BranchId, s.VariantId } into g
                             select new { g.Key.BranchId, g.Key.VariantId, Count = g.Count() }).ToListAsync();
        return stock.Count(s => s.OnHand != (serials.FirstOrDefault(x => x.BranchId == s.BranchId && x.VariantId == s.VariantId)?.Count ?? 0))
               + serials.Count(x => !stock.Any(s => s.BranchId == x.BranchId && s.VariantId == x.VariantId));
    }
}
