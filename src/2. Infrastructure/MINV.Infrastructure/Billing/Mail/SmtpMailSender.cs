using System.Net;
using System.Net.Mail;
using System.Text;
using MINV.Application.Abstractions;

namespace MINV.Infrastructure.Billing.Mail;

/// <summary>
/// V4.1 · Envío de correo por SMTP (servidor de la empresa en <c>billing.mail_settings</c>): entrega del XML y de la
/// representación gráfica al comprador (art. 26 RND 102100000011) y avisos de anulación o reversión. La contraseña llega
/// descifrada solo en memoria y nunca se registra. En la demostración no se registra (no se envían correos reales).
/// </summary>
public sealed class SmtpMailSender : IMailSender
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task SendAsync(MailMessageSpec message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var server = message.Server;
        using var mail = new MailMessage
        {
            From = new MailAddress(server.FromAddress, server.FromName, Encoding.UTF8),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = message.HtmlBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true,
        };
        mail.To.Add(new MailAddress(message.To));
        foreach (var attachment in message.Attachments)
        {
            mail.Attachments.Add(new Attachment(new MemoryStream(attachment.Content, writable: false), attachment.FileName, attachment.ContentType));
        }
        using var client = new SmtpClient(server.Host, server.Port)
        {
            EnableSsl = server.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = (int)Timeout.TotalMilliseconds,
            UseDefaultCredentials = false,
        };
        if (!string.IsNullOrEmpty(server.UserName))
        {
            client.Credentials = new NetworkCredential(server.UserName, server.Password);
        }
        await client.SendMailAsync(mail, cancellationToken);
    }
}
