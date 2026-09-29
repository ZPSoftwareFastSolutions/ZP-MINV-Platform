using System.Text.RegularExpressions;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Events;

namespace MINV.Domain.Sales;

/// <summary>V4.2 · Estado de un armado de PC (cotización). V6: <see cref="Reserved"/> (con stock reservado). Se guarda como texto.</summary>
public enum PcBuildStatus
{
    Draft,
    Quoted,
    Sold,
    Cancelled,

    /// <summary>V6 · Cotizado y con el stock de cada pieza reservado hasta <see cref="PcBuild.ReservedUntil"/>.</summary>
    Reserved,
}

/// <summary>V6 · Por dónde nació el armado: el escritorio (vendedor) o la tienda web (cliente). Se guarda como texto.</summary>
public enum PcBuildChannel
{
    Desktop,
    Web,
}

/// <summary>V7 · Qué es el documento: un armado de PC (piezas por ranura y compatibilidad, regla T-06) o un carrito (reserva de
/// CUALQUIER producto, sin ranuras obligatorias ni compatibilidad, regla P-05). No cambia después de crear. Se guarda como texto.</summary>
public enum PcBuildKind
{
    Build,
    Cart,
}

/// <summary>
/// V4.2 · Armado de PC (regla T-06): cotización con las piezas por ranura y el PRECIO COTIZADO de cada una (instantánea
/// comercial documentada: el cliente tiene ese precio mientras la cotización esté vigente, aunque cambie la lista de
/// precios). Número ARM-CM-000001. Borrador → Cotizado (congela precios y vigencia) → Vendido (con la venta que lo cobró)
/// o Anulado. La compatibilidad la decide SOLO <see cref="PcCompatibility.Check"/>: un armado con errores no se cotiza sin
/// la confirmación explícita del usuario y queda marcado (<see cref="QuotedWithErrors"/>). Pasa a la caja como carrito y
/// se vende con los casos de uso normales de venta.
/// <para>V6 (reglas S-03 y S-04): canal (escritorio o web), contacto del cliente web, <b>reserva</b>
/// (<c>Quoted → Reserved → Sold | Cancelled</c>) con vigencia (<see cref="ReservedUntil"/>) y motivo del cierre, publicación
/// como armado sugerido en la web y bitácora append-only <see cref="PcBuildEvent"/> de cada cambio de estado. Los hechos
/// que interesan fuera (reservado, liberado, vendido) salen como eventos de dominio al outbox.</para>
/// <para>V7 (regla P-05): <see cref="Kind"/> distingue el armado del <b>carrito</b>. Un carrito reserva cualquier producto:
/// sus líneas pueden ir SIN ranura, no aplica la regla de ranura única, no evalúa compatibilidad y nunca se publica como
/// armado sugerido; se numera RES-WEB-000001 (tienda) o RES-CM-000001 (mostrador). Los estados, la reserva de stock, la
/// bitácora y los eventos son los mismos. La reserva puede llevar los datos para la factura del visitante
/// (<see cref="SetBuyer"/>, mismas reglas del SIN que el cliente), que la caja precarga al cobrar.</para>
/// </summary>
public sealed partial class PcBuild : Entity, IBranchScoped, IConcurrencyAware, IAggregateRoot, IHasDomainEvents
{
    public const int MaxQuantity = 16;

    /// <summary>V6 · Máximo de líneas de una reserva web (regla S-05).</summary>
    public const int MaxLines = 20;

    /// <summary>V6 · Motivo con que el trabajo en segundo plano cierra una reserva vencida.</summary>
    public const string ExpiredReason = "Vencida";

    /// <summary>V6 · Prefijo de la numeración de las reservas web (por empresa, en la sucursal de la tienda).</summary>
    public const string WebNumberPrefix = "ARM-WEB";

    /// <summary>Prefijo de los armados del escritorio: ARM + código de la sucursal (ARM-CM-000001).</summary>
    public const string NumberPrefix = "ARM";

    /// <summary>V7 · Prefijo de los carritos del escritorio (mostrador): RES + código de la sucursal (RES-CM-000001).</summary>
    public const string CartNumberPrefix = "RES";

