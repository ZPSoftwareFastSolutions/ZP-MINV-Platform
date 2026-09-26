using System.Windows;
using Microsoft.Win32;
using MINV.DesktopClient.Controls;

namespace MINV.DesktopClient.Services;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Tema claro / oscuro / según Windows. Reemplaza la paleta (primer diccionario de la aplicación); como todas las vistas
/// usan DynamicResource, el cambio es inmediato y sin reiniciar. V4.2 · El tema oscuro (gaming) es el predeterminado
/// (regla T-08): se usa cuando no hay una preferencia guardada o la guardada no se reconoce.
/// </summary>
public sealed class ThemeService
{
    /// <summary>Tema de una instalación nueva.</summary>
    public const ThemeMode Default = ThemeMode.Dark;

    /// <summary>Nombre del tema predeterminado tal como se guarda en las preferencias.</summary>
    public const string DefaultName = "oscuro";

    private static readonly Uri LightUri = new("pack://application:,,,/Theme/Palette.Light.xaml");
    private static readonly Uri DarkUri = new("pack://application:,,,/Theme/Palette.Dark.xaml");
    private readonly ClientSettings _settings;

    public ThemeService(ClientSettings settings)
    {
        _settings = settings;
        Mode = Parse(settings.Theme);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Mode == ThemeMode.System)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => Apply(ThemeMode.System, save: false));
            }
        };
    }

    public ThemeMode Mode { get; private set; }

    public bool IsDark { get; private set; }

    public event EventHandler? Changed;

    public static ThemeMode Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "claro" or "light" => ThemeMode.Light,
        "oscuro" or "dark" => ThemeMode.Dark,
        "sistema" or "system" => ThemeMode.System,
        _ => Default,
    };

    public static string Name(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "claro",
        ThemeMode.Dark => "oscuro",
        _ => "sistema",
    };

    public void Apply(ThemeMode mode, bool save = true)
    {
        Mode = mode;
        IsDark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemPrefersDark());
        var resources = System.Windows.Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = IsDark ? DarkUri : LightUri };
        if (resources.Count > 0)
        {
            resources[0] = palette;
        }
        else
        {
            resources.Add(palette);
        }
        if (save)
        {
            _settings.Theme = Name(mode);
            _settings.Save();
        }
        ThemeEvents.Raise();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Tema de las aplicaciones de Windows (Configuración › Personalización › Colores).</summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
