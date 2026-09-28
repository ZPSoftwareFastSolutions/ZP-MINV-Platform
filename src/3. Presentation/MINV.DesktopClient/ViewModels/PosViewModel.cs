using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Sales;
using MINV.Application.Tech;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Accounting;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Sales;
using MINV.Hardware;
using MINV.Hardware.EscPos;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Producto vendible en la cuadrícula del punto de venta.</summary>
public sealed class PosProduct(SellableProduct p, ImageSource? image) : ObservableObject
{
    private decimal _available = p.Available;

    public SellableProduct Product { get; } = p;

    public string Sku => Product.Sku;

    public string Name => Product.Name;

    public string CategoryCode => Product.CategoryCode;

    public ImageSource? Image { get; } = image;

    public string PriceText => Fmt.Money(Product.Price);

    public decimal Available
    {
        get => _available;
        set
        {
            if (Set(ref _available, value))
            {
                OnPropertiesChanged(nameof(AvailableText), nameof(IsOut), nameof(IsLow));
            }
        }
    }

    /// <summary>V6 · Con reservas: «Disponible 2 (reservado 1)»; lo reservado no se vende a otro cliente (regla S-03).</summary>
    public string AvailableText => IsOut
        ? Reserved > 0 ? $"Agotado · reservado {Fmt.Qty(Reserved)}" : "Agotado"
        : Reserved > 0 ? $"Disponible {Fmt.Qty(_available)} (reservado {Fmt.Qty(Reserved)})" : $"{Fmt.Qty(_available)} {Product.Unit}";

    /// <summary>V6 · Unidades reservadas por armados (web y escritorio) y reservas de caja en la sucursal.</summary>
    public decimal Reserved { get; init; }

    public bool HasReserved => Reserved > 0;

    public bool IsOut => _available <= 0;

    public bool IsLow => !IsOut && _available <= 3;

    /// <summary>V4.2 · Ficha resumida (serie o IMEI, garantía, plataformas).</summary>
    public TechProductRow? Tech { get; init; }

    public bool IsSerialized => Tech?.TrackSerials == true;

    public SerialKind SerialKind => Tech?.SerialKind ?? SerialKind.Serial;

    public string? SerialBadge => IsSerialized ? TechText.KindBadge(SerialKind) : null;

    public bool HasSerialBadge => IsSerialized;

    public IReadOnlyList<string> Platforms => Tech?.Platforms ?? [];

    public bool HasPlatforms => Platforms.Count > 0;

    public string? WarrantyBadge => Tech is { WarrantyMonths: > 0 } t ? TechText.Warranty(t.WarrantyMonths) : null;
}

/// <summary>Línea del carrito (cantidad y descuento editables; el importe incluye el IVA).</summary>
public sealed class CartLine : ObservableObject
{
    private readonly Action _changed;
    private decimal _quantity;
    private int _discount;

    private readonly decimal? _unitPrice;

    public CartLine(PosProduct product, decimal quantity, Action changed, decimal? unitPrice = null, bool locked = false, bool reserved = false)
    {
        Product = product;
        _quantity = quantity;
        _changed = changed;
        _unitPrice = unitPrice;
        IsLocked = locked;
        IsReserved = reserved;
        Serials.CollectionChanged += (_, _) =>
        {
            if (IsSerialized && !IsLocked)
            {
                Quantity = Serials.Count;   // una unidad por serie (en un armado la cantidad es la cotizada)
            }
            OnPropertiesChanged(nameof(SerialsText), nameof(HasSerials), nameof(MissingSerials), nameof(SerialsNeeded));
            _changed();
        };
    }

    public PosProduct Product { get; }

    /// <summary>V4.2 · Línea de un armado cotizado: precio congelado, cantidad y descuento fijos.</summary>
    public bool IsLocked { get; }

    /// <summary>V6 · Línea de un armado RESERVADO: sus unidades ya están apartadas (no cuentan en lo disponible), así que no
    /// «supera lo disponible»; la venta consume la reserva (regla S-04).</summary>
    public bool IsReserved { get; }

    public bool IsEditable => !IsLocked;

    public decimal UnitPrice => _unitPrice ?? Product.Product.Price;

    /// <summary>V4.2 · Series o IMEI de las unidades de la línea (una por unidad, regla T-02).</summary>
    public ObservableCollection<string> Serials { get; } = [];

    public bool IsSerialized => Product.IsSerialized;

    public bool HasSerials => Serials.Count > 0;

    public string SerialsText => Serials.Count == 0 ? string.Empty : $"{TechText.KindBadge(Product.SerialKind)}: {string.Join(", ", Serials)}";

    /// <summary>Faltan series (p. ej. un armado cargado sin elegir sus unidades).</summary>
    public bool MissingSerials => IsSerialized && Serials.Count != (int)_quantity;

    public string SerialsNeeded => $"Elija {(int)_quantity} {(IsSerialized ? TechText.KindLabel(Product.SerialKind) : "")} (tiene {Serials.Count})";

    public string Name => Product.Name;

    public string Sku => Product.Sku;

    public ImageSource? Image => Product.Image;

    public string UnitPriceText => $"{Fmt.Money(UnitPrice)} / {Product.Product.Unit}" + (IsLocked ? " · precio cotizado" : string.Empty);

    public static IReadOnlyList<int> Discounts { get; } = [0, 5, 10, 15, 20, 25];

    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (Set(ref _quantity, value))
            {
                OnPropertiesChanged(nameof(QuantityText), nameof(Amount), nameof(AmountText), nameof(ExceedsStock), nameof(MissingSerials),
                    nameof(SerialsNeeded));
                _changed();
            }
        }
    }

    public string QuantityText
    {
        get => Fmt.Qty(_quantity);
        set
        {
            if (!IsSerialized && !IsLocked && Fmt.TryParseQuantity(value, out var q) && q > 0)
            {
                Quantity = Product.Product.AllowsDecimals ? decimal.Round(q, 3) : decimal.Round(q, 0, MidpointRounding.AwayFromZero);
            }
            OnPropertyChanged();
        }
    }

    public int Discount
    {
        get => _discount;
        set
        {
            if (Set(ref _discount, value))
            {
                OnPropertiesChanged(nameof(Amount), nameof(AmountText), nameof(HasDiscount));
                _changed();
            }
        }
    }

    public bool HasDiscount => _discount > 0;

    public decimal Amount => decimal.Round(_quantity * UnitPrice * (1 - _discount / 100m), 2, MidpointRounding.AwayFromZero);

    public string AmountText => Fmt.Money(Amount);

    /// <summary>Guía visual (poka-yoke): la cantidad supera lo disponible (existencias − reservado). La venta la bloquea el dominio.</summary>
    public bool ExceedsStock => !IsReserved && _quantity > Product.Available;

    /// <summary>V6 · Aviso claro cuando la cantidad supera lo disponible, con lo reservado si lo hay.</summary>
    public string ExceedsStockText => Product.Reserved > 0
        ? $"Supera lo disponible: hay {Fmt.Qty(Product.Available)} ({Fmt.Qty(Product.Reserved)} reservada{(Product.Reserved == 1 ? "" : "s")} para armados)"
        : $"Supera lo disponible: hay {Fmt.Qty(Product.Available)}";

    public decimal Step => Product.Product.AllowsDecimals ? 0.5m : 1m;
}