    /// <summary>V7 · Prefijo de los carritos de la tienda web (por empresa, en la sucursal de la tienda): RES-WEB-000001.</summary>
    public const string WebCartNumberPrefix = "RES-WEB";

    /// <summary>Ranuras que admiten varias líneas (el resto, una sola pieza por armado).</summary>
    public static readonly IReadOnlyList<PcSlot> MultiSlots =
        [PcSlot.Ram, PcSlot.Storage, PcSlot.Gpu, PcSlot.Monitor, PcSlot.Peripheral, PcSlot.Software, PcSlot.Service];

    private readonly List<PcBuildLine> _lines = new();
    private readonly List<PcBuildEvent> _history = new();
    private readonly List<IDomainEvent> _events = new();

    private PcBuild()
    {
    }

    public PcBuild(Guid tenantId, Guid branchId, string number, string name, Guid? customerId, DateOnly validUntil, Guid userId,
        DateTimeOffset createdAt)
        : this(tenantId, branchId, number, name, customerId, validUntil, userId, createdAt, PcBuildChannel.Desktop, PcBuildKind.Build)
    {
    }

    private PcBuild(Guid tenantId, Guid branchId, string number, string name, Guid? customerId, DateOnly validUntil, Guid userId,
        DateTimeOffset createdAt, PcBuildChannel channel, PcBuildKind kind)
        : base(tenantId)
    {
        Kind = Guard.Defined(kind, "El tipo");
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        Number = Guard.Text(number, NumberLabel, 40);
        Name = Guard.PlainText(name, NameLabel, 150);
        CustomerId = Guard.NotEmptyIfPresent(customerId, nameof(customerId));
        ValidUntil = validUntil;
        CreatedByUserId = Guard.NotEmpty(userId, nameof(userId));
        CreatedAt = createdAt.ToUniversalTime();
        Channel = channel;
        Status = PcBuildStatus.Draft;
        Log(PcBuildEventAction.Created, userId, createdAt, (kind, channel) switch
        {
            (PcBuildKind.Cart, PcBuildChannel.Web) => "Carrito creado desde la tienda web",
            (PcBuildKind.Cart, _) => "Carrito creado en el mostrador",
            (_, PcBuildChannel.Web) => "Armado creado desde la tienda web",
            _ => "Armado creado",
        });
    }

    /// <summary>
    /// V6 · Armado que nace en la tienda web (canal Web): sin cliente registrado, con los datos de contacto obligatorios
    /// (nombre y teléfono boliviano; correo y notas opcionales). Se cotiza y reserva en el mismo comando.
    /// </summary>
    public static PcBuild CreateWeb(Guid tenantId, Guid branchId, string number, string name, string contactName, string contactPhone,
        string? contactEmail, string? notes, DateOnly validUntil, Guid userId, DateTimeOffset createdAt)
    {
        var build = new PcBuild(tenantId, branchId, number, name, null, validUntil, userId, createdAt, PcBuildChannel.Web, PcBuildKind.Build);
        build.SetContact(contactName, contactPhone, contactEmail, notes);
        return build;
    }

    /// <summary>
    /// V7 · Carrito que nace en la tienda web (canal Web, regla P-05): reserva de cualquier producto con los datos de
    /// contacto obligatorios. <paramref name="customerId"/> solo cuando reserva un cliente con cuenta (queda ligada a él).
    /// </summary>
    public static PcBuild CreateWebCart(Guid tenantId, Guid branchId, string number, string name, string contactName, string contactPhone,
        string? contactEmail, string? notes, DateOnly validUntil, Guid userId, DateTimeOffset createdAt, Guid? customerId = null)
    {
        var cart = new PcBuild(tenantId, branchId, number, name, customerId, validUntil, userId, createdAt, PcBuildChannel.Web, PcBuildKind.Cart);
        cart.SetContact(contactName, contactPhone, contactEmail, notes);
        return cart;
    }

