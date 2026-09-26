namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>V4.1 · Fragmentos de CHECK compartidos por las configuraciones de la facturación.</summary>
internal static class BillingChecks
{
    /// <summary>Estado guardado como texto: solo los nombres del enum (la lista sale del enum y no se desincroniza).</summary>
    public static string In<TEnum>(string column) where TEnum : struct, Enum =>
        $"{column} IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"))})";

    /// <summary>Ambiente del SIN: 1 producción, 2 pruebas y piloto.</summary>
    public static string Environment(string column = "environment") => $"{column} IN (1, 2)";

    /// <summary>Huella SHA-256 en hexadecimal minúscula.</summary>
    public static string Sha256(string column) => $"{column} ~ '^[0-9a-f]{{64}}$'";
}
