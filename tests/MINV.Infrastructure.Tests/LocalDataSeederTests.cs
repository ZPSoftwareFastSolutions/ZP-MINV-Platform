using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Abstractions;
using MINV.Application.Accounting;
using MINV.Application.Accounts;
using MINV.Application.Billing;
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
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.Domain.Accounting;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;
using MINV.Domain.Sales;
using MINV.Domain.Service;
using MINV.Domain.Warehousing;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Seeding.Tecnologia;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// Datos de prueba de la base LOCAL (<c>minv datos-prueba</c>) sobre la base en memoria. V4.2: la empresa es Tech Zone
/// Gaming S.R.L. (catálogo de tecnología embebido): el generador recorre casi todos los casos de uso (usuarios, catálogo con
/// fichas técnicas e imágenes, clientes, proveedores, compras con series, transferencias con series, caja con series,
/// pedidos web, devoluciones, garantías, armados, toma física, asientos y facturación) con la tubería completa, y el
/// resultado debe ser coherente.
/// </summary>
public sealed class LocalDataSeederTests
{
    internal static async Task<(ServiceProvider Services, SeedResult Result)> SeedAsync(int days = 8, string tenant = "PRUEBA", int seed = 7)
    {
        var services = new ServiceCollection();
        services.AddMinvApplication();
        services.AddMinvDemoInfrastructure();
        var sp = services.BuildServiceProvider();
        var logFile = Environment.GetEnvironmentVariable("MINV_SEED_LOG");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await sp.GetRequiredService<LocalDataSeeder>().SeedAsync(new SeedOptions(tenant, Days: days, Seed: seed),
            line => { if (logFile is { Length: > 0 }) { File.AppendAllText(logFile, $"[{watch.Elapsed.TotalSeconds,6:N1} s] {line}{Environment.NewLine}"); } });
        return (sp, result);
    }

