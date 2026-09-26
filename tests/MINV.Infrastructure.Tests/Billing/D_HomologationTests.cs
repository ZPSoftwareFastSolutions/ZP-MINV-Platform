using Microsoft.EntityFrameworkCore;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>
/// V4.1 · Catálogos sincronizados y homologación (investigación 04 §5-§7): producto → (actividad, producto SIN) validado
/// contra lo sincronizado (el producto SIN debe ser de ESA actividad), unidades y medios de pago contra sus paramétricas,
/// búsqueda sin acentos y sugerencia por palabras.
/// </summary>
public sealed class D_HomologationTests
{
    private const string Hardware = "4752100";      // ferretería (simulador)
    private const string Construction = "4752400";  // materiales de construcción (simulador)
    private const string Computers = "4741100";     // V4.2: computadoras, periféricos y software (actividad principal del simulador)

    [Fact]
    public async Task Homologa_productos_unidades_y_medios_de_pago_contra_el_catalogo_sincronizado()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.ConfigureAsync(admin, enable: false, elAlto: false);
        await D_BillingTestHost.CreateProductAsync(admin, "FER-001", "Juego de destornilladores (6 piezas)", stock: 0);
        await D_BillingTestHost.CreateProductAsync(admin, "FER-002", "Tornillo drywall 6x1\" (caja x100)", stock: 0);
        await D_BillingTestHost.CreateProductAsync(admin, "SEG-001", "Guantes de nitrilo (par)", "SEG", stock: 0);
        await D_BillingTestHost.CreateProductAsync(admin, "FER-099", "Producto descontinuado", stock: 0, active: false);

        var before = await admin.Send(new GetHomologationQuery());
        Assert.Equal(3, before.PendingProducts);   // el inactivo no cuenta
        Assert.Equal(4, before.Products.Count);
        Assert.Equal(5, before.Activities.Count);   // V4.2: 3 de tecnología + 2 de ferretería (simulador)
        Assert.All(before.Activities, a => Assert.Equal([1, 24], a.Sectors));
        Assert.Contains(before.SinUnits, u => u.Code == 57 && u.Description == "UNIDAD (BIENES)");
        Assert.Contains(before.SinPaymentMethods, m => m.Code == 1 && m.Description == "EFECTIVO");

