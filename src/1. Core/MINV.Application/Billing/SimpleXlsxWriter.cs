using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace MINV.Application.Billing;

/// <summary>
/// V4.1 · Escritor mínimo de libros de Excel (.xlsx, Office Open XML) sin dependencias: una hoja, encabezados en negrita,
/// textos en línea (<c>inlineStr</c>), números como NÚMERO (formato 0.00 para los importes) y fechas como texto
/// dd/mm/aaaa, igual que las plantillas del SIN (libro de ventas y de compras estándar).
/// </summary>
public static class SimpleXlsxWriter
{
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Estilos: 0 = normal, 1 = encabezado (negrita), 2 = importe (0.00), 3 = entero.</summary>
    private const int StyleHeader = 1;
    private const int StyleMoney = 2;
    private const int StyleInteger = 3;

    /// <summary>
    /// Escribe una hoja. Cada celda puede ser <see cref="string"/> (texto), <see cref="decimal"/> (importe con 2 decimales),
    /// <see cref="int"/> o <see cref="long"/> (entero) o null (vacía).
    /// </summary>
    public static byte[] Write(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        var name = string.IsNullOrWhiteSpace(sheetName) ? "Hoja1" : new string(sheetName.Where(c => c is not ('\\' or '/' or '?' or '*' or '[' or ']' or ':'))
            .Take(31).ToArray());
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "</Types>");
            Entry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");
            Entry(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<workbook xmlns=\"{MainNamespace}\" xmlns:r=\"{RelationshipsNamespace}\">" +
                $"<sheets><sheet name=\"{Escape(name)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Entry(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>");
            Entry(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<styleSheet xmlns=\"{MainNamespace}\">" +
                "<fonts count=\"2\"><font><sz val=\"10\"/><name val=\"Arial\"/></font><font><b/><sz val=\"10\"/><name val=\"Arial\"/></font></fonts>" +
                "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"4\">" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                "<xf numFmtId=\"2\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                "<xf numFmtId=\"1\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                "</cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                "</styleSheet>");
            Entry(zip, "xl/worksheets/sheet1.xml", Sheet(headers, rows));
        }
        return output.ToArray();
    }

    private static string Sheet(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append(CultureInfo.InvariantCulture, $"<worksheet xmlns=\"{MainNamespace}\" xmlns:r=\"{RelationshipsNamespace}\">");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        sb.Append("<sheetData>");
        sb.Append("<row r=\"1\">");
        for (var c = 0; c < headers.Count; c++)
        {
            InlineText(sb, Reference(c, 1), headers[c], StyleHeader);
        }
        sb.Append("</row>");
        var r = 1;
        foreach (var row in rows)
        {
            r++;
            sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{r}\">");
            for (var c = 0; c < row.Count; c++)
            {
                var reference = Reference(c, r);
                switch (row[c])
                {
                    case null:
                        break;
                    case decimal d:
                        sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"{StyleMoney}\"><v>{d.ToString(CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case int i:
                        sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"{StyleInteger}\"><v>{i.ToString(CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case long l:
                        sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"{StyleInteger}\"><v>{l.ToString(CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case var other:
                        InlineText(sb, reference, Convert.ToString(other, CultureInfo.InvariantCulture) ?? string.Empty, 0);
                        break;
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void InlineText(StringBuilder sb, string reference, string text, int style) =>
        sb.Append(CultureInfo.InvariantCulture,
            $"<c r=\"{reference}\"{(style > 0 ? $" s=\"{style}\"" : string.Empty)} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>");

    /// <summary>Referencia A1 (columna 0 → A, 26 → AA).</summary>
    public static string Reference(int column, int row)
    {
        var letters = string.Empty;
        for (var n = column + 1; n > 0; n = (n - 1) / 26)
        {
            letters = (char)('A' + ((n - 1) % 26)) + letters;
        }
        return letters + row.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Texto seguro para XML (sin caracteres de control no válidos en XML 1.0).</summary>
    private static string Escape(string text) =>
        SecurityElement.Escape(new string(text.Where(ch => ch is '\t' or '\n' or '\r' || ch >= ' ').ToArray())) ?? string.Empty;

    private static void Entry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
