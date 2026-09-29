using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Abstractions;
using MINV.Application.Accounts;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Integration;
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Tests;

/// <summary>V7 · Empresa de prueba en memoria con el emisor falso registrado (como lo registra el servidor con PostgreSQL). Se
/// carga UNA vez por clase; cada prueba usa sus propios correos y mide respecto de lo que encuentra.</summary>
public sealed class ReservationMailFixture : IAsyncLifetime
{
    public const string Tenant = "CORREO";

    public RecordingMailSender Mailer { get; } = new();

    public ServiceProvider Services { get; private set; } = null!;

    public SeedResult Seed { get; private set; } = null!;

    public async Task InitializeAsync() => (Services, Seed) = await SeedAsync(Tenant, 31, Mailer);

    public async Task DisposeAsync() => await Services.DisposeAsync();

    internal static async Task<(ServiceProvider Services, SeedResult Seed)> SeedAsync(string tenant, int seed, IMailSender mailer)
    {
        var services = new ServiceCollection();
        services.AddMinvApplication();
        services.AddMinvDemoInfrastructure();
        services.AddSingleton(mailer);
        var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<LocalDataSeeder>().SeedAsync(new SeedOptions(tenant, Days: 3, Seed: seed), _ => { });
        return (provider, result);
    }
}

/// <summary>Utilidades compartidas por las pruebas del correo de la reserva.</summary>
internal static class MailFlow
{
    public const string Password = "Cliente-2026";

    public static async Task<IServiceScope> LoginAsync(ServiceProvider services, SeedResult seed, string tenant, string role)
    {
        var scope = services.CreateScope();
        var who = seed.Users.First(u => u.RoleCode == role);
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand(tenant, who.Email, who.Password, "pruebas", "7.0"));
        return scope;
    }

    public static Task<T> SendAsync<T>(IServiceScope scope, IRequest<T> request)
    {
        scope.ServiceProvider.GetRequiredService<IMinvDbContext>().ClearTracking();
        return scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    public static CreateStorefrontReservationCommand Cart(string key, string sku, string? email, int quantity = 1, string name = "Ana Quispe",
        string? notes = null) =>
        new([new StorefrontReservationLineInput(sku, quantity)], new StorefrontContactInput(name, "+591 71234567", email), notes, key, null, PcBuildKind.Cart);

    /// <summary>Un producto con stock de sobra en la casa matriz (las pruebas reservan varias veces).</summary>
    public static async Task<StorefrontProduct> PlentifulAsync(IServiceScope admin, int minimum = 12) =>
        (await SendAsync(admin, new GetStorefrontCatalogQuery())).Products.Where(p => p.Available >= minimum)
        .OrderByDescending(p => p.Available).ThenBy(p => p.Sku, StringComparer.Ordinal).First();

    /// <summary>Los correos de una reserva con su cola (del más viejo al más nuevo).</summary>
    public static async Task<List<(OutgoingMail Mail, OutgoingMailDispatch Dispatch)>> MailsAsync(MinvWriteDbContext db, string number)
    {
        db.ChangeTracker.Clear();
        var build = await db.PcBuilds.AsNoTracking().SingleAsync(b => b.Number == number);
        var rows = await (from m in db.OutgoingMails.AsNoTracking()
                          join d in db.OutgoingMailDispatches.AsNoTracking() on m.Id equals d.OutgoingMailId
                          where m.PcBuildId == build.Id
                          select new { m, d }).ToListAsync();
        return rows.OrderBy(r => r.m.RequestedAt).Select(r => (r.m, r.d)).ToList();
    }

    public static async Task<int> CountAsync(MinvWriteDbContext db, string? recipient = null)
    {
        db.ChangeTracker.Clear();
        return recipient is null ? await db.OutgoingMails.AsNoTracking().CountAsync()
            : await db.OutgoingMails.AsNoTracking().CountAsync(m => m.Recipient == recipient);
    }

    public static async Task<string> CodeAsync(Func<Task> action) => (await Assert.ThrowsAsync<DomainException>(action)).Code;

    /// <summary>Contexto que anota qué entidades nuevas entran en cada <c>SaveChanges</c>: prueba que el correo y su cola se
    /// guardan en el MISMO <c>SaveChanges</c> (la misma transacción) que la reserva.</summary>
    public sealed class SaveSpy(MinvWriteDbContext inner) : IMinvDbContext
    {
        public List<IReadOnlyList<string>> Saves { get; } = [];

        public BranchScope Branches => ((IMinvDbContext)inner).Branches;

        public DbSet<TEntity> Set<TEntity>() where TEntity : class => inner.Set<TEntity>();

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves.Add(inner.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).Select(e => e.Entity.GetType().Name).Distinct()
                .Order(StringComparer.Ordinal).ToList());
            return await inner.SaveChangesAsync(cancellationToken);
        }

        public void ClearTracking() => inner.ClearTracking();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => inner.BeginTransactionAsync(cancellationToken);

        public void Publish(IDomainEvent domainEvent) => inner.Publish(domainEvent);
    }

    /// <summary>Usuario sin <c>sales.pcbuild.manage</c> (para ver el destinatario enmascarado sin pasar por la tubería).</summary>
    public sealed class LimitedUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();

        public string? Email => "consulta@correo.example";

        public string? DisplayName => "Consulta";

        public IReadOnlyCollection<string> Permissions { get; } = [PermissionCodes.SalesView];

        public void SignIn(Guid userId, string email, string displayName, IReadOnlyCollection<string> permissions) => throw new NotSupportedException();

        public void SignOut() => throw new NotSupportedException();
    }
}

