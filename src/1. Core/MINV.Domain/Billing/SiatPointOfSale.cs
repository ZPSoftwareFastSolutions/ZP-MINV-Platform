using MINV.Domain.Common;

namespace MINV.Domain.Billing;

/// <summary>
/// V4.1 · Punto de venta del SIN dentro de una sucursal y un ambiente. El código 0 representa la sucursal sin punto de
/// venta («no corresponde»); los demás los asigna el SIN con <c>registroPuntoVenta</c>. Guarda el MODO de operación
/// (en línea, fuera de línea, contingencia manual, recuperando): es estado que cambia; los hechos (CUIS, CUFD,
/// eventos, documentos) viven en sus propias tablas.
/// </summary>
public sealed class SiatPointOfSale : Entity, IBranchScoped, IConcurrencyAware
{
    private SiatPointOfSale()
    {
    }

    public SiatPointOfSale(Guid tenantId, Guid branchId, int environment, int code, int typeCode, string name, string? description,
        Guid? posRegisterId, DateTimeOffset now)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        Guard.That(environment is SiatCodes.EnvironmentProduction or SiatCodes.EnvironmentTest, "siat.environment",
            "El ambiente es 1 (producción) o 2 (pruebas y piloto).");
        Guard.That(code is >= 0 and <= 9999, "siat.pos_code", "El código de punto de venta va de 0 a 9999.");
        Guard.That(typeCode is >= 0 and <= 99, "siat.pos_type", "El tipo de punto de venta no es válido.");
        Environment = environment;
        Code = code;
        TypeCode = typeCode;
        Name = Guard.Text(name, "El nombre del punto de venta", 100);
        Description = Guard.OptionalText(description, "La descripción", 200);
        PosRegisterId = Guard.NotEmptyIfPresent(posRegisterId, nameof(posRegisterId));
        Mode = SiatConnectionMode.Online;
        ModeSince = now;
    }

    public Guid BranchId { get; private set; }

    public int Environment { get; private set; }

    /// <summary>codigoPuntoVenta del SIN (0 = sin punto de venta).</summary>
    public int Code { get; private set; }

    /// <summary>Tipo de punto de venta del catálogo (5 = cajeros); 0 para el punto «sin punto de venta».</summary>
    public int TypeCode { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Caja de M-INV que factura con este punto de venta (null: se usa para la API y la oficina).</summary>
    public Guid? PosRegisterId { get; private set; }

    public SiatConnectionMode Mode { get; private set; }

    public DateTimeOffset ModeSince { get; private set; }

    /// <summary>Última comunicación exitosa con el SIN.</summary>
    public DateTimeOffset? LastContactAt { get; private set; }

    /// <summary>Fallos de comunicación seguidos (a los 2 se pasa a fuera de línea).</summary>
    public int ConsecutiveFailures { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Próximo intento de verificar la comunicación estando fuera de línea (≤ 2 h).</summary>
    public DateTimeOffset? RetryAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public bool IsClosed => ClosedAt is not null;

    public bool IsOnline => Mode == SiatConnectionMode.Online;

    /// <summary>Emite fuera de línea (tipo de emisión 2) en este modo.</summary>
    public bool EmitsOffline => Mode is SiatConnectionMode.Offline or SiatConnectionMode.ManualContingency or SiatConnectionMode.Recovering;

    public void LinkRegister(Guid? posRegisterId) => PosRegisterId = Guard.NotEmptyIfPresent(posRegisterId, nameof(posRegisterId));

    public void Rename(string name, string? description)
    {
        Name = Guard.Text(name, "El nombre del punto de venta", 100);
        Description = Guard.OptionalText(description, "La descripción", 200);
    }

    /// <summary>El SIN respondió: se limpia el contador de fallos.</summary>
    public void RecordContact(DateTimeOffset now)
    {
        LastContactAt = now;
        ConsecutiveFailures = 0;
        LastError = null;
    }

    /// <summary>
    /// Registra un fallo de comunicación. Devuelve true si con este fallo corresponde pasar a fuera de línea (dos fallos
    /// seguidos en línea: «un par de reintentos» de la documentación).
    /// </summary>
    public bool RecordFailure(string error, DateTimeOffset now, int threshold = 2)
    {
        ConsecutiveFailures++;
        LastError = Guard.OptionalText(error.Length > 500 ? error[..500] : error, "El error", 500);
        if (Mode == SiatConnectionMode.Offline)
        {
            RetryAt = now.Add(RetryDelay(ConsecutiveFailures));
        }
        return Mode == SiatConnectionMode.Online && ConsecutiveFailures >= threshold;
    }

    /// <summary>Pasa a fuera de línea (evento automático de internet o de acceso al SIN).</summary>
    public void GoOffline(DateTimeOffset now)
    {
        Guard.That(!IsClosed, "siat.pos_closed", "El punto de venta está cerrado.");
        if (Mode == SiatConnectionMode.Offline)
        {
            return;
        }
        Mode = SiatConnectionMode.Offline;
        ModeSince = now;
        RetryAt = now.Add(RetryDelay(1));
    }

    /// <summary>El usuario declara una contingencia manual (energía, software o hardware: facturas con CAFC).</summary>
    public void StartManualContingency(DateTimeOffset now)
    {
        Guard.That(!IsClosed, "siat.pos_closed", "El punto de venta está cerrado.");
        Guard.That(Mode == SiatConnectionMode.Online, "siat.mode", "Solo se declara una contingencia manual estando en línea.");
        Mode = SiatConnectionMode.ManualContingency;
        ModeSince = now;
        RetryAt = null;
    }

    /// <summary>Volvió la comunicación: CUFD nuevo, registro del evento y envío de paquetes en curso.</summary>
    public void StartRecovery(DateTimeOffset now)
    {
        if (Mode is SiatConnectionMode.Online or SiatConnectionMode.Recovering)
        {
            return;
        }
        Mode = SiatConnectionMode.Recovering;
        ModeSince = now;
        RetryAt = null;
    }

    /// <summary>Recuperación terminada (evento registrado y paquetes enviados): de nuevo en línea.</summary>
    public void BackOnline(DateTimeOffset now)
    {
        Mode = SiatConnectionMode.Online;
        ModeSince = now;
        ConsecutiveFailures = 0;
        RetryAt = null;
        LastContactAt = now;
        LastError = null;
    }

    /// <summary>Cierre definitivo en el SIN (<c>cierrePuntoVenta</c>).</summary>
    public void Close(DateTimeOffset now)
    {
        Guard.That(Code > 0, "siat.pos_zero", "El punto 0 (sin punto de venta) no se cierra.");
        ClosedAt = now;
    }

    /// <summary>Espera creciente entre verificaciones fuera de línea: 1, 2, 4… minutos, nunca más de 2 horas.</summary>
    public static TimeSpan RetryDelay(int attempt)
    {
        var minutes = Math.Min(Math.Pow(2, Math.Max(0, attempt - 1)), FiscalRules.MaxOfflineRetryInterval.TotalMinutes);
        return TimeSpan.FromMinutes(minutes);
    }
}

/// <summary>V4.1 · Código Único de Inicio de Sistemas (vigencia 365 días) de un punto de venta. Hecho inmutable: el
/// vigente es el último no vencido.</summary>
public sealed class SiatCuis : Entity, IBranchScoped, IAppendOnly
{
    private SiatCuis()
    {
    }

    public SiatCuis(Guid tenantId, Guid branchId, Guid pointOfSaleId, string code, DateTimeOffset validUntil, DateTimeOffset obtainedAt)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        PointOfSaleId = Guard.NotEmpty(pointOfSaleId, nameof(pointOfSaleId));
        Code = Guard.Text(code, "El CUIS", 100);
        Guard.That(validUntil > obtainedAt, "siat.cuis_validity", "La vigencia del CUIS debe ser posterior a su obtención.");
        ValidUntil = validUntil;
        ObtainedAt = obtainedAt;
    }

    public Guid BranchId { get; private set; }

    public Guid PointOfSaleId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public DateTimeOffset ValidUntil { get; private set; }

    public DateTimeOffset ObtainedAt { get; private set; }

    public bool IsValidAt(DateTimeOffset now) => now < ValidUntil;

    /// <summary>Se puede renovar desde el 5.º día anterior al vencimiento.</summary>
    public bool IsRenewableAt(DateTimeOffset now) => now >= ValidUntil.AddDays(-5);
}

