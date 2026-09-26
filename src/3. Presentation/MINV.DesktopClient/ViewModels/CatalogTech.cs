using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using MINV.Application.Catalog;
using MINV.Application.Tech;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Catalog;

namespace MINV.DesktopClient.ViewModels;

// =====================================================================================================================
// V4.2 · Catálogo técnico en el escritorio: pestaña «Ficha técnica» del editor de productos (cada especificación con el
// control de su tipo), filtros por especificación de la categoría elegida (facetas) y administración de especificaciones
// por categoría. Las reglas (tipo del valor, opción de ESA especificación, obligatorias, herencia) las valida el dominio.
// =====================================================================================================================

/// <summary>Opción de una especificación de tipo «opción» con varios valores (casillas).</summary>
public sealed class SpecOptionToggle(string value, Action changed) : ObservableObject
{
    private bool _checked;

    public string Value { get; } = value;

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

/// <summary>Una especificación de la ficha con el control que le corresponde: número con su unidad, texto, una opción
/// (combo) o varias (casillas). Las multivalor de texto o número se escriben separadas por comas.</summary>
public sealed class SpecField : ObservableObject
{
    public const string NoValue = "(sin valor)";
    private readonly Action _changed;
    private string _text = string.Empty;
    private string? _option;

    public SpecField(SpecDefinitionView definition, IReadOnlyList<string> values, Action changed)
    {
        Definition = definition;
        _changed = changed;
        if (IsOptionMulti)
        {
            Toggles = definition.Options.Select(o => new SpecOptionToggle(o, changed)).ToList();
            foreach (var toggle in Toggles.Where(t => values.Contains(t.Value, StringComparer.OrdinalIgnoreCase)))
            {
                toggle.IsChecked = true;
            }
        }
        else if (IsOptionSingle)
        {
            Choices = [NoValue, .. definition.Options];
            _option = definition.Options.FirstOrDefault(o => values.Contains(o, StringComparer.OrdinalIgnoreCase)) ?? NoValue;
        }
        else
        {
            _text = string.Join(", ", values.Select(v => definition.DataType == SpecDataType.Number ? ShowNumber(v) : v));
        }
    }

    public SpecDefinitionView Definition { get; }

    public string Code => Definition.Code;

    public string Label => Definition.Name + (Definition.IsRequired ? " *" : string.Empty);

    public string? Unit => Definition.Unit;

    public bool HasUnit => !string.IsNullOrWhiteSpace(Definition.Unit);

    public string Help => string.Join(" · ", new[]
    {
        TechText.SpecType(Definition.DataType, Definition.IsMultiValued),
        Definition.IsInherited ? $"de {Definition.CategoryName}" : null,
        Definition.CompatibilityKey is not null ? "la usa el armador de PC" : null,
        Definition.IsFilterable ? "filtra el catálogo" : null,
    }.Where(t => t is not null));

    public bool IsNumber => Definition.DataType == SpecDataType.Number;

    public bool IsText => Definition.DataType == SpecDataType.Text;

    public bool IsFree => Definition.DataType != SpecDataType.Option;

    public bool IsOptionSingle => Definition.DataType == SpecDataType.Option && !Definition.IsMultiValued;

    public bool IsOptionMulti => Definition.DataType == SpecDataType.Option && Definition.IsMultiValued;

    public string Placeholder => Definition.IsMultiValued ? "Valores separados por comas" : IsNumber ? "0" : "Valor";

    public IReadOnlyList<SpecOptionToggle> Toggles { get; } = [];

    public IReadOnlyList<string> Choices { get; } = [];

    public string Text
    {
        get => _text;
        set
        {
            if (Set(ref _text, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(Problem));
                _changed();
            }
        }
    }

    public string? Option
    {
        get => _option;
        set
        {
            if (Set(ref _option, value))
            {
                _changed();
            }
        }
    }

    /// <summary>Guía: un número mal escrito (el dominio vuelve a validar).</summary>
    public string? Problem => IsNumber && Values().FirstOrDefault(v => TechNumbers.Parse(v) is null) is { } wrong ? $"«{wrong}» no es un número" : null;