        // Válida: el producto SIN es de la actividad
        var ok = await admin.Send(new SaveProductHomologationCommand([new ProductHomologationInput("fer-001", Hardware, 1001903)]));
        Assert.Contains("1 producto(s) nuevo(s)", ok, StringComparison.Ordinal);
        // Producto SIN de OTRA actividad: rechazado (errores 1017/2011 del SIN)
        var other = await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new SaveProductHomologationCommand([new ProductHomologationInput("FER-002", Hardware, 1003362)])));
        Assert.Equal("siat.product_activity", other.Code);
        Assert.Contains(Construction, other.Message, StringComparison.Ordinal);
        Assert.Equal("siat.activity_unknown", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new SaveProductHomologationCommand([new ProductHomologationInput("FER-002", "9999999", 1001903)])))).Code);
        Assert.Equal("siat.product_activity", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new SaveProductHomologationCommand([new ProductHomologationInput("FER-002", Hardware, 42)])))).Code);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            admin.Send(new SaveProductHomologationCommand([new ProductHomologationInput("NO-EXISTE", Hardware, 1001903)])));
        await Assert.ThrowsAsync<RequestValidationException>(() => admin.Send(new SaveProductHomologationCommand([])));

        // Reasignación del mismo producto
        var moved = await admin.Send(new SaveProductHomologationCommand([new ProductHomologationInput("FER-001", Hardware, 1003450)]));
        Assert.Contains("1 reasignado(s)", moved, StringComparison.Ordinal);
        Assert.Equal(1003450, (await admin.Db.Set<ProductSiatCode>().AsNoTracking().SingleAsync()).SinProductCode);

        // Unidades y medios de pago contra sus paramétricas
        Assert.Contains("UNIDAD (BIENES)", await admin.Send(new SaveUnitHomologationCommand("und", 57)), StringComparison.Ordinal);
        Assert.Equal("siat.catalog_code", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveUnitHomologationCommand("UND", 999)))).Code);
        Assert.Contains("EFECTIVO", await admin.Send(new SavePaymentMethodHomologationCommand("efectivo", 1)), StringComparison.Ordinal);
        Assert.Contains("TARJETA", await admin.Send(new SavePaymentMethodHomologationCommand("TARJETA", 2)), StringComparison.Ordinal);
        await admin.Send(new SavePaymentMethodHomologationCommand("TARJETA", 10));   // reasigna (efectivo - tarjeta)

        var after = await admin.Send(new GetHomologationQuery());
        Assert.Equal(2, after.PendingProducts);
        var row = after.Products.Single(p => p.Sku == "FER-001");
        Assert.Equal((Hardware, 1003450), (row.ActivityCode, row.SinProductCode!.Value));
        Assert.StartsWith("Accesorios para Herramientas", row.SinProductDescription, StringComparison.Ordinal);
        Assert.Equal((57, "UNIDAD (BIENES)"), (after.Units.Single(u => u.Code == "UND").SinUnitCode!.Value, after.Units.Single(u => u.Code == "UND").SinUnitDescription));
        Assert.Null(after.Units.Single(u => u.Code == "KG").SinUnitCode);
        Assert.Equal(10, after.PaymentMethods.Single(m => m.Code == "TARJETA").SinCode);
        Assert.Equal("EFECTIVO - TARJETA", after.PaymentMethods.Single(m => m.Code == "TARJETA").SinDescription);
    }

    [Fact]
    public async Task Busca_sin_acentos_lista_catalogos_y_sugiere_la_homologacion_por_palabras()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.ConfigureAsync(admin, enable: false, elAlto: false);

        var found = await admin.Send(new SearchSiatProductsQuery(Hardware, "FIJACION"));
        Assert.Equal(1003451, Assert.Single(found).ProductCode);   // «Artículos para Fijación (Tornillos…)»
        Assert.Contains(await admin.Send(new SearchSiatProductsQuery(null, "destornillador")), p => p.ProductCode == 1001903);
        Assert.Equal(3, (await admin.Send(new SearchSiatProductsQuery(null, null, 3))).Count);
        Assert.All(await admin.Send(new SearchSiatProductsQuery(Construction, null)), p => Assert.Equal(Construction, p.ActivityCode));

        var methods = await admin.Send(new GetSiatCatalogQuery("tipo_metodo_pago"));
        Assert.Contains(methods, m => m.Code == 2 && m.Description == "TARJETA" && m.IsCurrent);
        Assert.NotEmpty(await admin.Send(new GetSiatCatalogQuery(SiatCatalogNames.Products)));
        Assert.Equal(30, (await admin.Send(new GetSiatCatalogQuery(SiatCatalogNames.Legends))).Count);   // 6 leyendas × 5 actividades
        Assert.Equal("siat.catalog_unknown", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new GetSiatCatalogQuery("NADA")))).Code);
        var activities = await admin.Send(new GetSiatActivitiesQuery());
        Assert.Equal(Computers, activities[0].Code);   // la principal (P) primero (V4.2: computadoras)
        Assert.Equal("P", activities[0].ActivityType);

        await D_BillingTestHost.CreateProductAsync(admin, "FER-001", "Juego de destornilladores (6 piezas)", stock: 0);
        await D_BillingTestHost.CreateProductAsync(admin, "FER-002", "Tornillo drywall 6x1\" (caja x100)", stock: 0);
        await D_BillingTestHost.CreateProductAsync(admin, "SEG-001", "Guantes de nitrilo (par)", "SEG", stock: 0);
        await D_BillingTestHost.CreateProductAsync(admin, "FER-003", "Zzyzx inexistente", stock: 0);
        var suggestions = await admin.Send(new SuggestProductHomologationQuery(Hardware));
        Assert.Equal(1001903, suggestions.Single(s => s.Sku == "FER-001").SinProductCode);   // herramientas de mano: destornilladores
        Assert.Equal(1003451, suggestions.Single(s => s.Sku == "FER-002").SinProductCode);   // fijación: tornillos
        Assert.Equal(1003452, suggestions.Single(s => s.Sku == "SEG-001").SinProductCode);   // seguridad: guantes
        // Sin palabras en común con el nombre, la categoría propone el genérico «accesorios de ferretería» (investigación 04 §6.4)
        Assert.Equal(1001912, suggestions.Single(s => s.Sku == "FER-003").SinProductCode);
        Assert.All(suggestions, s => Assert.Equal(Hardware, s.ActivityCode));

        // Lo sugerido se guarda tal cual (la sugerencia respeta la actividad) y ya no queda nada pendiente
        Assert.Contains("4 producto(s) nuevo(s)", await admin.Send(new SaveProductHomologationCommand(suggestions)), StringComparison.Ordinal);
        Assert.Empty(await admin.Send(new SuggestProductHomologationQuery(Hardware)));
        Assert.Equal("siat.activity_unknown", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SuggestProductHomologationQuery("0000000")))).Code);
    }
}
