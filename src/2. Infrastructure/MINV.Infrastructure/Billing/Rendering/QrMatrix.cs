using QRCoder;

namespace MINV.Infrastructure.Billing.Rendering;

/// <summary>
/// V4.1 · Matriz del código QR (corrección M) SIN la zona blanca de 4 módulos que agrega QRCoder: la usa la
/// representación gráfica para dibujar el QR vectorial (cada módulo oscuro es un rectángulo negro).
/// </summary>
public static class QrMatrix
{
    public static bool[,] Create(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        using var data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var rows = data.ModuleMatrix;
        var symbol = 17 + (4 * data.Version);
        var quiet = Math.Max(0, (rows.Count - symbol) / 2);
        var size = rows.Count - (2 * quiet);
        var matrix = new bool[size, size];
        for (var r = 0; r < size; r++)
        {
            for (var c = 0; c < size; c++)
            {
                matrix[r, c] = rows[r + quiet][c + quiet];
            }
        }
        return matrix;
    }

    /// <summary>Rectángulos negros del QR dibujado en un cuadrado de <paramref name="size"/> puntos con la esquina superior
    /// izquierda en (<paramref name="x"/>, <paramref name="y"/>). Une los módulos oscuros consecutivos de cada fila.</summary>
    public static IEnumerable<(double X, double Y, double Width, double Height)> Rectangles(bool[,] matrix, double x, double y, double size)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        var n = matrix.GetLength(0);
        var module = size / n;
        for (var r = 0; r < n; r++)
        {
            var c = 0;
            while (c < n)
            {
                if (!matrix[r, c])
                {
                    c++;
                    continue;
                }
                var start = c;
                while (c < n && matrix[r, c])
                {
                    c++;
                }
                yield return (x + (start * module), y + (r * module), (c - start) * module, module);
            }
        }
    }
}
