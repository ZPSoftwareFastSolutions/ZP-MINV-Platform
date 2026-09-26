using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using MINV.DesktopClient.Services;

namespace MINV.DesktopClient.Tests;

/// <summary>
/// V4.2 · Tema gaming (regla T-08 y A-09): el oscuro es el predeterminado, claro y oscuro tienen las MISMAS claves, los
/// textos cumplen el contraste WCAG AA y las vistas solo usan recursos que existen (un DynamicResource que no está en la
/// paleta deja el elemento transparente sin avisar).
/// </summary>
public sealed class ThemeTests
{
    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string ClientFolder = Path.Combine(RepositoryRoot(), "src", "3. Presentation", "MINV.DesktopClient");

    [Fact]
    public void V42_el_tema_oscuro_es_el_predeterminado_y_la_preferencia_guardada_manda()
    {
        Assert.Equal(ThemeMode.Dark, ThemeService.Default);
        Assert.Equal(ThemeMode.Dark, ThemeService.Parse(new ClientSettings().Theme));   // instalación nueva
        Assert.Equal(ThemeMode.Dark, ThemeService.Parse(JsonSerializer.Deserialize<ClientSettings>("""{"TenantCode":"TECHZONE"}""")!.Theme));
        Assert.Equal(ThemeMode.Dark, ThemeService.Parse(null));
        Assert.Equal(ThemeMode.Dark, ThemeService.Parse("desconocido"));
        // La preferencia guardada del usuario manda
        Assert.Equal(ThemeMode.Light, ThemeService.Parse(JsonSerializer.Deserialize<ClientSettings>("""{"Theme":"claro"}""")!.Theme));
        Assert.Equal(ThemeMode.System, ThemeService.Parse(JsonSerializer.Deserialize<ClientSettings>("""{"Theme":"sistema"}""")!.Theme));
        Assert.Equal(ThemeMode.Dark, ThemeService.Parse("oscuro"));
        Assert.Equal(ThemeService.DefaultName, ThemeService.Name(ThemeService.Default));

        // Preferencias de la V4.1: «sistema» era el valor por defecto guardado solo → pasa UNA vez a oscuro
        var previous = JsonSerializer.Deserialize<ClientSettings>("""{"Theme":"sistema"}""")!;
        previous.ApplyEditionDefaults();
        Assert.Equal(ThemeMode.Dark, ThemeService.Parse(previous.Theme));
        var chosen = JsonSerializer.Deserialize<ClientSettings>("""{"Theme":"sistema","ThemeEdition":42}""")!;
        chosen.ApplyEditionDefaults();
        Assert.Equal(ThemeMode.System, ThemeService.Parse(chosen.Theme));   // elegido en la V4.2: se respeta
        var light = JsonSerializer.Deserialize<ClientSettings>("""{"Theme":"claro"}""")!;
        light.ApplyEditionDefaults();
        Assert.Equal(ThemeMode.Light, ThemeService.Parse(light.Theme));
    }

    [Fact]
    public void V42_claro_y_oscuro_tienen_las_mismas_claves_en_el_mismo_orden_y_del_mismo_tipo()
    {
        var dark = ReadPalette("Palette.Dark.xaml");
        var light = ReadPalette("Palette.Light.xaml");
        Assert.Equal(PaletteKeys("Palette.Dark.xaml"), PaletteKeys("Palette.Light.xaml"));   // mismas claves y en el mismo orden
        Assert.All(dark.Keys, k => Assert.Equal(dark[k].Type, light[k].Type));
        // Claves del sistema visual que el escritorio necesita (V3.1 + acentos gaming de la V4.2)
        Assert.Subset(dark.Keys.ToHashSet(),
            new HashSet<string> { "Canvas", "Surface", "TextPrimary", "Brand", "OnBrand", "BrandGradient", "BrandGradientStrip", "BrandGradientButton",
                "BrandGradientButtonHover", "FocusRingBrush", "Sidebar", "ChartEntries", "ChartIssues", "BloodRed" });
    }