/// <summary>
/// V7 · Correo de confirmación de la reserva (regla P-06) sobre la empresa de prueba EN MEMORIA, con la tubería completa: todos
/// los caminos que reservan (tienda, cuenta de cliente, carrito de mostrador y reserva de una cotización) encolan el correo en el
/// MISMO SaveChanges que la reserva y ninguno envía nada; sin correo o sin stock no se encola; la repetición idempotente no
/// duplica; los topes por destinatario; el reenvío reemplaza el pendiente; la cola para el personal y lo que el despachador lee.
/// </summary>
public sealed class ReservationMailFlowTests(ReservationMailFixture fixture) : IClassFixture<ReservationMailFixture>
{
    private const string Tenant = ReservationMailFixture.Tenant;
    private readonly ServiceProvider _services = fixture.Services;
    private readonly SeedResult _seed = fixture.Seed;
    private readonly RecordingMailSender _mailer = fixture.Mailer;

    private Task<IServiceScope> LoginAsync(string role) => MailFlow.LoginAsync(_services, _seed, Tenant, role);

    // ------------------------------------------------------------------------------------------------ encolar
    [Fact]
    public async Task Reservar_encola_el_correo_en_el_mismo_SaveChanges_que_la_reserva_y_no_envia_nada()
    {
        using var admin = await LoginAsync(RoleCodes.Admin);
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var clock = _services.GetRequiredService<DemoClock>();
        var user = admin.ServiceProvider.GetRequiredService<ICurrentUser>();
        var product = await MailFlow.PlentifulAsync(admin);

        // Tienda: UN SaveChanges con la reserva, sus reservas de stock, la idempotencia, el correo y su cola
        db.ChangeTracker.Clear();
        var spy = new MailFlow.SaveSpy(db);
        var start = clock.UtcNow;
        var result = await new CreateStorefrontReservationHandler(spy, user, admin.ServiceProvider.GetRequiredService<ITenantContext>(), clock)
            .Handle(MailFlow.Cart("correo-misma-transaccion", product.Sku, "  Ana.Quispe@Correo.Example "), default);
        var save = Assert.Single(spy.Saves);
        Assert.Subset(save.ToHashSet(), new HashSet<string>
            { nameof(PcBuild), nameof(StockReservation), nameof(ProcessedRequest), nameof(OutgoingMail), nameof(OutgoingMailDispatch) });
        Assert.DoesNotContain(nameof(OutgoingMailAttempt), save);
        Assert.True(result.Reservation.MailQueued);

        // En la base: el hecho (sucursal, reserva, destinatario en minúsculas, quién y cuándo) y la cola pendiente sin intentos
        var (mail, dispatch) = Assert.Single(await MailFlow.MailsAsync(db, result.Reservation.Number));
        var build = await db.PcBuilds.AsNoTracking().SingleAsync(b => b.Number == result.Reservation.Number);
        Assert.Equal((build.Id, build.BranchId, OutgoingMailKind.ReservationConfirmed, "ana.quispe@correo.example", user.UserId!.Value),
            (mail.PcBuildId, mail.BranchId, mail.Kind, mail.Recipient, mail.RequestedByUserId));
        Assert.InRange(mail.RequestedAt, start, clock.UtcNow);
        Assert.Equal((OutgoingMailStatus.Pending, 0, mail.RequestedAt, (DateTimeOffset?)null, (string?)null),
            (dispatch.Status, dispatch.Attempts, dispatch.NextAttemptAt, dispatch.CompletedAt, dispatch.LastError));
        Assert.Equal(0, await db.OutgoingMailAttempts.AsNoTracking().CountAsync(a => a.OutgoingMailId == mail.Id));

        // Carrito de mostrador: lo mismo, en UN SaveChanges
        db.ChangeTracker.Clear();
        spy = new MailFlow.SaveSpy(db);
        var counter = await new ReserveCartHandler(spy, user, clock)
            .Handle(new ReserveCartCommand([new CartItemInput(product.Sku)], "Luis Rojas", "76543210", "luis.rojas@correo.example"), default);
        Assert.Subset(Assert.Single(spy.Saves).ToHashSet(), new HashSet<string> { nameof(PcBuild), nameof(StockReservation), nameof(OutgoingMail), nameof(OutgoingMailDispatch) });
        Assert.Equal("luis.rojas@correo.example", Assert.Single(await MailFlow.MailsAsync(db, counter.Number)).Mail.Recipient);

        // Con la tubería completa (validación, permisos y auditoría) tampoco se envía nada: el emisor sigue vacío
        var piped = await MailFlow.SendAsync(admin, new ReserveCartCommand([new CartItemInput(product.Sku)], "Rosa Mamani", "70011223", "rosa@correo.example"));
        Assert.Single(await MailFlow.MailsAsync(db, piped.Number));
        Assert.Empty(_mailer.Sent);
    }

