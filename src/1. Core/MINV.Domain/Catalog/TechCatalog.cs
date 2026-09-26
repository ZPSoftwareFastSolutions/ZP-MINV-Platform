using System.Globalization;
using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>V4.2 · Tipo de dato de una especificación técnica. Se guarda como texto.</summary>
public enum SpecDataType
{
    /// <summary>Texto libre (modelo de gráficos, arquitectura…).</summary>
    Text,

    /// <summary>Número con unidad (núcleos, GB, W, mm, Hz…).</summary>
    Number,

    /// <summary>Una o varias opciones de una lista cerrada (socket, tipo de RAM, formato, plataforma…).</summary>
    Option,
}

/// <summary>V4.2 · Tipo de identificador por unidad de un producto serializado. Se guarda como texto.</summary>
public enum SerialKind
{
    /// <summary>Número de serie del fabricante.</summary>
    Serial,

    /// <summary>IMEI (equipos con módulo celular): va en el campo numeroImei de la factura del SIN.</summary>
    Imei,
}

/// <summary>
/// V4.2 · Claves de compatibilidad que usa el armador de PC (<see cref="PcCompatibility"/>). Una especificación puede
/// declarar una clave: así las reglas no dependen del código ni del nombre visible de la especificación.
/// </summary>
public static class CompatibilityKeys
{
    public const string CpuSocket = "cpu_socket";
    public const string RamType = "ram_type";
    public const string RamSlots = "ram_slots";
    public const string RamMaxGb = "ram_max_gb";
    public const string FormFactor = "form_factor";
    public const string CaseFormFactors = "case_form_factors";
    public const string GpuLengthMm = "gpu_length_mm";
    public const string CaseMaxGpuMm = "case_max_gpu_mm";
    public const string PowerDrawW = "power_draw_w";
    public const string CpuTdpW = "cpu_tdp_w";
    public const string PsuWatts = "psu_watts";
    public const string M2Slots = "m2_slots";
    public const string CoolerSockets = "cooler_sockets";
    public const string IntegratedGraphics = "igpu";
    public const string StorageInterface = "storage_interface";

    /// <summary>Del kit de memoria: cantidad de módulos y capacidad total (GB).</summary>
    public const string RamModules = "ram_modules";
    public const string RamCapacityGb = "ram_capacity_gb";

    public static readonly IReadOnlyList<string> All =
    [
        CpuSocket, RamType, RamSlots, RamMaxGb, FormFactor, CaseFormFactors, GpuLengthMm, CaseMaxGpuMm, PowerDrawW, CpuTdpW, PsuWatts,
        M2Slots, CoolerSockets, IntegratedGraphics, StorageInterface, RamModules, RamCapacityGb,
    ];
}

/// <summary>
/// V4.2 · Especificación técnica de una categoría (Socket, VRAM, Tipo de RAM, Plataforma…). Las categorías hijas heredan
/// las de sus madres. Las opciones de las de tipo opción están en <see cref="SpecOption"/>.
/// </summary>
public sealed class SpecDefinition : Entity
{
    private SpecDefinition()
    {
    }

    public SpecDefinition(Guid tenantId, Guid categoryId, string code, string name, string? unit, SpecDataType dataType, bool isMultiValued,
        bool isFilterable, bool isRequired, string? compatibilityKey, int sortOrder)
        : base(tenantId)
    {
        CategoryId = Guard.NotEmpty(categoryId, nameof(categoryId));
        Code = Guard.Text(code, "El código de la especificación", 40).ToLowerInvariant();
        Guard.That(Code.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'), "spec.code",
            "El código de la especificación solo admite minúsculas, dígitos y guion bajo.");
        DataType = Guard.Defined(dataType, "El tipo de la especificación");
        Guard.That(!isMultiValued || dataType == SpecDataType.Option, "spec.multi", "Solo una especificación de opciones admite varios valores.");
        IsMultiValued = isMultiValued;
        Update(name, unit, isFilterable, isRequired, compatibilityKey, sortOrder);
    }

    public Guid CategoryId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Unit { get; private set; }

    public SpecDataType DataType { get; private set; }

    public bool IsMultiValued { get; private set; }

    public bool IsFilterable { get; private set; }

    public bool IsRequired { get; private set; }

    /// <summary>Clave de <see cref="CompatibilityKeys"/> (null = no participa en el armador).</summary>
    public string? CompatibilityKey { get; private set; }

    public int SortOrder { get; private set; }

    public void Update(string name, string? unit, bool isFilterable, bool isRequired, string? compatibilityKey, int sortOrder)
    {
        Name = Guard.Text(name, "El nombre de la especificación", 80);
        Unit = Guard.OptionalText(unit, "La unidad", 20);
        IsFilterable = isFilterable;
        IsRequired = isRequired;
        Guard.That(compatibilityKey is null || CompatibilityKeys.All.Contains(compatibilityKey), "spec.compat",
            $"La clave de compatibilidad «{compatibilityKey}» no existe.");
        CompatibilityKey = compatibilityKey;
        SortOrder = sortOrder;
    }
}

