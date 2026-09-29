using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using MINV.Application.Abstractions;

namespace MINV.Infrastructure.Billing.Mail;

/// <summary>
/// V4.1 · Envío de correo por SMTP (servidor de la empresa en <c>billing.mail_settings</c>): entrega del XML y de la
/// representación gráfica al comprador (art. 26 RND 102100000011) y avisos de anulación o reversión. La contraseña llega
/// descifrada solo en memoria y nunca se registra. En la demostración no se registra (no se envían correos reales).
/// V7 · También la confirmación de las reservas (despachador de la cola de correos, con el servidor de la empresa o el del
/// servidor <c>Minv:Mail</c>): alternativa de texto plano (<c>multipart/alternative</c>), cabecera <c>Message-ID</c> determinista,
/// tiempo máximo REAL también en el envío asíncrono (<see cref="SmtpClient.Timeout"/> solo rige el envío síncrono: el límite lo
/// pone un <see cref="CancellationToken"/> con vencimiento) y fallas clasificadas en <see cref="MailDeliveryException"/> con un
/// mensaje propio en español, sin credenciales, sin el nombre del servidor y sin la dirección del destinatario.
/// </summary>
public sealed class SmtpMailSender : IMailSender
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _timeout;

    public SmtpMailSender()
        : this(Timeout)
    {
    }

    /// <summary>Con otro tiempo máximo (pruebas).</summary>
    public SmtpMailSender(TimeSpan timeout) => _timeout = timeout > TimeSpan.Zero ? timeout : Timeout;

    public async Task SendAsync(MailMessageSpec message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var server = message.Server;
        using var mail = Compose(message);
        using var client = new SmtpClient(server.Host, server.Port)
        {
            EnableSsl = server.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = (int)_timeout.TotalMilliseconds,
            UseDefaultCredentials = false,
        };
        if (!string.IsNullOrEmpty(server.UserName))
        {
            client.Credentials = new NetworkCredential(server.UserName, server.Password);
        }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_timeout);
        try
        {
            // WaitAsync: aunque el cliente no atienda la cancelación, la espera termina al vencer el plazo
            await client.SendMailAsync(mail, limit.Token).WaitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MailDeliveryException(MailFailureKind.Server,
                $"El servidor de correo no respondió en {_timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s.");
        }
        catch (SmtpFailedRecipientException ex)
        {
            throw Recipient(ex);
        }
        catch (SmtpException ex)
        {
            throw Classify(ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Net.Sockets.SocketException
                                       or System.Security.Authentication.AuthenticationException)
        {
            throw new MailDeliveryException(MailFailureKind.Server, ConnectionFailed, ex);
        }
    }

    public const string ConnectionFailed =
        "No se pudo conectar con el servidor de correo (nombre, puerto, red o conexión segura): revise su configuración.";

    /// <summary>El mensaje SMTP: remitente del servidor, UN destinatario sin nombre visible, asunto y cuerpo en UTF-8 y, si hay
    /// texto plano, <c>multipart/alternative</c> (texto primero, HTML después: el cliente de correo muestra el último que sabe
    /// dibujar).</summary>
    internal static MailMessage Compose(MailMessageSpec message)
    {
        var server = message.Server;
        MailAddress from, to;
        try
        {
            from = new MailAddress(server.FromAddress, server.FromName, Encoding.UTF8);
        }
        catch (FormatException ex)
        {
            throw new MailDeliveryException(MailFailureKind.Server, "La dirección del remitente del correo no es válida: revise su configuración.", ex);
        }
        try
        {
            to = new MailAddress(message.To);
        }
        catch (FormatException ex)
        {
            throw new MailDeliveryException(MailFailureKind.Permanent, "La dirección del destinatario no es válida.", ex);
        }
        var mail = new MailMessage
        {
            From = from,
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            HeadersEncoding = Encoding.UTF8,
        };
        try
        {
            mail.To.Add(to);
            if (string.IsNullOrEmpty(message.TextBody))
            {
                mail.Body = message.HtmlBody;
                mail.IsBodyHtml = true;
            }
            else
            {
                mail.Body = message.TextBody;
                mail.IsBodyHtml = false;
                mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
            }
            if (!string.IsNullOrEmpty(message.MessageId))
            {
                mail.Headers["Message-ID"] = message.MessageId;
            }
            foreach (var attachment in message.Attachments)
            {
                mail.Attachments.Add(new Attachment(new MemoryStream(attachment.Content, writable: false), attachment.FileName, attachment.ContentType));
            }
            return mail;
        }
        catch
        {
            mail.Dispose();
            throw;
        }
    }

    /// <summary>El servidor rechazó al destinatario: 5xx es definitivo (el buzón no existe o no lo acepta), 4xx pasajero.</summary>
    private static MailDeliveryException Recipient(SmtpFailedRecipientException ex)
    {
        var code = (int)ex.StatusCode;
        return code switch
        {
            >= 500 and < 600 => new MailDeliveryException(MailFailureKind.Permanent,
                $"El servidor del destinatario rechazó el mensaje de forma definitiva (SMTP {Code(code)}).", ex),
            >= 400 and < 500 => new MailDeliveryException(MailFailureKind.Transient,
                $"El buzón del destinatario no está disponible por ahora (SMTP {Code(code)}).", ex),
            _ => new MailDeliveryException(MailFailureKind.Server, ConnectionFailed, ex),
        };
    }

    /// <summary>
    /// Falla del diálogo con el servidor de correo. Solo 450, 451 y 452 (buzón ocupado, error local pasajero, sin espacio) son del
    /// mensaje y pasajeras; lo demás es del servidor: 421 (no disponible), 454/530/534/535/538 (credenciales o conexión segura),
    /// 500 a 504 y los rechazos del remitente o del contenido (configuración), y la falla de conexión (sin código).
    /// </summary>
    internal static MailDeliveryException Classify(SmtpException ex)
    {
        var code = (int)ex.StatusCode;
        return code switch
        {
            450 or 451 or 452 => new MailDeliveryException(MailFailureKind.Transient,
                $"El servidor de correo no pudo aceptar el mensaje por ahora (SMTP {Code(code)}).", ex),
            421 => new MailDeliveryException(MailFailureKind.Server, $"El servidor de correo no está disponible (SMTP {Code(code)}).", ex),
            454 or 530 or 534 or 535 or 538 => new MailDeliveryException(MailFailureKind.Server,
                $"El servidor de correo rechazó las credenciales o exige una conexión segura (SMTP {Code(code)}): revise el usuario, la contraseña y STARTTLS.", ex),
            >= 400 and < 600 => new MailDeliveryException(MailFailureKind.Server,
                $"El servidor de correo rechazó el envío (SMTP {Code(code)}): revise su configuración.", ex),
            _ => new MailDeliveryException(MailFailureKind.Server, ConnectionFailed, ex),
        };
    }

    private static string Code(int code) => code.ToString(CultureInfo.InvariantCulture);
}