/// <summary>
/// V4.1 · Código Único de Facturación Diaria (24 h) de un punto de venta, con el CÓDIGO DE CONTROL que se concatena al
/// CUF y la DIRECCIÓN que va en el XML. Hecho inmutable: se pide uno nuevo cada día y antes de registrar un evento.
/// </summary>
public sealed class SiatCufd : Entity, IBranchScoped, IAppendOnly
{
    private SiatCufd()
    {
    }

    public SiatCufd(Guid tenantId, Guid branchId, Guid pointOfSaleId, Guid cuisId, string code, string controlCode, string address,
        DateTimeOffset validUntil, DateTimeOffset obtainedAt)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        PointOfSaleId = Guard.NotEmpty(pointOfSaleId, nameof(pointOfSaleId));
        CuisId = Guard.NotEmpty(cuisId, nameof(cuisId));
        Code = Guard.Text(code, "El CUFD", 100);
        ControlCode = Guard.Text(controlCode, "El código de control del CUFD", 50);
        Address = Guard.Text(address, "La dirección del CUFD", 500);
        Guard.That(validUntil > obtainedAt, "siat.cufd_validity", "La vigencia del CUFD debe ser posterior a su obtención.");
        ValidUntil = validUntil;
        ObtainedAt = obtainedAt;
    }

    public Guid BranchId { get; private set; }

    public Guid PointOfSaleId { get; private set; }

    public Guid CuisId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string ControlCode { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public DateTimeOffset ValidUntil { get; private set; }

    public DateTimeOffset ObtainedAt { get; private set; }

    public bool IsValidAt(DateTimeOffset now) => now < ValidUntil;

    /// <summary>Usable fuera de línea cuando el servicio de CUFD no responde: hasta 72 h desde su obtención.</summary>
    public bool IsUsableOfflineAt(DateTimeOffset now) => now < ObtainedAt.Add(FiscalRules.CufdExtendedValidity);
}
