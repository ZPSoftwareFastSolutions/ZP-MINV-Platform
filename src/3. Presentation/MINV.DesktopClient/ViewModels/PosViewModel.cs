using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MINV.Application.Abstractions;
using MINV.Application.Sales;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
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

    public string AvailableText => IsOut ? "Agotado" : $"{Fmt.Qty(_available)} {Product.Unit}";

    public bool IsOut => _available <= 0;

    public bool IsLow => !IsOut && _available <= 3;
}

/// <summary>Línea del carrito (cantidad y descuento editables; el importe incluye el IVA).</summary>
public sealed class CartLine : ObservableObject
{
    private readonly Action _changed;
    private decimal _quantity;
    private int _discount;

    public CartLine(PosProduct product, decimal quantity, Action changed)
    {
        Product = product;
        _quantity = quantity;
        _changed = changed;
    }

    public PosProduct Product { get; }

    public string Name => Product.Name;

    public string Sku => Product.Sku;

    public ImageSource? Image => Product.Image;

    public string UnitPriceText => $"{Fmt.Money(Product.Product.Price)} / {Product.Product.Unit}";

    public static IReadOnlyList<int> Discounts { get; } = [0, 5, 10, 15, 20, 25];

    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (Set(ref _quantity, value))
            {
                OnPropertiesChanged(nameof(QuantityText), nameof(Amount), nameof(AmountText), nameof(ExceedsStock));
                _changed();
            }
        }
    }

    public string QuantityText
    {
        get => Fmt.Qty(_quantity);
        set
        {
            if (Fmt.TryParseQuantity(value, out var q) && q > 0)
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

    public decimal Amount => decimal.Round(_quantity * Product.Product.Price * (1 - _discount / 100m), 2, MidpointRounding.AwayFromZero);

    public string AmountText => Fmt.Money(Amount);

    /// <summary>Guía visual (poka-yoke): la cantidad supera lo disponible. La venta la bloquea el dominio.</summary>
    public bool ExceedsStock => _quantity > Product.Available;

    public decimal Step => Product.Product.AllowsDecimals ? 0.5m : 1m;
}

/// <summary>
/// Punto de venta: cuadrícula de productos con imagen (búsqueda, categorías, escáner), carrito con descuentos, cliente y
/// medio de pago en combos, cobro con vuelto, ticket en pantalla o impreso, y apertura / cierre de caja con arqueo.
/// </summary>
public sealed class PosViewModel : PageViewModel, IScannerTarget
{
    private const string AllCategories = "ALL";
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

    public PosViewModel(AppServices app) : base(app, "pos", "Punto de venta", "Vender, cobrar y emitir la factura", Glyphs.Cart)
    {
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
        Add = new RelayCommand<PosProduct>(AddProduct);
        Increase = new RelayCommand<CartLine>(l => l.Quantity += l.Step);
        Decrease = new RelayCommand<CartLine>(l =>
        {
            if (l.Quantity - l.Step <= 0)
            {
                Cart.Remove(l);
            }
            else
            {
                l.Quantity -= l.Step;
            }
        });
        Remove = new RelayCommand<CartLine>(l => Cart.Remove(l));
        ClearCart = new AsyncRelayCommand(ClearCartAsync, () => Cart.Count > 0);
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
                OnPropertiesChanged(nameof(IsCash), nameof(NeedsReference), nameof(ChangeText), nameof(CheckoutText));
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

    public string TaxText => $"IVA incluido ({TaxRate:0.##} %): {Fmt.Money(decimal.Round(Total * TaxRate / (100 + TaxRate), 2))}";

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

    public string CheckoutText => Cart.Count == 0 ? "Agregue productos" : $"Cobrar {Fmt.Money(Total)}";

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
        var sellable = await App.SendAsync(new GetSellableProductsQuery());
        var images = await App.Images.AllAsync(force);
        var inCart = Cart.ToDictionary(l => l.Sku, l => l);
        _products = sellable.Select(p => new PosProduct(p, images.GetValueOrDefault(p.Sku))).ToList();
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
        Subtitle = $"{state.CompanyName} · {state.BranchName}";
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(NoProducts));
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
        Register ??= Registers.FirstOrDefault();
        OnPropertiesChanged(nameof(IsOpen), nameof(IsClosed), nameof(Session), nameof(SessionTitle), nameof(SessionText), nameof(TaxText));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
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
        try
        {
            var lines = Cart.Select(l => new SaleLineInput(l.Sku, l.Quantity, l.Discount)).ToList();
            var result = await App.SendAsync(new CheckoutCommand(_customer.Code, _method.Code, lines, cash, NeedsReference ? _reference.Trim() : null));
            LastSale = result;
            App.Notify.Success($"Venta {result.InvoiceNumber} cobrada",
                result.Change > 0 ? $"Total {Fmt.Money(result.Total)} · entregue vuelto de {Fmt.Money(result.Change)}" : $"Total {Fmt.Money(result.Total)}");
            Cart.Clear();
            CashReceived = string.Empty;
            Reference = string.Empty;
            Customer = Customers.FirstOrDefault(c => c.Code == "CF") ?? Customer;
            App.Data.Invalidate();
            if (App.Settings.PrinterTarget is { Length: > 0 })
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
