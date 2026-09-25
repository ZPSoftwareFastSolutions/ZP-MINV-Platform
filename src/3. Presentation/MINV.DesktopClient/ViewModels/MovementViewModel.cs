using System.Windows.Threading;
using MINV.Application.Inventory.Movements;
using MINV.Application.Inventory.Queries;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.ViewModels;

/// <summary>
/// Registrar un movimiento en tres pasos (tipo → producto → cantidad), con vista previa del stock que quedará y el
/// poka-yoke visual de la V2.1: una salida que dejaría la posición en negativo se tiñe de rojo sangre y no se puede
/// registrar (la validación definitiva la hace el dominio al guardar).
/// </summary>
public sealed class MovementViewModel : PageViewModel, IScannerTarget
{
    /// <summary>Tipos que se registran a mano (los de compras y traslados los generan sus propios procesos).</summary>
    private static readonly string[] ManualTypes =
    [
        MovementTypeCodes.Receipt, MovementTypeCodes.Issue, MovementTypeCodes.AdjustmentIn, MovementTypeCodes.AdjustmentOut,
        MovementTypeCodes.SaleReturn, MovementTypeCodes.InitialBalance, MovementTypeCodes.Sale,
    ];

    private readonly DispatcherTimer _previewDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private MovementTypeOption? _type;
    private ProductCard? _product;
    private bool _loadingProduct;
    private string _binCode = string.Empty;
    private string _quantityText = string.Empty;
    private string? _document;
    private string? _notes;
    private string? _pendingType;
    private string? _pendingSku;
    private int _focusRequest;

