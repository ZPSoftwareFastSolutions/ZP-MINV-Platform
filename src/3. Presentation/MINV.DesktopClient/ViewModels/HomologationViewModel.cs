using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using MINV.Application.Billing;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Billing;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Producto con su homologación (actividad y código de producto del SIN).</summary>
public sealed record ProductHomologationItem(ProductHomologationRow Row)
{
    public bool IsPending => Row.SinProductCode is null;

    public string SinText => Row.SinProductCode is { } code ? $"{code} · {Row.SinProductDescription ?? "(sin descripción)"}" : "Sin homologar";

    public string ActivityText => Row.ActivityCode ?? "—";

    public string StatusText => !Row.IsActive ? "Inactivo" : IsPending ? "Pendiente" : "Homologado";

    public string StatusBrush => !Row.IsActive ? "StatusInactive" : IsPending ? "Warning" : "Success";

    public string StatusSoftBrush => StatusBrush + "Soft";
}

/// <summary>Unidad de medida de M-INV con su unidad del SIN (combo del catálogo UNIDAD_MEDIDA).</summary>
public sealed class UnitHomologationItem : ObservableObject
{
    private readonly Func<UnitHomologationItem, Task> _save;
    private Choice<int>? _selected;

    public UnitHomologationItem(UnitHomologationRow row, IReadOnlyList<Choice<int>> options, Func<UnitHomologationItem, Task> save)
    {
        Row = row;
        Options = options;
        _save = save;
        _selected = options.FirstOrDefault(o => o.Value == row.SinUnitCode);
    }

    public UnitHomologationRow Row { get; }

    public IReadOnlyList<Choice<int>> Options { get; }

    public bool IsPending => Row.SinUnitCode is null;

    public Choice<int>? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value) && value is not null && value.Value != Row.SinUnitCode)
            {
                _ = _save(this);
            }
        }
    }
}

/// <summary>Medio de pago de M-INV con su método de pago del SIN (combo del catálogo TIPO_METODO_PAGO).</summary>
public sealed class PaymentHomologationItem : ObservableObject
{
    private readonly Func<PaymentHomologationItem, Task> _save;
    private Choice<int>? _selected;

    public PaymentHomologationItem(PaymentMethodHomologationRow row, IReadOnlyList<Choice<int>> options, Func<PaymentHomologationItem, Task> save)
    {
        Row = row;
        Options = options;
        _save = save;
        _selected = options.FirstOrDefault(o => o.Value == row.SinCode);
    }

    public PaymentMethodHomologationRow Row { get; }

    public IReadOnlyList<Choice<int>> Options { get; }

    public bool IsPending => Row.SinCode is null;

    public Choice<int>? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value) && value is not null && value.Value != Row.SinCode)
            {
                _ = _save(this);
            }
        }
    }
}

