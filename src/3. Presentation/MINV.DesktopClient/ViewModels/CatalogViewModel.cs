using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MINV.Application.Catalog;
using MINV.Application.Inventory.Queries;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Producto del catálogo en la galería (imagen, precio, margen y stock actual).</summary>
public sealed class CatalogProduct(CatalogItem item, StockRow? stock, ImageSource? image, decimal taxRate) : ObservableObject
{
    public CatalogItem Item { get; } = item;

    public string Sku => Item.Sku;

    public string Name => Item.Name;

    public string Category => Item.Category;

    public string Supplier => Item.Supplier ?? "Sin proveedor";

    public string Unit => Item.Unit;

    public ImageSource? Image { get; } = image;

    public bool HasImage => Image is not null;

    public decimal Price => Item.SalePrice;

    public string PriceText => Item.SalePrice > 0 ? Fmt.Money(Item.SalePrice) : "Sin precio";

    public string CostText => Fmt.Money(Item.UnitCost);

    /// <summary>Margen bruto sobre el precio sin impuesto (el precio de venta incluye el IVA).</summary>
    public decimal Margin => Item.SalePrice <= 0 ? 0 : (Item.SalePrice * 100 / (100 + taxRate) - Item.UnitCost) / (Item.SalePrice * 100 / (100 + taxRate));

    public string MarginText => Item.SalePrice <= 0 ? "—" : Margin.ToString("P0", Fmt.Culture);

    public bool LowMargin => Item.SalePrice > 0 && Margin < 0.15m;

    public decimal Stock => stock?.Stock ?? 0;

    public string StockText => $"{Fmt.Qty(Stock)} {Item.Unit}";

    public StockStatusCode Status => stock?.Status ?? (Item.IsActive ? StockStatusCode.OutOfStock : StockStatusCode.Inactive);

    public double Level => (double)(stock?.Level ?? (Stock > 0 ? 1m : 0m));

    public string BinText => Item.BinCode ?? "—";

    public bool IsActive => Item.IsActive;
}

/// <summary>
/// Catálogo de productos con imágenes: galería o lista, filtros por categoría, estado y orden, y un editor lateral
/// (categoría, unidad, proveedor y posición se eligen de combos; precio, costo, mínimo, máximo, código de barras e imagen).
/// Editar exige el permiso de catálogo; los demás roles lo consultan.
/// </summary>
public sealed class CatalogViewModel : PageViewModel
{
    private static readonly Choice<string?> AllCategories = new("Todas las categorías", null);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private List<CatalogProduct> _items = [];
    private string _search = string.Empty;
    private Choice<string?> _category = AllCategories;
    private Choice<string> _state;
    private Choice<string> _sort;
    private bool _isGallery = true;
    private string _summary = string.Empty;
    private int _visible;
    private decimal _taxRate = 13;
    private CatalogEditor? _editor;