    /// <summary>
    /// V7 · Carrito que arma el personal en el mostrador (canal Desktop): para un cliente registrado o para quien deja su
    /// contacto (<see cref="SetContact"/>; en el escritorio es opcional).
    /// </summary>
    public static PcBuild CreateDesktopCart(Guid tenantId, Guid branchId, string number, string name, Guid? customerId, DateOnly validUntil,
        Guid userId, DateTimeOffset createdAt) =>
        new(tenantId, branchId, number, name, customerId, validUntil, userId, createdAt, PcBuildChannel.Desktop, PcBuildKind.Cart);

    /// <summary>V7 · Prefijo de la numeración según el tipo y el canal (al de escritorio se le agrega el código de la sucursal).</summary>
    public static string NumberPrefixOf(PcBuildKind kind, PcBuildChannel channel) => (kind, channel) switch
    {
        (PcBuildKind.Cart, PcBuildChannel.Web) => WebCartNumberPrefix,
        (PcBuildKind.Cart, _) => CartNumberPrefix,
        (_, PcBuildChannel.Web) => WebNumberPrefix,
        _ => NumberPrefix,
    };

    public Guid BranchId { get; private set; }

    /// <summary>V7 · Armado de PC o carrito (por defecto, armado). No cambia después de crear.</summary>
    public PcBuildKind Kind { get; private set; }

    /// <summary>V7 · ¿Es un carrito? (líneas sin ranura obligatoria, sin ranura única y sin compatibilidad).</summary>
    public bool IsCart => Kind == PcBuildKind.Cart;

