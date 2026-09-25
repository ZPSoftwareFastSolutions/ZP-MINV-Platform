using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Accounting;
using MINV.Application.Catalog;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Inventory.Queries;
using MINV.Application.Partners;
using MINV.Application.Purchasing;
using MINV.Application.Reports;
using MINV.Application.Sales;
using MINV.Domain.Iam;
using MINV.Domain.Purchasing;
using MINV.Infrastructure.Seeding;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// Datos de prueba de la base LOCAL (<c>minv datos-prueba</c>) sobre la base en memoria: el generador recorre casi todos
/// los casos de uso nuevos (usuarios, catálogo con imágenes, clientes, proveedores, caja, ventas, anulaciones, compras,
/// recepciones, asientos, toma física) con la tubería completa, y el resultado debe ser coherente.
/// </summary>
public sealed class LocalDataSeederTests
{
    private static async Task<(ServiceProvider Services, SeedResult Result)> SeedAsync(int days = 8)
    {
        var services = new ServiceCollection();
        services.AddMinvApplication();
        services.AddMinvDemoInfrastructure();
        var sp = services.BuildServiceProvider();
        var result = await sp.GetRequiredService<LocalDataSeeder>().SeedAsync(new SeedOptions("PRUEBA", Days: days, Seed: 7), _ => { });
        return (sp, result);
    }

    private static async Task<(IServiceScope Scope, IMediator Mediator)> SignInAsync(ServiceProvider sp, SeedUser user)
    {
        var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.Send(new LoginCommand("PRUEBA", user.Email, user.Password, "PRUEBAS", "test"));
        return (scope, mediator);
    }

    [Fact]
    public async Task Genera_una_empresa_completa_y_coherente_con_usuarios_de_cada_rol()
    {
        var (sp, result) = await SeedAsync();
        Assert.Equal(9, result.Users.Count);
        Assert.All(RoleCodes.All, r => Assert.Contains(result.Users, u => u.RoleCode == r.Code));
        Assert.All(result.Users, u => Assert.Matches(@"^[A-Za-z]+-\d{4}$", u.Password));
        Assert.Equal(61, result.Products);
        Assert.True(result.Tickets > 20, $"Solo {result.Tickets} ventas");
        Assert.True(result.JournalEntries > result.Tickets);

        var admin = result.Users.First(u => u.RoleCode == RoleCodes.Admin);
        var (scope, m) = await SignInAsync(sp, admin);
        using (scope)
        {
            // Catálogo: todos con imagen, precio y posición
            var catalog = await m.Send(new GetCatalogQuery());
            Assert.Equal(61, catalog.Count);
            Assert.All(catalog, c => Assert.True(c.HasImage && c.SalePrice > c.UnitCost && c.BinCode is not null, c.Sku));
            Assert.Equal(61, (await m.Send(new GetProductImagesQuery())).Count);

            // Contabilidad: partida doble en todo el libro y resultado con ingresos, costo y gastos
            var chart = await m.Send(new GetChartOfAccountsQuery());
            var postable = chart.Where(a => a.IsPostable).ToList();
            Assert.Equal(postable.Sum(a => a.Debit), postable.Sum(a => a.Credit));
            var statement = await m.Send(new GetIncomeStatementQuery(result.From, result.To));
            Assert.True(statement.TotalRevenue > 0 && statement.TotalCosts > 0);
            Assert.Equal(statement.TotalRevenue - statement.TotalCosts - statement.TotalExpenses, statement.NetIncome);

            // Ventas: cada venta simulada es una factura; el reporte cuadra con el historial
            var sales = await m.Send(new GetSalesQuery(result.From, result.To));
            Assert.Equal(result.Tickets, sales.Count);
            var report = await m.Send(new GetSalesReportQuery(result.From, result.To));
            Assert.Equal(sales.Count(s => s.Status == MINV.Domain.Sales.InvoiceStatus.Issued), report.Tickets);
            Assert.True(report.MarginPercent is > 15 and < 45, $"Margen {report.MarginPercent} %");

            // Stock: nunca negativo (poka-yoke) y con productos en alerta para el pedido sugerido
            var projection = await m.Send(new GetStockProjectionQuery());
            Assert.All(projection.Result.Stock, s => Assert.True(s.Stock >= 0, s.Sku));

            // Compras: recibidas, aprobadas por recibir y un borrador
            var orders = await m.Send(new GetPurchaseOrdersQuery());
            Assert.Contains(orders, o => o.Status == PurchaseOrderStatus.Received);
            Assert.Contains(orders, o => o.Status == PurchaseOrderStatus.Draft);
            Assert.Equal(8, (await m.Send(new GetSuppliersQuery())).Count);
            Assert.Equal(29, (await m.Send(new GetCustomersQuery())).Customers.Count);
            Assert.Equal(9, (await m.Send(new GetUsersQuery())).Count);
        }

        // El cajero tiene su caja abierta hoy y productos para vender
        var cashier = result.Users.First(u => u.RoleCode == RoleCodes.Cashier);
        var (cs, cm) = await SignInAsync(sp, cashier);
        using (cs)
        {
            var state = await cm.Send(new GetPosStateQuery());
            Assert.NotNull(state.Session);
            Assert.Equal(61, (await cm.Send(new GetSellableProductsQuery())).Count);
        }
    }

