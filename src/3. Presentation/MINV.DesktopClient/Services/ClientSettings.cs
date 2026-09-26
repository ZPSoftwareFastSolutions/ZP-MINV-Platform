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

    /// <summary>V4 · «local» (PostgreSQL directo) o «nube» (servidor M-INV).</summary>
    public string ConnectionMode { get; set; } = "local";

    /// <summary>V4 · Dirección del servidor M-INV en la nube (https).</summary>
    public string? ServerUrl { get; set; }

    public bool Remember { get; set; } = true;

    /// <summary>
    /// «sistema», «claro» u «oscuro». V4.2 · Edición Tecnología: una instalación nueva empieza en el tema oscuro; si el
    /// usuario ya eligió uno (está guardado en cliente.json), manda su preferencia.
    /// </summary>
    public string Theme { get; set; } = ThemeService.DefaultName;

    /// <summary>
    /// V4.2 · Edición de las preferencias de tema. Hasta la V4.1 «sistema» era el valor por defecto que se guardaba solo, no
    /// una elección del usuario: al abrir la V4.2 por primera vez ese valor pasa UNA vez al tema oscuro de la edición
    /// Tecnología (ver <see cref="ApplyEditionDefaults"/>). «claro» u «oscuro» elegidos antes se respetan.
    /// </summary>
    public int ThemeEdition { get; set; }

    /// <summary>Edición actual de las preferencias de tema (4.2).</summary>
    public const int CurrentThemeEdition = 42;

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
            var settings = File.Exists(FilePath) ? JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(FilePath)) ?? new() : new();
            settings.ApplyEditionDefaults();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ClientSettings { ThemeEdition = CurrentThemeEdition };
        }
    }

    /// <summary>Pasa UNA vez al tema oscuro las preferencias guardadas antes de la V4.2 con el «sistema» por defecto.</summary>
    public void ApplyEditionDefaults()
    {
        if (ThemeEdition >= CurrentThemeEdition)
        {
            return;
        }
        if (string.Equals(Theme, "sistema", StringComparison.OrdinalIgnoreCase))
        {
            Theme = ThemeService.DefaultName;
        }
        ThemeEdition = CurrentThemeEdition;
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
