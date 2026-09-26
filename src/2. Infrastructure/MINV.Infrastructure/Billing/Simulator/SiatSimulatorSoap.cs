using System.Globalization;
using System.Xml.Linq;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Infrastructure.Billing.Soap;
using F = MINV.Infrastructure.Billing.Soap.SiatSoapContract.Fields;

namespace MINV.Infrastructure.Billing.Simulator;

/// <summary>V4.1 · Respuesta HTTP del simulador (código y cuerpo XML).</summary>
public sealed record SiatSimulatorHttpResult(int StatusCode, string Body)
{
    public const string ContentType = SiatSoapContract.ContentType;
}

/// <summary>
/// V4.1 · Lado servidor del contrato SOAP (<see cref="SiatSoapContract"/>) sobre el <see cref="SiatSimulatorEngine"/>: lee la
/// solicitud con el mismo lector tolerante que el cliente, llama al motor y escribe la respuesta. No depende de ASP.NET:
/// el proyecto MINV.SiatSimulator solo le pasa la ruta, la cabecera <c>apikey</c> y el cuerpo.
/// <list type="bullet">
/// <item>Recurso inexistente → 404; simulador apagado → 503; sin cabecera <c>apikey</c> → 401.</item>
/// <item>Sobre ilegible u operación que el recurso no publica → 500 con falla SOAP (SOAP 1.1).</item>
/// <item>Token no aceptado → 200 con el mensaje 989 (como el SIN).</item>
/// </list>
/// </summary>
public sealed class SiatSimulatorSoapHandler(SiatSimulatorEngine engine)
{
    public SiatSimulatorEngine Engine => engine;

    public SiatSimulatorHttpResult Handle(string? resourcePath, string? apiKeyHeader, string? body)
    {
        if (SiatSoapContract.ResourceFromPath(resourcePath) is not { } resource)
        {
            return Fault(404, "El recurso no existe en el simulador del SIN.");
        }
        if (!engine.Available)
        {
            return Fault(503, "Servicio no disponible (simulador del SIN fuera de servicio).");
        }
        if (SiatSoapContract.TokenFrom(apiKeyHeader) is not { } token)
        {
            return Fault(401, "Falta la cabecera apikey con el token delegado (TokenApi <token>).");
        }
        if (SiatSoapReader.ParseRequest(body ?? string.Empty) is not { } request)
        {
            return Fault(500, "La solicitud no es un sobre SOAP 1.1 legible.");
        }
        var name = request.Root.Name.LocalName;
        if (!SiatSoapContract.OperationsOf(resource).Contains(name))
        {
            return Fault(500, $"La operación {name} no existe en el recurso {SiatSoapContract.ResourcePath(resource)}.");
        }
        var ns = request.Root.Name.NamespaceName is { Length: > 0 } requested ? requested : SiatSoapContract.DefaultNamespace;
        try
        {
            var (operation, reply) = Dispatch(resource, name, request, token);
            return new SiatSimulatorHttpResult(200,
                SiatSoapEnvelope.Serialize(SiatSoapEnvelope.Response(ns, operation.Name, SiatSimulatorSoapWriter.Payload(operation, reply))));
        }
        catch (SiatUnavailableException ex)
        {
            return Fault(503, ex.Message);
        }
    }

    /// <summary>Descripción mínima del recurso para <c>GET …?wsdl</c> (NO es el WSDL del SIN: solo lista las operaciones).</summary>
    public static string Describe(SiatResource resource, string ns = SiatSoapContract.DefaultNamespace)
    {
        XNamespace wsdl = "http://schemas.xmlsoap.org/wsdl/";
        var service = SiatSoapContract.ResourcePath(resource);
        var document = new XElement(wsdl + "definitions",
            new XAttribute(XNamespace.Xmlns + "wsdl", wsdl.NamespaceName),
            new XAttribute("name", service),
            new XAttribute("targetNamespace", ns),
            new XComment(" Simulador del SIN de M-INV: descripción mínima (FUERA DE DOC: el contrato real se confirma con el WSDL del piloto). "),
            new XElement(wsdl + "portType", new XAttribute("name", service),
                SiatSoapContract.OperationsOf(resource).Select(o => new XElement(wsdl + "operation", new XAttribute("name", o)))),
            new XElement(wsdl + "service", new XAttribute("name", service)));
        return SiatSoapEnvelope.Serialize(document);
    }