    public MovementViewModel(AppServices app) : base(app, "registro", "Registrar movimiento", "Entradas, salidas y ajustes", Glyphs.Swap)
    {
        Picker = new ProductPickerViewModel(app.Data);
        Picker.Picked += async (_, item) => await LoadProductAsync(item.Sku);
        Picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProductPickerViewModel.Selected) && Picker.Selected is null && _product is not null)
            {
                Product = null;
            }
        };
        SelectType = new RelayCommand<MovementTypeOption>(SetType);
        Register = new AsyncRelayCommand(RegisterAsync, () => CanRegister);
        Increment = new RelayCommand(() => Step(+1));
        Decrement = new RelayCommand(() => Step(-1));
        Clear = new RelayCommand(() => ResetForm(clearProduct: true));
        OpenProduct = new RelayCommand(() => app.Navigator.OpenProduct(_product!.Sku), () => _product is not null);
        _previewDelay.Tick += (_, _) =>
        {
            _previewDelay.Stop();
            RaisePreview();
        };
    }

    public ProductPickerViewModel Picker { get; }

    public BulkObservableCollection<MovementTypeOption> Types { get; } = [];

    public BulkObservableCollection<string> Bins { get; } = [];

    public BulkObservableCollection<MovementItem> SessionLog { get; } = [];

    public MovementTypeOption? SelectedType
    {
        get => _type;
        private set
        {
            if (Set(ref _type, value))
            {
                RaisePreview();
            }
        }
    }

    public ProductCard? Product
    {
        get => _product;
        private set
        {
            if (Set(ref _product, value))
            {
                OnPropertiesChanged(nameof(HasProduct), nameof(ProductName), nameof(ProductSku), nameof(ProductMeta), nameof(TotalStockText),
                    nameof(MinMaxText), nameof(Level), nameof(Status), nameof(UnitText));
                RaisePreview();
            }
        }
    }

    public bool HasProduct => _product is not null;

    public bool IsLoadingProduct
    {
        get => _loadingProduct;
        private set => Set(ref _loadingProduct, value);
    }

    public string ProductName => _product?.Name ?? "";

    public string ProductSku => _product is { } p ? $"{p.Sku} · {p.Category}" : "";

    public string ProductMeta => _product is { } p ? string.Join(" · ", new[] { p.Supplier, p.Barcodes.FirstOrDefault() }.Where(x => !string.IsNullOrEmpty(x))) : "";

    public string UnitText => _product?.Unit ?? "";

    public string TotalStockText => _product is { } p ? Fmt.Qty(p.OnHand, p.Unit) : "—";

    public string MinMaxText => _product is { } p ? $"Mín. {Fmt.Qty(p.Minimum)} · Máx. {Fmt.Qty(p.Maximum)}" : "";

    public double Level => _product is { Maximum: > 0 } p ? (double)Math.Clamp(p.OnHand / p.Maximum, 0, 1) : _product?.OnHand > 0 ? 1 : 0;

    public StockStatusCode Status => _product?.Status ?? StockStatusCode.Optimal;

    public string BinCode
    {
        get => _binCode;
        set
        {
            if (Set(ref _binCode, (value ?? "").Trim().ToUpperInvariant()))
            {
                RaisePreview();
            }
        }
    }

    public string QuantityText
    {
        get => _quantityText;
        set
        {
            if (Set(ref _quantityText, value ?? string.Empty))
            {
                _previewDelay.Stop();
                _previewDelay.Start();
            }
        }
    }

    public string? Document
    {
        get => _document;
        set => Set(ref _document, value);
    }

    public string? Notes
    {
        get => _notes;
        set
        {
            if (Set(ref _notes, value))
            {
                OnPropertyChanged(nameof(NotesMissing));
            }
        }
    }

    public decimal? Quantity => Fmt.TryParseQuantity(_quantityText, out var q) ? q : null;

    public string? QuantityError => _quantityText.Trim().Length == 0 ? null
        : Quantity is not { } q ? "Escriba un número (use coma o punto para los decimales)."
        : q <= 0 ? "La cantidad debe ser mayor que 0."
        : _product is { AllowsDecimals: false } && decimal.Truncate(q) != q ? $"La unidad {_product.Unit} no admite decimales."
        : null;

    public bool NotesRequired => _type?.RequiresNotes == true;

    public bool NotesMissing => NotesRequired && string.IsNullOrWhiteSpace(_notes);

    /// <summary>Existencia en la posición elegida y el lote por defecto (sin lote se registra ahí): es la que protege el
    /// poka-yoke.</summary>
    public decimal BinAvailable => _product?.Bins.Where(b => b.BinCode == _binCode && b.LotNumber == Batch.DefaultLotNumber)
        .Sum(b => b.Available) ?? 0;

    public string BinStockText => _product is null ? "—" : Fmt.Qty(BinAvailable, _product.Unit);

    public decimal? Projected => _product is not null && _type is not null && Quantity is { } q && q > 0
        ? BinAvailable + (_type.Increases ? q : -q)
        : null;

    public string ProjectedText => Projected is { } p && _product is not null ? Fmt.Qty(p, _product.Unit) : "—";

    public string ChangeText => _type is not null && Quantity is { } q && q > 0 && _product is not null
        ? (_type.Increases ? "+" : "−") + Fmt.Qty(q, _product.Unit)
        : "";

    /// <summary>Poka-yoke visual: la salida dejaría la posición en negativo.</summary>
    public bool WouldBeNegative => Projected < 0;

    public bool CanRegister => _product is not null && _type is not null && Quantity is > 0 && QuantityError is null && !WouldBeNegative
                               && !NotesMissing && _binCode.Length > 0 && !IsLoadingProduct;

    public string RegisterText => _type is null ? "Registrar" : $"Registrar {_type.Name.ToLower(Fmt.Culture)}";

    /// <summary>Cambia cuando la vista debe enfocar el buscador de productos.</summary>
    public int FocusRequest
    {
        get => _focusRequest;
        private set => Set(ref _focusRequest, value);
    }

    public RelayCommand<MovementTypeOption> SelectType { get; }

    public AsyncRelayCommand Register { get; }

    public RelayCommand Increment { get; }

    public RelayCommand Decrement { get; }

    public RelayCommand Clear { get; }

    public RelayCommand OpenProduct { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        await Picker.EnsureLoadedAsync(force);
        if (Types.Count == 0 || force)
        {
            var s = App.Session;
            var all = await App.SendAsync(new GetMovementTypesQuery());
            var allowed = all.Where(t => ManualTypes.Contains(t.Code)
                                         && s.Can(t.Domain == MovementDomain.Sales
                                             ? PermissionCodes.MovementsRegisterSales
                                             : PermissionCodes.MovementsRegisterWarehouse))
                .OrderBy(t => Array.IndexOf(ManualTypes, t.Code))
                .Select(t => new MovementTypeOption(t)).ToList();
            Types.ReplaceAll(allowed);
            var bins = await App.SendAsync(new GetBinsQuery());
            Bins.ReplaceAll(bins.Select(b => b.Code));
            if (_type is null && Types.Count > 0)
            {
                SetType(Types[0]);
            }
        }
        await ApplyPendingAsync();
        if (_product is not null && HasLoaded)
        {
            await LoadProductAsync(_product.Sku);
        }
    }

    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is MovementPrefill prefill)
        {
            _pendingType = prefill.TypeCode;
            _pendingSku = prefill.Sku;
            if (HasLoaded)
            {
                _ = ApplyPendingAsync();
            }
        }
        FocusRequest++;
    }

    public bool OnScanned(string code)
    {
        if (Picker.TrySelectCode(code))
        {
            return true;
        }
        App.Notify.Warning("Código no encontrado", $"«{code}» no es un SKU ni un código de barras del catálogo.");
        return true;
    }

    private async Task ApplyPendingAsync()
    {
        if (_pendingType is { } typeCode && Types.FirstOrDefault(t => t.Code == typeCode) is { } option)
        {
            SetType(option);
        }
        _pendingType = null;
        if (_pendingSku is { } sku)
        {
            _pendingSku = null;
            if (!Picker.TrySelectCode(sku))
            {
                await LoadProductAsync(sku);
            }
        }
    }

    private void SetType(MovementTypeOption option)
    {
        foreach (var t in Types)
        {
            t.IsSelected = ReferenceEquals(t, option);
        }
        SelectedType = option;
        OnPropertiesChanged(nameof(NotesRequired), nameof(NotesMissing), nameof(RegisterText));
    }

    private async Task LoadProductAsync(string sku)
    {
        IsLoadingProduct = true;
        try
        {
            var card = await App.SendAsync(new GetProductCardQuery(sku, 1));
            var keepBin = _product?.Sku == card.Sku && _binCode.Length > 0;
            Product = card;
            if (!keepBin)
            {
                BinCode = card.PrimaryBin ?? card.Bins.OrderByDescending(b => b.OnHand).FirstOrDefault()?.BinCode
                          ?? Bins.FirstOrDefault() ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            Product = null;
            App.Notify.Error("No se pudo abrir el producto", AppServices.Describe(ex));
        }
        finally
        {
            IsLoadingProduct = false;
            RaisePreview();
        }
    }

    private void Step(int direction)
    {
        var q = Quantity ?? 0;
        QuantityText = Fmt.Qty(Math.Max(0, q + direction));
        _previewDelay.Stop();
        RaisePreview();
    }

    private async Task RegisterAsync()
    {
        if (_product is null || _type is null || Quantity is not { } quantity)
        {
            return;
        }
        var product = _product;
        var type = _type;
        var ok = await RunAsync(async () =>
        {
            var result = await App.SendAsync(new RegisterMovementCommand(product.Sku, _binCode, type.Code, quantity, null,
                string.IsNullOrWhiteSpace(_document) ? null : _document.Trim(),
                string.IsNullOrWhiteSpace(_notes) ? null : _notes.Trim()));
            App.Notify.Success($"{type.Title} registrada",
                $"{(type.Increases ? "+" : "−")}{Fmt.Qty(quantity, product.Unit)} · {product.Sku} · quedan {Fmt.Qty(result.QuantityOnHand, product.Unit)} en {_binCode}");
            SessionLog.Insert(0, new MovementItem(new RecentMovement(App.Now, App.Session.Workspace.Today, product.Sku,
                product.Name, type.Code, type.Name, (short)(type.Increases ? 1 : -1), quantity, product.Unit, _binCode,
                App.Session.DisplayName, _document), App.Now));
            App.Data.Invalidate();
        }, "No se registró el movimiento");
        if (ok)
        {
            ResetForm(clearProduct: false);
            await LoadProductAsync(product.Sku);
        }
    }

    private void ResetForm(bool clearProduct)
    {
        QuantityText = string.Empty;
        Document = null;
        Notes = null;
        if (clearProduct)
        {
            Picker.Clear();
            Product = null;
        }
        FocusRequest++;
        RaisePreview();
    }

    private void RaisePreview() => OnPropertiesChanged(nameof(Quantity), nameof(QuantityError), nameof(BinAvailable), nameof(BinStockText),
        nameof(Projected), nameof(ProjectedText), nameof(ChangeText), nameof(WouldBeNegative), nameof(CanRegister), nameof(NotesRequired),
        nameof(NotesMissing), nameof(RegisterText));
}
