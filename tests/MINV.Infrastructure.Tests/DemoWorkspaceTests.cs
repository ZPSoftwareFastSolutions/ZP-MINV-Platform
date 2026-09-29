using System.Diagnostics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Inventory.Movements;
using MINV.Application.Inventory.PhysicalCounts;
using MINV.Application.Inventory.Queries;
using MINV.Application.Tech;
using MINV.Domain.Accounting;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Infrastructure.Demo;
using MINV.Infrastructure.Seeding.Tecnologia;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// Modo demostración del cliente de escritorio. V4.2: la empresa de prueba Tech Zone Gaming S.R.L. generada en la base en
/// memoria con el MISMO generador que la base local (casos de uso de verdad, tubería MediatR completa: validación, RBAC,
/// series, auditoría) y con la facturación activa contra el simulador del SIN en memoria. También prueba las consultas que
/// usa la interfaz (ficha y kardex, tendencia, últimos movimientos, toma física, contraseña).
/// </summary>
public sealed class DemoWorkspaceTests
{
    internal static async Task<(ServiceProvider Services, DemoSession Demo)> PrepareAsync()
    {
        var services = new ServiceCollection();
        services.AddMinvApplication();
        services.AddMinvDemoInfrastructure();
        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var demo = await scope.ServiceProvider.GetRequiredService<DemoWorkspace>().PrepareAsync();
        return (sp, demo);
    }

    internal static async Task<(IServiceScope Scope, IMediator Mediator, LoginResult Login)> SignInAsync(ServiceProvider sp, DemoSession demo,
        string email = DemoWorkspace.AdminEmail, string? password = null)
    {
        var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var login = await mediator.Send(new LoginCommand(demo.TenantCode, email, password ?? demo.Password, "PRUEBAS", "test"));
        return (scope, mediator, login);
    }

    /// <summary>Un producto con existencia que NO lleva serie (los serializados se mueven con sus series, regla T-02).</summary>
    internal static bool WithoutSerial(string sku) => !TechSeedCatalog.Current.Product(sku).TracksSerials;

    [Fact]
    public async Task La_demostracion_es_Tech_Zone_Gaming_y_permite_ingresar_con_cada_rol()
    {
        var watch = Stopwatch.StartNew();
        var (sp, demo) = await PrepareAsync();
        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"La demostración tardó {watch.Elapsed.TotalSeconds:N1} s");
        Assert.Equal("TECHZONE", demo.TenantCode);
        Assert.Equal("Tech Zone Gaming S.R.L.", demo.CompanyName);
        Assert.Equal(RoleCodes.Admin, demo.Users[0].RoleCode);
        Assert.Equal(DemoWorkspace.AdminEmail, demo.Users[0].Email);
        // un acceso por rol (V6: el usuario técnico TIENDA_WEB no tiene contraseña de prueba: no está entre los accesos)
        Assert.Equal(RoleCodes.All.Where(r => r.Code != RoleCodes.Storefront).Select(r => r.Code).Order(), demo.Users.Select(u => u.RoleCode).Order());
        Assert.Equal(159, demo.Seed.Products);
        Assert.Equal(["CM", "CB", "SC"], demo.Seed.Branches);
        Assert.True(demo.Seed.Tickets > 30, $"Solo {demo.Seed.Tickets} ventas");
        Assert.DoesNotContain(demo.Password, demo.ToString(), StringComparison.Ordinal);

