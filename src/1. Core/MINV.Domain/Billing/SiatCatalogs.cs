using MINV.Domain.Common;

namespace MINV.Domain.Billing;

/// <summary>Nombres estables de las paramétricas sincronizadas (clave de <see cref="SiatCatalogItem"/>).</summary>
public static class SiatCatalogNames
{
    public const string ServiceMessages = "MENSAJES_SERVICIOS";
    public const string SignificantEvents = "EVENTOS_SIGNIFICATIVOS";
    public const string VoidReasons = "MOTIVOS_ANULACION";
    public const string Countries = "PAIS_ORIGEN";
    public const string IdentityDocumentTypes = "TIPO_DOCUMENTO_IDENTIDAD";
    public const string DocumentSectorTypes = "TIPO_DOCUMENTO_SECTOR";
    public const string EmissionTypes = "TIPO_EMISION";
    public const string RoomTypes = "TIPO_HABITACION";
    public const string PaymentMethods = "TIPO_METODO_PAGO";
    public const string Currencies = "TIPO_MONEDA";
    public const string PointOfSaleTypes = "TIPO_PUNTO_VENTA";
    public const string InvoiceTypes = "TIPOS_FACTURA";
    public const string UnitsOfMeasure = "UNIDAD_MEDIDA";

    /// <summary>Las 13 paramétricas de forma (código, descripción).</summary>
    public static readonly IReadOnlyList<string> Parametric =
    [
        ServiceMessages, SignificantEvents, VoidReasons, Countries, IdentityDocumentTypes, DocumentSectorTypes, EmissionTypes,
        RoomTypes, PaymentMethods, Currencies, PointOfSaleTypes, InvoiceTypes, UnitsOfMeasure,
    ];

    // Catálogos con estructura propia (tablas propias)
    public const string Activities = "ACTIVIDADES";
    public const string ActivitySectors = "ACTIVIDADES_DOCUMENTO_SECTOR";
    public const string Legends = "LEYENDAS_FACTURA";
    public const string Products = "PRODUCTOS_SERVICIOS";
    public const string DateTime = "FECHA_HORA";

    /// <summary>Los 18 servicios de sincronización (13 paramétricas + 4 estructurados + fecha y hora).</summary>
    public static readonly IReadOnlyList<string> All =
        [.. Parametric, Activities, ActivitySectors, Legends, Products, DateTime];
}

/// <summary>
/// V4.1 · Valor de una paramétrica del SIN (métodos de pago, unidades, motivos de anulación, eventos…). Se actualiza con
/// la sincronización diaria; lo que el SIN retira queda con <see cref="IsCurrent"/> = false (nunca se borra: hay
/// documentos que lo usan).
/// </summary>
public sealed class SiatCatalogItem : Entity
{
    private SiatCatalogItem()
    {
    }

    public SiatCatalogItem(Guid tenantId, string catalog, int code, string description, DateTimeOffset syncedAt)
        : base(tenantId)
    {
        Catalog = Guard.Code(catalog, "El catálogo", 40);
        Code = Guard.NonNegative(code, "El código");
        Refresh(description, syncedAt);
    }

    public string Catalog { get; private set; } = string.Empty;

    public int Code { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public bool IsCurrent { get; private set; }

    public DateTimeOffset SyncedAt { get; private set; }

    public void Refresh(string description, DateTimeOffset syncedAt)
    {
        Description = Guard.Text(description, "La descripción", 500);
        IsCurrent = true;
        SyncedAt = syncedAt;
    }

    public void Retire() => IsCurrent = false;
}

/// <summary>V4.1 · Actividad económica del NIT (código CAEB; texto: conserva los ceros a la izquierda).</summary>
public sealed class SiatActivity : Entity
{
    private SiatActivity()
    {
    }

    public SiatActivity(Guid tenantId, string code, string description, string? activityType, DateTimeOffset syncedAt)
        : base(tenantId)
    {
        Code = Guard.Text(code, "El código de actividad", 10);
        Refresh(description, activityType, syncedAt);
    }

    public string Code { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>Tipo de actividad (P = principal, S = secundaria…, tal como lo devuelve el SIN).</summary>
    public string? ActivityType { get; private set; }

    public bool IsCurrent { get; private set; }

    public DateTimeOffset SyncedAt { get; private set; }

    public void Refresh(string description, string? activityType, DateTimeOffset syncedAt)
    {
        Description = Guard.Text(description, "La descripción", 500);
        ActivityType = Guard.OptionalText(activityType, "El tipo de actividad", 10);
        IsCurrent = true;
        SyncedAt = syncedAt;
    }

    public void Retire() => IsCurrent = false;
}

/// <summary>V4.1 · Documentos sector habilitados para una actividad del NIT.</summary>
public sealed class SiatActivitySector : Entity
{
    private SiatActivitySector()
    {
    }

