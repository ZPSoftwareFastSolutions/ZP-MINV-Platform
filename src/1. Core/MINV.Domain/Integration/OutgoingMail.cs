using System.Text.RegularExpressions;
using MINV.Domain.Common;

namespace MINV.Domain.Integration;

/// <summary>V7 · Tipo de correo saliente. Hoy solo existe la confirmación de una reserva; un tipo nuevo se agrega aquí y en el
/// <c>CHECK</c> de <c>integration.outgoing_mails</c>.</summary>
public enum OutgoingMailKind
{
    /// <summary>Confirmación de una reserva (carrito o armado): código, productos, total y hasta cuándo se guarda.</summary>
    ReservationConfirmed,
}

/// <summary>V7 · Estado de la cola de un correo: pendiente, enviado, agotado (se acabaron los intentos o el destinatario lo
/// rechazó para siempre) o cancelado (la reserva dejó de estar reservada, o un reenvío lo reemplazó).</summary>
public enum OutgoingMailStatus
{
    Pending,
    Sent,
    Exhausted,
    Cancelled,
}

/// <summary>
/// V7 · El HECHO «se pidió enviar este correo» (regla P-06): append-only y de la sucursal de la reserva. Se guarda en la MISMA
/// transacción que la reserva y nunca dentro de ella se envía nada (regla B-08): lo envía después el despachador. NO guarda el
/// asunto ni el cuerpo: se derivan al enviar desde la reserva, cuyos precios están congelados. <see cref="Recipient"/> puede
/// diferir del correo de contacto de la reserva (un reenvío a otra dirección).
/// </summary>
public sealed partial class OutgoingMail : Entity, IBranchScoped, IAppendOnly
{
    public const int MaxRecipientLength = 254;

    /// <summary>Dominio del <c>Message-ID</c> cuando el remitente no tiene uno válido.</summary>
    public const string FallbackMessageDomain = "minv.local";

    /// <summary>Caracteres que un destinatario nunca lleva: con ellos se arma un nombre visible, una lista o un comentario.</summary>
    private const string ForbiddenInRecipient = "<>\",;:()[]\\";

    private OutgoingMail()
    {
    }

    public OutgoingMail(Guid tenantId, Guid branchId, OutgoingMailKind kind, Guid pcBuildId, string recipient, Guid requestedByUserId,
        DateTimeOffset requestedAt)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        Kind = Guard.Defined(kind, "El tipo de correo");
        PcBuildId = Guard.NotEmpty(pcBuildId, nameof(pcBuildId));
        Recipient = NormalizeRecipient(recipient);
        RequestedByUserId = Guard.NotEmpty(requestedByUserId, nameof(requestedByUserId));
        RequestedAt = requestedAt.ToUniversalTime();
    }

    public Guid BranchId { get; private set; }

    public OutgoingMailKind Kind { get; private set; }

    /// <summary>Reserva (carrito o armado) de la que sale el contenido del correo.</summary>
    public Guid PcBuildId { get; private set; }

    /// <summary>Dirección a la que se pidió el envío, en minúsculas. Los topes por destinatario la cuentan por su buzón
    /// (<see cref="MailboxOf"/>).</summary>
    public string Recipient { get; private set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Quién lo pidió: el principal de la tienda, el cliente de la cuenta o el usuario del personal.</summary>
    public Guid RequestedByUserId { get; private set; }

    /// <summary>¿Es una dirección a la que se puede enviar? Una sola dirección, sin espacios ni saltos de línea, sin nombre
    /// visible y sin los caracteres con que se arma una lista o una cabecera.</summary>
    public static bool IsValidRecipient(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxRecipientLength || !RecipientPattern().IsMatch(text))
        {
            return false;
        }
        foreach (var c in text)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c) || ForbiddenInRecipient.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>El destinatario recortado y en minúsculas; <c>mail.recipient</c> si no es una dirección válida.</summary>
    public static string NormalizeRecipient(string? value) =>
        IsValidRecipient(value)
            ? value!.Trim().ToLowerInvariant()
            : throw new DomainException("mail.recipient", "El correo del destinatario no es una dirección válida.");

    /// <summary>
    /// V7 · Buzón al que de verdad llega <paramref name="recipient"/>, SOLO para contar los topes (el correo sale a la dirección
    /// tal como se escribió): sin la etiqueta «+…» de la parte local (subdirección: <c>ana+1@x</c> y <c>ana+2@x</c> llegan a
    /// <c>ana@x</c>) y, en Gmail (<c>gmail.com</c> y <c>googlemail.com</c>), sin los puntos de la parte local, que Gmail ignora
    /// (<c>a.na@gmail.com</c> = <c>ana@gmail.com</c>). Así una misma persona no recibe más de los correos permitidos cambiando
    /// la forma de escribir su dirección. Un texto que no es una dirección válida se devuelve recortado y en minúsculas.
    /// </summary>
    public static string MailboxOf(string? recipient)
    {
        var address = recipient?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IsValidRecipient(address))
        {
            return address;
        }
        var at = address.LastIndexOf('@');
        var local = address[..at];
        var domain = address[(at + 1)..];
        var plus = local.IndexOf('+', StringComparison.Ordinal);
        if (plus > 0)
        {
            local = local[..plus];
        }
        if (domain is "gmail.com" or "googlemail.com")
        {
            domain = "gmail.com";
            var withoutDots = local.Replace(".", string.Empty, StringComparison.Ordinal);
            local = withoutDots.Length > 0 ? withoutDots : local;
        }
        return local + "@" + domain;
    }

    /// <summary>
    /// <c>Message-ID</c> determinista del correo: el mismo en cada reintento, así el servidor del destinatario reconoce un
    /// duplicado si el proceso cayó después de enviar y antes de registrar el intento (entrega «al menos una vez»).
    /// <paramref name="senderAddress"/> es la dirección (o el dominio) del remitente; si no sirve, se usa
    /// <see cref="FallbackMessageDomain"/>.
    /// </summary>
    public string MessageId(string? senderAddress)
    {
        var domain = senderAddress?.Trim().ToLowerInvariant() ?? string.Empty;
        var at = domain.LastIndexOf('@');
        if (at >= 0)
        {
            domain = domain[(at + 1)..];
        }
        if (!DomainPattern().IsMatch(domain))
        {
            domain = FallbackMessageDomain;
        }
        return $"<minv-{Id:N}@{domain}>";
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex RecipientPattern();

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$")]
    private static partial Regex DomainPattern();
}

