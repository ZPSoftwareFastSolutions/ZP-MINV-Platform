using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using MINV.Application.Iam;
using MINV.Application.Inventory.Queries;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Actividad (auditoría inmutable; en la V2.1: 14_ACTIVIDAD): quién hizo qué, cuándo y con qué resultado. V7: usuario,
/// acción y fecha en listas desplegables, «Limpiar filtros» y «Exportar CSV».</summary>
public sealed class ActivityViewModel : PageViewModel
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private List<ActivityItem> _items = [];
    private FilterChip? _filter;
    private string _search = string.Empty;
    private int _visible;
    private Choice<string?> _user;
    private Choice<string?> _action;
    private Choice<PeriodOption?> _period;

    public ActivityViewModel(AppServices app) : base(app, "actividad", "Actividad", "Auditoría: quién hizo qué y cuándo", Glyphs.History)
    {
        Users.ReplaceAll([new Choice<string?>("Todos los usuarios", null)]);
        ActionsFilter.ReplaceAll([new Choice<string?>("Todas las acciones", null)]);
        Periods.ReplaceAll(FilterChoices.Periods(Today));
        _user = Users[0];
        _action = ActionsFilter[0];
        _period = Periods[0];
        Rows = CollectionViewSource.GetDefaultView(_items);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Apply();
        };
        SelectFilter = new RelayCommand<FilterChip>(chip =>
        {
            foreach (var c in Filters)
            {
                c.IsSelected = ReferenceEquals(c, chip);
            }
            _filter = chip;
            Apply();
        });
        Export = new RelayCommand(() => App.ExportCsv(App.CsvName("actividad"), "Actividad", ExportTable()), () => _items.Count > 0);
        ClearFilters = new RelayCommand(() =>
        {
            _search = string.Empty;
            _user = Users[0];
            _action = ActionsFilter[0];
            _period = Periods[0];
            foreach (var c in Filters)
            {
                c.IsSelected = c.Value is null;
            }
            _filter = Filters.FirstOrDefault(c => c.Value is null);
            OnPropertiesChanged(nameof(Search), nameof(User), nameof(ActionChoice), nameof(Period));
            Apply();
        }, () => HasFilters);
    }

    public ICollectionView Rows { get; private set; }

    public BulkObservableCollection<FilterChip> Filters { get; } = [];

    /// <summary>V7 · Quién (las personas que aparecen en la bitácora).</summary>
    public BulkObservableCollection<Choice<string?>> Users { get; } = [];

    /// <summary>V7 · Qué (las acciones que aparecen, con su nombre en español).</summary>
    public BulkObservableCollection<Choice<string?>> ActionsFilter { get; } = [];

    /// <summary>V7 · Cuándo: hoy, ayer, esta semana…</summary>
    public BulkObservableCollection<Choice<PeriodOption?>> Periods { get; } = [];

    public Choice<string?> User { get => _user; set { if (Set(ref _user, value ?? Users[0])) { Apply(); } } }

    public Choice<string?> ActionChoice { get => _action; set { if (Set(ref _action, value ?? ActionsFilter[0])) { Apply(); } } }

    public Choice<PeriodOption?> Period { get => _period; set { if (Set(ref _period, value ?? Periods[0])) { Apply(); } } }

    public bool HasFilters => _search.Trim().Length > 0 || _user.Value is not null || _action.Value is not null || _period.Value is not null
                              || _filter?.Value is not null;

    public string Summary => _visible == _items.Count ? $"{_items.Count} registros" : $"{_visible} de {_items.Count} registros";

    public RelayCommand Export { get; }

    public RelayCommand ClearFilters { get; }

    private DateOnly Today => DateOnly.FromDateTime(App.Now.ToLocalTime().DateTime);

    /// <summary>V7 · Lo que se exporta: los registros visibles con sus filtros.</summary>
    public CsvTable ExportTable() => CsvTable.Of(
        ["Fecha y hora", "Usuario", "Correo", "Acción", "Resultado", "Detalle"],
        Rows.Cast<ActivityItem>(),
        a => [a.Row.OccurredAt, a.UserName, a.Row.UserEmail, a.ActionText, a.OutcomeText, a.Details]);

    private bool Matches(object o)
    {
        if (o is not ActivityItem a || (_filter?.Value is AuditOutcome outcome && a.Outcome != outcome)
            || (_user.Value is { } user && a.UserName != user) || (_action.Value is { } action && a.Row.Action != action))
        {
            return false;
        }
        if (_period.Value is { } period)
        {
            var day = DateOnly.FromDateTime(a.Row.OccurredAt.ToLocalTime().DateTime);
            if (day < period.From || day > period.To)
            {
                return false;
            }
        }
        var q = _search.Trim();
        return q.Length == 0 || FilterChoices.Contains(a.SearchText, q);
    }

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

    public int VisibleCount { get => _visible; private set => Set(ref _visible, value); }

    public bool IsEmpty => HasLoaded && VisibleCount == 0;

    public RelayCommand<FilterChip> SelectFilter { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var now = App.Now;
        var rows = await App.SendAsync(new GetActivityQuery(1000));
        _items = rows.Select(r => new ActivityItem(r, now)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = Matches;
        OnPropertyChanged(nameof(Rows));
        Users.ReplaceAll(FilterChoices.Of("Todos los usuarios", _items.Select(i => i.UserName)));
        _user = FilterChoices.Keep(Users, _user);
        ActionsFilter.ReplaceAll([new Choice<string?>("Todas las acciones", null),
            .. _items.Select(i => i.Row.Action).Distinct(StringComparer.Ordinal).Select(a => new Choice<string?>(Fmt.Action(a), a))
                .OrderBy(c => c.Label, StringComparer.Create(Fmt.Culture, true))]);
        _action = FilterChoices.Keep(ActionsFilter, _action);
        Periods.ReplaceAll(FilterChoices.Periods(Today));
        _period = FilterChoices.Keep(Periods, _period);
        OnPropertiesChanged(nameof(User), nameof(ActionChoice), nameof(Period));
        var previous = _filter?.Value;
        var chips = new List<FilterChip>
        {
            new("Todo", null, _items.Count),
            new("Correctos", AuditOutcome.Succeeded, _items.Count(a => a.Outcome == AuditOutcome.Succeeded), "Success"),
            new("Rechazados", AuditOutcome.Rejected, _items.Count(a => a.Outcome == AuditOutcome.Rejected), "Warning"),
            new("Con error", AuditOutcome.Failed, _items.Count(a => a.Outcome == AuditOutcome.Failed), "Danger"),
        };
        Filters.ReplaceAll(chips);
        _filter = chips.FirstOrDefault(c => Equals(c.Value, previous)) ?? chips[0];
        _filter.IsSelected = true;
        Apply();
    }

    private void Apply()
    {
        Rows.Refresh();
        VisibleCount = Rows.Cast<object>().Count();
        OnPropertiesChanged(nameof(IsEmpty), nameof(HasFilters), nameof(Summary));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
}

/// <summary>
/// Ficha de un producto en el panel lateral (en la V1: 17_KARDEX): semáforo, existencias por posición, gráfico del saldo
/// y kardex con saldo acumulado.
/// </summary>
public sealed class ProductDetailViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly string _sku;
    private ProductCard? _card;
    private System.Windows.Media.ImageSource? _image;
    private bool _loading = true;
    private string? _error;

    public ProductDetailViewModel(AppServices app, string sku)
    {
        _app = app;
        _sku = sku;
        RegisterEntry = new RelayCommand(() => Go(MovementTypeCodes.Receipt), () => CanRegisterEntries && _card is not null);
        RegisterIssue = new RelayCommand(() => Go(MovementTypeCodes.Issue), () => CanRegisterIssues && _card is not null);
        CopySku = new RelayCommand(() =>
        {
            if (ClipboardText.TrySet(_card?.Sku ?? _sku))
            {
                _app.Notify.Info("SKU copiado", _card?.Sku ?? _sku);
            }
        });
        Refresh = new AsyncRelayCommand(LoadAsync);
    }

    public ProductCard? Card
    {
        get => _card;
        private set
        {
            if (Set(ref _card, value))
            {
                OnPropertyChanged(string.Empty);
            }
        }
    }

    public bool IsLoading
    {
        get => _loading;
        private set => Set(ref _loading, value);
    }

    /// <summary>Imagen del producto (tamaño de ficha) o null.</summary>
    public System.Windows.Media.ImageSource? Image
    {
        get => _image;
        private set
        {
            if (Set(ref _image, value))
            {
                OnPropertyChanged(nameof(HasImage));
            }
        }
    }

    public bool HasImage => _image is not null;

    public bool CanEditCatalog => _app.Session.Can(PermissionCodes.CatalogManage);

    public RelayCommand EditInCatalog => new(() => _app.Navigator.Navigate("catalogo", Sku));

    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    public string Sku => _card?.Sku ?? _sku;

    public string Name => _card?.Name ?? "Cargando…";

    public string Category => _card?.Category ?? "";

    public string Unit => _card?.Unit ?? "";

    public StockStatusCode Status => _card?.Status ?? StockStatusCode.Optimal;

    public string OnHandText => _card is { } c ? Fmt.Qty(c.OnHand) : "—";

    public string AvailableText => _card is { } c ? Fmt.Qty(c.Available, c.Unit) : "—";

    public string ReservedText => _card is { } c ? Fmt.Qty(c.Reserved, c.Unit) : "—";

    public string MinMaxText => _card is { } c ? $"{Fmt.Qty(c.Minimum)} / {Fmt.Qty(c.Maximum)}" : "—";

    public string CostText => _card is { } c ? Fmt.Money(c.UnitCost) : "—";

    public string ValueText => _card is { } c ? Fmt.Money(Math.Max(0, c.OnHand) * c.UnitCost) : "—";

    public string SupplierText => string.IsNullOrWhiteSpace(_card?.Supplier) ? "Sin proveedor preferido" : _card!.Supplier!;

    public string BarcodesText => _card is { Barcodes.Count: > 0 } c ? string.Join(" · ", c.Barcodes) : "Sin código de barras";

    public string PrimaryBinText => _card?.PrimaryBin ?? "Sin posición asignada";

    public double Level => _card is { Maximum: > 0 } c ? (double)Math.Clamp(c.OnHand / c.Maximum, 0, 1) : _card?.OnHand > 0 ? 1 : 0;

    public double MinimumReference => (double)(_card?.Minimum ?? 0);

    public IReadOnlyList<ProductBinStock> Bins => _card?.Bins ?? [];

    public IReadOnlyList<KardexItem> Kardex => _card?.Movements.Select(m => new KardexItem(m)).ToList() ?? [];

    /// <summary>Saldo después de cada movimiento, del más antiguo al más reciente (gráfico).</summary>
    public IReadOnlyList<double> Balances => _card?.Movements.Reverse().Select(m => (double)m.Balance).ToList() ?? [];

    public string MovementsText => _card is { } c
        ? c.TotalMovements == c.Movements.Count ? $"{c.TotalMovements} movimientos" : $"Últimos {c.Movements.Count} de {c.TotalMovements} movimientos"
        : "";

    public bool HasMovements => _card is { Movements.Count: > 0 };

    public bool CanRegisterEntries => _app.Session.Can(PermissionCodes.MovementsRegisterWarehouse);

    public bool CanRegisterIssues => _app.Session.Can(PermissionCodes.MovementsRegisterSales);

    public RelayCommand RegisterEntry { get; }

    public RelayCommand RegisterIssue { get; }

    public RelayCommand CopySku { get; }

    public AsyncRelayCommand Refresh { get; }

    public async Task LoadAsync()
    {
        IsLoading = true;
        Error = null;
        try
        {
            Card = await _app.SendAsync(new GetProductCardQuery(_sku, 300));
            Image = await _app.Images.LargeAsync(Card.VariantId);
        }
        catch (Exception ex)
        {
            Error = AppServices.Describe(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Go(string typeCode) => _app.Navigator.Navigate("registro", new MovementPrefill(Sku, typeCode));
}
