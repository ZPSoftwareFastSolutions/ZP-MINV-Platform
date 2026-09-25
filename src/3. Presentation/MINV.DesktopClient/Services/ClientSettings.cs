using System.IO;
using System.Text.Json;

namespace MINV.DesktopClient.Services;

/// <summary>
/// Preferencias de esta estación (no de la empresa): última empresa y correo, tema, barra lateral y periféricos.
/// Se guardan en <c>%LOCALAPPDATA%\M-INV\cliente.json</c>. Nunca contienen contraseñas.
/// </summary>
public sealed class ClientSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string? TenantCode { get; set; }

    public string? Email { get; set; }

    public bool Remember { get; set; } = true;

    /// <summary>«sistema», «claro» u «oscuro».</summary>
    public string Theme { get; set; } = "sistema";

    public bool CompactSidebar { get; set; }

    public string? PrinterKind { get; set; }

    public string? PrinterTarget { get; set; }

    public int BaudRate { get; set; } = 9600;

    public int PrinterColumns { get; set; } = 48;

    /// <summary>En las capturas de pantalla automáticas no se escribe en el perfil del usuario.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsReadOnly { get; set; }

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "M-INV", "cliente.json");

    public static ClientSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(FilePath)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ClientSettings();
        }
    }

    public void Save()
    {
        if (IsReadOnly)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("M-INV · no se pudieron guardar las preferencias: {0}", ex.Message);
        }
    }
}
