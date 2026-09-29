using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MINV.Application.Abstractions;
using MINV.Infrastructure.Billing.Mail;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// V7 · Servidor SMTP mínimo en loopback para probar <see cref="SmtpMailSender"/> de verdad (sin red externa): guarda las órdenes y
/// el mensaje que recibe y puede rechazar al destinatario, exigir credenciales que nunca acepta o no responder nunca.
/// </summary>
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<string> _commands = [];
    private readonly List<string> _messages = [];
    private readonly Task _loop;

    public FakeSmtpServer(string? rejectRecipient = null, bool requireAuth = false, bool silent = false)
    {
        RejectRecipient = rejectRecipient;
        RequireAuth = requireAuth;
        Silent = silent;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = AcceptAsync();
    }

    public int Port { get; }

    /// <summary>Respuesta a RCPT TO (p. ej. «550 5.1.1 …»); null = lo acepta.</summary>
    public string? RejectRecipient { get; }

    /// <summary>Anuncia AUTH LOGIN, rechaza siempre las credenciales y exige autenticarse para MAIL FROM (530).</summary>
    public bool RequireAuth { get; }

    /// <summary>Acepta la conexión y nunca responde (ni el saludo).</summary>
    public bool Silent { get; }

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_commands)
            {
                return _commands.ToList();
            }
        }
    }

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_commands)
            {
                return _messages.ToList();
            }
        }
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                if (Silent)
                {
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                    return;
                }
                using var reader = new StreamReader(stream, Encoding.ASCII);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
                await writer.WriteLineAsync("220 buzon.prueba ESMTP");
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    Record(line);
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    switch (parts.Length == 0 ? string.Empty : parts[0].ToUpperInvariant())
                    {
                        case "EHLO":
                            await writer.WriteAsync(RequireAuth ? "250-buzon.prueba\r\n250-AUTH LOGIN\r\n250 OK\r\n" : "250-buzon.prueba\r\n250 OK\r\n");
                            break;
                        case "HELO":
                            await writer.WriteLineAsync("250 buzon.prueba");
                            break;
                        case "AUTH":
                            if (parts.Length < 3)
                            {
                                await writer.WriteLineAsync("334 VXNlcm5hbWU6");
                                Record(await reader.ReadLineAsync(_stop.Token) ?? string.Empty);
                            }
                            await writer.WriteLineAsync("334 UGFzc3dvcmQ6");
                            Record(await reader.ReadLineAsync(_stop.Token) ?? string.Empty);
                            await writer.WriteLineAsync("535 5.7.8 Authentication credentials invalid");
                            break;
                        case "MAIL":
                            await writer.WriteLineAsync(RequireAuth ? "530 5.7.0 Authentication required" : "250 2.1.0 OK");
                            break;
                        case "RCPT":
                            await writer.WriteLineAsync(RejectRecipient ?? "250 2.1.5 OK");
                            break;
                        case "DATA":
                            await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                            var data = new StringBuilder();
                            while (await reader.ReadLineAsync(_stop.Token) is { } body && body != ".")
                            {
                                data.Append(body).Append("\r\n");
                            }
                            lock (_commands)
                            {
                                _messages.Add(data.ToString());
                            }
                            await writer.WriteLineAsync("250 2.0.0 OK queued");
                            break;
                        case "QUIT":
                            await writer.WriteLineAsync("221 2.0.0 Bye");
                            return;
                        case "RSET":
                        case "NOOP":
                            await writer.WriteLineAsync("250 OK");
                            break;
                        default:
                            await writer.WriteLineAsync("502 5.5.2 Command not implemented");
                            break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // El cliente cortó la conexión (o la prueba terminó)
            }
        }
    }

    private void Record(string line)
    {
        lock (_commands)
        {
            _commands.Add(line);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Detenido
        }
        _stop.Dispose();
    }
}

/// <summary>
/// V7 · <see cref="SmtpMailSender"/> contra un servidor SMTP de prueba en loopback: texto plano y HTML (<c>multipart/alternative</c>)
/// con el <c>Message-ID</c> del correo y un solo destinatario; fallas clasificadas (destinatario rechazado = definitiva, credenciales
/// rechazadas o sin conexión = del servidor) con mensajes sin datos del servidor ni del destinatario; y el tiempo máximo REAL en el
/// envío asíncrono (riesgo R-01: <c>SmtpClient.Timeout</c> solo rige el envío síncrono).
/// </summary>
public sealed class SmtpMailSenderTests
{
    private const string Recipient = "cliente@correo.example";
    private const string MessageId = "<minv-0123456789abcdef0123456789abcdef@tienda.example>";

    private static MailMessageSpec Spec(int port, string? user = null, string? password = null) =>
        new(new MailServer("127.0.0.1", port, false, user, password, "ventas@tienda.example", "Tienda de Prueba"), Recipient,
            "Reserva ARM-WEB-000123 · Tienda de Prueba", "<p>Su reserva <b>ARM-WEB-000123</b></p>", [], "Su reserva ARM-WEB-000123", MessageId);

