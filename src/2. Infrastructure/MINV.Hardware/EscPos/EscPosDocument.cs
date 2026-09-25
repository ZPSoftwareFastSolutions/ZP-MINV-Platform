using System.Globalization;
using System.Text;

namespace MINV.Hardware.EscPos;

/// <summary>
/// Constructor de documentos ESC/POS (impresoras térmicas de 58/80 mm). Genera los bytes exactos que entiende la
/// impresora: inicialización, alineación, negrita, doble tamaño, texto en la página de códigos PC858 (acentos y «ñ»),
/// avance, códigos de barras, QR, apertura del cajón y corte.
/// </summary>
public sealed class EscPosDocument
{
    public const byte Esc = 0x1B;
    public const byte Gs = 0x1D;
    public const byte Lf = 0x0A;

    /// <summary>Página de códigos 858 (= 850 con €) para español; en ESC/POS se selecciona con ESC t 19.</summary>
    private const int CodePage = 858;
    private const byte CodePageSelector = 19;

    private static readonly Encoding TextEncoding = CreateEncoding();
    private readonly List<byte> _bytes = new(512);

    public EscPosDocument(int columns = 48)
    {
        Columns = columns is >= 24 and <= 64 ? columns : throw new ArgumentOutOfRangeException(nameof(columns));
        Initialize();
    }

    /// <summary>Caracteres por línea (48 en 80 mm con fuente A; 32 en 58 mm).</summary>
    public int Columns { get; }

    public EscPosDocument Initialize()
    {
        _bytes.AddRange([Esc, (byte)'@', Esc, (byte)'t', CodePageSelector]);
        return this;
    }

    public EscPosDocument Align(TextAlign align)
    {
        _bytes.AddRange([Esc, (byte)'a', (byte)align]);
        return this;
    }

    public EscPosDocument Bold(bool on)
    {
        _bytes.AddRange([Esc, (byte)'E', (byte)(on ? 1 : 0)]);
        return this;
    }

    /// <summary>Tamaño de carácter (1 a 8 veces en ancho y alto).</summary>
    public EscPosDocument Size(int width, int height)
    {
        if (width is < 1 or > 8 || height is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "El tamaño va de 1 a 8.");
        }
        _bytes.AddRange([Gs, (byte)'!', (byte)(((width - 1) << 4) | (height - 1))]);
        return this;
    }

    public EscPosDocument Text(string text)
    {
        _bytes.AddRange(TextEncoding.GetBytes(Sanitize(text)));
        return this;
    }

    public EscPosDocument Line(string text = "") => Text(text).Feed(1);

    /// <summary>Línea con texto a la izquierda y a la derecha (p. ej. descripción e importe).</summary>
    public EscPosDocument Columns2(string left, string right)
    {
        var space = Columns - right.Length;
        var l = left.Length > space - 1 ? left[..Math.Max(0, space - 1)] : left;
        return Line(l + new string(' ', Math.Max(1, Columns - l.Length - right.Length)) + right);
    }

    public EscPosDocument Separator(char c = '-') => Line(new string(c, Columns));

    public EscPosDocument Feed(int lines)
    {
        _bytes.AddRange([Esc, (byte)'d', (byte)Math.Clamp(lines, 0, 255)]);
        return this;
    }

    /// <summary>Código de barras CODE128 (subconjunto B) con el texto debajo.</summary>
    public EscPosDocument Code128(string data)
    {
        var payload = Encoding.ASCII.GetBytes("{B" + data);
        if (payload.Length > 255)
        {
            throw new ArgumentException("El código de barras es demasiado largo.", nameof(data));
        }
        _bytes.AddRange([Gs, (byte)'h', 80, Gs, (byte)'w', 2, Gs, (byte)'H', 2]);   // alto, ancho, texto debajo
        _bytes.AddRange([Gs, (byte)'k', 73, (byte)payload.Length]);
        _bytes.AddRange(payload);
        return this;
    }

    /// <summary>Código QR (modelo 2, corrección M).</summary>
    public EscPosDocument Qr(string data, int moduleSize = 6)
    {
        var payload = Encoding.UTF8.GetBytes(data);
        var len = payload.Length + 3;
        _bytes.AddRange([Gs, (byte)'(', (byte)'k', 4, 0, 49, 65, 50, 0]);                       // modelo 2
        _bytes.AddRange([Gs, (byte)'(', (byte)'k', 3, 0, 49, 67, (byte)Math.Clamp(moduleSize, 1, 16)]);
        _bytes.AddRange([Gs, (byte)'(', (byte)'k', 3, 0, 49, 69, 49]);                           // corrección M
        _bytes.AddRange([Gs, (byte)'(', (byte)'k', (byte)(len & 0xFF), (byte)(len >> 8), 49, 80, 48]);
        _bytes.AddRange(payload);
        _bytes.AddRange([Gs, (byte)'(', (byte)'k', 3, 0, 49, 81, 48]);                           // imprimir
        return this;
    }

    /// <summary>Pulso al cajón de dinero (pin 2).</summary>
    public EscPosDocument OpenCashDrawer()
    {
        _bytes.AddRange([Esc, (byte)'p', 0, 25, 250]);
        return this;
    }

    /// <summary>Avanza y corta el papel (corte parcial).</summary>
    public EscPosDocument Cut()
    {
        _bytes.AddRange([Gs, (byte)'V', 66, 3]);
        return this;
    }

    public byte[] ToArray() => [.. _bytes];

    public static string Money(decimal amount) => amount.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-BO"));

    private static string Sanitize(string text) =>
        new(text.Where(ch => ch >= ' ' || ch == '\n').ToArray());

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(CodePage, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    }
}

public enum TextAlign : byte
{
    Left = 0,
    Center = 1,
    Right = 2,
}
