using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Infrastructure.Billing.Rendering;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>
/// Representación gráfica en PDF (hoja carta, sin dependencias): estructura válida (objetos, xref, trailer), textos con
/// acentos en WinAnsi, QR vectorial ≥ 3 × 3 cm, salto de página con muchas líneas, notas y marcas de agua. Con la variable
/// <c>MINV_BILLING_SAMPLES_DIR</c> las pruebas dejan los PDF en esa carpeta para revisarlos a ojo.
/// </summary>
public sealed partial class PdfRendererTests
{
    private const string Cuf = "9C1A83F996B702B8497F0555BFF4C27CB7E1783A67A84B0CA3176D74";
    private const string NoteCuf = "44AAEC00DBD34C53C3E5B135433591A5FA086F86A867A75AC82F24C74";

    private static readonly string[] Legends =
    [
        "ESTA FACTURA CONTRIBUYE AL DESARROLLO DEL PAÍS, EL USO ILÍCITO SERÁ SANCIONADO PENALMENTE DE ACUERDO A LEY",
        "Ley N° 453: El proveedor deberá entregar el producto en las modalidades y términos ofertados o convenidos.",
        "“Este documento es la Representación Gráfica de un Documento Fiscal Digital emitido en una modalidad de facturación en línea”",
    ];

    private readonly FiscalPdfRenderer _renderer = new();

    /// <summary>La factura del PDF oficial «factura COMPRA VENTA.pdf» (309.51 Bs).</summary>
    internal static FiscalPrintModel Invoice(IReadOnlyList<FiscalPrintLine>? lines = null, bool isTest = true, bool isVoided = false,
        bool isOffline = false)
    {
        lines ??=
        [
            new("GA-CL-013", "GANCHO P/ CALAMINA M6*J 60", "PIEZAS", 200m, 0.63m, 7.52m, 118.48m),
            new("GA-CL-015", "GANCHO P/ CALAMINA M6*J 70", "PIEZAS", 100m, 0.72m, 4.11m, 67.89m),
            new("RP-FR-01", "REPOSICION DE FORMULARIO", "UNIDAD (BIENES)", 1m, 0.98m, 0m, 0.98m),
            new("HI-CO-004", "HI. CORRUGADO 5/16\" (8,00MM) CA-50", "TUBOS", 3m, 40.72m, 0m, 122.16m),
        ];
        var total = lines.Sum(l => l.Subtotal);
        return new FiscalPrintModel("FACTURA", "(Con Derecho a Crédito Fiscal)", "Metales Totai S.R.L.", 123451020, "SUCURSAL N. 5", 0,
            "Av. Juan XXIII", "2824512", "Yacuiba", 2377, Cuf, new DateTime(2022, 5, 6, 9, 19, 42, 957), "DAVID ZELADA", "987654", "1864",
            lines, total, 0m, total, 0m, total, total, AmountInWords.Bolivianos(total), "EFECTIVO", "JPEREZ", Legends,
            $"https://pilotosiat.impuestos.gob.bo/consulta/QR?nit=123451020&cuf={Cuf}&numero=2377", isTest, isVoided, isOffline);
    }

