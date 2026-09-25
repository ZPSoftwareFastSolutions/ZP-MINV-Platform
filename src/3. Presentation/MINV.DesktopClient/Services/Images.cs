using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MINV.Application.Catalog;

namespace MINV.DesktopClient.Services;

/// <summary>
/// Imágenes de los productos (galería del stock, catálogo y punto de venta). Se leen una vez por sesión, se decodifican
/// a miniatura (congeladas: se comparten entre pantallas sin copiar) y se invalidan al cambiar una imagen.
/// </summary>
public sealed class ImageCache(SerialMediator mediator)
{
    private Task<Dictionary<string, ImageSource>>? _images;

    /// <summary>Se incrementa cada vez que cambia una imagen (las pantallas vuelven a pedirlas).</summary>
    public int Version { get; private set; }

    public event EventHandler? Changed;

    public async Task<IReadOnlyDictionary<string, ImageSource>> AllAsync(bool force = false)
    {
        if (force || _images is null || _images.IsFaulted || _images.IsCanceled)
        {
            _images = LoadAsync();
        }
        return await _images;
    }

    /// <summary>Miniatura de un producto (o null si no tiene imagen).</summary>
    public async Task<ImageSource?> ForAsync(string sku) => (await AllAsync()).GetValueOrDefault(sku);

    /// <summary>Imagen en tamaño de ficha (se decodifica aparte, más grande que la miniatura).</summary>
    public async Task<ImageSource?> LargeAsync(Guid variantId)
    {
        var images = await mediator.SendAsync(new GetProductImagesQuery([variantId]));
        return images.Count == 0 ? null : Decode(images[0].Content, 520);
    }

    public void Invalidate()
    {
        _images = null;
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<Dictionary<string, ImageSource>> LoadAsync()
    {
        var images = await mediator.SendAsync(new GetProductImagesQuery());
        var map = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in images)
        {
            if (Decode(image.Content, 240) is { } source)
            {
                map[image.Sku] = source;
            }
        }
        return map;
    }

    /// <summary>Decodifica una imagen a un ancho máximo (no se guarda el original en memoria). Null si está dañada.</summary>
    public static ImageSource? Decode(byte[] content, int width)
    {
        try
        {
            using var stream = new MemoryStream(content);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = width;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>Imagen elegida por el usuario, lista para guardar (PNG o JPEG de hasta 1 MB, como exige la base).</summary>
public sealed record PickedImage(byte[] Content, string ContentType, string FileName, ImageSource Preview);

public static class ImageFiles
{
    public const int MaxBytes = 1024 * 1024;

    /// <summary>
    /// Abre el cuadro para elegir una foto. Las fotos grandes se reducen a 800 px y se guardan como JPEG para no pasar de
    /// 1 MB; las pequeñas se guardan tal cual. Devuelve null si el usuario cancela; lanza si el archivo no es una imagen.
    /// </summary>
    public static PickedImage? Pick()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Elegir la imagen del producto",
            Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }
        var bytes = File.ReadAllBytes(dialog.FileName);
        var name = Path.GetFileName(dialog.FileName);
        BitmapSource source;
        try
        {
            using var stream = new MemoryStream(bytes);
            source = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("El archivo elegido no es una imagen válida.", ex);
        }
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var keep = bytes.Length <= MaxBytes && Math.Max(source.PixelWidth, source.PixelHeight) <= 1600
                   && extension is ".png" or ".jpg" or ".jpeg";
        if (keep)
        {
            return new PickedImage(bytes, extension == ".png" ? "image/png" : "image/jpeg", name, ImageCache.Decode(bytes, 520) ?? source);
        }
        var scale = Math.Min(1d, 800d / Math.Max(source.PixelWidth, source.PixelHeight));
        BitmapSource resized = scale < 1 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
        foreach (var quality in new[] { 88, 75, 60, 45 })
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(resized));
            using var output = new MemoryStream();
            encoder.Save(output);
            if (output.Length <= MaxBytes)
            {
                var jpeg = output.ToArray();
                return new PickedImage(jpeg, "image/jpeg", Path.ChangeExtension(name, ".jpg"), ImageCache.Decode(jpeg, 520) ?? resized);
            }
        }
        throw new InvalidDataException("La imagen es demasiado grande incluso reducida: elija una de menos de 1 MB.");
    }
}