    [Fact]
    public void V42_las_paletas_se_cargan_como_recursos_de_WPF() => Wpf.Run(() =>
    {
        foreach (var file in new[] { "Palette.Dark.xaml", "Palette.Light.xaml" })
        {
            var dictionary = (ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(ClientFolder, "Theme", file)));
            var keys = dictionary.Keys.Cast<string>().ToHashSet();
            Assert.Equal(ReadPalette(file).Keys.ToHashSet(), keys);
            Assert.All(keys, k => Assert.True(dictionary[k] is Brush or Color, $"{file} · {k}"));
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("Palette.Dark.xaml")]
    [InlineData("Palette.Light.xaml")]
    public void V42_los_textos_cumplen_contraste_WCAG_AA_y_el_foco_se_ve(string file)
    {
        var p = ReadPalette(file);
        var failures = new List<string>();
        void Check(string text, string background, double minimum)
        {
            var ratio = Worst(p, text, background);
            if (ratio < minimum)
            {
                failures.Add($"{text} sobre {background}: {ratio:0.00}:1 (mín. {minimum})");
            }
        }
        foreach (var background in new[] { "Canvas", "Surface", "SurfaceAlt", "SurfaceHover", "InputBackground" })
        {
            foreach (var text in new[] { "TextPrimary", "TextSecondary", "TextMuted" })
            {
                Check(text, background, 4.5);
            }
            Check("FocusRingBrush", background, 3);   // foco visible (componente, WCAG 1.4.11)
        }
        foreach (var background in new[] { "Brand", "BrandHover", "BrandPressed", "SidebarSelected", "BrandGradientButton", "BrandGradientButtonHover" })
        {
            Check("OnBrand", background, 4.5);        // botón principal, menú activo, casillas
        }
        foreach (var background in new[] { "BrandSoft", "BrandSoftHover", "Surface" })
        {
            Check("BrandText", background, 4.5);
        }
        Check("SidebarText", "Sidebar", 4.5);         // menú y ayudas emergentes
        Check("SidebarText", "SidebarHover", 4.5);
        Check("SidebarMuted", "Sidebar", 4.5);
        Check("#FFFFFF", "BloodRed", 4.5);             // poka-yoke de la V2.1
        Check("Canvas", "Danger", 4.5);                // botón de peligro y contadores
        Check("Canvas", "DangerHover", 4.5);
        foreach (var state in new[] { "Success", "Warning", "Danger", "Info" })
        {
            Check(state, state + "Soft", 4.5);
            Check(state, "Surface", 4.5);
        }
        foreach (var status in new[] { "OutOfStock", "Critical", "Low", "Optimal", "Overstock", "Inconsistent", "Inactive" })
        {
            Check("Status" + status, "Status" + status + "Soft", 4.5);
            Check("Status" + status, "Surface", 4.5);
        }
        foreach (var series in new[] { "ChartEntries", "ChartIssues", "Brand" })
        {
            Check(series, "Surface", 3);               // series de los gráficos y marcas de las casillas
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void V42_las_vistas_solo_usan_colores_de_la_paleta_y_recursos_que_existen()
    {
        var palette = ReadPalette("Palette.Dark.xaml").Keys.ToHashSet();
        var files = Directory.EnumerateFiles(ClientFolder, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetRelativePath(ClientFolder, f), f => Regex.Replace(File.ReadAllText(f), "<!--.*?-->", "", RegexOptions.Singleline));
        Assert.Contains(Path.Combine("Theme", "Controls.xaml"), files.Keys);
        var defined = files.Values.SelectMany(t => Regex.Matches(t, "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value)).ToHashSet();
        var problems = new List<string>();
        foreach (var (name, text) in files)
        {
            foreach (Match m in Regex.Matches(text, @"\{(Dynamic|Static)Resource\s+([A-Za-z_][\w.]*)\s*\}"))
            {
                var (kind, key) = (m.Groups[1].Value, m.Groups[2].Value);
                if (kind == "Dynamic" && !palette.Contains(key))
                {
                    problems.Add($"{name}: DynamicResource {key} no está en la paleta");
                }
                else if (kind == "Static" && !defined.Contains(key))
                {
                    problems.Add($"{name}: StaticResource {key} no existe");
                }
            }
        }
        Assert.Empty(problems.Distinct());
    }

    /// <summary>
    /// Un StaticResource que existe pero está FUERA DE ALCANCE (definido en los recursos de otro elemento de la vista) compila
    /// y pasa la prueba anterior, pero la aplicación se cae al dibujar la pantalla (así se cayó el Inicio con la plantilla
    /// TechBar). Las pruebas de las pantallas no dibujan las vistas: esta recorre cada XAML como lo resuelve WPF (recursos de
    /// la aplicación + recursos de los elementos ancestros definidos ANTES del uso).
    /// </summary>
    [Fact]
    public void V42_los_StaticResource_de_las_vistas_estan_al_alcance()
    {
        var app = XDocument.Load(Path.Combine(ClientFolder, "App.xaml"));
        var dictionaries = app.Descendants(XName.Get("ResourceDictionary", Presentation)).Select(d => (string?)d.Attribute("Source"))
            .Where(s => s is not null).Select(s => Path.Combine(s!.Split('/'))).ToList();
        Assert.Contains(Path.Combine("Views", "TechForms.xaml"), dictionaries);
        var global = dictionaries
            .SelectMany(d => XDocument.Load(Path.Combine(ClientFolder, d)).Root!.Elements().Select(e => (string?)e.Attribute(XName.Get("Key", Xaml))))
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        var views = Directory.EnumerateFiles(Path.Combine(ClientFolder, "Views"), "*.xaml", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(ClientFolder, f)).Where(f => !dictionaries.Contains(f)).ToList();
        Assert.Contains(Path.Combine("Views", "Pages", "DashboardView.xaml"), views);
        foreach (var view in views)
        {
            var root = XDocument.Load(Path.Combine(ClientFolder, view), LoadOptions.SetLineInfo).Root!;
            CheckScope(root, [], view, global, problems);
        }
        Assert.Empty(problems);
    }

    private static void CheckScope(XElement element, List<HashSet<string>> scopes, string file, HashSet<string> global, List<string> problems)
    {
        foreach (var attribute in element.Attributes())
        {
            foreach (Match m in Regex.Matches(attribute.Value, @"\{StaticResource\s+(?:ResourceKey=)?([A-Za-z_][\w.]*)\s*\}"))
            {
                var key = m.Groups[1].Value;
                if (!global.Contains(key) && !scopes.Any(s => s.Contains(key)))
                {
                    problems.Add($"{file}:{((System.Xml.IXmlLineInfo)element).LineNumber} · StaticResource {key} fuera de alcance");
                }
            }
        }
        var own = new HashSet<string>(StringComparer.Ordinal);
        scopes.Add(own);
        foreach (var child in element.Elements())
        {
            if (!child.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal))
            {
                CheckScope(child, scopes, file, global, problems);
                continue;
            }
            // Recursos del elemento: cada uno ve los definidos antes que él (y los de sus ancestros)
            var entries = child.Elements().ToList();
            if (entries is [{ Name.LocalName: "ResourceDictionary" } dictionary] && dictionary.Attribute(XName.Get("Key", Xaml)) is null)
            {
                entries = dictionary.Elements().Where(e => !e.Name.LocalName.Contains('.', StringComparison.Ordinal)).ToList();
            }
            foreach (var entry in entries)
            {
                CheckScope(entry, scopes, file, global, problems);
                if ((string?)entry.Attribute(XName.Get("Key", Xaml)) is { } key)
                {
                    own.Add(key);
                }
            }
        }
        scopes.RemoveAt(scopes.Count - 1);
    }

    [Fact]
    public void V42_la_marca_de_la_edicion_Tecnologia_esta_en_la_carga_el_inicio_de_sesion_y_la_ayuda()
    {
        Assert.Equal("Edición Tecnología · PC, componentes y consolas", Brand.Edition);
        foreach (var view in new[] { "SplashWindow.xaml", "LoginWindow.xaml", Path.Combine("Pages", "HelpView.xaml"), Path.Combine("Pages", "SettingsView.xaml") })
        {
            Assert.Contains("s:Brand.Edition", File.ReadAllText(Path.Combine(ClientFolder, "Views", view)), StringComparison.Ordinal);
        }
        foreach (var view in new[] { "SplashWindow.xaml", "LoginWindow.xaml" })
        {
            Assert.Contains("/Assets/minv-logotipo.png", File.ReadAllText(Path.Combine(ClientFolder, "Views", view)), StringComparison.Ordinal);
        }
        var project = File.ReadAllText(Path.Combine(ClientFolder, "MINV.DesktopClient.csproj"));
        Assert.Contains(@"<ApplicationIcon>Assets\minv.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains(@"<Resource Include=""Assets\minv-logotipo.png"" />", project, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------- utilidades
    private sealed record Entry(string Type, IReadOnlyList<string> Colors);

    /// <summary>Claves de una paleta en el orden del archivo.</summary>
    private static List<string> PaletteKeys(string file) =>
        XDocument.Load(Path.Combine(ClientFolder, "Theme", file)).Root!.Elements().Select(e => (string)e.Attribute(XName.Get("Key", Xaml))!).ToList();

    /// <summary>Claves de una paleta con su tipo y sus colores (#AARRGGBB o #RRGGBB).</summary>
    private static Dictionary<string, Entry> ReadPalette(string file)
    {
        var root = XDocument.Load(Path.Combine(ClientFolder, "Theme", file)).Root!;
        var result = new Dictionary<string, Entry>();
        foreach (var element in root.Elements())
        {
            var key = (string?)element.Attribute(XName.Get("Key", Xaml)) ?? throw new InvalidDataException($"{file}: recurso sin x:Key");
            var colors = element.Name.LocalName switch
            {
                "SolidColorBrush" => [(string)element.Attribute("Color")!],
                "Color" => [element.Value.Trim()],
                "LinearGradientBrush" => element.Elements(XName.Get("GradientStop", Presentation)).Select(s => (string)s.Attribute("Color")!).ToList(),
                _ => new List<string>(),
            };
            result.Add(key, new Entry(element.Name.LocalName, colors));
        }
        return result;
    }

    /// <summary>Contraste más bajo entre un texto y un fondo (en un degradado, el peor punto del recorrido).</summary>
    private static double Worst(Dictionary<string, Entry> p, string text, string background)
    {
        var fg = text.StartsWith('#') ? Rgb(text) : Rgb(p[text].Colors.Single());
        var stops = p[background].Colors.Select(Rgb).ToList();
        var samples = stops.Count == 1
            ? stops
            : Enumerable.Range(0, 101).Select(i => Mix(stops, i / 100.0)).ToList();
        return samples.Min(bg => Contrast(fg, bg));
    }

    private static (double R, double G, double B) Mix(IReadOnlyList<(double R, double G, double B)> stops, double t)
    {
        // Paradas equidistantes (así están los degradados de la paleta: 0 y 1)
        var position = t * (stops.Count - 1);
        var i = Math.Min((int)position, stops.Count - 2);
        var f = position - i;
        var (a, b) = (stops[i], stops[i + 1]);
        return (a.R + (b.R - a.R) * f, a.G + (b.G - a.G) * f, a.B + (b.B - a.B) * f);
    }

    private static (double R, double G, double B) Rgb(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8)
        {
            Assert.Equal("FF", h[..2]);   // los colores de texto y fondo son opacos
            h = h[2..];
        }
        return (Channel(h, 0), Channel(h, 2), Channel(h, 4));

        static double Channel(string h, int i) => int.Parse(h.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static double Contrast((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance((double R, double G, double B) c)
    {
        static double Linear(double v)
        {
            v /= 255;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MINV.sln")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio (MINV.sln).");
    }
}
