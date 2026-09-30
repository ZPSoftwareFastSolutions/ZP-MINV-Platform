using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using MINV.Application.Tech;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Service;

namespace MINV.DesktopClient.ViewModels;

// =====================================================================================================================
// V4.2 · Piezas compartidas de las pantallas de la edición Tecnología: textos y colores de estados (series, RMA, armados,
// ranuras), catálogo técnico por SKU (serie/IMEI, garantía, plataformas) y el formulario de captura de series e IMEI
// (escáner en modo teclado, pegar una lista, contador «3 de 5», validación de IMEI y duplicados en vivo como GUÍA: la
// validación que manda es la del dominio al guardar).
// =====================================================================================================================

/// <summary>V4.2 · Textos visibles y claves de la paleta de la edición Tecnología (nunca colores fijos, regla T-08).</summary>
public static class TechText
{
    /// <summary>
    /// Nombre en español de una clave de compatibilidad del armador (las claves son constantes del dominio,
    /// <see cref="CompatibilityKeys"/>; aquí solo se traducen para mostrarlas). Una clave nueva sin traducir se muestra tal cual.
    /// </summary>
    public static string CompatibilityKey(string key) => key switch
    {
        CompatibilityKeys.CpuSocket => "Socket del procesador",
        CompatibilityKeys.RamType => "Tipo de RAM",
        CompatibilityKeys.RamSlots => "Ranuras de RAM de la placa",
        CompatibilityKeys.RamMaxGb => "RAM máxima de la placa (GB)",
        CompatibilityKeys.FormFactor => "Formato de la placa",
        CompatibilityKeys.CaseFormFactors => "Formatos de placa del gabinete",
        CompatibilityKeys.GpuLengthMm => "Largo de la tarjeta de video (mm)",
        CompatibilityKeys.CaseMaxGpuMm => "Largo máximo de GPU del gabinete (mm)",
        CompatibilityKeys.PowerDrawW => "Consumo (W)",
        CompatibilityKeys.CpuTdpW => "TDP del procesador (W)",
        CompatibilityKeys.PsuWatts => "Potencia de la fuente (W)",
        CompatibilityKeys.M2Slots => "Ranuras M.2 de la placa",
        CompatibilityKeys.CoolerSockets => "Sockets del disipador",
        CompatibilityKeys.IntegratedGraphics => "Gráficos integrados",
        CompatibilityKeys.StorageInterface => "Interfaz de almacenamiento",
        CompatibilityKeys.RamModules => "Módulos del kit de RAM",
        CompatibilityKeys.RamCapacityGb => "Capacidad del kit de RAM (GB)",
        _ => key,
    };

    public static string SerialStatus(SerialNumberStatus status) => Capitalize(SerialNumber.Describe(status));

    /// <summary>Primera letra en mayúscula sin tocar el resto (conserva siglas como «RMA»; <see cref="Fmt.SentenceCase"/> las
    /// pasaría a minúsculas).</summary>
    public static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? string.Empty : char.ToUpper(text[0], Fmt.Culture) + text[1..];

    public static string SerialBrush(SerialNumberStatus status) => status switch
    {
        SerialNumberStatus.InStock => "Success",
        SerialNumberStatus.Sold => "Info",
        SerialNumberStatus.InTransit or SerialNumberStatus.Reserved => "Info",
        SerialNumberStatus.Returned or SerialNumberStatus.InRma => "Warning",
        _ => "StatusInactive",
    };

    public static string ClaimStatus(WarrantyClaimStatus status) => status switch
    {
        WarrantyClaimStatus.SentToSupplier => "En el proveedor",
        _ => Fmt.SentenceCase(WarrantyClaim.Describe(status)),
    };

    public static string ClaimBrush(WarrantyClaimStatus status) => status switch
    {
        WarrantyClaimStatus.Received => "Info",
        WarrantyClaimStatus.Diagnosing or WarrantyClaimStatus.SentToSupplier => "Warning",
        WarrantyClaimStatus.Repaired or WarrantyClaimStatus.Replaced => "Success",
        WarrantyClaimStatus.Rejected => "Danger",
        _ => "StatusInactive",
    };

