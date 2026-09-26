using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Application.Billing;

/// <summary>
/// V4.1 · Arma la representación gráfica (<see cref="FiscalPrintModel"/>) de un documento fiscal a partir de lo guardado
/// (documento, líneas, XML, configuración del ambiente para la URL del QR). La usan la consulta de impresión, el envío
/// por correo y la caja.
/// </summary>
/// <remarks>
/// Es fiel AL MOMENTO DE LA EMISIÓN: el emisor (razón social, NIT, municipio, teléfono, dirección, sucursal y punto de venta)
/// sale del XML exacto que se envió al SIN, no de la configuración de hoy; la leyenda de la Ley 453 es la elegida al emitir
/// (investigación 06 §13-§14). Título y subtítulo según el tipo (06 §10), etiqueta «CASA MATRIZ» o «SUCURSAL N° x», monto
/// literal «Son: …» del monto a pagar (en notas, del monto devuelto), QR = URL base del ambiente + nit, cuf, numero y t=2
/// (media hoja; el rollo usa t=1) y la marca «SIN VALOR LEGAL» en el ambiente de pruebas.
/// </remarks>
public static class FiscalPrintModelBuilder
{
    public const string InvoiceTitle = "FACTURA";
    public const string InvoiceSubtitle = "(Con Derecho a Crédito Fiscal)";
    public const string NoteTitle = "NOTA CRÉDITO - DÉBITO";

    public static async Task<FiscalPrintModel> BuildAsync(IMinvDbContext db, Guid documentId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var document = await db.Set<FiscalDocument>().AsNoTracking().Include(d => d.Lines).Include(d => d.NoteReference)
                           .FirstOrDefaultAsync(d => d.Id == documentId, ct)
                       ?? throw new NotFoundException("El documento fiscal no existe (o es de una sucursal que no es suya).");
        var file = await db.Set<FiscalDocumentFile>().AsNoTracking().Where(f => f.DocumentId == documentId).OrderByDescending(f => f.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var issuer = file is null ? null : IssuerData.FromXml(file.Xml);
        var settings = await db.Set<SiatSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        var profile = await db.Set<SiatEnvironmentProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.Environment == document.Environment, ct)
                      ?? throw new DomainException("siat.no_profile",
                          $"Falta la configuración de conexión del ambiente {document.Environment} (con la URL base del QR) para imprimir el documento.");
        var mapping = await db.Set<SiatBranch>().AsNoTracking().FirstOrDefaultAsync(b => b.BranchId == document.BranchId, ct);
        var cuf = Cuf.DecodeFull(document.Cuf);
        var cufdAddress = issuer?.Address is null
            ? await db.Set<SiatCufd>().AsNoTracking().Where(c => c.Id == document.CufdId).Select(c => c.Address).FirstOrDefaultAsync(ct)
            : null;

        var nit = issuer?.Nit ?? cuf?.Nit ?? settings?.Nit ?? 0;
        var branchCode = issuer?.BranchCode ?? cuf?.BranchCode ?? mapping?.SiatCode ?? 0;
        var pointOfSaleCode = issuer?.PointOfSaleCode ?? cuf?.PointOfSaleCode ?? 0;

        // Líneas con la DESCRIPCIÓN de la unidad (no el código) y el código interno del producto (no el del SIN)
        var units = await FiscalCatalogText.DescriptionsAsync(db, SiatCatalogNames.UnitsOfMeasure, document.Lines.Select(l => l.SinUnitCode).Distinct().ToList(), ct);
        var lines = document.Lines.OrderBy(l => l.LineNumber).Select(l => new FiscalPrintLine(l.ProductCode, l.Description,
            units.GetValueOrDefault(l.SinUnitCode, l.SinUnitCode.ToString(CultureInfo.InvariantCulture)), l.Quantity, l.UnitPrice, l.Discount ?? 0m,
            l.Subtotal, l.TransactionCode)).ToList();

        // Medio de pago con la descripción del catálogo (la tarjeta, solo enmascarada) y el cajero que emitió
        string? payment = null;
        if (document.PaymentMethodCode is { } method)
        {
            payment = (await FiscalCatalogText.DescriptionsAsync(db, SiatCatalogNames.PaymentMethods, [method], ct)).GetValueOrDefault(method)
                      ?? method.ToString(CultureInfo.InvariantCulture);
            if (document.CardNumberMasked is { Length: > 0 } card)
            {
                payment += " · " + card;
            }
        }
        var issuedBy = await db.Set<FiscalDocumentEvent>().AsNoTracking()
            .Where(e => e.DocumentId == documentId && e.Action == FiscalDocumentAction.Issued && e.UserId != null)
            .OrderBy(e => e.OccurredAt).Select(e => e.UserId).FirstOrDefaultAsync(ct);
        var cashier = issuedBy is { } userId
            ? await db.Set<User>().AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        // Venta de M-INV: la facturada o, en una nota, la devuelta (o la de la factura original)
        var saleInvoiceId = document.InvoiceId
                            ?? (document.SalesReturnId is { } returnId
                                ? await db.Set<SalesReturn>().AsNoTracking().Where(r => r.Id == returnId).Select(r => (Guid?)r.InvoiceId).FirstOrDefaultAsync(ct)
                                : null)
                            ?? (document.NoteReference?.OriginalDocumentId is { } originalId
                                ? await db.Set<FiscalDocument>().AsNoTracking().Where(d => d.Id == originalId).Select(d => d.InvoiceId).FirstOrDefaultAsync(ct)
                                : null);
        var saleNumber = saleInvoiceId is { } invoiceId
            ? await db.Set<Invoice>().AsNoTracking().Where(i => i.Id == invoiceId).Select(i => i.Number).FirstOrDefaultAsync(ct)
            : null;

        var legends = new List<string>
        {
            SiatSettings.FixedLegend,
            document.Legend,
            document.EmissionType == SiatCodes.EmissionOffline
                ? settings?.OfflineLegend ?? SiatSettings.DefaultOfflineLegend
                : settings?.OnlineLegend ?? SiatSettings.DefaultOnlineLegend,
        };
        var isNote = document.Kind == FiscalDocumentKind.CreditDebitNote;
        var qr = QrUrl(profile.QrBaseUrl, nit, document.Cuf, document.Number, 2);

        if (isNote)
        {
            var note = document.NoteReference ?? throw new DomainException("fiscal.note_reference", "La nota no tiene los datos de la factura original.");
            var returnedSubtotal = document.Lines.Where(l => l.TransactionCode == 2).Sum(l => l.Subtotal);
            var returned = document.ReturnedTotal;
            return new FiscalPrintModel(NoteTitle, string.Empty, issuer?.BusinessName ?? settings?.BusinessName ?? string.Empty, nit,
                BranchLabel(branchCode), pointOfSaleCode, issuer?.Address ?? cufdAddress ?? string.Empty, issuer is null ? mapping?.Phone : issuer.Phone,
                issuer?.Municipality ?? mapping?.Municipality ?? string.Empty, document.Number, document.Cuf, document.IssuedAt,
                document.BuyerName ?? "S/N", FiscalIssuedStatus.BuyerDocument(document), document.CustomerCode, lines, returnedSubtotal,
                note.DiscountShare ?? 0m, returned, 0m, returned, returned, "Son: " + AmountInWords.Bolivianos(returned), payment,
                cashier ?? document.UserCode, legends, qr, document.Environment == SiatCodes.EnvironmentTest, document.Status == FiscalDocumentStatus.Voided,
                document.EmissionType == SiatCodes.EmissionOffline, new FiscalPrintOriginal(note.OriginalNumber, note.OriginalCuf, note.OriginalIssuedAt),
                returned, document.VatAmount, saleNumber);
        }
        var amountToPay = document.TotalSubjectToVat;   // montoTotal − gift card
        return new FiscalPrintModel(InvoiceTitle, InvoiceSubtitle, issuer?.BusinessName ?? settings?.BusinessName ?? string.Empty, nit,
            BranchLabel(branchCode), pointOfSaleCode, issuer?.Address ?? cufdAddress ?? string.Empty, issuer is null ? mapping?.Phone : issuer.Phone,
            issuer?.Municipality ?? mapping?.Municipality ?? string.Empty, document.Number, document.Cuf, document.IssuedAt, document.BuyerName ?? "S/N",
            FiscalIssuedStatus.BuyerDocument(document), document.CustomerCode, lines, document.LinesSubtotal, document.AdditionalDiscount,
            document.TotalAmount, document.GiftCardAmount, amountToPay, document.TotalSubjectToVat, "Son: " + AmountInWords.Bolivianos(amountToPay), payment,
            cashier ?? document.UserCode, legends, qr, document.Environment == SiatCodes.EnvironmentTest, document.Status == FiscalDocumentStatus.Voided,
            document.EmissionType == SiatCodes.EmissionOffline, SaleNumber: saleNumber);
    }

