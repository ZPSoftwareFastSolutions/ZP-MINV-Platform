using System.Globalization;
using System.Xml.Linq;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using F = MINV.Infrastructure.Billing.Soap.SiatSoapContract.Fields;

namespace MINV.Infrastructure.Billing.Soap;

/// <summary>
/// V4.1 · Una llamada al SIN ya armada, SIN el token (viaja solo en la cabecera HTTP): recurso, operación, campos del
/// parámetro en el orden del contrato y lugar (para la bitácora). La usan el cliente SOAP y el simulador en proceso, así
/// que ambos registran exactamente el mismo cuerpo.
/// </summary>
public sealed record SiatSoapCall(SiatResource Resource, SiatSoapContract.Operation Operation, IReadOnlyList<KeyValuePair<string, string?>> Fields,
    SiatPlace? Place)
{
    public XElement Envelope(SiatEndpointSet endpoints) => SiatSoapEnvelope.Request(SiatSoapContract.Namespace(endpoints), Operation, Fields);

    public XElement Envelope(string ns) => SiatSoapEnvelope.Request(ns, Operation, Fields);
}

/// <summary>
/// V4.1 · Solicitudes de cada operación del puerto <see cref="ISiatGateway"/> (FUERA DE DOC: nombres y orden de los campos
/// según la práctica de los servicios v2 del SIN; confirmar con el WSDL del piloto). Los valores nulos no se envían.
/// </summary>
public static class SiatSoapRequests
{
    public static SiatSoapCall CheckCommunication(SiatResource resource) => new(resource, SiatSoapContract.VerifyCommunication, [], null);

    // ------------------------------------------------------------------------------------------------ códigos
    public static SiatSoapCall Cuis(SiatConnection c, SiatPlace place) =>
        new(SiatResource.Codes, SiatSoapContract.Cuis,
        [
            Field(F.Environment, c.Environment), Field(F.Modality, c.Modality), Field(F.PointOfSale, place.PointOfSaleCode),
            Field(F.SystemCode, c.SystemCode), Field(F.Branch, place.BranchCode), Field(F.Nit, c.Nit),
        ], place);

    public static SiatSoapCall Cufd(SiatConnection c, SiatPlace place, string cuis) =>
        new(SiatResource.Codes, SiatSoapContract.Cufd,
        [
            Field(F.Environment, c.Environment), Field(F.Modality, c.Modality), Field(F.PointOfSale, place.PointOfSaleCode),
            Field(F.SystemCode, c.SystemCode), Field(F.Branch, place.BranchCode), Field(F.Cuis, cuis), Field(F.Nit, c.Nit),
        ], place);

    public static SiatSoapCall VerifyNit(SiatConnection c, SiatPlace place, string cuis, long nitToVerify) =>
        new(SiatResource.Codes, SiatSoapContract.VerifyNit,
        [
            Field(F.Environment, c.Environment), Field(F.Modality, c.Modality), Field(F.SystemCode, c.SystemCode),
            Field(F.Branch, place.BranchCode), Field(F.Cuis, cuis), Field(F.Nit, c.Nit), Field(F.NitToVerify, nitToVerify),
        ], place);

    // ------------------------------------------------------------------------------------------------ sincronización
    /// <summary>Sincronización de un catálogo; la fecha y hora tiene su propia operación (<see cref="Clock"/>).</summary>
    public static SiatSoapCall Sync(SiatConnection c, SiatPlace place, string cuis, string catalog)
    {
        var sync = SiatSoapContract.SyncFor(catalog);
        if (sync.Shape == SiatSoapContract.SyncShape.Clock)
        {
            throw new DomainException("siat.catalog_clock", "La fecha y hora del SIN se sincroniza con su propia operación (sincronizarFechaHora).");
        }
        return new(SiatResource.Sync, sync.Operation, SyncFields(c, place, cuis), place);
    }

    public static SiatSoapCall Clock(SiatConnection c, SiatPlace place, string cuis) =>
        new(SiatResource.Sync, SiatSoapContract.SyncFor(SiatCatalogNames.DateTime).Operation, SyncFields(c, place, cuis), place);

    // ------------------------------------------------------------------------------------------------ operaciones
    public static SiatSoapCall RegisterPointOfSale(SiatConnection c, SiatPlace place, string cuis, int typeCode, string name, string description) =>
        new(SiatResource.Operations, SiatSoapContract.RegisterPointOfSale,
        [
            Field(F.Environment, c.Environment), Field(F.Modality, c.Modality), Field(F.SystemCode, c.SystemCode), Field(F.Branch, place.BranchCode),
            Field(F.PointOfSaleType, typeCode), Field(F.Cuis, cuis), Field(F.Description, description), Field(F.Nit, c.Nit),
            Field(F.PointOfSaleName, name),
        ], place);

    public static SiatSoapCall ListPointsOfSale(SiatConnection c, SiatPlace place, string cuis) =>
        new(SiatResource.Operations, SiatSoapContract.ListPointsOfSale,
        [
            Field(F.Environment, c.Environment), Field(F.SystemCode, c.SystemCode), Field(F.Branch, place.BranchCode), Field(F.Cuis, cuis),
            Field(F.Nit, c.Nit),
        ], place);

    public static SiatSoapCall ClosePointOfSale(SiatConnection c, SiatPlace place, string cuis) =>
        new(SiatResource.Operations, SiatSoapContract.ClosePointOfSale,
        [
            Field(F.Environment, c.Environment), Field(F.PointOfSale, place.PointOfSaleCode), Field(F.SystemCode, c.SystemCode),
            Field(F.Branch, place.BranchCode), Field(F.Cuis, cuis), Field(F.Nit, c.Nit),
        ], place);