    public IReadOnlyList<string> Values()
    {
        if (IsOptionMulti)
        {
            return Toggles.Where(t => t.IsChecked).Select(t => t.Value).ToList();
        }
        if (IsOptionSingle)
        {
            return _option is null or NoValue ? [] : [_option];
        }
        var parts = Definition.IsMultiValued
            ? _text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [_text.Trim()];
        return parts.Where(p => p.Length > 0).ToList();
    }

    /// <summary>Número guardado (punto decimal) → como lo escribe una persona (coma decimal).</summary>
    private static string ShowNumber(string raw) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n.ToString("0.####", Fmt.Culture) : raw;
}

/// <summary>Lectura de números escritos con coma o punto decimal (guía; el dominio decide).</summary>
public static class TechNumbers
{
    public static decimal? Parse(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Contains(',', StringComparison.Ordinal) && !value.Contains('.', StringComparison.Ordinal))
        {
            value = value.Replace(',', '.');
        }
        return decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}

/// <summary>
/// Pestaña «Ficha técnica» del editor de productos: perfil (lleva serie o IMEI y meses de garantía) y las especificaciones
/// de la categoría (propias y heredadas). Solo se guarda si se cambió algo (<see cref="IsDirty"/>).
/// </summary>
public sealed class TechSheetEditor : ObservableObject
{
    private readonly bool _wasTracking;
    private bool _trackSerials;
    private Choice<SerialKind> _kind;
    private string _warranty;
    private bool _dirty;

    public TechSheetEditor(IReadOnlyList<SpecDefinitionView> definitions, ProductTechView? current, bool canEdit, string categoryName)
    {
        CanEdit = canEdit;
        CategoryName = categoryName;
        Kinds = [new("Número de serie del fabricante", SerialKind.Serial), new("IMEI (celulares y equipos con SIM: 15 dígitos)", SerialKind.Imei)];
        _trackSerials = current?.TrackSerials ?? false;
        _wasTracking = _trackSerials;
        _kind = Kinds.First(k => k.Value == (current?.SerialKind ?? SerialKind.Serial));
        _warranty = (current?.WarrantyMonths ?? 0).ToString(CultureInfo.InvariantCulture);
        SerialsInStock = current?.SerialsInStock ?? 0;
        var values = (current?.Specs ?? []).ToDictionary(s => s.Code, s => s.Values, StringComparer.OrdinalIgnoreCase);
        Fields = definitions.Select(d => new SpecField(d, values.GetValueOrDefault(d.Code) ?? [], MarkDirty)).ToList();
    }

    public bool CanEdit { get; }

    public string CategoryName { get; }

    public IReadOnlyList<SpecField> Fields { get; }

    public bool HasFields => Fields.Count > 0;

    public bool NoFields => Fields.Count == 0;

    public string FieldsHelp => Fields.Count == 0
        ? $"La categoría {CategoryName} todavía no tiene especificaciones: un administrador las define con «Especificaciones» en el catálogo."
        : $"{Fields.Count} especificaciones de {CategoryName} (las marcadas con * son obligatorias).";

    public IReadOnlyList<Choice<SerialKind>> Kinds { get; }

    public IReadOnlyList<string> WarrantyPresets { get; } = ["0", "3", "6", "12", "18", "24", "36"];

    public int SerialsInStock { get; }

    public bool TrackSerials
    {
        get => _trackSerials;
        set
        {
            if (Set(ref _trackSerials, value))
            {
                MarkDirty();
                OnPropertyChanged(nameof(SerialHelp));
            }
        }
    }

    public Choice<SerialKind> Kind
    {
        get => _kind;
        set
        {
            if (Set(ref _kind, value ?? Kinds[0]))
            {
                MarkDirty();
            }
        }
    }

    public string WarrantyMonths
    {
        get => _warranty;
        set
        {
            if (Set(ref _warranty, value ?? string.Empty))
            {
                MarkDirty();
                OnPropertyChanged(nameof(WarrantyText));
            }
        }
    }

    public string WarrantyText => int.TryParse(_warranty, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) && m > 0
        ? $"{TechText.Warranty(m)}: se imprime «Garantía hasta dd/mm/aaaa» en el ticket y la factura (fecha de la venta + meses)."
        : "Sin garantía del producto.";

