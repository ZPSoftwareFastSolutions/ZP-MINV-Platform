using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MINV.ApiGateway;
using MINV.ApiGateway.Background;
using MINV.Application.Abstractions;
using MINV.Infrastructure.Billing.Mail;
using MINV.Infrastructure.Integration;
using MINV.Infrastructure.Tests;

namespace MINV.Integration.Tests;

/// <summary>V7 · Gateway REAL (Kestrel, base en memoria, empresa NUBE) con el emisor de correo falso y el servidor SMTP de respaldo
/// configurado; el trabajo en segundo plano del correo queda apagado (como en las pruebas de webhooks): la pasada se ejecuta a mano.</summary>
public sealed class MailGatewayFixture : SeededServer
{
    public RecordingMailSender Mailer { get; } = new();

    protected override string[] ExtraArgs =>
    [
        "--Minv:Mail:Host", "smtp.nube.example", "--Minv:Mail:FromAddress", "reservas@nube.example", "--Minv:Mail:FromName", "Tienda NUBE",
        "--Minv:Mail:PublicUrl", "https://tienda.example",
    ];

    protected override WebApplication Build(string[] args) => ApiGatewayApp.Build(args, services => services.AddSingleton<IMailSender>(Mailer));
}

/// <summary>
/// V7 · Correo de la reserva de punta a punta por la API pública (regla P-06): reservar con correo responde <c>mailQueued</c> sin
/// enviar nada; la pasada del despachador envía UN mensaje con el número de la reserva; repetir la misma petición (idempotente) no
/// produce otro. Y el registro del gateway: despachador siempre, trabajo en segundo plano y SMTP real solo con <c>Minv:Mail:Enabled</c>.
/// </summary>
public sealed class ReservationMailApiTests(MailGatewayFixture server) : IClassFixture<MailGatewayFixture>
{
    private const string Sku = "CASE-COR-4000D";   // gabinete sin serie con existencia en la casa matriz

    private static HttpRequestMessage Post(object body, string key) =>
        new(HttpMethod.Post, "/storefront/v1/reservations") { Content = JsonContent.Create(body), Headers = { { "Idempotency-Key", key } } };

    private static object Reservation(string email) => new
    {
        lines = new[] { new { sku = Sku, quantity = 1, slot = "case" } },
        contact = new { name = "Paola Vargas", phone = "71239876", email },
        notes = "Pruebas del correo",
    };

    [Fact]
    public async Task Reservar_con_correo_encola_y_la_pasada_envia_un_solo_mensaje_aunque_se_repita_la_peticion()
    {
        var http = server.CreateClient();
        const string to = "paola.vargas@correo.example";
        var key = "correo-" + Guid.NewGuid().ToString("N");

        var created = await http.SendAsync(Post(Reservation("Paola.Vargas@Correo.Example"), key));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reservation = await created.Content.ReadFromJsonAsync<JsonElement>();
        var number = reservation.GetProperty("number").GetString()!;
        Assert.True(reservation.GetProperty("mailQueued").GetBoolean());
        Assert.DoesNotContain("paola.vargas", reservation.GetRawText(), StringComparison.OrdinalIgnoreCase);   // el correo no se devuelve (S-06)
        Assert.Equal(0, server.Mailer.Attempted(to));   // la reserva no envía nada: sale del despachador después del COMMIT

        // Una pasada a mano: UN mensaje al cliente con el número de la reserva y el enlace público
        var dispatcher = server.Services.GetRequiredService<MailDispatcher>();
        var summary = await dispatcher.RunOnceAsync(100, default);
        Assert.True(summary.Sent >= 1);
        Assert.False(summary.Halted);
        var message = Assert.Single(server.Mailer.SentTo(to));
        Assert.Contains(number, message.Subject, StringComparison.Ordinal);
        Assert.Contains(number, message.TextBody, StringComparison.Ordinal);
        Assert.Contains($"https://tienda.example/reserva/{number}", message.HtmlBody, StringComparison.Ordinal);
        Assert.Equal(("smtp.nube.example", "reservas@nube.example", "Tienda NUBE"), (message.Server.Host, message.Server.FromAddress, message.Server.FromName));
        Assert.EndsWith("@nube.example>", message.MessageId, StringComparison.Ordinal);

        // La misma petición otra vez: la reserva original (idempotente), sin otro correo en la cola ni otro mensaje
        var again = await http.SendAsync(Post(Reservation("Paola.Vargas@Correo.Example"), key));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues("Idempotent-Replayed").Single());
        var replayed = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(number, replayed.GetProperty("number").GetString());
        await dispatcher.RunOnceAsync(100, default);
        Assert.Single(server.Mailer.SentTo(to));
        Assert.Equal(1, server.Mailer.Attempted(to));

        // El trabajo en segundo plano del correo está apagado por defecto (la pasada solo la hizo la prueba)
        Assert.DoesNotContain(server.Services.GetServices<IHostedService>(), s => s is MailDispatcherService);
    }

    [Fact]
    public async Task Con_el_correo_encendido_corre_el_trabajo_en_segundo_plano_y_en_memoria_se_registra_el_envio_SMTP()
    {
        await using var app = ApiGatewayApp.Build([
            "--urls", "http://127.0.0.1:0", "--Minv:Storage", "memoria", "--Minv:Webhooks:Enabled", "false",
            "--Minv:Mail:Enabled", "true", "--Minv:Mail:IntervalSeconds", "30", "--Minv:Mail:Host", "buzon", "--Minv:Mail:Port", "1025",
            "--Minv:Mail:UseSsl", "false", "--Minv:Mail:FromAddress", "tienda@nube.example", "--Minv:Mail:Password", "clave-de-aplicacion",
        ]);
        Assert.Contains(app.Services.GetServices<IHostedService>(), s => s is MailDispatcherService);
        Assert.IsType<SmtpMailSender>(app.Services.GetRequiredService<IMailSender>());
        var options = app.Services.GetRequiredService<MailOptions>();
        Assert.Equal((true, "buzon", 1025, false, TimeSpan.FromSeconds(30)), (options.Enabled, options.Server()!.Host, options.Server()!.Port,
            options.Server()!.UseSsl, options.Interval));
        Assert.DoesNotContain("clave-de-aplicacion", options.ToString(), StringComparison.Ordinal);

        // Apagado (por defecto) y en memoria: sin emisor real, pero con el despachador para una pasada a mano
        await using var off = ApiGatewayApp.Build(["--urls", "http://127.0.0.1:0", "--Minv:Storage", "memoria", "--Minv:Webhooks:Enabled", "false"]);
        Assert.DoesNotContain(off.Services.GetServices<IHostedService>(), s => s is MailDispatcherService);
        Assert.Null(off.Services.GetService<IMailSender>());
        Assert.NotNull(off.Services.GetService<MailDispatcher>());
        Assert.False(off.Services.GetRequiredService<MailOptions>().Enabled);
    }
}