/// <summary>V4.2 · Opción válida de una especificación de tipo opción (AM5, DDR5, ATX, PS5…).</summary>
public sealed class SpecOption : Entity
{
    private SpecOption()
    {
    }

    public SpecOption(Guid tenantId, Guid specDefinitionId, string value, int sortOrder)
        : base(tenantId)
    {
        SpecDefinitionId = Guard.NotEmpty(specDefinitionId, nameof(specDefinitionId));
        Value = Guard.Text(value, "La opción", 60);
        SortOrder = sortOrder;
    }

    public Guid SpecDefinitionId { get; private set; }

    public string Value { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    /// <summary>Nueva posición de la opción en la lista (el valor no cambia: lo usan los productos).</summary>
    public void Reorder(int sortOrder) => SortOrder = sortOrder;
}

/// <summary>
/// V4.2 · Valor de una especificación para un producto: número, texto u opción. Las especificaciones multivalor tienen
/// una fila por opción (p. ej. formatos que admite un gabinete).
/// </summary>
public sealed class ProductSpecValue : Entity
{
    private ProductSpecValue()
    {
    }

    private ProductSpecValue(Guid tenantId, Guid productId, Guid specDefinitionId)
        : base(tenantId)
    {
        ProductId = Guard.NotEmpty(productId, nameof(productId));
        SpecDefinitionId = Guard.NotEmpty(specDefinitionId, nameof(specDefinitionId));
    }

    public Guid ProductId { get; private set; }

    public Guid SpecDefinitionId { get; private set; }

    public decimal? NumberValue { get; private set; }

    public string? TextValue { get; private set; }

    public Guid? OptionId { get; private set; }

    public static ProductSpecValue Number(Guid tenantId, Guid productId, SpecDefinition spec, decimal value)
    {
        Guard.That(spec.DataType == SpecDataType.Number, "spec.type", $"{spec.Name} no es numérica.");
        return new ProductSpecValue(tenantId, productId, spec.Id) { NumberValue = value };
    }

    public static ProductSpecValue Text(Guid tenantId, Guid productId, SpecDefinition spec, string value)
    {
        Guard.That(spec.DataType == SpecDataType.Text, "spec.type", $"{spec.Name} no es de texto.");
        return new ProductSpecValue(tenantId, productId, spec.Id) { TextValue = Guard.Text(value, spec.Name, 200) };
    }

    public static ProductSpecValue Option(Guid tenantId, Guid productId, SpecDefinition spec, SpecOption option)
    {
        Guard.That(spec.DataType == SpecDataType.Option, "spec.type", $"{spec.Name} no es de opciones.");
        Guard.That(option.SpecDefinitionId == spec.Id, "spec.option", $"«{option.Value}» no es una opción de {spec.Name}.");
        return new ProductSpecValue(tenantId, productId, spec.Id) { OptionId = option.Id };
    }

    /// <summary>Valor como texto para mostrar (la opción se resuelve fuera).</summary>
    public string Display(string? optionValue, string? unit) => NumberValue is { } n
        ? n.ToString("0.##", CultureInfo.GetCultureInfo("es-BO")) + (unit is null ? string.Empty : " " + unit)
        : TextValue ?? optionValue ?? string.Empty;
}

/// <summary>
/// V4.2 · Perfil técnico 1:1 de un producto de tecnología: tipo de identificador por unidad (serie o IMEI) y meses de
/// garantía. Que el producto lleve serie lo dice <see cref="Product.TrackingMode"/> = <see cref="TrackingMode.Serial"/>.
/// </summary>
public sealed class ProductTechProfile : BaseEntity, IConcurrencyAware
{
    private ProductTechProfile()
    {
    }

    public ProductTechProfile(Guid tenantId, Guid productId, SerialKind serialKind, int warrantyMonths)
        : base(tenantId)
    {
        ProductId = Guard.NotEmpty(productId, nameof(productId));
        Update(serialKind, warrantyMonths);
    }

    public Guid ProductId { get; private set; }

    public SerialKind SerialKind { get; private set; }

    public int WarrantyMonths { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public void Update(SerialKind serialKind, int warrantyMonths)
    {
        SerialKind = Guard.Defined(serialKind, "El tipo de serie");
        Guard.That(warrantyMonths is >= 0 and <= 120, "tech.warranty", "La garantía va de 0 a 120 meses.");
        WarrantyMonths = warrantyMonths;
    }

    /// <summary>Fin de la garantía de una unidad vendida en <paramref name="soldOn"/>.</summary>
    public DateOnly WarrantyUntil(DateOnly soldOn) => soldOn.AddMonths(WarrantyMonths);
}
