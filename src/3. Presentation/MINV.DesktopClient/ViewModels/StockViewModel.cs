using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.ViewModels;

/// <summary>
/// Stock al instante (en la V2.1: 15_STOCK, que había que «Recalcular»): búsqueda, filtros por estado y categoría,
/// orden por columna, exportación y acceso directo a la ficha o al registro. La tabla es virtualizada (100.000+ filas).
/// </summary>
public sealed class StockViewModel : PageViewModel
{
    private const string AllCategories = "Todas las categorías";
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private List<StockItem> _items = [];
    private string _search = string.Empty;
    private FilterChip? _statusFilter;
    private string _category = AllCategories;
    private string _summary = string.Empty;
    private int _visible;
    private bool _isGallery = true;

    public StockViewModel(AppServices app) : base(app, "stock", "Stock", "Existencias al instante, sin recalcular", Glyphs.Box)
    {
        Rows = CollectionViewSource.GetDefaultView(_items);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ApplyFilter();
        };
        SelectFilter = new RelayCommand<FilterChip>(chip =>
        {
            foreach (var c in Filters)
            {
                c.IsSelected = ReferenceEquals(c, chip);
            }
            _statusFilter = chip;
            ApplyFilter();
        });
        OpenProduct = new RelayCommand<StockItem>(i => app.Navigator.OpenProduct(i.Sku));
        RegisterFor = new RelayCommand<StockItem>(i => app.Navigator.Navigate("registro", new MovementPrefill(i.Sku, null)));
        Export = new RelayCommand(ExportCsv, () => _items.Count > 0);
        ClearSearch = new RelayCommand(() => Search = string.Empty);
    }

    /// <summary>Vista filtrable y ordenable (la tabla virtualiza: solo dibuja las filas visibles).</summary>
    public ICollectionView Rows { get; private set; }

    public BulkObservableCollection<FilterChip> Filters { get; } = [];

    public BulkObservableCollection<string> Categories { get; } = [];

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

    public string Category
    {
        get => _category;
        set
        {
            if (Set(ref _category, value ?? AllCategories))
            {
                ApplyFilter();
            }
        }
    }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    /// <summary>Galería (tarjetas con la imagen de cada producto) o tabla.</summary>
    public bool IsGallery
    {
        get => _isGallery;
        set
        {
            if (Set(ref _isGallery, value))
            {
                OnPropertyChanged(nameof(IsList));
            }
        }
    }

    public bool IsList
    {
        get => !_isGallery;
        set => IsGallery = !value;
    }

    public int VisibleCount { get => _visible; private set => Set(ref _visible, value); }

    public bool IsEmpty => HasLoaded && VisibleCount == 0;

    public bool CanRegister => App.Session.Can(PermissionCodes.MovementsRegisterWarehouse) || App.Session.Can(PermissionCodes.MovementsRegisterSales);

    public string TotalValueText { get; private set; } = "—";

    public string TotalUnitsText { get; private set; } = "—";

    public string AlertsText { get; private set; } = "—";

    public string ProductsText { get; private set; } = "—";

    public RelayCommand<FilterChip> SelectFilter { get; }

    public RelayCommand<StockItem> OpenProduct { get; }

    public RelayCommand<StockItem> RegisterFor { get; }

    public RelayCommand Export { get; }

    public RelayCommand ClearSearch { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var view = await App.Data.ProjectionAsync(force);
        var rows = view.Result.Stock;
        var images = await App.Images.AllAsync(force);
        _items = rows.Select(r => new StockItem(r) { Image = images.GetValueOrDefault(r.Sku) }).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = Matches;
        OnPropertyChanged(nameof(Rows));

        var previous = _statusFilter?.Value;
        var chips = new List<FilterChip> { new("Todos", null, rows.Count) };
        foreach (var status in new[]
                 {
                     StockStatusCode.OutOfStock, StockStatusCode.Critical, StockStatusCode.Low, StockStatusCode.Optimal,
                     StockStatusCode.Overstock, StockStatusCode.Inconsistent, StockStatusCode.Inactive,
                 })
        {
            var n = rows.Count(r => r.Status == status);
            if (n > 0)
            {
                chips.Add(new FilterChip(Fmt.StatusName(status), status, n, Fmt.StatusBrushKey(status)));
            }
        }
        Filters.ReplaceAll(chips);
        _statusFilter = chips.FirstOrDefault(c => Equals(c.Value, previous)) ?? chips[0];
        _statusFilter.IsSelected = true;
        Categories.ReplaceAll(new[] { AllCategories }.Concat(rows.Select(r => r.Category).Distinct().Order(StringComparer.Create(Fmt.Culture, true))));
        if (!Categories.Contains(_category))
        {
            _category = AllCategories;
        }
        OnPropertyChanged(nameof(Category));

        TotalValueText = Fmt.Money(rows.Sum(r => r.InventoryValue));
        ProductsText = $"{rows.Count(r => r.IsActive)} activos de {rows.Count}";
        AlertsText = $"{view.Result.Alerts.Count} en alerta";
        TotalUnitsText = $"{view.Result.Movements:N0} movimientos procesados";
        OnPropertiesChanged(nameof(TotalValueText), nameof(ProductsText), nameof(AlertsText), nameof(TotalUnitsText));
        Subtitle = $"{view.WarehouseCode} · calculado al instante el {Fmt.Date(view.Today)}";
        OnPropertyChanged(nameof(Subtitle));
        ApplyFilter();
    }

    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is StockStatusCode status && Filters.FirstOrDefault(f => Equals(f.Value, status)) is { } chip)
        {
            SelectFilter.Execute(chip);
        }
    }

    private bool Matches(object o)
    {
        if (o is not StockItem r)
        {
            return false;
        }
        if (_statusFilter?.Value is StockStatusCode status && r.Status != status)
        {
            return false;
        }
        if (_category != AllCategories && r.Category != _category)
        {
            return false;
        }
        var q = _search.Trim();
        return q.Length == 0
               || r.Sku.Contains(q, StringComparison.OrdinalIgnoreCase)
               || Fmt.Culture.CompareInfo.IndexOf(r.Name, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
               || Fmt.Culture.CompareInfo.IndexOf(r.Supplier, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        VisibleCount = Rows.Cast<object>().Count();
        Summary = VisibleCount == _items.Count ? $"{_items.Count} productos" : $"{VisibleCount} de {_items.Count} productos";
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void ExportCsv()
    {
        var rows = Rows.Cast<StockItem>().ToList();
        var path = FileDialogs.SaveCsv($"stock-{App.Session.Workspace.WarehouseCode}-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        if (path is null)
        {
            return;
        }
        Csv.Write(path, ["SKU", "Producto", "Categoría", "Proveedor", "Unidad", "Stock", "Mínimo", "Máximo", "Estado", "Salidas 30 d",
                "Cobertura (días)", "Costo unitario", "Valor", "Último movimiento"],
            rows.Select(r => new object?[]
            {
                r.Sku, r.Name, r.Category, r.Supplier, r.Unit, r.Row.Stock, r.Row.Minimum, r.Row.Maximum, StockRules.Label(r.Status),
                r.Row.Sales30Days, r.Row.CoverageDays, r.Row.UnitCost, r.Row.InventoryValue, r.Row.LastMovement,
            }));
        App.Notify.Success("Stock exportado", $"{rows.Count} filas en {System.IO.Path.GetFileName(path)}");
    }
}
