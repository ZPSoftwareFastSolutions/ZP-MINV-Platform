using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>
/// Imagen de una variante (la foto que se ve en el catálogo, el stock en galería y el punto de venta). Una por variante;
/// se guarda en la base (PNG o JPEG, máximo 1 MB) para que todas las estaciones vean la misma sin compartir carpetas.
/// </summary>
public sealed class ProductImage : Entity
{
    public const int MaxBytes = 1_048_576;

    private ProductImage()
    {
    }

    public ProductImage(Guid tenantId, Guid variantId, byte[] content, string contentType, string? fileName)
        : base(tenantId)
    {
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        Replace(content, contentType, fileName);
    }

    public Guid VariantId { get; private set; }

    public byte[] Content { get; private set; } = [];

    public string ContentType { get; private set; } = string.Empty;

    public string? FileName { get; private set; }

    public void Replace(byte[] content, string contentType, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        Guard.That(content.Length > 0, "image.empty", "La imagen está vacía.");
        Guard.That(content.Length <= MaxBytes, "image.size", "La imagen supera 1 MB: use una más liviana.");
        Guard.That(contentType is "image/png" or "image/jpeg", "image.type", "La imagen debe ser PNG o JPEG.");
        Content = content;
        ContentType = contentType;
        FileName = Guard.OptionalText(fileName, "El nombre del archivo", 200);
    }
}