    [Fact]
    public async Task Envia_texto_plano_y_HTML_con_el_Message_ID_a_un_solo_destinatario()
    {
        await using var server = new FakeSmtpServer();
        await new SmtpMailSender(TimeSpan.FromSeconds(10)).SendAsync(Spec(server.Port));

        var commands = server.Commands;
        Assert.Contains(commands, c => c.StartsWith("MAIL FROM:<ventas@tienda.example>", StringComparison.OrdinalIgnoreCase));
        Assert.Equal($"RCPT TO:<{Recipient}>", Assert.Single(commands, c => c.StartsWith("RCPT", StringComparison.OrdinalIgnoreCase)), ignoreCase: true);
        var data = Assert.Single(server.Messages);
        Assert.Contains($"Message-ID: {MessageId}", data, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("multipart/alternative", data, StringComparison.OrdinalIgnoreCase);
        var plain = data.IndexOf("text/plain", StringComparison.OrdinalIgnoreCase);
        var html = data.IndexOf("text/html", StringComparison.OrdinalIgnoreCase);
        Assert.True(plain >= 0 && html > plain, "texto plano primero y HTML después");
        Assert.Contains($"To: {Recipient}", data, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sin_texto_plano_sale_solo_en_HTML_como_hasta_la_V6()
    {
        await using var server = new FakeSmtpServer();
        var spec = Spec(server.Port) with { TextBody = null, MessageId = null };
        await new SmtpMailSender(TimeSpan.FromSeconds(10)).SendAsync(spec);
        var data = Assert.Single(server.Messages);
        Assert.Contains("text/html", data, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("multipart/alternative", data, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_destinatario_rechazado_es_una_falla_definitiva_sin_su_direccion_en_el_mensaje()
    {
        await using var server = new FakeSmtpServer(rejectRecipient: $"550 5.1.1 <{Recipient}>: Recipient address rejected: User unknown");
        var ex = await Assert.ThrowsAsync<MailDeliveryException>(() => new SmtpMailSender(TimeSpan.FromSeconds(10)).SendAsync(Spec(server.Port)));
        Assert.Equal(MailFailureKind.Permanent, ex.Kind);
        Assert.Contains("550", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Recipient, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(server.Messages);

        // Un rechazo pasajero del buzón (452) se reintenta
        await using var busy = new FakeSmtpServer(rejectRecipient: "452 4.2.2 Mailbox full, try later");
        Assert.Equal(MailFailureKind.Transient,
            (await Assert.ThrowsAsync<MailDeliveryException>(() => new SmtpMailSender(TimeSpan.FromSeconds(10)).SendAsync(Spec(busy.Port)))).Kind);
    }

    [Fact]
    public async Task Credenciales_rechazadas_y_servidor_inalcanzable_son_fallas_del_servidor_sin_credenciales_en_el_mensaje()
    {
        await using var server = new FakeSmtpServer(requireAuth: true);
        var ex = await Assert.ThrowsAsync<MailDeliveryException>(() =>
            new SmtpMailSender(TimeSpan.FromSeconds(10)).SendAsync(Spec(server.Port, "ventas@tienda.example", "clave-de-aplicacion")));
        Assert.Equal(MailFailureKind.Server, ex.Kind);
        Assert.Contains("530", ex.Message, StringComparison.Ordinal);
        foreach (var secret in new[] { "clave-de-aplicacion", "ventas@tienda.example", "127.0.0.1" })
        {
            Assert.DoesNotContain(secret, ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Empty(server.Messages);

        // Nadie escucha en el puerto: no se pudo conectar
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        var refused = await Assert.ThrowsAsync<MailDeliveryException>(() => new SmtpMailSender(TimeSpan.FromSeconds(10)).SendAsync(Spec(port)));
        Assert.Equal((MailFailureKind.Server, SmtpMailSender.ConnectionFailed), (refused.Kind, refused.Message));
    }

    [Fact]
    public async Task El_tiempo_maximo_rige_tambien_el_envio_asincrono()
    {
        await using var server = new FakeSmtpServer(silent: true);
        var watch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<MailDeliveryException>(() => new SmtpMailSender(TimeSpan.FromSeconds(1)).SendAsync(Spec(server.Port)));
        Assert.Equal(MailFailureKind.Server, ex.Kind);
        Assert.Equal("El servidor de correo no respondió en 1 s.", ex.Message);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(15));

        // La cancelación de quien llama NO es una falla del servidor: sale como cancelación
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SmtpMailSender(TimeSpan.FromSeconds(30)).SendAsync(Spec(server.Port), cancelled.Token));
    }

    [Fact]
    public void Una_direccion_de_destinatario_invalida_es_definitiva_y_una_de_remitente_invalida_es_del_servidor()
    {
        var spec = Spec(25);
        Assert.Equal(MailFailureKind.Permanent, Assert.Throws<MailDeliveryException>(() => SmtpMailSender.Compose(spec with { To = "no es un correo" })).Kind);
        Assert.Equal(MailFailureKind.Server,
            Assert.Throws<MailDeliveryException>(() => SmtpMailSender.Compose(spec with { Server = spec.Server with { FromAddress = "remitente" } })).Kind);
        using var mail = SmtpMailSender.Compose(spec);
        Assert.Equal((Recipient, "Su reserva ARM-WEB-000123", false, 1, MessageId),
            (Assert.Single(mail.To).Address, mail.Body, mail.IsBodyHtml, mail.AlternateViews.Count, mail.Headers["Message-ID"]));
        Assert.Equal(string.Empty, Assert.Single(mail.To).DisplayName);
    }
}