    [Fact]
    public async Task Sin_correo_o_sin_stock_no_se_encola_nada_y_la_reserva_de_una_cotizacion_encola_si_tiene_correo()
    {
        using var admin = await LoginAsync(RoleCodes.Admin);
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var product = await MailFlow.PlentifulAsync(admin);
        var before = await MailFlow.CountAsync(db);

        // Sin correo de contacto: la reserva sale igual y la respuesta dice que no se encoló
        var noMail = (await MailFlow.SendAsync(admin, MailFlow.Cart("correo-sin-correo", product.Sku, null))).Reservation;
        Assert.Equal(("Reserved", false), (noMail.Status, noMail.MailQueued));
        Assert.Empty(await MailFlow.MailsAsync(db, noMail.Number));
        var counter = await MailFlow.SendAsync(admin, new ReserveCartCommand([new CartItemInput(product.Sku)], "Luis Rojas", "76543210"));
        Assert.Empty(await MailFlow.MailsAsync(db, counter.Number));

        // Un correo que el dominio acepta pero al que no se puede enviar (armaría una lista): la reserva sale, sin correo
        var list = (await MailFlow.SendAsync(admin, MailFlow.Cart("correo-lista", product.Sku, "ana,otro@correo.example"))).Reservation;
        Assert.Equal(("Reserved", false), (list.Status, list.MailQueued));
        Assert.Empty(await MailFlow.MailsAsync(db, list.Number));

        // Sin stock suficiente no se reserva nada y tampoco queda ningún correo (todo o nada)
        var scarce = (await MailFlow.SendAsync(admin, new GetStorefrontCatalogQuery())).Products
            .Where(p => p.Available >= 1 && p.Available < PcBuild.MaxQuantity).OrderBy(p => p.Sku, StringComparer.Ordinal).First();
        var tooMany = (int)scarce.Available + 1;
        await Assert.ThrowsAsync<StorefrontStockException>(() => MailFlow.SendAsync(admin,
            MailFlow.Cart("correo-sin-stock", scarce.Sku, "sin.stock@correo.example", tooMany)));
        await Assert.ThrowsAsync<StorefrontStockException>(() => MailFlow.SendAsync(admin,
            new ReserveCartCommand([new CartItemInput(scarce.Sku, tooMany)], "Luis Rojas", "76543210", "sin.stock@correo.example")));
        // …ni con datos que la validación o el dominio rechazan
        await Assert.ThrowsAsync<RequestValidationException>(() => MailFlow.SendAsync(admin,
            MailFlow.Cart("correo-nombre", product.Sku, "sin.stock@correo.example", name: "Ana\r\nBcc: x@correo.example")));
        await Assert.ThrowsAsync<DomainException>(() => MailFlow.SendAsync(admin, MailFlow.Cart("correo-invalido", product.Sku, "Ana <sin.stock@correo.example>")));
        Assert.Equal(0, await MailFlow.CountAsync(db, "sin.stock@correo.example"));
        Assert.Equal(before, await MailFlow.CountAsync(db));

        // La reserva de una cotización del escritorio: sin correo de contacto no encola; con correo, sí (misma transacción)
        var quoted = await MailFlow.SendAsync(admin, new SavePcBuildCommand(null, "Carrito de mostrador", null, [new PcBuildItemInput(null, product.Sku)], Quote: true,
            Kind: PcBuildKind.Cart));
        var reserved = await MailFlow.SendAsync(admin, new ReservePcBuildCommand(quoted.Number, 24));
        Assert.Equal(PcBuildStatus.Reserved, reserved.Status);
        Assert.Empty(await MailFlow.MailsAsync(db, quoted.Number));
        var withContact = await MailFlow.SendAsync(admin, new SavePcBuildCommand(null, "Carrito de Luis", null, [new PcBuildItemInput(null, product.Sku)], Quote: true,
            Kind: PcBuildKind.Cart));
        db.ChangeTracker.Clear();
        var quote = await db.PcBuilds.Include(b => b.Lines).SingleAsync(b => b.Number == withContact.Number);
        quote.SetContact("Luis Rojas", "76543210", "Luis.Cotizacion@Correo.Example", null);
        await db.SaveChangesAsync();
        await MailFlow.SendAsync(admin, new ReservePcBuildCommand(withContact.Number, 24));
        var (mail, dispatch) = Assert.Single(await MailFlow.MailsAsync(db, withContact.Number));
        Assert.Equal(("luis.cotizacion@correo.example", OutgoingMailStatus.Pending), (mail.Recipient, dispatch.Status));
        Assert.Empty(_mailer.Sent);
    }