        var (scope, mediator, login) = await SignInAsync(sp, demo);
        using (scope)
        {
            Assert.Contains(RoleCodes.Admin, login.Roles);
            var workspace = await mediator.Send(new GetWorkspaceQuery());
            Assert.Equal("Tech Zone Gaming S.R.L.", workspace.CompanyName);
            Assert.Equal("ALM01", workspace.WarehouseCode);
            Assert.Equal(demo.Today, workspace.Today);
            var view = await mediator.Send(new GetStockProjectionQuery());
            Assert.Equal(159, view.Result.Stock.Count);
            Assert.True(view.Result.Movements > 300, $"Solo {view.Result.Movements} movimientos");   // 8 días de operación (más el saldo inicial)
            Assert.NotEmpty(view.Result.Alerts);
            // Edición Tecnología: series en stock, casos RMA y armados de PC para explorar
            Assert.NotEmpty(await mediator.Send(new SearchSerialsQuery(Status: SerialNumberStatus.InStock, Max: 5)));
            // Los indicadores de «Series e IMEI» cuentan TODAS las series (la lista se limita a las más recientes)
            var totals = await mediator.Send(new GetSerialSummaryQuery());
            Assert.True(totals.Total > 1000, $"Solo {totals.Total} series");
            Assert.Equal((await mediator.Send(new SearchSerialsQuery(Status: SerialNumberStatus.InStock, Max: 5000))).Count, totals.InStock);
            Assert.Equal((await mediator.Send(new SearchSerialsQuery(Status: SerialNumberStatus.Sold, Max: 5000))).Count, totals.Sold);
            Assert.InRange(totals.SoldInWarranty, 1, totals.Sold);
            var recent = await mediator.Send(new SearchSerialsQuery(Max: 20));
            Assert.Equal(recent.OrderByDescending(r => r.ReceivedAt).Select(r => r.Serial), recent.Select(r => r.Serial));   // las más recientes primero
            var bySku = await mediator.Send(new SearchSerialsQuery(recent[0].Sku.ToLowerInvariant(), Max: 5000));   // la búsqueda también encuentra por SKU
            Assert.NotEmpty(bySku);
            Assert.All(bySku, r => Assert.Contains(recent[0].Sku, r.Sku + " " + r.Serial, StringComparison.OrdinalIgnoreCase));
            Assert.NotEmpty(await mediator.Send(new GetWarrantyClaimsQuery()));
            // V6: + 2 reservas web aparte; V7: los carritos (también el de mostrador, del canal del escritorio) aparte
            Assert.Equal(8, (await mediator.Send(new GetPcBuildsQuery(Channel: MINV.Domain.Sales.PcBuildChannel.Desktop,
                Kind: MINV.Domain.Sales.PcBuildKind.Build))).Count);
            // V7 · La tienda web de la demostración (regla P-13): cuentas de cliente con sus reservas, carritos y correos en cola
            Assert.Equal((2, 4, 1, 1), (demo.Seed.Web!.Accounts, demo.Seed.Web.AccountReservations, demo.Seed.Web.ActiveCarts, demo.Seed.Web.CounterCarts));
            Assert.True(demo.Seed.Web.QueuedMails >= 7, $"Solo {demo.Seed.Web.QueuedMails} correos en la cola");
        }

        // Cada usuario de la demostración entra con su rol (la interfaz se adapta a sus permisos)
        foreach (var user in demo.Users)
        {
            var (s, _, l) = await SignInAsync(sp, demo, user.Email);
            using (s)
            {
                Assert.Contains(user.RoleCode, l.Roles);
            }
        }

