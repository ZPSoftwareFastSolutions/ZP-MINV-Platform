using MINV.Domain.Billing;

namespace MINV.Application.Abstractions;

/// <summary>V4.1 · Recursos SOAP del SIN (cada uno con su URL en <see cref="SiatEndpointSet"/>).</summary>
public enum SiatResource
{
    Codes,
    Sync,
    Operations,
    PurchaseSale,
    Computerized,
    Adjustment,
}

/// <summary>V4.1 · Credenciales y destino de una llamada: ambiente, NIT, sistema, token EN CLARO (solo en memoria) y URL.</summary>
public sealed record SiatConnection(int Environment, long Nit, string SystemCode, string Token, SiatEndpointSet Endpoints, TimeSpan Timeout,
    int Modality = SiatCodes.ModalityComputerized)
{
    /// <summary>Nunca imprime el token.</summary>
    public override string ToString() => $"SIAT ambiente {Environment} NIT {Nit} sistema {SystemCode}";
}

/// <summary>V4.1 · Sucursal del Padrón y punto de venta de la llamada (y la sucursal de M-INV para la bitácora).</summary>
public sealed record SiatPlace(int BranchCode, int PointOfSaleCode, Guid? BranchId = null);

/// <summary>V4.1 · CUIS y CUFD vigentes que acompañan las llamadas de facturación.</summary>
public sealed record SiatCodesForCall(string Cuis, string Cufd);

/// <summary>V4.1 · Documento sector y tipo de factura/documento de ajuste (deciden el recurso: sector 1 → Compra Venta,
/// tipo 3 → Documentos de Ajuste, el resto → Facturación Computarizada).</summary>
public sealed record SiatDocumentRef(int DocumentSector, int DocumentType)
{
    public static readonly SiatDocumentRef PurchaseSale = new(SiatCodes.SectorPurchaseSale, SiatCodes.InvoiceWithTaxCredit);
    public static readonly SiatDocumentRef CreditDebitNote = new(SiatCodes.SectorCreditDebitNote, SiatCodes.AdjustmentDocument);

    public SiatResource Resource => DocumentType == SiatCodes.AdjustmentDocument ? SiatResource.Adjustment
        : DocumentSector == SiatCodes.SectorPurchaseSale ? SiatResource.PurchaseSale : SiatResource.Computerized;
}

/// <summary>V4.1 · Mensaje del SIN (código, descripción y, en paquetes, número de archivo y de detalle).</summary>
public sealed record SiatMessage(int Code, string Description, int? FileNumber = null, int? DetailNumber = null)
{
    /// <summary>Los códigos 2000–2019 son advertencias: no invalidan el documento.</summary>
    public bool IsWarning => Code is >= 2000 and < 3000 or SiatCodes.CuisAboutToExpire;
}

/// <summary>V4.1 · Respuesta de los servicios de recepción, paquetes, anulación, reversión, verificación y comunicación.</summary>
public sealed record SiatReply(bool Transaction, int? StatusCode, string? StatusDescription, string? ReceptionCode, IReadOnlyList<SiatMessage> Messages)
{
    public bool Has(int code) => StatusCode == code || Messages.Any(m => m.Code == code);

    public string Describe() => StatusDescription is { Length: > 0 } d
        ? $"{StatusCode} {d}"
        : Messages.Count > 0 ? string.Join(" · ", Messages.Select(m => $"{m.Code} {m.Description}")) : $"{StatusCode}";
}

public sealed record SiatCuisReply(bool Transaction, string? Code, DateTimeOffset? ValidUntil, IReadOnlyList<SiatMessage> Messages);

public sealed record SiatCufdReply(bool Transaction, string? Code, string? ControlCode, string? Address, DateTimeOffset? ValidUntil,
    IReadOnlyList<SiatMessage> Messages);

public sealed record SiatNitReply(bool Transaction, bool IsValid, int? Code, string? Description, IReadOnlyList<SiatMessage> Messages);

/// <summary>Fila de un catálogo sincronizado: código y descripción (+ actividad y dato extra en los catálogos
/// estructurados: tipo de actividad, tipo de documento sector, NANDINA…).</summary>
public sealed record SiatCatalogRow(string Code, string Description, string? ActivityCode = null, string? Extra = null);

