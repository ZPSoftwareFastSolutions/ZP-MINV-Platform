using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Infrastructure.Billing.Soap;

/// <summary>
/// V4.1 · Contrato SOAP de los servicios del SIN en UN solo lugar: sobre, cabeceras, recursos, operaciones, parámetros,
/// campos y elementos de respuesta. Lo usan el cliente (<see cref="SiatSoapGateway"/>) y el simulador
/// (<c>Billing/Simulator</c>), así que un cambio del contrato se hace aquí y vale para ambos (regla F-16).
/// <para>
/// FUERA DE DOC: confirmar con el WSDL del piloto. La documentación del SIN NO publica el WSDL, el namespace, la versión
/// de SOAP ni los nombres exactos de operaciones y elementos (hueco H-01, investigación 00 §2.6). Todo lo de este archivo
/// es la práctica conocida de los servicios v2 del SIN: SOAP 1.1, operaciones en lowerCamelCase con el prefijo del
/// namespace, un parámetro <c>Solicitud…</c> sin namespace y la cabecera HTTP <c>apikey: TokenApi &lt;token&gt;</c>
/// (esta última sí documentada). Si el WSDL real difiere, se ajustan estas constantes sin tocar la lógica.
/// </para>
/// </summary>
public static class SiatSoapContract
{
    // ------------------------------------------------------------------------------------------------ sobre y cabeceras
    /// <summary>SOAP 1.1 (FUERA DE DOC).</summary>
    public const string EnvelopeNamespace = "http://schemas.xmlsoap.org/soap/envelope/";

    /// <summary>Namespace de los servicios cuando la configuración no trae otro (FUERA DE DOC).</summary>
    public const string DefaultNamespace = "https://siat.impuestos.gob.bo/";

    /// <summary>Prefijo del elemento de operación en el cuerpo.</summary>
    public const string BodyPrefix = "siat";

    public const string MediaType = "text/xml";

    public const string ContentType = "text/xml; charset=utf-8";

    /// <summary>SOAP 1.1 exige la cabecera SOAPAction; los servicios del SIN la aceptan vacía (FUERA DE DOC).</summary>
    public const string SoapActionHeader = "SOAPAction";

    public const string SoapActionValue = "\"\"";

    /// <summary>Cabecera del token delegado (documentada: <c>headers.put("apikey", "TokenApi " + token)</c>).</summary>
    public const string ApiKeyHeader = "apikey";

    public const string ApiKeyScheme = "TokenApi";

    /// <summary>Valor de la cabecera <c>apikey</c> (con UN espacio después de TokenApi).</summary>
    public static string ApiKeyValue(string token) => ApiKeyScheme + " " + token;

