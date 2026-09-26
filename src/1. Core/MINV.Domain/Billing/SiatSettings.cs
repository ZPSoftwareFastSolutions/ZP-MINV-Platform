using MINV.Domain.Common;

namespace MINV.Domain.Billing;

/// <summary>
/// V4.1 · Configuración de facturación de la empresa (una fila por tenant): datos del Padrón y del sistema autorizado,
/// ambiente activo, leyendas de modo y desfase del reloj con el SIN.
/// </summary>
public sealed class SiatSettings : BaseEntity, IConcurrencyAware
{
    /// <summary>Leyenda 3 de la representación gráfica cuando el documento se emitió en línea.</summary>
    public const string DefaultOnlineLegend =
        "“Este documento es la Representación Gráfica de un Documento Fiscal Digital emitido en una modalidad de facturación en línea”";

    /// <summary>Leyenda 3 cuando se emitió fuera de línea (texto configurable: la documentación no lo publica).</summary>
    public const string DefaultOfflineLegend =
        "“Este documento es la Representación Gráfica de un Documento Fiscal Digital emitido fuera de línea, verifique su envío con su proveedor o en la página web www.impuestos.gob.bo”";

    /// <summary>Leyenda 1 (fija) de toda factura.</summary>
    public const string FixedLegend = "ESTA FACTURA CONTRIBUYE AL DESARROLLO DEL PAÍS, EL USO ILÍCITO SERÁ SANCIONADO PENALMENTE DE ACUERDO A LEY";

    private SiatSettings()
    {
    }

    public SiatSettings(Guid tenantId, long nit, string businessName, string systemCode, int environment)
        : base(tenantId)
    {
        Update(nit, businessName, systemCode, environment, DefaultOnlineLegend, DefaultOfflineLegend);
        IsEnabled = false;
    }

    /// <summary>NIT del emisor (Padrón Nacional de Contribuyentes).</summary>
    public long Nit { get; private set; }

    /// <summary>Razón social del emisor tal como figura en el Padrón.</summary>
    public string BusinessName { get; private set; } = string.Empty;

    /// <summary>Código del sistema asignado por el SIN al registrar el sistema de facturación.</summary>
    public string SystemCode { get; private set; } = string.Empty;

    /// <summary>Ambiente activo: 1 producción, 2 pruebas y piloto.</summary>
    public int Environment { get; private set; }

    /// <summary>Modalidad: siempre 2 (Computarizada en Línea) en la V4.1.</summary>
    public int Modality { get; private set; } = SiatCodes.ModalityComputerized;

    /// <summary>La emisión fiscal está activa (si no, las ventas siguen sin documento fiscal).</summary>
    public bool IsEnabled { get; private set; }

    public string OnlineLegend { get; private set; } = DefaultOnlineLegend;

    public string OfflineLegend { get; private set; } = DefaultOfflineLegend;

    /// <summary>Diferencia (ms) entre el reloj del SIN y el del servidor, medida con la sincronización de fecha y hora.</summary>
    public long ClockOffsetMs { get; private set; }

    public DateTimeOffset? ClockSyncedAt { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public void Update(long nit, string businessName, string systemCode, int environment, string onlineLegend, string offlineLegend)
    {
        Guard.That(nit is > 0 and <= 9_999_999_999_999, "siat.nit", "El NIT tiene entre 1 y 13 dígitos.");
        Guard.That(environment is SiatCodes.EnvironmentProduction or SiatCodes.EnvironmentTest, "siat.environment",
            "El ambiente es 1 (producción) o 2 (pruebas y piloto).");
        Nit = nit;
        BusinessName = Guard.Text(businessName, "La razón social", 200);
        SystemCode = Guard.Text(systemCode, "El código de sistema", 50);
        Environment = environment;
        OnlineLegend = Guard.Text(onlineLegend, "La leyenda de emisión en línea", 250);
        OfflineLegend = Guard.Text(offlineLegend, "La leyenda de emisión fuera de línea", 250);
    }

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;

    /// <summary>Registra la hora del SIN recibida en la sincronización: desde ahí la hora fiscal = hora del servidor + desfase.</summary>
    public void SynchronizeClock(DateTimeOffset serverUtcNow, DateTimeOffset siatNow)
    {
        ClockOffsetMs = (long)(siatNow - serverUtcNow).TotalMilliseconds;
        ClockSyncedAt = serverUtcNow;
    }

    /// <summary>Hora actual según el SIN (UTC).</summary>
    public DateTimeOffset SiatNow(DateTimeOffset serverUtcNow) => serverUtcNow.AddMilliseconds(ClockOffsetMs);
}

/// <summary>
/// V4.1 · Datos de conexión por ambiente: token delegado cifrado (AES-256-GCM con la clave maestra de integraciones; el
/// texto plano nunca se guarda ni se registra), vigencia del token, URL de cada recurso SOAP, namespace y URL del QR.
/// Nada de esto está fijo en el código: el SIN entrega las URL al registrar el sistema y al iniciar operaciones.
/// </summary>
public sealed class SiatEnvironmentProfile : Entity, IConcurrencyAware
{
    private SiatEnvironmentProfile()
    {
    }