    [Fact]
    public async Task La_repeticion_idempotente_no_encola_otro_correo()
    {
        using var admin = await LoginAsync(RoleCodes.Admin);
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var product = await MailFlow.PlentifulAsync(admin);
        var command = MailFlow.Cart("correo-idempotente", product.Sku, "repetida@correo.example", 2);

        var first = await MailFlow.SendAsync(admin, command);
        var again = await MailFlow.SendAsync(admin, command);
        Assert.Equal((false, true, true, true), (first.Replayed, again.Replayed, first.Reservation.MailQueued, again.Reservation.MailQueued));
        Assert.Equal(first.Reservation.Number, again.Reservation.Number);
        Assert.Single(await MailFlow.MailsAsync(db, first.Reservation.Number));
        Assert.Equal(1, await MailFlow.CountAsync(db, "repetida@correo.example"));
    }

    [Fact]
    public async Task La_reserva_de_una_cuenta_de_cliente_encola_la_confirmacion_a_su_correo()
    {
        using var admin = await LoginAsync(RoleCodes.Admin);
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var product = await MailFlow.PlentifulAsync(admin);
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetBranches(new BranchScope(false, [Guid.Empty], null));
        scope.ServiceProvider.GetRequiredService<IRequestOrigin>().Set(RequestChannels.Web, null);
        var account = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new RegisterCustomerAccountCommand(Tenant, "Camila Flores", "Camila.Flores@Correo.Example", "70012345", MailFlow.Password, null, "web", "7.0.0"));

