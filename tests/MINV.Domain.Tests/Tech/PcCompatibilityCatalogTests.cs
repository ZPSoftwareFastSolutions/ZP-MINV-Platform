using System.Globalization;
using System.Text.Json;
using MINV.Domain.Catalog;

namespace MINV.Domain.Tests.Tech;

/// <summary>
/// V4.2 · <see cref="PcCompatibility"/> con los armados del catálogo de prueba (<c>catalogo-tecnologia.json</c>, sección
/// <c>armados</c>): las piezas se arman como lo hará el caso de uso, con las especificaciones del producto que tienen clave
/// de compatibilidad (heredadas por categoría). Los 6 compatibles no tienen errores ni avisos y los 2 de prueba tienen
/// exactamente los errores y avisos que calculó el generador del catálogo.
/// </summary>
public sealed class PcCompatibilityCatalogTests
{
    private static readonly Lazy<JsonDocument> Catalog = new(() => JsonDocument.Parse(File.ReadAllText(CatalogPath())));

    private static readonly Dictionary<string, PcSlot> Slots = new(StringComparer.Ordinal)
    {
        ["cpu"] = PcSlot.Cpu, ["placa"] = PcSlot.Motherboard, ["ram"] = PcSlot.Ram, ["gpu"] = PcSlot.Gpu,
        ["almacenamiento"] = PcSlot.Storage, ["fuente"] = PcSlot.Psu, ["gabinete"] = PcSlot.Case, ["refrigeracion"] = PcSlot.Cooler,
        ["extra"] = PcSlot.Peripheral,
    };

    public static IEnumerable<object[]> Builds() =>
        Catalog.Value.RootElement.GetProperty("armados").EnumerateArray().Select(a => new object[] { a.GetProperty("numero").GetString()! });

    [Fact]
    public void El_catalogo_tiene_6_armados_compatibles_y_2_incompatibles()
    {
        var builds = Catalog.Value.RootElement.GetProperty("armados").EnumerateArray().ToList();
        Assert.Equal(8, builds.Count);
        Assert.Equal(6, builds.Count(b => Check(b).IsCompatible));
        Assert.Equal(2, builds.Count(b => !Check(b).IsCompatible));
        // Toda clave de compatibilidad del catálogo es una constante del dominio (regla T-01)
        var keys = Catalog.Value.RootElement.GetProperty("especificaciones").EnumerateArray()
            .Select(s => s.GetProperty("clave_compatibilidad"))
            .Where(k => k.ValueKind == JsonValueKind.String).Select(k => k.GetString()!).Distinct().ToList();
        Assert.All(keys, k => Assert.Contains(k, CompatibilityKeys.All));
        Assert.Contains(CompatibilityKeys.RamModules, keys);
        Assert.Contains(CompatibilityKeys.RamCapacityGb, keys);
    }

    [Theory]
    [MemberData(nameof(Builds))]
    public void Cada_armado_del_catalogo_da_los_errores_y_avisos_esperados(string number)
    {
        var build = Catalog.Value.RootElement.GetProperty("armados").EnumerateArray().Single(a => a.GetProperty("numero").GetString() == number);
        var expected = build.GetProperty("compatibilidad");
        var report = Check(build);
        Assert.Equal(expected.GetProperty("compatible").GetBoolean(), report.IsCompatible);
        Assert.Equal(Codes(expected.GetProperty("errores")), report.Issues.Where(i => i.IsError).Select(i => i.Code).Order());
        Assert.Equal(Codes(expected.GetProperty("avisos")), report.Issues.Where(i => !i.IsError).Select(i => i.Code).Order());
        Assert.Equal(expected.GetProperty("consumo_estimado_w").GetInt32(), report.EstimatedDrawW);
        Assert.Equal(expected.GetProperty("potencia_fuente_w").GetInt32(), report.PsuW);
        Assert.True(report.RecommendedPsuW >= report.EstimatedDrawW * PcCompatibility.RecommendedFactor);
        Assert.Equal(build.GetProperty("marcado_incompatible").GetBoolean(), !report.IsCompatible);
    }

