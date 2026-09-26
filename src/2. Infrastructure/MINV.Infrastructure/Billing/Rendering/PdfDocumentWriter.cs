using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace MINV.Infrastructure.Billing.Rendering;

/// <summary>
/// V4.1 · Escritor de PDF 1.4 mínimo y sin dependencias: páginas del mismo tamaño, fuentes estándar Type1 (Helvetica,
/// Helvetica-Bold y Helvetica-BoldOblique con WinAnsiEncoding), texto, líneas, rectángulos y rellenos en escala de grises,
/// diccionario de información, tabla xref y trailer. El flujo de contenido va sin comprimir (texto legible, útil para
/// auditar y probar) o con FlateDecode si <see cref="Compress"/> es verdadero. La salida es determinista.
/// </summary>
public sealed class PdfDocumentWriter
{
    private readonly List<PdfPage> _pages = new();

    public PdfDocumentWriter(double pageWidth = 612, double pageHeight = 792)
    {
        PageWidth = pageWidth > 0 ? pageWidth : throw new ArgumentOutOfRangeException(nameof(pageWidth));
        PageHeight = pageHeight > 0 ? pageHeight : throw new ArgumentOutOfRangeException(nameof(pageHeight));
    }

    /// <summary>Ancho de página en puntos (612 = carta).</summary>
    public double PageWidth { get; }

    /// <summary>Alto de página en puntos (792 = carta).</summary>
    public double PageHeight { get; }

    public bool Compress { get; set; }

    public string? Title { get; set; }

    public string? Author { get; set; }

    public string? Subject { get; set; }

    public string? Keywords { get; set; }

    public string Creator { get; set; } = "M-INV";

    /// <summary>Fecha de creación del documento (se omite si es null: así el PDF no cambia entre reimpresiones).</summary>
    public DateTime? CreatedAt { get; set; }

    public IReadOnlyList<PdfPage> Pages => _pages;

    public PdfPage AddPage()
    {
        var page = new PdfPage(PageWidth, PageHeight);
        _pages.Add(page);
        return page;
    }