        var mine = await MailFlow.SendAsync(scope, new CreateMyReservationCommand([new StorefrontReservationLineInput(product.Sku)], HoldDays: 1));
        Assert.Equal(("Reserved", true), (mine.Status, mine.MailQueued));
        var (mail, dispatch) = Assert.Single(await MailFlow.MailsAsync(db, mine.Number));
        Assert.Equal(("camila.flores@correo.example", account.Login.UserId, OutgoingMailStatus.Pending), (mail.Recipient, mail.RequestedByUserId, dispatch.Status));
        // La cuenta de cliente no ve la cola ni reenvía (no tiene sales.pcbuild.manage)
        await Assert.ThrowsAsync<AccessDeniedException>(() => MailFlow.SendAsync(scope, new GetOutgoingMailsQuery()));
        await Assert.ThrowsAsync<AccessDeniedException>(() => MailFlow.SendAsync(scope, new ResendReservationMailCommand(mine.Number)));
        Assert.Empty(_mailer.Sent);
    }

    // ------------------------------------------------------------------------------------------------ topes
    [Fact]
    public async Task Un_destinatario_recibe_como_maximo_3_correos_cada_24_horas()
    {
        using var admin = await LoginAsync(RoleCodes.Admin);
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var clock = _services.GetRequiredService<DemoClock>();
        var product = await MailFlow.PlentifulAsync(admin);
        const string recipient = "tope@correo.example";

        // Tres reservas (con otras mayúsculas también cuenta como el mismo destinatario): tres correos
        var queued = new List<bool>();
        foreach (var (key, email) in new[] { ("tope-1", recipient), ("tope-2", "TOPE@Correo.Example"), ("tope-3", " tope@correo.example ") })
        {
            queued.Add((await MailFlow.SendAsync(admin, MailFlow.Cart(key, product.Sku, email))).Reservation.MailQueued);
        }
        Assert.Equal([true, true, true], queued);
        // La cuarta reserva sale igual, pero sin correo
        var fourth = (await MailFlow.SendAsync(admin, MailFlow.Cart("tope-4", product.Sku, recipient))).Reservation;
        Assert.Equal(("Reserved", false), (fourth.Status, fourth.MailQueued));
        Assert.Empty(await MailFlow.MailsAsync(db, fourth.Number));
        Assert.Equal(ReservationMail.MaxPerRecipient, await MailFlow.CountAsync(db, recipient));
        // El reenvío tampoco pasa el tope (a esa dirección); a otra dirección, sí
        Assert.Equal("mail.recipient_limit", await MailFlow.CodeAsync(() => MailFlow.SendAsync(admin, new ResendReservationMailCommand(fourth.Number))));
        Assert.Equal("otro.tope@correo.example",
            (await MailFlow.SendAsync(admin, new ResendReservationMailCommand(fourth.Number, "otro.tope@correo.example"))).Recipient);

        // Solo cuentan las últimas 24 horas: tres correos de hace 25 horas no impiden uno nuevo
        const string old = "viejo@correo.example";
        db.ChangeTracker.Clear();
        var build = await db.PcBuilds.AsNoTracking().SingleAsync(b => b.Number == fourth.Number);
        var userId = admin.ServiceProvider.GetRequiredService<ICurrentUser>().UserId!.Value;
        var longAgo = clock.UtcNow.AddHours(-25);
        for (var i = 0; i < ReservationMail.MaxPerRecipient; i++)
        {
            var mail = new OutgoingMail(build.TenantId, build.BranchId, OutgoingMailKind.ReservationConfirmed, build.Id, old, userId, longAgo.AddMinutes(i));
            db.OutgoingMails.Add(mail);
            db.OutgoingMailDispatches.Add(new OutgoingMailDispatch(build.TenantId, mail.Id, mail.RequestedAt));
        }
        await db.SaveChangesAsync();
        Assert.True((await MailFlow.SendAsync(admin, MailFlow.Cart("tope-viejo", product.Sku, old))).Reservation.MailQueued);
        Assert.Empty(_mailer.Sent);
    }

    // ------------------------------------------------------------------------------------------------ reenvío
    [Fact]
    public async Task Reenviar_encola_otro_correo_y_cancela_el_pendiente_que_reemplaza()
    {
        using var admin = await LoginAsync(RoleCodes.Admin);
        using var readOnly = await LoginAsync(RoleCodes.ReadOnly);
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var product = await MailFlow.PlentifulAsync(admin);
        var reservation = (await MailFlow.SendAsync(admin, MailFlow.Cart("reenvio-1", product.Sku, "reenvio@correo.example"))).Reservation;
        var (first, _) = Assert.Single(await MailFlow.MailsAsync(db, reservation.Number));

        // Al mismo correo de contacto (el número no distingue mayúsculas): el pendiente anterior queda cancelado
        var row = await MailFlow.SendAsync(admin, new ResendReservationMailCommand(" " + reservation.Number.ToLowerInvariant() + " "));
        Assert.Equal((reservation.Number, PcBuildKind.Cart, LocalDataSeeder.BranchMain, OutgoingMailKind.ReservationConfirmed, "Confirmación de reserva", "reenvio@correo.example"),
            (row.Reservation, row.ReservationKind, row.BranchCode, row.Kind, row.KindText, row.Recipient));
        Assert.Equal((OutgoingMailStatus.Pending, "Pendiente", 0, OutgoingMailAttempt.MaxAttempts, (DateTimeOffset?)null, (DateTimeOffset?)null),
            (row.Status, row.StatusText, row.Attempts, row.MaxAttempts, row.LastAttemptAt, row.CompletedAt));
        Assert.Equal(row.RequestedAt, row.NextAttemptAt);
        var mails = await MailFlow.MailsAsync(db, reservation.Number);
        Assert.Equal(2, mails.Count);
        var replaced = mails.Single(m => m.Mail.Id == first.Id).Dispatch;
        Assert.Equal((OutgoingMailStatus.Cancelled, ReservationMail.ReplacedReason, 0), (replaced.Status, replaced.LastError, replaced.Attempts));
        Assert.NotNull(replaced.CompletedAt);

        // A otra dirección: el correo nuevo va allí y el anterior también se reemplaza
        var other = await MailFlow.SendAsync(admin, new ResendReservationMailCommand(reservation.Number, " Otra.Direccion@Correo.Example "));
        Assert.Equal("otra.direccion@correo.example", other.Recipient);
        mails = await MailFlow.MailsAsync(db, reservation.Number);
        Assert.Equal([OutgoingMailStatus.Cancelled, OutgoingMailStatus.Cancelled, OutgoingMailStatus.Pending], mails.Select(m => m.Dispatch.Status));
        // La auditoría del reenvío no guarda el correo completo (ni en el pedido ni en la respuesta)
        db.ChangeTracker.Clear();
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "ResendReservationMail").ToListAsync();
        Assert.Contains(audits, a => a.Outcome == AuditOutcome.Succeeded);
        Assert.DoesNotContain(audits, a => (a.Details ?? string.Empty).Contains("otra.direccion@correo.example", StringComparison.OrdinalIgnoreCase)
                                           || (a.Details ?? string.Empty).Contains("reenvio@correo.example", StringComparison.OrdinalIgnoreCase));

        // Rechazos: sin permiso, reserva inexistente, correo inválido, reserva sin correo, reserva que ya no está vigente
        await Assert.ThrowsAsync<AccessDeniedException>(() => MailFlow.SendAsync(readOnly, new ResendReservationMailCommand(reservation.Number)));
        await Assert.ThrowsAsync<AccessDeniedException>(() => MailFlow.SendAsync(readOnly, new GetOutgoingMailsQuery()));
        await Assert.ThrowsAsync<NotFoundException>(() => MailFlow.SendAsync(admin, new ResendReservationMailCommand("RES-WEB-999999")));
        await Assert.ThrowsAsync<RequestValidationException>(() => MailFlow.SendAsync(admin,
            new ResendReservationMailCommand(reservation.Number, "a@correo.example, b@correo.example")));
        var silent = (await MailFlow.SendAsync(admin, MailFlow.Cart("reenvio-sin-correo", product.Sku, null))).Reservation;
        Assert.Equal("mail.no_recipient", await MailFlow.CodeAsync(() => MailFlow.SendAsync(admin, new ResendReservationMailCommand(silent.Number))));
        Assert.Equal("dado@correo.example", (await MailFlow.SendAsync(admin, new ResendReservationMailCommand(silent.Number, "dado@correo.example"))).Recipient);
        await MailFlow.SendAsync(admin, new ReleasePcBuildReservationCommand(reservation.Number, "El cliente ya no la quiere"));
        Assert.Equal("pcbuild.state", await MailFlow.CodeAsync(() => MailFlow.SendAsync(admin, new ResendReservationMailCommand(reservation.Number))));
        Assert.Equal(3, (await MailFlow.MailsAsync(db, reservation.Number)).Count);
        Assert.Empty(_mailer.Sent);
    }

    // ------------------------------------------------------------------------------------------------ cola y despachador
    [Fact]
    public async Task La_cola_muestra_estado_intentos_y_ultimo_error_y_el_despachador_arma_el_correo_o_lo_cancela()
    {
        using var admin = await LoginAsync(RoleCodes.Admin);
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var clock = _services.GetRequiredService<DemoClock>();
        var product = await MailFlow.PlentifulAsync(admin);
        // Un nombre con etiquetas y comillas (sin saltos de línea: esos los rechaza la validación) y notas con el teléfono completo
        var reservation = (await MailFlow.SendAsync(admin, MailFlow.Cart("cola-1", product.Sku, "cola@correo.example", 2,
            name: "Ana \"<script>alert(1)</script>\"", notes: "Llamar al 71234567 antes de las 18"))).Reservation;
        var (mail, _) = Assert.Single(await MailFlow.MailsAsync(db, reservation.Number));

        // Lo que leerá el despachador: el correo armado con la reserva (el asunto y el cuerpo NO se guardan en la base)
        var draft = await ReservationMail.DraftAsync(db, mail, clock.UtcNow, "https://tienda.example", default);
        Assert.True(draft.ShouldSend);
        var content = draft.Content!;
        Assert.Equal($"Reserva {reservation.Number} · {_seed.CompanyName}", content.Subject);
        foreach (var (body, encode) in new (string, Func<string, string>)[] { (content.HtmlBody, WebUtility.HtmlEncode), (content.TextBody, s => s) })
        {
            Assert.Contains(encode(reservation.Number), body, StringComparison.Ordinal);
            Assert.Contains(encode(product.Sku), body, StringComparison.Ordinal);
            Assert.Contains(encode(ReservationMailTemplate.Money(reservation.Total)), body, StringComparison.Ordinal);
            Assert.Contains(encode(ReservationMailTemplate.Until(reservation.ReservedUntil!.Value)), body, StringComparison.Ordinal);
            Assert.Contains($"https://tienda.example/reserva/{reservation.Number}", body, StringComparison.Ordinal);
            // Ni el nombre crudo, ni las notas, ni el teléfono completo, ni el correo del cliente
            Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Llamar al", body, StringComparison.Ordinal);
            Assert.DoesNotContain("71234567", body, StringComparison.Ordinal);
            Assert.DoesNotContain("cola@correo.example", body, StringComparison.Ordinal);
        }
        Assert.Contains("Hola:", content.TextBody, StringComparison.Ordinal);
        // Una reserva vencida ya no se confirma
        Assert.Equal(ReservationMail.ExpiredReason, (await ReservationMail.DraftAsync(db, mail, reservation.ReservedUntil!.Value.AddMinutes(1), null, default)).CancelReason);

        // Una pasada del despachador que falla: intento en la bitácora + cola reprogramada (1 minuto después)
        var now = clock.UtcNow;
        db.ChangeTracker.Clear();
        var dispatch = await db.OutgoingMailDispatches.SingleAsync(d => d.OutgoingMailId == mail.Id);
        dispatch.RecordFailure("421 4.7.0 Servicio no disponible, intente luego", now);
        db.OutgoingMailAttempts.Add(new OutgoingMailAttempt(mail.TenantId, mail.Id, 1, false, "421 4.7.0 Servicio no disponible, intente luego", now, 1200));
        await db.SaveChangesAsync();
        var row = Assert.Single(await MailFlow.SendAsync(admin, new GetOutgoingMailsQuery(Number: reservation.Number.ToLowerInvariant())));
        Assert.Equal((OutgoingMailStatus.Pending, "Pendiente", 1, "421 4.7.0 Servicio no disponible, intente luego", now, now.AddMinutes(1), "cola@correo.example"),
            (row.Status, row.StatusText, row.Attempts, row.LastError, row.LastAttemptAt!.Value, row.NextAttemptAt!.Value, row.Recipient));
        // El segundo intento sale bien: enviado, sin error ni próximo intento
        db.ChangeTracker.Clear();
        dispatch = await db.OutgoingMailDispatches.SingleAsync(d => d.OutgoingMailId == mail.Id);
        dispatch.MarkSent(now.AddMinutes(1));
        db.OutgoingMailAttempts.Add(new OutgoingMailAttempt(mail.TenantId, mail.Id, 2, true, null, now.AddMinutes(1), 900));
        await db.SaveChangesAsync();
        row = Assert.Single(await MailFlow.SendAsync(admin, new GetOutgoingMailsQuery(OutgoingMailStatus.Sent, reservation.Number)));
        Assert.Equal((OutgoingMailStatus.Sent, "Enviado", 2, (string?)null, now.AddMinutes(1), (DateTimeOffset?)null, now.AddMinutes(1)),
            (row.Status, row.StatusText, row.Attempts, row.LastError, row.LastAttemptAt!.Value, row.NextAttemptAt, row.CompletedAt!.Value));
        Assert.Empty(await MailFlow.SendAsync(admin, new GetOutgoingMailsQuery(OutgoingMailStatus.Pending, reservation.Number)));
        // La bitácora de intentos es append-only: un intento no se corrige
        db.ChangeTracker.Clear();
        var attempt = await db.OutgoingMailAttempts.FirstAsync(a => a.OutgoingMailId == mail.Id);
        db.Entry(attempt).State = EntityState.Modified;
        await Assert.ThrowsAsync<AppendOnlyViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        // La reserva se libera antes de enviar su correo: el despachador lo cancela (no gasta intentos)
        var released = (await MailFlow.SendAsync(admin, MailFlow.Cart("cola-2", product.Sku, "cola.liberada@correo.example"))).Reservation;
        var (pending, _) = Assert.Single(await MailFlow.MailsAsync(db, released.Number));
        await MailFlow.SendAsync(admin, new ReleasePcBuildReservationCommand(released.Number, "Se arrepintió"));
        var cancelled = await ReservationMail.DraftAsync(db, pending, clock.UtcNow, null, default);
        Assert.Equal((false, ReservationMail.ClosedReason), (cancelled.ShouldSend, cancelled.CancelReason));
        db.ChangeTracker.Clear();
        (await db.OutgoingMailDispatches.SingleAsync(d => d.OutgoingMailId == pending.Id)).Cancel(cancelled.CancelReason!, clock.UtcNow);
        await db.SaveChangesAsync();
        row = Assert.Single(await MailFlow.SendAsync(admin, new GetOutgoingMailsQuery(OutgoingMailStatus.Cancelled, released.Number)));
        Assert.Equal(("Cancelado", 0, ReservationMail.ClosedReason), (row.StatusText, row.Attempts, row.LastError));

        // Sin sales.pcbuild.manage el destinatario sale enmascarado (defensa en profundidad de la regla S-06)
        db.ChangeTracker.Clear();
        var masked = await new GetOutgoingMailsHandler(db, new MailFlow.LimitedUser()).Handle(new GetOutgoingMailsQuery(Number: reservation.Number), default);
        Assert.Equal("c***@correo.example", Assert.Single(masked).Recipient);
        // La cola completa trae lo más nuevo primero y respeta el máximo pedido
        var all = await MailFlow.SendAsync(admin, new GetOutgoingMailsQuery(Take: 500));
        Assert.Equal(all.OrderByDescending(r => r.RequestedAt).Select(r => r.Id), all.Select(r => r.Id));
        Assert.Equal(2, (await MailFlow.SendAsync(admin, new GetOutgoingMailsQuery(Take: 2))).Count);
        Assert.Empty(_mailer.Sent);
    }
}