    /// <summary>Botón que lleva el caso al estado <paramref name="next"/>.</summary>
    public static string ClaimAction(WarrantyClaimStatus next) => next switch
    {
        WarrantyClaimStatus.Diagnosing => "Pasar a diagnóstico",
        WarrantyClaimStatus.SentToSupplier => "Enviar al proveedor",
        WarrantyClaimStatus.Repaired => "Marcar reparado",
        WarrantyClaimStatus.Replaced => "Reemplazar con otra unidad",
        WarrantyClaimStatus.Rejected => "Rechazar la garantía",
        WarrantyClaimStatus.Delivered => "Entregar al cliente",
        _ => Fmt.SentenceCase(WarrantyClaim.Describe(next)),
    };

    public static string ClaimGlyph(WarrantyClaimStatus next) => next switch
    {
        WarrantyClaimStatus.Diagnosing => Glyphs.Wrench,
        WarrantyClaimStatus.SentToSupplier => Glyphs.Transfer,
        WarrantyClaimStatus.Repaired => Glyphs.CheckCircle,
        WarrantyClaimStatus.Replaced => Glyphs.Swap,
        WarrantyClaimStatus.Rejected => Glyphs.Error,
        WarrantyClaimStatus.Delivered => Glyphs.Person,
        _ => Glyphs.ChevronRight,
    };

    public static string ClaimAction(WarrantyClaimAction action) => action switch
    {
        WarrantyClaimAction.Opened => "Caso abierto",
        WarrantyClaimAction.StatusChanged => "Cambio de estado",
        WarrantyClaimAction.NoteAdded => "Nota",
        WarrantyClaimAction.ReplacementIssued => "Reposición entregada",
        WarrantyClaimAction.Closed => "Caso cerrado",
        _ => action.ToString(),
    };

    public static string SerialAction(SerialEventAction action) => action switch
    {
        SerialEventAction.Received => "Ingresó al stock",
        SerialEventAction.Sold => "Vendida",
        SerialEventAction.Returned => "Devuelta por el cliente",
        SerialEventAction.TransferDispatched => "Despachada en una transferencia",
        SerialEventAction.TransferReceived => "Recibida por transferencia",
        SerialEventAction.RmaReceived => "Recibida en garantía (RMA)",
        SerialEventAction.SentToSupplier => "Enviada al proveedor o al servicio técnico",
        SerialEventAction.Repaired => "Reparada",
        SerialEventAction.Replaced => "Reemplazada por otra unidad",
        SerialEventAction.ReplacementIssued => "Entregada como reposición de garantía",
        SerialEventAction.ReturnedToSupplier => "Devuelta al proveedor",
        SerialEventAction.Scrapped => "Dada de baja",
        SerialEventAction.Adjusted => "Ajuste de inventario",
        SerialEventAction.Restocked => "Volvió al stock",
        SerialEventAction.ReturnedToCustomer => "Entregada a su dueño",
        _ => action.ToString(),
    };

    public static string SerialActionBrush(SerialEventAction action) => action switch
    {
        SerialEventAction.Received or SerialEventAction.Restocked or SerialEventAction.TransferReceived or SerialEventAction.Repaired => "Success",
        SerialEventAction.Sold or SerialEventAction.ReplacementIssued or SerialEventAction.ReturnedToCustomer => "Info",
        SerialEventAction.Returned or SerialEventAction.RmaReceived or SerialEventAction.SentToSupplier or SerialEventAction.Replaced => "Warning",
        SerialEventAction.Scrapped or SerialEventAction.ReturnedToSupplier => "Danger",
        _ => "Brand",
    };

    public static string SerialActionGlyph(SerialEventAction action) => action switch
    {
        SerialEventAction.Received or SerialEventAction.Restocked => Glyphs.Box,
        SerialEventAction.Sold or SerialEventAction.ReplacementIssued => Glyphs.Cart,
        SerialEventAction.Returned or SerialEventAction.ReturnedToCustomer => Glyphs.Undo,
        SerialEventAction.TransferDispatched or SerialEventAction.TransferReceived or SerialEventAction.SentToSupplier => Glyphs.Transfer,
        SerialEventAction.RmaReceived or SerialEventAction.Repaired or SerialEventAction.Replaced => Glyphs.Shield,
        SerialEventAction.Scrapped or SerialEventAction.ReturnedToSupplier => Glyphs.Delete,
        _ => Glyphs.Swap,
    };

