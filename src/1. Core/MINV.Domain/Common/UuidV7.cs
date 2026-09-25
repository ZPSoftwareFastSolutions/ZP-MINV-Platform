using System.Security.Cryptography;

namespace MINV.Domain.Common;

/// <summary>
/// UUID versión 7 (RFC 9562): 48 bits de milisegundos Unix + 74 bits aleatorios. Se generan en el cliente sin
/// coordinación (sucesor de los IDs <c>E-AAAAMMDD-HHMMSS-XXXX</c> de la V2.1) y, por ser crecientes en el tiempo, no
/// fragmentan los índices B-tree de PostgreSQL.
/// <see cref="NewGuid()"/> es además monótono dentro del proceso (método 3 del RFC: contador de 12 bits en el mismo
/// milisegundo): el orden de creación se conserva aunque se creen miles de filas por milisegundo.
/// </summary>
public static class UuidV7
{
    private static readonly object Gate = new();
    private static long _lastMs = -1;
    private static int _counter;

    /// <summary>UUID v7 del instante actual, estrictamente creciente dentro del proceso.</summary>
    public static Guid NewGuid()
    {
        var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int counter;
        lock (Gate)
        {
            if (ms > _lastMs)
            {
                _lastMs = ms;
                _counter = RandomNumberGenerator.GetInt32(0x400);   // arranque aleatorio con margen para contar
            }
            else if (++_counter > 0xFFF)
            {
                _lastMs++;                                           // más de 4096 IDs en el mismo milisegundo
                _counter = 0;
            }
            ms = _lastMs;
            counter = _counter;
        }
        return Build(ms, counter);
    }

    /// <summary>UUID v7 de un instante dado (p. ej. el Timestamp de un movimiento migrado de la V2.1).</summary>
    public static Guid NewGuid(DateTimeOffset timestamp) =>
        Build(timestamp.ToUnixTimeMilliseconds(), RandomNumberGenerator.GetInt32(0x1000));

    /// <summary>Instante codificado en un UUID v7 (útil en auditoría y pruebas).</summary>
    public static DateTimeOffset GetTimestamp(Guid id)
    {
        Span<byte> b = stackalloc byte[16];
        id.TryWriteBytes(b, bigEndian: true, out _);
        long ms = ((long)b[0] << 40) | ((long)b[1] << 32) | ((long)b[2] << 24) | ((long)b[3] << 16) | ((long)b[4] << 8) | b[5];
        return DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }

    private static Guid Build(long ms, int counter)
    {
        Span<byte> b = stackalloc byte[16];
        RandomNumberGenerator.Fill(b);
        b[0] = (byte)(ms >> 40);
        b[1] = (byte)(ms >> 32);
        b[2] = (byte)(ms >> 24);
        b[3] = (byte)(ms >> 16);
        b[4] = (byte)(ms >> 8);
        b[5] = (byte)ms;
        b[6] = (byte)(0x70 | ((counter >> 8) & 0x0F));   // versión 7 + 4 bits altos del contador
        b[7] = (byte)counter;
        b[8] = (byte)((b[8] & 0x3F) | 0x80);             // variante RFC
        return new Guid(b, bigEndian: true);
    }
}