    public string SerialHelp => !_trackSerials
        ? "El producto se vende por cantidad, sin serie."
        : _wasTracking
            ? $"Cada unidad entra, se vende y vuelve por garantía con su serie o IMEI ({(SerialsInStock == 1 ? "1 unidad" : $"{SerialsInStock} unidades")} con serie en stock)."
            : "Cada unidad entra, se vende y vuelve por garantía con su serie o IMEI. Para activarlo en un producto con existencias, cada " +
              "unidad en stock debe tener su serie: regístrelas en «Series e IMEI › Registrar series de stock» (gerencia con todas las sucursales).";

    public bool IsDirty
    {
        get => _dirty;
        private set => Set(ref _dirty, value);
    }

    public string? Check()
    {
        if (!int.TryParse(_warranty.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var months) || months is < 0 or > 120)
        {
            return "La garantía va de 0 a 120 meses.";
        }
        return Fields.FirstOrDefault(f => f.Problem is not null) is { } wrong ? $"{wrong.Definition.Name}: {wrong.Problem}." : null;
    }

    public SaveProductTechCommand ToCommand(string sku) => new(sku, _trackSerials, _kind.Value,
        int.Parse(_warranty.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture),
        Fields.Select(f => new ProductSpecInput(f.Code, f.Values())).Where(i => i.Values.Count > 0).ToList());

    private void MarkDirty() => IsDirty = true;
}

/// <summary>Filtro por una especificación de la categoría elegida (faceta con la cantidad de productos por valor).</summary>
public sealed class FacetFilter : ObservableObject
{
    public static readonly Choice<string?> Any = new("Cualquiera", null);
    private readonly Action _changed;
    private Choice<string?> _selected = Any;

    public FacetFilter(SpecFacet facet, Action changed)
    {
        Facet = facet;
        _changed = changed;
        Choices = [Any, .. facet.Values.Select(v => new Choice<string?>(
            $"{(facet.DataType == SpecDataType.Number ? ShowNumber(v.Value) : v.Value)}{(facet.Unit is null || facet.DataType != SpecDataType.Number ? "" : " " + facet.Unit)} ({v.Count})",
            v.Value))];
    }

    public SpecFacet Facet { get; }

    public string Name => Facet.Name;

    public IReadOnlyList<Choice<string?>> Choices { get; }

    public Choice<string?> Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value ?? Any))
            {
                OnPropertyChanged(nameof(IsActive));
                _changed();
            }
        }
    }

    public bool IsActive => _selected.Value is not null;

    public SpecFilter? ToFilter() => _selected.Value is { } value ? new SpecFilter(Facet.Code, [value]) : null;

    public void Reset()
    {
        _selected = Any;
        OnPropertiesChanged(nameof(Selected), nameof(IsActive));
    }

    private static string ShowNumber(string raw) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n.ToString("0.##", Fmt.Culture) : raw;
}

/// <summary>Especificación en la lista de la administración.</summary>
public sealed class SpecRow(SpecDefinitionView view)
{
    public SpecDefinitionView View { get; } = view;

    public string Name => View.Name + (View.Unit is { Length: > 0 } unit ? $" ({unit})" : string.Empty);

    public string Code => View.Code;

    public string TypeText => TechText.SpecType(View.DataType, View.IsMultiValued);

    public string Flags => string.Join(" · ", new[]
    {
        View.IsRequired ? "obligatoria" : null, View.IsFilterable ? "filtrable" : null, View.CompatibilityKey is { } key ? "armador: " + TechText.CompatibilityKey(key) : null,
        View.IsInherited ? "heredada de " + View.CategoryName : null,
    }.Where(t => t is not null));

    public string OptionsText => View.Options.Count == 0 ? string.Empty : string.Join(", ", View.Options);

    public bool IsInherited => View.IsInherited;
}

/// <summary>
/// Administración de especificaciones por categoría (Administrador): lista las propias y las heredadas de sus madres, y
/// crea o modifica una con su tipo (texto, número con unidad u opción), multivalor, filtrable, obligatoria, clave de
/// compatibilidad del armador (constantes del dominio) y opciones. También crea subcategorías (heredan las especificaciones).
/// </summary>
public sealed class SpecsAdminDialog : FormDialog
{
    private static readonly Choice<string?> NoKey = new("(no participa en el armador)", null);
    private readonly AppServices _app;
    private OptionItem? _category;
    private SpecRow? _selected;
    private string _code = string.Empty;
    private string _name = string.Empty;
    private string _unit = string.Empty;
    private Choice<SpecDataType> _type;
    private bool _multi;
    private bool _filterable = true;
    private bool _required;
    private Choice<string?> _key = NoKey;
    private string _order = "10";
    private string _options = string.Empty;
    private bool _editing;
    private string? _formError;