    private (SiatSoapContract.Operation Operation, object Reply) Dispatch(SiatResource resource, string name, SiatSoapReader r, string token)
    {
        if (name == SiatSoapContract.VerifyCommunication.Name)
        {
            return (SiatSoapContract.VerifyCommunication, engine.CheckCommunication(token));
        }
        switch (resource)
        {
            case SiatResource.Codes:
                if (name == SiatSoapContract.Cuis.Name)
                {
                    return (SiatSoapContract.Cuis, engine.RequestCuis(token, Caller(r), Place(r)));
                }
                if (name == SiatSoapContract.Cufd.Name)
                {
                    return (SiatSoapContract.Cufd, engine.RequestCufd(token, Caller(r), Place(r), r.Text(F.Cuis)));
                }
                return (SiatSoapContract.VerifyNit,
                    engine.VerifyNit(token, Caller(r), r.Int(F.Branch) ?? -1, r.Text(F.Cuis), r.Long(F.NitToVerify) ?? 0));
            case SiatResource.Sync:
                var sync = SiatSoapContract.SyncByOperation(name)!;
                return sync.Shape == SiatSoapContract.SyncShape.Clock
                    ? (sync.Operation, engine.SyncClock(token, Caller(r), Place(r), r.Text(F.Cuis)))
                    : (sync.Operation, engine.SyncCatalog(token, Caller(r), Place(r), r.Text(F.Cuis), sync.Catalog));
            case SiatResource.Operations:
                if (name == SiatSoapContract.RegisterPointOfSale.Name)
                {
                    return (SiatSoapContract.RegisterPointOfSale, engine.RegisterPointOfSale(token, Caller(r), r.Int(F.Branch) ?? -1, r.Text(F.Cuis),
                        r.Int(F.PointOfSaleType) ?? 0, r.Text(F.PointOfSaleName), r.Text(F.Description)));
                }
                if (name == SiatSoapContract.ListPointsOfSale.Name)
                {
                    return (SiatSoapContract.ListPointsOfSale, engine.ListPointsOfSale(token, Caller(r), r.Int(F.Branch) ?? -1, r.Text(F.Cuis)));
                }
                if (name == SiatSoapContract.ClosePointOfSale.Name)
                {
                    return (SiatSoapContract.ClosePointOfSale, engine.ClosePointOfSale(token, Caller(r), Place(r), r.Text(F.Cuis)));
                }
                return (SiatSoapContract.RegisterEvent, engine.RegisterEvent(token, Caller(r), Place(r), r.Text(F.Cuis), r.Text(F.Cufd),
                    r.Int(F.EventReason) ?? 0, r.Text(F.Description), SiatSoapReader.ParseBoliviaTime(r.Text(F.EventStart)),
                    SiatSoapReader.ParseBoliviaTime(r.Text(F.EventEnd)), r.Text(F.EventCufd)));
            default:
                var operations = SiatSoapContract.Documents(resource);
                var call = DocumentCall(resource, r);
                if (name == operations.Reception.Name)
                {
                    return (operations.Reception, engine.ReceiveDocument(token, call, Bytes(r), r.Text(F.FileHash),
                        SiatSoapReader.ParseBoliviaTime(r.Text(F.SentAt))));
                }
                if (name == operations.Package?.Name)
                {
                    return (operations.Package!, engine.ReceivePackage(token, call, Bytes(r), r.Text(F.FileHash),
                        SiatSoapReader.ParseBoliviaTime(r.Text(F.SentAt)), r.Int(F.DocumentCount) ?? 0, r.Text(F.EventCode), r.Text(F.Cafc)));
                }
                if (name == operations.PackageValidation?.Name)
                {
                    return (operations.PackageValidation!, engine.ValidatePackage(token, call, r.Text(F.ReceptionCode)));
                }
                if (name == operations.Void.Name)
                {
                    return (operations.Void, engine.VoidDocument(token, call, r.Text(F.Cuf), r.Int(F.VoidReason) ?? 0));
                }
                if (name == operations.Revert.Name)
                {
                    return (operations.Revert, engine.RevertVoid(token, call, r.Text(F.Cuf)));
                }
                return (operations.Status, engine.CheckDocumentStatus(token, call, r.Text(F.Cuf)));
        }
    }

    private static SiatSimulatorCaller Caller(SiatSoapReader r) =>
        new(r.Int(F.Environment) ?? 0, r.Int(F.Modality) ?? 0, r.Long(F.Nit) ?? 0, r.Text(F.SystemCode));

    private static SiatPlace Place(SiatSoapReader r) => new(r.Int(F.Branch) ?? -1, r.Int(F.PointOfSale) ?? 0);

    private static SiatSimulatorDocumentCall DocumentCall(SiatResource resource, SiatSoapReader r) =>
        new(Caller(r), Place(r), resource, r.Int(F.DocumentSector) ?? 0, r.Int(F.DocumentType) ?? 0, r.Int(F.Emission) ?? 0, r.Text(F.Cuis),
            r.Text(F.Cufd));

