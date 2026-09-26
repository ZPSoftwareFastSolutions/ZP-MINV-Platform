using System.Globalization;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;

namespace MINV.Infrastructure.Billing.Rendering;

/// <summary>
/// V4.1 · Representación gráfica en PDF, hoja CARTA (612 × 792 pt), sin dependencias de PDF: sigue la disposición de los
/// ejemplos oficiales del SIN (investigación 06 §10–13): emisor a la izquierda (razón social, CASA MATRIZ / SUCURSAL,
/// punto de venta, dirección, teléfono, municipio), NIT, número y CÓD. AUTORIZACIÓN (el CUF) a la derecha, título y
/// subtítulo, comprador, detalle con salto de página, totales, «Son: …», las tres leyendas centradas y el QR vectorial
/// (≥ 3 × 3 cm) a su derecha. Las notas crédito-débito llevan la factura original y las dos secciones de detalle.
/// Marcas de agua: «SIN VALOR LEGAL» (ambiente de pruebas) y «ANULADO»; «FUERA DE LÍNEA» discreto si corresponde.
/// </summary>
public sealed class FiscalPdfRenderer : IFiscalDocumentRenderer
{
    /// <summary>Lado del QR en puntos: 90 pt = 3,18 cm (el SIN recomienda no bajar de 3 × 3 cm).</summary>
    public const double QrSize = 90;

    public byte[] RenderPdf(FiscalPrintModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new Layout(model).Render();
    }