    public SpecsAdminDialog(AppServices app, IReadOnlyList<OptionItem> categories, string? categoryCode)
        : base("Especificaciones por categoría", "Cerrar", Glyphs.Settings, width: 980)
    {
        _app = app;
        Categories = new BulkObservableCollection<OptionItem>();
        Categories.ReplaceAll(categories);
        Types = [new("Texto", SpecDataType.Text), new("Número (con unidad)", SpecDataType.Number), new("Opción (lista de valores)", SpecDataType.Option)];
        _type = Types[0];
        // Claves de compatibilidad: las constantes del dominio (regla T-01), nunca una lista escrita a mano en la vista
        Keys = [NoKey, .. typeof(CompatibilityKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).Order(StringComparer.Ordinal)
            .Select(k => new Choice<string?>(TechText.CompatibilityKey(k), k)).OrderBy(k => k.Label, StringComparer.Create(Fmt.Culture, true))];
        NewSpec = new RelayCommand(() => Edit(null), () => _category is not null);
        SaveSpec = new AsyncRelayCommand(SaveSpecAsync, () => _category is not null && _editing);
        NewSubcategory = new AsyncRelayCommand(NewSubcategoryAsync, () => _category is not null);
        _category = Categories.FirstOrDefault(c => c.Code == categoryCode) ?? Categories.FirstOrDefault();
    }

    public override string Subtitle => "Cada categoría define sus especificaciones y sus subcategorías las heredan. Las filtrables alimentan los filtros " +
                                        "del catálogo y de la caja; las que tienen clave de compatibilidad las usa el armador de PC.";

    public override bool ShowConfirm => false;

    public BulkObservableCollection<OptionItem> Categories { get; }

    public BulkObservableCollection<SpecRow> Specs { get; } = [];

    public IReadOnlyList<Choice<SpecDataType>> Types { get; }

    public IReadOnlyList<Choice<string?>> Keys { get; }

    public OptionItem? Category
    {
        get => _category;
        set
        {
            if (Set(ref _category, value))
            {
                _ = LoadAsync();
            }
        }
    }

