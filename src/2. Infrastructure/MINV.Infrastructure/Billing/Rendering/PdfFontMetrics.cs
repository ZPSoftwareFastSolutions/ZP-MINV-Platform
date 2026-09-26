namespace MINV.Infrastructure.Billing.Rendering;

/// <summary>Fuentes estándar Type1 del PDF (no se incrustan: todo lector las trae) con WinAnsiEncoding.</summary>
public enum PdfFont
{
    Helvetica,
    HelveticaBold,
    HelveticaBoldOblique,
}

/// <summary>
/// V4.1 · Métricas de Helvetica y Helvetica-Bold (anchos AFM de Adobe, en milésimas del cuerpo, para los códigos 32–255 de
/// WinAnsiEncoding) y codificación WinAnsi (acentos, «ñ», «°», comillas tipográficas). Sirven para alinear a la derecha,
/// centrar y cortar el texto en líneas sin depender de ninguna biblioteca.
/// </summary>
public static class PdfFontMetrics
{
    private const int FirstCode = 32;

    private static readonly int[] Helvetica =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,        // 32–47
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,        // 48–63
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,       // 64–79
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,        // 80–95
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,        // 96–111
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584, 350,        // 112–127
        556, 350, 222, 556, 333, 1000, 556, 556, 333, 1000, 667, 333, 1000, 350, 611, 350,     // 128–143
        350, 222, 222, 333, 333, 350, 556, 1000, 333, 1000, 500, 333, 944, 350, 500, 667,      // 144–159
        278, 333, 556, 556, 556, 556, 260, 556, 333, 737, 370, 556, 584, 333, 737, 333,        // 160–175
        400, 584, 333, 333, 333, 556, 537, 278, 333, 333, 365, 556, 834, 834, 834, 611,        // 176–191
        667, 667, 667, 667, 667, 667, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278,       // 192–207
        722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,        // 208–223
        556, 556, 556, 556, 556, 556, 889, 500, 556, 556, 556, 556, 278, 278, 278, 278,        // 224–239
        556, 556, 556, 556, 556, 556, 556, 584, 611, 556, 556, 556, 556, 500, 556, 500,        // 240–255
    ];

    private static readonly int[] HelveticaBold =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,        // 32–47
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,        // 48–63
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,        // 64–79
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,        // 80–95
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,        // 96–111
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584, 350,        // 112–127
        556, 350, 278, 556, 500, 1000, 556, 556, 333, 1000, 667, 333, 1000, 350, 611, 350,     // 128–143
        350, 278, 278, 500, 500, 350, 556, 1000, 333, 1000, 556, 333, 944, 350, 500, 667,      // 144–159
        278, 333, 556, 556, 556, 556, 280, 556, 333, 737, 370, 556, 584, 333, 737, 333,        // 160–175
        400, 584, 333, 333, 333, 611, 556, 278, 333, 333, 365, 556, 834, 834, 834, 611,        // 176–191
        722, 722, 722, 722, 722, 722, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278,       // 192–207
        722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,        // 208–223
        556, 556, 556, 556, 556, 556, 889, 556, 556, 556, 556, 556, 278, 278, 278, 278,        // 224–239
        611, 611, 611, 611, 611, 611, 611, 584, 611, 611, 611, 611, 611, 556, 611, 556,        // 240–255
    ];

    /// <summary>Caracteres de WinAnsi (0x80–0x9F) que no coinciden con Latin-1.</summary>
    private static readonly Dictionary<char, byte> WinAnsiSpecials = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87, ['ˆ'] = 0x88,
        ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93,
        ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B,
        ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
    };

    /// <summary>Nombre PostScript de la fuente (/BaseFont).</summary>
    public static string BaseFont(PdfFont font) => font switch
    {
        PdfFont.Helvetica => "Helvetica",
        PdfFont.HelveticaBold => "Helvetica-Bold",
        PdfFont.HelveticaBoldOblique => "Helvetica-BoldOblique",
        _ => throw new ArgumentOutOfRangeException(nameof(font)),
    };

    /// <summary>Texto en bytes WinAnsi: lo que no existe en la codificación se reemplaza por «?».</summary>
    public static byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            bytes[i] = Encode(text[i]);
        }
        return bytes;
    }

    /// <summary>Ancho del texto en puntos con la fuente y el cuerpo dados.</summary>
    public static double Width(string text, PdfFont font, double size)
    {
        ArgumentNullException.ThrowIfNull(text);
        var widths = font == PdfFont.Helvetica ? Helvetica : HelveticaBold;
        var units = 0;
        foreach (var ch in text)
        {
            units += widths[Encode(ch) - FirstCode];
        }
        return units * size / 1000d;
    }

    /// <summary>Corta el texto en líneas que entran en <paramref name="maxWidth"/>: por palabras y, si una palabra sola no
    /// entra, por caracteres. Los saltos de línea del texto se respetan.</summary>
    public static IReadOnlyList<string> Wrap(string? text, PdfFont font, double size, double maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var current = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (Width(candidate, font, size) <= maxWidth)
                {
                    current = candidate;
                    continue;
                }
                if (current.Length > 0)
                {
                    lines.Add(current);
                }
                current = word;
                if (Width(word, font, size) > maxWidth)
                {
                    var pieces = BreakCharacters(word, font, size, maxWidth);
                    lines.AddRange(pieces.Take(pieces.Count - 1));
                    current = pieces[^1];
                }
            }
            lines.Add(current);
        }
        return lines;
    }

    /// <summary>Corta un texto sin espacios (CUF, códigos) en trozos que entran en <paramref name="maxWidth"/>.</summary>
    public static IReadOnlyList<string> BreakCharacters(string? text, PdfFont font, double size, double maxWidth)
    {
        var pieces = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in text ?? string.Empty)
        {
            if (current.Length > 0 && Width(current.ToString() + ch, font, size) > maxWidth)
            {
                pieces.Add(current.ToString());
                current.Clear();
            }
            current.Append(ch);
        }
        pieces.Add(current.ToString());
        return pieces;
    }

    private static byte Encode(char ch)
    {
        if (ch is >= ' ' and <= '~' or >= ' ' and <= 'ÿ')
        {
            return (byte)ch;
        }
        if (ch == '\t')
        {
            return (byte)' ';
        }
        return WinAnsiSpecials.TryGetValue(ch, out var b) ? b : (byte)'?';
    }
}
