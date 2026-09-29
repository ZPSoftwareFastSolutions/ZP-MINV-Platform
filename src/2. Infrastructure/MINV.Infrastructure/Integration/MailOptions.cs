using MINV.Application.Abstractions;

namespace MINV.Infrastructure.Integration;

/// <summary>
/// V7 · Correo saliente del servidor (sección <c>Minv:Mail</c>; en Docker, las variables <c>MINV_MAIL_*</c> de
/// <c>deploy/.env</c>). <see cref="Enabled"/> enciende el despachador de la cola de correos en este proceso; <see cref="Host"/>
/// y <see cref="FromAddress"/> forman el servidor SMTP de RESPALDO: el despachador usa primero el de la empresa
/// (<c>billing.mail_settings</c>, si está activo) y, si no tiene, este. Gmail: <c>smtp.gmail.com</c>, puerto 587,
/// <see cref="UseSsl"/> (STARTTLS) y una contraseña de aplicación que solo genera el dueño de la cuenta.
/// La contraseña llega SOLO por variable de entorno (nunca en <c>appsettings.json</c> ni en el repositorio, regla P-06) y
/// <see cref="ToString"/> no la muestra.
/// </summary>
public sealed class MailOptions
{
    public const string Section = "Minv:Mail";

    /// <summary>Segundos entre pasadas cuando la cola está vacía.</summary>
    public const int DefaultIntervalSeconds = 15;

    /// <summary>Nombre del remitente si no se indica otro.</summary>
    public const string DefaultFromName = "M-INV";

    /// <summary>¿Corre el despachador de la cola de correos en este proceso? (Con almacenamiento en memoria, además, registra
    /// el envío SMTP real.) Apagado por defecto.</summary>
    public bool Enabled { get; set; }

    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    /// <summary>STARTTLS (TLS explícito, el del puerto 587). <c>System.Net.Mail</c> no admite TLS implícito (465).</summary>
    public bool UseSsl { get; set; } = true;

    public string? UserName { get; set; }

    /// <summary>Contraseña (de aplicación, en Gmail). Solo por variable de entorno; nunca se imprime.</summary>
    public string? Password { get; set; }

    public string? FromAddress { get; set; }

    public string? FromName { get; set; }

    /// <summary>Dirección pública de la tienda (https://…): el correo enlaza «Ver mi reserva» a <c>{PublicUrl}/reserva/{número}</c>;
    /// sin ella el correo sale sin el enlace.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>Segundos entre pasadas del despachador cuando la cola está vacía (1 a 3600).</summary>
    public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;

    /// <summary>¿Hay servidor de respaldo? (servidor y remitente indicados).</summary>
    public bool HasServer => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);

    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Clamp(IntervalSeconds, 1, 3600));

    /// <summary>El servidor SMTP de respaldo, o null si no está configurado.</summary>
    public MailServer? Server() =>
        HasServer
            ? new MailServer(Host!.Trim(), Port, UseSsl, Blank(UserName)?.Trim(), Blank(Password), FromAddress!.Trim(),
                Blank(FromName)?.Trim() ?? DefaultFromName)
            : null;

    /// <summary>Nunca imprime la contraseña.</summary>
    public override string ToString() =>
        $"Correo del servidor: {(Enabled ? "encendido" : "apagado")}, " +
        (HasServer ? Server()!.ToString() : "sin servidor SMTP de respaldo") +
        $", enlace {(string.IsNullOrWhiteSpace(PublicUrl) ? "(ninguno)" : PublicUrl.Trim())}, cada {Interval.TotalSeconds:0} s";

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