    /// <summary>Fija el parámetro <c>t</c> de la URL del QR del SIN (1 = rollo, 2 = media hoja/hoja).</summary>
    public static string WithQrSize(string url, int size)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }
        var parts = url.Split('?', 2);
        var query = parts.Length > 1 ? parts[1].Split('&', StringSplitOptions.RemoveEmptyEntries).ToList() : new List<string>();
        query.RemoveAll(p => p.Equals("t", StringComparison.OrdinalIgnoreCase) || p.StartsWith("t=", StringComparison.OrdinalIgnoreCase));
        query.Add("t=" + size.ToString(CultureInfo.InvariantCulture));
        return parts[0] + "?" + string.Join('&', query);
    }

    private enum Align
    {
        Left,
        Center,
        Right,
    }

    private sealed record Column(string Title, double Width, Align Align);

    private sealed record Total(string Label, string Value, bool Bold = false);

    private sealed class Layout
    {
        private const double Left = 30;
        private const double Right = 584;
        private const double Center = (Left + Right) / 2;
        private const double Top = 40;
        private const double Bottom = 742;
        private const double LineStep = 9.3;

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private readonly FiscalPrintModel _m;
        private readonly PdfDocumentWriter _pdf;
        private PdfPage _page = null!;
        private double _y;

        public Layout(FiscalPrintModel model)
        {
            _m = model;
            _pdf = new PdfDocumentWriter
            {
                Title = $"{model.Title} N° {model.Number}",
                Author = model.IssuerName,
                Subject = $"CUF {model.Cuf}",
                Keywords = $"NIT {model.IssuerNit}; N° {model.Number}; CUF {model.Cuf}",
                CreatedAt = model.IssuedAt,
            };
        }

        private bool IsNote => _m.Original is not null;

        public byte[] Render()
        {
            NewPage(first: true);
            Header();
            TitleBlock();
            Buyer();
            if (IsNote)
            {
                NoteBody();
            }
            else
            {
                InvoiceBody();
            }
            Legends();
            var pages = _pdf.Pages;
            for (var i = 0; i < pages.Count; i++)
            {
                pages[i].TextRight(Right, 765, $"{i + 1}/{pages.Count}", PdfFont.Helvetica, 8);
            }
            return _pdf.ToArray();
        }

        // -------------------------------------------------------------------------------------------- páginas
        private void NewPage(bool first)
        {
            _page = _pdf.AddPage();
            Watermarks();
            if (first)
            {
                _y = Top;
                return;
            }
            var continuation = $"{_m.Title} N° {_m.Number} · CÓD. AUTORIZACIÓN {_m.Cuf} (continuación)";
            _y = Top;
            foreach (var line in PdfFontMetrics.Wrap(continuation, PdfFont.HelveticaBold, 8, Right - Left))
            {
                _page.Text(Left, _y, line, PdfFont.HelveticaBold, 8);
                _y += 10;
            }
            _y += 8;
        }

        private void EnsureSpace(double height)
        {
            if (_y + height > Bottom)
            {
                NewPage(first: false);
            }
        }

        private void Watermarks()
        {
            var marks = new List<(string Text, double Gray)>();
            if (_m.IsTest)
            {
                marks.Add(("SIN VALOR LEGAL", 0.86));
            }
            if (_m.IsVoided)
            {
                marks.Add(("ANULADO", 0.8));
            }
            for (var i = 0; i < marks.Count; i++)
            {
                var (text, gray) = marks[i];
                var size = Math.Min(64, 540 / PdfFontMetrics.Width(text, PdfFont.HelveticaBoldOblique, 1));
                var y = marks.Count == 1 ? 330 : 250 + (i * 230);
                _page.RotatedText(Center, y, 35, text, PdfFont.HelveticaBoldOblique, size, gray);
            }
        }

        // -------------------------------------------------------------------------------------------- encabezado
        private void Header()
        {
            const double columnWidth = 220;
            const double columnCenter = Left + (columnWidth / 2);
            var y = Top;
            foreach (var line in PdfFontMetrics.Wrap(_m.IssuerName, PdfFont.HelveticaBold, 9, columnWidth))
            {
                _page.TextCenter(columnCenter, y, line, PdfFont.HelveticaBold, 9);
                y += 11;
            }
            _page.TextCenter(columnCenter, y, _m.BranchLabel, PdfFont.HelveticaBold, 8);
            y += 10;
            _page.TextCenter(columnCenter, y, $"No. Punto de Venta {_m.PointOfSaleCode.ToString(Invariant)}", PdfFont.Helvetica, 8);
            y += 10;
            foreach (var line in PdfFontMetrics.Wrap(_m.Address, PdfFont.Helvetica, 8, columnWidth).Take(3))
            {
                _page.TextCenter(columnCenter, y, line, PdfFont.Helvetica, 8);
                y += 10;
            }
            if (!string.IsNullOrWhiteSpace(_m.Phone))
            {
                _page.TextCenter(columnCenter, y, "Teléfono: " + _m.Phone.Trim(), PdfFont.Helvetica, 8);
                y += 10;
            }
            _page.TextCenter(columnCenter, y, _m.Municipality, PdfFont.Helvetica, 8);
            y += 10;

            const double labelX = 370;
            const double valueX = 470;
            var ry = Top;
            _page.Text(labelX, ry, "NIT", PdfFont.HelveticaBold, 8).Text(valueX, ry, _m.IssuerNit.ToString(Invariant), PdfFont.Helvetica, 8);
            ry += 11;
            _page.Text(labelX, ry, IsNote ? "Nota N°" : "FACTURA N°", PdfFont.HelveticaBold, 8)
                .Text(valueX, ry, _m.Number.ToString(Invariant), PdfFont.Helvetica, 8);
            ry += 11;
            _page.Text(labelX, ry, "CÓD. AUTORIZACIÓN", PdfFont.HelveticaBold, 8);
            foreach (var piece in PdfFontMetrics.BreakCharacters(_m.Cuf, PdfFont.Helvetica, 8, Right - valueX))
            {
                _page.Text(valueX, ry, piece, PdfFont.Helvetica, 8);
                ry += LineStep;
            }
            if (_m.IsOffline)
            {
                ry += 3;
                _page.Text(labelX, ry, "FUERA DE LÍNEA", PdfFont.Helvetica, 7, gray: 0.45);
                ry += 10;
            }
            _y = Math.Max(y, ry);
        }

        private void TitleBlock()
        {
            var y = Math.Max(_y + 20, 128);
            _page.TextCenter(Center, y, _m.Title, PdfFont.HelveticaBold, 14);
            y += 15;
            var subtitle = _m.Subtitle?.Trim();
            if (!string.IsNullOrEmpty(subtitle))
            {
                _page.TextCenter(Center, y, subtitle.StartsWith('(') ? subtitle : $"({subtitle})", PdfFont.Helvetica, 9);
                y += 11;
            }
            _y = y + 18;
        }

        private void Buyer()
        {
            var rows = new List<(string LeftLabel, string LeftValue, string RightLabel, string RightValue, bool BreakRight)>
            {
                ("Fecha:", Date(_m.IssuedAt), "NIT/CI/CEX:", _m.BuyerDocument, false),
                ("Nombre/Razón Social:", _m.BuyerName, "Cod. Cliente:", _m.CustomerCode, false),
            };
            if (_m.Original is { } original)
            {
                rows.Add(("N° Factura:", original.Number.ToString(Invariant), "Fecha Factura:", Date(original.IssuedAt), false));
                rows.Add((string.Empty, string.Empty, "N° Autorización/CUF:", original.Cuf, true));
            }
            const double leftValueX = 140;
            const double leftValueWidth = 212;
            const double rightLabelEnd = 455;
            const double rightValueX = 461;
            const double step = 11;
            foreach (var row in rows)
            {
                var leftLines = PdfFontMetrics.Wrap(row.LeftValue, PdfFont.Helvetica, 9, leftValueWidth);
                var rightLines = row.BreakRight
                    ? PdfFontMetrics.BreakCharacters(row.RightValue, PdfFont.Helvetica, 9, Right - rightValueX)
                    : PdfFontMetrics.Wrap(row.RightValue, PdfFont.Helvetica, 9, Right - rightValueX);
                _page.Text(Left, _y, row.LeftLabel, PdfFont.HelveticaBold, 9);
                for (var i = 0; i < leftLines.Count; i++)
                {
                    _page.Text(leftValueX, _y + (i * step), leftLines[i], PdfFont.Helvetica, 9);
                }
                _page.TextRight(rightLabelEnd, _y, row.RightLabel, PdfFont.HelveticaBold, 9);
                for (var i = 0; i < rightLines.Count; i++)
                {
                    _page.Text(rightValueX, _y + (i * step), rightLines[i], PdfFont.Helvetica, 9);
                }
                _y += (Math.Max(leftLines.Count, rightLines.Count) * step) + 4;
            }
            _y += 10;
        }

        // -------------------------------------------------------------------------------------------- cuerpo
        private void InvoiceBody()
        {
            Table(Columns("CÓDIGO PRODUCTO / SERVICIO"), _m.Lines, quantityDecimals: 2);
            Totals(
            [
                new("SUBTOTAL Bs", Money(_m.Subtotal)),
                new("DESCUENTO Bs", Money(_m.Discount)),
                new("TOTAL Bs", Money(_m.Total)),
                new("MONTO GIFT CARD Bs", Money(_m.GiftCard)),
                new("MONTO A PAGAR Bs", Money(_m.AmountToPay), Bold: true),
                new("IMPORTE BASE CRÉDITO FISCAL", Money(_m.TaxBase), Bold: true),
            ], _m.AmountInWords);
        }

        private void NoteBody()
        {
            var original = _m.Lines.Where(l => l.TransactionCode is null or 1).ToList();
            var returned = _m.Lines.Where(l => l.TransactionCode == 2).ToList();
            var originalTotal = original.Sum(l => l.Subtotal);
            var returnedSubtotal = returned.Sum(l => l.Subtotal);
            var returnedTotal = _m.ReturnedTotal ?? (returnedSubtotal - _m.Discount);
            var discountShare = returnedSubtotal - returnedTotal;
            var effective = _m.CreditDebitAmount ?? FiscalRules.Vat(returnedTotal);

            Section("DETALLE DOCUMENTO ORIGEN");
            Table(Columns("CÓDIGO PRODUCTO"), original, quantityDecimals: 10);
            Totals([new("MONTO TOTAL ORIGINAL Bs", Money(originalTotal), Bold: true)], words: null);
            _y += 22;
            Section("DETALLE DE LA DEVOLUCIÓN O RESCISIÓN DE SERVICIO");
            Table(Columns("CÓDIGO PRODUCTO"), returned, quantityDecimals: 10);
            var totals = new List<Total>();
            if (discountShare != 0)
            {
                totals.Add(new("SUBTOTAL Bs", Money(returnedSubtotal)));
                totals.Add(new("MONTO DESCUENTO CRÉDITO DÉBITO Bs", Money(discountShare)));
            }
            totals.Add(new("MONTO TOTAL DEVUELTO Bs", Money(returnedTotal), Bold: true));
            totals.Add(new("MONTO EFECTIVO DEL CRÉDITO O DÉBITO (13%) Bs", Money(effective), Bold: true));
            Totals(totals, _m.AmountInWords);
        }

        private void Section(string title)
        {
            EnsureSpace(60);
            _page.Text(Left, _y + 8, title, PdfFont.HelveticaBold, 9);
            _y += 16;
        }

        private static Column[] Columns(string codeTitle) =>
        [
            new(codeTitle, 79, Align.Left),
            new("CANTIDAD", 59, Align.Right),
            new("UNIDAD DE MEDIDA", 56, Align.Left),
            new("DESCRIPCIÓN", 146, Align.Left),
            new("PRECIO UNITARIO", 70, Align.Right),
            new("DESCUENTO", 69, Align.Right),
            new("SUBTOTAL", 75, Align.Right),
        ];

        private void Table(Column[] columns, IReadOnlyList<FiscalPrintLine> lines, int quantityDecimals)
        {
            var quantityFormat = quantityDecimals <= 2 ? "#,##0.00" : "#,##0.00" + new string('#', quantityDecimals - 2);
            var headers = columns.Select(c => PdfFontMetrics.Wrap(c.Title, PdfFont.HelveticaBold, 8, c.Width - 6)).ToArray();
            var headerHeight = (headers.Max(h => h.Count) * 9) + 8;
            var rows = lines.Select(l => new[]
            {
                l.ProductCode, l.Quantity.ToString(quantityFormat, Invariant), l.Unit, l.Description, Money(l.UnitPrice), Money(l.Discount),
                Money(l.Subtotal),
            }).Select(cells => cells.Select((text, i) => columns[i].Align == Align.Right
                ? new[] { text }
                : PdfFontMetrics.Wrap(text, PdfFont.Helvetica, 8, columns[i].Width - 6)).ToArray()).ToList();

            EnsureSpace(headerHeight + (rows.Count > 0 ? RowHeight(rows[0]) : 0));
            DrawHeader();
            foreach (var cells in rows)
            {
                var height = RowHeight(cells);
                if (_y + height > Bottom)
                {
                    NewPage(first: false);
                    DrawHeader();
                }
                DrawRow(cells, height, PdfFont.Helvetica, headerRow: false);
            }
            return;

            void DrawHeader() => DrawRow(headers, headerHeight, PdfFont.HelveticaBold, headerRow: true);

            void DrawRow(IReadOnlyList<string>[] cells, double height, PdfFont font, bool headerRow)
            {
                var x = Left;
                for (var i = 0; i < columns.Length; i++)
                {
                    var column = columns[i];
                    _page.Rectangle(x, _y, column.Width, height);
                    var lineHeight = headerRow ? 9 : LineStep;
                    var blockTop = _y + ((height - (cells[i].Count * lineHeight)) / 2);
                    for (var j = 0; j < cells[i].Count; j++)
                    {
                        var baseline = blockTop + ((j + 1) * lineHeight) - 2.3;
                        var align = headerRow ? Align.Center : column.Align;
                        switch (align)
                        {
                            case Align.Right:
                                _page.TextRight(x + column.Width - 3, baseline, cells[i][j], font, 8);
                                break;
                            case Align.Center:
                                _page.TextCenter(x + (column.Width / 2), baseline, cells[i][j], font, 8);
                                break;
                            default:
                                _page.Text(x + 3, baseline, cells[i][j], font, 8);
                                break;
                        }
                    }
                    x += column.Width;
                }
                _y += height;
            }
        }

        private static double RowHeight(IReadOnlyList<string>[] cells) => Math.Max(16, (cells.Max(c => c.Count) * LineStep) + 7);

        private void Totals(IReadOnlyList<Total> totals, string? words)
        {
            const double rowHeight = 12;
            var labelWidth = Math.Max(130, totals.Max(t => PdfFontMetrics.Width(t.Label, t.Bold ? PdfFont.HelveticaBold : PdfFont.Helvetica, 7)) + 10);
            var valueWidth = Math.Max(76, totals.Max(t => PdfFontMetrics.Width(t.Value, PdfFont.HelveticaBold, 8)) + 10);
            var boxX = Right - labelWidth - valueWidth;
            var boxHeight = totals.Count * rowHeight;
            IReadOnlyList<string> wordLines = string.IsNullOrWhiteSpace(words)
                ? Array.Empty<string>()
                : PdfFontMetrics.Wrap(SonPrefix(words), PdfFont.HelveticaBold, 8, boxX - 20 - (Left + 8));
            EnsureSpace(Math.Max(boxHeight, wordLines.Count * 10));
            for (var i = 0; i < totals.Count; i++)
            {
                var t = totals[i];
                var y = _y + (i * rowHeight);
                var font = t.Bold ? PdfFont.HelveticaBold : PdfFont.Helvetica;
                _page.Rectangle(boxX, y, labelWidth, rowHeight).Rectangle(boxX + labelWidth, y, valueWidth, rowHeight)
                    .TextRight(boxX + labelWidth - 4, y + 8.6, t.Label, font, 7)
                    .TextRight(Right - 4, y + 8.8, t.Value, font, 8);
            }
            var wordsTop = _y + (boxHeight / 2) - (wordLines.Count * 10 / 2d);
            for (var i = 0; i < wordLines.Count; i++)
            {
                _page.Text(Left + 8, wordsTop + 7 + (i * 10), wordLines[i], PdfFont.HelveticaBold, 8);
            }
            _y = Math.Max(_y + boxHeight, wordsTop + (wordLines.Count * 10));
        }

        // -------------------------------------------------------------------------------------------- pie
        private void Legends()
        {
            const double areaWidth = Right - QrSize - 16 - Left;
            const double areaCenter = Left + (areaWidth / 2);
            var blocks = _m.Legends.Where(l => !string.IsNullOrWhiteSpace(l))
                .Select((legend, i) => (Size: i == 0 ? 7d : 7.5d, Lines: PdfFontMetrics.Wrap(legend.Trim(), PdfFont.Helvetica, i == 0 ? 7 : 7.5, areaWidth)))
                .ToList();
            var legendsHeight = blocks.Sum(b => b.Lines.Count * (b.Size + 2.5)) + (Math.Max(0, blocks.Count - 1) * 9);
            var qrUrl = WithQrSize(_m.QrUrl, 2);
            var hasQr = !string.IsNullOrWhiteSpace(qrUrl);
            var height = Math.Max(legendsHeight, hasQr ? QrSize : 0);
            _y += 28;
            if (_y + height > Bottom)
            {
                NewPage(first: false);
                _y += 8;
            }
            var top = _y;
            var y = top + 8;
            foreach (var (size, lines) in blocks)
            {
                foreach (var line in lines)
                {
                    _page.TextCenter(areaCenter, y, line, PdfFont.Helvetica, size);
                    y += size + 2.5;
                }
                y += 9;
            }
            if (hasQr)
            {
                var matrix = QrMatrix.Create(qrUrl);
                _page.FillRectangles(QrMatrix.Rectangles(matrix, Right - QrSize, top - 4, QrSize));
            }
            _y = top + height;
        }

        // -------------------------------------------------------------------------------------------- formatos
        private static string Money(decimal value) => value.ToString("#,##0.00", Invariant);

        private static string Date(DateTime value) => value.ToString("dd/MM/yyyy hh:mm tt", Invariant);

        private static string SonPrefix(string words)
        {
            var text = words.Trim();
            return text.StartsWith("Son:", StringComparison.OrdinalIgnoreCase) ? text : "Son: " + text;
        }
    }
}