    [Fact]
    public async Task Cada_rol_tiene_sus_funciones_y_la_tuberia_bloquea_las_ajenas()
    {
        var (sp, result) = await SeedAsync(days: 3);
        var cashier = result.Users.First(u => u.RoleCode == RoleCodes.Cashier);
        var (cs, cm) = await SignInAsync(sp, cashier);
        using (cs)
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => cm.Send(new GetChartOfAccountsQuery()));
            await Assert.ThrowsAsync<AccessDeniedException>(() => cm.Send(new GetUsersQuery()));
            await Assert.ThrowsAsync<AccessDeniedException>(() => cm.Send(new CreateSuggestedPurchaseOrdersCommand()));
        }
        var keeper = result.Users.First(u => u.RoleCode == RoleCodes.Warehouse);
        var (ks, km) = await SignInAsync(sp, keeper);
        using (ks)
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => km.Send(new GetPosStateQuery()));
            Assert.NotEmpty(await km.Send(new GetPurchaseOrdersQuery()));
        }
        var manager = result.Users.First(u => u.RoleCode == RoleCodes.Management);
        var (ms, mm) = await SignInAsync(sp, manager);
        using (ms)
        {
            var number = await mm.Send(new CreateJournalEntryCommand(result.To, "Pago de servicios de prueba",
                [new JournalLineSpec("6.1.03", 150, 0), new JournalLineSpec(MINV.Domain.Accounting.AccountCodes.Cash, 0, 150)]));
            Assert.StartsWith("AS-", number, StringComparison.Ordinal);
            await Assert.ThrowsAsync<RequestValidationException>(() => mm.Send(new CreateJournalEntryCommand(result.To, "Descuadrado",
                [new JournalLineSpec("6.1.03", 150, 0), new JournalLineSpec(MINV.Domain.Accounting.AccountCodes.Cash, 0, 100)])));
        }
    }

    [Fact]
    public async Task La_demostracion_trae_imagenes_y_precios_para_el_punto_de_venta()
    {
        var (sp, demo) = await DemoWorkspaceTests.PrepareAsync();
        var (scope, mediator, _) = await DemoWorkspaceTests.SignInAsync(sp, demo);
        using (scope)
        {
            Assert.Equal(34, (await mediator.Send(new GetProductImagesQuery())).Count);
            Assert.All(await mediator.Send(new GetCatalogQuery()), c => Assert.True(c.HasImage && c.SalePrice > 0, c.Sku));
        }
    }
}
