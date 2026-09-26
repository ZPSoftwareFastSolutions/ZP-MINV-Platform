using System.Globalization;
using System.Text;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;

namespace MINV.Hardware.EscPos;

/// <summary>
/// V4.1 · Representación gráfica en ROLLO (ESC/POS, 80 mm = 48 columnas o 58 mm = 32): la misma información que la hoja
/// en una sola columna (investigación 06 §12): emisor centrado, «FACTURA» y «CON DERECHO A CRÉDITO FISCAL» (o «NOTA
/// CRÉDITO - DÉBITO»), NIT, número, CUF partido en líneas, comprador, líneas «cantidad x precio = subtotal», totales,
/// «Son: …», las tres leyendas y el QR nativo de la impresora con la URL del SIN (t=1, rollo). Acentos en PC858.
/// </summary>
public sealed class FiscalRollRenderer : IFiscalRollRenderer
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public byte[] RenderRoll(FiscalPrintModel model, int columns = 48)
    {
        ArgumentNullException.ThrowIfNull(model);
        var width = columns is >= 24 and <= 64 ? columns : 48;
        var isNote = model.Original is not null;
        var doc = new EscPosDocument(width).Align(TextAlign.Center).Bold(true);
        foreach (var line in Wrap(model.IssuerName, width))
        {
            doc.Line(line);
        }
        doc.Bold(false);
        Centered(doc, width, model.BranchLabel, $"No. Punto de Venta {model.PointOfSaleCode.ToString(Invariant)}", model.Address,
            string.IsNullOrWhiteSpace(model.Phone) ? null : "Teléfono: " + model.Phone.Trim(), model.Municipality);
        if (model.IsTest)
        {
            doc.Bold(true).Line("*** SIN VALOR LEGAL ***").Bold(false);
        }
        if (model.IsVoided)
        {
            doc.Bold(true).Line("*** ANULADO ***").Bold(false);
        }
        doc.Separator().Bold(true).Size(1, 2);
        foreach (var line in Wrap(model.Title, width))
        {
            doc.Line(line);
        }
        doc.Size(1, 1).Bold(false);
        var subtitle = (model.Subtitle ?? string.Empty).Trim().Trim('(', ')').Trim();
        if (subtitle.Length > 0)
        {
            Centered(doc, width, subtitle.ToUpper(CultureInfo.GetCultureInfo("es-BO")));
        }
        if (model.IsOffline)
        {
            doc.Line("FUERA DE LÍNEA");
        }

        doc.Align(TextAlign.Left).Separator();
        Pair(doc, width, "NIT", model.IssuerNit.ToString(Invariant));
        Pair(doc, width, isNote ? "NOTA N°" : "FACTURA N°", model.Number.ToString(Invariant));
        doc.Line("CÓD. AUTORIZACIÓN:");
        foreach (var piece in Chunks(model.Cuf, width))
        {
            doc.Line(piece);
        }
        doc.Separator();
        Labeled(doc, width, "Fecha: ", Date(model.IssuedAt));
        Labeled(doc, width, "Nombre/Razón Social: ", model.BuyerName);
        Labeled(doc, width, "NIT/CI/CEX: ", model.BuyerDocument);
        Labeled(doc, width, "Cod. Cliente: ", model.CustomerCode);
        if (model.Original is { } original)
        {
            Labeled(doc, width, "N° Factura: ", original.Number.ToString(Invariant));
            Labeled(doc, width, "Fecha Factura: ", Date(original.IssuedAt));
            doc.Line("N° Autorización/CUF:");
            foreach (var piece in Chunks(original.Cuf, width))
            {
                doc.Line(piece);
            }
        }
        if (!string.IsNullOrWhiteSpace(model.PaymentMethod))
        {
            Labeled(doc, width, "Método de pago: ", model.PaymentMethod);
        }
        if (!string.IsNullOrWhiteSpace(model.Cashier))
        {
            Labeled(doc, width, "Cajero: ", model.Cashier);
        }

        if (isNote)
        {
            NoteBody(doc, width, model);
        }
        else
        {
            doc.Separator();
            Lines(doc, width, model.Lines, decimals: 2);
            doc.Separator();
            Pair(doc, width, "SUBTOTAL Bs", Money(model.Subtotal));
            Pair(doc, width, "DESCUENTO Bs", Money(model.Discount));
            Pair(doc, width, "TOTAL Bs", Money(model.Total));
            Pair(doc, width, "MONTO GIFT CARD Bs", Money(model.GiftCard));
            doc.Bold(true);
            Pair(doc, width, "MONTO A PAGAR Bs", Money(model.AmountToPay));
            Pair(doc, width, "IMPORTE BASE CRÉDITO FISCAL", Money(model.TaxBase));
            doc.Bold(false);
        }
        if (!string.IsNullOrWhiteSpace(model.AmountInWords))
        {
            doc.Feed(1);
            foreach (var line in Wrap(SonPrefix(model.AmountInWords), width))
            {
                doc.Line(line);
            }
        }

        doc.Separator().Align(TextAlign.Center);
        foreach (var legend in model.Legends.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            foreach (var line in Wrap(Plain(legend), width))
            {
                doc.Line(line);
            }
            doc.Feed(1);
        }
        if (!string.IsNullOrWhiteSpace(model.QrUrl))
        {
            doc.Qr(WithQrSize(model.QrUrl, 1), moduleSize: 6).Feed(1);
        }
        return doc.Feed(3).Cut().ToArray();
    }

    /// <summary>Fija el parámetro <c>t</c> de la URL del QR del SIN (1 = rollo).</summary>
    public static string WithQrSize(string url, int size)
    {
        var parts = url.Trim().Split('?', 2);
        var query = parts.Length > 1 ? parts[1].Split('&', StringSplitOptions.RemoveEmptyEntries).ToList() : new List<string>();
        query.RemoveAll(p => p.Equals("t", StringComparison.OrdinalIgnoreCase) || p.StartsWith("t=", StringComparison.OrdinalIgnoreCase));
        query.Add("t=" + size.ToString(Invariant));
        return parts[0] + "?" + string.Join('&', query);
    }

    private static void NoteBody(EscPosDocument doc, int width, FiscalPrintModel model)
    {
        var original = model.Lines.Where(l => l.TransactionCode is null or 1).ToList();
        var returned = model.Lines.Where(l => l.TransactionCode == 2).ToList();
        var returnedSubtotal = returned.Sum(l => l.Subtotal);
        var returnedTotal = model.ReturnedTotal ?? (returnedSubtotal - model.Discount);
        var effective = model.CreditDebitAmount ?? FiscalRules.Vat(returnedTotal);
        doc.Separator().Bold(true);
        foreach (var line in Wrap("DETALLE DOCUMENTO ORIGEN", width))
        {
            doc.Line(line);
        }
        doc.Bold(false);
        Lines(doc, width, original, decimals: 10);
        doc.Bold(true);
        Pair(doc, width, "MONTO TOTAL ORIGINAL Bs", Money(original.Sum(l => l.Subtotal)));
        doc.Bold(false).Separator().Bold(true);
        foreach (var line in Wrap("DETALLE DE LA DEVOLUCIÓN O RESCISIÓN DE SERVICIO", width))
        {
            doc.Line(line);
        }
        doc.Bold(false);
        Lines(doc, width, returned, decimals: 10);
        if (returnedSubtotal != returnedTotal)
        {
            Pair(doc, width, "SUBTOTAL Bs", Money(returnedSubtotal));
            Pair(doc, width, "MONTO DESCUENTO CRÉDITO DÉBITO Bs", Money(returnedSubtotal - returnedTotal));
        }
        doc.Bold(true);
        Pair(doc, width, "MONTO TOTAL DEVUELTO Bs", Money(returnedTotal));
        Pair(doc, width, "MONTO EFECTIVO DEL CRÉDITO O DÉBITO (13%) Bs", Money(effective));
        doc.Bold(false);
    }

    /// <summary>Cada línea: código y descripción; debajo «cantidad unidad x precio - descuento = subtotal».</summary>
    private static void Lines(EscPosDocument doc, int width, IReadOnlyList<FiscalPrintLine> lines, int decimals)
    {
        var quantityFormat = decimals <= 2 ? "#,##0.00" : "#,##0.00" + new string('#', decimals - 2);
        foreach (var l in lines)
        {
            foreach (var text in Wrap($"{l.ProductCode} {l.Description}", width))
            {
                doc.Line(text);
            }
            var detail = $"  {l.Quantity.ToString(quantityFormat, Invariant)} {l.Unit} x {Money(l.UnitPrice)}";
            if (l.Discount != 0)
            {
                detail += $" - {Money(l.Discount)}";
            }
            Pair(doc, width, detail + " =", Money(l.Subtotal));
        }
    }

    /// <summary>Etiqueta a la izquierda y valor a la derecha; si no entran en una línea, el valor va en la siguiente.</summary>
    private static void Pair(EscPosDocument doc, int width, string label, string value)
    {
        if (label.Length + value.Length + 1 <= width)
        {
            doc.Columns2(label, value);
            return;
        }
        foreach (var line in Wrap(label, width))
        {
            doc.Line(line);
        }
        doc.Line(value.PadLeft(width));
    }

    private static void Labeled(EscPosDocument doc, int width, string label, string? value)
    {
        foreach (var line in Wrap(label + (value ?? string.Empty).Trim(), width))
        {
            doc.Line(line);
        }
    }

    private static void Centered(EscPosDocument doc, int width, params string?[] texts)
    {
        foreach (var text in texts.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            foreach (var line in Wrap(text!, width))
            {
                doc.Line(line);
            }
        }
    }

    /// <summary>Corta por palabras en líneas de <paramref name="width"/> columnas (una palabra larga se corta).</summary>
    internal static IReadOnlyList<string> Wrap(string? text, int width)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = word;
            while (rest.Length > 0)
            {
                var space = current.Length == 0 ? width : width - current.Length - 1;
                if (rest.Length <= space)
                {
                    if (current.Length > 0)
                    {
                        current.Append(' ');
                    }
                    current.Append(rest);
                    rest = string.Empty;
                }
                else if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    lines.Add(rest[..width]);
                    rest = rest[width..];
                }
            }
        }
        if (current.Length > 0 || lines.Count == 0)
        {
            lines.Add(current.ToString());
        }
        return lines;
    }

    private static IEnumerable<string> Chunks(string? text, int width)
    {
        var s = (text ?? string.Empty).Trim();
        for (var i = 0; i < s.Length; i += width)
        {
            yield return s.Substring(i, Math.Min(width, s.Length - i));
        }
    }

    /// <summary>Comillas tipográficas a rectas (PC858 no las tiene).</summary>
    private static string Plain(string text) =>
        text.Trim().Replace('“', '"').Replace('”', '"').Replace('‘', '\'').Replace('’', '\'').Replace('–', '-').Replace('—', '-');

    private static string Money(decimal value) => value.ToString("#,##0.00", Invariant);

    private static string Date(DateTime value) => value.ToString("dd/MM/yyyy hh:mm tt", Invariant);

    private static string SonPrefix(string words)
    {
        var text = words.Trim();
        return text.StartsWith("Son:", StringComparison.OrdinalIgnoreCase) ? text : "Son: " + text;
    }
}
