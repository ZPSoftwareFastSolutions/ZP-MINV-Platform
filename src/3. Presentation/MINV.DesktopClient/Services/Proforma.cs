using System.Globalization;
using System.Text;
using MINV.Application.Tech;
using MINV.Hardware.EscPos;
using MINV.Infrastructure.Billing.Rendering;

namespace MINV.DesktopClient.Services;

/// <summary>V4.2 · Pieza de la proforma de un armado: ranura, producto, cantidad, precio cotizado y meses de garantía.</summary>
public sealed record ProformaLine(string Slot, string Sku, string Name, int Quantity, decimal UnitPrice, decimal Subtotal, int WarrantyMonths);

/// <summary>V4.2 · Proforma (cotización) de un armado de PC: lo que se entrega al cliente antes de venderle.</summary>
public sealed record ProformaModel(string Company, string? TaxId, string Branch, string Number, string Name, string? Customer, DateTimeOffset CreatedAt,
    DateOnly ValidUntil, IReadOnlyList<ProformaLine> Lines, decimal Total, bool IsCompatible, bool QuotedWithErrors, IReadOnlyList<string> Notes,
    string CurrencySymbol, string? Seller);

/// <summary>
/// V4.2 · Proforma del armador en tres salidas con el mismo contenido: texto de ticket (vista previa en pantalla), rollo
/// ESC/POS (impresora de la caja, <see cref="EscPosDocument"/>) y PDF hoja carta (reutiliza el escritor de PDF sin
/// dependencias de la facturación, <see cref="PdfDocumentWriter"/>). La garantía se imprime en meses: la fecha exacta
/// («Garantía hasta…») sale de la venta (regla T-04).
/// </summary>
public static class PcBuildProforma
{
    public const string Title = "PROFORMA · ARMADO DE PC";
    public const string NotInvoice = "Documento sin valor fiscal: no es una factura.";

    public static string Validity(ProformaModel m) =>
        $"Precios congelados, válidos hasta el {m.ValidUntil.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} (sujeto a stock).";