/// <summary>
/// V7 · Cola de envío de un correo (1:1, se crea en la misma transacción que <see cref="OutgoingMail"/>). Es la tabla MUTABLE
/// del correo (regla B-06: el estado que cambia vive aparte del hecho inmutable): el despachador toma los pendientes vencidos
/// con <c>FOR UPDATE SKIP LOCKED</c>, registra cada intento en <see cref="OutgoingMailAttempt"/> (append-only) y reprograma
/// con la espera de <see cref="OutgoingMailAttempt.BackoffBefore"/> hasta <see cref="OutgoingMailAttempt.MaxAttempts"/>
/// intentos. Solo cambia por sus métodos, y solo mientras está pendiente (salvo <see cref="CountLateAttempt"/>).
/// B6 · El despachador que lo toma queda como dueño de su ARRENDAMIENTO (<see cref="LeasedUntil"/>): lo renueva justo antes de
/// enviar solo si sigue siendo suyo (<see cref="Renew"/>, así otra réplica que lo reclamó después no lo envía dos veces) y,
/// mientras está vigente, un reenvío no lo cancela (el correo ya está saliendo).
/// </summary>
public sealed class OutgoingMailDispatch : BaseEntity, IConcurrencyAware
{
    public const int MaxErrorLength = 500;

    private OutgoingMailDispatch()
    {
    }

    public OutgoingMailDispatch(Guid tenantId, Guid outgoingMailId, DateTimeOffset createdAt)
        : base(tenantId)
    {
        OutgoingMailId = Guard.NotEmpty(outgoingMailId, nameof(outgoingMailId));
        Status = OutgoingMailStatus.Pending;
        NextAttemptAt = createdAt.ToUniversalTime();
    }

    public Guid OutgoingMailId { get; private set; }

    public OutgoingMailStatus Status { get; private set; }

    /// <summary>Intentos de envío ya hechos (0 a <see cref="OutgoingMailAttempt.MaxAttempts"/>).</summary>
    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Último error de envío o, si se canceló, el motivo. Nunca lleva credenciales ni datos del servidor.</summary>
    public string? LastError { get; private set; }

    /// <summary>B6 · Hasta cuándo lo tiene tomado el despachador que lo está enviando (null = nadie lo tiene). Lo fija el
    /// reclamo (<c>integration.claim_outgoing_mails</c> o <see cref="Claim"/>) y sirve de marca de dueño: cada reclamo pone
    /// otro valor. Se borra al cerrar o reprogramar el intento.</summary>
    public DateTimeOffset? LeasedUntil { get; private set; }

    public uint RowVersion { get; private set; }

