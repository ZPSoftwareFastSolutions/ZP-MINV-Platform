using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Integration;
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Infrastructure.Integration;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Tests;

/// <summary>V7 · Empresa propia para el despachador: cada pasada procesa TODA la cola vencida, así que no comparte la base con las
/// pruebas del encolado.</summary>
public sealed class MailDispatcherFixture : IAsyncLifetime
{
    public const string Tenant = "DESPACHO";

    public RecordingMailSender Mailer { get; } = new();

    public ServiceProvider Services { get; private set; } = null!;

    public SeedResult Seed { get; private set; } = null!;

    public async Task InitializeAsync() => (Services, Seed) = await ReservationMailFixture.SeedAsync(Tenant, 41, Mailer);

    public async Task DisposeAsync() => await Services.DisposeAsync();
}

/// <summary>Reloj del despachador corrido respecto del de la empresa (para que «le toque» a un correo reprogramado).</summary>
internal sealed class ShiftedClock(IClock inner) : IClock
{
    public TimeSpan Shift { get; set; }

    public DateTimeOffset UtcNow => inner.UtcNow + Shift;

    public DateOnly TodayIn(string timeZoneId) => SystemClock.DateIn(UtcNow, timeZoneId);
}

/// <summary>
/// V7 · Despachador de la cola de correos (<see cref="MailDispatcher"/>, regla P-06) sobre la empresa de prueba EN MEMORIA con el
/// emisor falso: la pasada envía y deja el correo enviado con su intento; una falla pasajera registra el intento y reprograma con
/// la espera; un rechazo definitivo lo agota; la reserva liberada cancela la cola sin enviar; sin servidor de correo queda
/// pendiente sin gastar intentos (y el servidor de la empresa manda sobre el del servidor); una falla del servidor de correo corta
/// la pasada sin quemar intentos; lo que se guarda como error nunca lleva credenciales ni datos del servidor.
/// </summary>
public sealed class MailDispatcherTests(MailDispatcherFixture fixture) : IClassFixture<MailDispatcherFixture>
{
    private const string Tenant = MailDispatcherFixture.Tenant;
    private readonly ServiceProvider _services = fixture.Services;
    private readonly SeedResult _seed = fixture.Seed;
    private readonly RecordingMailSender _mailer = fixture.Mailer;

    private Task<IServiceScope> LoginAsync() => MailFlow.LoginAsync(_services, _seed, Tenant, RoleCodes.Admin);

    private static MailOptions Options() => new()
    {
        Host = "smtp.despacho.example",
        Port = 2525,
        UseSsl = false,
        UserName = "envios@despacho.example",
        Password = "clave-secreta-123",
        FromAddress = "envios@despacho.example",
        FromName = "Tienda de prueba",
        PublicUrl = "https://tienda.example",
    };