    public SiatActivitySector(Guid tenantId, string activityCode, int documentSector, string? sectorType, DateTimeOffset syncedAt)
        : base(tenantId)
    {
        ActivityCode = Guard.Text(activityCode, "El código de actividad", 10);
        Guard.That(documentSector is >= 1 and <= 99, "siat.sector", "El documento sector va de 1 a 99.");
        DocumentSector = documentSector;
        Refresh(sectorType, syncedAt);
    }

    public string ActivityCode { get; private set; } = string.Empty;

    public int DocumentSector { get; private set; }

    public string? SectorType { get; private set; }

    public bool IsCurrent { get; private set; }

    public DateTimeOffset SyncedAt { get; private set; }

    public void Refresh(string? sectorType, DateTimeOffset syncedAt)
    {
        SectorType = Guard.OptionalText(sectorType, "El tipo de documento sector", 20);
        IsCurrent = true;
        SyncedAt = syncedAt;
    }

    public void Retire() => IsCurrent = false;
}

/// <summary>V4.1 · Leyenda de la Ley N° 453 asociada a una actividad (la factura lleva una al azar en cada emisión).</summary>
public sealed class SiatLegend : Entity
{
    private SiatLegend()
    {
    }

    public SiatLegend(Guid tenantId, string activityCode, string text, DateTimeOffset syncedAt)
        : base(tenantId)
    {
        ActivityCode = Guard.Text(activityCode, "El código de actividad", 10);
        Text = Guard.Text(text, "La leyenda", 200);
        IsCurrent = true;
        SyncedAt = syncedAt;
    }

    public string ActivityCode { get; private set; } = string.Empty;

    public string Text { get; private set; } = string.Empty;

    public bool IsCurrent { get; private set; }

    public DateTimeOffset SyncedAt { get; private set; }

    public void Confirm(DateTimeOffset syncedAt)
    {
        IsCurrent = true;
        SyncedAt = syncedAt;
    }

    public void Retire() => IsCurrent = false;
}

/// <summary>V4.1 · Producto o servicio genérico del SIN (homologación de los productos propios).</summary>
public sealed class SiatProduct : Entity
{
    private SiatProduct()
    {
    }

    public SiatProduct(Guid tenantId, string activityCode, int productCode, string description, DateTimeOffset syncedAt)
        : base(tenantId)
    {
        ActivityCode = Guard.Text(activityCode, "El código de actividad", 10);
        Guard.That(productCode is >= 1 and <= 99_999_999, "siat.product_code", "El código de producto SIN va de 1 a 99999999.");
        ProductCode = productCode;
        Refresh(description, syncedAt);
    }

    public string ActivityCode { get; private set; } = string.Empty;

    public int ProductCode { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public bool IsCurrent { get; private set; }

    public DateTimeOffset SyncedAt { get; private set; }

    public void Refresh(string description, DateTimeOffset syncedAt)
    {
        Description = Guard.Text(description, "La descripción del producto SIN", 1000);
        IsCurrent = true;
        SyncedAt = syncedAt;
    }

    public void Retire() => IsCurrent = false;
}

/// <summary>V4.1 · Registro inmutable de cada sincronización de un catálogo.</summary>
public sealed class SiatSyncRun : Entity, IAppendOnly
{
    private SiatSyncRun()
    {
    }

    public SiatSyncRun(Guid tenantId, int environment, string catalog, int items, string? error, DateTimeOffset occurredAt, Guid? userId)
        : base(tenantId)
    {
        Environment = environment;
        Catalog = Guard.Code(catalog, "El catálogo", 40);
        Items = Guard.NonNegative(items, "La cantidad de filas");
        Error = Guard.OptionalText(error is { Length: > 1000 } ? error[..1000] : error, "El error", 1000);
        OccurredAt = occurredAt;
        UserId = Guard.NotEmptyIfPresent(userId, nameof(userId));
    }

    public int Environment { get; private set; }

    public string Catalog { get; private set; } = string.Empty;

    public int Items { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public Guid? UserId { get; private set; }

    public bool Succeeded => Error is null;
}

/// <summary>V4.1 · Homologación de un producto de M-INV con la actividad y el producto genérico del SIN.</summary>
public sealed class ProductSiatCode : Entity, IConcurrencyAware
{
    private ProductSiatCode()
    {
    }

    public ProductSiatCode(Guid tenantId, Guid productId, string activityCode, int sinProductCode)
        : base(tenantId)
    {
        ProductId = Guard.NotEmpty(productId, nameof(productId));
        Assign(activityCode, sinProductCode);
    }

    public Guid ProductId { get; private set; }

    public string ActivityCode { get; private set; } = string.Empty;

    public int SinProductCode { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public void Assign(string activityCode, int sinProductCode)
    {
        ActivityCode = Guard.Text(activityCode, "El código de actividad", 10);
        Guard.That(sinProductCode is >= 1 and <= 99_999_999, "siat.product_code", "El código de producto SIN va de 1 a 99999999.");
        SinProductCode = sinProductCode;
    }
}

/// <summary>V4.1 · Homologación de una unidad de medida de M-INV con la paramétrica «Unidad de Medida» del SIN.</summary>
public sealed class UnitSiatCode : Entity
{
    private UnitSiatCode()
    {
    }