    public SiatEnvironmentProfile(Guid tenantId, int environment, SiatEndpointSet endpoints, string qrBaseUrl, int timeoutSeconds)
        : base(tenantId)
    {
        Guard.That(environment is SiatCodes.EnvironmentProduction or SiatCodes.EnvironmentTest, "siat.environment",
            "El ambiente es 1 (producción) o 2 (pruebas y piloto).");
        Environment = environment;
        UpdateEndpoints(endpoints, qrBaseUrl, timeoutSeconds);
    }

    public int Environment { get; private set; }

    public string CodesUrl { get; private set; } = string.Empty;

    public string SyncUrl { get; private set; } = string.Empty;

    public string OperationsUrl { get; private set; } = string.Empty;

    public string PurchaseSaleUrl { get; private set; } = string.Empty;

    public string ComputerizedUrl { get; private set; } = string.Empty;

    public string AdjustmentUrl { get; private set; } = string.Empty;

    /// <summary>Namespace de los servicios SOAP (p. ej. https://siat.impuestos.gob.bo/).</summary>
    public string Namespace { get; private set; } = string.Empty;

    /// <summary>URL base de la consulta por QR (…/consulta/QR).</summary>
    public string QrBaseUrl { get; private set; } = string.Empty;

    /// <summary>Tiempo máximo de espera de cada llamada (segundos): pasado, se considera sin comunicación.</summary>
    public int TimeoutSeconds { get; private set; }

    public string? TokenCiphertext { get; private set; }

    public string? TokenKeyId { get; private set; }

    /// <summary>Vigencia del token delegado («Hasta» elegido en el Portal SIAT).</summary>
    public DateOnly? TokenValidUntil { get; private set; }