    public static string BuildStatus(PcBuildStatus status, bool expired) => status switch
    {
        PcBuildStatus.Draft => "Borrador",
        PcBuildStatus.Quoted when expired => "Cotización vencida",
        PcBuildStatus.Quoted => "Cotizado",
        PcBuildStatus.Reserved when expired => "Reserva vencida",   // V6
        PcBuildStatus.Reserved => "Reservado",
        PcBuildStatus.Sold => "Vendido",
        _ => "Anulado",
    };

    public static string BuildBrush(PcBuildStatus status, bool expired) => status switch
    {
        PcBuildStatus.Draft => "StatusInactive",
        PcBuildStatus.Quoted when expired => "Warning",
        PcBuildStatus.Quoted => "Info",
        PcBuildStatus.Reserved when expired => "Warning",
        PcBuildStatus.Reserved => "Brand",
        PcBuildStatus.Sold => "Success",
        _ => "Danger",
    };

    /// <summary>V7 · Acción de la bitácora de un armado o de una reserva (regla S-04), en palabras.</summary>
    public static string BuildAction(PcBuildEventAction action) => action switch
    {
        PcBuildEventAction.Created => "Creada",
        PcBuildEventAction.Quoted => "Cotizada",
        PcBuildEventAction.Reserved => "Stock reservado",
        PcBuildEventAction.Released => "Reserva liberada",
        PcBuildEventAction.Expired => "Reserva vencida",
        PcBuildEventAction.Sold => "Vendida en la caja",
        PcBuildEventAction.Cancelled => "Anulada",
        PcBuildEventAction.Published => "Publicada en la web",
        PcBuildEventAction.Unpublished => "Retirada de la web",
        _ => "Cambio",
    };

    public static string BuildActionBrush(PcBuildEventAction action) => action switch
    {
        PcBuildEventAction.Reserved or PcBuildEventAction.Published => "Brand",
        PcBuildEventAction.Sold => "Success",
        PcBuildEventAction.Released or PcBuildEventAction.Cancelled => "Danger",
        PcBuildEventAction.Expired => "Warning",
        _ => "Info",
    };

    public static string Slot(PcSlot slot) => slot switch
    {
        PcSlot.Cpu => "Procesador",
        PcSlot.Motherboard => "Placa madre",
        PcSlot.Ram => "Memoria RAM",
        PcSlot.Gpu => "Tarjeta de video",
        PcSlot.Storage => "Almacenamiento",
        PcSlot.Psu => "Fuente de poder",
        PcSlot.Case => "Gabinete",
        PcSlot.Cooler => "Refrigeración",
        PcSlot.Monitor => "Monitor",
        PcSlot.Peripheral => "Periféricos",
        PcSlot.Software => "Software",
        _ => "Servicios",
    };

    /// <summary>V7 · La línea de un carrito puede venir sin ranura: es un producto suelto.</summary>
    public static string Slot(PcSlot? slot) => slot is { } known ? Slot(known) : "Producto";

    public static string SlotGlyph(PcSlot slot) => slot switch
    {
        PcSlot.Cpu => Glyphs.Pulse,
        PcSlot.Motherboard => Glyphs.Link,
        PcSlot.Ram => Glyphs.Checklist,
        PcSlot.Gpu => Glyphs.Sparkle,
        PcSlot.Storage => Glyphs.Save,
        PcSlot.Psu => Glyphs.Power,
        PcSlot.Case => Glyphs.Box,
        PcSlot.Cooler => Glyphs.Sync,
        PcSlot.Monitor => Glyphs.Monitor,
        PcSlot.Peripheral => Glyphs.Keyboard,
        PcSlot.Software => Glyphs.Code,
        _ => Glyphs.Wrench,
    };

    /// <summary>«IMEI» o «Serie» (insignias).</summary>
    public static string KindBadge(SerialKind kind) => kind == SerialKind.Imei ? "IMEI" : "Serie";

    /// <summary>«IMEI» o «N° de serie» (campos y columnas).</summary>
    public static string KindLabel(SerialKind kind) => kind == SerialKind.Imei ? "IMEI" : "N° de serie";

    /// <summary>«Garantía 12 meses» / «Garantía 1 año» (insignia); vacío si no tiene.</summary>
    public static string Warranty(int months) => months switch
    {
        <= 0 => string.Empty,
        1 => "Garantía 1 mes",
        12 => "Garantía 1 año",
        _ when months % 12 == 0 => $"Garantía {months / 12} años",
        _ => $"Garantía {months} meses",
    };