public sealed record SiatCatalogReply(bool Transaction, IReadOnlyList<SiatCatalogRow> Rows, IReadOnlyList<SiatMessage> Messages);

public sealed record SiatClockReply(bool Transaction, DateTime? SiatTime, IReadOnlyList<SiatMessage> Messages);

public sealed record SiatPointOfSaleReply(bool Transaction, int? Code, IReadOnlyList<SiatMessage> Messages);

public sealed record SiatPointOfSaleInfo(int Code, string Name, int? TypeCode);

public sealed record SiatPointOfSaleListReply(bool Transaction, IReadOnlyList<SiatPointOfSaleInfo> Items, IReadOnlyList<SiatMessage> Messages);

public sealed record SiatEventReply(bool Transaction, string? ReceptionCode, IReadOnlyList<SiatMessage> Messages);

/// <summary>
/// V4.1 · Puerto hacia los servicios web SOAP del SIN (modalidad Computarizada en Línea). Las respuestas de negocio
/// (códigos 9xx, mensajes) vuelven en los <c>*Reply</c>; la FALTA de comunicación (timeout, error de red, HTTP 4xx/5xx,
/// respuesta ilegible) lanza <see cref="SiatUnavailableException"/>: es lo que dispara el modo fuera de línea.
/// La implementación registra cada llamada en la bitácora técnica SIN el token.
/// </summary>
public interface ISiatGateway
{
    Task<SiatReply> CheckCommunicationAsync(SiatConnection connection, SiatResource resource, CancellationToken cancellationToken = default);

    Task<SiatCuisReply> RequestCuisAsync(SiatConnection connection, SiatPlace place, CancellationToken cancellationToken = default);

    Task<SiatCufdReply> RequestCufdAsync(SiatConnection connection, SiatPlace place, string cuis, CancellationToken cancellationToken = default);

    Task<SiatNitReply> VerifyNitAsync(SiatConnection connection, SiatPlace place, string cuis, long nitToVerify,
        CancellationToken cancellationToken = default);

    /// <summary>Sincroniza un catálogo (<see cref="SiatCatalogNames"/>, salvo FECHA_HORA).</summary>
    Task<SiatCatalogReply> SyncCatalogAsync(SiatConnection connection, SiatPlace place, string cuis, string catalog,
        CancellationToken cancellationToken = default);

    Task<SiatClockReply> SyncClockAsync(SiatConnection connection, SiatPlace place, string cuis, CancellationToken cancellationToken = default);

    Task<SiatPointOfSaleReply> RegisterPointOfSaleAsync(SiatConnection connection, SiatPlace place, string cuis, int typeCode, string name,
        string description, CancellationToken cancellationToken = default);

    Task<SiatPointOfSaleListReply> ListPointsOfSaleAsync(SiatConnection connection, SiatPlace place, string cuis,
        CancellationToken cancellationToken = default);

    Task<SiatReply> ClosePointOfSaleAsync(SiatConnection connection, SiatPlace place, string cuis, CancellationToken cancellationToken = default);

    /// <summary>registroEventoSignificativo: <paramref name="cufd"/> es el CUFD NUEVO y <paramref name="eventCufd"/> el vigente al
    /// empezar el evento; las fechas van en hora fiscal con milisegundos.</summary>
    Task<SiatEventReply> RegisterEventAsync(SiatConnection connection, SiatPlace place, string cuis, string cufd, int eventCode,
        string description, DateTime startedAt, DateTime endedAt, string eventCufd, CancellationToken cancellationToken = default);

    /// <summary>Recepción individual en línea (codigoEmision 1): <paramref name="gzip"/> = GZIP del XML; <paramref name="sha256"/> =
    /// SHA-256 en hexadecimal minúscula de esos mismos bytes.</summary>
    Task<SiatReply> SendDocumentAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        byte[] gzip, string sha256, DateTime sentAt, CancellationToken cancellationToken = default);

    /// <summary>Paquete de contingencia (codigoEmision 2): GZIP(TAR) de hasta 500 XML; <paramref name="eventReceptionCode"/> es el
    /// código de recepción del evento significativo.</summary>
    Task<SiatReply> SendPackageAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        byte[] tarGz, string sha256, DateTime sentAt, int documentCount, string eventReceptionCode, string? cafc,
        CancellationToken cancellationToken = default);

    Task<SiatReply> ValidatePackageAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        string receptionCode, CancellationToken cancellationToken = default);

    Task<SiatReply> VoidDocumentAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document, string cuf,
        int reasonCode, CancellationToken cancellationToken = default);

    Task<SiatReply> RevertVoidAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document, string cuf,
        CancellationToken cancellationToken = default);

    Task<SiatReply> CheckDocumentStatusAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        string cuf, CancellationToken cancellationToken = default);
}