    public bool IsPending => Status == OutgoingMailStatus.Pending;

    /// <summary>Número del intento que sigue (1 = el primero).</summary>
    public int NextAttempt => Attempts + 1;

    /// <summary>¿Le toca salir? Pendiente y con su espera cumplida.</summary>
    public bool IsDueAt(DateTimeOffset now) => IsPending && NextAttemptAt <= now;

    /// <summary>B6 · ¿Lo está enviando un despachador en este momento? (pendiente con su arrendamiento vigente)</summary>
    public bool IsLeasedAt(DateTimeOffset now) => IsPending && LeasedUntil is { } until && until > now;

    /// <summary>B6 · Un despachador lo toma hasta <paramref name="until"/> (lo mismo que hace
    /// <c>integration.claim_outgoing_mails</c> en PostgreSQL): solo si le toca. Mientras tanto no vuelve a la cola.</summary>
    public void Claim(DateTimeOffset now, DateTimeOffset until)
    {
        Guard.That(IsDueAt(now), "mail.not_due", "El correo no está pendiente o todavía no le toca salir.");
        Guard.That(until > now, "mail.lease", "El arrendamiento del correo debe terminar después de tomarlo.");
        LeasedUntil = Microseconds(until);
        NextAttemptAt = LeasedUntil.Value;
    }

    /// <summary>
    /// B6 · Justo antes de enviar, el despachador que lo tomó con el arrendamiento <paramref name="held"/> lo renueva hasta
    /// <paramref name="until"/> (tiempo para UN envío). Si el arrendamiento ya no es el suyo (venció y otra réplica lo reclamó) o
    /// el correo ya no está pendiente, <c>mail.lease</c>: ese despachador NO debe enviarlo.
    /// </summary>
    public void Renew(DateTimeOffset held, DateTimeOffset until)
    {
        Guard.That(IsPending && LeasedUntil == Microseconds(held), "mail.lease", "Otro despachador tomó este correo: no se envía dos veces.");
        LeasedUntil = Microseconds(until);
        NextAttemptAt = LeasedUntil.Value;
    }

    /// <summary>
    /// B6 · La pasada que lo tomó con <paramref name="held"/> se cortó antes de intentarlo (falló el servidor de correo): vuelve
    /// a la cola para la pasada siguiente, sin gastar un intento. Solo si el arrendamiento sigue siendo el de esa pasada (si no,
    /// no toca nada y devuelve falso).
    /// </summary>
    public bool Release(DateTimeOffset held, DateTimeOffset now)
    {
        if (!IsPending || LeasedUntil != Microseconds(held))
        {
            return false;
        }
        LeasedUntil = null;
        NextAttemptAt = now.ToUniversalTime();
        return true;
    }

    /// <summary>
    /// B6 · Un intento que terminó cuando la cola ya estaba cerrada por otro camino (no debería pasar: el arrendamiento lo
    /// impide; queda para una réplica detenida más que su arrendamiento): el intento OCURRIÓ, así que se cuenta junto con su fila
    /// de la bitácora (<see cref="Attempts"/> = filas de <c>outgoing_mail_attempts</c>) sin cambiar el estado ni el cierre.
    /// </summary>
    public void CountLateAttempt()
    {
        Guard.That(!IsPending, "mail.pending", "Un correo pendiente registra su intento con MarkSent o RecordFailure.");
        Guard.That(Attempts < OutgoingMailAttempt.MaxAttempts, "mail.attempt", $"El correo ya tiene {OutgoingMailAttempt.MaxAttempts} intentos.");
        Attempts++;
    }

    /// <summary>El servidor de correo aceptó el mensaje: queda enviado.</summary>
    public void MarkSent(DateTimeOffset now)
    {
        EnsurePending();
        Attempts++;
        Status = OutgoingMailStatus.Sent;
        CompletedAt = now.ToUniversalTime();
        LastError = null;
        LeasedUntil = null;
    }

    /// <summary>
    /// El intento falló. Una falla pasajera (red, tiempo agotado, servidor ocupado) se reprograma con la espera del intento
    /// siguiente y, al quinto intento, queda agotado. Una falla <paramref name="permanent"/> (el buzón no existe, el
    /// destinatario rechazó el mensaje) lo deja agotado de inmediato: insistir no cambia nada.
    /// </summary>
    public void RecordFailure(string? error, DateTimeOffset now, bool permanent = false)
    {
        EnsurePending();
        Attempts++;
        LastError = Clean(error) ?? "El envío falló sin detalle.";
        LeasedUntil = null;
        if (permanent || Attempts >= OutgoingMailAttempt.MaxAttempts)
        {
            Status = OutgoingMailStatus.Exhausted;
            CompletedAt = now.ToUniversalTime();
            return;
        }
        NextAttemptAt = now.ToUniversalTime() + OutgoingMailAttempt.BackoffBefore(Attempts + 1);
    }