    /// <summary>
    /// V4.2 · El mayor 1.1.05 Inventario de cada sucursal es el valor de su stock: Σ existencias × costo promedio vigente de la
    /// variante en su almacén (una sucursal de los datos de prueba tiene un almacén). Así se vigila la «deriva» que dejaban las
    /// mermas y la toma física sin asiento (+2.646,02 en la carga de 60 días) y la factura del proveedor calculada sobre un costo
    /// neto de «importe / 1,13» (−2.193,23).
    /// </summary>
    internal static async Task AssertInventoryLedgerIsStockValueAsync(MinvWriteDbContext db)
    {
        var costs = (await db.Set<AverageCostHistory>().AsNoTracking().ToListAsync())
            .GroupBy(h => (h.VariantId, h.BranchId)).ToDictionary(g => g.Key, g => g.MaxBy(h => h.Sequence)!.AverageCost);
        var stock = await (from l in db.Set<StockLevel>()
                           join b in db.Set<Batch>() on l.BatchId equals b.Id
                           select new { l.BranchId, b.VariantId, l.QuantityOnHand }).ToListAsync();
        var value = stock.GroupBy(x => x.BranchId)
            .ToDictionary(g => g.Key, g => JournalPoster.Money(g.Sum(x => x.QuantityOnHand * costs.GetValueOrDefault((x.VariantId, x.BranchId)))));
        var ledger = (await (from l in db.Set<JournalLine>()
                             join e in db.Set<JournalEntry>() on l.JournalEntryId equals e.Id
                             join a in db.Set<Account>() on l.AccountId equals a.Id
                             where a.Code == AccountCodes.Inventory
                             select new { e.BranchId, l.Debit, l.Credit }).ToListAsync())
            .GroupBy(x => x.BranchId).ToDictionary(g => g.Key, g => g.Sum(x => x.Debit - x.Credit));
        var branches = await db.Set<Branch>().AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code);
        Assert.NotEmpty(value);
        foreach (var (branch, code) in branches)
        {
            Assert.True(value.GetValueOrDefault(branch) == ledger.GetValueOrDefault(branch),
                $"Sucursal {code}: 1.1.05 = {ledger.GetValueOrDefault(branch):N2} y valor del stock = {value.GetValueOrDefault(branch):N2}");
        }
    }

    internal static async Task<(IServiceScope Scope, IMediator Mediator)> SignInAsync(ServiceProvider sp, SeedUser user, string tenant = "PRUEBA")
    {
        var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.Send(new LoginCommand(tenant, user.Email, user.Password, "PRUEBAS", "test"));
        return (scope, mediator);
    }

    [Fact]
    public async Task Genera_una_empresa_completa_y_coherente_con_usuarios_de_cada_rol()
    {
        var (sp, result) = await SeedAsync();
        Assert.Equal("Tech Zone Gaming S.R.L.", result.CompanyName);
        var staff = result.Users.Where(u => u.RoleCode != RoleCodes.Customer).ToList();
        Assert.Equal(12, staff.Count);   // administrador + 11 usuarios repartidos en 3 sucursales
        // V7 · Y, después del personal, las dos cuentas de cliente de la tienda web (regla P-13): cubren el rol CLIENTE
        Assert.Equal(2, result.Users.Count(u => u.RoleCode == RoleCodes.Customer));
        Assert.Equal(staff, result.Users.Take(staff.Count));
        // V6 · TIENDA_WEB es el usuario técnico de la tienda web: sin contraseña de prueba (no está en la lista de usuarios)
        Assert.All(RoleCodes.All.Where(r => r.Code != RoleCodes.Storefront), r => Assert.Contains(result.Users, u => u.RoleCode == r.Code));
        Assert.Equal("tienda-web@techzone.example", result.StorefrontUser);
        Assert.All(result.Users, u => Assert.Matches(@"^[A-Za-z]+-\d{4}$", u.Password));
        Assert.All(staff, u => Assert.EndsWith("@techzone.example", u.Email, StringComparison.Ordinal));
        // Los clientes no son de la empresa: correos ficticios .example fuera de su dominio
        Assert.All(result.Users.Except(staff), u => Assert.True(u.Email.EndsWith(".example", StringComparison.Ordinal)
                                                               && !u.Email.EndsWith("@techzone.example", StringComparison.Ordinal), u.Email));
        Assert.All(result.Users, u => Assert.DoesNotContain(u.Password, u.ToString(), StringComparison.Ordinal));
        Assert.Equal(159, result.Products);
        Assert.Equal(8, result.Suppliers);
        Assert.True(result.Tickets > 60, $"Solo {result.Tickets} ventas");
        Assert.True(result.JournalEntries > result.Tickets);

        var admin = result.Users.First(u => u.RoleCode == RoleCodes.Admin);
        var (scope, m) = await SignInAsync(sp, admin);
        using (scope)
        {
            // Catálogo: todos con imagen, precio sobre el costo y posición
            var catalog = await m.Send(new GetCatalogQuery());
            Assert.Equal(159, catalog.Count);
            Assert.All(catalog, c => Assert.True(c.HasImage && c.SalePrice > c.UnitCost && c.BinCode is not null, c.Sku));
            // El costo es NETO de IVA (el 87 % del costo con IVA; el precio incluye el IVA): el margen sobre el precio neto (el 87 %
            // del precio, VatRules) que muestra el catálogo es el «margen_pct» del JSON y ninguno queda con «margen bajo» (< 15 %)
            Assert.All(catalog, c => Assert.InRange((c.SalePrice * 0.87m - c.UnitCost) / (c.SalePrice * 0.87m)
                                                    - TechSeedCatalog.Current.Product(c.Sku).ListMargin, -0.002m, 0.002m));
            Assert.DoesNotContain(catalog, c => (c.SalePrice * 0.87m - c.UnitCost) / (c.SalePrice * 0.87m) < 0.15m);
            Assert.Equal(159, (await m.Send(new GetProductImagesQuery())).Count);
            Assert.Contains(catalog, c => c.Unit == "SERV");   // servicios técnicos con su unidad (SIN: 58)

            // Contabilidad: partida doble en todo el libro y resultado con ingresos, costo y gastos
            var chart = await m.Send(new GetChartOfAccountsQuery());
            var postable = chart.Where(a => a.IsPostable).ToList();
            Assert.Equal(postable.Sum(a => a.Debit), postable.Sum(a => a.Credit));
            var statement = await m.Send(new GetIncomeStatementQuery(result.From, result.To));
            Assert.True(statement.TotalRevenue > 0 && statement.TotalCosts > 0);
            Assert.Equal(statement.TotalRevenue - statement.TotalCosts - statement.TotalExpenses, statement.NetIncome);
            // V4.2 · El mayor 1.1.05 Inventario de cada sucursal es el valor de su stock (mermas y toma física contabilizadas)
            await AssertInventoryLedgerIsStockValueAsync(scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>());

            // Ventas: caja (también los armados y las facturas manuales transcritas) + pedidos de la tienda en línea
            var sales = await m.Send(new GetSalesQuery(result.From, result.To));
            Assert.Equal(result.Tickets + result.ExternalOrders, sales.Count);
            var report = await m.Send(new GetSalesReportQuery(result.From, result.To));
            Assert.Equal(sales.Count(s => s.Status == InvoiceStatus.Issued), report.Tickets);
            Assert.True(report.MarginPercent is > 5 and < 45, $"Margen {report.MarginPercent} %");

            // Stock: nunca negativo (poka-yoke) y con productos en alerta para el pedido sugerido
            var projection = await m.Send(new GetStockProjectionQuery());
            Assert.All(projection.Result.Stock, s => Assert.True(s.Stock >= 0, s.Sku));

            // Compras: recibidas (con series), aprobadas por recibir y un borrador
            var orders = await m.Send(new GetPurchaseOrdersQuery());
            Assert.Contains(orders, o => o.Status == PurchaseOrderStatus.Received);
            Assert.Contains(orders, o => o.Status == PurchaseOrderStatus.Draft);
            Assert.Equal(8, (await m.Send(new GetSuppliersQuery())).Count);
            Assert.True((await m.Send(new GetCustomersQuery())).Customers.Count >= 31);   // 30 del catálogo + CF (+ compradores facturados)
            Assert.Equal(15, (await m.Send(new GetUsersQuery())).Count);   // V6: + el usuario técnico de la tienda web; V7: + las 2 cuentas de cliente
        }

        // El cajero tiene su caja abierta hoy (los domingos la tienda no abre: la carga no deja turnos ese día) y productos para vender
        var cashier = result.Users.First(u => u.RoleCode == RoleCodes.Cashier);
        var (cs, cm) = await SignInAsync(sp, cashier);
        using (cs)
        {
            var state = await cm.Send(new GetPosStateQuery());
            if (result.To.DayOfWeek != DayOfWeek.Sunday)
            {
                Assert.NotNull(state.Session);
            }
            Assert.Equal(159, (await cm.Send(new GetSellableProductsQuery())).Count);
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
            await Assert.ThrowsAsync<AccessDeniedException>(() => cm.Send(new MoveWarrantyClaimCommand("RMA-CM-000001", WarrantyClaimStatus.Diagnosing)));
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
        Assert.Equal(["CM", "CB", "SC"], result.Branches);
        Assert.True(result.Transfers >= 4, $"Solo {result.Transfers} transferencias");
        Assert.StartsWith("minv_", result.ApiKeyToken, StringComparison.Ordinal);
        Assert.DoesNotContain(result.ApiKeyToken, result.ToString(), StringComparison.Ordinal);

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

        // Cajero de Cochabamba: solo ve su sucursal (ventas, stock y transferencias que llegan a ella)
        var cashierCb = result.Users.First(u => u.RoleCode == RoleCodes.Cashier && u.Branches == "CB");
        var (cs, cm) = await SignInAsync(sp, cashierCb);
        using (cs)
        {
            var sales = await cm.Send(new GetSalesQuery(result.From, result.To));
            Assert.NotEmpty(sales);
            Assert.All(sales, s => Assert.StartsWith("F-CB-", s.InvoiceNumber, StringComparison.Ordinal));
            var state = await cm.Send(new GetPosStateQuery());
            Assert.All(state.Registers, r => Assert.StartsWith("CB-", r.Code, StringComparison.Ordinal));
            var branches = await cm.Send(new GetBranchesQuery());
            Assert.Single(branches, b => b.IsVisible);
            Assert.Null(branches.First(b => b.Code == "CM").StockValue);
        }

        // Bodega de Santa Cruz: recibe la transferencia en tránsito; no puede despacharla ni anular las ajenas. Un faltante
        // exige su motivo y, si el producto lleva serie, cuál serie no llegó (regla T-02)
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
            ks.ServiceProvider.GetRequiredService<MINV.Application.Abstractions.IMinvDbContext>().ClearTracking();   // como el escritorio
            var received = await km.Send(new ReceiveTransferCommand(inTransit.Id,
                [new TransferReceiptInput(line.Sku, line.Quantity - 1, "Una unidad llegó rota", line.Serials is { Count: > 0 } s ? [s[0]] : null)]));
            Assert.Contains("faltante", received.Message, StringComparison.Ordinal);
            var after = await km.Send(new GetTransferQuery(inTransit.Id));
            Assert.Equal(TransferStatus.Received, after.Header.Status);
            Assert.Equal(1, after.Lines[0].Shortage);
            var pending = (await km.Send(new GetTransfersQuery(TransferStatus.Pending))).ToList();
            Assert.Empty(pending);   // la pendiente va a Cochabamba: Santa Cruz no la ve
        }

        // La API Key del e-commerce: su canal y sus alcances (no puede administrar usuarios)
        using var api = sp.CreateScope();
        var principal = await api.ServiceProvider.GetRequiredService<Integration.ApiKeyAuthenticator>().AuthenticateAsync(result.ApiKeyToken, default);
        Assert.NotNull(principal);
        var apiMediator = api.ServiceProvider.GetRequiredService<IMediator>();
        var catalog = await apiMediator.Send(new GetApiCatalogQuery(1, 500));
        Assert.Equal(159, catalog.Total);
        await Assert.ThrowsAsync<AccessDeniedException>(() => apiMediator.Send(new GetUsersQuery()));
        Assert.Null(await sp.CreateScope().ServiceProvider.GetRequiredService<Integration.ApiKeyAuthenticator>()
            .AuthenticateAsync(result.ApiKeyToken[..^2] + "xx", default));
    }

    /// <summary>
    /// V4.1 · La empresa de prueba FACTURA desde la mitad del período con el simulador del SIN en proceso: cada venta de caja
    /// tiene su documento fiscal válido cuyo total es el cobrado, hubo un corte de internet en Cochabamba (facturas fuera de
    /// línea recuperadas en un paquete validado), una contingencia manual con facturas CAFC, anulaciones (una revertida),
    /// notas crédito-débito, un rechazo por NIT re-emitido con excepción y facturas de proveedores; al final todo está en
    /// línea con CUFD vigente y los libros cuadran con los documentos. V4.2: las facturas de productos serializados llevan
    /// las series (numeroSerie o numeroImei).
    /// </summary>
    [Fact]
    public async Task V41_la_empresa_de_prueba_factura_y_los_totales_fiscales_cuadran_con_las_ventas()
    {
        var (sp, result) = await SeedAsync(days: 16);
        var billing = Assert.IsType<SeedBilling>(result.Billing);
        Assert.Equal(1023456029L, billing.Nit);
        Assert.Equal("TECH ZONE GAMING S.R.L.", billing.BusinessName);
        Assert.Equal(2, billing.Environment);
        Assert.StartsWith("SIM-", billing.SiatToken, StringComparison.Ordinal);
        Assert.DoesNotContain(billing.SiatToken, billing.ToString(), StringComparison.Ordinal);
        Assert.Equal(8, billing.PointsOfSale);   // 5 cajas + el punto 0 de cada sucursal
        Assert.True(billing.ValidInvoices > 30, $"Solo {billing.ValidInvoices} facturas válidas");
        Assert.True(billing.OfflineRecovered >= 3, $"Solo {billing.OfflineRecovered} facturas fuera de línea recuperadas");
        Assert.Equal(3, billing.CafcInvoices);
        Assert.True(billing.CreditNotes >= 2, $"Solo {billing.CreditNotes} notas crédito-débito");   // 2 devoluciones parciales (+ la devolución por falla)
        Assert.Equal(2, billing.Voided);   // una con devolución de mercadería y otra re-emitida; la tercera se revirtió
        Assert.Equal(1, billing.Reverted);
        Assert.True(billing.WebInvoices > 0 && billing.SupplierInvoices > 0, $"{billing.WebInvoices} web · {billing.SupplierInvoices} proveedores");

        var admin = result.Users.First(u => u.RoleCode == RoleCodes.Admin);
        var (scope, m) = await SignInAsync(sp, admin);
        using (scope)
        {
            var documents = await m.Send(new GetFiscalDocumentsQuery(billing.From, result.To));
            // Nada quedó pendiente, fuera de línea o en paquete: el trabajo automático lo resolvió todo
            Assert.DoesNotContain(documents, d => d.Status is FiscalDocumentStatus.Pending or FiscalDocumentStatus.Offline
                or FiscalDocumentStatus.InPackage or FiscalDocumentStatus.NoResponse or FiscalDocumentStatus.DuplicateToVoid);
            Assert.Contains(documents, d => d.Status == FiscalDocumentStatus.Rejected && d.LastSiatCode is not null);   // el NIT inválido
            Assert.Contains(documents, d => d.Status == FiscalDocumentStatus.Discarded);   // el que se envió sin respuesta durante el corte

            // Cada venta del período facturado tiene UN documento vigente y su total fiscal es el total cobrado
            var sales = (await m.Send(new GetSalesQuery(billing.From, result.To))).Where(s => s.IssuedAt >= new DateTimeOffset(
                billing.From.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(-4))).ToList();
            var active = documents.Where(d => d.Kind == FiscalDocumentKind.Invoice && d.Status == FiscalDocumentStatus.Valid && d.SaleNumber is not null)
                .GroupBy(d => d.SaleNumber!).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var sale in sales.Where(s => s.Status == InvoiceStatus.Issued))
            {
                Assert.True(active.TryGetValue(sale.InvoiceNumber, out var docs), $"La venta {sale.InvoiceNumber} ({sale.IssuedAt:dd/MM HH:mm}, " +
                    $"{sale.PaymentMethod}) no tiene factura válida: " + string.Join(", ", documents.Where(d => d.SaleNumber == sale.InvoiceNumber)
                        .Select(d => $"N° {d.Number} {d.Status} {d.LastSiatCode} PV {d.PointOfSaleCode}")));
                Assert.Equal(sale.Total, Assert.Single(docs!).Total);
            }

            // Eventos: el corte de internet de Cochabamba (conciliado) y la contingencia manual CAFC de Santa Cruz
            var events = await m.Send(new GetSignificantEventsQuery(billing.From, result.To));
            Assert.Contains(events, e => e is { BranchCode: "CB", Kind: SignificantEventKind.Offline, Status: SignificantEventStatus.Reconciled });
            var cafc = Assert.Single(events, e => e.Kind == SignificantEventKind.ManualCafc);
            Assert.Equal("SC", cafc.BranchCode);
            Assert.Equal(3, cafc.Documents);
            Assert.All(await m.Send(new GetFiscalPackagesQuery()), p => Assert.Equal(FiscalPackageStatus.Validated, p.Status));

            // Estado SIAT: todos los puntos en línea con CUFD vigente; homologación completa (UND → 57, SERV → 58)
            var status = await m.Send(new GetSiatStatusQuery());
            Assert.True(status.Enabled && status.HasToken);
            Assert.All(status.Points, p =>
            {
                Assert.Equal(SiatConnectionMode.Online, p.Mode);
                Assert.True(p.CufdValidUntil > DateTimeOffset.UtcNow, $"CUFD vencido en {p.BranchCode} · {p.Code}");
            });
            var homologation = await m.Send(new GetHomologationQuery());
            Assert.Equal(0, homologation.PendingProducts);
            Assert.Equal(57, homologation.Units.Single(u => u.Code == "UND").SinUnitCode);
            Assert.Equal(58, homologation.Units.Single(u => u.Code == "SERV").SinUnitCode);
            Assert.Equal(["4741100", "4741200", "4742100"], homologation.Products.Select(p => p.ActivityCode!).Distinct().Order());

            // Libros del mes de hoy: el total del libro de ventas es la suma de las facturas válidas del mes
            var month = result.To;
            var book = await m.Send(new GetSalesBookQuery(month.Year, month.Month));
            var validThisMonth = documents.Where(d => d.Kind == FiscalDocumentKind.Invoice && d.Status == FiscalDocumentStatus.Valid
                                                                                        && d.IssuedAt.Year == month.Year && d.IssuedAt.Month == month.Month).ToList();
            Assert.Equal(validThisMonth.Count, book.Valid);
            Assert.Equal(validThisMonth.Sum(d => d.Total), book.Total);
            var supplierInvoices = await m.Send(new GetSupplierInvoicesQuery(billing.From.AddDays(-30), result.To));
            Assert.NotEmpty(supplierInvoices);

            // V4.2 · Las compras van al costo NETO de IVA (el 87 % de lo facturado): la factura del proveedor es recepción / 0,87 y su
            // crédito fiscal (13 % del importe) es exactamente el IVA que se suma a la deuda: el inventario no cambia y el mayor
            // 1.1.05 de cada sucursal sigue siendo el valor de su stock
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            var invoiceLines = await db.Set<SupplierInvoiceLine>().AsNoTracking().ToListAsync();
            Assert.All(supplierInvoices, i =>
            {
                var received = invoiceLines.Where(l => l.SupplierInvoiceId == i.Id).Sum(l => JournalPoster.Money(l.Quantity * l.UnitCost));
                Assert.Equal(FiscalRules.InvoiceForNetCost(received), i.TotalAmount);
                Assert.Equal(JournalPoster.Money(i.TotalAmount * 0.13m), i.TaxCredit);
                Assert.Equal(i.TotalAmount - received, i.TaxCredit);
            });
            var accounts = await db.Set<Account>().AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);
            var ids = supplierInvoices.Select(i => i.Id).ToList();
            var entries = await db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines)
                .Where(e => e.SourceCorrelationId != null && ids.Contains(e.SourceCorrelationId.Value)).ToListAsync();
            Assert.Equal(supplierInvoices.Count, entries.Count);
            Assert.All(entries, e => Assert.DoesNotContain(e.Lines, l => accounts[l.AccountId] == AccountCodes.Inventory));
            await AssertInventoryLedgerIsStockValueAsync(db);

            // V4.2 · Las facturas de productos serializados llevan las series en el XML del SIN (regla T-03)
            var xmls = await db.Set<FiscalDocumentFile>().Select(f => f.Xml).ToListAsync();
            Assert.Contains(xmls, x => x.Contains("<numeroSerie>", StringComparison.Ordinal));
        }

        // El cajero de Cochabamba emite en línea (su punto volvió a estar en línea) y el comprobante es válido al enviarlo
        var cashierCb = result.Users.First(u => u.RoleCode == RoleCodes.Cashier && u.Branches == "CB");
        var (cs, cm) = await SignInAsync(sp, cashierCb);
        using (cs)
        {
            if ((await cm.Send(new GetPosStateQuery())).Session is null)
            {
                // Los domingos la tienda no abre (la carga no deja turnos abiertos ese día): el cajero abre su caja para la prueba
                await cm.Send(new OpenPosSessionCommand($"{LocalDataSeeder.BranchCochabamba}-CAJA1", 500m));
            }
            var fiscal = await cm.Send(new GetPosFiscalStateQuery());
            Assert.True(fiscal.BillingEnabled && fiscal.Ready, fiscal.Message);
            var product = (await cm.Send(new GetSellableProductsQuery())).First(p => p.Available >= 2 && !TechSeedCatalog.Current.Product(p.Sku).TracksSerials);
            var sale = await cm.Send(new CheckoutCommand("CF", "EFECTIVO", [new SaleLineInput(product.Sku, 1)], 1000m, null,
                new FiscalBuyerInput(SiatCodes.DocumentCi, "4455667", null, "Comprador de prueba", null)));
            var sent = await cm.Send(new DispatchFiscalDocumentsCommand(sale.FiscalDocumentId));
            Assert.Equal(FiscalDocumentStatus.Valid, Assert.Single(sent.Documents).Status);
        }
    }

    [Fact]
    public async Task V41_sin_facturacion_la_carga_es_la_de_la_V4()
    {
        var services = new ServiceCollection();
        services.AddMinvApplication();
        services.AddMinvDemoInfrastructure();
        var sp = services.BuildServiceProvider();
        var result = await sp.GetRequiredService<LocalDataSeeder>().SeedAsync(new SeedOptions("SINFACT", Days: 4, Seed: 3, Billing: false), _ => { });
        Assert.Null(result.Billing);
        var (scope, m) = await SignInAsync(sp, result.Users.First(u => u.RoleCode == RoleCodes.Admin), "SINFACT");
        using (scope)
        {
            Assert.False((await m.Send(new GetSiatStatusQuery())).Configured);
            Assert.Empty(await m.Send(new GetFiscalDocumentsQuery(result.From, result.To)));
        }
    }

    /// <summary>
    /// V7 · La tienda web de la empresa de prueba (regla P-13), hecha con los casos de uso: dos cuentas de cliente que ingresan con
    /// su contraseña (solo con el rol CLIENTE) y ven SOLO sus reservas (un carrito y un armado cada una, ligadas a su cliente); un
    /// carrito de la tienda vigente de un solo monitor con los datos para la factura, otro vencido (lo cerró el vencimiento) y uno de
    /// mostrador; y los correos de confirmación en la cola, sin enviar. El archivo de usuarios de prueba lista las cuentas en su
    /// propia sección y su lector (las capturas del escritorio con la base local) sigue encontrando al administrador y al cajero.
    /// </summary>
    [Fact]
    public async Task V7_la_tienda_web_trae_cuentas_de_cliente_con_sus_reservas_carritos_y_correos_en_cola()
    {
        const string tenant = "TIENDAWEB";
        var (sp, result) = await SeedAsync(days: 3, tenant: tenant, seed: 19);
        var web = Assert.IsType<SeedWeb>(result.Web);
        Assert.Equal((2, 4, 2, 1, 1, 1), (web.Accounts, web.AccountReservations, web.AccountBuilds, web.ActiveCarts, web.ExpiredCarts, web.CounterCarts));
        var customers = result.Users.Where(u => u.RoleCode == RoleCodes.Customer).ToList();
        Assert.Equal(2, customers.Count);
        Assert.All(customers, c => Assert.Equal((LocalDataSeeder.CustomerRoleName, LocalDataSeeder.BranchMain), (c.RoleName, c.Branches)));

        // Cada cuenta ingresa con su contraseña y ve SOLO sus reservas: un carrito y un armado, vigentes, en la sucursal de la tienda
        var mine = new Dictionary<string, IReadOnlyList<StorefrontReservationView>>(StringComparer.Ordinal);
        foreach (var customer in customers)
        {
            var (scope, m) = await SignInAsync(sp, customer, tenant);
            using (scope)
            {
                Assert.Equal([PermissionCodes.AccountManage, PermissionCodes.AccountReserve],
                    scope.ServiceProvider.GetRequiredService<ICurrentUser>().Permissions.Order(StringComparer.Ordinal));
                var account = await m.Send(new GetMyAccountQuery());
                Assert.Equal((customer.Name, customer.Email), (account.Name, account.Email));
                Assert.StartsWith(AccountRules.CustomerCodePrefix + "-", account.CustomerCode, StringComparison.Ordinal);
                var reservations = await m.Send(new GetMyReservationsQuery());
                Assert.Equal(["build", "cart"], reservations.Select(r => r.Kind).Order(StringComparer.Ordinal));
                Assert.All(reservations, r => Assert.Equal(("Reserved", LocalDataSeeder.BranchMain, customer.Name), (r.Status, r.Branch, r.ContactName)));
                Assert.StartsWith(PcBuild.WebNumberPrefix + "-", reservations.Single(r => r.Kind == "build").Number, StringComparison.Ordinal);
                Assert.StartsWith(PcBuild.WebCartNumberPrefix + "-", reservations.Single(r => r.Kind == "cart").Number, StringComparison.Ordinal);
                mine[customer.Email] = reservations;
                // Es un cliente: lo del personal no (regla P-04)
                await Assert.ThrowsAsync<AccessDeniedException>(() => m.Send(new GetPcBuildsQuery()));
            }
        }
        var (first, second) = (mine[customers[0].Email], mine[customers[1].Email]);
        Assert.Empty(first.Select(r => r.Number).Intersect(second.Select(r => r.Number)));
        var (other, om) = await SignInAsync(sp, customers[1], tenant);
        using (other)
        {
            // La reserva de la otra cuenta «no existe» para cancelarla
            await Assert.ThrowsAsync<NotFoundException>(() => om.Send(new CancelMyReservationCommand(first[0].Number)));
        }

        var admin = result.Users.First(u => u.RoleCode == RoleCodes.Admin);
        var (adminScope, am) = await SignInAsync(sp, admin, tenant);
        using (adminScope)
        {
            // Las reservas de las cuentas siguen vigentes y quedaron ligadas a su cliente; los armados, sin errores de compatibilidad
            var numbers = mine.Values.SelectMany(v => v).Select(r => r.Number).ToHashSet(StringComparer.Ordinal);
            var rows = (await am.Send(new GetPcBuildsQuery(Channel: PcBuildChannel.Web))).Where(r => numbers.Contains(r.Number)).ToList();
            Assert.Equal(4, rows.Count);
            Assert.All(rows, r => Assert.Equal((PcBuildStatus.Reserved, r.ContactName), (r.Status, r.Customer)));
            Assert.All(rows.Where(r => r.Kind == PcBuildKind.Build), r => Assert.False(r.QuotedWithErrors, r.Number));

            // Carritos: el de la tienda de un solo monitor (vigente, con el CI para la factura), el vencido y el de mostrador
            var carts = await am.Send(new GetPcBuildsQuery(Kind: PcBuildKind.Cart));
            var single = Assert.Single(carts, c => c.Channel == PcBuildChannel.Web && c.Customer is null && c.Status == PcBuildStatus.Reserved);
            Assert.Equal((1, 1m, 1), (single.Items, single.Reserved, single.BuyerDocumentType));
            var monitor = Assert.Single((await am.Send(new GetPcBuildQuery(single.Number))).QuotedItems);
            Assert.Equal("MON", TechSeedCatalog.Current.Product(monitor.Sku).Category);
            var expired = Assert.Single(carts, c => c.Status == PcBuildStatus.Cancelled && c.CancelReason == PcBuild.ExpiredReason);
            Assert.Equal((PcBuildChannel.Web, 0m), (expired.Channel, expired.Reserved));   // el vencimiento devolvió el stock
            var counter = Assert.Single(carts, c => c.Channel == PcBuildChannel.Desktop);
            Assert.Equal((PcBuildStatus.Reserved, LocalDataSeeder.BranchMain, 1), (counter.Status, counter.BranchCode, counter.BuyerDocumentType));
            Assert.StartsWith($"{PcBuild.CartNumberPrefix}-{LocalDataSeeder.BranchMain}-", counter.Number, StringComparison.Ordinal);
            Assert.Equal(counter.ContactName, counter.Customer);   // para un cliente habitual de la casa matriz

            // Correos: la confirmación de cada reserva quedó en la cola, sin intentos (nadie envía durante la carga, regla B-08)
            var queue = await am.Send(new GetOutgoingMailsQuery(OutgoingMailStatus.Pending, Take: 500));
            Assert.Equal(web.QueuedMails, queue.Count);
            Assert.All(customers, c => Assert.Equal(mine[c.Email].Select(r => r.Number).Order(StringComparer.Ordinal),
                queue.Where(q => q.Recipient == c.Email).Select(q => q.Reservation).Order(StringComparer.Ordinal)));
            Assert.Contains(queue, q => q.Reservation == single.Number);
            Assert.Contains(queue, q => q.Reservation == counter.Number);
            Assert.Contains(queue, q => q.Reservation == expired.Number);   // lo cancelará el despachador: la reserva ya no está vigente
            Assert.All(queue, q => Assert.Equal((0, (string?)null), (q.Attempts, q.LastError)));

            // Los registros fueron por el canal web y la auditoría no guarda la contraseña (regla P-03)
            var db = adminScope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "RegisterCustomerAccount").ToListAsync();
            Assert.Equal(2, audits.Count(a => a.Outcome == AuditOutcome.Succeeded && a.Channel == RequestChannels.Web));
            Assert.All(customers, c => Assert.DoesNotContain(audits, a => (a.Details ?? string.Empty).Contains(c.Password, StringComparison.Ordinal)));
        }

        // El archivo de usuarios de prueba: el personal como siempre y las cuentas en su propia sección, con las mismas columnas
        var text = SeedUsersFile.Text(result);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var section = lines.FindIndex(l => l.StartsWith(SeedUsersFile.CustomersTitle, StringComparison.Ordinal));
        Assert.True(section > lines.FindIndex(l => l.StartsWith($"{admin.RoleName,-15} {admin.Name,-26} {admin.Email,-44} ", StringComparison.Ordinal)));
        Assert.All(customers, c => Assert.Contains($"{c.RoleName,-15} {c.Name,-26} {c.Email,-44} {c.Password}", lines.Skip(section)));
        Assert.All(customers, c => Assert.DoesNotContain(lines.Take(section), l => l.Contains(c.Email, StringComparison.Ordinal)));
        Assert.Contains($"Tienda web (V7): 2 cuentas de cliente con 4 reservas propias", text, StringComparison.Ordinal);
        // Su lector (las capturas del escritorio con la base local, ScreenshotRunner.LocalUsers) sigue encontrando la empresa, el
        // administrador y el cajero: la primera fila que empieza con el rol, el correo y la contraseña que siguen
        (string, string)? Find(string role) => lines
            .Select(l => Regex.Match(l, "^" + role + @"\s.*?\s(?<correo>[^\s@]+@[^\s@]+)\s+(?<clave>\S+)"))
            .Where(x => x.Success).Select(x => ((string, string)?)(x.Groups["correo"].Value, x.Groups["clave"].Value)).FirstOrDefault();
        Assert.Equal(tenant, Regex.Match(text, @"código de empresa: (\S+)").Groups[1].Value);
        Assert.Equal((admin.Email, admin.Password), Find("Administrador"));
        var cashier = result.Users.First(u => u.RoleCode == RoleCodes.Cashier);
        Assert.Equal((cashier.Email, cashier.Password), Find("Cajero"));
    }

    [Fact]
    public async Task La_demostracion_trae_imagenes_y_precios_para_el_punto_de_venta()
    {
        var (sp, demo) = await DemoWorkspaceTests.PrepareAsync();
        var (scope, mediator, _) = await DemoWorkspaceTests.SignInAsync(sp, demo);
        using (scope)
        {
            Assert.Equal(159, (await mediator.Send(new GetProductImagesQuery())).Count);
            Assert.All(await mediator.Send(new GetCatalogQuery()), c => Assert.True(c.HasImage && c.SalePrice > 0, c.Sku));
        }
    }
}