    public byte[] ToArray()
    {
        if (_pages.Count == 0)
        {
            AddPage();
        }
        // Objetos: 1 catálogo, 2 páginas, 3–5 fuentes, luego (página, contenido) por página y al final la información.
        const int fontStart = 3;
        var fonts = Enum.GetValues<PdfFont>();
        var firstPage = fontStart + fonts.Length;
        var infoId = firstPage + (_pages.Count * 2);
        var objects = new List<byte[]>(infoId);
        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        var kids = string.Join(' ', Enumerable.Range(0, _pages.Count).Select(i => $"{firstPage + (i * 2)} 0 R"));
        objects.Add(Ascii($"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>"));
        foreach (var font in fonts)
        {
            objects.Add(Ascii($"<< /Type /Font /Subtype /Type1 /BaseFont /{PdfFontMetrics.BaseFont(font)} /Encoding /WinAnsiEncoding >>"));
        }
        var fontResources = string.Join(' ', fonts.Select((f, i) => $"/F{i + 1} {fontStart + i} 0 R"));
        for (var i = 0; i < _pages.Count; i++)
        {
            var contentId = firstPage + (i * 2) + 1;
            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(PageWidth)} {Num(PageHeight)}] " +
                              $"/Resources << /Font << {fontResources} >> >> /Contents {contentId} 0 R >>"));
            objects.Add(Stream(Ascii(_pages[i].Content)));
        }
        objects.Add(Ascii(Info()));

        using var output = new MemoryStream();
        Write(output, "%PDF-1.4\n");
        output.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);   // marca binaria (recomendación del estándar)
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = output.Position;
            Write(output, $"{i + 1} 0 obj\n");
            output.Write(objects[i]);
            Write(output, "\nendobj\n");
        }
        var xref = output.Position;
        var table = new StringBuilder().Append("xref\n0 ").Append(objects.Count + 1).Append('\n').Append("0000000000 65535 f\r\n");
        foreach (var offset in offsets)
        {
            table.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n\r\n");
        }
        table.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R /Info ").Append(infoId).Append(" 0 R >>\n")
            .Append("startxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        Write(output, table.ToString());
        return output.ToArray();
    }

    private string Info()
    {
        var sb = new StringBuilder("<< /Producer ").Append(PdfPage.Literal("M-INV · PdfDocumentWriter"));
        Add("Title", Title);
        Add("Author", Author);
        Add("Subject", Subject);
        Add("Keywords", Keywords);
        Add("Creator", Creator);
        if (CreatedAt is { } created)
        {
            sb.Append(" /CreationDate (D:").Append(created.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)).Append(')');
        }
        return sb.Append(" >>").ToString();

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                sb.Append(" /").Append(key).Append(' ').Append(PdfPage.Literal(value));
            }
        }
    }

    private byte[] Stream(byte[] data)
    {
        var body = data;
        var filter = string.Empty;
        if (Compress)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(data);
            }
            body = ms.ToArray();
            filter = " /Filter /FlateDecode";
        }
        var head = Ascii($"<< /Length {body.Length}{filter} >>\nstream\n");
        var tail = Ascii("\nendstream");
        return [.. head, .. body, .. tail];
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static void Write(Stream stream, string text) => stream.Write(Ascii(text));

    internal static string Num(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// Página de un <see cref="PdfDocumentWriter"/>. Las coordenadas se dan desde la esquina SUPERIOR izquierda (como se
/// mide un diseño en papel): <c>x</c> hacia la derecha y <c>y</c> hacia abajo; en el texto, <c>y</c> es la línea base.
/// </summary>
public sealed class PdfPage
{
    private readonly StringBuilder _content = new(4096);

    internal PdfPage(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public double Width { get; }

    public double Height { get; }

    /// <summary>Operadores del flujo de contenido (ASCII).</summary>
    public string Content => _content.ToString();

    /// <summary>Texto con la línea base en (<paramref name="x"/>, <paramref name="y"/>). <paramref name="gray"/>: 0 negro … 1 blanco.</summary>
    public PdfPage Text(double x, double y, string text, PdfFont font, double size, double gray = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return this;
        }
        _content.Append(Gray(gray)).Append(" g BT /").Append(FontName(font)).Append(' ').Append(PdfDocumentWriter.Num(size)).Append(" Tf ")
            .Append(PdfDocumentWriter.Num(x)).Append(' ').Append(PdfDocumentWriter.Num(Height - y)).Append(" Td ")
            .Append(Literal(text)).Append(" Tj ET\n");
        return this;
    }

    /// <summary>Texto alineado a la derecha: termina en <paramref name="xRight"/>.</summary>
    public PdfPage TextRight(double xRight, double y, string text, PdfFont font, double size, double gray = 0) =>
        Text(xRight - PdfFontMetrics.Width(text, font, size), y, text, font, size, gray);

    /// <summary>Texto centrado en <paramref name="xCenter"/>.</summary>
    public PdfPage TextCenter(double xCenter, double y, string text, PdfFont font, double size, double gray = 0) =>
        Text(xCenter - (PdfFontMetrics.Width(text, font, size) / 2), y, text, font, size, gray);

    /// <summary>Texto girado <paramref name="degrees"/> (sentido antihorario) y centrado en (<paramref name="xCenter"/>,
    /// <paramref name="yCenter"/>): marcas de agua.</summary>
    public PdfPage RotatedText(double xCenter, double yCenter, double degrees, string text, PdfFont font, double size, double gray)
    {
        var radians = degrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var halfWidth = PdfFontMetrics.Width(text, font, size) / 2;
        var halfHeight = size * 0.35;
        var cx = xCenter;
        var cy = Height - yCenter;
        var tx = cx - ((cos * halfWidth) - (sin * halfHeight));
        var ty = cy - ((sin * halfWidth) + (cos * halfHeight));
        _content.Append("q ").Append(Gray(gray)).Append(" g BT /").Append(FontName(font)).Append(' ').Append(PdfDocumentWriter.Num(size))
            .Append(" Tf ").Append(Coef(cos)).Append(' ').Append(Coef(sin)).Append(' ').Append(Coef(-sin)).Append(' ').Append(Coef(cos)).Append(' ')
            .Append(PdfDocumentWriter.Num(tx)).Append(' ').Append(PdfDocumentWriter.Num(ty)).Append(" Tm ").Append(Literal(text)).Append(" Tj ET Q\n");
        return this;
    }

    public PdfPage Line(double x1, double y1, double x2, double y2, double width = 0.5, double gray = 0)
    {
        _content.Append(Gray(gray)).Append(" G ").Append(PdfDocumentWriter.Num(width)).Append(" w ")
            .Append(PdfDocumentWriter.Num(x1)).Append(' ').Append(PdfDocumentWriter.Num(Height - y1)).Append(" m ")
            .Append(PdfDocumentWriter.Num(x2)).Append(' ').Append(PdfDocumentWriter.Num(Height - y2)).Append(" l S\n");
        return this;
    }

    /// <summary>Borde de un rectángulo cuya esquina superior izquierda es (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public PdfPage Rectangle(double x, double y, double width, double height, double lineWidth = 0.5, double gray = 0)
    {
        _content.Append(Gray(gray)).Append(" G ").Append(PdfDocumentWriter.Num(lineWidth)).Append(" w ")
            .Append(Rect(x, y, width, height)).Append(" S\n");
        return this;
    }

    public PdfPage FillRectangle(double x, double y, double width, double height, double gray = 0)
    {
        _content.Append(Gray(gray)).Append(" g ").Append(Rect(x, y, width, height)).Append(" f\n");
        return this;
    }

    /// <summary>Varios rectángulos rellenos con UNA sola operación de relleno (p. ej. los módulos de un QR).</summary>
    public PdfPage FillRectangles(IEnumerable<(double X, double Y, double Width, double Height)> rectangles, double gray = 0)
    {
        ArgumentNullException.ThrowIfNull(rectangles);
        _content.Append(Gray(gray)).Append(" g\n");
        var any = false;
        foreach (var (x, y, w, h) in rectangles)
        {
            _content.Append(Rect(x, y, w, h)).Append('\n');
            any = true;
        }
        _content.Append(any ? "f\n" : "n\n");
        return this;
    }

    /// <summary>Cadena literal de PDF con los bytes WinAnsi: escapa «\», «(» y «)», y los bytes no imprimibles en octal.</summary>
    internal static string Literal(string text)
    {
        var sb = new StringBuilder(text.Length + 2).Append('(');
        foreach (var b in PdfFontMetrics.Encode(text))
        {
            switch (b)
            {
                case (byte)'\\':
                case (byte)'(':
                case (byte)')':
                    sb.Append('\\').Append((char)b);
                    break;
                case < 32 or > 126:
                    sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    break;
                default:
                    sb.Append((char)b);
                    break;
            }
        }
        return sb.Append(')').ToString();
    }

    private string Rect(double x, double y, double width, double height) =>
        $"{PdfDocumentWriter.Num(x)} {PdfDocumentWriter.Num(Height - y - height)} {PdfDocumentWriter.Num(width)} {PdfDocumentWriter.Num(height)} re";

    private static string FontName(PdfFont font) => "F" + ((int)font + 1).ToString(CultureInfo.InvariantCulture);

    private static string Gray(double gray) => PdfDocumentWriter.Num(Math.Clamp(gray, 0, 1));

    private static string Coef(double value) => Math.Round(value, 5).ToString("0.#####", CultureInfo.InvariantCulture);
}