    public static string SpecType(SpecDataType type, bool multi) => (type switch
    {
        SpecDataType.Number => "Número",
        SpecDataType.Option => "Opción",
        _ => "Texto",
    }) + (multi ? " (varios valores)" : string.Empty);
}

/// <summary>V6 · Unidades reservadas por SKU en el almacén de trabajo (reservas de armados web y del escritorio, reservas de
/// caja): stock, catálogo y caja muestran «Reservado: n» y disponible = existencias − reservado (regla S-08).</summary>
public static class ReservedStock
{
    /// <summary>Reservado por SKU (vacío si la sesión no puede leer el stock o la lectura falla: las pantallas siguen).</summary>
    public static async Task<IReadOnlyDictionary<string, decimal>> LoadAsync(AppServices app)
    {
        try
        {
            var rows = await app.SendAsync(new MINV.Application.Inventory.Queries.GetStockReservationsQuery());
            return rows.ToDictionary(r => r.Sku, r => r.Reserved, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            System.Diagnostics.Trace.TraceWarning("M-INV · stock reservado: {0}", ex.Message);
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>«Reservado: 3».</summary>
    public static string Badge(decimal reserved) => $"Reservado: {Fmt.Qty(reserved)}";
}

/// <summary>V4.2 · Catálogo técnico por SKU (serie o IMEI, garantía, plataformas) para las pantallas que lo necesitan.</summary>
public static class TechCatalog
{
    /// <summary>Productos activos con su ficha resumida (vacío si la sesión no puede leer el catálogo técnico).</summary>
    public static async Task<IReadOnlyDictionary<string, TechProductRow>> LoadAsync(AppServices app, string? categoryCode = null,
        string? platform = null, IReadOnlyList<SpecFilter>? filters = null)
    {
        try
        {
            var rows = await app.SendAsync(new SearchTechProductsQuery(CategoryCode: categoryCode, Platform: platform, Filters: filters, Max: 2000));
            return rows.GroupBy(r => r.Sku, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            System.Diagnostics.Trace.TraceWarning("M-INV · catálogo técnico: {0}", ex.Message);
            return new Dictionary<string, TechProductRow>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Plataformas definidas en las especificaciones «plataforma» y «plataformas» (regla T-07: los chips salen de ahí,
    /// nunca de una lista fija).</summary>
    public static async Task<IReadOnlyList<string>> PlatformsAsync(AppServices app)
    {
        try
        {
            var definitions = await app.SendAsync(new GetSpecDefinitionsQuery());
            return definitions.Where(d => TechSpecCodes.IsPlatform(d.Code)).OrderBy(d => d.Code == TechSpecCodes.Platform ? 0 : 1)
                .SelectMany(d => d.Options).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            System.Diagnostics.Trace.TraceWarning("M-INV · plataformas: {0}", ex.Message);
            return [];
        }
    }

    /// <summary>Ficha técnica de un producto (null si no se pudo leer).</summary>
    public static async Task<ProductTechView?> ProductAsync(AppServices app, string sku)
    {
        try
        {
            return await app.SendAsync(new GetProductTechQuery(sku));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            System.Diagnostics.Trace.TraceWarning("M-INV · ficha técnica de {0}: {1}", sku, ex.Message);
            return null;
        }
    }
}

// --------------------------------------------------------------------------------------------------- captura de series
/// <summary>Cómo se toman las series de una línea: se escriben (unidades que ENTRAN) o se eligen de las disponibles (SALEN).</summary>
public enum SerialCaptureMode
{
    Entry,
    Pick,
}

/// <summary>Serie escrita o escaneada, con su problema (guía visual: IMEI no válido, repetida).</summary>
public sealed class SerialEntry(string value, string? problem) : ObservableObject
{
    private string? _problem = problem;

    public string Value { get; } = value;

    public string? Problem
    {
        get => _problem;
        set
        {
            if (Set(ref _problem, value))
            {
                OnPropertyChanged(nameof(HasProblem));
            }
        }
    }

    public bool HasProblem => _problem is not null;
}

/// <summary>Unidad disponible que se puede elegir (caja, salidas, reposición de garantía).</summary>
public sealed class SerialPickOption(SerialRow row, Action changed) : ObservableObject
{
    private bool _checked;

    public SerialRow Row { get; } = row;

    public string Serial => Row.Serial;

    public string Detail => string.Join(" · ", new[]
    {
        Row.Warehouse, Row.ReceivedAt is { } at ? "ingresó " + Fmt.DateTime(at) : null,
    }.Where(t => !string.IsNullOrWhiteSpace(t)));

    public bool IsChecked
    {
        get => _checked;
        set
        {
            if (Set(ref _checked, value))
            {
                changed();
            }
        }
    }
}

/// <summary>
/// Una línea del formulario de series: el producto, cuántas series lleva («3 de 5») y cómo se toman. Las que ENTRAN se
/// escanean (el escáner en modo teclado escribe y termina con Enter), se escriben o se pegan como lista; las que SALEN se
/// eligen de las disponibles de la sucursal (con búsqueda; escanear una la marca).
/// </summary>
public sealed class SerialCaptureLine : ObservableObject
{
    private readonly Action _changed;
    private readonly List<SerialPickOption> _options = [];
    private string _input = string.Empty;
    private string _paste = string.Empty;
    private string _search = string.Empty;
    private string? _message;
    private bool _messageIsError;

    public SerialCaptureLine(string sku, string name, SerialKind kind, int expected, SerialCaptureMode mode, Action changed,
        IReadOnlyList<SerialRow>? available = null, IEnumerable<string>? initial = null, bool flexible = false)
    {
        Sku = sku;
        Name = name;
        Kind = kind;
        Expected = expected;
        Mode = mode;
        IsFlexible = flexible;
        _changed = changed;
        foreach (var row in available ?? [])
        {
            _options.Add(new SerialPickOption(row, OnPicked));
        }
        Options = CollectionViewSource.GetDefaultView(_options);
        Options.Filter = o => o is SerialPickOption p && (_search.Trim().Length == 0 || p.Serial.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase));
        AddInput = new RelayCommand(() =>
        {
            Accept(_input);
            Input = string.Empty;
        });
        AddPasted = new RelayCommand(() =>
        {
            var added = 0;
            foreach (var value in Split(_paste))
            {
                if (Add(value))
                {
                    added++;
                }
            }
            Paste = string.Empty;
            if (added > 0)
            {
                Say(added == 1 ? "Se agregó 1 serie de la lista." : $"Se agregaron {added} series de la lista.", error: Entries.Any(e => e.HasProblem));
            }
        }, () => _paste.Trim().Length > 0);
        RemoveEntry = new RelayCommand<SerialEntry>(entry =>
        {
            Entries.Remove(entry);
            Revalidate();
        });
        ClearAll = new RelayCommand(() =>
        {
            Entries.Clear();
            foreach (var option in _options)
            {
                option.IsChecked = false;
            }
            Revalidate();
        });
        foreach (var value in initial ?? [])
        {
            if (mode == SerialCaptureMode.Entry)
            {
                Add(value);
            }
            else if (_options.FirstOrDefault(o => o.Serial.Equals(value, StringComparison.OrdinalIgnoreCase)) is { } option)
            {
                option.IsChecked = true;
            }
        }
    }

    public string Sku { get; }

    public string Name { get; }

    public SerialKind Kind { get; }

    public string KindText => TechText.KindLabel(Kind);

    public string KindBadge => TechText.KindBadge(Kind);

    public SerialCaptureMode Mode { get; }

    public bool IsEntry => Mode == SerialCaptureMode.Entry;

    public bool IsPick => Mode == SerialCaptureMode.Pick;

    /// <summary>Cantidad elegida libremente (caja: al agregar un producto se eligen una o varias unidades).</summary>
    public bool IsFlexible { get; }

    public int Expected { get; }

    public ObservableCollection<SerialEntry> Entries { get; } = [];

    public ICollectionView Options { get; }

    public int AvailableCount => _options.Count;

    public bool NoneAvailable => IsPick && _options.Count == 0;

    public string Input { get => _input; set => Set(ref _input, value ?? string.Empty); }

    public string Paste { get => _paste; set => Set(ref _paste, value ?? string.Empty); }

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? string.Empty))
            {
                Options.Refresh();
            }
        }
    }

    public string? Message { get => _message; private set => Set(ref _message, value); }

    public bool MessageIsError { get => _messageIsError; private set => Set(ref _messageIsError, value); }

    public string Placeholder => IsEntry
        ? $"Escanee o escriba el {KindText} y presione Enter"
        : $"Escanee el {KindText} o búsquelo en la lista";

    public int Count => IsEntry ? Entries.Count : _options.Count(o => o.IsChecked);

    public string CountText => IsFlexible ? $"{Count} {(Count == 1 ? "elegida" : "elegidas")}" : $"{Count} de {Expected}";

    public bool HasProblems => Entries.Any(e => e.HasProblem);

    public bool IsComplete => !HasProblems && (IsFlexible ? Count >= 1 : Count == Expected);

    public string CountBrush => IsComplete ? "Success" : HasProblems || (!IsFlexible && Count > Expected) ? "Danger" : "Warning";

    public string CountSoftBrush => CountBrush + "Soft";

    public IReadOnlyList<string> Serials => IsEntry
        ? Entries.Select(e => e.Value).ToList()
        : _options.Where(o => o.IsChecked).Select(o => o.Serial).ToList();

    public RelayCommand AddInput { get; }

    public RelayCommand AddPasted { get; }

    public RelayCommand<SerialEntry> RemoveEntry { get; }

    public RelayCommand ClearAll { get; }

    /// <summary>Lectura del escáner o Enter en el campo: agrega (entrada) o marca (salida). false si no la tomó.</summary>
    public bool Accept(string? code)
    {
        var value = (code ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return false;
        }
        if (IsEntry)
        {
            return Add(value);
        }
        var normalized = Kind == SerialKind.Imei ? Imei.Normalize(value) : value.ToUpperInvariant();
        if (_options.FirstOrDefault(o => o.Serial.Equals(normalized, StringComparison.OrdinalIgnoreCase)) is not { } option)
        {
            Say($"«{value}» no está disponible en esta sucursal para {Sku}.", error: true);
            return false;
        }
        if (option.IsChecked)
        {
            Say($"{option.Serial} ya estaba elegida.", error: false);
            return true;
        }
        if (!IsFlexible && Count >= Expected)
        {
            Say($"Ya eligió las {Expected} series de {Sku}: quite una para cambiarla.", error: true);
            return false;
        }
        option.IsChecked = true;
        Say($"✔ {option.Serial}", error: false);
        return true;
    }

    /// <summary>Series de las otras líneas del MISMO producto (para marcar repetidas).</summary>
    internal Func<SerialCaptureLine, IEnumerable<string>>? Siblings { get; set; }

    internal void Revalidate()
    {
        var others = Siblings?.Invoke(this).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
        {
            entry.Problem = Problem(entry.Value) ?? (!seen.Add(entry.Value) || others.Contains(entry.Value) ? "Repetida" : null);
        }
        Changed();
    }

    private bool Add(string raw)
    {
        if (!IsFlexible && Entries.Count >= Expected)
        {
            Say($"Ya tiene las {Expected} series de {Sku}: quite una para cambiarla.", error: true);
            return false;
        }
        var value = Kind == SerialKind.Imei ? Imei.Normalize(raw) : raw.Trim().ToUpperInvariant();
        if (Entries.Any(e => e.Value.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            Say($"{value} ya está en la lista (repetida).", error: true);
            return false;
        }
        var problem = Problem(value);
        Entries.Add(new SerialEntry(value, problem));
        Say(problem is null ? $"✔ {value}" : $"{value}: {problem}", error: problem is not null);
        Revalidate();
        return true;
    }

    /// <summary>Guía en vivo (el dominio vuelve a validar al guardar): IMEI con 15 dígitos y dígito de Luhn; serie sin
    /// espacios, comas ni punto y coma.</summary>
    private string? Problem(string value)
    {
        try
        {
            _ = SerialNumber.Normalize(Kind, value);
            return null;
        }
        catch (DomainException) when (Kind == SerialKind.Imei)
        {
            return "IMEI no válido (15 dígitos con dígito verificador)";
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
    }

    private void OnPicked()
    {
        if (!IsFlexible && Count > Expected)
        {
            Say($"Eligió {Count} series y la línea lleva {Expected}.", error: true);
        }
        Changed();
    }

    private void Say(string text, bool error)
    {
        Message = text;
        MessageIsError = error;
    }

    private void Changed()
    {
        OnPropertiesChanged(nameof(Count), nameof(CountText), nameof(IsComplete), nameof(HasProblems), nameof(CountBrush), nameof(CountSoftBrush),
            nameof(Serials));
        _changed();
    }

    /// <summary>Una lista pegada: una serie por renglón o separadas por comas, punto y coma, tabulaciones o espacios.</summary>
    public static IReadOnlyList<string> Split(string? text) =>
        (text ?? string.Empty).Split(['\r', '\n', ',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// Formulario modal de series e IMEI: una sección por producto serializado del documento (recepción de compra, saldo
/// inicial o ajuste, venta en la caja, venta de un armado, reposición de garantía). «Confirmar» se habilita cuando cada
/// línea tiene sus series; el caso de uso se ejecuta desde el propio formulario, así un rechazo del dominio (serie
/// repetida en la empresa, de otra sucursal o en otro estado) se muestra aquí mismo para corregirlo.
/// </summary>
public sealed class SerialsDialog : FormDialog, IScannerTarget
{
    private readonly Func<IReadOnlyList<SkuSerials>, Task<bool>>? _submit;
    private readonly string? _subtitle;

    public SerialsDialog(string title, string? subtitle, string confirmText, IReadOnlyList<SerialCaptureLineSpec> lines,
        Func<IReadOnlyList<SkuSerials>, Task<bool>>? submit = null)
        : base(title, confirmText, Glyphs.Barcode, width: 680)
    {
        _subtitle = subtitle;
        _submit = submit;
        Lines = lines.Select(l => new SerialCaptureLine(l.Sku, l.Name, l.Kind, l.Expected, l.Mode, OnChanged, l.Available, l.Initial, l.Flexible))
            .ToList();
        foreach (var line in Lines)
        {
            line.Siblings = self => Lines.Where(o => !ReferenceEquals(o, self) && o.IsEntry && o.Sku.Equals(self.Sku, StringComparison.OrdinalIgnoreCase))
                .SelectMany(o => o.Serials);
        }
    }

    public override string? Subtitle => _subtitle;

    public IReadOnlyList<SerialCaptureLine> Lines { get; }

    public string SummaryText => Lines.Count == 1
        ? Lines[0].CountText
        : $"{Lines.Count(l => l.IsComplete)} de {Lines.Count} productos con sus series";

    /// <summary>Series por producto (en el orden de las líneas).</summary>
    public IReadOnlyList<SkuSerials> Result => Lines.GroupBy(l => l.Sku, StringComparer.OrdinalIgnoreCase)
        .Select(g => new SkuSerials(g.Key, g.SelectMany(l => l.Serials).ToList())).ToList();

    /// <summary>El escáner escribe en la primera línea que todavía no está completa.</summary>
    public bool OnScanned(string code)
    {
        var line = Lines.FirstOrDefault(l => !l.IsComplete) ?? Lines.FirstOrDefault();
        return line?.Accept(code) ?? false;
    }

    protected override bool CanConfirm() => Lines.All(l => l.IsComplete);

    protected override async Task<bool> SubmitAsync()
    {
        if (Lines.FirstOrDefault(l => !l.IsComplete) is { } missing)
        {
            Error = missing.HasProblems
                ? $"Revise las series marcadas en rojo de {missing.Name}."
                : $"{missing.Name} lleva {missing.Expected} series y tiene {missing.Count}.";
            return false;
        }
        return _submit is null || await _submit(Result);
    }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(SummaryText));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Muestra el formulario y devuelve las series por producto (null si se canceló).</summary>
    public static async Task<IReadOnlyList<SkuSerials>?> AskAsync(AppServices app, string title, string? subtitle, string confirmText,
        IReadOnlyList<SerialCaptureLineSpec> lines, Func<IReadOnlyList<SkuSerials>, Task<bool>>? submit = null)
    {
        var dialog = new SerialsDialog(title, subtitle, confirmText, lines, submit);
        return await app.Dialogs.ShowAsync(dialog) ? dialog.Result : null;
    }
}

/// <summary>Definición de una línea del formulario de series.</summary>
public sealed record SerialCaptureLineSpec(string Sku, string Name, SerialKind Kind, int Expected, SerialCaptureMode Mode,
    IReadOnlyList<SerialRow>? Available = null, IReadOnlyList<string>? Initial = null, bool Flexible = false);

/// <summary>Formatos breves de la edición Tecnología.</summary>
public static class TechFormat
{
    public static string Months(int months) => months <= 0 ? "Sin garantía" : months == 1 ? "1 mes" : $"{months.ToString(CultureInfo.InvariantCulture)} meses";
}