    public DateTimeOffset? TokenUpdatedAt { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public bool HasToken => TokenCiphertext is not null;

    public SiatEndpointSet Endpoints => new(CodesUrl, SyncUrl, OperationsUrl, PurchaseSaleUrl, ComputerizedUrl, AdjustmentUrl, Namespace);

    public void UpdateEndpoints(SiatEndpointSet endpoints, string qrBaseUrl, int timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        CodesUrl = Url(endpoints.Codes, "La URL del servicio de Códigos");
        SyncUrl = Url(endpoints.Sync, "La URL del servicio de Sincronización");
        OperationsUrl = Url(endpoints.Operations, "La URL del servicio de Operaciones");
        PurchaseSaleUrl = Url(endpoints.PurchaseSale, "La URL del servicio de Compra Venta");
        ComputerizedUrl = Url(endpoints.Computerized, "La URL del servicio de Facturación Computarizada");
        AdjustmentUrl = Url(endpoints.Adjustment, "La URL del servicio de Documentos de Ajuste");
        Namespace = Guard.Text(endpoints.Namespace, "El namespace", 200);
        QrBaseUrl = Url(qrBaseUrl, "La URL del QR");
        Guard.That(timeoutSeconds is >= 3 and <= 120, "siat.timeout", "El tiempo de espera va de 3 a 120 segundos.");
        TimeoutSeconds = timeoutSeconds;
    }

    /// <summary>Guarda el token YA CIFRADO (lo cifra la aplicación con ISecretProtector).</summary>
    public void SetToken(string ciphertext, string keyId, DateOnly? validUntil, DateTimeOffset now)
    {
        TokenCiphertext = Guard.Text(ciphertext, "El token cifrado", 8000);
        TokenKeyId = Guard.Text(keyId, "La clave del token", 40);
        TokenValidUntil = validUntil;
        TokenUpdatedAt = now;
    }

    private static string Url(string? value, string name)
    {
        var url = Guard.Text(value, name, 400);
        Guard.That(Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback),
            "siat.url", $"{name} debe ser https (http solo en este mismo equipo).");
        return url;
    }
}

/// <summary>URL de los recursos SOAP del SIN (una por servicio) y su namespace.</summary>
public sealed record SiatEndpointSet(string Codes, string Sync, string Operations, string PurchaseSale, string Computerized,
    string Adjustment, string Namespace)
{
    /// <summary>Patrón conocido de los servicios del SIN (NO publicado en la documentación: se confirma con el WSDL del
    /// piloto y se edita en la configuración).</summary>
    public static SiatEndpointSet ForBaseUrl(string baseUrl, string ns = "https://siat.impuestos.gob.bo/")
    {
        var b = baseUrl.TrimEnd('/');
        return new SiatEndpointSet($"{b}/v2/FacturacionCodigos", $"{b}/v2/FacturacionSincronizacion", $"{b}/v2/FacturacionOperaciones",
            $"{b}/v2/ServicioFacturacionCompraVenta", $"{b}/v2/ServicioFacturacionComputarizada", $"{b}/v2/ServicioFacturacionDocumentoAjuste", ns);
    }
}

/// <summary>V4.1 · Sucursal de M-INV ↔ sucursal del Padrón (0 = casa matriz), con los datos que van en la factura.</summary>
public sealed class SiatBranch : Entity
{
    private SiatBranch()
    {
    }

    public SiatBranch(Guid tenantId, Guid branchId, int siatCode, string municipality, string? phone)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        Update(siatCode, municipality, phone);
    }

    public Guid BranchId { get; private set; }

    /// <summary>codigoSucursal del Padrón: 0 = casa matriz, 1..n.</summary>
    public int SiatCode { get; private set; }

    /// <summary>Municipio o departamento que se refleja en la factura (1 a 25 caracteres).</summary>
    public string Municipality { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public void Update(int siatCode, string municipality, string? phone)
    {
        Guard.That(siatCode is >= 0 and <= 9999, "siat.branch_code", "El código de sucursal del Padrón va de 0 a 9999.");
        SiatCode = siatCode;
        Municipality = Guard.Text(municipality, "El municipio", 25);
        Phone = Guard.OptionalText(phone, "El teléfono", 25);
    }
}

/// <summary>V4.1 · Servidor de correo de la empresa para entregar el XML y la representación gráfica al comprador.</summary>
public sealed class MailSettings : BaseEntity, IConcurrencyAware
{
    private MailSettings()
    {
    }

    public MailSettings(Guid tenantId, string host, int port, bool useSsl, string? userName, string fromAddress, string fromName)
        : base(tenantId)
    {
        Update(host, port, useSsl, userName, fromAddress, fromName);
    }

    public string Host { get; private set; } = string.Empty;

    public int Port { get; private set; }

    public bool UseSsl { get; private set; }

    public string? UserName { get; private set; }

    public string? PasswordCiphertext { get; private set; }

    public string? PasswordKeyId { get; private set; }

    public string FromAddress { get; private set; } = string.Empty;

    public string FromName { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; } = true;

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public void Update(string host, int port, bool useSsl, string? userName, string fromAddress, string fromName)
    {
        Host = Guard.Text(host, "El servidor SMTP", 200);
        Guard.That(port is > 0 and <= 65535, "mail.port", "El puerto va de 1 a 65535.");
        Port = port;
        UseSsl = useSsl;
        UserName = Guard.OptionalText(userName, "El usuario", 200);
        FromAddress = Guard.Email(fromAddress, "El remitente");
        FromName = Guard.Text(fromName, "El nombre del remitente", 100);
    }

    public void SetPassword(string ciphertext, string keyId)
    {
        PasswordCiphertext = Guard.Text(ciphertext, "La contraseña cifrada", 4000);
        PasswordKeyId = Guard.Text(keyId, "La clave", 40);
    }

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;
}