    public UnitSiatCode(Guid tenantId, Guid unitId, int sinUnitCode)
        : base(tenantId)
    {
        UnitId = Guard.NotEmpty(unitId, nameof(unitId));
        Assign(sinUnitCode);
    }

    public Guid UnitId { get; private set; }

    public int SinUnitCode { get; private set; }

    public void Assign(int sinUnitCode)
    {
        Guard.That(sinUnitCode is >= 1 and <= 999, "siat.unit_code", "El código de unidad SIN no es válido.");
        SinUnitCode = sinUnitCode;
    }
}

/// <summary>V4.1 · Homologación de un medio de pago de M-INV con la paramétrica «Tipo Método Pago» del SIN.</summary>
public sealed class PaymentMethodSiatCode : Entity
{
    private PaymentMethodSiatCode()
    {
    }

    public PaymentMethodSiatCode(Guid tenantId, Guid paymentMethodId, int sinPaymentMethodCode)
        : base(tenantId)
    {
        PaymentMethodId = Guard.NotEmpty(paymentMethodId, nameof(paymentMethodId));
        Assign(sinPaymentMethodCode);
    }

    public Guid PaymentMethodId { get; private set; }

    public int SinPaymentMethodCode { get; private set; }

    public void Assign(int sinPaymentMethodCode)
    {
        Guard.That(sinPaymentMethodCode is >= 1 and <= 999, "siat.payment_code", "El código de método de pago SIN no es válido.");
        SinPaymentMethodCode = sinPaymentMethodCode;
    }
}

/// <summary>V4.1 · Verificación de un NIT contra el Padrón (<c>verificarNit</c>): hecho inmutable con el código del SIN.</summary>
public sealed class CustomerNitCheck : Entity, IAppendOnly
{
    private CustomerNitCheck()
    {
    }

    public CustomerNitCheck(Guid tenantId, Guid? customerId, long nit, int siatCode, bool isValid, string? description, DateTimeOffset checkedAt,
        Guid? userId)
        : base(tenantId)
    {
        CustomerId = Guard.NotEmptyIfPresent(customerId, nameof(customerId));
        Guard.That(nit > 0, "siat.nit", "El NIT verificado debe ser positivo.");
        Nit = nit;
        SiatCode = siatCode;
        IsValid = isValid;
        Description = Guard.OptionalText(description is { Length: > 300 } ? description[..300] : description, "La descripción", 300);
        CheckedAt = checkedAt;
        UserId = Guard.NotEmptyIfPresent(userId, nameof(userId));
    }

    public Guid? CustomerId { get; private set; }

    public long Nit { get; private set; }

    public int SiatCode { get; private set; }

    public bool IsValid { get; private set; }

    public string? Description { get; private set; }

    public DateTimeOffset CheckedAt { get; private set; }

    public Guid? UserId { get; private set; }
}

/// <summary>
/// V4.1 · Bitácora técnica inmutable de cada llamada SOAP al SIN (evidencia para la inspección y el soporte). NUNCA
/// contiene el token: la cabecera <c>apikey</c> no se guarda y los cuerpos se registran sin ella.
/// </summary>
public sealed class SiatServiceCall : Entity, IAppendOnly
{
    private SiatServiceCall()
    {
    }

    public SiatServiceCall(Guid tenantId, int environment, string resource, string operation, Guid? branchId, int? pointOfSaleCode,
        DateTimeOffset occurredAt, int durationMs, int? httpStatus, int? siatCode, bool succeeded, string? requestBody, string? responseBody,
        string? error)
        : base(tenantId)
    {
        Environment = environment;
        Resource = Guard.Text(resource, "El recurso", 60);
        Operation = Guard.Text(operation, "La operación", 80);
        BranchId = Guard.NotEmptyIfPresent(branchId, nameof(branchId));
        PointOfSaleCode = pointOfSaleCode;
        OccurredAt = occurredAt;
        DurationMs = Math.Max(0, durationMs);
        HttpStatus = httpStatus;
        SiatCode = siatCode;
        Succeeded = succeeded;
        RequestBody = Truncate(requestBody, 20000);
        ResponseBody = Truncate(responseBody, 20000);
        Error = Truncate(error, 1000);
    }

    public int Environment { get; private set; }

    public string Resource { get; private set; } = string.Empty;

    public string Operation { get; private set; } = string.Empty;

    public Guid? BranchId { get; private set; }

    public int? PointOfSaleCode { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public int DurationMs { get; private set; }

    public int? HttpStatus { get; private set; }

    public int? SiatCode { get; private set; }

    public bool Succeeded { get; private set; }

    public string? RequestBody { get; private set; }

    public string? ResponseBody { get; private set; }

    public string? Error { get; private set; }

    private static string? Truncate(string? value, int max) => value is null ? null : value.Length > max ? value[..max] : value;
}