    /// <summary>La nota del PDF oficial «Nota CreditoDebito.pdf» con la devolución del XML oficial (775 / 75 → 9.75).</summary>
    internal static FiscalPrintModel CreditNote(decimal? discountShare = null)
    {
        var returnedTotal = 75m - (discountShare ?? 0m);
        return new FiscalPrintModel("NOTA CRÉDITO - DÉBITO", string.Empty, "PRUEBA", 123456022, "CASA MATRIZ", 0, "AV. JORGE LOPEZ #123",
            "2846005", "La Paz", 1, NoteCuf, new DateTime(2021, 10, 6, 16, 3, 49, 570), "Juan Perez", "5115889", "51158891",
            [
                new("123456", "Amortiguadores", "OTRO", 1m, 775m, 0m, 775m, 1),
                new("123457", "Tornillos", "OTRO", 1m, 75m, 0m, 75m, 2),
            ],
            75m, discountShare ?? 0m, returnedTotal, 0m, returnedTotal, returnedTotal, AmountInWords.Bolivianos(returnedTotal), null, null,
            Legends, $"https://pilotosiat.impuestos.gob.bo/consulta/QR?nit=123456022&cuf={NoteCuf}&numero=1", false, false, false,
            new FiscalPrintOriginal(1, "44AAEC00DBD34C53C3E2CCE1A3FA7AF1E2A08606A667A75AC82F24C74", new DateTime(2021, 10, 6, 16, 3, 48, 675)),
            ReturnedTotal: returnedTotal, CreditDebitAmount: Math.Round(returnedTotal * 0.13m, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void El_PDF_es_valido_y_lleva_FACTURA_y_el_CUF()
    {
        var pdf = _renderer.RenderPdf(Invoice());
        Save("factura-compra-venta.pdf", pdf);
        var text = Latin1(pdf);
        Assert.StartsWith("%PDF-1.4\n", text);
        Assert.EndsWith("%%EOF\n", text);
        AssertValidStructure(pdf);
        Assert.Contains("(FACTURA)", text);
        Assert.Contains("CUF " + Cuf, text);                       // en la información del documento (título, asunto)
        var pieces = PdfFontMetrics.BreakCharacters(Cuf, PdfFont.Helvetica, 8, 584 - 470);
        Assert.True(pieces.Count >= 2);
        Assert.All(pieces, p => Assert.Contains($"({p}) Tj", text));   // y partido en líneas bajo CÓD. AUTORIZACIÓN
        Assert.Contains("(Metales Totai S.R.L.)", text);
        Assert.Contains("(SUCURSAL N. 5)", text);
        Assert.Contains("(No. Punto de Venta 0)", text);
        Assert.Contains("(FACTURA N\\260)", text);                 // «°» en WinAnsi (octal 260)
        Assert.Contains("(C\\323D. AUTORIZACI\\323N)", text);     // «Ó» en WinAnsi (octal 323)
        Assert.Contains("(\\(Con Derecho a Cr\\351dito Fiscal\\))", text);
        Assert.Contains("(06/05/2022 09:19 AM)", text);
        Assert.Contains("(NIT/CI/CEX:)", text);
        Assert.Contains("(Cod. Cliente:)", text);
        Assert.Contains("(C\\323DIGO)", text);
        Assert.Contains("(118.48)", text);
        Assert.Contains("(IMPORTE BASE CR\\311DITO FISCAL)", text);
        Assert.Contains("(MONTO A PAGAR Bs)", text);
        Assert.Contains("(309.51)", text);
        Assert.Contains("(Son: Trescientos nueve 51/100 Bolivianos)", text);
        Assert.Contains("(\\223Este documento es la Representaci\\363n Gr\\341fica", text);   // comillas tipográficas en WinAnsi
        Assert.Contains("(1/1)", text);
        Assert.Contains("/BaseFont /Helvetica /Encoding /WinAnsiEncoding", text);
        Assert.Contains("/BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding", text);
        Assert.Contains("(SIN VALOR LEGAL)", text);                // ambiente de pruebas
        Assert.DoesNotContain("(ANULADO)", text);
        Assert.DoesNotContain("FUERA DE L", text);
    }

    [Fact]
    public void El_QR_es_vectorial_de_al_menos_3_por_3_cm_con_la_URL_para_hoja()
    {
        Assert.True(FiscalPdfRenderer.QrSize >= 85);   // 3 cm = 85.04 pt
        var model = Invoice();
        var url = FiscalPdfRenderer.WithQrSize(model.QrUrl, 2);
        Assert.EndsWith("&numero=2377&t=2", url);
        var matrix = QrMatrix.Create(url);
        var n = matrix.GetLength(0);
        Assert.Equal(n, matrix.GetLength(1));
        Assert.Equal(0, (n - 21) % 4);   // 21 + 4·(versión − 1): sin la zona blanca de QRCoder
        foreach (var (row, col) in new[] { (0, 0), (0, n - 7), (n - 7, 0) })   // patrones de posición en tres esquinas
        {
            Assert.True(matrix[row, col] && matrix[row + 6, col + 6] && matrix[row + 3, col + 3]);
            Assert.False(matrix[row + 1, col + 1] || matrix[row + 5, col + 5]);
        }

        var rectangles = QrMatrix.Rectangles(matrix, 494, 500, FiscalPdfRenderer.QrSize).ToList();
        Assert.Equal(494, rectangles.Min(r => r.X), 3);
        Assert.Equal(494 + FiscalPdfRenderer.QrSize, rectangles.Max(r => r.X + r.Width), 3);
        Assert.Equal(500, rectangles.Min(r => r.Y), 3);
        Assert.Equal(500 + FiscalPdfRenderer.QrSize, rectangles.Max(r => r.Y + r.Height), 3);
        var dark = 0;
        foreach (var cell in matrix)
        {
            dark += cell ? 1 : 0;
        }
        Assert.Equal(dark * Math.Pow(FiscalPdfRenderer.QrSize / n, 2), rectangles.Sum(r => r.Width * r.Height), 3);

        var content = Latin1(_renderer.RenderPdf(model));
        Assert.True(RectangleOps().Count(content) >= rectangles.Count, "el QR se dibuja con rectángulos negros");
    }

    [Fact]
    public void Muchas_lineas_saltan_de_pagina_y_numeran_n_de_N()
    {
        var lines = Enumerable.Range(1, 130).Select(i => new FiscalPrintLine($"COD-{i:0000}",
            i % 7 == 0 ? $"Producto {i} con una descripción larga que ocupa varias líneas en la columna de descripción de la tabla" : $"Producto {i}",
            "UNIDAD (BIENES)", i, 1.5m, 0m, i * 1.5m)).ToList();
        var pdf = _renderer.RenderPdf(Invoice(lines));
        Save("factura-varias-paginas.pdf", pdf);
        AssertValidStructure(pdf);
        var text = Latin1(pdf);
        var pages = PageObjects().Count(text);
        Assert.True(pages >= 3, $"{pages} páginas");
        Assert.Contains($"/Count {pages}", text);
        for (var i = 1; i <= pages; i++)
        {
            Assert.Contains($"({i}/{pages})", text);
        }
        Assert.Contains("(COD-0001)", text);
        Assert.Contains("(COD-0130)", text);
        Assert.Contains("(continuaci\\363n\\))", text);                               // encabezado de las páginas siguientes
        Assert.True(Occurrences(text, "(DESCRIPCI\\323N)") >= pages - 1);               // la cabecera de la tabla se repite
        Assert.Equal(pages, Occurrences(text, "(SIN VALOR LEGAL)"));                   // la marca de agua en cada página
        Assert.Equal(1, Occurrences(text, "(IMPORTE BASE CR\\311DITO FISCAL)"));
    }

    [Fact]
    public void La_nota_lleva_la_factura_original_y_las_dos_secciones_de_detalle()
    {
        var pdf = _renderer.RenderPdf(CreditNote());
        Save("nota-credito-debito.pdf", pdf);
        AssertValidStructure(pdf);
        var text = Latin1(pdf);
        Assert.Contains("(NOTA CR\\311DITO - D\\311BITO)", text);
        Assert.Contains("(Nota N\\260)", text);
        Assert.Contains("(N\\260 Factura:)", text);
        Assert.Contains("(Fecha Factura:)", text);
        Assert.Contains("(N\\260 Autorizaci\\363n/CUF:)", text);
        Assert.Contains("(DETALLE DOCUMENTO ORIGEN)", text);
        Assert.Contains("(DETALLE DE LA DEVOLUCI\\323N O RESCISI\\323N DE SERVICIO)", text);
        Assert.Contains("(MONTO TOTAL ORIGINAL Bs)", text);
        Assert.Contains("(775.00)", text);
        Assert.Contains("(MONTO TOTAL DEVUELTO Bs)", text);
        Assert.Contains("(75.00)", text);
        Assert.Contains("(MONTO EFECTIVO DEL CR\\311DITO O D\\311BITO \\(13%\\) Bs)", text);
        Assert.Contains("(9.75)", text);
        Assert.Contains("(Son: Setenta y cinco 00/100 Bolivianos)", text);
        Assert.DoesNotContain("(IMPORTE BASE CR", text);
        Assert.DoesNotContain("(MONTO A PAGAR Bs)", text);
        Assert.DoesNotContain("(SIN VALOR LEGAL)", text);
    }

    [Fact]
    public void La_nota_con_descuento_prorrateado_muestra_el_subtotal_y_el_descuento()
    {
        var text = Latin1(_renderer.RenderPdf(CreditNote(discountShare: 4.41m)));
        Assert.Contains("(MONTO DESCUENTO CR\\311DITO D\\311BITO Bs)", text);
        Assert.Contains("(4.41)", text);
        Assert.Contains("(70.59)", text);
        Assert.Contains("(9.18)", text);
        Assert.Contains("(Son: Setenta 59/100 Bolivianos)", text);
    }

    [Fact]
    public void Marcas_de_agua_anulado_y_fuera_de_linea()
    {
        var pdf = _renderer.RenderPdf(Invoice(isTest: true, isVoided: true, isOffline: true));
        Save("factura-anulada-fuera-de-linea.pdf", pdf);
        var text = Latin1(pdf);
        Assert.Contains("(SIN VALOR LEGAL)", text);
        Assert.Contains("(ANULADO)", text);
        Assert.Contains("(FUERA DE L\\315NEA)", text);
        Assert.Contains(" Tm ", text);   // texto girado (diagonal)
        var clean = Latin1(_renderer.RenderPdf(Invoice(isTest: false)));
        Assert.DoesNotContain("(SIN VALOR LEGAL)", clean);
        Assert.DoesNotContain(" Tm ", clean);
    }

    [Fact]
    public void Es_determinista_y_puede_comprimirse()
    {
        var model = Invoice();
        Assert.Equal(_renderer.RenderPdf(model), _renderer.RenderPdf(model));

        var writer = new PdfDocumentWriter { Compress = true, Title = "Prueba" };
        writer.AddPage().Text(30, 40, "Ñandú · FACTURA N° 1", PdfFont.HelveticaBold, 12);
        var compressed = writer.ToArray();
        AssertValidStructure(compressed);
        var text = Latin1(compressed);
        Assert.Contains("/Filter /FlateDecode", text);
        Assert.DoesNotContain("FACTURA", text);
        var start = text.IndexOf("stream\n", text.IndexOf("/FlateDecode", StringComparison.Ordinal), StringComparison.Ordinal) + 7;
        var length = int.Parse(Regex.Match(text[..start], @"/Length (\d+) /Filter /FlateDecode >>\nstream\n$").Groups[1].Value,
            CultureInfo.InvariantCulture);
        using var z = new ZLibStream(new MemoryStream(compressed, start, length), CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        Assert.Contains("(\\321and\\372 \\267 FACTURA N\\260 1) Tj", Latin1(output.ToArray()));
    }

    [Fact]
    public void Metricas_de_Helvetica_para_alinear_y_cortar()
    {
        Assert.Equal(0.278 * 10, PdfFontMetrics.Width(" ", PdfFont.Helvetica, 10), 6);
        Assert.Equal((611 + 722 + 722 + 611 + 722 + 722 + 722) / 1000d * 14, PdfFontMetrics.Width("FACTURA", PdfFont.HelveticaBold, 14), 6);
        Assert.Equal(PdfFontMetrics.Width("ANULADO", PdfFont.HelveticaBold, 20), PdfFontMetrics.Width("ANULADO", PdfFont.HelveticaBoldOblique, 20));
        Assert.Equal(PdfFontMetrics.Width("n", PdfFont.Helvetica, 10), PdfFontMetrics.Width("ñ", PdfFont.Helvetica, 10));
        Assert.Equal(new byte[] { 0xD1, 0xF1, 0xE1, 0xC9, 0xB0, 0x80, 0x93, 0x94, (byte)'?' }, PdfFontMetrics.Encode("ÑñáÉ°€“”✔"));

        var pieces = PdfFontMetrics.BreakCharacters(Cuf, PdfFont.Helvetica, 8, 75);
        Assert.Equal(Cuf, string.Concat(pieces));
        Assert.True(pieces.Count >= 3);
        Assert.All(pieces, p => Assert.True(PdfFontMetrics.Width(p, PdfFont.Helvetica, 8) <= 75));

        var text = "Ley N° 453: El proveedor deberá entregar el producto en las modalidades y términos ofertados o convenidos.";
        var lines = PdfFontMetrics.Wrap(text, PdfFont.Helvetica, 7.5, 120);
        Assert.True(lines.Count > 1);
        Assert.Equal(text, string.Join(' ', lines));
        Assert.All(lines, l => Assert.True(PdfFontMetrics.Width(l, PdfFont.Helvetica, 7.5) <= 120));
        Assert.Equal(["uno", "dos"], PdfFontMetrics.Wrap("uno\ndos", PdfFont.Helvetica, 8, 500));
        Assert.Equal([string.Empty], PdfFontMetrics.Wrap(null, PdfFont.Helvetica, 8, 500));
    }

    [Fact]
    public void La_URL_del_QR_fija_el_tamano_una_sola_vez()
    {
        Assert.Equal("https://x/QR?nit=1&cuf=A&numero=2&t=2", FiscalPdfRenderer.WithQrSize("https://x/QR?nit=1&cuf=A&numero=2&t=1", 2));
        Assert.Equal("https://x/QR?nit=1&t=2", FiscalPdfRenderer.WithQrSize("https://x/QR?nit=1&T=1", 2));
        Assert.Equal("https://x/QR?t=2", FiscalPdfRenderer.WithQrSize("https://x/QR", 2));
        Assert.Equal(string.Empty, FiscalPdfRenderer.WithQrSize(string.Empty, 2));
    }

    [Fact]
    public void Sin_URL_de_QR_no_dibuja_el_codigo()
    {
        var model = Invoice(isTest: false) with { QrUrl = string.Empty };
        var pdf = _renderer.RenderPdf(model);
        AssertValidStructure(pdf);
        Assert.Equal(0, RectangleOps().Count(Latin1(pdf)));
    }

    // ------------------------------------------------------------------------------------------------ utilidades
    /// <summary>Cada entrada de la tabla xref apunta a «n 0 obj», startxref apunta a «xref» y el trailer declara el tamaño.</summary>
    private static void AssertValidStructure(byte[] pdf)
    {
        var text = Latin1(pdf);
        var startXref = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n%%EOF\n$").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.StartsWith("xref\n0 ", text[startXref..]);
        var header = Regex.Match(text[startXref..], @"^xref\n0 (\d+)\n");
        var size = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
        var entries = Regex.Matches(text[startXref..], @"(\d{10}) (\d{5}) ([nf])\r\n");
        Assert.Equal(size, entries.Count);
        for (var i = 1; i < size; i++)
        {
            var offset = int.Parse(entries[i].Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i} 0 obj\n", text[offset..]);
        }
        Assert.Contains($"trailer\n<< /Size {size} /Root 1 0 R /Info {size - 1} 0 R >>", text);
        foreach (Match stream in Regex.Matches(text, @"/Length (\d+)[^>]*>>\nstream\n"))
        {
            var length = int.Parse(stream.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.StartsWith("\nendstream", text[(stream.Index + stream.Length + length)..]);
        }
    }

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static void Save(string name, byte[] pdf)
    {
        if (Environment.GetEnvironmentVariable("MINV_BILLING_SAMPLES_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), pdf);
        }
    }

    [GeneratedRegex(@"/Type /Page ")]
    private static partial Regex PageObjects();

    [GeneratedRegex(@" re\n")]
    private static partial Regex RectangleOps();
}
