using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Sales;
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Provisioning;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// V6 · Tienda web conectada sobre la empresa de prueba EN MEMORIA (los mismos casos de uso que el gateway y el escritorio):
/// la instantánea sale de la base con la forma del mock de la V5; el usuario técnico existe con su rol; reservar crea el armado
/// web y las reservas de stock en la misma transacción (todo o nada); vender consume la reserva (una sola salida); liberar y
/// vencer devuelven el stock; idempotencia por llave; contacto inválido; el escritorio reserva, publica y libera sus cotizaciones.
/// </summary>
public sealed class StorefrontFlowTests : IAsyncLifetime
{
    private const string Sku = "CASE-COR-4000D";
    private ServiceProvider _services = null!;
    private SeedResult _seed = null!;

    public async Task InitializeAsync() => (_services, _seed) = await LocalDataSeederTests.SeedAsync(days: 6, tenant: "WEBT", seed: 21);

    public async Task DisposeAsync() => await _services.DisposeAsync();

    /// <summary>Sesión del administrador (tiene storefront.read, storefront.reserve y el armador) en la casa matriz.</summary>
    private async Task<IServiceScope> AdminAsync()
    {
        var scope = _services.CreateScope();
        var admin = _seed.Users.First(u => u.RoleCode == RoleCodes.Admin);
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand("WEBT", admin.Email, admin.Password, "pruebas", "6.0"));
        return scope;
    }

    private static Task<T> SendAsync<T>(IServiceScope scope, IRequest<T> request)
    {
        scope.ServiceProvider.GetRequiredService<IMinvDbContext>().ClearTracking();
        return scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    private static CreateStorefrontReservationCommand Reservation(string key, int quantity = 1, string phone = "71234567", string sku = Sku) =>
        new([new StorefrontReservationLineInput(sku, quantity, "case")], new StorefrontContactInput("Ana Quispe", phone, "ana@correo.example"), "Paso el sábado", key);

    [Fact]
    public async Task La_instantanea_sale_de_la_base_y_el_usuario_tecnico_existe_con_su_rol()
    {
        using var scope = await AdminAsync();
        var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var technical = await db.Users.SingleAsync(u => u.Email == _seed.StorefrontUser);
        var role = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == technical.Id select r.Code).SingleAsync();
        Assert.Equal(RoleCodes.Storefront, role);
        var (_, permissions) = await UserAccess.PermissionsAsync(db, technical.Id, default);
        Assert.Equal([PermissionCodes.StockView, PermissionCodes.StorefrontRead, PermissionCodes.StorefrontReserve], permissions.Order());
        Assert.DoesNotContain(_seed.Users, u => u.Email == _seed.StorefrontUser);   // no tiene contraseña de prueba

        var snapshot = await SendAsync(scope, new GetStorefrontCatalogQuery());
        Assert.Equal(("WEBT", "CM"), (snapshot.Company.Code, snapshot.Branch.Code));
        Assert.Equal(3, snapshot.Company.Branches.Count);
        Assert.InRange(snapshot.Products.Count, 150, 165);
        var product = snapshot.Products.Single(p => p.Sku == Sku);
        Assert.Equal((Sku.ToLowerInvariant(), "CASE", "Componentes > Gabinetes"), (product.Slug, product.Category, product.CategoryPath));
        Assert.Equal(product.OnHand - product.Reserved, product.Available);
        Assert.Contains(product.Specs, s => s.Key == "condicion" && s.Filterable);
        Assert.False(string.IsNullOrWhiteSpace(product.Description));   // propia del producto (la carga la trae) o generada como en la V5
        Assert.InRange(product.Popularity, 1, 10);
        Assert.Equal($"/storefront/v1/products/{Sku}/image", product.Image);
        Assert.All(snapshot.Categories.Where(c => c.Parent is null), c => Assert.NotEqual("Tag", c.Icon));
        Assert.Equal(snapshot.Products.Count, snapshot.Categories.Where(c => c.Parent is null).Sum(c => c.ProductCount));
        Assert.Equal(snapshot.Products.Select(p => p.Brand).Distinct().Count(), snapshot.Brands.Count);
        // Armados publicados por la carga (S-09): cada uno con sus piezas y precios cotizados; un armado con piezas sin precio no sale
        Assert.Equal(_seed.Tech!.PcBuildsPublished, snapshot.Presets.Count);
        Assert.True(snapshot.Presets.Count >= 5);
        Assert.All(snapshot.Presets, p => Assert.Equal(p.Total, p.Lines.Sum(l => l.Quantity * l.UnitPrice)));
        Assert.Equal(snapshot.Presets.Select(p => p.Number), (await SendAsync(scope, new GetStorefrontPresetsQuery())).Select(p => p.Number));
        // Las reservas web de la carga: una activa y una vencida (cerrada por el trabajo de vencimiento)
        var web = await SendAsync(scope, new GetPcBuildsQuery(Channel: PcBuildChannel.Web));
        Assert.Contains(web, b => b.Status == PcBuildStatus.Reserved && b.ReservedUntil > DateTimeOffset.UtcNow && b.ContactPhone is not null);
        Assert.Contains(web, b => b.Status == PcBuildStatus.Cancelled && b.CancelReason == PcBuild.ExpiredReason);
        var dashboard = await SendAsync(scope, new GetTechDashboardQuery());
        Assert.Equal(1, dashboard.WebReservationsActive);
        Assert.True(dashboard.WebReservationsValue > 0);
        // La imagen y el producto suelto
        var image = await SendAsync(scope, new GetStorefrontProductImageQuery(Sku));
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(product.Sku, (await SendAsync(scope, new GetStorefrontProductQuery(product.Slug))).Sku);
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(scope, new GetStorefrontProductQuery("no-existe")));
    }

    [Fact]
    public async Task Reservar_reserva_stock_vender_consume_y_liberar_o_vencer_devuelven()
    {
        using var scope = await AdminAsync();
        var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var clock = _services.GetRequiredService<DemoClock>();
        async Task<(decimal OnHand, decimal Reserved)> StockAsync()
        {
            db.ChangeTracker.Clear();
            var branch = await db.Branches.SingleAsync(b => b.Code == LocalDataSeeder.BranchMain);
            var rows = await (from l in db.StockLevels join b in db.Batches on l.BatchId equals b.Id join v in db.ProductVariants on b.VariantId equals v.Id
                              where v.Sku == Sku && l.BranchId == branch.Id select new { l.QuantityOnHand, l.QuantityReserved }).ToListAsync();
            return (rows.Sum(r => r.QuantityOnHand), rows.Sum(r => r.QuantityReserved));
        }
        var before = await StockAsync();

        // 1. Reservar: armado web cotizado y reservado + una reserva de stock por línea, todo en la misma transacción
        var result = await SendAsync(scope, Reservation("k1"));
        Assert.False(result.Replayed);
        var view = result.Reservation;
        Assert.StartsWith(PcBuild.WebNumberPrefix + "-", view.Number, StringComparison.Ordinal);
        Assert.Equal(("Reserved", "Reservada", "Ana Quispe", "CM"), (view.Status, view.StatusText, view.ContactName, view.Branch));
        Assert.InRange((view.ReservedUntil!.Value - clock.UtcNow).TotalHours, 47.9, 48.1);
        var build = await db.PcBuilds.AsNoTracking().Include(b => b.Lines).Include(b => b.History).SingleAsync(b => b.Number == view.Number);
        Assert.Equal((PcBuildChannel.Web, PcBuildStatus.Reserved, "71234567", "ana@correo.example"), (build.Channel, build.Status, build.ContactPhone, build.ContactEmail));
        Assert.Equal([PcBuildEventAction.Created, PcBuildEventAction.Quoted, PcBuildEventAction.Reserved], build.History.OrderBy(h => h.OccurredAt).Select(h => h.Action));
        var reservations = await db.StockReservations.AsNoTracking().Where(r => r.PcBuildLineId == build.Lines.Single().Id).ToListAsync();
        Assert.Equal(1m, Assert.Single(reservations).Quantity);
        Assert.Equal((before.OnHand, before.Reserved + 1), await StockAsync());
        Assert.Contains(await db.OutboxEvents.AsNoTracking().Select(e => e.EventType).ToListAsync(), e => e == Domain.Integration.IntegrationEvents.PcBuildReserved);
        Assert.Contains(await db.ProcessedRequests.AsNoTracking().Select(p => p.RequestType).ToListAsync(), t => t.EndsWith(nameof(CreateStorefrontReservationCommand), StringComparison.Ordinal));

        // 2. Idempotencia: misma llave → misma reserva; misma llave con otro contenido → 422
        var replayed = await SendAsync(scope, Reservation("k1"));
        Assert.True(replayed.Replayed);
        Assert.Equal(view.Number, replayed.Reservation.Number);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => SendAsync(scope, Reservation("k1", quantity: 2)));
        Assert.Equal((before.OnHand, before.Reserved + 1), await StockAsync());

        // 3. El cliente consulta con su teléfono (con o sin +591); con otro, no existe
        Assert.Equal("Reserved", (await SendAsync(scope, new GetStorefrontReservationQuery(view.Number, "+591 7123-4567"))).Status);
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(scope, new GetStorefrontReservationQuery(view.Number, "79999999")));

        // 4. Vender en caja consume la reserva: la existencia baja UNA vez y lo reservado vuelve a lo de antes
        var cashier = _seed.Users.First(u => u.RoleCode == RoleCodes.Cashier && u.Branches == LocalDataSeeder.BranchMain);
        using var pos = _services.CreateScope();
        await pos.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand("WEBT", cashier.Email, cashier.Password, "pruebas", "6.0"));
        CheckoutResult sale;
        try
        {
            sale = await SendAsync(pos, new SellPcBuildCommand(view.Number, "EFECTIVO", null, 100000m));
        }
        catch (DomainException ex) when (ex.Code == "pos.closed")
        {
            await SendAsync(pos, new OpenPosSessionCommand("CAJA03", 500));
            sale = await SendAsync(pos, new SellPcBuildCommand(view.Number, "EFECTIVO", null, 100000m));
        }
        Assert.Equal(view.Total, sale.Total);
        Assert.Equal((before.OnHand - 1, before.Reserved), await StockAsync());
        Assert.Equal(ReservationStatus.Consumed, (await db.StockReservations.AsNoTracking().SingleAsync(r => r.Id == reservations[0].Id)).Status);
        var sold = await SendAsync(scope, new GetStorefrontReservationQuery(view.Number, "71234567"));
        Assert.Equal(("Sold", "Vendida"), (sold.Status, sold.StatusText));
        Assert.Contains(await db.OutboxEvents.AsNoTracking().Select(e => e.EventType).ToListAsync(), e => e == Domain.Integration.IntegrationEvents.PcBuildSold);
        var detail = await SendAsync(scope, new GetPcBuildQuery(view.Number));
        Assert.Equal(PcBuildEventAction.Sold, detail.History!.Last().Action);
        Assert.Equal(sale.InvoiceNumber, detail.Build.InvoiceNumber);

        // 5. Otra reserva que el cliente cancela: el stock vuelve; una vencida la cierra el trabajo del sistema
        var second = (await SendAsync(scope, Reservation("k2", phone: "+59176543210"))).Reservation;
        Assert.Equal((before.OnHand - 1, before.Reserved + 1), await StockAsync());
        var cancelled = await SendAsync(scope, new CancelStorefrontReservationCommand(second.Number, "76543210"));
        Assert.Equal(("Cancelled", "Cancelada"), (cancelled.Status, cancelled.StatusText));
        Assert.Equal((before.OnHand - 1, before.Reserved), await StockAsync());
        await Assert.ThrowsAsync<DomainException>(() => SendAsync(scope, new CancelStorefrontReservationCommand(second.Number, "76543210")));
        var third = (await SendAsync(scope, Reservation("k3", phone: "70000001"))).Reservation;
        var realNow = clock.UtcNow;
        try
        {
            Assert.Equal(0, await SendAsync(scope, new ExpirePcBuildReservationsCommand()));
            clock.StartAt(realNow.AddHours(49));
            Assert.Equal("Expired", (await SendAsync(scope, new GetStorefrontReservationQuery(third.Number, "70000001"))).Status);
            // Vence la de esta prueba y, si la carga de prueba consiguió stock para ella, también la reserva web activa de la carga
            Assert.InRange(await SendAsync(scope, new ExpirePcBuildReservationsCommand()), 1, 2);
            Assert.Empty(await db.PcBuilds.AsNoTracking().Where(b => b.Status == PcBuildStatus.Reserved).ToListAsync());
            var expired = await SendAsync(scope, new GetStorefrontReservationQuery(third.Number, "70000001"));
            Assert.Equal(("Expired", "Vencida", PcBuild.ExpiredReason), (expired.Status, expired.StatusText, expired.CancelReason));
            Assert.Equal((before.OnHand - 1, before.Reserved), await StockAsync());
            Assert.Equal(ReservationStatus.Expired, (await db.StockReservations.AsNoTracking()
                .SingleAsync(r => r.PcBuildLineId == db.PcBuilds.Where(b => b.Number == third.Number).SelectMany(b => b.Lines).Select(l => l.Id).First())).Status);
        }
        finally
        {
            clock.StartAt(realNow);
        }
        // La auditoría del comando web enmascara el teléfono (regla S-06)
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "CreateStorefrontReservation").Select(a => a.Details ?? string.Empty).ToListAsync();
        Assert.NotEmpty(audits);
        Assert.DoesNotContain(audits, a => a.Contains("\"71234567\"", StringComparison.Ordinal));
        Assert.Contains(audits, a => a.Contains("*****567", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sin_stock_no_se_reserva_nada_y_el_contacto_invalido_se_rechaza()
    {
        using var scope = await AdminAsync();
        var snapshot = await SendAsync(scope, new GetStorefrontCatalogQuery());
        var scarce = snapshot.Products.Where(p => p.Available is > 0 and < 16 && p.Category == "CASE").OrderBy(p => p.Available).First();
        var requested = (int)scarce.Available + 1;
        var command = new CreateStorefrontReservationCommand(
            [new StorefrontReservationLineInput(Sku, 1, "peripherals"), new StorefrontReservationLineInput(scarce.Sku, requested, "case")],
            new StorefrontContactInput("Mateo Condori Prueba", "76543219"), null, "k-falta");
        var ex = await Assert.ThrowsAsync<StorefrontStockException>(() => SendAsync(scope, command));
        var shortage = Assert.Single(ex.Shortages);
        Assert.Equal((scarce.Sku, requested, scarce.Available), (shortage.Sku, shortage.Requested, shortage.Available));
        Assert.Equal(StorefrontStockException.ErrorCode, ex.Code);
        // Todo o nada: ni el gabinete que alcanzaba quedó reservado ni hay armado web nuevo
        var after = await SendAsync(scope, new GetStorefrontProductQuery(Sku.ToLowerInvariant()));
        Assert.Equal(snapshot.Products.Single(p => p.Sku == Sku).Reserved, after.Reserved);
        var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        Assert.False(await db.PcBuilds.AsNoTracking().AnyAsync(b => b.ContactName == "Mateo Condori Prueba"));

        Assert.Equal("pcbuild.contact_phone", (await Assert.ThrowsAsync<DomainException>(() => SendAsync(scope, Reservation("k-tel", phone: "123")))).Code);
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(scope, new CreateStorefrontReservationCommand([], new StorefrontContactInput("x", "71234567"), null, "k-vacio")));
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(scope, Reservation("k-mucho", quantity: 17)));
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(scope, Reservation("k-sku", sku: "NO-EXISTE")));
        // Sin permiso: el rol Consulta no reserva por la web
        var reader = _seed.Users.First(u => u.RoleCode == RoleCodes.ReadOnly);
        using var readOnly = _services.CreateScope();
        await readOnly.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand("WEBT", reader.Email, reader.Password, "pruebas", "6.0"));
        await Assert.ThrowsAsync<AccessDeniedException>(() => SendAsync(readOnly, Reservation("k-sin-permiso")));
        await Assert.ThrowsAsync<AccessDeniedException>(() => SendAsync(readOnly, new GetStorefrontCatalogQuery()));
    }

    [Fact]
    public async Task El_escritorio_reserva_publica_libera_y_anula_sus_cotizaciones()
    {
        using var scope = await AdminAsync();
        var quoted = (await SendAsync(scope, new GetPcBuildsQuery(PcBuildStatus.Quoted, PcBuildChannel.Desktop)))
            .Where(b => !b.IsExpired && b.BranchCode == LocalDataSeeder.BranchMain && b.IsCompatible).OrderBy(b => b.Total).ToList();
        Assert.NotEmpty(quoted);
        var target = quoted[0];
        var detail = await SendAsync(scope, new GetPcBuildQuery(target.Number));
        var reservable = detail.QuotedItems.All(i => i.Stock >= i.Quantity);
        if (!reservable)
        {
            // Sin stock para todas las piezas la reserva se rechaza completa (todo o nada) y el armado sigue cotizado
            await Assert.ThrowsAsync<StorefrontStockException>(() => SendAsync(scope, new ReservePcBuildCommand(target.Number)));
            Assert.Equal(PcBuildStatus.Quoted, (await SendAsync(scope, new GetPcBuildQuery(target.Number))).Build.Status);
            return;
        }
        var reserved = await SendAsync(scope, new ReservePcBuildCommand(target.Number, 24));
        Assert.Equal((PcBuildStatus.Reserved, PcBuildChannel.Desktop), (reserved.Status, reserved.Channel));
        Assert.Equal(detail.QuotedItems.Sum(i => i.Quantity), reserved.Reserved);
        Assert.InRange((reserved.ReservedUntil!.Value - DateTimeOffset.UtcNow).TotalHours, 23.5, 24.5);
        // Publicado (o ya lo estaba por la carga) y sigue en la instantánea; despublicar lo saca
        if (!reserved.PublishedToWeb)
        {
            await SendAsync(scope, new PublishPcBuildCommand(target.Number));
        }
        Assert.Contains((await SendAsync(scope, new GetStorefrontPresetsQuery())), p => p.Number == target.Number);
        var unpublished = await SendAsync(scope, new PublishPcBuildCommand(target.Number, false));
        Assert.False(unpublished.PublishedToWeb);
        Assert.DoesNotContain((await SendAsync(scope, new GetStorefrontPresetsQuery())), p => p.Number == target.Number);
        // Liberar devuelve el stock y deja el motivo
        var released = await SendAsync(scope, new ReleasePcBuildReservationCommand(target.Number, "El cliente no volvió"));
        Assert.Equal((PcBuildStatus.Cancelled, "El cliente no volvió", 0m), (released.Status, released.CancelReason, released.Reserved));
        var history = (await SendAsync(scope, new GetPcBuildQuery(target.Number))).History!;
        Assert.Equal(PcBuildEventAction.Released, history.Last().Action);
        Assert.Contains(history, h => h.Action == PcBuildEventAction.Reserved);
        // Anular un armado reservado también libera (segunda cotización)
        if (quoted.Count > 1)
        {
            var other = quoted[1];
            var otherDetail = await SendAsync(scope, new GetPcBuildQuery(other.Number));
            if (otherDetail.QuotedItems.All(i => i.Stock >= i.Quantity))
            {
                await SendAsync(scope, new ReservePcBuildCommand(other.Number));
                Assert.Contains("anulado", await SendAsync(scope, new CancelPcBuildCommand(other.Number, "Cambió de idea")), StringComparison.Ordinal);
                var row = (await SendAsync(scope, new GetPcBuildQuery(other.Number))).Build;
                Assert.Equal((PcBuildStatus.Cancelled, "Cambió de idea", 0m), (row.Status, row.CancelReason, row.Reserved));
            }
        }
        // Un armado web no se publica
        var web = (await SendAsync(scope, new GetPcBuildsQuery(Channel: PcBuildChannel.Web))).First();
        Assert.Equal("pcbuild.publish_channel", (await Assert.ThrowsAsync<DomainException>(() => SendAsync(scope, new PublishPcBuildCommand(web.Number)))).Code);
    }
}