/// <summary>
/// V4.1 · «Homologación»: cada producto, unidad y medio de pago de M-INV con su código del SIN (sin homologar no se
/// factura). Productos: asignar con búsqueda en el catálogo del SIN o «Sugerir» por palabras y aceptar en lote.
/// </summary>
public sealed class HomologationViewModel : PageViewModel
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private List<ProductHomologationItem> _items = [];
    private HomologationView? _view;
    private int _tab;
    private bool _onlyPending;
    private string _search = string.Empty;
    private ProductHomologationItem? _selected;
    private SiatActivityView? _activity;
    private string _summary = string.Empty;

    public HomologationViewModel(AppServices app)
        : base(app, "homologacion", "Homologación", "Productos, unidades y medios de pago con sus códigos del SIN", Glyphs.Tag)
    {
        Products = CollectionViewSource.GetDefaultView(_items);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ApplyFilter();
        };
        Assign = new AsyncRelayCommand(AssignAsync, () => CanEdit && _selected is not null);
        Suggest = new AsyncRelayCommand(SuggestAsync, () => CanEdit && _activity is not null && (_view?.PendingProducts ?? 0) > 0);
        AssignRow = new AsyncRelayCommand<ProductHomologationItem>(async item =>
        {
            Selected = item;
            await AssignAsync();
        }, _ => CanEdit);
    }

    public bool CanEdit => App.Session.Can(PermissionCodes.BillingConfigure);

    /// <summary>0 = productos, 1 = unidades, 2 = medios de pago.</summary>
    public int Tab
    {
        get => _tab;
        set
        {
            if (Set(ref _tab, value))
            {
                OnPropertiesChanged(nameof(IsProductsTab), nameof(IsUnitsTab), nameof(IsMethodsTab));
            }
        }
    }

    public bool IsProductsTab { get => _tab == 0; set { if (value) { Tab = 0; } } }

    public bool IsUnitsTab { get => _tab == 1; set { if (value) { Tab = 1; } } }

    public bool IsMethodsTab { get => _tab == 2; set { if (value) { Tab = 2; } } }

    public ICollectionView Products { get; private set; }

    public BulkObservableCollection<UnitHomologationItem> Units { get; } = [];

    public BulkObservableCollection<PaymentHomologationItem> Methods { get; } = [];

    public IReadOnlyList<SiatActivityView> Activities => _view?.Activities.Where(a => a.IsCurrent).ToList() ?? [];

    /// <summary>Actividad económica con la que «Sugerir» busca en el catálogo del SIN.</summary>
    public SiatActivityView? Activity { get => _activity; set => Set(ref _activity, value); }

    public bool OnlyPending { get => _onlyPending; set { if (Set(ref _onlyPending, value)) { ApplyFilter(); } } }

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? string.Empty))
            {
                _debounce.Stop();
                _debounce.Start();
            }
        }
    }

    public ProductHomologationItem? Selected { get => _selected; set => Set(ref _selected, value); }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public KpiCard PendingKpi { get; } = new("Productos sin homologar", Glyphs.Warning, "Warning", "WarningSoft");

    public KpiCard DoneKpi { get; } = new("Productos homologados", Glyphs.CheckCircle, "Success", "SuccessSoft");

    public KpiCard UnitsKpi { get; } = new("Unidades", Glyphs.Box, "Info", "InfoSoft");

    public KpiCard MethodsKpi { get; } = new("Medios de pago", Glyphs.Money);

    public bool IsEmpty => HasLoaded && Products.IsEmpty;

    public bool NoCatalog => HasLoaded && (_view?.Activities.Count ?? 0) == 0;

    public AsyncRelayCommand Assign { get; }

    public AsyncRelayCommand Suggest { get; }

    public AsyncRelayCommand<ProductHomologationItem> AssignRow { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var view = await App.SendAsync(new GetHomologationQuery());
        _view = view;
        var selected = _selected?.Row.ProductId;
        _items = view.Products.Select(p => new ProductHomologationItem(p)).ToList();
        Products = CollectionViewSource.GetDefaultView(_items);
        Products.Filter = Matches;
        OnPropertyChanged(nameof(Products));
        _selected = _items.FirstOrDefault(i => i.Row.ProductId == selected);
        OnPropertyChanged(nameof(Selected));
        var units = view.SinUnits.Select(u => new Choice<int>($"{u.Code} · {Fmt.SentenceCase(u.Description)}", u.Code)).ToList();
        var methods = view.SinPaymentMethods.Select(m => new Choice<int>($"{m.Code} · {Fmt.SentenceCase(m.Description)}", m.Code)).ToList();
        Units.ReplaceAll(view.Units.Select(u => new UnitHomologationItem(u, units, SaveUnitAsync)));
        Methods.ReplaceAll(view.PaymentMethods.Select(m => new PaymentHomologationItem(m, methods, SaveMethodAsync)));
        _activity = Activities.FirstOrDefault(a => a.Code == _activity?.Code)
                    ?? Activities.FirstOrDefault(a => a.Sectors.Contains(SiatCodes.SectorPurchaseSale)) ?? Activities.FirstOrDefault();
        OnPropertiesChanged(nameof(Activities), nameof(Activity), nameof(NoCatalog));
        var active = view.Products.Count(p => p.IsActive);
        PendingKpi.Value = view.PendingProducts.ToString("N0", Fmt.Culture);
        PendingKpi.Detail = view.PendingProducts == 0 ? "Todos los productos activos se pueden facturar" : "No se facturan hasta homologarlos";
        DoneKpi.Value = (active - view.PendingProducts).ToString("N0", Fmt.Culture);
        DoneKpi.Detail = $"de {active} productos activos";
        UnitsKpi.Value = $"{view.Units.Count(u => u.SinUnitCode is not null)} de {view.Units.Count}";
        UnitsKpi.Detail = view.Units.Any(u => u.SinUnitCode is null) ? $"{view.Units.Count(u => u.SinUnitCode is null)} sin unidad del SIN" : "Todas homologadas";
        MethodsKpi.Value = $"{view.PaymentMethods.Count(m => m.SinCode is not null)} de {view.PaymentMethods.Count}";
        MethodsKpi.Detail = view.PaymentMethods.Any(m => m.SinCode is null) ? "Hay medios de pago sin código del SIN" : "Todos homologados";
        Badge = view.PendingProducts > 0 ? view.PendingProducts.ToString(Fmt.Culture) : null;
        ApplyFilter();
    }

    private bool Matches(object o)
    {
        if (o is not ProductHomologationItem p || (_onlyPending && !(p.IsPending && p.Row.IsActive)))
        {
            return false;
        }
        var q = _search.Trim();
        return q.Length == 0 || p.Row.Sku.Contains(q, StringComparison.OrdinalIgnoreCase)
               || Fmt.Culture.CompareInfo.IndexOf(p.Row.Name, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
               || Fmt.Culture.CompareInfo.IndexOf(p.Row.Category, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
               || (p.Row.SinProductCode?.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.Ordinal) ?? false);
    }

    private void ApplyFilter()
    {
        Products.Refresh();
        var visible = Products.Cast<object>().Count();
        Summary = visible == _items.Count ? $"{_items.Count} productos" : $"{visible} de {_items.Count} productos";
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task AssignAsync()
    {
        if (_selected is not { } item || _view is null)
        {
            return;
        }
        var dialog = new AssignProductDialog(App, item.Row, _view.Activities);
        var opening = dialog.StartAsync();
        var saved = App.Dialogs.ShowAsync(dialog);
        await opening;
        if (await saved)
        {
            App.Notify.Success("Producto homologado", $"{item.Row.Sku} · {item.Row.Name}");
            await LoadAsync(force: true);
        }
    }

    private async Task SuggestAsync()
    {
        var activity = _activity!;
        try
        {
            var suggestions = await App.SendAsync(new SuggestProductHomologationQuery(activity.Code));
            if (suggestions.Count == 0)
            {
                App.Notify.Info("Sin sugerencias", $"Ningún producto pendiente coincide con el catálogo del SIN de la actividad {activity.Code}: asígnelos uno por uno.");
                return;
            }
            var catalog = (await App.SendAsync(new SearchSiatProductsQuery(activity.Code, null, 1000)))
                .GroupBy(p => p.ProductCode).ToDictionary(g => g.Key, g => g.First().Description);
            var names = _items.ToDictionary(i => i.Row.Sku, i => i.Row.Name, StringComparer.OrdinalIgnoreCase);
            var dialog = new SuggestionsDialog(App, activity.Code,
                suggestions.Select(s => new SuggestionItem(s, $"{s.Sku} · {names.GetValueOrDefault(s.Sku, s.Sku)}", catalog.GetValueOrDefault(s.SinProductCode))).ToList());
            if (await App.Dialogs.ShowAsync(dialog))
            {
                App.Notify.Success("Homologación guardada", FiscalText.Plain(dialog.ResultMessage ?? string.Empty));
                await LoadAsync(force: true);
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo sugerir", AppServices.Describe(ex));
        }
    }

    private async Task SaveUnitAsync(UnitHomologationItem item)
    {
        if (!CanEdit)
        {
            App.Notify.Warning("Sin permiso", "Homologar exige el permiso de configurar la facturación.");
            return;
        }
        if (await RunAsync(() => App.SendAsync(new SaveUnitHomologationCommand(item.Row.Code, item.Selected!.Value)), "No se pudo guardar la unidad"))
        {
            App.Notify.Success("Unidad homologada", $"{item.Row.Code} → {item.Selected!.Label}");
            await LoadAsync(force: true);
        }
    }

    private async Task SaveMethodAsync(PaymentHomologationItem item)
    {
        if (!CanEdit)
        {
            App.Notify.Warning("Sin permiso", "Homologar exige el permiso de configurar la facturación.");
            return;
        }
        if (await RunAsync(() => App.SendAsync(new SavePaymentMethodHomologationCommand(item.Row.Code, item.Selected!.Value)), "No se pudo guardar el medio de pago"))
        {
            App.Notify.Success("Medio de pago homologado", $"{item.Row.Name} → {item.Selected!.Label}");
            await LoadAsync(force: true);
        }
    }
}
