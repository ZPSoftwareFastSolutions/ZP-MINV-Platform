using System.Globalization;
using System.IO;
using System.Text;

namespace MINV.DesktopClient.Services;

/// <summary>V7 · Tabla que se exporta: encabezados y filas con valores TIPADOS (números, fechas, textos).</summary>
public sealed record CsvTable(IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows)
{
    public int Count => Rows.Count;

    /// <summary>Arma la tabla a partir de las filas visibles de una lista (lo que el usuario ve con sus filtros).</summary>
    public static CsvTable Of<T>(IReadOnlyList<string> headers, IEnumerable<T> items, Func<T, object?[]> row) =>
        new(headers, items.Select(row).ToList());
}

/// <summary>
/// V7 · Exportación a CSV compartida por todas las listas del escritorio. Un solo formato:
/// <list type="bullet">
/// <item>UTF-8 con BOM (Excel muestra bien los acentos y la «ñ»);</item>
/// <item>separador «;» siempre (Excel en español lo abre en columnas; la coma queda para los decimales);</item>
/// <item>números con la coma decimal de la cultura y sin separador de miles; fechas dd/MM/aaaa y horas locales;</item>
/// <item>textos NEUTRALIZADOS contra la inyección de fórmulas: una celda de texto que empieza con «=», «+», «-», «@»
/// (también sus variantes de ancho completo), tabulador o retorno se escribe con un apóstrofo delante, así Excel la muestra
/// como texto y nunca la evalúa. Los valores numéricos se escriben como números (un −5 sigue siendo un número).</item>
/// </list>
/// </summary>
public static class CsvExport
{
    public const string Separator = ";";

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>Caracteres que Excel (o LibreOffice) interpreta como el inicio de una fórmula o de un comando.</summary>
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r', '\n', '＝', '＋', '－', '＠'];

    /// <summary>Contenido completo del archivo (sin el BOM, que agrega <see cref="Write"/>), con fin de línea de Windows.</summary>
    public static string Text(CsvTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var sb = new StringBuilder();
        sb.Append(string.Join(Separator, table.Headers.Select(h => Quote(Neutralize(h))))).Append("\r\n");
        foreach (var row in table.Rows)
        {
            sb.Append(string.Join(Separator, row.Select(Cell))).Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Escribe el archivo (UTF-8 con BOM). Lanza <see cref="IOException"/> si el archivo está abierto en otro programa.</summary>
    public static void Write(string path, CsvTable table) => File.WriteAllText(path, Text(table), Utf8WithBom);

    /// <summary>Bytes del archivo tal como queda en el disco (con el BOM al principio).</summary>
    public static byte[] Bytes(CsvTable table) => [.. Utf8WithBom.GetPreamble(), .. Utf8WithBom.GetBytes(Text(table))];

    /// <summary>Una celda ya formateada, neutralizada y entre comillas si hace falta.</summary>
    public static string Cell(object? value) => Quote(value switch
    {
        null => string.Empty,
        string text => Neutralize(text),
        decimal d => d.ToString("0.######", Fmt.Culture),
        double d => d.ToString("0.######", Fmt.Culture),
        float f => f.ToString("0.######", Fmt.Culture),
        int or long or short or byte => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        bool b => b ? "Sí" : "No",
        DateOnly date => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateTime time => time.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        DateTimeOffset instant => instant.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        _ => Neutralize(value.ToString() ?? string.Empty),
    });

    /// <summary>Texto que empieza como una fórmula → con un apóstrofo delante (regla de OWASP contra la inyección en CSV).</summary>
    public static string Neutralize(string text) =>
        text.Length > 0 && Array.IndexOf(FormulaStarts, text[0]) >= 0 ? "'" + text : text;

    private static string Quote(string value) =>
        value.Contains(Separator, StringComparison.Ordinal) || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}
