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
    private static readonly Choice<string?> AllSuppliers = new("Todos los proveedores", null);

    /// <summary>V6 · Valor del chip «Con reservas» (los demás chips llevan un <see cref="StockStatusCode"/>).</summary>
    public static readonly object ReservedFilter = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private List<StockItem> _items = [];
    private string _search = string.Empty;
    private FilterChip? _statusFilter;
    private object? _pendingStatus;
    private string _category = AllCategories;
    private Choice<string?> _supplier = AllSuppliers;
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
        // V7 · «Limpiar filtros» vuelve a todo (antes «Limpiar la búsqueda» solo borraba el texto y la lista seguía vacía por el chip o la
        // categoría)
        ClearSearch = new RelayCommand(ClearAllFilters);
        ClearFilters = new RelayCommand(ClearAllFilters, () => HasFilters);
        Suppliers.ReplaceAll([AllSuppliers]);
    }

    /// <summary>V7 · Proveedores de los productos (lista desplegable).</summary>
    public BulkObservableCollection<Choice<string?>> Suppliers { get; } = [];

    public Choice<string?> Supplier
    {
        get => _supplier;
        set
        {
            if (Set(ref _supplier, value ?? AllSuppliers))
            {
                ApplyFilter();
            }
        }
    }

    public bool HasFilters => _search.Trim().Length > 0 || _category != AllCategories || _supplier.Value is not null
                              || (_statusFilter is not null && Filters.Count > 0 && !ReferenceEquals(_statusFilter, Filters[0]));

    public RelayCommand ClearFilters { get; }

    private void ClearAllFilters()
    {
        _search = string.Empty;
        _category = AllCategories;
        _supplier = AllSuppliers;
        foreach (var chip in Filters)
        {
            chip.IsSelected = ReferenceEquals(chip, Filters[0]);
        }
        _statusFilter = Filters.FirstOrDefault();
        OnPropertiesChanged(nameof(Search), nameof(Category), nameof(Supplier));
        ApplyFilter();
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
        var reserved = await ReservedStock.LoadAsync(App);   // V6 · reservado por producto (regla S-08)
        _items = rows.Select(r => new StockItem(r) { Image = images.GetValueOrDefault(r.Sku), Reserved = reserved.GetValueOrDefault(r.Sku) }).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = Matches;
        OnPropertyChanged(nameof(Rows));

        // V7 · El estado pedido desde otra pantalla (la leyenda del inicio) antes de la primera carga se perdía: se guarda y se aplica aquí
        var previous = _pendingStatus ?? _statusFilter?.Value;
        _pendingStatus = null;
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
        // V6 · Productos con unidades reservadas (armados web y del escritorio)
        if (_items.Count(i => i.HasReserved) is var withReservations and > 0)
        {
            chips.Add(new FilterChip("Con reservas", ReservedFilter, withReservations, "Brand"));
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
        Suppliers.ReplaceAll(FilterChoices.Of(AllSuppliers.Label, rows.Select(r => r.Supplier)));
        _supplier = FilterChoices.Keep(Suppliers, _supplier);
        OnPropertyChanged(nameof(Supplier));

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
        if (parameter is not StockStatusCode status)
        {
            return;
        }
        if (Filters.FirstOrDefault(f => Equals(f.Value, status)) is { } chip)
        {
            SelectFilter.Execute(chip);
        }
        else
        {
            // Primera visita: los chips todavía no existen; se aplica al terminar de cargar
            _pendingStatus = status;
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
        if (ReferenceEquals(_statusFilter?.Value, ReservedFilter) && !r.HasReserved)
        {
            return false;
        }
        if (_category != AllCategories && r.Category != _category)
        {
            return false;
        }
        if (_supplier.Value is { } supplier && !string.Equals(r.Supplier, supplier, StringComparison.CurrentCultureIgnoreCase))
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
        OnPropertiesChanged(nameof(IsEmpty), nameof(HasFilters));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>V7 · Lo que se exporta: las filas visibles con sus filtros (formato único de <see cref="CsvExport"/>).</summary>
    public CsvTable ExportTable() => CsvTable.Of(
        ["SKU", "Producto", "Categoría", "Proveedor", "Unidad", "Stock", "Reservado", "Disponible", "Mínimo", "Máximo", "Estado", "Salidas 30 d",
            "Cobertura (días)", "Costo unitario", "Valor", "Último movimiento"],
        Rows.Cast<StockItem>(),
        r => [r.Sku, r.Name, r.Category, r.Supplier, r.Unit, r.Row.Stock, r.Reserved, r.Available, r.Row.Minimum, r.Row.Maximum, StockRules.Label(r.Status),
            r.Row.Sales30Days, r.Row.CoverageDays, r.Row.UnitCost, r.Row.InventoryValue, r.Row.LastMovement]);

    private void ExportCsv() => App.ExportCsv(App.CsvName($"stock-{App.Session.Workspace.WarehouseCode}"), "Stock", ExportTable());
}
