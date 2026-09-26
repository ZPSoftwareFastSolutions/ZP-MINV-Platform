using System.Globalization;

namespace MINV.Domain.Catalog;

/// <summary>V4.2 · Ranura del armador de PC. Se guarda como texto.</summary>
public enum PcSlot
{
    Cpu,
    Motherboard,
    Ram,
    Gpu,
    Storage,
    Psu,
    Case,
    Cooler,
    Monitor,
    Peripheral,
    Software,
    Service,
}

/// <summary>V4.2 · Pieza del armado: ranura, producto, cantidad y sus especificaciones por clave de compatibilidad
/// (valores como texto; los números con punto decimal).</summary>
public sealed record PcComponent(PcSlot Slot, string Sku, string Name, int Quantity, IReadOnlyDictionary<string, IReadOnlyList<string>> Specs)
{
    public IReadOnlyList<string> Values(string key) => Specs.TryGetValue(key, out var v) ? v : [];

    public string? Value(string key) => Values(key).FirstOrDefault();

    public decimal? Number(string key) =>
        decimal.TryParse(Value(key), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;
}

/// <summary>V4.2 · Resultado de una regla de compatibilidad.</summary>
public sealed record PcIssue(string Code, bool IsError, string Message);

/// <summary>V4.2 · Informe del armador: problemas, consumo estimado y fuente recomendada.</summary>
public sealed record PcCompatibilityReport(IReadOnlyList<PcIssue> Issues, int EstimatedDrawW, int RecommendedPsuW, int? PsuW)
{
    public bool IsCompatible => Issues.All(i => !i.IsError);

    public int Errors => Issues.Count(i => i.IsError);

    public int Warnings => Issues.Count(i => !i.IsError);
}

/// <summary>
/// V4.2 · Reglas de compatibilidad del armador de PC (dominio puro; las mismas del catálogo de prueba,
/// <c>generar_catalogo.py</c>): socket CPU = placa; tipo de RAM = placa; módulos ≤ ranuras y capacidad ≤ máxima;
/// formato de placa admitido por el gabinete; largo de GPU ≤ máximo del gabinete; fuente ≥ consumo (TDP CPU + GPU +
/// 75 W) y aviso si &lt; consumo × 1,3; unidades NVMe ≤ ranuras M.2; enfriador compatible con el socket; aviso si la CPU
/// no tiene gráficos y no hay GPU; aviso por pieza obligatoria faltante.
/// </summary>
public static class PcCompatibility
{
    public const int BaseDrawW = 75;
    public const decimal RecommendedFactor = 1.3m;

    public static readonly IReadOnlyList<PcSlot> Required =
        [PcSlot.Cpu, PcSlot.Motherboard, PcSlot.Ram, PcSlot.Storage, PcSlot.Psu, PcSlot.Case];