        // V7 · El acceso de cliente es una cuenta de la tienda web: ve sus reservas (un carrito y un armado) y nada del personal
        var customer = Assert.Single(demo.Users, u => u.RoleCode == RoleCodes.Customer);
        var (cs, cm, _) = await SignInAsync(sp, demo, customer.Email);
        using (cs)
        {
            Assert.Equal(["build", "cart"], (await cm.Send(new MINV.Application.Accounts.GetMyReservationsQuery())).Select(r => r.Kind).Order(StringComparer.Ordinal));
            await Assert.ThrowsAsync<AccessDeniedException>(() => cm.Send(new GetWorkspaceQuery()));
        }
    }

    /// <summary>
    /// V4.1 · La demostración FACTURA sin PostgreSQL ni red: simulador del SIN en memoria (sin archivo de estado), las ventas
    /// de los últimos días con su factura válida, y la caja emite, envía y representa su documento; el consumidor final sin
    /// datos factura con el NIT especial 99003. V4.2: una venta de un producto serializado lleva su serie en la factura.
    /// </summary>
    [Fact]
    public async Task V41_la_demostracion_factura_con_el_simulador_en_memoria()
    {
        var (sp, demo) = await PrepareAsync();
        var billing = Assert.IsType<MINV.Infrastructure.Seeding.SeedBilling>(demo.Billing);
        Assert.True(billing.ValidInvoices > 10, $"Solo {billing.ValidInvoices} facturas válidas");
        Assert.Equal(8, billing.PointsOfSale);
        Assert.Null(sp.GetRequiredService<MINV.Infrastructure.Billing.Simulator.SiatSimulatorEngine>().Options.StateFile);   // nada en disco

        var (scope, mediator, _) = await SignInAsync(sp, demo);
        using var _s = scope;
        var status = await mediator.Send(new GetSiatStatusQuery());
        Assert.True(status.Enabled && status.HasToken);
        Assert.Equal(8, status.Points.Count);   // punto 0 de cada sucursal y el de cada caja
        Assert.All(status.Points, p => Assert.Equal(SiatConnectionMode.Online, p.Mode));
        var clock = sp.GetRequiredService<MINV.Application.Abstractions.IClock>();
        Assert.All(status.Points, p => Assert.True(p.CufdValidUntil > clock.UtcNow));
        Assert.Equal(0, (await mediator.Send(new GetHomologationQuery())).PendingProducts);
        var documents = await mediator.Send(new GetFiscalDocumentsQuery(billing.From, demo.Today));
        Assert.Contains(documents, d => d.Status == FiscalDocumentStatus.Valid);

        // La caja 3 (libre): el consumidor final sin datos de facturación sale con el NIT especial 99003 y el documento es válido
        await mediator.Send(new MINV.Application.Sales.OpenPosSessionCommand("CAJA03", 100m));
        var product = (await mediator.Send(new MINV.Application.Sales.GetSellableProductsQuery())).First(p => p.Available >= 5 && WithoutSerial(p.Sku));
        var sale = await mediator.Send(new MINV.Application.Sales.CheckoutCommand("CF", "EFECTIVO",
            [new MINV.Application.Sales.SaleLineInput(product.Sku, 1)]));
        Assert.Equal(FiscalDocumentStatus.Pending, sale.FiscalStatus);
        var sent = await mediator.Send(new DispatchFiscalDocumentsCommand(sale.FiscalDocumentId));
        var document = Assert.Single(sent.Documents);
        Assert.Equal(FiscalDocumentStatus.Valid, document.Status);
        Assert.Equal(SiatCodes.SpecialMinorSales, document.BuyerDocument);
        var pdf = await mediator.Send(new RenderFiscalDocumentQuery(document.Id));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 4));

        // Un producto serializado se vende con la serie escaneada del stock de la casa matriz
        var serialized = (await mediator.Send(new MINV.Application.Sales.GetSellableProductsQuery())).First(p => p.Available >= 2 && !WithoutSerial(p.Sku));
        var serial = (await mediator.Send(new GetAvailableSerialsQuery(serialized.Sku)))[0].Serial;
        await Assert.ThrowsAsync<DomainException>(() => mediator.Send(new MINV.Application.Sales.CheckoutCommand("CF", "EFECTIVO",
            [new MINV.Application.Sales.SaleLineInput(serialized.Sku, 1)])));   // sin la serie: rechazada (T-02)
        var withSerial = await mediator.Send(new MINV.Application.Sales.CheckoutCommand("CF", "EFECTIVO",
            [new MINV.Application.Sales.SaleLineInput(serialized.Sku, 1, 0, [serial])]));
        Assert.Equal(FiscalDocumentStatus.Valid, Assert.Single((await mediator.Send(new DispatchFiscalDocumentsCommand(withSerial.FiscalDocumentId))).Documents).Status);
        Assert.Equal(SerialNumberStatus.Sold, (await mediator.Send(new GetWarrantyStatusQuery(serial, serialized.Sku))).Status);
    }

    [Fact]
    public async Task Consultas_de_la_interfaz_ficha_kardex_tendencia_y_ultimos_movimientos()
    {
        var (sp, demo) = await PrepareAsync();
        var (scope, mediator, _) = await SignInAsync(sp, demo);
        using var _s = scope;
        var stock = (await mediator.Send(new GetStockProjectionQuery())).Result.Stock;

        var lookup = await mediator.Send(new GetProductLookupQuery());
        Assert.Equal(stock.Count(r => r.IsActive), lookup.Count);
        Assert.All(lookup, p => Assert.False(string.IsNullOrEmpty(p.PrimaryBin)));
        Assert.NotEmpty(await mediator.Send(new GetBinsQuery()));
        var types = await mediator.Send(new GetMovementTypesQuery());
        Assert.Contains(types, t => t.Code == MovementTypeCodes.Receipt && t.StockFactor == 1);

        // Ficha: el kardex termina en el stock de la proyección y el saldo acumulado cuadra fila por fila
        foreach (var row in stock.Where(r => r.Entries + r.Issues > 0).Take(8))
        {
            var card = await mediator.Send(new GetProductCardQuery(row.Sku, 5000));
            Assert.Equal(row.Stock, card.OnHand);
            Assert.Equal(row.Status, card.Status);
            Assert.Equal(row.Stock, card.Movements[0].Balance);
            Assert.Equal(card.OnHand, card.Movements.Sum(m => m.Signed));
            Assert.Equal(card.TotalMovements, card.Movements.Count);
            Assert.Equal(card.OnHand, card.Bins.Sum(b => b.OnHand));
        }

        var recent = await mediator.Send(new GetRecentMovementsQuery(10));
        Assert.Equal(10, recent.Count);
        Assert.True(recent.Zip(recent.Skip(1)).All(p => p.First.RecordedAt >= p.Second.RecordedAt));

        var trend = await mediator.Send(new GetMovementTrendQuery(30));
        Assert.Equal(30, trend.Count);
        Assert.Equal(demo.Today, trend[^1].Date);
        Assert.True(trend.Sum(d => d.Movements) > 0);
        // El saldo inicial es la apertura del inventario, no operación: el gráfico «Entradas y salidas» no lo cuenta como
        // entrada (el día de la apertura aplastaba el resto) y lo informa aparte
        var db = scope.ServiceProvider.GetRequiredService<IMinvDbContext>();
        var first = trend[0].Date;
        var inflows = await (from m in db.Set<StockMovement>()
                             join t in db.Set<MovementType>() on m.MovementTypeId equals t.Id
                             where m.BusinessDate >= first && m.BusinessDate <= demo.Today && t.StockFactor > 0
                             select new { m.Quantity, t.IsInitialBalance }).ToListAsync();
        Assert.True(trend.Sum(d => d.Opening) > 0);
        Assert.Equal(inflows.Where(x => x.IsInitialBalance).Sum(x => x.Quantity), trend.Sum(d => d.Opening));
        Assert.Equal(inflows.Where(x => !x.IsInitialBalance).Sum(x => x.Quantity), trend.Sum(d => d.Entries));
    }

    [Fact]
    public async Task Registrar_un_movimiento_actualiza_la_ficha_y_el_poka_yoke_bloquea_el_negativo()
    {
        var (sp, demo) = await PrepareAsync();
        var (scope, mediator, _) = await SignInAsync(sp, demo);
        using var _s = scope;
        var row = (await mediator.Send(new GetStockProjectionQuery())).Result.Stock.First(r => r.Stock > 0 && r.IsActive && WithoutSerial(r.Sku));
        var before = await mediator.Send(new GetProductCardQuery(row.Sku));
        var bin = before.PrimaryBin!;
        var result = await mediator.Send(new RegisterMovementCommand(row.Sku, bin, MovementTypeCodes.Receipt, 5, DocumentReference: "FAC-1"));
        Assert.StartsWith("✔", result.Message);
        var after = await mediator.Send(new GetProductCardQuery(row.Sku));
        Assert.Equal(before.OnHand + 5, after.OnHand);
        Assert.Equal("FAC-1", after.Movements[0].Document);
        Assert.Equal(before.TotalMovements + 1, after.TotalMovements);
        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            mediator.Send(new RegisterMovementCommand(row.Sku, bin, MovementTypeCodes.Issue, after.OnHand + 1)));
        Assert.Equal(after.OnHand, (await mediator.Send(new GetProductCardQuery(row.Sku))).OnHand);

        // V4.2 · Una merma (AJUSTE (−)) se contabiliza al costo promedio del almacén: Debe 5.1.09 / Haber 1.1.05 (el mayor
        // sigue al valor del stock); una ENTRADA registrada a mano no genera asiento (límite L-05)
        Assert.DoesNotContain("asiento", result.Message, StringComparison.Ordinal);
        var merma = await mediator.Send(new RegisterMovementCommand(row.Sku, bin, MovementTypeCodes.AdjustmentOut, 2, Notes: "Merma: empaque dañado"));
        Assert.Contains("asiento AS-", merma.Message, StringComparison.Ordinal);
        var value = JournalPoster.Money(2 * after.UnitCost);
        Assert.True(value > 0);
        Assert.Equal((value, value, 0m, 0m), await AdjustmentEntryAsync(scope, merma.MovementId));
    }

    /// <summary>V4.2 · Asiento de un ajuste de inventario (por el documento que lo originó): (Debe 5.1.09, Haber 1.1.05 por
    /// faltantes; Debe 1.1.05, Haber 4.1.02 por sobrantes).</summary>
    private static async Task<(decimal Shrinkage, decimal InventoryOut, decimal InventoryIn, decimal Surplus)> AdjustmentEntryAsync(IServiceScope scope,
        Guid correlationId)
    {
        var db = scope.ServiceProvider.GetRequiredService<MINV.Infrastructure.Persistence.MinvWriteDbContext>();
        var accounts = await db.Set<Account>().AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);
        var entry = await db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.SourceCorrelationId == correlationId);
        Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
        decimal Sum(string code, Func<JournalLine, decimal> side) => entry.Lines.Where(l => accounts[l.AccountId] == code).Sum(side);
        return (Sum(AccountCodes.InventoryShrinkage, l => l.Debit), Sum(AccountCodes.Inventory, l => l.Credit), Sum(AccountCodes.Inventory, l => l.Debit),
            Sum(AccountCodes.InventorySurplus, l => l.Credit));
    }

    [Fact]
    public async Task Toma_fisica_abrir_contar_corregir_y_contabilizar()
    {
        var (sp, demo) = await PrepareAsync();
        var (scope, mediator, _) = await SignInAsync(sp, demo);
        using var _s = scope;
        // La carga deja una toma en curso (periféricos): se anula para empezar de cero
        if (await mediator.Send(new GetOpenPhysicalCountQuery()) is { } previous)
        {
            await mediator.Send(new CancelPhysicalCountCommand(previous.Id));
        }
        Assert.Null(await mediator.Send(new GetOpenPhysicalCountQuery()));
        var opened = await mediator.Send(new OpenPhysicalCountCommand("ALM01"));
        var row = (await mediator.Send(new GetStockProjectionQuery())).Result.Stock.First(r => r.Stock > 0 && r.IsActive && WithoutSerial(r.Sku));
        var card = await mediator.Send(new GetProductCardQuery(row.Sku));
        var bin = card.Bins.First(b => b.OnHand > 0);

        await mediator.Send(new RecordCountCommand(opened.PhysicalCountId, row.Sku, bin.BinCode, bin.OnHand + 3));
        var sheet = (await mediator.Send(new GetOpenPhysicalCountQuery()))!;
        Assert.Equal(opened.Number, sheet.Number);
        Assert.Equal(3, Assert.Single(sheet.Lines).Difference);

        Assert.True(await mediator.Send(new RemoveCountCommand(opened.PhysicalCountId, row.Sku, bin.BinCode)));
        Assert.Empty((await mediator.Send(new GetOpenPhysicalCountQuery()))!.Lines);
        await mediator.Send(new RecordCountCommand(opened.PhysicalCountId, row.Sku, bin.BinCode, bin.OnHand + 2));

        var posted = await mediator.Send(new PostPhysicalCountCommand(opened.PhysicalCountId, Confirmed: true));
        Assert.Equal(1, posted.Surpluses);
        // V4.2 · El sobrante se contabiliza al costo promedio: Debe 1.1.05 / Haber 4.1.02 Sobrantes de inventario
        Assert.Contains("asiento AS-", posted.Message, StringComparison.Ordinal);
        var surplus = JournalPoster.Money(2 * card.UnitCost);
        Assert.Equal((0m, 0m, surplus, surplus), await AdjustmentEntryAsync(scope, opened.PhysicalCountId));
        Assert.Null(await mediator.Send(new GetOpenPhysicalCountQuery()));
        var after = await mediator.Send(new GetProductCardQuery(row.Sku));
        Assert.Equal(card.OnHand + 2, after.OnHand);
        Assert.Equal(MovementTypeCodes.AdjustmentIn, after.Movements[0].TypeCode);
        Assert.Equal(opened.Number, after.Movements[0].Document);
    }

    [Fact]
    public async Task Cambiar_la_contrasena_cerrar_sesion_y_permisos_por_rol()
    {
        var (sp, demo) = await PrepareAsync();
        var (scope, mediator, login) = await SignInAsync(sp, demo);
        using (scope)
        {
            await Assert.ThrowsAsync<AuthenticationFailedException>(() => mediator.Send(new ChangePasswordCommand("incorrecta1", "NuevaClave2026")));
            var invalid = await Assert.ThrowsAsync<RequestValidationException>(() => mediator.Send(new ChangePasswordCommand(demo.Password, "corta")));
            Assert.Contains(invalid.Errors, e => e.Contains("8 caracteres", StringComparison.Ordinal));
            Assert.True(await mediator.Send(new ChangePasswordCommand(demo.Password, "NuevaClave2026")));
            var activity = await mediator.Send(new GetActivityQuery(20));
            Assert.Contains(activity, a => a.Action == "ChangePassword" && a.Outcome == AuditOutcome.Rejected);
            Assert.Contains(activity, a => a.Action == "ChangePassword" && a.Outcome == AuditOutcome.Succeeded);
            Assert.True(await mediator.Send(new LogoutCommand(login.SessionId)));
            Assert.False(await mediator.Send(new LogoutCommand(login.SessionId)));   // ya estaba cerrada
            var db = scope.ServiceProvider.GetRequiredService<MINV.Infrastructure.Persistence.MinvWriteDbContext>();
            Assert.False((await db.Sessions.FindAsync(login.SessionId))!.IsOpen);
            Assert.Contains(await mediator.Send(new GetActivityQuery(5)), a => a.Action == "Logout" && a.UserName == "Administrador General");
        }
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => SignInAsync(sp, demo));
        var (again, _, _) = await SignInAsync(sp, demo, password: "NuevaClave2026");
        again.Dispose();

        // Ventas no registra entradas de bodega ni cuenta; sí consulta el stock
        var seller = demo.Users.First(u => u.RoleCode == RoleCodes.Sales);
        var (s2, m2, _) = await SignInAsync(sp, demo, seller.Email);
        using (s2)
        {
            Assert.NotEmpty((await m2.Send(new GetStockProjectionQuery())).Result.Stock);
            var sku = (await m2.Send(new GetProductLookupQuery())).First(p => WithoutSerial(p.Sku));
            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                m2.Send(new RegisterMovementCommand(sku.Sku, sku.PrimaryBin!, MovementTypeCodes.Receipt, 1)));
            await Assert.ThrowsAsync<AccessDeniedException>(() => m2.Send(new GetOpenPhysicalCountQuery()));
        }
    }
}