    public static SiatSoapCall RegisterEvent(SiatConnection c, SiatPlace place, string cuis, string cufd, int eventCode, string description,
        DateTime startedAt, DateTime endedAt, string eventCufd) =>
        new(SiatResource.Operations, SiatSoapContract.RegisterEvent,
        [
            Field(F.Environment, c.Environment), Field(F.EventReason, eventCode), Field(F.PointOfSale, place.PointOfSaleCode),
            Field(F.SystemCode, c.SystemCode), Field(F.Branch, place.BranchCode), Field(F.Cufd, cufd), Field(F.EventCufd, eventCufd),
            Field(F.Cuis, cuis), Field(F.Description, description), Field(F.EventEnd, FiscalRules.FormatDateTime(endedAt)),
            Field(F.EventStart, FiscalRules.FormatDateTime(startedAt)), Field(F.Nit, c.Nit),
        ], place);

    // ------------------------------------------------------------------------------------------------ documentos
    public static SiatSoapCall SendDocument(SiatConnection c, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document, byte[] gzip,
        string sha256, DateTime sentAt)
    {
        ArgumentNullException.ThrowIfNull(gzip);
        var fields = DocumentFields(c, place, codes, document, SiatCodes.EmissionOnline);
        fields.Add(Field(F.File, Convert.ToBase64String(gzip)));
        fields.Add(Field(F.SentAt, FiscalRules.FormatDateTime(sentAt)));
        fields.Add(Field(F.FileHash, sha256));
        return new(document.Resource, SiatSoapContract.Documents(document.Resource).Reception, fields, place);
    }

    public static SiatSoapCall SendPackage(SiatConnection c, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document, byte[] tarGz,
        string sha256, DateTime sentAt, int documentCount, string eventReceptionCode, string? cafc)
    {
        ArgumentNullException.ThrowIfNull(tarGz);
        var operation = SiatSoapContract.Documents(document.Resource).Package ?? throw SiatGatewayErrors.PackagesNotSupported();
        var fields = DocumentFields(c, place, codes, document, SiatCodes.EmissionOffline);
        fields.Add(Field(F.File, Convert.ToBase64String(tarGz)));
        fields.Add(Field(F.SentAt, FiscalRules.FormatDateTime(sentAt)));
        fields.Add(Field(F.FileHash, sha256));
        fields.Add(Field(F.Cafc, string.IsNullOrWhiteSpace(cafc) ? null : cafc.Trim()));
        fields.Add(Field(F.DocumentCount, documentCount));
        fields.Add(Field(F.EventCode, eventReceptionCode));
        return new(document.Resource, operation, fields, place);
    }

    public static SiatSoapCall ValidatePackage(SiatConnection c, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        string receptionCode)
    {
        var operation = SiatSoapContract.Documents(document.Resource).PackageValidation ?? throw SiatGatewayErrors.PackagesNotSupported();
        var fields = DocumentFields(c, place, codes, document, SiatCodes.EmissionOffline);
        fields.Add(Field(F.ReceptionCode, receptionCode));
        return new(document.Resource, operation, fields, place);
    }

    public static SiatSoapCall VoidDocument(SiatConnection c, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document, string cuf,
        int reasonCode)
    {
        var fields = DocumentFields(c, place, codes, document, SiatCodes.EmissionOnline);
        fields.Add(Field(F.VoidReason, reasonCode));
        fields.Add(Field(F.Cuf, cuf));
        return new(document.Resource, SiatSoapContract.Documents(document.Resource).Void, fields, place);
    }

    public static SiatSoapCall RevertVoid(SiatConnection c, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document, string cuf)
    {
        var fields = DocumentFields(c, place, codes, document, SiatCodes.EmissionOnline);
        fields.Add(Field(F.Cuf, cuf));
        return new(document.Resource, SiatSoapContract.Documents(document.Resource).Revert, fields, place);
    }

    public static SiatSoapCall CheckDocumentStatus(SiatConnection c, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document, string cuf)
    {
        var fields = DocumentFields(c, place, codes, document, SiatCodes.EmissionOnline);
        fields.Add(Field(F.Cuf, cuf));
        return new(document.Resource, SiatSoapContract.Documents(document.Resource).Status, fields, place);
    }

    // ------------------------------------------------------------------------------------------------ comunes
    private static List<KeyValuePair<string, string?>> SyncFields(SiatConnection c, SiatPlace place, string cuis) =>
    [
        Field(F.Environment, c.Environment), Field(F.PointOfSale, place.PointOfSaleCode), Field(F.SystemCode, c.SystemCode),
        Field(F.Branch, place.BranchCode), Field(F.Cuis, cuis), Field(F.Nit, c.Nit),
    ];

    /// <summary>Los once campos comunes de los servicios de facturación (02 §3.2), en orden alfabético como los publica el
    /// servicio; los propios de cada operación van después.</summary>
    private static List<KeyValuePair<string, string?>> DocumentFields(SiatConnection c, SiatPlace place, SiatCodesForCall codes,
        SiatDocumentRef document, int emission)
    {
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(document);
        return
        [
            Field(F.Environment, c.Environment), Field(F.DocumentSector, document.DocumentSector), Field(F.Emission, emission),
            Field(F.Modality, c.Modality), Field(F.PointOfSale, place.PointOfSaleCode), Field(F.SystemCode, c.SystemCode),
            Field(F.Branch, place.BranchCode), Field(F.Cufd, codes.Cufd), Field(F.Cuis, codes.Cuis), Field(F.Nit, c.Nit),
            Field(F.DocumentType, document.DocumentType),
        ];
    }

    private static KeyValuePair<string, string?> Field(string name, string? value) => new(name, value);

    private static KeyValuePair<string, string?> Field(string name, long value) => new(name, value.ToString(CultureInfo.InvariantCulture));
}