    private MailDispatcher Dispatcher(MailOptions options, IClock? clock = null) =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), options, clock ?? _services.GetRequiredService<DemoClock>());

    private static async Task<StorefrontReservationView> ReserveAsync(IServiceScope admin, string key, string email)
    {
        var product = await MailFlow.PlentifulAsync(admin);
        var reservation = (await MailFlow.SendAsync(admin, MailFlow.Cart(key, product.Sku, email))).Reservation;
        Assert.True(reservation.MailQueued);
        return reservation;
    }

    private static async Task<(OutgoingMail Mail, OutgoingMailDispatch Dispatch, List<OutgoingMailAttempt> Attempts)> QueueAsync(MinvWriteDbContext db,
        string number)
    {
        var (mail, dispatch) = Assert.Single(await MailFlow.MailsAsync(db, number));
        var attempts = await db.OutgoingMailAttempts.AsNoTracking().Where(a => a.OutgoingMailId == mail.Id).OrderBy(a => a.Attempt).ToListAsync();
        return (mail, dispatch, attempts);
    }

    private static void AssertNoSecrets(MailOptions options, string? text)
    {
        Assert.False(string.IsNullOrEmpty(text));
        foreach (var secret in new[] { options.Password!, options.UserName!, options.Host! })
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Una_pasada_envia_el_correo_lo_deja_enviado_y_registra_su_intento()
    {
        using var admin = await LoginAsync();
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        const string to = "envio@correo.example";
        var reservation = await ReserveAsync(admin, "despacho-envio", to);
        Assert.Equal(0, _mailer.Attempted(to));   // reservar no envía nada (regla B-08)

        var options = Options();
        var summary = await Dispatcher(options).RunOnceAsync(50, default);
        Assert.True(summary.Mails >= 1 && summary.Sent >= 1);
        Assert.False(summary.Halted);

        // UN mensaje: al destinatario, con el asunto del servidor, texto plano, HTML con el enlace y el Message-ID del correo
        var message = Assert.Single(_mailer.SentTo(to));
        var (mail, dispatch, attempts) = await QueueAsync(db, reservation.Number);
        Assert.Equal((to, $"Reserva {reservation.Number} · {_seed.CompanyName}", mail.MessageId(options.FromAddress)),
            (message.To, message.Subject, message.MessageId));
        Assert.Contains(reservation.Number, message.TextBody, StringComparison.Ordinal);
        Assert.Contains($"https://tienda.example/reserva/{reservation.Number}", message.HtmlBody, StringComparison.Ordinal);
        Assert.Empty(message.Attachments);
        Assert.Equal(("smtp.despacho.example", 2525, false, "envios@despacho.example", "Tienda de prueba", "clave-secreta-123"),
            (message.Server.Host, message.Server.Port, message.Server.UseSsl, message.Server.FromAddress, message.Server.FromName, message.Server.Password));

        // La cola: enviado al primer intento, sin error; la bitácora: un intento logrado
        Assert.Equal((OutgoingMailStatus.Sent, 1, (string?)null), (dispatch.Status, dispatch.Attempts, dispatch.LastError));
        Assert.NotNull(dispatch.CompletedAt);
        var attempt = Assert.Single(attempts);
        Assert.Equal((1, true, (string?)null), (attempt.Attempt, attempt.Succeeded, attempt.Error));
        Assert.Equal(dispatch.CompletedAt, attempt.AttemptedAt);
        Assert.True(attempt.DurationMs >= 0);

        // Otra pasada no lo vuelve a enviar
        await Dispatcher(options).RunOnceAsync(50, default);
        Assert.Equal(1, _mailer.Attempted(to));
    }

    [Fact]
    public async Task Una_falla_pasajera_registra_el_intento_y_reprograma_con_la_espera_sin_credenciales_en_el_error()
    {
        using var admin = await LoginAsync();
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        const string to = "pasajera@correo.example";
        var reservation = await ReserveAsync(admin, "despacho-pasajera", to);
        var options = Options();
        var clock = new ShiftedClock(_services.GetRequiredService<DemoClock>());
        // El emisor repite en su error la contraseña, el usuario y el servidor: nada de eso puede quedar guardado
        _mailer.FailFor(to, new MailDeliveryException(MailFailureKind.Transient,
            $"451 4.3.0 Servidor ocupado ({options.Password} / {options.UserName} en {options.Host!.ToUpperInvariant()})"));

        var summary = await Dispatcher(options, clock).RunOnceAsync(50, default);
        Assert.True(summary.Failed >= 1);
        Assert.False(summary.Halted);
        Assert.Empty(_mailer.SentTo(to));
        var (_, dispatch, attempts) = await QueueAsync(db, reservation.Number);
        var failed = Assert.Single(attempts);
        Assert.Equal((OutgoingMailStatus.Pending, 1, (DateTimeOffset?)null), (dispatch.Status, dispatch.Attempts, dispatch.CompletedAt));
        Assert.Equal((1, false), (failed.Attempt, failed.Succeeded));
        // Reprogramado con la espera del segundo intento (1 minuto) desde el intento fallido
        Assert.Equal(failed.AttemptedAt + OutgoingMailAttempt.BackoffBefore(2), dispatch.NextAttemptAt);
        Assert.Equal(failed.Error, dispatch.LastError);
        Assert.Contains("451", failed.Error, StringComparison.Ordinal);
        AssertNoSecrets(options, failed.Error);

        // Todavía no le toca: la pasada siguiente no lo intenta
        await Dispatcher(options, clock).RunOnceAsync(50, default);
        Assert.Equal(1, _mailer.Attempted(to));

        // Pasado el minuto, sale en el segundo intento: la bitácora conserva los dos
        clock.Shift = OutgoingMailAttempt.BackoffBefore(2) + TimeSpan.FromSeconds(5);
        await Dispatcher(options, clock).RunOnceAsync(50, default);
        Assert.Single(_mailer.SentTo(to));
        (_, dispatch, attempts) = await QueueAsync(db, reservation.Number);
        Assert.Equal((OutgoingMailStatus.Sent, 2, (string?)null), (dispatch.Status, dispatch.Attempts, dispatch.LastError));
        Assert.Equal([(1, false), (2, true)], attempts.Select(a => (a.Attempt, a.Succeeded)));
    }

    [Fact]
    public async Task Un_rechazo_definitivo_lo_agota_al_primer_intento_y_un_error_inesperado_se_guarda_sin_su_detalle()
    {
        using var admin = await LoginAsync();
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var options = Options();
        const string gone = "no.existe@correo.example";
        var rejected = await ReserveAsync(admin, "despacho-definitivo", gone);
        _mailer.FailFor(gone, new MailDeliveryException(MailFailureKind.Permanent, "El servidor del destinatario rechazó el mensaje de forma definitiva (SMTP 550)."));
        const string odd = "inesperado@correo.example";
        var unexpected = await ReserveAsync(admin, "despacho-inesperado", odd);
        _mailer.FailFor(odd, new InvalidOperationException($"Falla interna hablando con {options.Host} como {options.UserName}"));

        await Dispatcher(options).RunOnceAsync(50, default);

        // Rechazo definitivo: agotado de inmediato (insistir no cambia nada), con su intento
        var (_, dispatch, attempts) = await QueueAsync(db, rejected.Number);
        Assert.Equal((OutgoingMailStatus.Exhausted, 1), (dispatch.Status, dispatch.Attempts));
        Assert.NotNull(dispatch.CompletedAt);
        Assert.Equal((1, false, "El servidor del destinatario rechazó el mensaje de forma definitiva (SMTP 550)."),
            (Assert.Single(attempts).Attempt, attempts[0].Succeeded, attempts[0].Error));
        // Una excepción que el emisor no clasificó: pasajera, y su mensaje (que podría traer datos del servidor) no se guarda
        (_, dispatch, attempts) = await QueueAsync(db, unexpected.Number);
        Assert.Equal((OutgoingMailStatus.Pending, 1), (dispatch.Status, dispatch.Attempts));
        Assert.Equal("El envío falló por un error inesperado (InvalidOperationException).", Assert.Single(attempts).Error);
        AssertNoSecrets(options, dispatch.LastError);
        Assert.Empty(_mailer.SentTo(gone));
    }

    [Fact]
    public async Task Si_la_reserva_ya_no_esta_reservada_la_cola_queda_cancelada_sin_envio()
    {
        using var admin = await LoginAsync();
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        const string to = "liberada@correo.example";
        var reservation = await ReserveAsync(admin, "despacho-liberada", to);
        await MailFlow.SendAsync(admin, new ReleasePcBuildReservationCommand(reservation.Number, "El cliente ya no la quiere"));

        var summary = await Dispatcher(Options()).RunOnceAsync(50, default);
        Assert.True(summary.Cancelled >= 1);
        var (_, dispatch, attempts) = await QueueAsync(db, reservation.Number);
        Assert.Equal((OutgoingMailStatus.Cancelled, 0, ReservationMail.ClosedReason), (dispatch.Status, dispatch.Attempts, dispatch.LastError));
        Assert.NotNull(dispatch.CompletedAt);
        Assert.Empty(attempts);
        Assert.Equal(0, _mailer.Attempted(to));
    }

    [Fact]
    public async Task Sin_servidor_de_correo_queda_pendiente_sin_gastar_intentos_y_el_servidor_de_la_empresa_manda()
    {
        using var admin = await LoginAsync();
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        const string to = "sin.servidor@correo.example";
        var reservation = await ReserveAsync(admin, "despacho-sin-servidor", to);
        var clock = new ShiftedClock(_services.GetRequiredService<DemoClock>());

        // Ni la empresa (su correo viene desactivado en los datos de prueba) ni el servidor tienen SMTP: pendiente, sin intento
        var before = clock.UtcNow;
        var summary = await Dispatcher(new MailOptions(), clock).RunOnceAsync(50, default);
        Assert.True(summary.Postponed >= 1);
        Assert.False(summary.Halted);
        var (mail, dispatch, attempts) = await QueueAsync(db, reservation.Number);
        Assert.Equal((OutgoingMailStatus.Pending, 0, MailDispatcher.NoServerReason, (DateTimeOffset?)null),
            (dispatch.Status, dispatch.Attempts, dispatch.LastError, dispatch.CompletedAt));
        Assert.InRange(dispatch.NextAttemptAt, before + MailDispatcher.NoServerDelay, clock.UtcNow + MailDispatcher.NoServerDelay);
        Assert.Empty(attempts);
        Assert.Equal(0, _mailer.Attempted(to));

        // La empresa activa su correo (Configuración › Facturación): su servidor manda sobre el del servidor
        await MailFlow.SendAsync(admin, new SaveMailSettingsCommand("smtp.empresa.example", 587, true, "correo@empresa.example", "clave-empresa-1",
            "correo@empresa.example", "Empresa de prueba", true));
        try
        {
            clock.Shift = MailDispatcher.NoServerDelay + TimeSpan.FromSeconds(5);
            await Dispatcher(Options(), clock).RunOnceAsync(50, default);
            var message = Assert.Single(_mailer.SentTo(to));
            Assert.Equal(("smtp.empresa.example", "correo@empresa.example", "clave-empresa-1", mail.MessageId("correo@empresa.example")),
                (message.Server.Host, message.Server.FromAddress, message.Server.Password, message.MessageId));
            (_, dispatch, attempts) = await QueueAsync(db, reservation.Number);
            Assert.Equal((OutgoingMailStatus.Sent, 1), (dispatch.Status, dispatch.Attempts));
            Assert.True(Assert.Single(attempts).Succeeded);
        }
        finally
        {
            // Las demás pruebas de la clase usan el servidor de respaldo
            await MailFlow.SendAsync(admin, new SaveMailSettingsCommand("smtp.empresa.example", 587, true, "correo@empresa.example", null,
                "correo@empresa.example", "Empresa de prueba", false));
        }
    }

    [Fact]
    public async Task Una_falla_del_servidor_de_correo_corta_la_pasada_sin_gastar_intentos()
    {
        using var admin = await LoginAsync();
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var options = Options();
        await Dispatcher(options).RunOnceAsync(500, default);   // la cola sin vencidos: solo quedan los dos de esta prueba
        const string first = "corte.a@correo.example", second = "corte.b@correo.example";
        var a = await ReserveAsync(admin, "despacho-corte-a", first);
        var b = await ReserveAsync(admin, "despacho-corte-b", second);
        _mailer.FailFor(first, new MailDeliveryException(MailFailureKind.Server,
            $"El servidor de correo rechazó las credenciales (SMTP 535) de {options.UserName} con {options.Password} en {options.Host}"));

        var summary = await Dispatcher(options).RunOnceAsync(50, default);
        Assert.True(summary.Mails >= 2);
        Assert.Equal((0, 0, 1, true), (summary.Sent, summary.Failed, summary.Postponed, summary.Halted));
        // El primero: pospuesto sin gastar un intento, con el motivo (sin credenciales ni servidor)
        var (_, dispatchA, attemptsA) = await QueueAsync(db, a.Number);
        Assert.Equal((OutgoingMailStatus.Pending, 0), (dispatchA.Status, dispatchA.Attempts));
        Assert.Empty(attemptsA);
        Assert.Contains("535", dispatchA.LastError, StringComparison.Ordinal);
        AssertNoSecrets(options, dispatchA.LastError);
        Assert.True(dispatchA.NextAttemptAt >= _services.GetRequiredService<DemoClock>().UtcNow + MailDispatcher.ServerFailureDelay - TimeSpan.FromMinutes(1));
        // El segundo: la pasada se cortó antes de tocarlo (ni intento, ni espera nueva). B6: la pasada lo había tomado (arrendamiento)
        // y, al cortarse, lo devolvió a la cola: le toca en la pasada siguiente
        var (_, dispatchB, attemptsB) = await QueueAsync(db, b.Number);
        Assert.Equal((OutgoingMailStatus.Pending, 0, (DateTimeOffset?)null, (string?)null), (dispatchB.Status, dispatchB.Attempts, dispatchB.LeasedUntil, dispatchB.LastError));
        Assert.True(dispatchB.IsDueAt(_services.GetRequiredService<DemoClock>().UtcNow));
        Assert.Empty(attemptsB);
        Assert.Equal(0, _mailer.Attempted(second));

        // La pasada siguiente (el servidor ya responde) envía el segundo; el primero espera sus 5 minutos
        var next = await Dispatcher(options).RunOnceAsync(50, default);
        Assert.False(next.Halted);
        Assert.Single(_mailer.SentTo(second));
        Assert.Empty(_mailer.SentTo(first));
    }

    /// <summary>B6 · Un reenvío que llega MIENTRAS el despachador envía el original ya no lo cancela (antes el cliente recibía dos
    /// correos y la cola quedaba «cancelada» con attempts = 0 y una fila de intento exitosa, rompiendo D08): se rechaza con
    /// <c>mail.sending</c> y, cuando el correo terminó de salir, el reenvío vuelve a funcionar.</summary>
    [Fact]
    public async Task Un_reenvio_mientras_el_correo_sale_se_rechaza_y_la_cola_cuadra_con_sus_intentos()
    {
        using var admin = await LoginAsync();
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        const string to = "en.vuelo@correo.example";
        var reservation = await ReserveAsync(admin, "despacho-en-vuelo", to);
        string? code = null;
        _mailer.During(to, async () =>
        {
            using var staff = await LoginAsync();
            code = await MailFlow.CodeAsync(() => MailFlow.SendAsync(staff, new ResendReservationMailCommand(reservation.Number)));
        });

        await Dispatcher(Options()).RunOnceAsync(50, default);

        Assert.Equal("mail.sending", code);
        // Un solo correo al cliente; la cola dice «enviado» con UN intento y la bitácora tiene ese intento (D08)
        Assert.Single(_mailer.SentTo(to));
        var (_, dispatch, attempts) = await QueueAsync(db, reservation.Number);
        Assert.Equal((OutgoingMailStatus.Sent, 1, (DateTimeOffset?)null), (dispatch.Status, dispatch.Attempts, dispatch.LeasedUntil));
        Assert.Equal((1, true), (Assert.Single(attempts).Attempt, attempts[0].Succeeded));
        // Ya enviado, el reenvío vuelve a funcionar: encola otro
        var row = await MailFlow.SendAsync(admin, new ResendReservationMailCommand(reservation.Number));
        Assert.Equal((OutgoingMailStatus.Pending, to), (row.Status, row.Recipient));
    }

    /// <summary>B6 · Dos réplicas del despachador: un lote que tarda más que el arrendamiento (120 s) ya no hace que un correo
    /// salga dos veces. La réplica A toma tres correos; el primero tarda; mientras envía el segundo vence el arrendamiento del
    /// tercero y la réplica B lo reclama y lo envía; A ya no lo toca (su arrendamiento dejó de ser el suyo).</summary>
    [Fact]
    public async Task Dos_replicas_no_envian_dos_veces_el_mismo_correo_aunque_el_lote_tarde_mas_que_el_arrendamiento()
    {
        using var admin = await LoginAsync();
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var options = Options();
        var clock = new ShiftedClock(_services.GetRequiredService<DemoClock>());
        await Dispatcher(options, clock).RunOnceAsync(500, default);   // la cola sin vencidos: solo quedan los de esta prueba
        const string slow = "lento@correo.example", first = "replica.a@correo.example", second = "replica.b@correo.example";
        var reservations = new[] { (slow, await ReserveAsync(admin, "replica-0", slow)), (first, await ReserveAsync(admin, "replica-1", first)),
            (second, await ReserveAsync(admin, "replica-2", second)) };
        var replicaA = Dispatcher(options, clock);
        var replicaB = Dispatcher(options, clock);
        MailDispatchSummary? other = null;
        _mailer.During(slow, () =>
        {
            clock.Shift += TimeSpan.FromSeconds(100);
            return Task.CompletedTask;
        });
        _mailer.During(first, async () =>
        {
            clock.Shift += TimeSpan.FromSeconds(30);
            other = await replicaB.RunOnceAsync(50, default);
        });

        var summary = await replicaA.RunOnceAsync(50, default);

        // A tomó los tres y envió los dos primeros; B reclamó el tercero (su arrendamiento de A había vencido) y lo envió
        Assert.Equal((3, 2), (summary.Mails, summary.Sent));
        Assert.NotNull(other);
        Assert.True(other!.Sent >= 1);
        foreach (var (to, reservation) in reservations)
        {
            Assert.Single(_mailer.SentTo(to));   // cada cliente recibió UN correo
            var (_, dispatch, attempts) = await QueueAsync(db, reservation.Number);
            Assert.Equal((OutgoingMailStatus.Sent, 1), (dispatch.Status, dispatch.Attempts));
            Assert.True(Assert.Single(attempts).Succeeded);   // D08: intentos = filas de la bitácora
        }
    }

    [Fact]
    public void Las_opciones_del_correo_no_muestran_la_contrasena_y_sin_servidor_no_hay_respaldo()
    {
        var options = Options();
        options.Enabled = true;
        var text = options.ToString();
        Assert.DoesNotContain("clave-secreta-123", text, StringComparison.Ordinal);
        Assert.Contains("smtp.despacho.example", text, StringComparison.Ordinal);
        Assert.DoesNotContain("clave-secreta-123", options.Server()!.ToString(), StringComparison.Ordinal);
        Assert.Equal(("smtp.despacho.example", 2525, false, "clave-secreta-123"),
            (options.Server()!.Host, options.Server()!.Port, options.Server()!.UseSsl, options.Server()!.Password));
        // Sin servidor o sin remitente no hay respaldo; nombre y espera por defecto
        Assert.Null(new MailOptions { Host = "smtp.x.example" }.Server());
        Assert.Null(new MailOptions { FromAddress = "a@x.example" }.Server());
        var defaults = new MailOptions { Host = " smtp.x.example ", FromAddress = " a@x.example ", UserName = " ", Password = "" };
        Assert.Equal(("smtp.x.example", 587, true, (string?)null, (string?)null, "a@x.example", MailOptions.DefaultFromName),
            (defaults.Server()!.Host, defaults.Server()!.Port, defaults.Server()!.UseSsl, defaults.Server()!.UserName, defaults.Server()!.Password,
                defaults.Server()!.FromAddress, defaults.Server()!.FromName));
        Assert.False(defaults.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(MailOptions.DefaultIntervalSeconds), defaults.Interval);
        Assert.Equal(TimeSpan.FromSeconds(1), new MailOptions { IntervalSeconds = 0 }.Interval);

        // El error que se guarda: una línea de 500 caracteres como máximo, sin credenciales ni datos del servidor
        var server = Options().Server()!;
        var error = MailDispatcher.Scrub(server, "535 5.7.8 clave-secreta-123\r\nusuario ENVIOS@despacho.example en SMTP.DESPACHO.EXAMPLE " + new string('x', 900));
        AssertNoSecrets(Options(), error);
        Assert.DoesNotContain('\n', error);
        Assert.True(error.Length <= OutgoingMailDispatch.MaxErrorLength);
        Assert.StartsWith("535 5.7.8 *** usuario *** en ***", error, StringComparison.Ordinal);
        Assert.Equal("El envío falló sin detalle.", MailDispatcher.Scrub(server, "  "));
    }
}
