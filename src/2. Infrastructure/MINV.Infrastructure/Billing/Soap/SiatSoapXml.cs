using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using MINV.Application.Abstractions;

namespace MINV.Infrastructure.Billing.Soap;

/// <summary>V4.1 · Sobres SOAP 1.1 del contrato del SIN: solicitud, respuesta y falla (<see cref="SiatSoapContract"/>).</summary>
public static class SiatSoapEnvelope
{
    private static readonly XNamespace Soap = SiatSoapContract.EnvelopeNamespace;

    /// <summary>Solicitud: <c>&lt;siat:OPERACION&gt;&lt;PARAMETRO&gt;campos&lt;/PARAMETRO&gt;&lt;/siat:OPERACION&gt;</c>. El parámetro y sus
    /// campos van SIN namespace; los campos con valor null se omiten.</summary>
    public static XElement Request(string ns, SiatSoapContract.Operation operation, IEnumerable<KeyValuePair<string, string?>> fields)
    {
        ArgumentNullException.ThrowIfNull(operation);
        XNamespace target = ns;
        var body = new XElement(target + operation.Name);
        if (operation.Parameter is not null)
        {
            body.Add(new XElement(operation.Parameter,
                fields.Where(f => f.Value is not null).Select(f => new XElement(f.Key, f.Value))));
        }
        return new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soapenv", Soap.NamespaceName),
            new XAttribute(XNamespace.Xmlns + SiatSoapContract.BodyPrefix, target.NamespaceName),
            new XElement(Soap + "Header"),
            new XElement(Soap + "Body", body));
    }

    /// <summary>Respuesta al estilo JAX-WS: <c>&lt;ns2:OPERACIONResponse&gt;&lt;Respuesta…&gt;…&lt;/Respuesta…&gt;&lt;/ns2:OPERACIONResponse&gt;</c>.</summary>
    public static XElement Response(string ns, string operationName, XElement payload)
    {
        XNamespace target = ns;
        return new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", Soap.NamespaceName),
            new XElement(Soap + "Body",
                new XElement(target + (operationName + "Response"), new XAttribute(XNamespace.Xmlns + "ns2", target.NamespaceName), payload)));
    }

    public static XElement Fault(string message) =>
        new(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", Soap.NamespaceName),
            new XElement(Soap + "Body",
                new XElement(Soap + "Fault",
                    new XElement("faultcode", "soap:Server"),
                    new XElement("faultstring", message))));

    public static string Serialize(XElement envelope) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + envelope.ToString(SaveOptions.DisableFormatting);

    /// <summary>Copia del sobre para la bitácora: el <c>archivo</c> (GZIP en base64) se reemplaza por su tamaño.</summary>
    public static string ForLog(XElement envelope)
    {
        var copy = new XElement(envelope);
        foreach (var file in copy.Descendants().Where(e => e.Name.LocalName == SiatSoapContract.Fields.File).ToList())
        {
            file.Value = $"[GZIP en base64: {file.Value.Length} caracteres]";
        }
        return Serialize(copy);
    }

    /// <summary>Lee un XML sin DTD ni resolución de entidades externas.</summary>
    internal static XDocument Load(string xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        return XDocument.Load(reader);
    }
}

