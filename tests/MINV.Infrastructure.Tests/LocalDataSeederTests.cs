using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Accounting;
using MINV.Application.Catalog;
using MINV.Application.Common;
using MINV.Application.Corporate;
using MINV.Application.Iam;
using MINV.Application.Integration;
using MINV.Application.Inventory.Transfers;
using MINV.Application.Inventory.Queries;
using MINV.Application.Partners;
using MINV.Application.Purchasing;
using MINV.Application.Reports;
using MINV.Application.Sales;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
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
        Assert.Equal(12, result.Users.Count);   // administrador + 11 usuarios repartidos en 3 sucursales
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
            Assert.Equal(result.Tickets + result.ExternalOrders, sales.Count);   // caja + pedidos del e-commerce
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
            Assert.Equal(12, (await m.Send(new GetUsersQuery())).Count);
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
    public async Task V4_tres_sucursales_con_transferencias_en_transito_y_aislamiento_por_sucursal()
    {
        var (sp, result) = await SeedAsync(days: 10);
        Assert.Equal(["CM", "EA", "SC"], result.Branches);
        Assert.True(result.Transfers >= 4, $"Solo {result.Transfers} transferencias");
        Assert.StartsWith("minv_", result.ApiKeyToken, StringComparison.Ordinal);

        // Gerencia global: ve todas las sucursales, el stock consolidado y lo que está en tránsito (contado una vez)
        var manager = result.Users.First(u => u.RoleCode == RoleCodes.Management);
        var (ms, mm) = await SignInAsync(sp, manager);
        using (ms)
        {
            var branches = await mm.Send(new GetBranchesQuery());
            Assert.Equal(3, branches.Count);
            Assert.All(branches, b => Assert.True(b.IsVisible && b.StockValue > 0, b.Code));
            var transfers = await mm.Send(new GetTransfersQuery());
            Assert.Contains(transfers, t => t.Status == TransferStatus.Received);
            Assert.Contains(transfers, t => t.Status == TransferStatus.Dispatched);
            Assert.Contains(transfers, t => t.Status == TransferStatus.Pending);
            var stock = await mm.Send(new ConsolidatedStockQuery());
            Assert.Equal(3, stock.Branches.Count);
            Assert.True(stock.InTransitValue > 0);
            Assert.Contains(stock.Rows, r => r.InTransit > 0);
            Assert.All(stock.Rows, r => Assert.Equal(r.Total, r.ByBranch.Sum() + r.InTransit));
        }

        // Cajero de El Alto: solo ve su sucursal (ventas, stock y transferencias que llegan a ella)
        var cashierEa = result.Users.First(u => u.RoleCode == RoleCodes.Cashier && u.Branches == "EA");
        var (cs, cm) = await SignInAsync(sp, cashierEa);
        using (cs)
        {
            var sales = await cm.Send(new GetSalesQuery(result.From, result.To));
            Assert.NotEmpty(sales);
            Assert.All(sales, s => Assert.StartsWith("F-EA-", s.InvoiceNumber, StringComparison.Ordinal));
            var state = await cm.Send(new GetPosStateQuery());
            Assert.All(state.Registers, r => Assert.StartsWith("EA-", r.Code, StringComparison.Ordinal));
            var branches = await cm.Send(new GetBranchesQuery());
            Assert.Single(branches, b => b.IsVisible);
            Assert.Null(branches.First(b => b.Code == "CM").StockValue);
        }

        // Bodega de Santa Cruz: recibe la transferencia en tránsito; no puede despacharla ni anular las ajenas
        var keeperSc = result.Users.First(u => u.RoleCode == RoleCodes.Warehouse && u.Branches == "SC");
        var (ks, km) = await SignInAsync(sp, keeperSc);
        using (ks)
        {
            var inTransit = (await km.Send(new GetTransfersQuery(TransferStatus.Dispatched))).Single();
            Assert.True(inTransit.CanReceive);
            Assert.False(inTransit.CanDispatch);
            var detail = await km.Send(new GetTransferQuery(inTransit.Id));
            var line = detail.Lines[0];
            await Assert.ThrowsAsync<DomainException>(() => km.Send(new ReceiveTransferCommand(inTransit.Id,
                [new TransferReceiptInput(line.Sku, line.Quantity - 1)])));   // faltante sin motivo
            var received = await km.Send(new ReceiveTransferCommand(inTransit.Id,
                [new TransferReceiptInput(line.Sku, line.Quantity - 1, "Una unidad llegó rota")]));
            Assert.Contains("faltante", received.Message, StringComparison.Ordinal);
            var after = await km.Send(new GetTransferQuery(inTransit.Id));
            Assert.Equal(TransferStatus.Received, after.Header.Status);
            Assert.Equal(1, after.Lines[0].Shortage);
            var pending = (await km.Send(new GetTransfersQuery(TransferStatus.Pending))).ToList();
            Assert.Empty(pending);   // la pendiente va a El Alto: Santa Cruz no la ve
        }

        // La API Key del e-commerce: su canal y sus alcances (no puede administrar usuarios)
        using var api = sp.CreateScope();
        var principal = await api.ServiceProvider.GetRequiredService<Integration.ApiKeyAuthenticator>().AuthenticateAsync(result.ApiKeyToken, default);
        Assert.NotNull(principal);
        var apiMediator = api.ServiceProvider.GetRequiredService<IMediator>();
        var catalog = await apiMediator.Send(new GetApiCatalogQuery(1, 500));
        Assert.Equal(61, catalog.Total);
        await Assert.ThrowsAsync<AccessDeniedException>(() => apiMediator.Send(new GetUsersQuery()));
        Assert.Null(await sp.CreateScope().ServiceProvider.GetRequiredService<Integration.ApiKeyAuthenticator>()
            .AuthenticateAsync(result.ApiKeyToken[..^2] + "xx", default));
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