    public CatalogViewModel(AppServices app) : base(app, "catalogo", "Catálogo", "Productos, precios e imágenes", Glyphs.Tag)
    {
        States =
        [
            new("Todos los productos", "all"), new("Solo activos", "active"), new("Solo inactivos", "inactive"),
            new("Sin imagen", "noimage"), new("Con alerta de stock", "alert"), new("Margen bajo (< 15 %)", "lowmargin"),
        ];
        Sorts =
        [
            new("Nombre (A → Z)", "name"), new("Precio: mayor a menor", "price-desc"), new("Precio: menor a mayor", "price-asc"),
            new("Stock: mayor a menor", "stock-desc"), new("Margen: mayor a menor", "margin-desc"), new("Categoría", "category"),
        ];
        _state = States[1];
        _sort = Sorts[0];
        Rows = CollectionViewSource.GetDefaultView(_items);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ApplyFilter();
        };
        New = new AsyncRelayCommand(() => OpenEditorAsync(null), () => CanEdit);
        Edit = new AsyncRelayCommand<CatalogProduct>(p => OpenEditorAsync(p));
        OpenProduct = new RelayCommand<CatalogProduct>(p => app.Navigator.OpenProduct(p.Sku));
        ClearSearch = new RelayCommand(() =>
        {
            Search = string.Empty;
            Category = AllCategories;
            State = States[0];
        });
        Export = new RelayCommand(ExportCsv, () => _items.Count > 0);
        App.Images.Changed += (_, _) => _ = LoadAsync(force: false);
    }

    public ICollectionView Rows { get; private set; }

    public BulkObservableCollection<Choice<string?>> Categories { get; } = [];

    public IReadOnlyList<Choice<string>> States { get; }

    public IReadOnlyList<Choice<string>> Sorts { get; }

    public bool CanEdit => App.Session.Can(PermissionCodes.CatalogManage);

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

    public Choice<string?> Category
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

    public Choice<string> State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value ?? States[0]))
            {
                ApplyFilter();
            }
        }
    }

    public Choice<string> Sort
    {
        get => _sort;
        set
        {
            if (Set(ref _sort, value ?? Sorts[0]))
            {
                ApplySort();
            }
        }
    }

    /// <summary>Galería (tarjetas con imagen) o lista (tabla).</summary>
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

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public bool IsEmpty => HasLoaded && _visible == 0;

    public KpiCard ProductsKpi { get; } = new("Productos", Glyphs.Tag);

    public KpiCard ImagesKpi { get; } = new("Con imagen", Glyphs.Sparkle, "Info", "InfoSoft");

    public KpiCard PriceKpi { get; } = new("Precio promedio", Glyphs.Money, "Success", "SuccessSoft");

    public KpiCard MarginKpi { get; } = new("Margen promedio", Glyphs.Chart, "Warning", "WarningSoft");

    /// <summary>Editor abierto (nuevo o existente) o null.</summary>
    public CatalogEditor? Editor
    {
        get => _editor;
        private set
        {
            if (Set(ref _editor, value))
            {
                OnPropertyChanged(nameof(IsEditing));
            }
        }
    }

    public bool IsEditing => _editor is not null;

    public AsyncRelayCommand New { get; }

    public AsyncRelayCommand<CatalogProduct> Edit { get; }

    public RelayCommand<CatalogProduct> OpenProduct { get; }

    public RelayCommand ClearSearch { get; }

    public RelayCommand Export { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var options = await App.SendAsync(new GetCatalogOptionsQuery());
        _taxRate = options.TaxRate;
        var catalog = await App.SendAsync(new GetCatalogQuery());
        var projection = await App.Data.ProjectionAsync(force);
        var stock = projection.Result.Stock.ToDictionary(s => s.Sku, StringComparer.OrdinalIgnoreCase);
        var images = await App.Images.AllAsync(force);
        _items = catalog.Select(c => new CatalogProduct(c, stock.GetValueOrDefault(c.Sku), images.GetValueOrDefault(c.Sku), _taxRate)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = Matches;
        OnPropertyChanged(nameof(Rows));

        var previous = _category.Value;
        Categories.ReplaceAll(new[] { AllCategories }.Concat(options.Categories.Select(c =>
            new Choice<string?>($"{c.Name} ({_items.Count(i => i.Item.CategoryCode == c.Code)})", c.Code))));
        _category = Categories.FirstOrDefault(c => c.Value == previous) ?? AllCategories;
        OnPropertyChanged(nameof(Category));

        var active = _items.Where(i => i.IsActive).ToList();
        ProductsKpi.Value = $"{active.Count} activos";
        ProductsKpi.Detail = $"{_items.Count} en total · {options.Categories.Count} categorías";
        ImagesKpi.Value = $"{_items.Count(i => i.HasImage)} de {_items.Count}";
        ImagesKpi.Detail = _items.Any(i => !i.HasImage) ? $"{_items.Count(i => !i.HasImage)} sin imagen" : "Todos tienen imagen";
        var priced = active.Where(i => i.Price > 0).ToList();
        PriceKpi.Value = priced.Count > 0 ? Fmt.Money(priced.Average(i => i.Price)) : "—";
        PriceKpi.Detail = $"Lista {options.PriceListName} · IVA {options.TaxRate:0.##} % incluido";
        MarginKpi.Value = priced.Count > 0 ? priced.Average(i => i.Margin).ToString("P1", Fmt.Culture) : "—";
        MarginKpi.Detail = $"{priced.Count(i => i.LowMargin)} con margen bajo";
        ApplySort();
    }

    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is string sku && CanEdit)
        {
            _ = OpenEditorForSkuAsync(sku);
        }
    }

    public void CloseEditor() => Editor = null;

    private async Task OpenEditorForSkuAsync(string sku)
    {
        await EnsureLoadedAsync();
        if (_items.FirstOrDefault(i => i.Sku.Equals(sku, StringComparison.OrdinalIgnoreCase)) is { } product)
        {
            await OpenEditorAsync(product);
        }
    }

    private async Task OpenEditorAsync(CatalogProduct? product)
    {
        if (!CanEdit)
        {
            App.Navigator.OpenProduct(product!.Sku);
            return;
        }
        try
        {
            var options = await App.SendAsync(new GetCatalogOptionsQuery());
            var bins = await App.SendAsync(new GetBinsQuery());
            var image = product?.Item.HasImage == true ? await App.Images.LargeAsync(product.Item.VariantId) : null;
            Editor = new CatalogEditor(this, App, options, bins.Select(b => b.Code).ToList(), product?.Item, image);
        }
        catch (Exception ex)
        {
            App.Notify.Error("No se pudo abrir el editor", AppServices.Describe(ex));
        }
    }

    /// <summary>Después de guardar: todo lo que muestra productos se vuelve a leer (stock, búsqueda, imágenes).</summary>
    internal async Task AfterSaveAsync(string sku)
    {
        Editor = null;
        await App.Data.LookupAsync(force: true);
        App.Data.Invalidate();
        App.Images.Invalidate();
        await LoadAsync(force: true);
        Search = string.Empty;
        if (_items.FirstOrDefault(i => i.Sku == sku) is { } saved)
        {
            Rows.MoveCurrentTo(saved);
        }
    }

    private bool Matches(object o)
    {
        if (o is not CatalogProduct p)
        {
            return false;
        }
        if (_category.Value is { } code && p.Item.CategoryCode != code)
        {
            return false;
        }
        var ok = _state.Value switch
        {
            "active" => p.IsActive,
            "inactive" => !p.IsActive,
            "noimage" => !p.HasImage,
            "alert" => p.IsActive && p.Status is StockStatusCode.OutOfStock or StockStatusCode.Critical or StockStatusCode.Low,
            "lowmargin" => p.LowMargin,
            _ => true,
        };
        if (!ok)
        {
            return false;
        }
        var q = _search.Trim();
        return q.Length == 0
               || p.Sku.Contains(q, StringComparison.OrdinalIgnoreCase)
               || (p.Item.Barcode?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
               || Fmt.Culture.CompareInfo.IndexOf(p.Name, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
               || Fmt.Culture.CompareInfo.IndexOf(p.Supplier, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    private void ApplySort()
    {
        using (Rows.DeferRefresh())
        {
            Rows.SortDescriptions.Clear();
            switch (_sort.Value)
            {
                case "price-desc":
                    Rows.SortDescriptions.Add(new SortDescription(nameof(CatalogProduct.Price), ListSortDirection.Descending));
                    break;
                case "price-asc":
                    Rows.SortDescriptions.Add(new SortDescription(nameof(CatalogProduct.Price), ListSortDirection.Ascending));
                    break;
                case "stock-desc":
                    Rows.SortDescriptions.Add(new SortDescription(nameof(CatalogProduct.Stock), ListSortDirection.Descending));
                    break;
                case "margin-desc":
                    Rows.SortDescriptions.Add(new SortDescription(nameof(CatalogProduct.Margin), ListSortDirection.Descending));
                    break;
                case "category":
                    Rows.SortDescriptions.Add(new SortDescription(nameof(CatalogProduct.Category), ListSortDirection.Ascending));
                    Rows.SortDescriptions.Add(new SortDescription(nameof(CatalogProduct.Name), ListSortDirection.Ascending));
                    break;
                default:
                    Rows.SortDescriptions.Add(new SortDescription(nameof(CatalogProduct.Name), ListSortDirection.Ascending));
                    break;
            }
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        _visible = Rows.Cast<object>().Count();
        Summary = _visible == _items.Count ? $"{_items.Count} productos" : $"{_visible} de {_items.Count} productos";
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void ExportCsv()
    {
        var rows = Rows.Cast<CatalogProduct>().ToList();
        var path = FileDialogs.SaveCsv($"catalogo-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        if (path is null)
        {
            return;
        }
        Csv.Write(path, ["SKU", "Producto", "Categoría", "Unidad", "Proveedor", "Costo", "Precio (IVA incl.)", "Margen", "Mínimo", "Máximo",
                "Stock", "Posición", "Código de barras", "Activo", "Imagen"],
            rows.Select(r => new object?[]
            {
                r.Sku, r.Name, r.Category, r.Unit, r.Supplier, r.Item.UnitCost, r.Item.SalePrice, decimal.Round(r.Margin, 4), r.Item.Minimum,
                r.Item.Maximum, r.Stock, r.Item.BinCode, r.Item.Barcode, r.IsActive ? "SÍ" : "NO", r.HasImage ? "SÍ" : "NO",
            }));
        App.Notify.Success("Catálogo exportado", $"{rows.Count} productos en {Path.GetFileName(path)}");
    }
}

/// <summary>Formulario de alta o edición de un producto (combos para todo lo que viene de una lista).</summary>
public sealed class CatalogEditor : ObservableObject
{
    private static readonly Choice<string?> NoSupplier = new("(sin proveedor)", null);
    private readonly CatalogViewModel _owner;
    private readonly AppServices _app;
    private readonly string? _originalSku;
    private readonly decimal _taxRate;
    private string _sku;
    private string _name;
    private string _description;
    private OptionItem? _category;
    private UnitOption? _unit;
    private Choice<string?> _supplier;
    private string? _bin;
    private string _cost;
    private string _price;
    private string _minimum;
    private string _maximum;
    private string _barcode;
    private bool _isActive;
    private ImageSource? _image;
    private PickedImage? _pendingImage;
    private bool _removeImage;
    private string? _error;

    public CatalogEditor(CatalogViewModel owner, AppServices app, CatalogOptions options, IReadOnlyList<string> bins, CatalogItem? item, ImageSource? image)
    {
        _owner = owner;
        _app = app;
        _originalSku = item?.Sku;
        _taxRate = options.TaxRate;
        Categories = [.. options.Categories];
        Units = options.Units;
        Suppliers = [NoSupplier, .. options.Suppliers.Select(s => new Choice<string?>(s.Name, s.Code))];
        Bins = bins;
        _sku = item?.Sku ?? string.Empty;
        _name = item?.Name ?? string.Empty;
        _description = item?.Description ?? string.Empty;
        _category = Categories.FirstOrDefault(c => c.Code == item?.CategoryCode) ?? Categories.FirstOrDefault();
        _unit = Units.FirstOrDefault(u => u.Code == item?.Unit) ?? Units.FirstOrDefault(u => u.Code == "UND") ?? Units.FirstOrDefault();
        _supplier = Suppliers.FirstOrDefault(s => s.Value == item?.SupplierCode) ?? NoSupplier;
        _bin = item?.BinCode;
        _cost = item is null ? string.Empty : Numbers.Plain(item.UnitCost);
        _price = item is null ? string.Empty : Numbers.Plain(item.SalePrice);
        _minimum = item is null ? "0" : Numbers.Plain(item.Minimum);
        _maximum = item is null ? "0" : Numbers.Plain(item.Maximum);
        _barcode = item?.Barcode ?? string.Empty;
        _isActive = item?.IsActive ?? true;
        _image = image;
        MarginPresets =
        [
            new("Aplicar margen…", 0m), new("Margen 20 %", 0.20m), new("Margen 25 %", 0.25m), new("Margen 30 %", 0.30m),
            new("Margen 35 %", 0.35m), new("Margen 40 %", 0.40m), new("Margen 50 %", 0.50m),
        ];
        Save = new AsyncRelayCommand(SaveAsync);
        Cancel = new RelayCommand(owner.CloseEditor);
        PickImage = new RelayCommand(OnPickImage);
        RemoveImage = new RelayCommand(() =>
        {
            _pendingImage = null;
            _removeImage = true;
            Image = null;
        }, () => Image is not null);
        NewCategory = new AsyncRelayCommand(NewCategoryAsync);
        GenerateBarcode = new RelayCommand(() => Barcode = NewEan13());
    }

    public bool IsNew => _originalSku is null;

    public string Heading => IsNew ? "Nuevo producto" : "Editar producto";

    public string SaveText => IsNew ? "Crear producto" : "Guardar cambios";

    public BulkObservableCollection<OptionItem> Categories { get; }

    public IReadOnlyList<UnitOption> Units { get; }

    public IReadOnlyList<Choice<string?>> Suppliers { get; }

    public IReadOnlyList<string> Bins { get; }

    public IReadOnlyList<Choice<decimal>> MarginPresets { get; }

    public string Sku { get => _sku; set => Set(ref _sku, value ?? string.Empty); }

    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }

    public string Description { get => _description; set => Set(ref _description, value ?? string.Empty); }

    public OptionItem? Category { get => _category; set => Set(ref _category, value); }

    public UnitOption? Unit { get => _unit; set => Set(ref _unit, value); }

    public Choice<string?> Supplier { get => _supplier; set => Set(ref _supplier, value ?? NoSupplier); }

    public string? Bin { get => _bin; set => Set(ref _bin, value); }

    public string Cost
    {
        get => _cost;
        set
        {
            if (Set(ref _cost, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(MarginText));
            }
        }
    }

    public string Price
    {
        get => _price;
        set
        {
            if (Set(ref _price, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(MarginText));
                OnPropertyChanged(nameof(NetPriceText));
            }
        }
    }

    public string Minimum { get => _minimum; set => Set(ref _minimum, value ?? string.Empty); }

    public string Maximum { get => _maximum; set => Set(ref _maximum, value ?? string.Empty); }

    public string Barcode { get => _barcode; set => Set(ref _barcode, value ?? string.Empty); }

    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    public ImageSource? Image
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

    /// <summary>Elegir un margen del combo calcula el precio de venta (con IVA) a partir del costo.</summary>
    public Choice<decimal>? MarginPreset
    {
        get => null;
        set
        {
            if (value is { Value: > 0 } preset && Numbers.TryParse(_cost, out var cost) && cost > 0)
            {
                var net = cost / (1 - preset.Value);
                Price = Numbers.Plain(decimal.Round(net * (100 + _taxRate) / 100, 1, MidpointRounding.AwayFromZero));
            }
            OnPropertyChanged();
        }
    }

    public string MarginText
    {
        get
        {
            if (!Numbers.TryParse(_cost, out var cost) || !Numbers.TryParse(_price, out var price) || price <= 0)
            {
                return "Indique costo y precio para ver el margen.";
            }
            var net = price * 100 / (100 + _taxRate);
            var margin = (net - cost) / net;
            return $"Margen {margin.ToString("P1", Fmt.Culture)} · ganancia {Fmt.Money(net - cost)} por unidad (sin IVA)";
        }
    }

    public string NetPriceText => Numbers.TryParse(_price, out var price) && price > 0
        ? $"Sin IVA: {Fmt.Money(price * 100 / (100 + _taxRate))} · IVA {Fmt.Money(price * _taxRate / (100 + _taxRate))}"
        : $"El precio incluye el IVA ({_taxRate:0.##} %).";

    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    public AsyncRelayCommand Save { get; }

    public RelayCommand Cancel { get; }

    public RelayCommand PickImage { get; }

    public RelayCommand RemoveImage { get; }

    public AsyncRelayCommand NewCategory { get; }

    public RelayCommand GenerateBarcode { get; }

    private void OnPickImage()
    {
        try
        {
            if (ImageFiles.Pick() is { } picked)
            {
                _pendingImage = picked;
                _removeImage = false;
                Image = picked.Preview;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _app.Notify.Warning("Imagen no válida", ex.Message);
        }
    }

    private async Task NewCategoryAsync()
    {
        var name = await _app.Dialogs.PromptAsync("Nueva categoría", "La categoría queda disponible para todos los productos.", "Nombre de la categoría",
            ["Ferretería", "Eléctricos", "Plomería", "Pinturas", "Herramientas", "Jardinería", "Limpieza", "Seguridad"], "Crear categoría",
            placeholder: "Ej.: Jardinería", glyph: Glyphs.Tag);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        var baseCode = new string(name.Normalize(System.Text.NormalizationForm.FormD).Where(char.IsAsciiLetter).ToArray()).ToUpperInvariant();
        baseCode = baseCode.Length >= 3 ? baseCode[..3] : (baseCode + "CAT")[..3];
        var code = baseCode;
        for (var i = 2; Categories.Any(c => c.Code == code); i++)
        {
            code = baseCode + i.ToString(CultureInfo.InvariantCulture);
        }
        try
        {
            await _app.SendAsync(new SaveCategoryCommand(code, name.Trim()));
            var option = new OptionItem(code, name.Trim());
            Categories.ReplaceAll(Categories.Append(option).OrderBy(c => c.Name, StringComparer.Create(Fmt.Culture, true)));
            Category = option;
            _app.Notify.Success("Categoría creada", $"{name.Trim()} ({code})");
        }
        catch (Exception ex)
        {
            _app.Notify.Error("No se pudo crear la categoría", AppServices.Describe(ex));
        }
    }

    private async Task SaveAsync()
    {
        Error = null;
        if (!Numbers.TryParse(_cost, out var cost, emptyIsZero: true) || !Numbers.TryParse(_price, out var price, emptyIsZero: true)
            || !Numbers.TryParse(_minimum, out var min, emptyIsZero: true) || !Numbers.TryParse(_maximum, out var max, emptyIsZero: true))
        {
            Error = "Revise los números: costo, precio, mínimo y máximo aceptan coma o punto decimal.";
            return;
        }
        if (_category is null || _unit is null)
        {
            Error = "Elija la categoría y la unidad.";
            return;
        }
        try
        {
            var sku = await _app.SendAsync(new SaveProductCommand(_originalSku, _sku.Trim(), _name.Trim(), string.IsNullOrWhiteSpace(_description) ? null : _description.Trim(),
                _category.Code, _unit.Code, _supplier.Value, min, max, cost, price, string.IsNullOrWhiteSpace(_barcode) ? null : _barcode.Trim(), _isActive,
                string.IsNullOrWhiteSpace(_bin) ? null : _bin.Trim()));
            if (_pendingImage is { } image)
            {
                await _app.SendAsync(new SetProductImageCommand(sku, image.Content, image.ContentType, image.FileName));
            }
            else if (_removeImage && !IsNew)
            {
                await _app.SendAsync(new RemoveProductImageCommand(sku));
            }
            _app.Notify.Success(IsNew ? "Producto creado" : "Producto actualizado", $"{sku} · {_name.Trim()}");
            await _owner.AfterSaveAsync(sku);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Error = AppServices.Describe(ex);
        }
    }

    /// <summary>EAN-13 interno (prefijo 200-299: uso en tienda, no choca con códigos de fabricantes).</summary>
    private static string NewEan13()
    {
        var digits = "2" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(10, 100).ToString(CultureInfo.InvariantCulture)
                     + System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000000, 1000000000).ToString(CultureInfo.InvariantCulture);
        return digits + MINV.Domain.Catalog.BarcodeRules.ComputeGtinCheckDigit(digits);
    }
}