    public static PcCompatibilityReport Check(IReadOnlyList<PcComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var issues = new List<PcIssue>();
        PcComponent? One(PcSlot slot) => components.FirstOrDefault(c => c.Slot == slot);
        IEnumerable<PcComponent> All(PcSlot slot) => components.Where(c => c.Slot == slot);
        var cpu = One(PcSlot.Cpu);
        var board = One(PcSlot.Motherboard);
        var box = One(PcSlot.Case);
        var psu = One(PcSlot.Psu);
        var cooler = One(PcSlot.Cooler);
        var rams = All(PcSlot.Ram).ToList();
        var gpus = All(PcSlot.Gpu).ToList();
        var storages = All(PcSlot.Storage).ToList();

        foreach (var slot in Required.Where(s => components.All(c => c.Slot != s)))
        {
            issues.Add(new PcIssue("PIEZA_FALTANTE", false, $"Falta {SlotName(slot)}."));
        }
        if (cpu is not null && board is not null && !Same(cpu.Value(CompatibilityKeys.CpuSocket), board.Value(CompatibilityKeys.CpuSocket)))
        {
            issues.Add(new PcIssue("SOCKET_CPU_PLACA", true,
                $"El procesador es {cpu.Value(CompatibilityKeys.CpuSocket)} y la placa {board.Value(CompatibilityKeys.CpuSocket)}: el socket no coincide."));
        }
        if (board is not null)
        {
            foreach (var ram in rams.Where(r => !Same(r.Value(CompatibilityKeys.RamType), board.Value(CompatibilityKeys.RamType))))
            {
                issues.Add(new PcIssue("TIPO_RAM", true,
                    $"{ram.Name} es {ram.Value(CompatibilityKeys.RamType)} y la placa admite {board.Value(CompatibilityKeys.RamType)}."));
            }
            var modules = rams.Sum(r => (int)(r.Number(CompatibilityKeys.RamModules) ?? 1) * r.Quantity);
            if (board.Number(CompatibilityKeys.RamSlots) is { } slots && modules > slots)
            {
                issues.Add(new PcIssue("RANURAS_RAM", true, $"Son {modules} módulos de RAM y la placa tiene {slots:0} ranuras."));
            }
            var capacity = rams.Sum(r => (r.Number(CompatibilityKeys.RamCapacityGb) ?? 0) * r.Quantity);
            if (board.Number(CompatibilityKeys.RamMaxGb) is { } max && capacity > max)
            {
                issues.Add(new PcIssue("CAPACIDAD_RAM", true, $"Son {capacity:0} GB de RAM y la placa admite hasta {max:0} GB."));
            }
            var nvme = storages.Where(s => s.Value(CompatibilityKeys.StorageInterface)?.StartsWith("NVMe", StringComparison.OrdinalIgnoreCase) == true)
                .Sum(s => s.Quantity);
            if (board.Number(CompatibilityKeys.M2Slots) is { } m2 && nvme > m2)
            {
                issues.Add(new PcIssue("RANURAS_M2", true, $"Son {nvme} unidades NVMe y la placa tiene {m2:0} ranuras M.2."));
            }
            if (box is not null && board.Value(CompatibilityKeys.FormFactor) is { } form
                                && !box.Values(CompatibilityKeys.CaseFormFactors).Any(f => Same(f, form)))
            {
                issues.Add(new PcIssue("FORMATO_GABINETE", true, $"El gabinete no admite placas {form}."));
            }
        }
        if (box?.Number(CompatibilityKeys.CaseMaxGpuMm) is { } maxLength)
        {
            foreach (var gpu in gpus.Where(g => g.Number(CompatibilityKeys.GpuLengthMm) is { } l && l > maxLength))
            {
                issues.Add(new PcIssue("LARGO_GPU", true,
                    $"{gpu.Name} mide {gpu.Number(CompatibilityKeys.GpuLengthMm):0} mm y el gabinete admite hasta {maxLength:0} mm."));
            }
        }
        if (cpu is not null && cooler is not null && cooler.Values(CompatibilityKeys.CoolerSockets).Count > 0
            && !cooler.Values(CompatibilityKeys.CoolerSockets).Any(s => Same(s, cpu.Value(CompatibilityKeys.CpuSocket))))
        {
            issues.Add(new PcIssue("SOCKET_REFRIGERACION", true,
                $"{cooler.Name} no es compatible con el socket {cpu.Value(CompatibilityKeys.CpuSocket)}."));
        }
        if (cpu is not null && gpus.Count == 0 && IsNo(cpu.Value(CompatibilityKeys.IntegratedGraphics)))
        {
            issues.Add(new PcIssue("SIN_GRAFICOS", false, "El procesador no tiene gráficos integrados y el armado no lleva tarjeta de video."));
        }
        var draw = (int)((cpu?.Number(CompatibilityKeys.CpuTdpW) ?? 0) + gpus.Sum(g => (g.Number(CompatibilityKeys.PowerDrawW) ?? 0) * g.Quantity)
                         + BaseDrawW);
        var recommended = (int)(Math.Ceiling(draw * RecommendedFactor / 50m) * 50);
        var psuW = psu?.Number(CompatibilityKeys.PsuWatts) is { } w ? (int)w : (int?)null;
        if (psuW is { } watts)
        {
            if (watts < draw)
            {
                issues.Add(new PcIssue("POTENCIA_FUENTE", true, $"La fuente es de {watts} W y el consumo estimado es {draw} W."));
            }
            else if (watts < draw * RecommendedFactor)
            {
                issues.Add(new PcIssue("POTENCIA_RECOMENDADA", false, $"La fuente de {watts} W queda justa: se recomienda {recommended} W o más."));
            }
        }
        return new PcCompatibilityReport(issues, draw, recommended, psuW);
    }

    public static string SlotName(PcSlot slot) => slot switch
    {
        PcSlot.Cpu => "el procesador",
        PcSlot.Motherboard => "la placa madre",
        PcSlot.Ram => "la memoria RAM",
        PcSlot.Gpu => "la tarjeta de video",
        PcSlot.Storage => "el almacenamiento",
        PcSlot.Psu => "la fuente de poder",
        PcSlot.Case => "el gabinete",
        PcSlot.Cooler => "la refrigeración",
        _ => slot.ToString(),
    };

    private static bool Same(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsNo(string? value) => value is not null && (value.Equals("No", StringComparison.OrdinalIgnoreCase) || value == "false");
}