    /// <summary>Token de una cabecera <c>apikey</c> («TokenApi &lt;token&gt;»), o null si falta o tiene otro formato.</summary>
    public static string? TokenFrom(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }
        var value = header.Trim();
        if (!value.StartsWith(ApiKeyScheme + " ", StringComparison.Ordinal))
        {
            return null;
        }
        var token = value[(ApiKeyScheme.Length + 1)..].Trim();
        return token.Length == 0 ? null : token;
    }

    // ------------------------------------------------------------------------------------------------ recursos
    /// <summary>Último segmento de la URL de cada recurso (patrón habitual <c>…/v2/&lt;Servicio&gt;</c>, FUERA DE DOC).
    /// Coincide con <see cref="SiatEndpointSet.ForBaseUrl"/> y con las rutas del simulador.</summary>
    public static string ResourcePath(SiatResource resource) => resource switch
    {
        SiatResource.Codes => "FacturacionCodigos",
        SiatResource.Sync => "FacturacionSincronizacion",
        SiatResource.Operations => "FacturacionOperaciones",
        SiatResource.PurchaseSale => "ServicioFacturacionCompraVenta",
        SiatResource.Computerized => "ServicioFacturacionComputarizada",
        SiatResource.Adjustment => "ServicioFacturacionDocumentoAjuste",
        _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null),
    };

    /// <summary>Recurso de un segmento de ruta (sin distinguir mayúsculas), o null.</summary>
    public static SiatResource? ResourceFromPath(string? path)
    {
        var segment = (path ?? string.Empty).Trim().TrimEnd('/');
        segment = segment[(segment.LastIndexOf('/') + 1)..];
        foreach (var resource in Enum.GetValues<SiatResource>())
        {
            if (string.Equals(ResourcePath(resource), segment, StringComparison.OrdinalIgnoreCase))
            {
                return resource;
            }
        }
        return null;
    }

    /// <summary>URL configurada de un recurso.</summary>
    public static string Url(SiatEndpointSet endpoints, SiatResource resource)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return resource switch
        {
            SiatResource.Codes => endpoints.Codes,
            SiatResource.Sync => endpoints.Sync,
            SiatResource.Operations => endpoints.Operations,
            SiatResource.PurchaseSale => endpoints.PurchaseSale,
            SiatResource.Computerized => endpoints.Computerized,
            SiatResource.Adjustment => endpoints.Adjustment,
            _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null),
        };
    }

    /// <summary>Namespace del cuerpo: el configurado o <see cref="DefaultNamespace"/>.</summary>
    public static string Namespace(SiatEndpointSet endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return string.IsNullOrWhiteSpace(endpoints.Namespace) ? DefaultNamespace : endpoints.Namespace.Trim();
    }

    // ------------------------------------------------------------------------------------------------ operaciones
    /// <summary>Operación SOAP: elemento del cuerpo (con el prefijo del namespace), elemento del parámetro (sin namespace)
    /// y elemento de la respuesta que escribe el simulador (el cliente la lee por nombre local, sin depender de él).</summary>
    public sealed record Operation(string Name, string? Parameter, string Response);

    public static readonly Operation VerifyCommunication = new("verificarComunicacion", null, "RespuestaComunicacion");

    // Recurso Códigos
    public static readonly Operation Cuis = new("cuis", "SolicitudCuis", "RespuestaCuis");
    public static readonly Operation Cufd = new("cufd", "SolicitudCufd", "RespuestaCufd");
    public static readonly Operation VerifyNit = new("verificarNit", "SolicitudVerificarNit", "RespuestaVerificarNit");

    // Recurso Operaciones
    public static readonly Operation RegisterPointOfSale = new("registroPuntoVenta", "SolicitudRegistroPuntoVenta", "RespuestaRegistroPuntoVenta");
    public static readonly Operation ListPointsOfSale = new("consultaPuntoVenta", "SolicitudConsultaPuntoVenta", "RespuestaConsultaPuntoVenta");
    public static readonly Operation ClosePointOfSale = new("cierrePuntoVenta", "SolicitudCierrePuntoVenta", "RespuestaCierrePuntoVenta");
    public static readonly Operation RegisterEvent = new("registroEventoSignificativo", "SolicitudEventoSignificativo", "RespuestaListaEventos");

    /// <summary>Respuesta común de los servicios de facturación (recepción, paquetes, anulación, reversión, estado).</summary>
    public const string ServiceResponse = "RespuestaServicioFacturacion";

    /// <summary>Operaciones de envío de un recurso de facturación (las notas no tienen paquetes: <see cref="Package"/> null).</summary>
    public sealed record DocumentOperations(Operation Reception, Operation? Package, Operation? PackageValidation, Operation Void, Operation Revert,
        Operation Status);

    /// <summary>Compra Venta (sector 1) y Facturación Computarizada (demás sectores): mismos nombres (investigación 02 §2.1).
    /// Los objetos de solicitud son los documentados.</summary>
    public static readonly DocumentOperations Invoices = new(
        new Operation("recepcionFactura", "SolicitudServicioRecepcionFactura", ServiceResponse),
        new Operation("recepcionPaqueteFactura", "SolicitudServicioRecepcionPaquete", ServiceResponse),
        new Operation("validacionRecepcionPaqueteFactura", "SolicitudServicioValidacionRecepcionPaquete", ServiceResponse),
        new Operation("anulacionFactura", "SolicitudServicioAnulacionFactura", ServiceResponse),
        new Operation("reversionAnulacionFactura", "SolicitudServicioReversionAnulacionFactura", ServiceResponse),
        new Operation("verificacionEstadoFactura", "SolicitudServicioVerificaEstadoFactura", ServiceResponse));

    /// <summary>Documentos de Ajuste (notas crédito-débito, tipo 3): sin paquetes ni masiva.</summary>
    public static readonly DocumentOperations Adjustments = new(
        new Operation("recepcionDocumentoAjuste", "SolicitudServicioRecepcionDocumentoAjuste", ServiceResponse),
        null,
        null,
        new Operation("anulacionDocumentoAjuste", "SolicitudServicioAnulacionDocumentoAjuste", ServiceResponse),
        new Operation("reversionAnulacionDocumentoAjuste", "SolicitudServicioReversionAnulacionDocumentoAjuste", ServiceResponse),
        new Operation("verificacionEstadoDocumentoAjuste", "SolicitudServicioVerificacionEstadoDocumentoAjuste", ServiceResponse));

    public static DocumentOperations Documents(SiatResource resource) => resource switch
    {
        SiatResource.PurchaseSale or SiatResource.Computerized => Invoices,
        SiatResource.Adjustment => Adjustments,
        _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, "El recurso no recibe documentos."),
    };

    // ------------------------------------------------------------------------------------------------ sincronización
    /// <summary>Forma de la respuesta de una sincronización (qué lista y qué campos trae).</summary>
    public enum SyncShape
    {
        /// <summary>listaCodigos { codigoClasificador, descripcion } (paramétricas y mensajes de servicios).</summary>
        Parametric,

        /// <summary>listaActividades { codigoCaeb, descripcion, tipoActividad }.</summary>
        Activities,

        /// <summary>listaActividadesDocumentoSector { codigoActividad, codigoDocumentoSector, tipoDocumentoSector }.</summary>
        ActivitySectors,

        /// <summary>listaLeyendas { codigoActividad, descripcionLeyenda }.</summary>
        Legends,

        /// <summary>listaCodigos { codigoActividad, codigoProducto, descripcionProducto, nandina }.</summary>
        Products,

        /// <summary>fechaHora (yyyy-MM-ddTHH:mm:ss.fff, hora de Bolivia sin zona).</summary>
        Clock,
    }

    /// <summary>Operación de sincronización de un catálogo de <see cref="SiatCatalogNames"/>.</summary>
    public sealed record SyncOperation(string Catalog, Operation Operation, SyncShape Shape);

    public const string SyncParameter = "SolicitudSincronizacion";

    /// <summary>Los 18 servicios de sincronización (nombres de operación FUERA DE DOC: investigación 04 §3.2).</summary>
    public static readonly IReadOnlyList<SyncOperation> SyncOperations =
    [
        Sync(SiatCatalogNames.Activities, "sincronizarActividades", "RespuestaListaActividades", SyncShape.Activities),
        Sync(SiatCatalogNames.DateTime, "sincronizarFechaHora", "RespuestaFechaHora", SyncShape.Clock),
        Sync(SiatCatalogNames.ActivitySectors, "sincronizarListaActividadesDocumentoSector", "RespuestaListaActividadesDocumentoSector",
            SyncShape.ActivitySectors),
        Sync(SiatCatalogNames.Legends, "sincronizarListaLeyendasFactura", "RespuestaListaParametricasLeyendas", SyncShape.Legends),
        Sync(SiatCatalogNames.ServiceMessages, "sincronizarListaMensajesServicios", "RespuestaListaParametricas", SyncShape.Parametric),
        Sync(SiatCatalogNames.Products, "sincronizarListaProductosServicios", "RespuestaListaProductos", SyncShape.Products),
        Sync(SiatCatalogNames.SignificantEvents, "sincronizarParametricaEventosSignificativos", "RespuestaListaParametricas", SyncShape.Parametric),
        Sync(SiatCatalogNames.VoidReasons, "sincronizarParametricaMotivoAnulacion", "RespuestaListaParametricas", SyncShape.Parametric),
        Sync(SiatCatalogNames.Countries, "sincronizarParametricaPaisOrigen", "RespuestaListaParametricas", SyncShape.Parametric),
        Sync(SiatCatalogNames.IdentityDocumentTypes, "sincronizarParametricaTipoDocumentoIdentidad", "RespuestaListaParametricas",
            SyncShape.Parametric),
        Sync(SiatCatalogNames.DocumentSectorTypes, "sincronizarParametricaTipoDocumentoSector", "RespuestaListaParametricas", SyncShape.Parametric),
        Sync(SiatCatalogNames.EmissionTypes, "sincronizarParametricaTipoEmision", "RespuestaListaParametricas", SyncShape.Parametric),
        Sync(SiatCatalogNames.RoomTypes, "sincronizarParametricaTipoHabitacion", "RespuestaListaParametricas", SyncShape.Parametric),
        Sync(SiatCatalogNames.PaymentMethods, "sincronizarParametricaTipoMetodoPago", "RespuestaListaParametricas", SyncShape.Parametric),
        Sync(SiatCatalogNames.Currencies, "sincronizarParametricaTipoMoneda", "RespuestaListaParametricas", SyncShape.Parametric),
        Sync(SiatCatalogNames.PointOfSaleTypes, "sincronizarParametricaTipoPuntoVenta", "RespuestaListaParametricas", SyncShape.Parametric),
        Sync(SiatCatalogNames.InvoiceTypes, "sincronizarParametricaTiposFactura", "RespuestaListaParametricas", SyncShape.Parametric),
        Sync(SiatCatalogNames.UnitsOfMeasure, "sincronizarParametricaUnidadMedida", "RespuestaListaParametricas", SyncShape.Parametric),
    ];

    /// <summary>Operación de sincronización de un catálogo (nombre de <see cref="SiatCatalogNames"/>).</summary>
    public static SyncOperation SyncFor(string catalog)
    {
        var name = (catalog ?? string.Empty).Trim().ToUpperInvariant();
        return SyncOperations.FirstOrDefault(s => s.Catalog == name)
               ?? throw new DomainException("siat.catalog_unknown", $"El catálogo «{catalog}» no es uno de los 18 servicios de sincronización del SIN.");
    }

    /// <summary>Sincronización de una operación SOAP (para el simulador), o null.</summary>
    public static SyncOperation? SyncByOperation(string operationName) =>
        SyncOperations.FirstOrDefault(s => s.Operation.Name == operationName);

    private static SyncOperation Sync(string catalog, string operation, string response, SyncShape shape) =>
        new(catalog, new Operation(operation, SyncParameter, response), shape);

    // ------------------------------------------------------------------------------------------------ operaciones por recurso
    /// <summary>Nombres de las operaciones que publica cada recurso (<c>verificarComunicacion</c> existe en todos: 02 §2.1).</summary>
    public static IReadOnlyList<string> OperationsOf(SiatResource resource)
    {
        IEnumerable<Operation> operations = resource switch
        {
            SiatResource.Codes => [Cuis, Cufd, VerifyNit],
            SiatResource.Sync => SyncOperations.Select(s => s.Operation),
            SiatResource.Operations => [RegisterPointOfSale, ListPointsOfSale, ClosePointOfSale, RegisterEvent],
            _ => DocumentOperationList(Documents(resource)),
        };
        return [VerifyCommunication.Name, .. operations.Select(o => o.Name)];
    }

    private static IEnumerable<Operation> DocumentOperationList(DocumentOperations d) =>
        new[] { d.Reception, d.Package, d.PackageValidation, d.Void, d.Revert, d.Status }.OfType<Operation>();

    // ------------------------------------------------------------------------------------------------ campos
    /// <summary>Campos de las solicitudes y respuestas (minúscula camel, sin namespace).</summary>
    public static class Fields
    {
        // Solicitudes
        public const string Environment = "codigoAmbiente";
        public const string Modality = "codigoModalidad";
        public const string PointOfSale = "codigoPuntoVenta";
        public const string SystemCode = "codigoSistema";
        public const string Branch = "codigoSucursal";
        public const string Nit = "nit";
        public const string Cuis = "cuis";
        public const string Cufd = "cufd";
        public const string NitToVerify = "nitParaVerificacion";
        public const string PointOfSaleType = "codigoTipoPuntoVenta";
        public const string Description = "descripcion";
        public const string PointOfSaleName = "nombrePuntoVenta";
        public const string EventReason = "codigoMotivoEvento";
        public const string EventCufd = "cufdEvento";
        public const string EventEnd = "fechaHoraFinEvento";
        public const string EventStart = "fechaHoraInicioEvento";
        public const string DocumentSector = "codigoDocumentoSector";
        public const string Emission = "codigoEmision";
        public const string DocumentType = "tipoFacturaDocumento";
        public const string File = "archivo";
        public const string SentAt = "fechaEnvio";
        public const string FileHash = "hashArchivo";
        public const string Cafc = "cafc";
        public const string DocumentCount = "cantidadFacturas";
        public const string EventCode = "codigoEvento";
        public const string ReceptionCode = "codigoRecepcion";
        public const string VoidReason = "codigoMotivo";
        public const string Cuf = "cuf";

        // Respuestas
        public const string Transaction = "transaccion";
        public const string Messages = "mensajesList";
        public const string Code = "codigo";
        public const string MessageDescription = "descripcion";
        public const string FileNumber = "numeroArchivo";
        public const string DetailNumber = "numeroDetalle";
        public const string ValidUntil = "fechaVigencia";
        public const string ControlCode = "codigoControl";
        public const string Address = "direccion";
        public const string Status = "codigoEstado";
        public const string StatusDescription = "codigoDescripcion";
        public const string PointsOfSale = "listaPuntosVentas";
        public const string PointOfSaleTypeName = "tipoPuntoVenta";
        public const string EventReceptionCode = "codigoRecepcionEventoSignificativo";
        public const string Return = "return";
        public const string DateTime = "fechaHora";
        public const string Activities = "listaActividades";
        public const string ActivityCaeb = "codigoCaeb";
        public const string ActivityType = "tipoActividad";
        public const string ActivitySectors = "listaActividadesDocumentoSector";
        public const string Activity = "codigoActividad";
        public const string SectorType = "tipoDocumentoSector";
        public const string Legends = "listaLeyendas";
        public const string LegendText = "descripcionLeyenda";
        public const string Codes = "listaCodigos";
        public const string Product = "codigoProducto";
        public const string ProductDescription = "descripcionProducto";
        public const string Nandina = "nandina";
        public const string Classifier = "codigoClasificador";
    }

    /// <summary>Nombres con que puede venir la lista de mensajes (la documentación usa varios: 02 §4). El cliente acepta
    /// todos; el simulador escribe <see cref="Fields.Messages"/>.</summary>
    public static readonly IReadOnlyList<string> MessageListNames =
        [Fields.Messages, "codigosRespuestas", "codigosRespuesta", "CodigosRespuestas", "todigosRespuestas", "mensajes"];

    /// <summary>Nombres alternativos del código de CUIS / CUFD en la respuesta.</summary>
    public static readonly IReadOnlyList<string> CodeNames = [Fields.Code, "codigoCUIS", "codigoCuis", "codigoCUFD", "codigoCufd"];

    /// <summary>Listas de elementos de las respuestas (sus campos no se confunden con los del nivel superior).</summary>
    public static readonly IReadOnlyList<string> ListNames =
        [.. MessageListNames, Fields.PointsOfSale, Fields.Activities, Fields.ActivitySectors, Fields.Legends, Fields.Codes, "listaEventos"];

    /// <summary>Formato de las fechas de vigencia que escribe el simulador (hora de Bolivia con su zona).</summary>
    public const string ValidityFormat = "yyyy-MM-dd'T'HH:mm:ss.fffzzz";

    /// <summary>Zona horaria de Bolivia (UTC−4, sin horario de verano).</summary>
    public static readonly TimeSpan BoliviaOffset = TimeSpan.FromHours(-4);
}
