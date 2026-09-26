using MINV.Infrastructure.Seeding.Tecnologia;

namespace MINV.Infrastructure.Seeding;

/// <summary>
/// V4.2 · Ilustraciones de los productos de la empresa de prueba Tech Zone Gaming (dibujos propios sin logotipos ni marcas,
/// regla T-09, generados con <c>tools/tecnologia/generar_imagenes_tecnologia.py</c> y embebidos desde
/// <c>Seeding/Tecnologia/Imagenes</c>). Cada producto del catálogo de tecnología trae su ilustración por SKU (campo
/// «imagen» del JSON). En producción cada empresa sube sus propias fotos desde el catálogo.
/// </summary>
public static class ProductImageLibrary
{
    /// <summary>Ilustración genérica cuando el SKU no es del catálogo de tecnología.</summary>
    public const string Fallback = "service";

    /// <summary>Ilustraciones disponibles (nombre sin extensión).</summary>
    public static IReadOnlyList<string> Kinds { get; } = typeof(ProductImageLibrary).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(TechSeedCatalog.ImagePrefix, StringComparison.Ordinal) && n.EndsWith(".png", StringComparison.Ordinal))
        .Select(n => n[TechSeedCatalog.ImagePrefix.Length..^4]).Order(StringComparer.Ordinal).ToList();

    /// <summary>Ilustración de un producto del catálogo de tecnología (por su SKU).</summary>
    public static string KindForSku(string sku)
    {
        var key = (sku ?? string.Empty).Trim().ToUpperInvariant();
        return TechSeedCatalog.Current.HasProduct(key) ? TechSeedCatalog.Current.Product(key).Image : Fallback;
    }

    /// <summary>PNG de la ilustración (o null si no existe).</summary>
    public static byte[]? Get(string kind) => TechSeedCatalog.Image(kind);

    /// <summary>PNG de la ilustración de un producto (por su SKU; la genérica si no es del catálogo).</summary>
    public static byte[] ForSku(string sku) => Get(KindForSku(sku)) ?? Get(Fallback)!;
}