    private static byte[]? Bytes(SiatSoapReader r)
    {
        var text = r.Text(F.File);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static SiatSimulatorHttpResult Fault(int status, string message) =>
        new(status, SiatSoapEnvelope.Serialize(SiatSoapEnvelope.Fault(message)));
}

/// <summary>V4.1 · Cuerpo de las respuestas SOAP del simulador (lo usan el host HTTP y la bitácora del gateway en proceso).</summary>
public static class SiatSimulatorSoapWriter
{
    /// <summary>Elemento de respuesta (<see cref="SiatSoapContract.Operation.Response"/>) con los campos de la respuesta del puerto.</summary>
    public static XElement Payload(SiatSoapContract.Operation operation, object reply)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return reply switch
        {
            SiatCuisReply r => new XElement(operation.Response, Opt(F.Code, r.Code), Opt(F.ValidUntil, Validity(r.ValidUntil)), Messages(r.Messages),
                Transaction(r.Transaction)),
            SiatCufdReply r => new XElement(operation.Response, Opt(F.Code, r.Code), Opt(F.ControlCode, r.ControlCode), Opt(F.Address, r.Address),
                Opt(F.ValidUntil, Validity(r.ValidUntil)), Messages(r.Messages), Transaction(r.Transaction)),
            SiatNitReply r => new XElement(operation.Response, Messages(r.Messages), Transaction(r.Transaction)),
            SiatCatalogReply r => Catalog(operation, r),
            SiatClockReply r => new XElement(operation.Response, Opt(F.DateTime, r.SiatTime is { } t ? FiscalRules.FormatDateTime(t) : null),
                Messages(r.Messages), Transaction(r.Transaction)),
            SiatPointOfSaleReply r => new XElement(operation.Response, Opt(F.PointOfSale, r.Code), Messages(r.Messages), Transaction(r.Transaction)),
            SiatPointOfSaleListReply r => new XElement(operation.Response,
                r.Items.Select(i => new XElement(F.PointsOfSale, new XElement(F.PointOfSale, i.Code), new XElement(F.PointOfSaleName, i.Name),
                    Opt(F.PointOfSaleTypeName, i.TypeCode))),
                Messages(r.Messages), Transaction(r.Transaction)),
            SiatEventReply r => new XElement(operation.Response, Opt(F.EventReceptionCode, r.ReceptionCode), Messages(r.Messages),
                Transaction(r.Transaction)),
            SiatReply r when operation == SiatSoapContract.VerifyCommunication => new XElement(operation.Response, Messages(r.Messages),
                Transaction(r.Transaction)),
            SiatReply r => new XElement(operation.Response, Opt(F.StatusDescription, r.StatusDescription), Opt(F.Status, r.StatusCode),
                Opt(F.ReceptionCode, r.ReceptionCode), Messages(r.Messages), Transaction(r.Transaction)),
            _ => throw new ArgumentException($"Respuesta no reconocida: {reply?.GetType().Name}.", nameof(reply)),
        };
    }

    private static XElement Catalog(SiatSoapContract.Operation operation, SiatCatalogReply reply)
    {
        var shape = SiatSoapContract.SyncByOperation(operation.Name)?.Shape ?? SiatSoapContract.SyncShape.Parametric;
        var rows = reply.Rows.Select(row => shape switch
        {
            SiatSoapContract.SyncShape.Activities => new XElement(F.Activities, new XElement(F.ActivityCaeb, row.Code),
                new XElement(F.MessageDescription, row.Description), Opt(F.ActivityType, row.Extra)),
            SiatSoapContract.SyncShape.ActivitySectors => new XElement(F.ActivitySectors, Opt(F.Activity, row.ActivityCode),
                new XElement(F.DocumentSector, row.Code), Opt(F.SectorType, row.Extra)),
            SiatSoapContract.SyncShape.Legends => new XElement(F.Legends, Opt(F.Activity, row.ActivityCode), new XElement(F.LegendText, row.Description)),
            SiatSoapContract.SyncShape.Products => new XElement(F.Codes, Opt(F.Activity, row.ActivityCode), new XElement(F.Product, row.Code),
                new XElement(F.ProductDescription, row.Description), Opt(F.Nandina, row.Extra)),
            _ => new XElement(F.Codes, new XElement(F.Classifier, row.Code), new XElement(F.MessageDescription, row.Description)),
        });
        return new XElement(operation.Response, rows, Messages(reply.Messages), Transaction(reply.Transaction));
    }

    private static IEnumerable<XElement> Messages(IReadOnlyList<SiatMessage> messages) =>
        messages.Select(m => new XElement(F.Messages, new XElement(F.Code, m.Code), new XElement(F.MessageDescription, m.Description),
            Opt(F.FileNumber, m.FileNumber), Opt(F.DetailNumber, m.DetailNumber)));

    private static XElement Transaction(bool value) => new(F.Transaction, value ? "true" : "false");

    private static string? Validity(DateTimeOffset? value) =>
        value?.ToOffset(SiatSoapContract.BoliviaOffset).ToString(SiatSoapContract.ValidityFormat, CultureInfo.InvariantCulture);

    private static XElement? Opt(string name, string? value) => value is null ? null : new XElement(name, value);

    private static XElement? Opt(string name, long? value) => value is null ? null : new XElement(name, value.Value.ToString(CultureInfo.InvariantCulture));
}