/// <summary>V7 · Tope por empresa (300 correos cada 24 horas) en una empresa propia: llenarlo afectaría a las demás pruebas.</summary>
public sealed class ReservationMailCompanyLimitTests : IAsyncLifetime
{
    private const string Tenant = "CORREOTOPE";
    private readonly RecordingMailSender _mailer = new();
    private ServiceProvider _services = null!;
    private SeedResult _seed = null!;

    public async Task InitializeAsync() => (_services, _seed) = await ReservationMailFixture.SeedAsync(Tenant, 37, _mailer);

    public async Task DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task Al_llegar_a_300_correos_en_24_horas_la_reserva_sale_sin_correo_y_el_reenvio_se_rechaza()
    {
        using var admin = await MailFlow.LoginAsync(_services, _seed, Tenant, RoleCodes.Admin);
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var clock = _services.GetRequiredService<DemoClock>();
        var product = await MailFlow.PlentifulAsync(admin);
        var first = (await MailFlow.SendAsync(admin, MailFlow.Cart("empresa-1", product.Sku, "primera@correo.example"))).Reservation;
        Assert.True(first.MailQueued);

        // Se completan los 300 del día con correos de otras reservas (como si la tienda hubiera tenido un día muy movido)
        db.ChangeTracker.Clear();
        var build = await db.PcBuilds.AsNoTracking().SingleAsync(b => b.Number == first.Number);
        var userId = admin.ServiceProvider.GetRequiredService<ICurrentUser>().UserId!.Value;
        var now = clock.UtcNow;
        var inWindow = await db.OutgoingMails.AsNoTracking().CountAsync(m => m.RequestedAt > now - ReservationMail.Window);
        for (var i = inWindow; i < ReservationMail.MaxPerCompany; i++)
        {
            var mail = new OutgoingMail(build.TenantId, build.BranchId, OutgoingMailKind.ReservationConfirmed, build.Id, $"cliente{i}@correo.example", userId,
                now.AddSeconds(-i));
            db.OutgoingMails.Add(mail);
            db.OutgoingMailDispatches.Add(new OutgoingMailDispatch(build.TenantId, mail.Id, mail.RequestedAt));
        }
        await db.SaveChangesAsync();

        var next = (await MailFlow.SendAsync(admin, MailFlow.Cart("empresa-2", product.Sku, "nueva@correo.example"))).Reservation;
        Assert.Equal(("Reserved", false), (next.Status, next.MailQueued));
        Assert.Empty(await MailFlow.MailsAsync(db, next.Number));
        Assert.Equal("mail.company_limit", await MailFlow.CodeAsync(() => MailFlow.SendAsync(admin, new ResendReservationMailCommand(next.Number))));
        Assert.Equal(ReservationMail.MaxPerCompany, await db.OutgoingMails.AsNoTracking().CountAsync(m => m.RequestedAt > now - ReservationMail.Window));
        Assert.Empty(_mailer.Sent);
    }
}