    public SpecRow? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value) && value is not null)
            {
                Edit(value);
            }
        }
    }

    public bool NoSpecs => Specs.Count == 0;

    public bool IsEditing { get => _editing; private set => Set(ref _editing, value); }

    public bool IsNewSpec => _selected is null || _selected.IsInherited;

    public string EditorTitle => _selected is { IsInherited: false } s ? $"Modificar «{s.View.Name}»" : $"Nueva especificación de {_category?.Name}";

    public string Code { get => _code; set => Set(ref _code, value ?? string.Empty); }

    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }

    public string Unit { get => _unit; set => Set(ref _unit, value ?? string.Empty); }

    public Choice<SpecDataType> Type
    {
        get => _type;
        set
        {
            if (Set(ref _type, value ?? Types[0]))
            {
                OnPropertyChanged(nameof(IsOptionType));
            }
        }
    }

    public bool IsOptionType => _type.Value == SpecDataType.Option;

    public bool IsMultiValued { get => _multi; set => Set(ref _multi, value); }

    public bool IsFilterable { get => _filterable; set => Set(ref _filterable, value); }

    public bool IsRequired { get => _required; set => Set(ref _required, value); }

    public Choice<string?> Key { get => _key; set => Set(ref _key, value ?? NoKey); }

    public string SortOrder { get => _order; set => Set(ref _order, value ?? string.Empty); }

    /// <summary>Opciones, una por renglón (en ese orden).</summary>
    public string Options { get => _options; set => Set(ref _options, value ?? string.Empty); }

    public string? FormError { get => _formError; private set => Set(ref _formError, value); }

    public RelayCommand NewSpec { get; }

    public AsyncRelayCommand SaveSpec { get; }

    public AsyncRelayCommand NewSubcategory { get; }

    protected override Task<bool> SubmitAsync() => Task.FromResult(true);

    public async Task LoadAsync(string? select = null)
    {
        if (_category is null)
        {
            Specs.ReplaceAll([]);
            return;
        }
        try
        {
            var rows = await _app.SendAsync(new GetSpecDefinitionsQuery(_category.Code));
            Specs.ReplaceAll(rows.Select(r => new SpecRow(r)));
            OnPropertyChanged(nameof(NoSpecs));
            _selected = select is null ? null : Specs.FirstOrDefault(s => s.Code == select && !s.IsInherited);
            OnPropertyChanged(nameof(Selected));
            Edit(_selected);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Error = AppServices.Describe(ex);
        }
    }

    private void Edit(SpecRow? row)
    {
        FormError = null;
        var view = row is { IsInherited: false } ? row.View : null;
        _selected = row;
        Code = view?.Code ?? string.Empty;
        Name = view?.Name ?? string.Empty;
        Unit = view?.Unit ?? string.Empty;
        Type = Types.First(t => t.Value == (view?.DataType ?? SpecDataType.Text));
        IsMultiValued = view?.IsMultiValued ?? false;
        IsFilterable = view?.IsFilterable ?? true;
        IsRequired = view?.IsRequired ?? false;
        Key = Keys.FirstOrDefault(k => k.Value == view?.CompatibilityKey) ?? NoKey;
        SortOrder = (view?.SortOrder ?? (Specs.Where(s => !s.IsInherited).Select(s => s.View.SortOrder).DefaultIfEmpty(0).Max() + 10))
            .ToString(CultureInfo.InvariantCulture);
        Options = view is null ? string.Empty : string.Join(Environment.NewLine, view.Options);
        IsEditing = true;
        OnPropertiesChanged(nameof(IsNewSpec), nameof(EditorTitle), nameof(Selected));
        if (row is { IsInherited: true })
        {
            FormError = $"«{row.View.Name}» se hereda de {row.View.CategoryName}: se modifica en esa categoría. Aquí puede crear una nueva.";
        }
    }

    private async Task SaveSpecAsync()
    {
        FormError = null;
        if (!int.TryParse(_order.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
        {
            FormError = "El orden es un número entero (10, 20, 30…).";
            return;
        }
        var options = _type.Value == SpecDataType.Option
            ? _options.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : [];
        try
        {
            var code = await _app.SendAsync(new SaveSpecDefinitionCommand(_category!.Code, _code.Trim(), _name.Trim(),
                string.IsNullOrWhiteSpace(_unit) ? null : _unit.Trim(), _type.Value, _multi, _filterable, _required, _key.Value, order, options));
            _app.Notify.Success("Especificación guardada", $"{_name.Trim()} ({code}) · {_category.Name}");
            await LoadAsync(code);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            FormError = AppServices.Describe(ex);
        }
    }

    private async Task NewSubcategoryAsync()
    {
        var parent = _category!;
        var name = await _app.Dialogs.PromptAsync("Nueva subcategoría", $"Se crea dentro de {parent.Name} y hereda sus especificaciones.",
            "Nombre de la subcategoría", [], "Crear subcategoría", placeholder: "Ej.: Tarjetas de video NVIDIA", glyph: Glyphs.Tag);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        var code = CategoryCodes.Suggest(name, Categories.Select(c => c.Code));
        try
        {
            await _app.SendAsync(new SaveCategoryCommand(code, name.Trim(), parent.Code));
            var option = new OptionItem(code, name.Trim());
            Categories.ReplaceAll(Categories.Append(option).OrderBy(c => c.Name, StringComparer.Create(Fmt.Culture, true)));
            Category = option;
            _app.Notify.Success("Subcategoría creada", $"{name.Trim()} ({code}) dentro de {parent.Name}");
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            FormError = AppServices.Describe(ex);
        }
    }
}

/// <summary>Código corto y único para una categoría nueva a partir de su nombre («Periféricos» → PER, PER2…).</summary>
public static class CategoryCodes
{
    public static string Suggest(string name, IEnumerable<string> existing)
    {
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var letters = new string(name.Normalize(System.Text.NormalizationForm.FormD).Where(char.IsAsciiLetter).ToArray()).ToUpperInvariant();
        var baseCode = letters.Length >= 3 ? letters[..3] : (letters + "CAT")[..3];
        var code = baseCode;
        for (var i = 2; taken.Contains(code); i++)
        {
            code = baseCode + i.ToString(CultureInfo.InvariantCulture);
        }
        return code;
    }
}