/// <summary>
/// V4.1 · Lectura TOLERANTE de un mensaje SOAP del SIN: busca los elementos por nombre LOCAL en cualquier nivel (sin
/// depender de prefijos, namespaces ni del envoltorio exacto). Los campos de nivel superior no se confunden con los de
/// las listas (mensajes, catálogos, puntos de venta). Sirve para las respuestas (cliente) y las solicitudes (simulador).
/// </summary>
public sealed class SiatSoapReader
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly Regex ZoneSuffix = new(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.CultureInvariant);

    private readonly XElement _root;

    private SiatSoapReader(XElement root)
    {
        _root = root;
    }

    /// <summary>Elemento leído (respuesta u operación).</summary>
    public XElement Root => _root;

    /// <summary>Respuesta del SIN: una falla SOAP, un cuerpo ausente o un XML ilegible NO son respuestas de negocio: lanzan
    /// <see cref="SiatUnavailableException"/> (sin comunicación).</summary>
    public static SiatSoapReader ParseResponse(string xml, string operation)
    {
        XDocument document;
        try
        {
            document = SiatSoapEnvelope.Load(xml);
        }
        catch (XmlException ex)
        {
            throw new SiatUnavailableException($"El SIN devolvió una respuesta ilegible en {operation}: {ex.Message}", ex);
        }
        var body = BodyOf(document) ?? throw new SiatUnavailableException($"La respuesta del SIN en {operation} no es un sobre SOAP.");
        if (body.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault") is { } fault)
        {
            throw new SiatUnavailableException($"El SIN respondió con una falla SOAP en {operation}: {FaultText(fault)}");
        }
        var response = body.Elements().FirstOrDefault()
                       ?? throw new SiatUnavailableException($"La respuesta del SIN en {operation} llegó vacía.");
        return new SiatSoapReader(response);
    }

    /// <summary>Operación de una solicitud (primer elemento del cuerpo), o null si no es un sobre SOAP.</summary>
    public static SiatSoapReader? ParseRequest(string xml)
    {
        try
        {
            var body = BodyOf(SiatSoapEnvelope.Load(xml));
            return body?.Elements().FirstOrDefault() is { } operation ? new SiatSoapReader(operation) : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>Texto de la falla SOAP de un cuerpo (HTTP 500), o null si no la hay o no se puede leer.</summary>
    public static string? FaultText(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }
        try
        {
            return SiatSoapEnvelope.Load(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault") is { } fault ? FaultText(fault) : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------------------------------------ campos
    public bool? Transaction => Bool(Text(SiatSoapContract.Fields.Transaction));

    /// <summary>Primer campo simple (sin hijos, fuera de las listas) con alguno de los nombres; null si falta o es nil.</summary>
    public string? Text(params string[] names)
    {
        foreach (var name in names)
        {
            var element = _root.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == name && !e.HasElements && !InsideList(e));
            if (element is not null && !IsNil(element))
            {
                return element.Value.Trim();
            }
        }
        return null;
    }

    public int? Int(params string[] names) => ParseInt(Text(names));

    public long? Long(params string[] names) =>
        long.TryParse(Text(names), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>Elementos de una lista (p. ej. <c>listaCodigos</c>) en cualquier nivel.</summary>
    public IEnumerable<XElement> Items(string name) => _root.DescendantsAndSelf().Where(e => e.Name.LocalName == name);

    /// <summary>Mensajes (código, descripción, número de archivo y de detalle) con cualquiera de los nombres de lista.</summary>
    public IReadOnlyList<SiatMessage> Messages
    {
        get
        {
            var messages = new List<SiatMessage>();
            foreach (var list in _root.Descendants().Where(e => SiatSoapContract.MessageListNames.Contains(e.Name.LocalName)))
            {
                var items = Child(list, SiatSoapContract.Fields.Code) is not null
                    ? new List<XElement> { list }
                    : list.Elements().Where(e => Child(e, SiatSoapContract.Fields.Code) is not null).ToList();
                foreach (var item in items)
                {
                    if (ParseInt(Child(item, SiatSoapContract.Fields.Code)) is { } code)
                    {
                        messages.Add(new SiatMessage(code, Child(item, SiatSoapContract.Fields.MessageDescription) ?? string.Empty,
                            ParseInt(Child(item, SiatSoapContract.Fields.FileNumber)), ParseInt(Child(item, SiatSoapContract.Fields.DetailNumber))));
                    }
                }
            }
            return messages;
        }
    }

    /// <summary>Hijo directo de un elemento de lista (null si falta o es nil).</summary>
    public static string? Child(XElement item, string name)
    {
        var element = item.Elements().FirstOrDefault(e => e.Name.LocalName == name);
        return element is null || IsNil(element) || element.HasElements ? null : element.Value.Trim();
    }

    public static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    public static bool? Bool(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => null,
    };

    /// <summary>Fecha con zona (se respeta) o sin zona (se toma como hora de Bolivia, UTC−4).</summary>
    public static DateTimeOffset? ParseInstant(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var text = value.Trim();
        var t = text.IndexOf('T');
        if (t > 0 && ZoneSuffix.IsMatch(text[t..])
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var zoned))
        {
            return zoned;
        }
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), SiatSoapContract.BoliviaOffset)
            : null;
    }

    /// <summary>Hora de Bolivia sin zona (la que usa la facturación) a partir de un texto con o sin zona.</summary>
    public static DateTime? ParseBoliviaTime(string? value) =>
        ParseInstant(value) is { } instant
            ? DateTime.SpecifyKind(instant.ToOffset(SiatSoapContract.BoliviaOffset).DateTime, DateTimeKind.Unspecified)
            : null;

    private bool InsideList(XElement element)
    {
        for (var parent = element.Parent; parent is not null && parent != _root.Parent; parent = parent.Parent)
        {
            if (SiatSoapContract.ListNames.Contains(parent.Name.LocalName))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsNil(XElement element) => string.Equals((string?)element.Attribute(Xsi + "nil"), "true", StringComparison.OrdinalIgnoreCase);

    private static XElement? BodyOf(XDocument document) => document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Body");

    private static string FaultText(XElement fault) =>
        fault.Descendants().FirstOrDefault(e => e.Name.LocalName is "faultstring" or "Text" or "Reason")?.Value.Trim() is { Length: > 0 } text
            ? text
            : "sin detalle";
}
