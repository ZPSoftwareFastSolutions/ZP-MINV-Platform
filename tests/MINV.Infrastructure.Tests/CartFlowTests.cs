using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Sales;
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// V7 · Carrito (regla P-05) sobre la empresa de prueba EN MEMORIA, con los mismos casos de uso que el gateway, el escritorio y
/// el panel: reservar cualquier producto (un solo monitor, dos gabinetes, productos sin ranura) con los días para recogerlo y los
/// datos para la factura; idempotencia con los campos nuevos; vender en caja consume la reserva y factura con los datos de quien
/// reservó; liberar y vencer devuelven el stock; carrito de mostrador; el tipo no cambia y un carrito no se publica.
/// </summary>
public sealed class CartFlowTests : IAsyncLifetime
{
    private const string Tenant = "CART";
    private ServiceProvider _services = null!;
    private SeedResult _seed = null!;

    public async Task InitializeAsync() => (_services, _seed) = await LocalDataSeederTests.SeedAsync(days: 6, tenant: Tenant, seed: 23);

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private async Task<IServiceScope> LoginAsync(string role, string? branch = null)
    {
        var scope = _services.CreateScope();
        var who = _seed.Users.First(u => u.RoleCode == role && (branch is null || u.Branches == branch));
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand(Tenant, who.Email, who.Password, "pruebas", "7.0"));
        return scope;
    }

    private static Task<T> SendAsync<T>(IServiceScope scope, IRequest<T> request)
    {
        scope.ServiceProvider.GetRequiredService<IMinvDbContext>().ClearTracking();
        return scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    private static CreateStorefrontReservationCommand Cart(string key, IReadOnlyList<StorefrontReservationLineInput> lines, string phone = "71234567",
        int? holdDays = null, ReservationBuyerInput? buyer = null, string? notes = null, string name = "Ana Quispe") =>
        new(lines, new StorefrontContactInput(name, phone, "ana@correo.example"), notes, key, null, PcBuildKind.Cart, holdDays, buyer);

    /// <summary>Último correlativo de los números con ese prefijo (0 si todavía no hay ninguno): la carga de prueba ya trae
    /// carritos de la tienda y de mostrador (V7, regla P-13), así que la numeración de las pruebas sigue a la suya.</summary>
    private static async Task<int> LastNumberAsync(MinvWriteDbContext db, string prefix)
    {
        db.ChangeTracker.Clear();
        return (await db.PcBuilds.AsNoTracking().Where(b => b.Number.StartsWith(prefix + "-")).Select(b => b.Number).ToListAsync())
            .Select(n => int.Parse(n[(prefix.Length + 1)..], System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
    }

    private static string Numbered(string prefix, int correlative) => $"{prefix}-{correlative:000000}";

    private static async Task<(decimal OnHand, decimal Reserved)> StockAsync(MinvWriteDbContext db, string sku)
    {
        db.ChangeTracker.Clear();
        var branch = await db.Branches.SingleAsync(b => b.Code == LocalDataSeeder.BranchMain);
        var rows = await (from l in db.StockLevels join b in db.Batches on l.BatchId equals b.Id join v in db.ProductVariants on b.VariantId equals v.Id
                          where v.Sku == sku && l.BranchId == branch.Id select new { l.QuantityOnHand, l.QuantityReserved }).ToListAsync();
        return (rows.Sum(r => r.QuantityOnHand), rows.Sum(r => r.QuantityReserved));
    }

    /// <summary>Venta en la caja del usuario (abre el turno si hace falta, como en las demás pruebas de la tienda).</summary>
    private static async Task<CheckoutResult> SellAsync(IServiceScope pos, SellPcBuildCommand command)
    {
        try
        {
            return await SendAsync(pos, command);
        }
        catch (DomainException ex) when (ex.Code == "pos.closed")
        {
            await SendAsync(pos, new OpenPosSessionCommand("CAJA03", 500));
            return await SendAsync(pos, command);
        }
    }

    /// <summary>Usuario con los permisos indicados y nada más (la empresa de prueba no tiene un rol que vea ventas sin gestionar reservas).</summary>
    private sealed class LimitedUser(params string[] permissions) : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();

        public string? Email => "consulta@correo.example";

        public string? DisplayName => "Consulta de ventas";

        public IReadOnlyCollection<string> Permissions { get; } = permissions;

        public void SignIn(Guid userId, string email, string displayName, IReadOnlyCollection<string> permissions) => throw new NotSupportedException();

        public void SignOut() => throw new NotSupportedException();
    }

    [Fact]
    public async Task Reservar_un_carrito_reserva_cualquier_producto_con_sus_dias_y_datos_de_factura()
    {
        using var scope = await LoginAsync(RoleCodes.Admin);
        var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var clock = _services.GetRequiredService<DemoClock>();
        var snapshot = await SendAsync(scope, new GetStorefrontCatalogQuery());
        // La vigencia y los días que se pueden pedir los informa el servidor (la web ya no los supone)
        Assert.Equal((StorefrontOptions.DefaultReservationHours, 3), (snapshot.ReservationHours, snapshot.MaxHoldDays));
        var monitor = snapshot.Products.Where(p => p.Category == "MON" && p.Available >= 1).OrderBy(p => p.Sku, StringComparer.Ordinal).First();
        var cases = snapshot.Products.Where(p => p.Category == "CASE" && p.Available >= 2).OrderBy(p => p.Sku, StringComparer.Ordinal).Take(2).ToList();
        var game = snapshot.Products.Where(p => p.Category == "JUE" && p.Available >= 2).OrderBy(p => p.Sku, StringComparer.Ordinal).First();
        Assert.Equal(2, cases.Count);
        Assert.True(monitor.Serialized);
        // V7 · La carga de prueba ya trae carritos (de la tienda, de las cuentas de cliente y de mostrador): la numeración sigue
        var last = await LastNumberAsync(db, PcBuild.WebCartNumberPrefix);
        var seededCarts = await db.PcBuilds.AsNoTracking().Where(b => b.Kind == PcBuildKind.Cart).Select(b => b.Number).ToListAsync();
        string[] numbers = [Numbered(PcBuild.WebCartNumberPrefix, last + 1), Numbered(PcBuild.WebCartNumberPrefix, last + 2),
            Numbered(PcBuild.WebCartNumberPrefix, last + 3)];

        // 1. Un solo monitor, sin pasar por el armador y sin ranura: 1 día para recogerlo
        var single = (await SendAsync(scope, Cart("c-monitor", [new StorefrontReservationLineInput(monitor.Sku)], holdDays: 1))).Reservation;
        Assert.Equal(numbers[0], single.Number);
        // V7 (B3) · Con correo de contacto, la confirmación queda encolada (mailQueued de verdad)
        Assert.Equal(("Reserved", "cart", true, false, "CM"), (single.Status, single.Kind, single.MailQueued, single.HasCompatibilityWarnings, single.Branch));
        var only = Assert.Single(single.Lines);
        Assert.Equal((monitor.Sku, (string?)null, 1, monitor.Price), (only.Sku, only.Slot, only.Quantity, only.UnitPrice));
        Assert.InRange((single.ReservedUntil!.Value - clock.UtcNow).TotalHours, 23.9, 24.1);
        Assert.Equal(monitor.Reserved + 1, (await SendAsync(scope, new GetStorefrontProductQuery(monitor.Slug))).Reserved);

        // 2. Varios productos: dos gabinetes distintos (ranura única en un armado), un juego y otra vez el monitor; con datos de factura
        var before = await StockAsync(db, cases[0].Sku);
        var buyer = new ReservationBuyerInput(1, " 4567890 ", "1a", " Ana Quispe Mamani ");
        var lines = new List<StorefrontReservationLineInput>
        {
            new(cases[0].Sku, 2, "case"), new(cases[1].Sku, 1, "case"), new(game.Sku, 2), new(monitor.Sku.ToLowerInvariant(), 1),
        };
        var result = await SendAsync(scope, Cart("c-varios", lines, holdDays: 3, buyer: buyer, notes: " Paso el sábado \r\n"));
        Assert.False(result.Replayed);
        var view = result.Reservation;
        Assert.Equal((numbers[1], "cart", "Reserved", "Paso el sábado"), (view.Number, view.Kind, view.Status, view.Notes));
        Assert.InRange((view.ReservedUntil!.Value - clock.UtcNow).TotalHours, 71.9, 72.1);
        Assert.Equal(4, view.Lines.Count);
        Assert.Equal(2, view.Lines.Count(l => l.Slot == "case"));
        Assert.Equal([game.Sku, monitor.Sku], view.Lines.Where(l => l.Slot is null).Select(l => l.Sku).Order(StringComparer.Ordinal));
        Assert.Equal(view.Lines.Sum(l => l.Subtotal), view.Total);
        Assert.Equal((before.OnHand, before.Reserved + 2), await StockAsync(db, cases[0].Sku));
        // La respuesta pública nunca lleva los datos para la factura, ni el teléfono ni el correo (reglas S-06 y P-05)
        var json = JsonSerializer.Serialize(view);
        Assert.DoesNotContain("4567890", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Mamani", json, StringComparison.Ordinal);
        Assert.DoesNotContain("71234567", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ana@correo.example", json, StringComparison.Ordinal);

        // En la base: carrito del canal Web, con su bitácora, una reserva de stock por línea y los datos de factura normalizados
        var cart = await db.PcBuilds.AsNoTracking().Include(b => b.Lines).Include(b => b.History).SingleAsync(b => b.Number == view.Number);
        Assert.Equal((PcBuildKind.Cart, PcBuildChannel.Web, PcBuildStatus.Reserved, false), (cart.Kind, cart.Channel, cart.Status, cart.QuotedWithErrors));
        Assert.Equal((1, "4567890", "1A", "Ana Quispe Mamani"), (cart.BuyerDocumentType, cart.BuyerDocumentNumber, cart.BuyerComplement, cart.BuyerName));
        Assert.Equal(2, cart.Lines.Count(l => l.Slot is null));
        Assert.Equal(2, cart.Lines.Count(l => l.Slot == PcSlot.Case));
        Assert.Equal([PcBuildEventAction.Created, PcBuildEventAction.Quoted, PcBuildEventAction.Reserved], cart.History.OrderBy(h => h.OccurredAt).Select(h => h.Action));
        var lineIds = cart.Lines.Select(l => l.Id).ToList();
        var reservations = await db.StockReservations.AsNoTracking().Where(r => r.PcBuildLineId != null && lineIds.Contains(r.PcBuildLineId.Value)).ToListAsync();
        Assert.Equal(lineIds.Order(), reservations.Select(r => r.PcBuildLineId!.Value).Distinct().Order());
        Assert.Equal(6m, reservations.Sum(r => r.Quantity));
        Assert.All(reservations, r => Assert.Equal(ReservationStatus.Active, r.Status));
        var outbox = await db.OutboxEvents.AsNoTracking().Where(e => e.EventType == Domain.Integration.IntegrationEvents.PcBuildReserved).Select(e => e.Payload).ToListAsync();
        Assert.Contains(outbox, p => p.Contains(view.Number, StringComparison.Ordinal) && p.Contains("\"kind\":\"Cart\"", StringComparison.Ordinal));
        Assert.DoesNotContain(outbox, p => p.Contains("4567890", StringComparison.Ordinal));   // los eventos nunca llevan datos del cliente

        // 3. Idempotencia con los campos nuevos: repetir igual devuelve la misma reserva; otro plazo, otros datos u otro tipo, no
        var replayed = await SendAsync(scope, Cart("c-varios", lines, holdDays: 3, buyer: buyer, notes: " Paso el sábado \r\n"));
        Assert.True(replayed.Replayed);
        Assert.Equal((view.Number, "cart", view.ReservedUntil), (replayed.Reservation.Number, replayed.Reservation.Kind, replayed.Reservation.ReservedUntil));
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => SendAsync(scope, Cart("c-varios", lines, holdDays: 2, buyer: buyer, notes: "Paso el sábado")));
        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            SendAsync(scope, Cart("c-varios", lines, holdDays: 3, buyer: buyer with { DocumentNumber = "4567891" }, notes: "Paso el sábado")));
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => SendAsync(scope, Cart("c-varios", lines, holdDays: 3, notes: "Paso el sábado")));
        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            SendAsync(scope, Cart("c-varios", lines, holdDays: 3, buyer: buyer, notes: "Paso el sábado") with { Kind = PcBuildKind.Build }));
        Assert.Equal((before.OnHand, before.Reserved + 2), await StockAsync(db, cases[0].Sku));
        Assert.Equal(seededCarts.Count + 2, await db.PcBuilds.AsNoTracking().CountAsync(b => b.Kind == PcBuildKind.Cart));

        // 4. Los armados siguen igual: su numeración es aparte, deducen la ranura y dos gabinetes en un armado se rechazan
        var build = (await SendAsync(scope, new CreateStorefrontReservationCommand([new StorefrontReservationLineInput(cases[0].Sku, 1)],
            new StorefrontContactInput("Mateo Condori", "76543210"), null, "a-armado"))).Reservation;
        Assert.StartsWith(PcBuild.WebNumberPrefix + "-", build.Number, StringComparison.Ordinal);
        Assert.Equal(("build", "case"), (build.Kind, Assert.Single(build.Lines).Slot));
        Assert.InRange((build.ReservedUntil!.Value - clock.UtcNow).TotalHours, 47.9, 48.1);
        Assert.Equal("pcbuild.slot", (await Assert.ThrowsAsync<DomainException>(() => SendAsync(scope, new CreateStorefrontReservationCommand(
            [new StorefrontReservationLineInput(cases[0].Sku, 1, "case"), new StorefrontReservationLineInput(cases[1].Sku, 1, "case")],
            new StorefrontContactInput("Mateo Condori", "76543210"), null, "a-dos-gabinetes")))).Code);
        Assert.Equal(numbers[2], (await SendAsync(scope, Cart("c-tercero", [new StorefrontReservationLineInput(game.Sku)], phone: "70000002"))).Reservation.Number);

        // 5. El cliente consulta su carrito con el número y el teléfono; el escritorio lo filtra por tipo y ve los datos de factura
        var mine = await SendAsync(scope, new GetStorefrontReservationQuery(view.Number.ToLowerInvariant(), "+591 7123-4567"));
        Assert.Equal((view.Number, "cart", "Reserved", 4), (mine.Number, mine.Kind, mine.Status, mine.Lines.Count));
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(scope, new GetStorefrontReservationQuery(view.Number, "79999999")));
        var carts = await SendAsync(scope, new GetPcBuildsQuery(Kind: PcBuildKind.Cart));
        Assert.Equal(seededCarts.Concat(numbers).Order(StringComparer.Ordinal), carts.Select(c => c.Number).Order(StringComparer.Ordinal));
        Assert.All(carts, c => Assert.Equal((PcBuildKind.Cart, true), (c.Kind, c.IsCompatible)));
        Assert.All(carts.Where(c => numbers.Contains(c.Number)), c => Assert.Equal(PcBuildChannel.Web, c.Channel));
        var row = carts.Single(c => c.Number == view.Number);
        Assert.Equal((1, "4567890", "1A", "Ana Quispe Mamani", "71234567", 6m),
            (row.BuyerDocumentType, row.BuyerDocumentNumber, row.BuyerComplement, row.BuyerName, row.ContactPhone, row.Reserved));
        var builds = await SendAsync(scope, new GetPcBuildsQuery(Kind: PcBuildKind.Build));
        Assert.NotEmpty(builds);
        Assert.All(builds, b => Assert.Equal(PcBuildKind.Build, b.Kind));
        Assert.Equal(carts.Count + builds.Count, (await SendAsync(scope, new GetPcBuildsQuery())).Count);
        var detail = await SendAsync(scope, new GetPcBuildQuery(view.Number));
        Assert.Equal((true, 0, 4), (detail.Check.IsCompatible, detail.Check.Issues.Count, detail.QuotedItems.Count));   // sin avisos de piezas faltantes
        Assert.Equal(2, detail.QuotedItems.Count(i => i.Slot is null));
        Assert.Equal(view.Total, detail.QuotedItems.Sum(i => i.Subtotal));
        // Sin sales.pcbuild.manage no se ven ni el contacto ni los datos para la factura (regla S-06)
        scope.ServiceProvider.GetRequiredService<IMinvDbContext>().ClearTracking();
        var limited = await new GetPcBuildsHandler(scope.ServiceProvider.GetRequiredService<IMinvDbContext>(), clock, new LimitedUser(PermissionCodes.SalesView))
            .Handle(new GetPcBuildsQuery(Kind: PcBuildKind.Cart), default);
        var hidden = limited.Single(c => c.Number == view.Number);
        Assert.Equal(((int?)null, (string?)null, (string?)null, (string?)null, (string?)null, (string?)null),
            (hidden.BuyerDocumentType, hidden.BuyerDocumentNumber, hidden.BuyerComplement, hidden.BuyerName, hidden.ContactPhone, hidden.ContactEmail));
        Assert.Equal((PcBuildKind.Cart, "Ana Quispe"), (hidden.Kind, hidden.ContactName));

        // 6. La auditoría enmascara los datos nuevos igual que el teléfono
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "CreateStorefrontReservation").Select(a => a.Details ?? string.Empty).ToListAsync();
        Assert.Contains(audits, a => a.Contains("****890", StringComparison.Ordinal));
        Assert.DoesNotContain(audits, a => a.Contains("4567890", StringComparison.Ordinal) || a.Contains("Mamani", StringComparison.Ordinal));
        Assert.DoesNotContain(audits, a => a.Contains("\"71234567\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Vender_en_caja_consume_la_reserva_del_carrito_y_factura_con_los_datos_de_quien_reservo()
    {
        using var scope = await LoginAsync(RoleCodes.Admin);
        using var pos = await LoginAsync(RoleCodes.Cashier, LocalDataSeeder.BranchMain);
        var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var snapshot = await SendAsync(scope, new GetStorefrontCatalogQuery());
        var loose = snapshot.Products.Where(p => !p.Serialized && p.Available >= 4 && p.Category is "JUE" or "CAB").OrderBy(p => p.Sku, StringComparer.Ordinal)
            .Take(2).ToList();
        Assert.Equal(2, loose.Count);
        var before = (A: await StockAsync(db, loose[0].Sku), B: await StockAsync(db, loose[1].Sku));

        // 1. Carrito de dos productos sin ranura, con el CI de quien reserva: el cajero cobra sin capturar comprador
        var reserved = (await SendAsync(scope, Cart("v-1", [new StorefrontReservationLineInput(loose[0].Sku, 2), new StorefrontReservationLineInput(loose[1].Sku, 1)],
            buyer: new ReservationBuyerInput(1, "4567890", null, "Ana Quispe Mamani")))).Reservation;
        Assert.Equal((before.A.OnHand, before.A.Reserved + 2), await StockAsync(db, loose[0].Sku));
        Assert.Equal((before.B.OnHand, before.B.Reserved + 1), await StockAsync(db, loose[1].Sku));
        var sale = await SellAsync(pos, new SellPcBuildCommand(reserved.Number, "EFECTIVO", null, 100000m));
        Assert.Equal(reserved.Total, sale.Total);
        Assert.Equal(3m, sale.Lines.Sum(l => l.Quantity));
        // La existencia baja UNA vez y lo reservado vuelve a lo de antes (regla S-04)
        Assert.Equal((before.A.OnHand - 2, before.A.Reserved), await StockAsync(db, loose[0].Sku));
        Assert.Equal((before.B.OnHand - 1, before.B.Reserved), await StockAsync(db, loose[1].Sku));
        var cart = await db.PcBuilds.AsNoTracking().Include(b => b.Lines).Include(b => b.History).SingleAsync(b => b.Number == reserved.Number);
        Assert.Equal((PcBuildStatus.Sold, PcBuildKind.Cart), (cart.Status, cart.Kind));
        Assert.Equal(PcBuildEventAction.Sold, cart.History.OrderBy(h => h.OccurredAt).Last().Action);
        var lineIds = cart.Lines.Select(l => l.Id).ToList();
        Assert.All(await db.StockReservations.AsNoTracking().Where(r => r.PcBuildLineId != null && lineIds.Contains(r.PcBuildLineId.Value)).ToListAsync(),
            r => Assert.Equal(ReservationStatus.Consumed, r.Status));
        var sold = await SendAsync(scope, new GetStorefrontReservationQuery(reserved.Number, "71234567"));
        Assert.Equal(("Sold", "Vendida", "cart"), (sold.Status, sold.StatusText, sold.Kind));
        Assert.Contains(await db.OutboxEvents.AsNoTracking().Where(e => e.EventType == Domain.Integration.IntegrationEvents.PcBuildSold).Select(e => e.Payload).ToListAsync(),
            p => p.Contains(reserved.Number, StringComparison.Ordinal) && p.Contains("\"kind\":\"Cart\"", StringComparison.Ordinal));
        // La factura del SIN salió a nombre de quien reservó
        Assert.NotNull(sale.FiscalDocumentId);
        var document = await db.Set<FiscalDocument>().AsNoTracking().SingleAsync(d => d.Id == sale.FiscalDocumentId);
        Assert.Equal((1, "4567890", "Ana Quispe Mamani", "ana@correo.example"),
            (document.BuyerDocumentType, document.BuyerDocumentNumber, document.BuyerName, document.BuyerEmail));
        // Vendido ya no se vende, ni se libera, ni lo cancela el cliente
        Assert.Equal("pcbuild.state", (await Assert.ThrowsAsync<DomainException>(() => SellAsync(pos, new SellPcBuildCommand(reserved.Number, "EFECTIVO", null, 100000m)))).Code);
        Assert.Equal("pcbuild.state", (await Assert.ThrowsAsync<DomainException>(() =>
            SendAsync(scope, new CancelStorefrontReservationCommand(reserved.Number, "71234567")))).Code);

        // 2. Si el cajero captura otro comprador, manda el cajero
        var second = (await SendAsync(scope, Cart("v-2", [new StorefrontReservationLineInput(loose[0].Sku, 1)], phone: "70000003",
            buyer: new ReservationBuyerInput(1, "4567890", null, "Ana Quispe Mamani")))).Reservation;
        var other = await SellAsync(pos, new SellPcBuildCommand(second.Number, "EFECTIVO", null, 100000m,
            Buyer: new MINV.Application.Billing.FiscalBuyerInput(1, "7654321", null, "Luis Rojas Vaca", null)));
        var otherDocument = await db.Set<FiscalDocument>().AsNoTracking().SingleAsync(d => d.Id == other.FiscalDocumentId);
        Assert.Equal(("7654321", "Luis Rojas Vaca"), (otherDocument.BuyerDocumentNumber, otherDocument.BuyerName));
        Assert.Equal((before.A.OnHand - 3, before.A.Reserved), await StockAsync(db, loose[0].Sku));

        // 3. Un solo monitor (lleva serie): se reserva sin serie y la serie se indica al cobrar
        var monitor = snapshot.Products.Where(p => p.Category == "MON" && p.Serialized && p.Available >= 1).OrderBy(p => p.Sku, StringComparer.Ordinal).First();
        var stock = await StockAsync(db, monitor.Sku);
        var single = (await SendAsync(scope, Cart("v-3", [new StorefrontReservationLineInput(monitor.Sku)], phone: "70000004", holdDays: 2))).Reservation;
        Assert.Equal((stock.OnHand, stock.Reserved + 1), await StockAsync(db, monitor.Sku));
        Assert.Equal(SerialErrorCodes.Required, (await Assert.ThrowsAsync<DomainException>(() =>
            SellAsync(pos, new SellPcBuildCommand(single.Number, "EFECTIVO", null, 100000m)))).Code);
        Assert.Equal((stock.OnHand, stock.Reserved + 1), await StockAsync(db, monitor.Sku));   // el rechazo no consumió la reserva
        var serial = (await SendAsync(pos, new GetAvailableSerialsQuery(monitor.Sku)))[0].Serial;
        var withSerial = await SellAsync(pos, new SellPcBuildCommand(single.Number, "EFECTIVO", [new SkuSerials(monitor.Sku, [serial])], 100000m));
        Assert.Equal(monitor.Price, withSerial.Total);
        Assert.Equal((stock.OnHand - 1, stock.Reserved), await StockAsync(db, monitor.Sku));
        Assert.Equal(SerialNumberStatus.Sold, (await db.Set<SerialNumber>().AsNoTracking().SingleAsync(s => s.Serial == serial)).Status);
    }

    [Fact]
    public async Task Liberar_y_vencer_un_carrito_devuelven_el_stock_segun_los_dias_pedidos()
    {
        using var scope = await LoginAsync(RoleCodes.Admin);
        var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var clock = _services.GetRequiredService<DemoClock>();
        var snapshot = await SendAsync(scope, new GetStorefrontCatalogQuery());
        var product = snapshot.Products.Where(p => !p.Serialized && p.Available >= 6 && p.Category == "JUE").OrderBy(p => p.Sku, StringComparer.Ordinal).First();
        var before = await StockAsync(db, product.Sku);
        StorefrontReservationLineInput[] Line(int quantity) => [new(product.Sku, quantity)];
        // Las reservas vigentes de la carga (V6 y V7): de hace unas horas y de 48 h, vencen entre las 25 y las 49 horas
        var seeded = await db.PcBuilds.AsNoTracking().CountAsync(b => b.Status == PcBuildStatus.Reserved);

        // 1. El cliente libera su carrito con el teléfono con que reservó: el stock vuelve
        var cancelled = (await SendAsync(scope, Cart("l-1", Line(2), phone: "76543210"))).Reservation;
        Assert.Equal((before.OnHand, before.Reserved + 2), await StockAsync(db, product.Sku));
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(scope, new CancelStorefrontReservationCommand(cancelled.Number, "79999999")));
        var view = await SendAsync(scope, new CancelStorefrontReservationCommand(cancelled.Number, "76543210"));
        Assert.Equal(("Cancelled", "Cancelada", "cart"), (view.Status, view.StatusText, view.Kind));
        Assert.Equal(before, await StockAsync(db, product.Sku));
        // El personal también lo libera (con motivo) desde el escritorio o el panel
        var staff = (await SendAsync(scope, Cart("l-2", Line(1), phone: "70000005"))).Reservation;
        var released = await SendAsync(scope, new ReleasePcBuildReservationCommand(staff.Number, "El cliente avisó que ya no lo quiere"));
        Assert.Equal((PcBuildStatus.Cancelled, PcBuildKind.Cart, 0m), (released.Status, released.Kind, released.Reserved));
        Assert.Equal(before, await StockAsync(db, product.Sku));

        // 2. Vencer: cada carrito vence a los días que pidió (1 día = 24 h; 3 días = 72 h); lo cierra el trabajo, nunca «al leer»
        var oneDay = (await SendAsync(scope, Cart("l-3", Line(1), phone: "70000006", holdDays: 1))).Reservation;
        var threeDays = (await SendAsync(scope, Cart("l-4", Line(3), phone: "70000007", holdDays: 3))).Reservation;
        Assert.Equal((before.OnHand, before.Reserved + 4), await StockAsync(db, product.Sku));
        var real = clock.UtcNow;
        try
        {
            Assert.Equal(0, await SendAsync(scope, new ExpirePcBuildReservationsCommand()));
            clock.StartAt(real.AddHours(25));
            Assert.Equal("Expired", (await SendAsync(scope, new GetStorefrontReservationQuery(oneDay.Number, "70000006"))).Status);
            Assert.Equal("Reserved", (await SendAsync(scope, new GetStorefrontReservationQuery(threeDays.Number, "70000007"))).Status);
            Assert.Equal((before.OnHand, before.Reserved + 4), await StockAsync(db, product.Sku));   // todavía nadie la cerró
            Assert.Equal(1, await SendAsync(scope, new ExpirePcBuildReservationsCommand()));
            var expired = await SendAsync(scope, new GetStorefrontReservationQuery(oneDay.Number, "70000006"));
            Assert.Equal(("Expired", "Vencida", PcBuild.ExpiredReason, "cart"), (expired.Status, expired.StatusText, expired.CancelReason, expired.Kind));
            Assert.Equal((before.OnHand, before.Reserved + 3), await StockAsync(db, product.Sku));
            clock.StartAt(real.AddHours(73));
            // Vence el de 3 días y también las vigentes de la carga (48 h): la reserva web de la V6 y, V7, los carritos y las reservas
            // de las cuentas de cliente
            Assert.Equal(1 + seeded, await SendAsync(scope, new ExpirePcBuildReservationsCommand()));
            Assert.Equal(before, await StockAsync(db, product.Sku));
            Assert.Empty(await db.PcBuilds.AsNoTracking().Where(b => b.Status == PcBuildStatus.Reserved).ToListAsync());
            var history = (await SendAsync(scope, new GetPcBuildQuery(threeDays.Number))).History!;
            Assert.Equal(PcBuildEventAction.Expired, history.Last().Action);
            // Un carrito vencido ya no se vende
            using var pos = await LoginAsync(RoleCodes.Cashier, LocalDataSeeder.BranchMain);
            Assert.Equal("pcbuild.state", (await Assert.ThrowsAsync<DomainException>(() =>
                SellAsync(pos, new SellPcBuildCommand(threeDays.Number, "EFECTIVO", null, 100000m)))).Code);
        }
        finally
        {
            clock.StartAt(real);
        }
    }

    [Fact]
    public async Task Sin_stock_o_con_datos_invalidos_no_se_reserva_nada()
    {
        using var scope = await LoginAsync(RoleCodes.Admin);
        var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var snapshot = await SendAsync(scope, new GetStorefrontCatalogQuery());
        var plenty = snapshot.Products.Where(p => p.Category == "JUE" && p.Available >= 2).OrderBy(p => p.Sku, StringComparer.Ordinal).First();
        var scarce = snapshot.Products.Where(p => p.Available is > 0 and < 16 && p.Category == "MON").OrderBy(p => p.Available).ThenBy(p => p.Sku, StringComparer.Ordinal).First();
        var requested = (int)scarce.Available + 1;
        var carts = await db.PcBuilds.AsNoTracking().CountAsync(b => b.Kind == PcBuildKind.Cart);

        // Todo o nada: el juego alcanzaba, el monitor no → no se reserva ninguno y se informa cuál falta y cuánto hay
        var ex = await Assert.ThrowsAsync<StorefrontStockException>(() => SendAsync(scope, Cart("s-falta",
            [new StorefrontReservationLineInput(plenty.Sku, 1), new StorefrontReservationLineInput(scarce.Sku, requested)], phone: "76543219")));
        var shortage = Assert.Single(ex.Shortages);
        Assert.Equal((scarce.Sku, requested, scarce.Available), (shortage.Sku, shortage.Requested, shortage.Available));
        Assert.Equal(StorefrontStockException.ErrorCode, ex.Code);
        Assert.Equal(plenty.Reserved, (await SendAsync(scope, new GetStorefrontProductQuery(plenty.Slug))).Reserved);
        // La misma llave sirve después, con una cantidad que sí hay (el rechazo no dejó registro de idempotencia)
        var retry = await SendAsync(scope, Cart("s-falta", [new StorefrontReservationLineInput(plenty.Sku, 1)], phone: "76543219"));
        Assert.False(retry.Replayed);
        Assert.Equal(carts + 1, await db.PcBuilds.AsNoTracking().CountAsync(b => b.Kind == PcBuildKind.Cart));

        // Datos inválidos: validación (400 en el gateway) o regla del dominio (422); ninguno crea nada
        StorefrontReservationLineInput[] line = [new(plenty.Sku, 1)];
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(scope, Cart("s-dias-0", line, holdDays: 0)));
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(scope, Cart("s-dias-4", line, holdDays: 4)));
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(scope, Cart("s-notas", line, notes: "Paso el sábado\r\nBcc: otro@correo.example")));
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(scope, Cart("s-nombre", line, name: "Ana\nQuispe")));
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(scope, Cart("s-razon", line, buyer: new ReservationBuyerInput(5, "1023456029", null, "Andina\tS.R.L."))));
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(scope, Cart("s-tipo-doc", line, buyer: new ReservationBuyerInput(7, "123"))));
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(scope, Cart("s-vacio", [])));
        Assert.Equal("buyer.doc_numeric", (await Assert.ThrowsAsync<DomainException>(() =>
            SendAsync(scope, Cart("s-ci", line, buyer: new ReservationBuyerInput(1, "45678-LP"))))).Code);
        Assert.Equal("buyer.complement", (await Assert.ThrowsAsync<DomainException>(() =>
            SendAsync(scope, Cart("s-nit", line, buyer: new ReservationBuyerInput(5, "1023456029", "1A"))))).Code);
        Assert.Equal("pcbuild.contact_phone", (await Assert.ThrowsAsync<DomainException>(() => SendAsync(scope, Cart("s-tel", line, phone: "123")))).Code);
        Assert.Equal("pcbuild.slot", (await Assert.ThrowsAsync<DomainException>(() =>
            SendAsync(scope, Cart("s-ranura", [new StorefrontReservationLineInput(plenty.Sku, 1, "teclado")])))).Code);
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(scope, Cart("s-sku", [new StorefrontReservationLineInput("NO-EXISTE")])));
        Assert.Equal(carts + 1, await db.PcBuilds.AsNoTracking().CountAsync(b => b.Kind == PcBuildKind.Cart));
        Assert.Equal(plenty.Reserved + 1, (await SendAsync(scope, new GetStorefrontProductQuery(plenty.Slug))).Reserved);
        // Sin permiso: el rol Consulta no reserva carritos
        using var readOnly = await LoginAsync(RoleCodes.ReadOnly);
        await Assert.ThrowsAsync<AccessDeniedException>(() => SendAsync(readOnly, Cart("s-sin-permiso", line)));
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            SendAsync(readOnly, new ReserveCartCommand([new CartItemInput(plenty.Sku)], "Luis Rojas", "76543210")));
    }

    [Fact]
    public async Task El_personal_reserva_un_carrito_en_el_mostrador_y_el_tipo_no_cambia_ni_se_publica()
    {
        using var seller = await LoginAsync(RoleCodes.Sales, LocalDataSeeder.BranchMain);
        var db = seller.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var clock = _services.GetRequiredService<DemoClock>();
        using var admin = await LoginAsync(RoleCodes.Admin);
        var snapshot = await SendAsync(admin, new GetStorefrontCatalogQuery());
        var psus = snapshot.Products.Where(p => p.Category == "PSU" && p.Available >= 1).OrderBy(p => p.Sku, StringComparer.Ordinal).Take(2).ToList();
        var game = snapshot.Products.Where(p => p.Category == "JUE" && p.Available >= 3).OrderBy(p => p.Sku, StringComparer.Ordinal).First();
        Assert.Equal(2, psus.Count);
        var before = await StockAsync(db, game.Sku);
        // V7 · La carga de prueba ya trae un carrito de mostrador en la casa matriz: la numeración sigue a la suya
        var prefix = $"{PcBuild.CartNumberPrefix}-{LocalDataSeeder.BranchMain}";
        var last = await LastNumberAsync(db, prefix);
        var counterCarts = await db.PcBuilds.AsNoTracking().CountAsync(b => b.Kind == PcBuildKind.Cart && b.Channel == PcBuildChannel.Desktop);

        // 1. Carrito de mostrador: dos fuentes distintas y un juego, para un cliente que deja su nombre y su teléfono
        var row = await SendAsync(seller, new ReserveCartCommand(
            [new CartItemInput(psus[0].Sku), new CartItemInput(psus[1].Sku), new CartItemInput(game.Sku, 2)], " Luis Rojas ", "(2) 221-2345", "Luis@Correo.example",
            "Pasa el lunes", 2, new ReservationBuyerInput(5, "1023456029", null, "Comercial Andina S.R.L."), "CF"));
        Assert.Equal(Numbered(prefix, last + 1), row.Number);
        Assert.Equal((PcBuildKind.Cart, PcBuildChannel.Desktop, PcBuildStatus.Reserved, LocalDataSeeder.BranchMain, true, false),
            (row.Kind, row.Channel, row.Status, row.BranchCode, row.IsCompatible, row.QuotedWithErrors));
        Assert.Equal(("Reserva de Luis Rojas", "Luis Rojas", "22212345", "luis@correo.example", "Pasa el lunes"),
            (row.Name, row.ContactName, row.ContactPhone, row.ContactEmail, row.Notes));
        Assert.Equal((5, "1023456029", "Comercial Andina S.R.L.", 3, 4m), (row.BuyerDocumentType, row.BuyerDocumentNumber, row.BuyerName, row.Items, row.Reserved));
        Assert.NotNull(row.Customer);
        Assert.InRange((row.ReservedUntil!.Value - clock.UtcNow).TotalHours, 47.9, 48.1);
        Assert.Equal(psus[0].Price + psus[1].Price + 2 * game.Price, row.Total);
        Assert.Equal((before.OnHand, before.Reserved + 2), await StockAsync(db, game.Sku));
        var cart = await db.PcBuilds.AsNoTracking().Include(b => b.Lines).Include(b => b.History).SingleAsync(b => b.Number == row.Number);
        Assert.All(cart.Lines, l => Assert.Null(l.Slot));
        Assert.Contains("Carrito creado en el mostrador", cart.History.OrderBy(h => h.OccurredAt).First().Detail, StringComparison.Ordinal);
        // No es una reserva de la tienda: la consulta pública por número y teléfono no la encuentra, y la web no la lista como armado sugerido
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(admin, new GetStorefrontReservationQuery(row.Number, "22212345")));
        Assert.DoesNotContain(await SendAsync(admin, new GetStorefrontPresetsQuery()), p => p.Number == row.Number);
        Assert.Equal("pcbuild.publish_kind", (await Assert.ThrowsAsync<DomainException>(() => SendAsync(seller, new PublishPcBuildCommand(row.Number)))).Code);
        // La auditoría del mostrador enmascara teléfono, correo y documento
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "ReserveCart").Select(a => a.Details ?? string.Empty).ToListAsync();
        Assert.NotEmpty(audits);
        Assert.DoesNotContain(audits, a => a.Contains("221-2345", StringComparison.Ordinal) || a.Contains("1023456029", StringComparison.Ordinal)
                                           || a.Contains("Correo.example", StringComparison.OrdinalIgnoreCase) || a.Contains("Andina", StringComparison.Ordinal));
        // Liberar devuelve el stock
        var released = await SendAsync(seller, new ReleasePcBuildReservationCommand(row.Number, "El cliente no volvió"));
        Assert.Equal((PcBuildStatus.Cancelled, "El cliente no volvió", 0m), (released.Status, released.CancelReason, released.Reserved));
        Assert.Equal(before, await StockAsync(db, game.Sku));

        // 2. Sin stock suficiente, sin cliente o con más días de los que admite el servidor no se reserva nada
        var shortage = await Assert.ThrowsAsync<StorefrontStockException>(() => SendAsync(seller, new ReserveCartCommand(
            [new CartItemInput(game.Sku, 1), new CartItemInput(psus[0].Sku, 16)], "Luis Rojas", "76543210")));
        Assert.Equal(psus[0].Sku, Assert.Single(shortage.Shortages).Sku);
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(seller, new ReserveCartCommand([new CartItemInput(game.Sku)], "Luis", "76543210", CustomerCode: "NO-EXISTE")));
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(seller, new ReserveCartCommand([new CartItemInput(game.Sku)], "Luis", "76543210", HoldDays: 4)));
        seller.ServiceProvider.GetRequiredService<IMinvDbContext>().ClearTracking();
        var strict = new ReserveCartHandler(seller.ServiceProvider.GetRequiredService<IMinvDbContext>(), seller.ServiceProvider.GetRequiredService<ICurrentUser>(),
            clock, new StorefrontOptions(12, 24));
        Assert.Equal("storefront.hold_days", (await Assert.ThrowsAsync<DomainException>(() =>
            strict.Handle(new ReserveCartCommand([new CartItemInput(game.Sku)], "Luis Rojas", "76543210", HoldDays: 2), default))).Code);
        seller.ServiceProvider.GetRequiredService<IMinvDbContext>().ClearTracking();
        Assert.Equal(before, await StockAsync(db, game.Sku));
        Assert.Equal(counterCarts + 1, await db.PcBuilds.AsNoTracking().CountAsync(b => b.Kind == PcBuildKind.Cart && b.Channel == PcBuildChannel.Desktop));

        // 3. Guardar un carrito con el caso de uso del armador: borrador RES, cotización sin compatibilidad, reserva y anulación
        var draft = await SendAsync(seller, new SavePcBuildCommand(null, "Carrito de Carla", "CF",
            [new PcBuildItemInput(null, psus[0].Sku), new PcBuildItemInput(PcSlot.Psu, psus[1].Sku), new PcBuildItemInput(null, game.Sku, 3)], Kind: PcBuildKind.Cart));
        Assert.Equal((Numbered(prefix, last + 2), PcBuildKind.Cart, PcBuildStatus.Draft, true), (draft.Number, draft.Kind, draft.Status, draft.IsCompatible));
        Assert.Equal("pcbuild.kind", (await Assert.ThrowsAsync<DomainException>(() => SendAsync(seller,
            new SavePcBuildCommand(draft.Id, "Carrito de Carla", "CF", [new PcBuildItemInput(PcSlot.Psu, psus[0].Sku)], Quote: true)))).Code);
        var quoted = await SendAsync(seller, new SavePcBuildCommand(draft.Id, "Carrito de Carla", "CF",
            [new PcBuildItemInput(null, psus[0].Sku), new PcBuildItemInput(null, psus[1].Sku), new PcBuildItemInput(null, game.Sku, 3)], Quote: true, ValidDays: 5,
            Kind: PcBuildKind.Cart));
        Assert.Equal((draft.Number, PcBuildStatus.Quoted, false, 3), (quoted.Number, quoted.Status, quoted.QuotedWithErrors, quoted.Items));
        Assert.Equal("pcbuild.publish_kind", (await Assert.ThrowsAsync<DomainException>(() => SendAsync(seller, new PublishPcBuildCommand(quoted.Number)))).Code);
        var reserved = await SendAsync(seller, new ReservePcBuildCommand(quoted.Number, 24));
        Assert.Equal((PcBuildStatus.Reserved, 5m), (reserved.Status, reserved.Reserved));
        Assert.Equal((before.OnHand, before.Reserved + 3), await StockAsync(db, game.Sku));
        Assert.Contains("anulado", await SendAsync(seller, new CancelPcBuildCommand(quoted.Number, "Cambió de idea")), StringComparison.Ordinal);
        Assert.Equal(before, await StockAsync(db, game.Sku));
        // Un armado de PC sigue exigiendo la ranura de cada pieza y se numera ARM
        Assert.Equal("pcbuild.slot", (await Assert.ThrowsAsync<DomainException>(() => SendAsync(seller,
            new SavePcBuildCommand(null, "Armado sin ranura", null, [new PcBuildItemInput(null, psus[0].Sku)])))).Code);
        var build = await SendAsync(seller, new SavePcBuildCommand(null, "Solo la fuente", null, [new PcBuildItemInput(PcSlot.Psu, psus[0].Sku)]));
        Assert.StartsWith($"ARM-{LocalDataSeeder.BranchMain}-", build.Number, StringComparison.Ordinal);
        Assert.Equal(PcBuildKind.Build, build.Kind);
        Assert.Equal("pcbuild.kind", (await Assert.ThrowsAsync<DomainException>(() => SendAsync(seller,
            new SavePcBuildCommand(build.Id, "Solo la fuente", null, [new PcBuildItemInput(null, psus[0].Sku)], Kind: PcBuildKind.Cart)))).Code);
    }
}
