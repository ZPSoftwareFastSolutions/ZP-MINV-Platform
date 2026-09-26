using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Infrastructure.Billing.Xml;

/// <summary>
/// V4.1 · XML de los documentos fiscales del SIN (modalidad Computarizada en Línea) y archivos que viajan al SIN
/// (investigación 05 §1–4 y 02 §5; regla F-05):
/// <list type="bullet">
/// <item>Factura Compra Venta (sector 1): raíz <c>facturaComputarizadaCompraVenta</c>, cabecera de 30 elementos y detalle de
/// 11, con 2 decimales en todos los montos.</item>
/// <item>Nota Crédito-Débito (sector 24): raíz <c>notaFiscalComputarizadaCreditoDebito</c>, cabecera de 27 y detalle de 10,
/// con hasta 10 decimales en cantidad, precio, descuento y subtotal del detalle.</item>
/// </list>
/// Orden EXACTO del XSD, TODOS los elementos presentes (los vacíos con <c>xsi:nil="true"</c>), raíz sin namespace, UTF-8
/// sin BOM, números con punto (InvariantCulture) y montos derivados del agregado. Todo XML se valida contra el XSD oficial
/// embebido antes de devolverse: un XML que no valida NO se envía.
/// </summary>
public sealed class SiatXmlSerializer : IFiscalDocumentSerializer
{
    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    private const string Declaration = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""";

    /// <summary>Hora fija de las entradas del TAR: el paquete es determinista (mismo contenido → mismos bytes y hash).</summary>
    private static readonly DateTimeOffset TarEntryTime = new(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public string BuildXml(FiscalDocument document, FiscalXmlContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        var xml = document.Kind switch
        {
            FiscalDocumentKind.Invoice => Write(document, context, WriteInvoice),
            FiscalDocumentKind.CreditDebitNote => Write(document, context, WriteCreditNote),
            _ => throw new NotSupportedException($"Documento fiscal {document.Kind} no implementado."),
        };
        Validate(xml, document.DocumentSector);
        return xml;
    }

    public void Validate(string xml, int documentSector)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var errors = SiatSchemas.Validate(xml, documentSector);
        if (errors.Count > 0)
        {
            var shown = string.Join(" · ", errors.Take(3));
            var more = errors.Count > 3 ? $" (y {errors.Count - 3} más)" : string.Empty;
            throw new DomainException("fiscal.xsd", $"El XML no cumple el esquema del SIN: {shown}{more}");
        }
    }

    /// <summary>GZIP (RFC 1952) de los bytes UTF-8 sin BOM del XML. Determinista: sin nombre ni fecha en la cabecera.</summary>
    public byte[] Gzip(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        return Compress(Utf8.GetBytes(xml));
    }

    /// <summary>
    /// Paquete de contingencia: GZIP(TAR(xml_1 … xml_n)), un archivo por documento en el orden dado (el SIN informa los
    /// errores por número de archivo). Nombres: los recibidos o <c>1.xml</c>, <c>2.xml</c>… (hueco H-04 de la
    /// investigación). Determinista: fecha, dueño y permisos fijos en cada entrada.
    /// </summary>
    public byte[] Package(IReadOnlyList<(string FileName, string Xml)> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        Guard.That(documents.Count is >= 1 and <= SiatCodes.MaxDocumentsPerPackage, "fiscal.package_size",
            $"Un paquete lleva de 1 a {SiatCodes.MaxDocumentsPerPackage} documentos del mismo sector.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, TarEntryFormat.Ustar, leaveOpen: true))
        {
            for (var i = 0; i < documents.Count; i++)
            {
                var (fileName, xml) = documents[i];
                var name = string.IsNullOrWhiteSpace(fileName) ? $"{i + 1}.xml" : fileName.Trim();
                Guard.That(name.Length <= 100 && name.IndexOfAny(['/', '\\']) < 0 && name.All(c => c is > ' ' and < (char)127),
                    "fiscal.package_name", $"El nombre «{name}» no sirve dentro del paquete (ASCII, sin carpetas, hasta 100 caracteres).");
                Guard.That(names.Add(name), "fiscal.package_name", $"El nombre «{name}» está repetido en el paquete.");
                ArgumentNullException.ThrowIfNull(xml);
                using var data = new MemoryStream(Utf8.GetBytes(xml));
                var entry = new UstarTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = data,
                    ModificationTime = TarEntryTime,
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                    Uid = 0,
                    Gid = 0,
                };
                writer.WriteEntry(entry);
            }
        }
        return Compress(tar.ToArray());
    }

    /// <summary>SHA-256 en hexadecimal MINÚSCULA (64 caracteres): hashArchivo y huella del documento.</summary>
    public string Sha256Hex(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    // ------------------------------------------------------------------------------------------------ factura (sector 1)
    private static void WriteInvoice(XmlWriter w, FiscalDocument d, FiscalXmlContext c)
    {
        WriteIssuerStart(w, c);
        w.WriteElementString("numeroFactura", Integer(d.Number));
        WriteDocumentPlace(w, d, c);
        w.WriteElementString("codigoMetodoPago", d.PaymentMethodCode is { } method ? Integer(method) : string.Empty);
        Optional(w, "numeroTarjeta", d.CardNumberMasked);
        w.WriteElementString("montoTotal", Amount(d.TotalAmount, 2));
        w.WriteElementString("montoTotalSujetoIva", Amount(d.TotalSubjectToVat, 2));
        w.WriteElementString("codigoMoneda", Integer(d.CurrencyCode));
        w.WriteElementString("tipoCambio", Amount(d.ExchangeRate, 2));
        w.WriteElementString("montoTotalMoneda", Amount(d.ExchangeRate == 1m ? d.TotalAmount : FiscalRules.Round2(d.TotalAmount / d.ExchangeRate), 2));
        OptionalAmount(w, "montoGiftCard", d.GiftCardAmount, 2);
        OptionalAmount(w, "descuentoAdicional", d.AdditionalDiscount, 2);
        w.WriteElementString("codigoExcepcion", Integer(d.ExceptionCode));
        Optional(w, "cafc", d.Cafc);
        WriteClosing(w, d);
        foreach (var line in d.Lines.OrderBy(l => l.LineNumber))
        {
            w.WriteStartElement("detalle");
            WriteLineStart(w, line);
            w.WriteElementString("cantidad", Amount(line.Quantity, 2));
            w.WriteElementString("unidadMedida", Integer(line.SinUnitCode));
            w.WriteElementString("precioUnitario", Amount(line.UnitPrice, 2));
            OptionalAmount(w, "montoDescuento", line.Discount ?? 0m, 2);
            w.WriteElementString("subTotal", Amount(line.Subtotal, 2));
            Optional(w, "numeroSerie", line.SerialNumber);
            Optional(w, "numeroImei", line.Imei);
            w.WriteEndElement();
        }
    }

    // ------------------------------------------------------------------------------------------------ nota (sector 24)
    private static void WriteCreditNote(XmlWriter w, FiscalDocument d, FiscalXmlContext c)
    {
        var original = d.NoteReference
                       ?? throw new DomainException("fiscal.note_original", "La nota crédito-débito no tiene los datos de la factura original.");
        WriteIssuerStart(w, c);
        w.WriteElementString("numeroNotaCreditoDebito", Integer(d.Number));
        WriteDocumentPlace(w, d, c);
        w.WriteElementString("numeroFactura", Integer(original.OriginalNumber));
        w.WriteElementString("numeroAutorizacionCuf", Text(original.OriginalCuf));
        w.WriteElementString("fechaEmisionFactura", FiscalRules.FormatDateTime(original.OriginalIssuedAt));
        w.WriteElementString("montoTotalOriginal", Amount(d.OriginalTotal, 2));
        w.WriteElementString("montoTotalDevuelto", Amount(d.ReturnedTotal, 2));
        OptionalAmount(w, "montoDescuentoCreditoDebito", original.DiscountShare ?? 0m, 2);
        w.WriteElementString("montoEfectivoCreditoDebito", Amount(d.VatAmount, 2));
        w.WriteElementString("codigoExcepcion", Integer(d.ExceptionCode));
        WriteClosing(w, d);
        foreach (var line in d.Lines.OrderBy(l => l.LineNumber))
        {
            w.WriteStartElement("detalle");
            WriteLineStart(w, line);
            w.WriteElementString("cantidad", Amount(line.Quantity, 10));
            w.WriteElementString("unidadMedida", Integer(line.SinUnitCode));
            w.WriteElementString("precioUnitario", Amount(line.UnitPrice, 10));
            OptionalAmount(w, "montoDescuento", line.Discount ?? 0m, 10);
            w.WriteElementString("subTotal", Amount(line.Subtotal, 10));
            w.WriteElementString("codigoDetalleTransaccion", line.TransactionCode is { } tx ? Integer(tx) : string.Empty);
            w.WriteEndElement();
        }
    }

    // ------------------------------------------------------------------------------------------------ bloques comunes
    /// <summary>nitEmisor, razonSocialEmisor, municipio, telefono.</summary>
    private static void WriteIssuerStart(XmlWriter w, FiscalXmlContext c)
    {
        w.WriteElementString("nitEmisor", Integer(c.Nit));
        w.WriteElementString("razonSocialEmisor", Text(c.BusinessName));
        w.WriteElementString("municipio", Text(c.Municipality));
        Optional(w, "telefono", c.Phone);
    }

    /// <summary>cuf … codigoCliente (idénticos en la factura y en la nota, después del número del documento).</summary>
    private static void WriteDocumentPlace(XmlWriter w, FiscalDocument d, FiscalXmlContext c)
    {
        w.WriteElementString("cuf", Text(d.Cuf));
        w.WriteElementString("cufd", Text(c.CufdCode));
        w.WriteElementString("codigoSucursal", Integer(c.BranchCode));
        w.WriteElementString("direccion", Text(c.Address));
        // C-24: siempre el valor numérico (0 = sin punto de venta), igual que en la solicitud y en el CUF.
        w.WriteElementString("codigoPuntoVenta", Integer(c.PointOfSaleCode));
        w.WriteElementString("fechaEmision", FiscalRules.FormatDateTime(d.IssuedAt));
        Optional(w, "nombreRazonSocial", d.BuyerName);
        w.WriteElementString("codigoTipoDocumentoIdentidad", Integer(d.BuyerDocumentType));
        w.WriteElementString("numeroDocumento", Text(d.BuyerDocumentNumber));
        Optional(w, "complemento", d.BuyerComplement);
        w.WriteElementString("codigoCliente", Text(d.CustomerCode));
    }

    /// <summary>leyenda, usuario, codigoDocumentoSector y fin de la cabecera.</summary>
    private static void WriteClosing(XmlWriter w, FiscalDocument d)
    {
        w.WriteElementString("leyenda", SingleLine(d.Legend));
        w.WriteElementString("usuario", Text(d.UserCode));
        w.WriteElementString("codigoDocumentoSector", Integer(d.DocumentSector));
        w.WriteEndElement();
    }

    /// <summary>actividadEconomica, codigoProductoSin, codigoProducto, descripcion.</summary>
    private static void WriteLineStart(XmlWriter w, FiscalDocumentLine line)
    {
        w.WriteElementString("actividadEconomica", Text(line.ActivityCode));
        w.WriteElementString("codigoProductoSin", Integer(line.SinProductCode));
        w.WriteElementString("codigoProducto", Text(line.ProductCode));
        w.WriteElementString("descripcion", Text(line.Description));
    }

    private static string Write(FiscalDocument document, FiscalXmlContext context, Action<XmlWriter, FiscalDocument, FiscalXmlContext> body)
    {
        var (root, xsd) = SiatSchemas.For(document.DocumentSector);
        var sb = new StringBuilder(4096).Append(Declaration).Append('\n');
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = "    ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            ConformanceLevel = ConformanceLevel.Document,
        };
        using (var w = XmlWriter.Create(sb, settings))
        {
            w.WriteStartElement(root);
            w.WriteAttributeString("xmlns", "xsi", null, XsiNamespace);
            w.WriteAttributeString("xsi", "noNamespaceSchemaLocation", XsiNamespace, xsd);
            w.WriteStartElement("cabecera");
            body(w, document, context);
            w.WriteEndElement();
        }
        return sb.ToString();
    }

    /// <summary>Elemento nillable: el valor o <c>xsi:nil="true"</c> si no hay (nunca se omite ni va vacío).</summary>
    private static void Optional(XmlWriter w, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Nil(w, name);
        }
        else
        {
            w.WriteElementString(name, Text(value));
        }
    }

    /// <summary>Monto nillable: 0 = «no aplica» → <c>xsi:nil="true"</c> (descuentos, gift card, prorrateo de la nota).</summary>
    private static void OptionalAmount(XmlWriter w, string name, decimal value, int decimals)
    {
        if (value == 0m)
        {
            Nil(w, name);
        }
        else
        {
            w.WriteElementString(name, Amount(value, decimals));
        }
    }

    private static void Nil(XmlWriter w, string name)
    {
        w.WriteStartElement(name);
        w.WriteAttributeString("xsi", "nil", XsiNamespace, "true");
        w.WriteEndElement();
    }

    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Número con punto y al menos 2 decimales (hasta <paramref name="decimals"/>). NO redondea: si el valor tiene más
    /// decimales de los que admite el XSD se escribe tal cual y la validación lo rechaza (un monto nunca se altera en
    /// silencio).
    /// </summary>
    private static string Amount(decimal value, int decimals)
    {
        if (!FiscalRules.HasAtMostDecimals(value, decimals))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        var format = decimals <= 2 ? "0.00" : "0.00" + new string('#', decimals - 2);
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Texto sin caracteres que XML no admite (controles, sustitutos sueltos), recortado.</summary>
    private static string Text(string? value)
    {
        var s = value ?? string.Empty;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (char.IsHighSurrogate(ch) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                sb.Append(ch).Append(s[++i]);
            }
            else if (XmlConvert.IsXmlChar(ch) && (ch >= ' ' || ch is '\t' or '\n' or '\r'))
            {
                sb.Append(ch);
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>Leyenda en una sola línea: sin saltos ni espacios añadidos (cuentan para el máximo de 200 del XSD).</summary>
    private static string SingleLine(string? value) =>
        string.Join(' ', Text(value).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data);
        }
        return output.ToArray();
    }
}