    public string Number { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid? CustomerId { get; private set; }

    /// <summary>Último día en que vale la cotización (inclusive).</summary>
    public DateOnly ValidUntil { get; private set; }

    public PcBuildStatus Status { get; private set; }

    /// <summary>Se cotizó con errores de compatibilidad porque el usuario lo confirmó explícitamente (regla T-06).</summary>
    public bool QuotedWithErrors { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? QuotedAt { get; private set; }

    /// <summary>Venta de M-INV que lo cobró (de la misma sucursal).</summary>
    public Guid? InvoiceId { get; private set; }

    /// <summary>V6 · Escritorio o tienda web.</summary>
    public PcBuildChannel Channel { get; private set; }

    /// <summary>V6 · Contacto del cliente (obligatorio en canal Web; opcional en el escritorio).</summary>
    public string? ContactName { get; private set; }

    /// <summary>V6 · Teléfono normalizado (solo dígitos, con «+591» si vino con código de país).</summary>
    public string? ContactPhone { get; private set; }

    public string? ContactEmail { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>V6 · Cuándo se reservó el stock (null si nunca se reservó).</summary>
    public DateTimeOffset? ReservedAt { get; private set; }

    /// <summary>V6 · Hasta cuándo vale la reserva de stock.</summary>
    public DateTimeOffset? ReservedUntil { get; private set; }

    /// <summary>V6 · Motivo del cierre (liberada, vencida, anulada).</summary>
    public string? CancelReason { get; private set; }

    /// <summary>V6 · Armado sugerido visible en la tienda web (solo armados del escritorio cotizados, reservados o vendidos).</summary>
    public bool PublishedToWeb { get; private set; }

    /// <summary>V7 · Datos para la factura que dejó quien reserva (instantánea del visitante, opcional): tipo de documento del
    /// SIN (1 CI, 2 CEX, 3 PAS, 4 OD, 5 NIT). Null = la reserva no trae datos de factura (los captura el cajero al cobrar).</summary>
    public int? BuyerDocumentType { get; private set; }

    /// <summary>V7 · Número del documento (CI y NIT solo dígitos).</summary>
    public string? BuyerDocumentNumber { get; private set; }

    /// <summary>V7 · Complemento del SEGIP (solo con CI, hasta 5 caracteres).</summary>
    public string? BuyerComplement { get; private set; }

    /// <summary>V7 · Nombre o razón social para la factura.</summary>
    public string? BuyerName { get; private set; }

    /// <summary>V7 · ¿La reserva trae los datos para la factura? (tipo y número de documento).</summary>
    public bool HasBuyer => BuyerDocumentType is not null && BuyerDocumentNumber is not null;

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<PcBuildLine> Lines => _lines;

    /// <summary>V6 · Bitácora append-only del armado (regla S-04).</summary>
    public IReadOnlyCollection<PcBuildEvent> History => _history;

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events;

    public void ClearDomainEvents() => _events.Clear();

    /// <summary>Total derivado de las líneas (no se guarda).</summary>
    public decimal Total => _lines.Sum(l => l.Subtotal);

    /// <summary>V6 · Reservado y todavía vigente (o reservado y vencido: lo cierra el trabajo en segundo plano).</summary>
    public bool IsReservationActive => Status == PcBuildStatus.Reserved;

    public void Rename(string name, Guid? customerId)
    {
        EnsureEditable();
        Name = Guard.PlainText(name, NameLabel, 150);
        CustomerId = Guard.NotEmptyIfPresent(customerId, nameof(customerId));
    }

    /// <summary>V6 · Contacto del cliente. En canal Web el nombre y el teléfono son obligatorios. V7: el nombre y las notas
    /// son textos de una línea (sin caracteres de control: después viajan al correo de la reserva).</summary>
    public void SetContact(string? contactName, string? contactPhone, string? contactEmail, string? notes)
    {
        var name = Guard.OptionalPlainText(contactName, "El nombre de contacto", 120);
        var phone = NormalizePhone(contactPhone);
        if (Channel == PcBuildChannel.Web)
        {
            Guard.That(name is not null, "pcbuild.contact_name", "Indique el nombre de quien reserva.");
            Guard.That(phone is not null, "pcbuild.contact_phone", "Indique un teléfono o WhatsApp para confirmar la reserva.");
        }
        var email = Guard.OptionalEmail(contactEmail, "El correo de contacto");
        var text = Guard.OptionalPlainText(notes, "Las notas", 500);
        ContactName = name;
        ContactPhone = phone;
        ContactEmail = email;
        Notes = text;
    }

    /// <summary>
    /// V7 · Datos para la factura que deja quien reserva (opcionales): tipo y número de documento con las MISMAS reglas del
    /// SIN que <see cref="Customer.SetFiscalIdentity"/> (CI y NIT solo dígitos; complemento solo con CI), más el nombre o la
    /// razón social. Sin tipo de documento no se guarda nada (el complemento y la razón social no valen solos). Es una
    /// instantánea del visitante: la caja la precarga al cobrar y solo la ve quien tiene <c>sales.pcbuild.manage</c>.
    /// </summary>
    public void SetBuyer(int? documentType, string? documentNumber, string? complement, string? name)
    {
        Guard.That(Status is not (PcBuildStatus.Sold or PcBuildStatus.Cancelled), "pcbuild.state",
            $"{Subject} {Number} está {Describe(Status)}: sus datos de factura ya no cambian.");
        var legalName = Guard.OptionalPlainText(name, "El nombre o razón social", 150);
        if (documentType is null)
        {
            Guard.That(string.IsNullOrWhiteSpace(documentNumber) && string.IsNullOrWhiteSpace(complement) && legalName is null, "pcbuild.buyer",
                "Para guardar los datos de la factura indique el tipo y el número de documento (CI o NIT).");
            BuyerDocumentType = null;
            BuyerDocumentNumber = null;
            BuyerComplement = null;
            BuyerName = null;
            return;
        }
        Billing.FiscalRules.EnsureBuyerDocument(documentType.Value, documentNumber ?? string.Empty, complement);
        var number = Guard.PlainText(documentNumber, "El número de documento", 20);
        var extra = Guard.OptionalPlainText(complement, "El complemento", 5)?.ToUpperInvariant();
        BuyerDocumentType = documentType;
        BuyerDocumentNumber = number;
        BuyerComplement = extra;
        BuyerName = legalName;
    }

    /// <summary>
    /// V6 · Teléfono boliviano: 7 u 8 dígitos (fijo o celular), con o sin «+591»; se admiten espacios y guiones. Devuelve
    /// null si viene vacío y lanza <c>pcbuild.contact_phone</c> si no es válido.
    /// </summary>
    public static string? NormalizePhone(string? phone)
    {
        var text = phone?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        var compact = PhoneSeparators().Replace(text, string.Empty);
        var match = BolivianPhone().Match(compact);
        Guard.That(match.Success, "pcbuild.contact_phone", "El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.");
        return (match.Groups["cc"].Success ? "+591" : string.Empty) + match.Groups["n"].Value;
    }

    /// <summary>V6 · ¿Es el teléfono con que se hizo la reserva? (comparación sin espacios ni código de país).</summary>
    public bool MatchesPhone(string? phone)
    {
        if (ContactPhone is null)
        {
            return false;
        }
        string? other;
        try
        {
            other = NormalizePhone(phone);
        }
        catch (DomainException)
        {
            return false;
        }
        return other is not null && string.Equals(Digits(other), Digits(ContactPhone), StringComparison.Ordinal);

        static string Digits(string value) => value.StartsWith("+591", StringComparison.Ordinal) ? value[4..] : value;
    }

    /// <summary>
    /// Agrega una pieza. En un ARMADO la ranura es obligatoria y las ranuras que no están en <see cref="MultiSlots"/> admiten
    /// una sola línea. V7: en un CARRITO la ranura es opcional (<c>null</c> = producto suelto) y no hay ranura única (se pueden
    /// reservar dos fuentes o dos monitores distintos). La regla «sin ranura solo en un carrito» cruza dos tablas: vive aquí
    /// y, en la base, en un trigger.
    /// </summary>
    public PcBuildLine AddLine(PcSlot? slot, Guid variantId, int quantity, decimal quotedUnitPrice)
    {
        EnsureEditable();
        if (slot is { } given)
        {
            Guard.Defined(given, "La ranura");
        }
        Guard.That(quantity is >= 1 and <= MaxQuantity, "pcbuild.quantity", $"La cantidad va de 1 a {MaxQuantity}.");
        if (!IsCart)
        {
            Guard.That(slot is not null, "pcbuild.slot", "En un armado cada pieza va en su ranura.");
            Guard.That(MultiSlots.Contains(slot!.Value) || _lines.All(l => l.Slot != slot), "pcbuild.slot",
                $"El armado ya tiene {PcCompatibility.SlotName(slot.Value)}.");
        }
        Guard.That(_lines.Count < MaxLines, "pcbuild.lines", $"{(IsCart ? "Un carrito" : "Un armado")} admite como máximo {MaxLines} líneas.");
        var line = new PcBuildLine(TenantId, BranchId, Id, slot, variantId, quantity, quotedUnitPrice);
        _lines.Add(line);
        return line;
    }

    public void RemoveLine(Guid lineId)
    {
        EnsureEditable();
        Guard.That(_lines.RemoveAll(l => l.Id == lineId) == 1, "pcbuild.line", "La pieza no está en el armado.");
    }

    /// <summary>
    /// Emite la cotización: congela precios y vigencia (<paramref name="validUntil"/> no puede ser anterior a hoy). Con
    /// errores de compatibilidad solo si el usuario lo confirma (<paramref name="acceptIncompatible"/>), y queda marcado.
    /// V7: en un carrito la compatibilidad NO se evalúa (regla P-05): el informe se ignora y nunca queda marcado.
    /// </summary>
    public void Quote(DateOnly validUntil, DateOnly today, PcCompatibilityReport compatibility, bool acceptIncompatible, DateTimeOffset now,
        Guid? userId = null)
    {
        ArgumentNullException.ThrowIfNull(compatibility);
        EnsureEditable();
        Guard.That(_lines.Count > 0, "pcbuild.empty", IsCart ? "El carrito no tiene productos." : "El armado no tiene piezas.");
        Guard.That(validUntil >= today, "pcbuild.valid_until", "La vigencia de la cotización no puede terminar antes de hoy.");
        var compatible = IsCart || compatibility.IsCompatible;
        Guard.That(compatible || acceptIncompatible, "pcbuild.incompatible",
            $"El armado tiene {compatibility.Errors} error(es) de compatibilidad: confírmelo explícitamente para cotizarlo igual.");
        ValidUntil = validUntil;
        QuotedWithErrors = !compatible;
        QuotedAt = now.ToUniversalTime();
        Status = PcBuildStatus.Quoted;
        Log(PcBuildEventAction.Quoted, userId ?? CreatedByUserId, now,
            QuotedWithErrors ? $"Cotizado con errores de compatibilidad aceptados; vale hasta el {validUntil:dd/MM/yyyy}" : $"Cotizado; vale hasta el {validUntil:dd/MM/yyyy}");
    }

    /// <summary>V7 · Cotiza un carrito: congela precios y vigencia sin evaluar compatibilidad (informe vacío, regla P-05).</summary>
    public void QuoteCart(DateOnly validUntil, DateOnly today, DateTimeOffset now, Guid? userId = null)
    {
        Guard.That(IsCart, "pcbuild.kind", $"El armado {Number} no es un carrito: cotícelo con su informe de compatibilidad.");
        Quote(validUntil, today, PcCompatibilityReport.Empty, acceptIncompatible: false, now, userId);
    }

    /// <summary>
    /// V6 · Reserva el stock de las piezas hasta <paramref name="until"/> (regla S-03): solo desde Cotizado y vigente. Las
    /// reservas de stock las crea el caso de uso en la MISMA transacción, una por línea (<see cref="PcBuildLine"/>).
    /// </summary>
    public void Reserve(DateTimeOffset now, DateTimeOffset until, DateOnly today, Guid userId)
    {
        Guard.That(Status == PcBuildStatus.Quoted, "pcbuild.state", $"El armado {Number} está {Describe(Status)}: solo se reserva una cotización.");
        Guard.That(!IsExpiredOn(today), "pcbuild.expired", $"La cotización {Number} venció el {ValidUntil:dd/MM/yyyy}: cotícela de nuevo.");
        Guard.That(until > now, "pcbuild.reserved_until", "La reserva debe vencer en el futuro.");
        Guard.That(_lines.Count > 0, "pcbuild.empty", "El armado no tiene piezas.");
        ReservedAt = now.ToUniversalTime();
        ReservedUntil = until.ToUniversalTime();
        CancelReason = null;
        Status = PcBuildStatus.Reserved;
        Log(PcBuildEventAction.Reserved, userId, now, $"Stock reservado hasta el {until:dd/MM/yyyy HH:mm} ({(Channel == PcBuildChannel.Web ? "tienda web" : "escritorio")})");
        _events.Add(new PcBuildReservedEvent(Id, Number, BranchId, Channel.ToString(), Total, ReservedUntil.Value, EventLines(), now, Kind.ToString()));
    }

    /// <summary>V6 · Libera la reserva (el cliente desistió, el vendedor la liberó o venció): pasa a Anulado con motivo y el
    /// caso de uso devuelve el stock en la misma transacción. Con <paramref name="expired"/> la acción es «vencida».</summary>
    public void ReleaseReservation(string reason, DateTimeOffset now, Guid userId, bool expired = false)
    {
        Guard.That(Status == PcBuildStatus.Reserved, "pcbuild.state", $"El armado {Number} está {Describe(Status)}: no tiene una reserva que liberar.");
        CancelReason = Guard.Text(reason, "El motivo", 250);
        Status = PcBuildStatus.Cancelled;
        Log(expired ? PcBuildEventAction.Expired : PcBuildEventAction.Released, userId, now, expired ? "Reserva vencida: stock devuelto" : $"Reserva liberada: {CancelReason}");
        _events.Add(new PcBuildReleasedEvent(Id, Number, BranchId, Channel.ToString(), Total, CancelReason, expired, EventLines(), now, Kind.ToString()));
    }

    /// <summary>V6 · ¿La reserva ya venció? (la cierra el trabajo en segundo plano, nunca «al leer», regla S-04).</summary>
    public bool IsReservationExpired(DateTimeOffset now) => Status == PcBuildStatus.Reserved && ReservedUntil is { } until && until <= now;

    /// <summary>Cobrado en la caja: una cotización vencida ya no se vende a su precio (se vuelve a armar y cotizar). V6: un
    /// armado reservado se vende consumiendo sus reservas (lo hace el caso de uso en la misma transacción).</summary>
    public void MarkSold(Guid invoiceId, DateOnly today, Guid? userId = null, DateTimeOffset? now = null)
    {
        Guard.That(Status is PcBuildStatus.Draft or PcBuildStatus.Quoted or PcBuildStatus.Reserved, "pcbuild.state",
            $"El armado {Number} está {Describe(Status)}: ya no se puede vender.");
        Guard.That(!IsExpiredOn(today), "pcbuild.expired", $"La cotización {Number} venció el {ValidUntil:dd/MM/yyyy}: cotícela de nuevo.");
        var wasReserved = Status == PcBuildStatus.Reserved;
        InvoiceId = Guard.NotEmpty(invoiceId, nameof(invoiceId));
        Status = PcBuildStatus.Sold;
        var at = now ?? new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        Log(PcBuildEventAction.Sold, userId ?? CreatedByUserId, at, wasReserved ? "Vendido en la caja (reserva consumida)" : "Vendido en la caja");
        _events.Add(new PcBuildSoldEvent(Id, Number, BranchId, Channel.ToString(), Total, invoiceId, wasReserved, EventLines(), at, Kind.ToString()));
    }

    /// <summary>Anula un borrador o una cotización. V6: un armado reservado se anula liberando la reserva
    /// (<see cref="ReleaseReservation"/>, lo hace el caso de uso).</summary>
    public void Cancel(Guid? userId = null, DateTimeOffset? now = null, string? reason = null)
    {
        Guard.That(Status is PcBuildStatus.Draft or PcBuildStatus.Quoted, "pcbuild.state",
            $"El armado {Number} está {Describe(Status)}: ya no se puede anular.");
        CancelReason = Guard.OptionalText(reason, "El motivo", 250);
        Status = PcBuildStatus.Cancelled;
        PublishedToWeb = false;
        Log(PcBuildEventAction.Cancelled, userId ?? CreatedByUserId, now ?? CreatedAt, CancelReason is null ? "Armado anulado" : $"Armado anulado: {CancelReason}");
    }

    /// <summary>V6 · Publica el armado como sugerido en la tienda web: solo armados del ESCRITORIO cotizados, reservados o
    /// vendidos, con todas sus piezas. V7: un carrito nunca se publica (es la reserva de un cliente, regla P-05).</summary>
    public void Publish(Guid userId, DateTimeOffset now)
    {
        Guard.That(!IsCart, "pcbuild.publish_kind", $"La reserva {Number} es un carrito: solo se publican armados de PC.");
        Guard.That(Channel == PcBuildChannel.Desktop, "pcbuild.publish_channel", "Solo se publican armados del escritorio (los de la web son reservas de clientes).");
        Guard.That(Status is PcBuildStatus.Quoted or PcBuildStatus.Reserved or PcBuildStatus.Sold, "pcbuild.publish_state",
            $"El armado {Number} está {Describe(Status)}: cotícelo antes de publicarlo.");
        Guard.That(_lines.Count > 0, "pcbuild.empty", "El armado no tiene piezas.");
        Guard.That(!PublishedToWeb, "pcbuild.published", $"El armado {Number} ya está publicado.");
        PublishedToWeb = true;
        Log(PcBuildEventAction.Published, userId, now, "Publicado en la tienda web como armado sugerido");
    }

    public void Unpublish(Guid userId, DateTimeOffset now)
    {
        Guard.That(PublishedToWeb, "pcbuild.not_published", $"El armado {Number} no está publicado.");
        PublishedToWeb = false;
        Log(PcBuildEventAction.Unpublished, userId, now, "Retirado de la tienda web");
    }

    /// <summary>Una cotización está vencida desde el día siguiente a su vigencia (V6: también una reserva, por su fecha).</summary>
    public bool IsExpiredOn(DateOnly today) => Status is PcBuildStatus.Quoted or PcBuildStatus.Reserved && today > ValidUntil;

    public static string Describe(PcBuildStatus status) => status switch
    {
        PcBuildStatus.Draft => "en borrador",
        PcBuildStatus.Quoted => "cotizado",
        PcBuildStatus.Reserved => "reservado",
        PcBuildStatus.Sold => "vendido",
        PcBuildStatus.Cancelled => "anulado",
        _ => status.ToString(),
    };

    private void EnsureEditable() =>
        Guard.That(Status == PcBuildStatus.Draft, "pcbuild.not_draft", "Solo se modifica un armado en borrador.");

    private void Log(PcBuildEventAction action, Guid userId, DateTimeOffset at, string detail) =>
        _history.Add(new PcBuildEvent(TenantId, BranchId, Id, action, Status, userId, at, detail));

    private IReadOnlyList<PcBuildEventLine> EventLines() =>
        _lines.Select(l => new PcBuildEventLine(l.VariantId, l.Slot?.ToString(), l.Quantity, l.QuotedUnitPrice)).ToList();

    private string NumberLabel => IsCart ? "El número de la reserva" : "El número del armado";

    private string NameLabel => IsCart ? "El nombre de la reserva" : "El nombre del armado";

    private string Subject => IsCart ? "La reserva" : "El armado";

    [GeneratedRegex(@"[\s\-\.\(\)]")]
    private static partial Regex PhoneSeparators();

    [GeneratedRegex(@"^(?<cc>\+?591)?(?<n>[0-9]{7,8})$")]
    private static partial Regex BolivianPhone();
}

/// <summary>V4.2 · Pieza de un armado con su precio cotizado. V7: la línea de un carrito puede ir sin ranura
/// (<see cref="Slot"/> nulo = producto suelto); en un armado la ranura sigue siendo obligatoria (lo exige <see cref="PcBuild.AddLine"/>).</summary>
public sealed class PcBuildLine : Entity, IBranchScoped
{
    private PcBuildLine()
    {
    }

    internal PcBuildLine(Guid tenantId, Guid branchId, Guid pcBuildId, PcSlot? slot, Guid variantId, int quantity, decimal quotedUnitPrice)
        : base(tenantId)
    {
        BranchId = branchId;
        PcBuildId = pcBuildId;
        Slot = slot is { } given ? Guard.Defined(given, "La ranura") : null;
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        Quantity = quantity;
        QuotedUnitPrice = decimal.Round(Guard.NonNegative(quotedUnitPrice, "El precio cotizado"), 2, MidpointRounding.AwayFromZero);
    }

    public Guid BranchId { get; private set; }

    public Guid PcBuildId { get; private set; }

    /// <summary>Ranura del armador. V7: null solo en la línea de un carrito (regla P-05).</summary>
    public PcSlot? Slot { get; private set; }

    public Guid VariantId { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Precio unitario congelado al cotizar (con impuestos, como la lista de precios).</summary>
    public decimal QuotedUnitPrice { get; private set; }

    public decimal Subtotal => decimal.Round(Quantity * QuotedUnitPrice, 2, MidpointRounding.AwayFromZero);
}

/// <summary>V6 · Acción registrada en la bitácora del armado (regla S-04). Se guarda como texto.</summary>
public enum PcBuildEventAction
{
    Created,
    Quoted,
    Reserved,
    Released,
    Expired,
    Sold,
    Cancelled,
    Published,
    Unpublished,
}

/// <summary>V6 · Bitácora append-only del armado: cada cambio de estado con su acción, el estado resultante, quién y cuándo.
/// La escribe SOLO <see cref="PcBuild"/>.</summary>
public sealed class PcBuildEvent : Entity, IBranchScoped, IAppendOnly
{
    private PcBuildEvent()
    {
    }

    internal PcBuildEvent(Guid tenantId, Guid branchId, Guid pcBuildId, PcBuildEventAction action, PcBuildStatus status, Guid userId,
        DateTimeOffset occurredAt, string detail)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        PcBuildId = Guard.NotEmpty(pcBuildId, nameof(pcBuildId));
        Action = Guard.Defined(action, "La acción");
        Status = Guard.Defined(status, "El estado");
        UserId = Guard.NotEmpty(userId, nameof(userId));
        OccurredAt = occurredAt.ToUniversalTime();
        Detail = Guard.Text(detail, "El detalle", 250);
    }

    public Guid BranchId { get; private set; }

    public Guid PcBuildId { get; private set; }

    public PcBuildEventAction Action { get; private set; }

    /// <summary>Estado del armado después de la acción.</summary>
    public PcBuildStatus Status { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string Detail { get; private set; } = string.Empty;
}