    [Fact]
    public void Los_incompatibles_de_prueba_fallan_por_las_reglas_documentadas()
    {
        var builds = Catalog.Value.RootElement.GetProperty("armados").EnumerateArray().ToDictionary(a => a.GetProperty("numero").GetString()!);
        var socket = Check(builds["ARM-CM-000004"]);
        Assert.Equal(new[] { "SOCKET_CPU_PLACA", "TIPO_RAM" }, socket.Issues.Where(i => i.IsError).Select(i => i.Code));
        Assert.Equal(new[] { "PIEZA_FALTANTE", "SIN_GRAFICOS" }, socket.Issues.Where(i => !i.IsError).Select(i => i.Code).Order());
        var everything = Check(builds["ARM-CM-000005"]);
        Assert.Equal(new[] { "CAPACIDAD_RAM", "FORMATO_GABINETE", "LARGO_GPU", "POTENCIA_FUENTE", "RANURAS_M2", "RANURAS_RAM", "SOCKET_REFRIGERACION" },
            everything.Issues.Select(i => i.Code).Order());
        Assert.Contains(everything.Issues, i => i.Code == "RANURAS_RAM" && i.Message.Contains("8 módulos", StringComparison.Ordinal));
        Assert.Contains(everything.Issues, i => i.Code == "CAPACIDAD_RAM" && i.Message.Contains("256 GB", StringComparison.Ordinal));
    }

    private static IEnumerable<string> Codes(JsonElement issues) =>
        issues.EnumerateArray().Select(i => i.GetProperty("regla").GetString()!).Order();

    private static PcCompatibilityReport Check(JsonElement build) =>
        PcCompatibility.Check(build.GetProperty("lineas").EnumerateArray()
            .Select(l => Component(Slots[l.GetProperty("ranura").GetString()!], l.GetProperty("sku").GetString()!, l.GetProperty("cantidad").GetInt32()))
            .ToList());

    /// <summary>Pieza del armado: las especificaciones del producto con clave de compatibilidad, de su categoría o de sus
    /// categorías madre (como las heredará la ficha técnica).</summary>
    private static PcComponent Component(PcSlot slot, string sku, int quantity)
    {
        var root = Catalog.Value.RootElement;
        var product = root.GetProperty("productos").EnumerateArray().Single(p => p.GetProperty("sku").GetString() == sku);
        var parents = root.GetProperty("categorias").EnumerateArray()
            .ToDictionary(c => c.GetProperty("codigo").GetString()!, c => c.GetProperty("padre").ValueKind == JsonValueKind.String
                ? c.GetProperty("padre").GetString()
                : null);
        var lineage = new List<string>();
        for (var c = product.GetProperty("categoria").GetString(); c is not null; c = parents[c])
        {
            lineage.Add(c);
        }
        var values = product.GetProperty("especificaciones");
        var specs = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var spec in root.GetProperty("especificaciones").EnumerateArray()
                     .Where(s => lineage.Contains(s.GetProperty("categoria").GetString()!) && s.GetProperty("clave_compatibilidad").ValueKind == JsonValueKind.String))
        {
            if (values.TryGetProperty(spec.GetProperty("codigo").GetString()!, out var value))
            {
                specs[spec.GetProperty("clave_compatibilidad").GetString()!] = value.ValueKind == JsonValueKind.Array
                    ? value.EnumerateArray().Select(Text).ToList()
                    : [Text(value)];
            }
        }
        return new PcComponent(slot, sku, product.GetProperty("nombre").GetString()!, quantity, specs);
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.Number
        ? value.GetDecimal().ToString(CultureInfo.InvariantCulture)
        : value.GetString()!;

    private static string CatalogPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MINV.sln")))
            {
                return Path.Combine(dir.FullName, "src", "2. Infrastructure", "MINV.Infrastructure", "Seeding", "Tecnologia", "catalogo-tecnologia.json");
            }
        }
        throw new InvalidOperationException("No se encontró la raíz del repositorio (MINV.sln).");
    }
}