    /// <summary>«CASA MATRIZ» (código 0 del Padrón) o «SUCURSAL N° x».</summary>
    public static string BranchLabel(int siatBranchCode) =>
        siatBranchCode == 0 ? "CASA MATRIZ" : $"SUCURSAL N° {siatBranchCode.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>URL del QR del SIN: base del ambiente + ?nit=&amp;cuf=&amp;numero=&amp;t= (1 rollo, 2 media hoja; investigación 06 §7).</summary>
    public static string QrUrl(string baseUrl, long nit, string cuf, long number, int size)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return string.Create(CultureInfo.InvariantCulture, $"{baseUrl.TrimEnd('?', '&')}{separator}nit={nit}&cuf={Uri.EscapeDataString(cuf)}&numero={number}&t={size}");
    }

    /// <summary>La misma URL del QR con otro tamaño de previsualización (t=1 rollo, t=2 media hoja).</summary>
    public static string WithQrSize(string qrUrl, int size)
    {
        var index = qrUrl.LastIndexOf("&t=", StringComparison.Ordinal);
        return (index < 0 ? qrUrl : qrUrl[..index]) + "&t=" + size.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Datos del emisor tal como viajaron en el XML (cabecera del sector 1 o 24).</summary>
    private sealed record IssuerData(long? Nit, string? BusinessName, string? Municipality, string? Phone, int? BranchCode, int? PointOfSaleCode,
        string? Address)
    {
        private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

        public static IssuerData? FromXml(string xml)
        {
            XElement? header;
            try
            {
                header = XDocument.Parse(xml).Root?.Element("cabecera");
            }
            catch (XmlException)
            {
                return null;
            }
            if (header is null)
            {
                return null;
            }
            string? Text(string name)
            {
                var element = header.Element(name);
                if (element is null || (string?)element.Attribute(Xsi + "nil") == "true")
                {
                    return null;
                }
                var value = element.Value.Trim();
                return value.Length == 0 ? null : value;
            }
            long? Long(string name) => long.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
            int? Int(string name) => int.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
            return new IssuerData(Long("nitEmisor"), Text("razonSocialEmisor"), Text("municipio"), Text("telefono"), Int("codigoSucursal"),
                Int("codigoPuntoVenta"), Text("direccion"));
        }
    }
}