    /// <summary>
    /// No se intentó enviar porque el problema no es del mensaje (la empresa no tiene el correo configurado, el servidor
    /// rechazó las credenciales): sigue pendiente, NO gasta un intento y vuelve a la cola después de
    /// <paramref name="delay"/> (de 1 minuto a 24 horas).
    /// </summary>
    public void Postpone(string reason, DateTimeOffset now, TimeSpan delay)
    {
        EnsurePending();
        Guard.That(delay >= TimeSpan.FromMinutes(1) && delay <= TimeSpan.FromHours(24), "mail.postpone",
            "La espera de un correo pospuesto va de 1 minuto a 24 horas.");
        LastError = Clean(Guard.Text(reason, "El motivo", 2000));
        NextAttemptAt = now.ToUniversalTime() + delay;
        LeasedUntil = null;
    }

    /// <summary>Ya no corresponde enviarlo: la reserva dejó de estar reservada (vendida, liberada o vencida) o un reenvío lo
    /// reemplazó. No gasta un intento; el motivo queda en <see cref="LastError"/>.</summary>
    public void Cancel(string reason, DateTimeOffset now)
    {
        EnsurePending();
        LastError = Clean(Guard.Text(reason, "El motivo", 2000));
        Status = OutgoingMailStatus.Cancelled;
        CompletedAt = now.ToUniversalTime();
        LeasedUntil = null;
    }

    /// <summary>La hora con la precisión de PostgreSQL (microsegundos): el arrendamiento se compara con el que devolvió la base.</summary>
    private static DateTimeOffset Microseconds(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }

    /// <summary>Texto de un error listo para guardar: una sola línea (sin caracteres de control) y de 500 caracteres como
    /// máximo; vacío = null.</summary>
    public static string? Clean(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }
        var text = new string(error.Select(c => char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c).ToArray()).Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
        {
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        }
        return text.Length > MaxErrorLength ? text[..MaxErrorLength] : text;
    }

    private void EnsurePending() =>
        Guard.That(IsPending, "mail.done", $"El correo ya está {Describe(Status)}: su cola no cambia más.");

    public static string Describe(OutgoingMailStatus status) => status switch
    {
        OutgoingMailStatus.Pending => "pendiente",
        OutgoingMailStatus.Sent => "enviado",
        OutgoingMailStatus.Exhausted => "agotado",
        OutgoingMailStatus.Cancelled => "cancelado",
        _ => status.ToString(),
    };
}

/// <summary>V7 · Intento de envío de un correo (append-only: cada reintento es una fila nueva, regla P-06). Solo se registra
/// cuando de verdad se intentó entregar el mensaje; un correo pospuesto no deja intento.</summary>
public sealed class OutgoingMailAttempt : Entity, IAppendOnly
{
    public const int MaxAttempts = 5;

    private OutgoingMailAttempt()
    {
    }

    public OutgoingMailAttempt(Guid tenantId, Guid outgoingMailId, int attempt, bool succeeded, string? error, DateTimeOffset attemptedAt,
        int durationMs)
        : base(tenantId)
    {
        OutgoingMailId = Guard.NotEmpty(outgoingMailId, nameof(outgoingMailId));
        Guard.That(attempt is >= 1 and <= MaxAttempts, "mail.attempt", $"El intento de un correo va de 1 a {MaxAttempts}.");
        Attempt = attempt;
        Succeeded = succeeded;
        // Un intento logrado no tiene error; uno fallido siempre dice por qué
        Error = succeeded ? null : OutgoingMailDispatch.Clean(error) ?? "El envío falló sin detalle.";
        AttemptedAt = attemptedAt.ToUniversalTime();
        DurationMs = Guard.NonNegative(durationMs, "La duración");
    }

    public Guid OutgoingMailId { get; private set; }

    public int Attempt { get; private set; }

    public bool Succeeded { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset AttemptedAt { get; private set; }

    public int DurationMs { get; private set; }

    /// <summary>Espera antes del intento <paramref name="attempt"/> (1 = inmediato): 1 min, 5 min, 30 min y 2 h.</summary>
    public static TimeSpan BackoffBefore(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.Zero,
        2 => TimeSpan.FromMinutes(1),
        3 => TimeSpan.FromMinutes(5),
        4 => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromHours(2),
    };
}
