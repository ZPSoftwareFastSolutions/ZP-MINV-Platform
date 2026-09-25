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

/// <summary>Actividad (auditoría inmutable; en la V2.1: 14_ACTIVIDAD): quién hizo qué, cuándo y con qué resultado.</summary>
public sealed class ActivityViewModel : PageViewModel
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private List<ActivityItem> _items = [];
    private FilterChip? _filter;
    private string _search = string.Empty;
    private int _visible;

    public ActivityViewModel(AppServices app) : base(app, "actividad", "Actividad", "Auditoría: quién hizo qué y cuándo", Glyphs.History)
    {
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
    }

    public ICollectionView Rows { get; private set; }

    public BulkObservableCollection<FilterChip> Filters { get; } = [];

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
        Rows.Filter = o => o is ActivityItem a
                           && (_filter?.Value is not AuditOutcome outcome || a.Outcome == outcome)
                           && (_search.Trim().Length == 0 || Fmt.Culture.CompareInfo.IndexOf(a.SearchText, _search.Trim(),
                               System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0);
        OnPropertyChanged(nameof(Rows));
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
        OnPropertyChanged(nameof(IsEmpty));
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