    private static string Money(decimal value) => value.ToString("#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>Resultado de la revisión de compatibilidad (regla T-06: un armado con errores solo se cotiza si el cliente lo
    /// acepta expresamente, y la proforma lo dice).</summary>
    public static string CompatibilityNote(ProformaModel m) => m.IsCompatible
        ? "Compatibilidad revisada: sin errores."
        : m.QuotedWithErrors
            ? "el cliente aceptó cotizar el armado con errores de compatibilidad."
            : "el armado tiene errores de compatibilidad.";

    /// <summary>Texto de ancho fijo (vista previa y ticket).</summary>
    public static string Text(ProformaModel m, int width = 42)
    {
        var sb = new StringBuilder();
        void Center(string text)
        {
            foreach (var part in FiscalTicketText.Wrap(text, width))
            {
                sb.AppendLine(new string(' ', Math.Max(0, (width - part.Length) / 2)) + part);
            }
        }
        void Pair(string left, string right) => sb.AppendLine(left.Length + right.Length + 1 > width
            ? left[..Math.Max(0, width - right.Length - 1)] + " " + right
            : left + new string(' ', width - left.Length - right.Length) + right);
        void Wrapped(string text, string indent = "")
        {
            foreach (var part in FiscalTicketText.Wrap(text, width - indent.Length))
            {
                sb.AppendLine(indent + part);
            }
        }

        Center(m.Company);
        if (m.TaxId is { Length: > 0 } nit)
        {
            Center("NIT " + nit);
        }
        Center(m.Branch);
        sb.AppendLine(new string('-', width));
        Center(Title);
        Pair("N°", m.Number);
        Pair("Fecha", m.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
        Wrapped("Armado: " + m.Name);
        Wrapped("Cliente: " + (m.Customer ?? "—"));
        if (m.Seller is { Length: > 0 } seller)
        {
            Wrapped("Atendió: " + seller);
        }
        sb.AppendLine(new string('-', width));
        foreach (var line in m.Lines)
        {
            Wrapped($"{line.Slot}: {line.Name}");
            Pair($"  {line.Quantity} x {Money(line.UnitPrice)}", Money(line.Subtotal));
            if (line.WarrantyMonths > 0)
            {
                sb.AppendLine("  " + TechPrint.WarrantyMonths(line.WarrantyMonths));
            }
        }
        sb.AppendLine(new string('-', width));
        Pair("TOTAL " + m.CurrencySymbol, Money(m.Total));
        sb.AppendLine(new string('-', width));
        Wrapped(m.IsCompatible ? CompatibilityNote(m) : "ATENCIÓN: " + CompatibilityNote(m));
        foreach (var note in m.Notes)
        {
            Wrapped("· " + note);
        }
        Wrapped(Validity(m));
        Wrapped(NotInvoice);
        return sb.ToString().TrimEnd();
    }

    /// <summary>Rollo ESC/POS (48 columnas en 80 mm, 32 en 58 mm) con corte.</summary>
    public static byte[] Roll(ProformaModel m, int columns = 48)
    {
        var doc = new EscPosDocument(columns);
        foreach (var line in Text(m, columns).Split('\n'))
        {
            doc.Line(line.TrimEnd('\r'));
        }
        return doc.Feed(3).Cut().ToArray();
    }

    /// <summary>PDF hoja carta: encabezado, tabla de piezas con garantía, total y condiciones.</summary>
    public static byte[] Pdf(ProformaModel m)
    {
        const double left = 40, right = 572, bottom = 740;
        var pdf = new PdfDocumentWriter { Title = $"Proforma {m.Number}", Author = m.Company, Subject = m.Name, CreatedAt = m.CreatedAt.LocalDateTime };
        var page = pdf.AddPage();
        var y = 50d;
        page.Text(left, y, m.Company, PdfFont.HelveticaBold, 13);
        page.TextRight(right, y, Title, PdfFont.HelveticaBold, 12);
        y += 15;
        if (m.TaxId is { Length: > 0 } nit)
        {
            page.Text(left, y, "NIT " + nit, PdfFont.Helvetica, 9);
        }
        page.TextRight(right, y, "N° " + m.Number, PdfFont.HelveticaBold, 10);
        y += 12;
        page.Text(left, y, m.Branch, PdfFont.Helvetica, 9);
        page.TextRight(right, y, "Fecha " + m.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), PdfFont.Helvetica, 9);
        y += 22;
        page.Line(left, y, right, y, 0.8);
        y += 16;
        page.Text(left, y, "Armado:", PdfFont.HelveticaBold, 9).Text(left + 60, y, m.Name, PdfFont.Helvetica, 9);
        y += 13;
        page.Text(left, y, "Cliente:", PdfFont.HelveticaBold, 9).Text(left + 60, y, m.Customer ?? "—", PdfFont.Helvetica, 9);
        if (m.Seller is { Length: > 0 } seller)
        {
            page.TextRight(right, y, "Atendió: " + seller, PdfFont.Helvetica, 9);
        }
        y += 22;

        // Tabla: pieza · producto · cant. · precio · subtotal · garantía
        double[] widths = [96, 206, 40, 70, 70, 50];
        string[] headers = ["PIEZA", "PRODUCTO", "CANT.", "PRECIO", "SUBTOTAL", "GARANTÍA"];
        void Row(IReadOnlyList<string> cells, PdfFont font, bool header)
        {
            var wrapped = cells.Select((c, i) => i is 0 or 1 ? PdfFontMetrics.Wrap(c, font, 8, widths[i] - 6) : (IReadOnlyList<string>)[c]).ToList();
            var height = Math.Max(16, wrapped.Max(w => w.Count) * 9.5 + 6);
            if (y + height > bottom)
            {
                page = pdf.AddPage();
                y = 50;
            }
            var x = left;
            for (var i = 0; i < widths.Length; i++)
            {
                if (header)
                {
                    page.FillRectangle(x, y, widths[i], height, 0.92);
                }
                page.Rectangle(x, y, widths[i], height);
                for (var j = 0; j < wrapped[i].Count; j++)
                {
                    var baseline = y + 11 + (j * 9.5);
                    if (i >= 2 && !header)
                    {
                        page.TextRight(x + widths[i] - 3, baseline, wrapped[i][j], font, 8);
                    }
                    else
                    {
                        page.Text(x + 3, baseline, wrapped[i][j], font, 8);
                    }
                }
                x += widths[i];
            }
            y += height;
        }
        Row(headers, PdfFont.HelveticaBold, header: true);
        foreach (var line in m.Lines)
        {
            Row([line.Slot, $"{line.Sku} · {line.Name}", line.Quantity.ToString(CultureInfo.InvariantCulture), Money(line.UnitPrice), Money(line.Subtotal),
                line.WarrantyMonths > 0 ? $"{line.WarrantyMonths} meses" : "—"], PdfFont.Helvetica, header: false);
        }
        y += 10;
        page.TextRight(right - 80, y + 4, "TOTAL " + m.CurrencySymbol, PdfFont.HelveticaBold, 11).TextRight(right, y + 4, Money(m.Total), PdfFont.HelveticaBold, 11);
        y += 30;
        var notes = new List<string>
        {
            m.IsCompatible ? CompatibilityNote(m) : "Atención: " + CompatibilityNote(m),
        };
        notes.AddRange(m.Notes);
        notes.Add(Validity(m));
        notes.Add("La garantía de cada pieza se cuenta desde la fecha de la venta (se imprime «Garantía hasta dd/mm/aaaa» en la factura).");
        notes.Add(NotInvoice);
        foreach (var note in notes)
        {
            foreach (var part in PdfFontMetrics.Wrap(note, PdfFont.Helvetica, 8.5, right - left))
            {
                if (y > bottom)
                {
                    page = pdf.AddPage();
                    y = 50;
                }
                page.Text(left, y, part, PdfFont.Helvetica, 8.5, gray: 0.25);
                y += 11;
            }
        }
        return pdf.ToArray();
    }
}
