namespace MINV.Application.Abstractions;

// =====================================================================================================================
// Puerto de correo saliente. Nació en la V4.1 para la facturación (entrega del XML y del PDF, avisos de anulación) y desde
// la V7 lo usa también el correo de las reservas (regla P-06). Vivía en SiatPorts.cs; el espacio de nombres no cambió.
// =====================================================================================================================

/// <summary>Servidor SMTP de un envío, con la contraseña EN CLARO solo en memoria (en la base va cifrada, regla F-12).</summary>
public sealed record MailServer(string Host, int Port, bool UseSsl, string? UserName, string? Password, string FromAddress, string FromName)
{
    /// <summary>Nunca imprime la contraseña (ni en un registro, ni en una excepción, ni en el depurador).</summary>
    public override string ToString() =>
        $"SMTP {Host}:{Port} ({(UseSsl ? "STARTTLS" : "sin cifrar")}) usuario {(string.IsNullOrEmpty(UserName) ? "(ninguno)" : UserName)} " +
        $"remitente {FromAddress}, contraseña {(string.IsNullOrEmpty(Password) ? "(ninguna)" : "***")}";

    /// <summary>V7 · Quita de un texto (el mensaje de una excepción del servidor de correo) la contraseña y el usuario de este
    /// servidor, por si el servidor los repite en su respuesta: lo que se guarda en la cola nunca lleva credenciales.</summary>
    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        var clean = text;
        if (!string.IsNullOrEmpty(Password))
        {
            clean = clean.Replace(Password, "***", StringComparison.Ordinal);
        }
        if (!string.IsNullOrEmpty(UserName))
        {
            clean = clean.Replace(UserName, "***", StringComparison.OrdinalIgnoreCase);
        }
        return clean;
    }
}

public sealed record MailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Mensaje a enviar. V7: <paramref name="TextBody"/> es la alternativa de texto plano del cuerpo HTML (sin ella el mensaje
/// sale solo en HTML, como hasta la V6) y <paramref name="MessageId"/> es la cabecera <c>Message-ID</c> con sus corchetes
/// angulares (<c>&lt;id@dominio&gt;</c>); determinista por correo, para que un reintento no parezca un mensaje nuevo. Sin
/// valor, la decide el servidor de correo.
/// </summary>
public sealed record MailMessageSpec(MailServer Server, string To, string Subject, string HtmlBody, IReadOnlyList<MailAttachment> Attachments,
    string? TextBody = null, string? MessageId = null)
{
    /// <summary>No imprime el cuerpo ni los adjuntos, y del servidor solo lo que imprime <see cref="MailServer"/>.</summary>
    public override string ToString() => $"Correo «{Subject}» para {To} por {Server}";
}

/// <summary>V4.1 · Envío de correo (entrega del XML y de la representación gráfica al comprador). V7: también la confirmación
/// de una reserva, que envía el despachador de la cola de correos DESPUÉS del COMMIT (nunca un caso de uso de negocio).</summary>
public interface IMailSender
{
    Task SendAsync(MailMessageSpec message, CancellationToken cancellationToken = default);
}
