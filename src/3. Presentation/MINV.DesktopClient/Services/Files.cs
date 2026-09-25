using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;

namespace MINV.DesktopClient.Services;

/// <summary>Cuadros de diálogo de archivos de Windows.</summary>
public static class FileDialogs
{
    public static string? SaveCsv(string suggestedName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar a Excel (CSV)",
            FileName = suggestedName,
            DefaultExt = ".csv",
            Filter = "Archivo CSV para Excel (*.csv)|*.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

/// <summary>
/// CSV que Excel abre directamente en español: UTF-8 con BOM (acentos correctos) y el separador de listas de la
/// cultura (punto y coma cuando la coma es el separador decimal).
/// </summary>
public static class Csv
{
    public static void Write(string path, IReadOnlyList<string> headers, IEnumerable<object?[]> rows)
    {
        var separator = Fmt.Culture.TextInfo.ListSeparator is { Length: > 0 } ls && ls != Fmt.Culture.NumberFormat.NumberDecimalSeparator ? ls : ";";
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(string.Join(separator, headers.Select(h => Escape(h, separator))));
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(separator, row.Select(v => Escape(Format(v), separator))));
        }
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        decimal d => d.ToString("0.######", Fmt.Culture),
        double d => d.ToString("0.######", Fmt.Culture),
        int i => i.ToString(Fmt.Culture),
        DateOnly date => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateTimeOffset dt => dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Escape(string value, string separator)
    {
        // Evita que Excel interprete como fórmula un texto que empieza con =, +, - o @ (inyección de fórmulas en CSV).
        if (value.Length > 0 && value[0] is '=' or '+' or '@' or '\t' or '\r' || (value.StartsWith('-') && !decimal.TryParse(value, NumberStyles.Any, Fmt.Culture, out _)))
        {
            value = "'" + value;
        }
        return value.Contains(separator, StringComparison.Ordinal) || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}

/// <summary>Portapapeles con reintento (otra aplicación puede tenerlo abierto un instante).</summary>
public static class ClipboardText
{
    public static bool TrySet(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(60);
            }
        }
        return false;
    }
}
