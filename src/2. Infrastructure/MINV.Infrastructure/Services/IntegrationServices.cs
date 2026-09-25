using System.Security.Cryptography;
using System.Text;
using MINV.Application.Abstractions;

namespace MINV.Infrastructure.Services;

/// <summary>V4 · Origen de la petición (por defecto, el escritorio con conexión directa).</summary>
public sealed class RequestOrigin : IRequestOrigin
{
    public string Channel { get; private set; } = RequestChannels.Desktop;

    public Guid? ApiKeyId { get; private set; }

    public void Set(string channel, Guid? apiKeyId)
    {
        if (channel is not (RequestChannels.Desktop or RequestChannels.Cloud or RequestChannels.Api))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Canal desconocido.");
        }
        Channel = channel;
        ApiKeyId = apiKeyId;
    }
}

/// <summary>
/// V4 · AES-256-GCM con claves maestras fuera de la base. Formato del texto cifrado: base64(nonce 12 ‖ texto ‖ etiqueta
/// 16). La clave vigente cifra; cualquiera de las configuradas descifra (rotación: se agrega la nueva como vigente y la
/// anterior se conserva hasta rotar todos los secretos). Las claves se leen de <c>MINV_INTEGRATION_KEYS</c>:
/// <c>id1:base64(32 bytes);id2:base64(32 bytes)</c> (la primera es la vigente).
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string KeysVariable = "MINV_INTEGRATION_KEYS";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly IReadOnlyDictionary<string, byte[]> _keys;

    public AesGcmSecretProtector(IReadOnlyList<(string Id, byte[] Key)> keys)
    {
        if (keys.Count == 0)
        {
            throw new InvalidOperationException("No hay claves maestras de integración configuradas.");
        }
        foreach (var (id, key) in keys)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 40 || key.Length != 32)
            {
                throw new InvalidOperationException("Cada clave maestra necesita un id (≤ 40) y 32 bytes (AES-256).");
            }
        }
        CurrentKeyId = keys[0].Id;
        _keys = keys.ToDictionary(k => k.Id, k => k.Key, StringComparer.Ordinal);
    }

    public string CurrentKeyId { get; }

    /// <summary>Lee las claves de la variable de entorno (o del texto indicado).</summary>
    public static AesGcmSecretProtector FromConfiguration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                $"Configure {KeysVariable} (id:base64 de 32 bytes) para cifrar los secretos de los webhooks.");
        }
        var keys = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split(':', 2))
            .Select(p => (p[0], Convert.FromBase64String(p[1])))
            .ToList();
        return new AesGcmSecretProtector(keys);
    }

    /// <summary>Clave efímera para la demostración en memoria y las pruebas (no sobrevive al proceso).</summary>
    public static AesGcmSecretProtector Ephemeral() => new([("efimera", RandomNumberGenerator.GetBytes(32))]);

    public string Protect(string secret)
    {
        var plain = Encoding.UTF8.GetBytes(secret);
        var buffer = new byte[NonceSize + plain.Length + TagSize];
        var nonce = buffer.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_keys[CurrentKeyId], TagSize);
        aes.Encrypt(nonce, plain, buffer.AsSpan(NonceSize, plain.Length), buffer.AsSpan(NonceSize + plain.Length, TagSize),
            Encoding.ASCII.GetBytes(CurrentKeyId));
        return Convert.ToBase64String(buffer);
    }

    public string Unprotect(string ciphertext, string keyId)
    {
        if (!_keys.TryGetValue(keyId, out var key))
        {
            throw new CryptographicException($"La clave maestra «{keyId}» no está configurada: no se puede descifrar el secreto.");
        }
        var buffer = Convert.FromBase64String(ciphertext);
        var length = buffer.Length - NonceSize - TagSize;
        var plain = new byte[length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(buffer.AsSpan(0, NonceSize), buffer.AsSpan(NonceSize, length), buffer.AsSpan(NonceSize + length, TagSize), plain,
            Encoding.ASCII.GetBytes(keyId));
        return Encoding.UTF8.GetString(plain);
    }
}

/// <summary>Sin claves configuradas (escritorio con conexión directa): los webhooks se registran desde el servidor en la
/// nube o el gateway, que sí las tienen.</summary>
public sealed class UnconfiguredSecretProtector : ISecretProtector
{
    public string CurrentKeyId => throw Missing();

    public string Protect(string secret) => throw Missing();

    public string Unprotect(string ciphertext, string keyId) => throw Missing();

    private static Application.Common.AccessDeniedException Missing() =>
        new($"Este equipo no tiene la clave maestra de integraciones ({AesGcmSecretProtector.KeysVariable}): registre los webhooks desde el " +
            "servidor en la nube o el API Gateway.");
}