/// <summary>
/// Punto de venta: cuadrícula de productos con imagen (búsqueda, categorías, escáner), carrito con descuentos, cliente y
/// medio de pago en combos, cobro con vuelto, ticket en pantalla o impreso, y apertura / cierre de caja con arqueo.
/// </summary>
public sealed class PosViewModel : PageViewModel, IScannerTarget
{
    private const string AllCategories = "ALL";
    private const int CategoryChipsShown = 10;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private List<PosProduct> _products = [];
    private PosState? _state;
    private string _search = string.Empty;
    private string _category = AllCategories;
    private PosOption? _customer;
    private PosOption? _method;
    private PosOption? _register;
    private string _cash = string.Empty;
    private string _reference = string.Empty;
    private string _openingCash = "500";
    private CheckoutResult? _last;
    private string? _receiptText;
    private PosFiscalState? _fiscal;
    private BuyerForm? _buyer;
    private string _cardNumber = string.Empty;
    private PosFiscalResult? _fiscalResult;
    private bool _buyerExpanded = true;
    private IReadOnlyDictionary<string, TechProductRow> _tech = new Dictionary<string, TechProductRow>();
    private string? _platform;
    private bool _allCategories;
    private PcBuildDetail? _build;
    private string? _pendingBuild;

    public PosViewModel(AppServices app, BillingWorkService billingWork)
        : base(app, "pos", "Punto de venta", "Vender, cobrar y emitir la factura", Glyphs.Cart)
    {
        // V4.1 · Cada ronda del trabajo automático puede cambiar el modo (en línea / fuera de línea): se actualiza la banda
        billingWork.Completed += async (_, _) =>
        {
            if (HasLoaded && IsBilling)
            {
                await RefreshFiscalQuietlyAsync();
            }
        };
        // Se activó, desactivó o configuró la facturación (en esta sesión o detectado por el trabajo automático): la caja
        // pasa a facturar (o deja de hacerlo) sin volver a iniciar sesión
        app.Session.BillingChanged += async (_, _) =>
        {
            if (HasLoaded)
            {
                await RefreshFiscalQuietlyAsync();
            }
        };
        ToggleBuyer = new RelayCommand(() => IsBuyerExpanded = !IsBuyerExpanded);
        CloseFiscalResult = new RelayCommand(() => FiscalResult = null);
        Products = CollectionViewSource.GetDefaultView(_products);
        Cart.CollectionChanged += (_, _) => Recalculate();
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Products.Refresh();
            OnPropertyChanged(nameof(NoProducts));
        };
        SelectFilter = new RelayCommand<FilterChip>(chip =>
        {
            foreach (var c in CategoryChips)
            {
                c.IsSelected = ReferenceEquals(c, chip);
            }
            _category = chip.Value as string ?? AllCategories;
            Products.Refresh();
            OnPropertyChanged(nameof(NoProducts));
        });
        ToggleCategories = new RelayCommand(() =>
        {
            _allCategories = !_allCategories;
            OnPropertiesChanged(nameof(CollapseCategories), nameof(MoreCategoriesText));
        });
        Add = new RelayCommand<PosProduct>(AddProduct);
        Increase = new RelayCommand<CartLine>(l =>
        {
            if (l.IsSerialized)
            {
                _ = PickSerialsAsync(l.Product, l);   // V4.2 · otra unidad = otra serie
                return;
            }
            l.Quantity += l.Step;
        }, l => !l.IsLocked);
        Decrease = new RelayCommand<CartLine>(l =>
        {
            if (l.IsSerialized && l.Serials.Count > 0)
            {
                l.Serials.RemoveAt(l.Serials.Count - 1);
                if (l.Serials.Count == 0)
                {
                    Cart.Remove(l);
                }
                return;
            }
            if (l.Quantity - l.Step <= 0)
            {
                Cart.Remove(l);
            }
            else
            {
                l.Quantity -= l.Step;
            }
        }, l => !l.IsLocked);
        Remove = new RelayCommand<CartLine>(l => Cart.Remove(l), l => !l.IsLocked);
        // V4.2 · Series de la línea, plataformas y venta de un armado cotizado
        EditSerials = new AsyncRelayCommand<CartLine>(l => l.IsLocked ? EnsureBuildSerialsAsync(force: true) : PickSerialsAsync(l.Product, l, replace: true),
            l => l.IsSerialized);
        SelectPlatform = new RelayCommand<FilterChip>(chip =>
        {
            _platform = chip.Value as string;
            foreach (var c in PlatformChips)
            {
                c.IsSelected = ReferenceEquals(c, chip);
            }
            Products.Refresh();
            OnPropertyChanged(nameof(NoProducts));
        });
        FromBuild = new AsyncRelayCommand(FromBuildAsync, () => IsOpen);
        ClearBuild = new RelayCommand(() =>
        {
            Build = null;
            Cart.Clear();
        }, () => _build is not null);
        ClearCart = new AsyncRelayCommand(ClearCartAsync, () => Cart.Count > 0 || _build is not null);
        Checkout = new AsyncRelayCommand(CheckoutAsync, () => Cart.Count > 0 && IsOpen);
        OpenSession = new AsyncRelayCommand(OpenSessionAsync, () => _register is not null);
        CloseSession = new AsyncRelayCommand(CloseSessionAsync, () => IsOpen);
        QuickCash = new RelayCommand<string>(v => CashReceived = v == "exacto" ? Numbers.Plain(Total) : v);
        PrintReceipt = new AsyncRelayCommand(PrintAsync, () => _last is not null);
        CloseReceipt = new RelayCommand(() => ReceiptText = null);
        OpeningOptions = ["0", "100", "200", "300", "500", "1000"];
    }

    public ICollectionView Products { get; private set; }

    public BulkObservableCollection<FilterChip> CategoryChips { get; } = [];

    /// <summary>V4.2 · Con muchas categorías (la edición Tecnología tiene decenas) los chips se pliegan a dos filas.</summary>
    public bool HasManyCategories => CategoryChips.Count > CategoryChipsShown;

    public bool CollapseCategories => HasManyCategories && !_allCategories;

    public string MoreCategoriesText => _allCategories ? "Ver menos categorías" : $"Ver todas las categorías ({CategoryChips.Count - 1})";

    public RelayCommand ToggleCategories { get; }

    /// <summary>V4.2 · Chips de plataforma (opciones de la especificación «Plataforma», regla T-07).</summary>
    public BulkObservableCollection<FilterChip> PlatformChips { get; } = [];

    public bool HasPlatforms => PlatformChips.Count > 1;

    public RelayCommand<FilterChip> SelectPlatform { get; }

    public AsyncRelayCommand<CartLine> EditSerials { get; }

    /// <summary>V4.2 · Cargar una cotización vigente del armador de PC para cobrarla a sus precios.</summary>
    public AsyncRelayCommand FromBuild { get; }

    public RelayCommand ClearBuild { get; }

    /// <summary>V4.2 · Armado cotizado que se está cobrando (null = venta normal).</summary>
    public PcBuildDetail? Build
    {
        get => _build;
        private set
        {
            if (Set(ref _build, value))
            {
                OnPropertiesChanged(nameof(IsBuildMode), nameof(BuildTitle), nameof(BuildDetail), nameof(CheckoutText));
            }
        }
    }

    public bool IsBuildMode => _build is not null;

    public string BuildTitle => _build is { } b ? $"Armado {b.Build.Number} · {b.Build.Name}" : string.Empty;

    public string BuildDetail => _build is { } b
        ? (b.Build.Status == PcBuildStatus.Reserved
              ? $"Reserva{(b.Build.Channel == PcBuildChannel.Web ? " de la tienda web" : string.Empty)} vigente hasta el {Fmt.DateTime(b.Build.ReservedUntil!.Value)} · " +
                "al cobrar se consume la reserva (el stock reservado sale una sola vez) · precios congelados"
              : $"Cotización vigente hasta el {Fmt.Date(b.Build.ValidUntil)} · precios congelados") +
          (b.Build.Customer is { } c ? " · " + c : b.Build.ContactName is { Length: > 0 } contact ? " · " + contact : string.Empty) +
          (b.Build.QuotedWithErrors ? " · cotizado con errores de compatibilidad aceptados" : string.Empty)
        : string.Empty;

    /// <summary>V6 · El armado del carrito está reservado: la venta consume la reserva.</summary>
    public bool IsReservedBuild => _build?.Build.Status == PcBuildStatus.Reserved;

    public ObservableCollection<CartLine> Cart { get; } = [];

    public BulkObservableCollection<PosOption> Customers { get; } = [];

    public BulkObservableCollection<PosOption> PaymentMethods { get; } = [];

    public BulkObservableCollection<PosOption> Registers { get; } = [];

    public IReadOnlyList<string> OpeningOptions { get; }

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

    public bool NoProducts => HasLoaded && Products.IsEmpty;

    // ------------------------------------------------------------------------------------------------ caja
    public bool IsOpen => _state?.Session is not null;

    public bool IsClosed => !IsOpen;

    public PosSessionInfo? Session => _state?.Session;

    public string SessionTitle => Session is { } s ? $"{s.RegisterName} abierta" : "Caja cerrada";

    public string SessionText => Session is { } s
        ? $"Desde las {s.OpenedAt.ToLocalTime():HH:mm} · {s.Tickets} ventas · {Fmt.Money(s.Sales)} · efectivo esperado {Fmt.Money(s.ExpectedCash)}"
        : "Abra la caja con el fondo inicial para empezar a vender.";

    public PosOption? Register
    {
        get => _register;
        set => Set(ref _register, value);
    }

    public string OpeningCash
    {
        get => _openingCash;
        set => Set(ref _openingCash, value ?? string.Empty);
    }

    // ------------------------------------------------------------------------------------------------ cobro
    public PosOption? Customer
    {
        get => _customer;
        set => Set(ref _customer, value);
    }

    public PosOption? PaymentMethod
    {
        get => _method;
        set
        {
            if (Set(ref _method, value))
            {
                OnPropertiesChanged(nameof(IsCash), nameof(NeedsReference), nameof(ChangeText), nameof(CheckoutText), nameof(IsCard));
                if (!IsCard)
                {
                    CardNumber = string.Empty;
                }
            }
        }
    }

    public bool IsCash => _method?.OpensCashDrawer ?? true;

    public bool NeedsReference => !IsCash;

    public string CashReceived
    {
        get => _cash;
        set
        {
            if (Set(ref _cash, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ChangeText));
                OnPropertyChanged(nameof(CashShort));
            }
        }
    }

    public string Reference
    {
        get => _reference;
        set => Set(ref _reference, value ?? string.Empty);
    }

    public decimal Total => Cart.Sum(l => l.Amount);

    public decimal TaxRate => _state?.TaxRate ?? 13;

    public string TotalText => Fmt.Money(Total);

    /// <summary>IVA incluido en el carrito, sobre el total como en la venta (V4.2: en Bolivia, el 13 % de lo facturado, el
    /// débito fiscal de la factura; <see cref="VatRules"/>).</summary>
    public string TaxText => $"IVA incluido ({TaxRate:0.##} %): {Fmt.Money(VatRules.IncludedTax(Total, TaxRate, _state?.VatOnInvoicedAmount ?? true))}";

    public string ItemsText => Cart.Count == 0 ? "Carrito vacío" : $"{Cart.Count} producto{(Cart.Count == 1 ? "" : "s")} · {Fmt.Qty(Cart.Sum(l => l.Quantity))} unidades";

    public string DiscountText => Cart.Any(l => l.HasDiscount)
        ? $"Descuentos: −{Fmt.Money(Cart.Sum(l => decimal.Round(l.Quantity * l.Product.Product.Price, 2) - l.Amount))}"
        : string.Empty;

    public bool CashShort => IsCash && Numbers.TryParse(_cash, out var cash) && cash > 0 && cash < Total;

    public string ChangeText
    {
        get
        {
            if (!IsCash)
            {
                return string.Empty;
            }
            if (!Numbers.TryParse(_cash, out var cash) || cash <= 0)
            {
                return "Sin monto recibido: se cobra exacto.";
            }
            return cash < Total ? $"Faltan {Fmt.Money(Total - cash)}" : $"Vuelto: {Fmt.Money(cash - Total)}";
        }
    }

    public string CheckoutText => Cart.Count == 0 ? "Agregue productos" : _build is { } b ? $"Cobrar el armado · {Fmt.Money(Total)}" : $"Cobrar {Fmt.Money(Total)}";

    public bool IsCartEmpty => Cart.Count == 0;

    // ------------------------------------------------------------------------------------------------ ticket
    public CheckoutResult? LastSale
    {
        get => _last;
        private set
        {
            if (Set(ref _last, value))
            {
                OnPropertyChanged(nameof(HasLastSale));
                OnPropertyChanged(nameof(LastSaleText));
            }
        }
    }

    public bool HasLastSale => _last is not null;

    public string LastSaleText => _last is { } s
        ? $"Última venta {s.InvoiceNumber} · {Fmt.Money(s.Total)} · {s.PaymentMethod}{(s.Change > 0 ? $" · vuelto {Fmt.Money(s.Change)}" : "")}"
        : string.Empty;

    /// <summary>Ticket en texto (vista previa en pantalla, igual al impreso).</summary>
    public string? ReceiptText
    {
        get => _receiptText;
        private set
        {
            if (Set(ref _receiptText, value))
            {
                OnPropertyChanged(nameof(IsReceiptOpen));
            }
        }
    }

    public bool IsReceiptOpen => _receiptText is not null;

    // ------------------------------------------------------------------------------------------------ V4.1 · facturación
    /// <summary>¿La empresa factura desde esta caja? (si no, la caja funciona como en la V4).</summary>
    public bool IsBilling => _fiscal?.BillingEnabled == true;

    public PosFiscalState? FiscalState => _fiscal;

    /// <summary>Banda de estado fiscal: «Facturación en línea · PV 1», «FUERA DE LÍNEA…», «Contingencia manual» o qué falta.</summary>
    public string FiscalTitle => _fiscal switch
    {
        null => string.Empty,
        { Ready: false, Mode: SiatConnectionMode.ManualContingency } => "Contingencia manual: use el talonario CAFC",
        { Ready: false } => "La caja todavía no puede facturar",
        { Mode: SiatConnectionMode.Offline } => "FUERA DE LÍNEA · las facturas se envían solas al volver la conexión",
        { Mode: SiatConnectionMode.Recovering } => "Recuperando la conexión con el SIN",
        { PointOfSaleCode: { } code } => $"Facturación en línea · PV {code}",
        _ => "Facturación en línea",
    };

    public string FiscalDetail => _fiscal is { } f ? FiscalText.Plain(f.Message) : string.Empty;

    public string FiscalBrush => _fiscal switch
    {
        { Ready: false, Mode: SiatConnectionMode.ManualContingency } => "Danger",
        { Ready: false } => "Danger",
        { Mode: SiatConnectionMode.Online } when !(_fiscal.Message.StartsWith('⚠')) => "Success",
        _ => "Warning",
    };

    public string FiscalSoftBrush => FiscalBrush + "Soft";

    public string FiscalGlyph => _fiscal switch
    {
        { Ready: false } => Glyphs.Warning,
        { Mode: SiatConnectionMode.Online } => Glyphs.CheckCircle,
        _ => Glyphs.Offline,
    };

    /// <summary>Datos de facturación del comprador (nominatividad).</summary>
    public BuyerForm? Buyer { get => _buyer; private set => Set(ref _buyer, value); }

    public bool IsBuyerExpanded { get => _buyerExpanded; set => Set(ref _buyerExpanded, value); }

    public RelayCommand ToggleBuyer { get; }

    /// <summary>El medio de pago es tarjeta: se pide el número (viaja al caso de uso, que lo guarda solo enmascarado).</summary>
    public bool IsCard => IsBilling && _method is { } m && (m.Code.Contains("TARJ", StringComparison.OrdinalIgnoreCase)
                                                            || m.Name.Contains("tarjeta", StringComparison.OrdinalIgnoreCase)
                                                            || m.Code.Contains("CARD", StringComparison.OrdinalIgnoreCase));

    /// <summary>Número de la tarjeta: se envía al cobrar y se borra de la pantalla en ese momento (nunca se guarda completo).</summary>
    public string CardNumber { get => _cardNumber; set => Set(ref _cardNumber, value ?? string.Empty); }

    /// <summary>Resultado fiscal del último cobro (panel sobre la caja).</summary>
    public PosFiscalResult? FiscalResult
    {
        get => _fiscalResult;
        private set
        {
            if (Set(ref _fiscalResult, value))
            {
                OnPropertyChanged(nameof(IsFiscalResultOpen));
            }
        }
    }

    public bool IsFiscalResultOpen => _fiscalResult is not null;

    public RelayCommand CloseFiscalResult { get; }

    public RelayCommand<FilterChip> SelectFilter { get; }

    public RelayCommand<PosProduct> Add { get; }

    public RelayCommand<CartLine> Increase { get; }

    public RelayCommand<CartLine> Decrease { get; }

    public RelayCommand<CartLine> Remove { get; }

    public AsyncRelayCommand ClearCart { get; }

    public AsyncRelayCommand Checkout { get; }

    public AsyncRelayCommand OpenSession { get; }

    public AsyncRelayCommand CloseSession { get; }

    public RelayCommand<string> QuickCash { get; }

    public AsyncRelayCommand PrintReceipt { get; }

    public RelayCommand CloseReceipt { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var state = await App.SendAsync(new GetPosStateQuery());
        ApplyState(state);
        await RefreshFiscalAsync();
        var sellable = await App.SendAsync(new GetSellableProductsQuery());
        var images = await App.Images.AllAsync(force);
        var inCart = Cart.GroupBy(l => l.Sku).ToDictionary(g => g.Key, g => g.First());
        _tech = await TechCatalog.LoadAsync(App);
        var reserved = await ReservedStock.LoadAsync(App);   // V6 · «Disponible n (reservado m)» (regla S-08)
        _products = sellable.Select(p => new PosProduct(p, images.GetValueOrDefault(p.Sku)) { Tech = _tech.GetValueOrDefault(p.Sku), Reserved = reserved.GetValueOrDefault(p.Sku) })
            .ToList();
        Products = CollectionViewSource.GetDefaultView(_products);
        Products.Filter = Matches;
        OnPropertyChanged(nameof(Products));
        // El carrito conserva sus líneas pero con la disponibilidad nueva
        foreach (var line in inCart.Values)
        {
            if (_products.FirstOrDefault(p => p.Sku == line.Sku) is { } fresh)
            {
                line.Product.Available = fresh.Available;
            }
        }
        var chips = new List<FilterChip> { new("Todo", AllCategories, _products.Count) };
        chips.AddRange(_products.GroupBy(p => (p.Product.CategoryCode, p.Product.Category)).OrderBy(g => g.Key.Category, StringComparer.Create(Fmt.Culture, true))
            .Select(g => new FilterChip(g.Key.Category, g.Key.CategoryCode, g.Count())));
        CategoryChips.ReplaceAll(chips);
        (CategoryChips.FirstOrDefault(c => (string?)c.Value == _category) ?? CategoryChips[0]).IsSelected = true;
        OnPropertiesChanged(nameof(HasManyCategories), nameof(CollapseCategories), nameof(MoreCategoriesText));
        // V4.2 · Plataformas: opciones de la especificación, con los productos vendibles de cada una
        var platforms = (await TechCatalog.PlatformsAsync(App))
            .Select(p => new FilterChip(p, p, _products.Count(x => x.Platforms.Contains(p, StringComparer.OrdinalIgnoreCase)))).Where(c => c.Count > 0).ToList();
        PlatformChips.ReplaceAll(platforms.Count == 0 ? [] : [new FilterChip("Todas", null, _products.Count(x => x.HasPlatforms)), .. platforms]);
        if (PlatformChips.FirstOrDefault(c => Equals(c.Value, _platform)) is { } platformChip)
        {
            platformChip.IsSelected = true;
        }
        else
        {
            _platform = null;
            if (PlatformChips.FirstOrDefault() is { } all)
            {
                all.IsSelected = true;
            }
        }
        OnPropertyChanged(nameof(HasPlatforms));
        Subtitle = $"{state.CompanyName} · {state.BranchName}";
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(NoProducts));
        if (_pendingBuild is { } number)
        {
            // Se carga al terminar la carga de la caja (pide confirmaciones y las series de las piezas)
            _pendingBuild = null;
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(async () => await LoadBuildAsync(number), DispatcherPriority.Background);
        }
    }

    /// <summary>V4.2 · «Vender en caja» desde el armador: la caja carga esa cotización.</summary>
    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is PcBuildToSell sale)
        {
            if (HasLoaded && !IsBusy)
            {
                _ = LoadBuildAsync(sale.Number);
            }
            else
            {
                _pendingBuild = sale.Number;
            }
        }
    }

    public bool OnScanned(string code)
    {
        var product = _products.FirstOrDefault(p => p.Sku.Equals(code, StringComparison.OrdinalIgnoreCase) || p.Product.Barcodes.Contains(code));
        if (product is null)
        {
            return false;
        }
        AddProduct(product);
        return true;
    }

    private void ApplyState(PosState state)
    {
        _state = state;
        var customer = _customer?.Code ?? "CF";
        var method = _method?.Code ?? "EFECTIVO";
        Customers.ReplaceAll(state.Customers);
        PaymentMethods.ReplaceAll(state.PaymentMethods);
        Registers.ReplaceAll(state.Registers);
        Customer = Customers.FirstOrDefault(c => c.Code == customer) ?? Customers.FirstOrDefault();
        PaymentMethod = PaymentMethods.FirstOrDefault(m => m.Code == method) ?? PaymentMethods.FirstOrDefault();
        // V4.2 · Caja preseleccionada: la del turno abierto del usuario o, si no tiene, una libre (la sugiere el caso de uso;
        // antes era siempre la primera, aunque tuviera el turno de otro cajero)
        Register = Registers.FirstOrDefault(r => r.Code == (state.Session?.RegisterCode ?? state.SuggestedRegister)) ?? Registers.FirstOrDefault();
        OnPropertiesChanged(nameof(IsOpen), nameof(IsClosed), nameof(Session), nameof(SessionTitle), nameof(SessionText), nameof(TaxText));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>V4.1 · Actualización en segundo plano de la banda fiscal: nunca interrumpe al cajero (fallas al registro).</summary>
    private async Task RefreshFiscalQuietlyAsync()
    {
        try
        {
            await RefreshFiscalAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("M-INV · banda fiscal de la caja: {0}", ex.Message);
        }
    }

    /// <summary>V4.1 · Estado fiscal de la caja (si la empresa tiene el módulo; sin él la caja sigue como en la V4).</summary>
    private async Task RefreshFiscalAsync()
    {
        if (!App.Session.HasBillingModule)
        {
            _fiscal = null;
        }
        else
        {
            try
            {
                _fiscal = await App.SendAsync(new GetPosFiscalStateQuery());
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                _fiscal = null;
                System.Diagnostics.Trace.TraceWarning("M-INV · estado fiscal de la caja: {0}", ex.Message);
            }
        }
        if (IsBilling && _buyer is null)
        {
            var buyer = new BuyerForm(App, _fiscal!.DocumentTypes);
            buyer.CustomerFound += (_, code) =>
            {
                if (Customers.FirstOrDefault(c => c.Code == code) is { } customer)
                {
                    Customer = customer;
                }
            };
            Buyer = buyer;
        }
        OnPropertiesChanged(nameof(IsBilling), nameof(FiscalState), nameof(FiscalTitle), nameof(FiscalDetail), nameof(FiscalBrush), nameof(FiscalSoftBrush),
            nameof(FiscalGlyph), nameof(IsCard));
    }

    private bool Matches(object o)
    {
        if (o is not PosProduct p)
        {
            return false;
        }
        if (_category != AllCategories && p.CategoryCode != _category)
        {
            return false;
        }
        if (_platform is not null && !p.Platforms.Contains(_platform, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }
        var q = _search.Trim();
        return q.Length == 0
               || p.Sku.Contains(q, StringComparison.OrdinalIgnoreCase)
               || p.Product.Barcodes.Any(b => b.Contains(q, StringComparison.Ordinal))
               || Fmt.Culture.CompareInfo.IndexOf(p.Name, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    private void AddProduct(PosProduct product)
    {
        if (!IsOpen)
        {
            App.Notify.Warning("Caja cerrada", "Abra la caja para empezar a vender.");
            return;
        }
        if (product.IsOut)
        {
            App.Notify.Warning("Producto agotado", $"{product.Name} no tiene stock disponible.");
            return;
        }
        if (_build is { } build)
        {
            App.Notify.Warning("Está cobrando un armado", $"El carrito tiene el armado {build.Build.Number}: cóbrelo o quítelo antes de agregar otros productos.");
            return;
        }
        if (product.IsSerialized)
        {
            // V4.2 · Producto con serie o IMEI: se elige (o escanea) la unidad que se lleva el cliente
            _ = PickSerialsAsync(product, Cart.FirstOrDefault(l => l.Sku == product.Sku));
            return;
        }
        if (Cart.FirstOrDefault(l => l.Sku == product.Sku) is { } line)
        {
            line.Quantity += line.Step;
        }
        else
        {
            Cart.Add(new CartLine(product, 1, Recalculate));
        }
        Recalculate();
    }

    /// <summary>
    /// V4.2 · Unidades de un producto serializado: las disponibles en la sucursal (sin las que ya están en el carrito), con
    /// búsqueda y escáner. Agregar suma las elegidas; <paramref name="replace"/> vuelve a elegir todas las de la línea.
    /// </summary>
    private async Task PickSerialsAsync(PosProduct product, CartLine? line, bool replace = false)
    {
        IReadOnlyList<MINV.Application.Tech.SerialRow> available;
        try
        {
            available = await App.SendAsync(new GetAvailableSerialsQuery(product.Sku));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudieron leer las series disponibles", AppServices.Describe(ex));
            return;
        }
        var taken = Cart.Where(l => l.Sku == product.Sku && (!replace || !ReferenceEquals(l, line))).SelectMany(l => l.Serials)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = available.Where(a => replace && line is not null && line.Serials.Contains(a.Serial) || !taken.Contains(a.Serial)).ToList();
        if (options.Count == 0)
        {
            App.Notify.Warning("Sin unidades disponibles", $"No quedan unidades de {product.Name} con {TechText.KindLabel(product.SerialKind)} en esta sucursal.");
            return;
        }
        var chosen = await SerialsDialog.AskAsync(App, $"{TechText.KindLabel(product.SerialKind)} de {product.Name}",
            "Escanee la unidad que se lleva el cliente o elíjala de la lista (disponibles en esta sucursal).", replace ? "Usar estas unidades" : "Agregar al carrito",
            [new SerialCaptureLineSpec(product.Sku, product.Name, product.SerialKind, 1, SerialCaptureMode.Pick, options,
                replace && line is not null ? line.Serials.ToList() : null, Flexible: true)]);
        if (chosen is null || chosen[0].Serials.Count == 0)
        {
            return;
        }
        if (line is null || !Cart.Contains(line))
        {
            line = new CartLine(product, 0, Recalculate);
            Cart.Add(line);
        }
        if (replace)
        {
            line.Serials.Clear();
        }
        foreach (var serial in chosen[0].Serials.Where(s => !line.Serials.Contains(s)))
        {
            line.Serials.Add(serial);
        }
        Recalculate();
    }

    /// <summary>V4.2 · Carga una cotización VIGENTE del armador: una línea por pieza a su precio cotizado (fijas).</summary>
    public async Task LoadBuildAsync(string number)
    {
        if (!IsOpen)
        {
            App.Notify.Warning("Caja cerrada", "Abra la caja para cobrar el armado.");
            return;
        }
        PcBuildDetail detail;
        try
        {
            detail = await App.SendAsync(new GetPcBuildQuery(number));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo cargar el armado", AppServices.Describe(ex));
            return;
        }
        // V6 · También se cobra un armado RESERVADO (web o escritorio): la venta consume la reserva (regla S-04)
        if (detail.Build.Status is not (PcBuildStatus.Quoted or PcBuildStatus.Reserved) || detail.Build.IsExpired)
        {
            App.Notify.Warning("El armado no se puede cobrar",
                $"{detail.Build.Number} está {TechText.BuildStatus(detail.Build.Status, detail.Build.IsExpired).ToLower(Fmt.Culture)}: solo se cobran cotizaciones y reservas vigentes.");
            return;
        }
        if (Cart.Count > 0 && _build is null
            && !await App.Dialogs.ConfirmAsync("Cargar el armado", $"Se quitan los {Cart.Count} productos del carrito para cobrar el armado {detail.Build.Number}.",
                "Cargar armado", "Volver"))
        {
            return;
        }
        var reserved = detail.Build.Status == PcBuildStatus.Reserved;
        Cart.Clear();
        foreach (var item in detail.QuotedItems)
        {
            var product = _products.FirstOrDefault(p => p.Sku.Equals(item.Sku, StringComparison.OrdinalIgnoreCase))
                          ?? new PosProduct(new SellableProduct(Guid.Empty, item.Sku, item.Name, string.Empty, "Armado", "UND", false, item.UnitPrice, item.Stock, []),
                              null) { Tech = _tech.GetValueOrDefault(item.Sku) };
            Cart.Add(new CartLine(product, item.Quantity, Recalculate, item.UnitPrice, locked: true, reserved: reserved));
        }
        Build = detail;
        if (detail.Build.Customer is { } name && Customers.FirstOrDefault(c => c.Name == name) is { } customer)
        {
            Customer = customer;
        }
        Recalculate();
        App.Notify.Info($"Armado {detail.Build.Number} en el carrito",
            $"{detail.QuotedItems.Count} piezas a precio cotizado · {Fmt.Money(Total)}" + (reserved ? " · la venta consume la reserva (el stock reservado sale una sola vez)" : string.Empty));
        await EnsureBuildSerialsAsync(force: false);
    }

    /// <summary>V4.2 · Series de las piezas serializadas del armado (se eligen todas juntas).</summary>
    private async Task<bool> EnsureBuildSerialsAsync(bool force)
    {
        var lines = Cart.Where(l => l.IsSerialized).ToList();
        if (lines.Count == 0 || (!force && lines.All(l => !l.MissingSerials)))
        {
            return true;
        }
        var specs = new List<SerialCaptureLineSpec>();
        foreach (var line in lines)
        {
            IReadOnlyList<MINV.Application.Tech.SerialRow> available;
            try
            {
                available = await App.SendAsync(new GetAvailableSerialsQuery(line.Sku));
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                App.Notify.Error("No se pudieron leer las series disponibles", AppServices.Describe(ex));
                return false;
            }
            specs.Add(new SerialCaptureLineSpec(line.Sku, line.Name, line.Product.SerialKind, (int)line.Quantity, SerialCaptureMode.Pick, available,
                line.Serials.ToList()));
        }
        var chosen = await SerialsDialog.AskAsync(App, $"Series del armado {_build?.Build.Number}",
            "Elija (o escanee) la unidad de cada pieza serializada que sale con el armado.", "Usar estas unidades", specs);
        if (chosen is null)
        {
            return false;
        }
        foreach (var line in lines)
        {
            var serials = chosen.First(c => c.Sku.Equals(line.Sku, StringComparison.OrdinalIgnoreCase)).Serials;
            line.Serials.Clear();
            // Varias piezas del mismo producto: cada línea toma las suyas en orden
            var mine = serials.Where(s => !lines.Where(o => !ReferenceEquals(o, line)).Any(o => o.Serials.Contains(s))).Take((int)line.Quantity).ToList();
            foreach (var serial in mine)
            {
                line.Serials.Add(serial);
            }
        }
        return true;
    }

    private async Task FromBuildAsync()
    {
        IReadOnlyList<PcBuildRow> builds;
        try
        {
            // V6 · También los armados reservados (web y escritorio): la venta consume la reserva (regla S-04)
            builds = (await App.SendAsync(new GetPcBuildsQuery())).Where(b => b.Status is PcBuildStatus.Quoted or PcBuildStatus.Reserved && !b.IsExpired).ToList();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudieron leer las cotizaciones", AppServices.Describe(ex));
            return;
        }
        if (builds.Count == 0)
        {
            App.Notify.Info("Sin cotizaciones vigentes", "Arme y cotice una PC en «Armador de PC» para cobrarla aquí.");
            return;
        }
        var dialog = new PickBuildDialog(builds, App.Now);
        if (await App.Dialogs.ShowAsync(dialog) && dialog.Selected is { } row)
        {
            await LoadBuildAsync(row.Number);
        }
    }

    private void Recalculate()
    {
        OnPropertiesChanged(nameof(Total), nameof(TotalText), nameof(TaxText), nameof(ItemsText), nameof(DiscountText), nameof(ChangeText),
            nameof(CheckoutText), nameof(IsCartEmpty), nameof(CashShort));
        // El carrito cambia sin tocar el teclado (clic en una tarjeta, escáner): se reevalúa «Cobrar» de inmediato
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async Task ClearCartAsync()
    {
        if (await App.Dialogs.ConfirmAsync("Vaciar el carrito", $"Se quitarán {Cart.Count} productos del carrito.", "Vaciar", "Volver", isDanger: true))
        {
            Cart.Clear();
            Build = null;
            CashReceived = string.Empty;
        }
    }

    private async Task OpenSessionAsync()
    {
        if (!Numbers.TryParse(_openingCash, out var opening, emptyIsZero: true) || opening < 0)
        {
            App.Notify.Warning("Fondo inicial", "Indique el efectivo con el que abre la caja (0 o más).");
            return;
        }
        if (await RunAsync(() => App.SendAsync(new OpenPosSessionCommand(_register!.Code, opening)), "No se pudo abrir la caja"))
        {
            App.Notify.Success("Caja abierta", $"{_register!.Name} con fondo de {Fmt.Money(opening)}.");
            ApplyState(await App.SendAsync(new GetPosStateQuery()));
            await RefreshFiscalAsync();
        }
    }

    private async Task CloseSessionAsync()
    {
        var session = Session!;
        var counted = await App.Dialogs.PromptAsync("Cerrar caja (arqueo)",
            $"Cuente el efectivo del cajón. Esperado: {Fmt.Money(session.ExpectedCash)} (fondo {Fmt.Money(session.OpeningCash)} + ventas en efectivo " +
            $"{Fmt.Money(session.CashSales)}). {session.Tickets} ventas por {Fmt.Money(session.Sales)}.",
            "Efectivo contado", [Numbers.Plain(session.ExpectedCash)], "Cerrar caja", Numbers.Plain(session.ExpectedCash), glyph: Glyphs.Lock);
        if (counted is null)
        {
            return;
        }
        if (!Numbers.TryParse(counted, out var amount) || amount < 0)
        {
            App.Notify.Warning("Monto no válido", "Escriba el efectivo contado, por ejemplo 1250,50.");
            return;
        }
        try
        {
            var difference = await App.SendAsync(new ClosePosSessionCommand(session.Id, amount));
            var detail = difference == 0 ? "Arqueo exacto." : difference > 0 ? $"Sobrante de {Fmt.Money(difference)}." : $"Faltante de {Fmt.Money(-difference)}.";
            App.Notify.Show(difference == 0 ? ToastKind.Success : ToastKind.Warning, "Caja cerrada", detail);
            Cart.Clear();
            LastSale = null;
            ApplyState(await App.SendAsync(new GetPosStateQuery()));
            await RefreshFiscalAsync();
        }
        catch (Exception ex)
        {
            App.Notify.Error("No se pudo cerrar la caja", AppServices.Describe(ex));
        }
    }

    private async Task CheckoutAsync()
    {
        if (_customer is null || _method is null)
        {
            App.Notify.Warning("Datos del cobro", "Elija el cliente y el medio de pago.");
            return;
        }
        decimal? cash = null;
        if (IsCash && Numbers.TryParse(_cash, out var received) && received > 0)
        {
            if (received < Total)
            {
                App.Notify.Warning("Efectivo insuficiente", $"Recibió {Fmt.Money(received)} y el total es {Fmt.Money(Total)}.");
                return;
            }
            cash = received;
        }
        if (NeedsReference && string.IsNullOrWhiteSpace(_reference))
        {
            App.Notify.Warning("Falta la referencia", $"El pago con {_method.Name} exige el número de operación o voucher.");
            return;
        }
        // V4.1 · Datos de facturación (nominatividad) y tarjeta (se envía; el caso de uso la guarda solo enmascarada)
        FiscalBuyerInput? buyer = null;
        string? card = null;
        if (IsBilling && _buyer is { } form)
        {
            if (form.Check() is { } problem)
            {
                App.Notify.Warning("Datos de facturación", problem);
                return;
            }
            buyer = form.ToInput();
            if (buyer is null && _customer.Code == "CF")
            {
                App.Notify.Warning("Falta el documento del comprador",
                    "Toda venta facturada lleva el número de documento del comprador (CI, NIT, pasaporte…). Si corresponde, use un NIT especial.");
                IsBuyerExpanded = true;
                return;
            }
            if (IsCard)
            {
                var digits = new string(_cardNumber.Where(char.IsAsciiDigit).ToArray());
                if (digits.Length is < 8 or > 19)
                {
                    App.Notify.Warning("Número de tarjeta", "Escriba el número de la tarjeta (se envía enmascarado: 4 primeros y 4 últimos dígitos).");
                    return;
                }
                card = digits;
            }
        }
        // V4.2 · Cada unidad serializada con su serie (la validación que manda es la del dominio)
        if (_build is not null && !await EnsureBuildSerialsAsync(force: false))
        {
            return;
        }
        if (Cart.FirstOrDefault(l => l.MissingSerials) is { } missing)
        {
            App.Notify.Warning("Faltan series", $"{missing.Name}: {missing.SerialsNeeded}.");
            return;
        }
        // V6 · Lo reservado para armados no se vende a otro cliente: aviso claro antes de cobrar (la validación que manda es la del
        // dominio, que rechaza la salida sin stock disponible)
        if (Cart.FirstOrDefault(l => l.ExceedsStock) is { } over)
        {
            App.Notify.Warning("Stock insuficiente", $"{over.Name}: pide {Fmt.Qty(over.Quantity)} y {over.ExceedsStockText.ToLower(Fmt.Culture)}. " +
                                                     "Baje la cantidad o libere la reserva desde el Armador de PC.");
            return;
        }
        try
        {
            CheckoutResult result;
            var consumedReservation = false;
            if (_build is { } build)
            {
                var serials = Cart.Where(l => l.HasSerials).GroupBy(l => l.Sku)
                    .Select(g => new SkuSerials(g.Key, g.SelectMany(l => l.Serials).ToList())).ToList();
                consumedReservation = build.Build.Status == PcBuildStatus.Reserved;
                result = await App.SendAsync(new SellPcBuildCommand(build.Build.Number, _method.Code, serials, cash, NeedsReference ? _reference.Trim() : null,
                    buyer, card, _customer.Code));
                Build = null;
            }
            else
            {
                var lines = Cart.Select(l => new SaleLineInput(l.Sku, l.Quantity, l.Discount, l.HasSerials ? l.Serials.ToList() : null)).ToList();
                result = await App.SendAsync(new CheckoutCommand(_customer.Code, _method.Code, lines, cash, NeedsReference ? _reference.Trim() : null, buyer, card));
            }
            CardNumber = string.Empty;
            LastSale = result;
            App.Notify.Success($"Venta {result.InvoiceNumber} cobrada",
                (result.Change > 0 ? $"Total {Fmt.Money(result.Total)} · entregue vuelto de {Fmt.Money(result.Change)}" : $"Total {Fmt.Money(result.Total)}")
                + (consumedReservation ? " · reserva consumida: el stock reservado salió con la venta" : string.Empty));
            Cart.Clear();
            CashReceived = string.Empty;
            Reference = string.Empty;
            Customer = Customers.FirstOrDefault(c => c.Code == "CF") ?? Customer;
            App.Data.Invalidate();
            if (result.FiscalDocumentId is { } documentId)
            {
                // V4.1 · Se envía ya al SIN y se imprime el documento DEFINITIVO (validado o re-emitido fuera de línea)
                await CompleteFiscalAsync(result, documentId);
            }
            else if (App.Settings.PrinterTarget is { Length: > 0 })
            {
                await PrintAsync();
            }
            else
            {
                ReceiptText = RenderText(result);
            }
            await LoadAsync(force: true);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo cobrar", AppServices.Describe(ex));
            await LoadAsync(force: true);
        }
    }

    /// <summary>
    /// V4.1 · Después de cobrar: envía el documento al SIN (DispatchFiscalDocumentsCommand), sigue sus reemplazos hasta el
    /// documento FINAL, imprime su rollo fiscal (si la caja tiene impresora) y muestra el resultado (número, CUF, estado y,
    /// si el SIN lo rechazó, sus mensajes con la opción de corregir el comprador y re-emitir).
    /// </summary>
    private async Task CompleteFiscalAsync(CheckoutResult sale, Guid documentId)
    {
        string? notice = null;
        try
        {
            var dispatch = await App.SendAsync(new DispatchFiscalDocumentsCommand(documentId));
            if (dispatch.WentOffline > 0)
            {
                notice = "Sin comunicación con el SIN: la caja pasó a fuera de línea y la factura se envía sola al volver la conexión.";
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            notice = "La factura quedó pendiente de envío (se envía sola): " + AppServices.Describe(ex);
        }
        try
        {
            var detail = await App.SendAsync(new GetFiscalDocumentQuery(documentId));
            for (var hops = 0; hops < 5 && detail.ReplacedByDocumentId is { } next; hops++)
            {
                detail = await App.SendAsync(new GetFiscalDocumentQuery(next));
            }
            FiscalPrintModel? model = null;
            try
            {
                model = await App.SendAsync(new GetFiscalPrintModelQuery(detail.Row.Id));
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                System.Diagnostics.Trace.TraceWarning("M-INV · ticket fiscal: {0}", ex.Message);
            }
            var printed = false;
            if (FiscalOutput.HasPrinter(App.Settings))
            {
                try
                {
                    printed = await FiscalOutput.PrintRollAsync(App, detail.Row.Id, openDrawer: IsCash);
                }
                catch (Exception ex) when (AppServices.IsExpected(ex) || ex is System.IO.IOException or UnauthorizedAccessException or TimeoutException
                                               or OperationCanceledException or System.Net.Sockets.SocketException or ArgumentException)
                {
                    App.Notify.Error("No se pudo imprimir la factura", AppServices.IsExpected(ex) ? AppServices.Describe(ex) : ex.Message);
                }
            }
            var result = new PosFiscalResult(App, sale, detail, model, printed, notice, IsCash);
            result.Closed += (_, _) => FiscalResult = null;
            FiscalResult = result;
            _buyer?.Clear();
            if (detail.Row.Status is FiscalDocumentStatus.Rejected or FiscalDocumentStatus.PackageRejected)
            {
                App.Notify.Error($"El SIN rechazó la factura N° {detail.Row.Number}", "Revise los mensajes y re-emita con los datos corregidos.");
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Warning("Venta cobrada; la factura se verá en Documentos fiscales", AppServices.Describe(ex));
            ReceiptText = RenderText(sale);
        }
        await RefreshFiscalAsync();
    }

    private Receipt ToReceipt(CheckoutResult r) => new(_state?.CompanyName ?? App.Session.Workspace.CompanyName, _state?.TaxId,
        _state?.BranchName ?? "", r.InvoiceNumber, r.IssuedAt, App.Session.DisplayName, r.Lines, r.Total, Fmt.CurrencySymbol, r.PaymentMethod, r.InvoiceNumber);

    private async Task PrintAsync()
    {
        if (_last is not { } sale)
        {
            return;
        }
        var s = App.Settings;
        if (ReceiptPrinters.Create(new HardwareOptions(s.PrinterKind, s.PrinterTarget, s.BaudRate)) is not { } printer)
        {
            ReceiptText = RenderText(sale);
            App.Notify.Info("Sin impresora configurada", "Se muestra el ticket en pantalla. Configure la impresora en Configuración.");
            return;
        }
        try
        {
            var document = ReceiptRenderer.Render(ToReceipt(sale), s.PrinterColumns, openDrawer: IsCash);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Task.Run(() => printer.PrintAsync(document, timeout.Token), timeout.Token);
            App.Notify.Success("Ticket impreso", $"{sale.InvoiceNumber} en {printer.Name}");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException
                                       or OperationCanceledException or System.Net.Sockets.SocketException or ArgumentException)
        {
            ReceiptText = RenderText(sale);
            App.Notify.Error("No se pudo imprimir", ex.Message);
        }
    }

    /// <summary>Ticket en texto de ancho fijo (40 columnas), para la vista previa en pantalla.</summary>
    private string RenderText(CheckoutResult r)
    {
        const int w = 40;
        var sb = new StringBuilder();
        void Center(string text) => sb.AppendLine(text.Length >= w ? text[..w] : new string(' ', (w - text.Length) / 2) + text);
        void Pair(string left, string right) => sb.AppendLine(left.Length + right.Length + 1 > w
            ? left[..Math.Max(0, w - right.Length - 1)] + " " + right
            : left + new string(' ', w - left.Length - right.Length) + right);
        Center(_state?.CompanyName ?? "");
        if (_state?.TaxId is { Length: > 0 } nit)
        {
            Center("NIT " + nit);
        }
        Center(_state?.BranchName ?? "");
        sb.AppendLine(new string('-', w));
        Pair("FACTURA " + r.InvoiceNumber, r.IssuedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
        sb.AppendLine("Cliente: " + r.Customer);
        sb.AppendLine("Cajero:  " + App.Session.DisplayName);
        sb.AppendLine(new string('-', w));
        foreach (var line in r.Lines)
        {
            sb.AppendLine(line.Description.Length > w ? line.Description[..w] : line.Description);
            Pair($"  {Fmt.Qty(line.Quantity)} x {line.UnitPrice.ToString("N2", Fmt.Culture)}", line.Amount.ToString("N2", Fmt.Culture));
            // V4.2 · Series o IMEI vendidos y garantía derivada de la venta (reglas T-03 y T-04)
            if (line.SerialsText is { Length: > 0 } serials)
            {
                foreach (var chunk in serials.Chunk(w - 2))
                {
                    sb.AppendLine("  " + new string(chunk));
                }
            }
            if (line.WarrantyUntil is { } until)
            {
                sb.AppendLine("  " + TechPrint.Warranty(until));
            }
        }
        sb.AppendLine(new string('-', w));
        Pair("TOTAL " + Fmt.CurrencySymbol, r.Total.ToString("N2", Fmt.Culture));
        Pair($"IVA incluido ({TaxRate:0.##} %)", r.Tax.ToString("N2", Fmt.Culture));
        Pair("Pago: " + r.PaymentMethod, "");
        if (r.Change > 0)
        {
            Pair("Vuelto", r.Change.ToString("N2", Fmt.Culture));
        }
        sb.AppendLine(new string('-', w));
        Center("¡Gracias por su compra!");
        return sb.ToString();
    }
}

/// <summary>V4.2 · Parámetro de navegación «Vender en caja» (del armador a la caja).</summary>
public sealed record PcBuildToSell(string Number);

/// <summary>V4.2 · Elegir una cotización vigente del armador para cobrarla en la caja.</summary>
public sealed class PickBuildDialog : FormDialog
{
    private PcBuildItem? _selected;

    public PickBuildDialog(IReadOnlyList<PcBuildRow> builds, DateTimeOffset now)
        : base("Cobrar un armado cotizado", "Cargar en el carrito", Glyphs.Monitor, width: 620)
    {
        Builds = builds.Select(b => new PcBuildItem(b, now)).ToList();
        _selected = Builds.FirstOrDefault();
    }

    public override string? Subtitle => "Cotizaciones y reservas vigentes del armador de PC: se cobran a sus precios congelados, con las series de cada pieza " +
                                        "(la venta de una reserva la consume).";

    public IReadOnlyList<PcBuildItem> Builds { get; }

    public PcBuildItem? Selected { get => _selected; set => Set(ref _selected, value); }

    protected override bool CanConfirm() => _selected is not null;

    protected override Task<bool> SubmitAsync() => Task.FromResult(_selected is not null);
}
