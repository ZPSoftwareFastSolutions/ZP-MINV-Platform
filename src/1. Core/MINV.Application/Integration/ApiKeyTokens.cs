using System.Security.Cryptography;
using System.Text;

namespace MINV.Application.Integration;

/// <summary>Token recién generado: el texto completo se muestra UNA vez; en la base solo quedan prefijo y hash.</summary>
public sealed record GeneratedApiKey(string Token, string Prefix, string Hash);

/// <summary>
/// V4 · Formato de las API Keys: <c>minv_&lt;prefijo 8&gt;_&lt;secreto 43&gt;</c> (secreto = 32 bytes aleatorios en base64url,
/// 256 bits). El prefijo es público y sirve para encontrar la llave; el hash es SHA-256 del token completo (con 256 bits
/// de azar no hace falta sal ni un KDF lento: no hay diccionario posible). La comparación es en tiempo constante.
/// También genera los secretos de firma de los webhooks y la firma de cada entrega.
/// </summary>
public static class ApiKeyTokens
{
    public const string Scheme = "minv";
    private const string PrefixAlphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    public static GeneratedApiKey Generate()
    {
        var prefix = new string(Enumerable.Range(0, 8).Select(_ => PrefixAlphabet[RandomNumberGenerator.GetInt32(PrefixAlphabet.Length)]).ToArray());
        var token = $"{Scheme}_{prefix}_{Base64Url(RandomNumberGenerator.GetBytes(32))}";
        return new GeneratedApiKey(token, prefix, Hash(token));
    }

    /// <summary>SHA-256 del texto en hexadecimal (minúsculas).</summary>
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Prefijo de un token bien formado (o null): <c>minv_abcd2345_…</c>.</summary>
    public static string? PrefixOf(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 20 or > 200)
        {
            return null;
        }
        var parts = token.Split('_', 3);
        return parts.Length == 3 && parts[0] == Scheme && parts[1].Length == 8 && parts[1].All(c => PrefixAlphabet.Contains(c)) && parts[2].Length >= 32
            ? parts[1]
            : null;
    }

    /// <summary>Compara el hash del token presentado con el guardado en tiempo constante.</summary>
    public static bool Matches(string token, string storedHash)
    {
        var actual = Encoding.ASCII.GetBytes(Hash(token));
        var expected = Encoding.ASCII.GetBytes(storedHash.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Token de sesión del servidor en la nube (256 bits, base64url). En la base solo queda su hash.</summary>
    public static string NewSessionToken() => "mses_" + Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Secreto de firma de un webhook: 32 bytes aleatorios (se guarda CIFRADO con la clave maestra).</summary>
    public static string NewWebhookSecret() => "whsec_" + Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Firma de una entrega: <c>t=&lt;unix&gt;,v1=&lt;hex(HMAC-SHA256(secreto, t + "." + cuerpo))&gt;</c>. Durante la rotación del
    /// secreto se firma con los dos (<c>t=…,v1=…,v1=…</c>): el receptor acepta si alguna coincide.
    /// </summary>
    public static string Sign(IEnumerable<string> secrets, long unixSeconds, string body)
    {
        var payload = Encoding.UTF8.GetBytes($"{unixSeconds}.{body}");
        var signatures = secrets.Select(s => "v1=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(s), payload)).ToLowerInvariant());
        return $"t={unixSeconds}," + string.Join(",", signatures);
    }

    /// <summary>Verificación del lado del receptor (la usan las pruebas y el ejemplo de la documentación): tolerancia de
    /// 5 minutos contra repeticiones y comparación en tiempo constante.</summary>
    public static bool Verify(string secret, string header, string body, DateTimeOffset now, TimeSpan? tolerance = null)
    {
        var parts = header.Split(',', StringSplitOptions.TrimEntries);
        var t = parts.FirstOrDefault(p => p.StartsWith("t=", StringComparison.Ordinal));
        if (t is null || !long.TryParse(t[2..], out var unix) || Math.Abs(now.ToUnixTimeSeconds() - unix) > (tolerance ?? TimeSpan.FromMinutes(5)).TotalSeconds)
        {
            return false;
        }
        var expected = Encoding.ASCII.GetBytes(Sign([secret], unix, body).Split(',')[1]);
        return parts.Where(p => p.StartsWith("v1=", StringComparison.Ordinal))
            .Any(p => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(p), expected));
    }

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