/// <summary>V4.1 · No hubo comunicación con el SIN (timeout, red, HTTP 4xx/5xx, respuesta ilegible). NO es un rechazo.</summary>
public sealed class SiatUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>V4.1 · Datos del emisor y del lugar que van en el XML (además del documento).</summary>
public sealed record FiscalXmlContext(long Nit, string BusinessName, string Municipality, string? Phone, int BranchCode, int PointOfSaleCode,
    string CufdCode, string Address);

/// <summary>
/// V4.1 · XML de los documentos del SIN (orden exacto del XSD, todos los elementos presentes, los vacíos con
/// <c>xsi:nil="true"</c>, UTF-8 sin BOM, sin namespace, números con punto) validado contra los XSD oficiales, y los
/// archivos que viajan: GZIP individual, GZIP(TAR) de paquetes y SHA-256 en hexadecimal minúscula.
/// </summary>
public interface IFiscalDocumentSerializer
{
    /// <summary>Arma el XML y lo valida contra el XSD del sector; si no valida lanza DomainException «fiscal.xsd».</summary>
    string BuildXml(FiscalDocument document, FiscalXmlContext context);

    /// <summary>Valida un XML contra el XSD oficial de su sector (1 o 24).</summary>
    void Validate(string xml, int documentSector);

    byte[] Gzip(string xml);

    /// <summary>GZIP(TAR(xml_1 … xml_n)) en el orden dado (el SIN informa errores por número de archivo).</summary>
    byte[] Package(IReadOnlyList<(string FileName, string Xml)> documents);

    string Sha256Hex(byte[] data);
}

/// <summary>V4.1 · Representación gráfica (lo que se imprime o se envía en PDF).</summary>
public sealed record FiscalPrintModel(
    string Title, string Subtitle, string IssuerName, long IssuerNit, string BranchLabel, int PointOfSaleCode, string Address, string? Phone,
    string Municipality, long Number, string Cuf, DateTime IssuedAt, string BuyerName, string BuyerDocument, string CustomerCode,
    IReadOnlyList<FiscalPrintLine> Lines, decimal Subtotal, decimal Discount, decimal Total, decimal GiftCard, decimal AmountToPay,
    decimal TaxBase, string AmountInWords, string? PaymentMethod, string? Cashier, IReadOnlyList<string> Legends, string QrUrl,
    bool IsTest, bool IsVoided, bool IsOffline, FiscalPrintOriginal? Original = null, decimal? ReturnedTotal = null,
    decimal? CreditDebitAmount = null, string? SaleNumber = null);

public sealed record FiscalPrintLine(string ProductCode, string Description, string Unit, decimal Quantity, decimal UnitPrice, decimal Discount,
    decimal Subtotal, int? TransactionCode = null);

/// <summary>Factura original que se imprime en una nota crédito-débito.</summary>
public sealed record FiscalPrintOriginal(long Number, string Cuf, DateTime IssuedAt);

/// <summary>V4.1 · Representación gráfica en PDF (media carta / carta), sin dependencias externas.</summary>
public interface IFiscalDocumentRenderer
{
    byte[] RenderPdf(FiscalPrintModel model);
}

/// <summary>V4.1 · Representación gráfica en rollo (ESC/POS 58/80 mm, QR nativo de la impresora).</summary>
public interface IFiscalRollRenderer
{
    byte[] RenderRoll(FiscalPrintModel model, int columns = 48);
}

public sealed record MailServer(string Host, int Port, bool UseSsl, string? UserName, string? Password, string FromAddress, string FromName);

public sealed record MailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record MailMessageSpec(MailServer Server, string To, string Subject, string HtmlBody, IReadOnlyList<MailAttachment> Attachments);

/// <summary>V4.1 · Envío de correo (entrega del XML y de la representación gráfica al comprador).</summary>
public interface IMailSender
{
    Task SendAsync(MailMessageSpec message, CancellationToken cancellationToken = default);
}
