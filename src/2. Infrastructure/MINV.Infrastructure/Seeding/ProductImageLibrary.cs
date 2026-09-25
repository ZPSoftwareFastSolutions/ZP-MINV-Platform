using System.Globalization;
using System.Text;

namespace MINV.Infrastructure.Seeding;

/// <summary>
/// Ilustraciones de productos incluidas en la aplicación (dibujos propios generados con
/// <c>tools/generar_imagenes_productos.py</c>): se asignan por el nombre del producto a los datos de prueba y a la
/// demostración. En producción cada empresa sube sus propias fotos desde el catálogo.
/// </summary>
public static class ProductImageLibrary
{
    private const string Prefix = "MINV.Infrastructure.Seeding.Imagenes.";

    /// <summary>Palabra clave (sin tildes, en minúsculas) → ilustración. Se evalúan en orden: la primera que aparece gana.</summary>
    private static readonly (string Keyword, string Kind)[] Rules =
    [
        ("llave de paso", "llave_paso"), ("cinta metrica", "cinta_metrica"), ("flexometro", "cinta_metrica"), ("cinta aislante", "cinta_aislante"),
        ("teflon", "cinta_aislante"), ("sierra caladora", "taladro"), ("atornillador", "taladro"), ("taladro", "taladro"), ("amoladora", "disco"),
        ("disco", "disco"), ("broca", "caja"), ("tornillo", "tornillo"), ("clavo", "tornillo"), ("perno", "tornillo"), ("tarugo", "caja"),
        ("chazo", "caja"), ("martillo", "martillo"), ("candado", "candado"), ("serrucho", "serrucho"), ("sierra", "serrucho"),
        ("destornillador", "destornillador"), ("alicate", "llave"), ("llave", "llave"), ("extension", "cable"), ("manguera", "cable"),
        ("cable", "cable"), ("bombillo", "foco"), ("reflector", "foco"), ("lampara", "foco"), ("foco", "foco"), ("tomacorriente", "enchufe"),
        ("interruptor", "enchufe"), ("enchufe", "enchufe"), ("breaker", "enchufe"), ("tubo", "tubo"), ("codo", "tubo"), ("tee", "tubo"),
        ("union", "tubo"), ("grifo", "grifo"), ("ducha", "grifo"), ("anticorrosiva", "pintura_roja"), ("roja", "pintura_roja"),
        ("latex", "pintura_blanca"), ("blanca", "pintura_blanca"), ("esmalte", "pintura"), ("pintura", "pintura"), ("brocha", "brocha"),
        ("rodillo", "rodillo"), ("lija", "caja"), ("casco", "casco"), ("guante", "guantes"), ("bota", "botas"), ("gafa", "gafas"),
        ("lente", "gafas"), ("tapabocas", "mascarilla"), ("mascarilla", "mascarilla"), ("chaleco", "chaleco"), ("extintor", "extintor"),
        ("desinfectante", "botella_verde"), ("jabon", "botella_verde"), ("hipoclorito", "botella"), ("cloro", "botella"), ("thinner", "botella"),
        ("pegamento", "botella"), ("silicona", "botella"), ("aceite", "botella"), ("bolsa", "bolsa"), ("escoba", "escoba"),
        ("trapeador", "escoba"), ("cemento", "cemento"), ("yeso", "cemento"), ("estuco", "cemento"), ("detergente", "cemento"),
        ("escalera", "escalera"), ("nivel", "nivel"), ("pala", "pala"), ("carretilla", "carretilla"),
    ];

    /// <summary>Ilustraciones disponibles (nombre sin extensión).</summary>
    public static IReadOnlyList<string> Kinds { get; } = typeof(ProductImageLibrary).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".png", StringComparison.Ordinal))
        .Select(n => n[Prefix.Length..^4]).Order(StringComparer.Ordinal).ToList();

    /// <summary>Ilustración que corresponde a un producto (por su nombre; si nada coincide, una caja).</summary>
    public static string KindFor(string productName)
    {
        var text = Normalize(productName);
        foreach (var (keyword, kind) in Rules)
        {
            if (text.Contains(keyword, StringComparison.Ordinal))
            {
                return kind;
            }
        }
        return "caja";
    }

    /// <summary>PNG de la ilustración (o null si no existe).</summary>
    public static byte[]? Get(string kind)
    {
        using var stream = typeof(ProductImageLibrary).Assembly.GetManifestResourceStream(Prefix + kind + ".png");
        if (stream is null)
        {
            return null;
        }
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static byte[] ForProduct(string productName) => Get(KindFor(productName)) ?? Get("caja")!;

    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}
