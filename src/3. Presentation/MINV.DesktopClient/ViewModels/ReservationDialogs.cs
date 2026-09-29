using System.Collections.ObjectModel;
using MINV.Application.Partners;
using MINV.Application.Sales;
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Sales;

namespace MINV.DesktopClient.ViewModels;

/// <summary>V7 · Producto que se puede agregar a una reserva de mostrador (precio de la lista y disponible en la sucursal).</summary>
public sealed class CounterProductOption(SellableProduct product)
{
    public SellableProduct Product { get; } = product;

    public string Sku => Product.Sku;

    public string Name => Product.Name;

    public string Category => Product.Category;

    public string PriceText => Fmt.Money(Product.Price);

    public bool IsOut => Product.Available <= 0;

    public string AvailableText => IsOut ? "Sin disponible" : $"Disponible {Fmt.Qty(Product.Available)}";
}

/// <summary>V7 · Línea de la reserva de mostrador (cantidad entera de 1 a 16, como en la tienda web, regla S-05).</summary>
public sealed class CounterCartLine : ObservableObject
{
    private int _quantity = 1;

    public CounterCartLine(SellableProduct product, Action changed)
    {
        Product = product;
        Increase = new RelayCommand(() =>
        {
            Quantity = Math.Min(PcBuild.MaxQuantity, _quantity + 1);
            changed();
        }, () => _quantity < PcBuild.MaxQuantity);
        Decrease = new RelayCommand(() =>
        {
            Quantity = Math.Max(1, _quantity - 1);
            changed();
        }, () => _quantity > 1);
    }

    public SellableProduct Product { get; }

    public string Sku => Product.Sku;

    public string Name => Product.Name;

    public int Quantity
    {
        get => _quantity;
        set
        {
            if (Set(ref _quantity, Math.Clamp(value, 1, PcBuild.MaxQuantity)))
            {
                OnPropertiesChanged(nameof(QuantityText), nameof(SubtotalText), nameof(ExceedsAvailable), nameof(AvailableText));
            }
        }
    }

    public string QuantityText => $"× {_quantity}";

    public decimal Subtotal => Product.Price * _quantity;

    public string SubtotalText => Fmt.Money(Subtotal);

    public string UnitPriceText => Fmt.Money(Product.Price);

    /// <summary>Guía: pide más de lo disponible (la reserva es todo o nada; el dominio lo rechaza con el detalle).</summary>
    public bool ExceedsAvailable => _quantity > Product.Available;

    public string AvailableText => ExceedsAvailable ? $"Solo hay {Fmt.Qty(Math.Max(0, Product.Available))} disponible{(Product.Available == 1 ? "" : "s")}" : string.Empty;

    public RelayCommand Increase { get; }

    public RelayCommand Decrease { get; }

    public CartItemInput ToInput() => new(Sku, _quantity);
}

/// <summary>
/// V7 · «Nueva reserva en mostrador» (ReserveCartCommand, regla P-05): el cliente (registrado u ocasional), el teléfono o
/// WhatsApp para avisarle, el correo opcional (recibe la confirmación), los productos elegidos con el buscador, los días para
/// recogerla (1 a 3), notas y, si los da, los datos para la factura (la caja los precarga al cobrar). La reserva es todo o
/// nada en la sucursal activa; la validación que manda es la del caso de uso.
/// </summary>
public sealed class CounterReservationDialog : FormDialog
{
    private static readonly Choice<string?> NoCustomer = new("Cliente ocasional (sin registrar)", null);
    private readonly AppServices _app;
    private List<SellableProduct> _products = [];
    private Dictionary<string, CustomerRow> _customers = new(StringComparer.OrdinalIgnoreCase);
    private Choice<string?> _customer = NoCustomer;
    private string _contactName = string.Empty;
    private string _contactPhone = string.Empty;
    private string _contactEmail = string.Empty;
    private string _notes = string.Empty;
    private Choice<int> _holdDays;
    private Choice<int?> _documentType;
    private string _documentNumber = string.Empty;
    private string _complement = string.Empty;
    private string _buyerName = string.Empty;
    private string _search = string.Empty;

    public CounterReservationDialog(AppServices app)
        : base("Nueva reserva en mostrador", "Reservar", Glyphs.Clock, width: 820)
    {
        _app = app;
        HoldOptions = [new("1 día (24 horas)", 1), new("2 días (48 horas)", 2), new("3 días (72 horas)", 3)];
        _holdDays = HoldOptions[1];
        DocumentTypes = [new("Sin datos de factura", null), .. DocumentTypeOption.From(null).Select(t => new Choice<int?>(t.Label, t.Code))];
        _documentType = DocumentTypes[0];
        Customers.ReplaceAll([NoCustomer]);
        AddProduct = new RelayCommand<CounterProductOption>(Add);
        RemoveLine = new RelayCommand<CounterCartLine>(line =>
        {
            Lines.Remove(line);
            Changed();
        });
        Lines.CollectionChanged += (_, _) => Changed();
    }

    public override string? Subtitle => $"Se reserva en {_app.Session.BranchText} a los precios de hoy; el stock queda apartado hasta que el cliente pase a pagar.";

    /// <summary>La reserva creada (después de confirmar).</summary>
    public PcBuildRow? Result { get; private set; }

    public BulkObservableCollection<Choice<string?>> Customers { get; } = [];

    public IReadOnlyList<Choice<int>> HoldOptions { get; }

    public IReadOnlyList<Choice<int?>> DocumentTypes { get; }

    public ObservableCollection<CounterCartLine> Lines { get; } = [];

    public BulkObservableCollection<CounterProductOption> Suggestions { get; } = [];

    public RelayCommand<CounterProductOption> AddProduct { get; }

    public RelayCommand<CounterCartLine> RemoveLine { get; }

    /// <summary>Cliente registrado: completa nombre, teléfono y correo si están vacíos.</summary>
    public Choice<string?> Customer
    {
        get => _customer;
        set
        {
            if (Set(ref _customer, value ?? NoCustomer) && _customer.Value is { } code && _customers.TryGetValue(code, out var row))
            {
                if (string.IsNullOrWhiteSpace(_contactName))
                {
                    ContactName = row.Name;
                }
                if (string.IsNullOrWhiteSpace(_contactPhone) && !string.IsNullOrWhiteSpace(row.Phone))
                {
                    ContactPhone = row.Phone;
                }
                if (string.IsNullOrWhiteSpace(_contactEmail) && !string.IsNullOrWhiteSpace(row.Email))
                {
                    ContactEmail = row.Email;
                }
            }
        }
    }

    public string ContactName { get => _contactName; set => SetField(ref _contactName, value); }

    public string ContactPhone { get => _contactPhone; set => SetField(ref _contactPhone, value); }

    public string ContactEmail { get => _contactEmail; set => Set(ref _contactEmail, value ?? string.Empty); }

    public string Notes { get => _notes; set => Set(ref _notes, value ?? string.Empty); }

    public Choice<int> HoldDays { get => _holdDays; set => Set(ref _holdDays, value ?? HoldOptions[1]); }

    public Choice<int?> DocumentType
    {
        get => _documentType;
        set
        {
            if (Set(ref _documentType, value ?? DocumentTypes[0]))
            {
                OnPropertiesChanged(nameof(HasDocument), nameof(IsCi));
            }
        }
    }

    public bool HasDocument => _documentType.Value is not null;

    public bool IsCi => _documentType.Value == MINV.Domain.Billing.SiatCodes.DocumentCi;

    public string DocumentNumber { get => _documentNumber; set => Set(ref _documentNumber, (value ?? string.Empty).Trim()); }

    public string Complement { get => _complement; set => Set(ref _complement, (value ?? string.Empty).Trim().ToUpperInvariant()); }

    public string BuyerName { get => _buyerName; set => Set(ref _buyerName, value ?? string.Empty); }

    /// <summary>Buscador de productos (SKU, nombre o código de barras, sin importar tildes).</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? string.Empty))
            {
                FilterSuggestions();
            }
        }
    }

    public bool NoSuggestions => _search.Trim().Length > 0 && Suggestions.Count == 0;

    public bool HasLines => Lines.Count > 0;

    public decimal Total => Lines.Sum(l => l.Subtotal);

    public string TotalText => Fmt.Money(Total);

    public string LinesText => Lines.Sum(l => l.Quantity) is var n && n == 1 ? "1 unidad" : $"{n} unidades";

    /// <summary>Lee los clientes registrados y los productos vendibles de la sucursal activa.</summary>
    public async Task LoadAsync()
    {
        try
        {
            _products = (await _app.SendAsync(new GetSellableProductsQuery())).ToList();
            var customers = await _app.SendAsync(new GetCustomersQuery());
            _customers = customers.Customers.Where(c => c.IsActive && c.Code != "CF").ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
            Customers.ReplaceAll([NoCustomer, .. _customers.Values.OrderBy(c => c.Name, StringComparer.Create(Fmt.Culture, true))
                .Select(c => new Choice<string?>($"{c.Name} ({c.Code})", c.Code))]);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Error = "No se pudieron leer los productos o los clientes: " + AppServices.Describe(ex);
        }
    }

    protected override bool CanConfirm() => Lines.Count > 0 && _contactName.Trim().Length > 0 && _contactPhone.Trim().Length > 0;

    protected override async Task<bool> SubmitAsync()
    {
        if (Lines.FirstOrDefault(l => l.ExceedsAvailable) is { } over)
        {
            Error = $"{over.Name}: pide {over.Quantity} y hay {Fmt.Qty(Math.Max(0, over.Product.Available))} disponible{(over.Product.Available == 1 ? "" : "s")}. " +
                    "La reserva es todo o nada: baje la cantidad o quite el producto.";
            return false;
        }
        ReservationBuyerInput? buyer = null;
        if (_documentType.Value is { } type)
        {
            if (_documentNumber.Length == 0)
            {
                Error = "Escriba el número de documento para la factura o elija «Sin datos de factura».";
                return false;
            }
            buyer = new ReservationBuyerInput(type, _documentNumber, IsCi && _complement.Length > 0 ? _complement : null,
                string.IsNullOrWhiteSpace(_buyerName) ? null : _buyerName.Trim());
        }
        Result = await _app.SendAsync(new ReserveCartCommand(Lines.Select(l => l.ToInput()).ToList(), _contactName.Trim(), _contactPhone.Trim(),
            string.IsNullOrWhiteSpace(_contactEmail) ? null : _contactEmail.Trim(), string.IsNullOrWhiteSpace(_notes) ? null : _notes.Trim(),
            _holdDays.Value, buyer, _customer.Value));
        return true;
    }

    private void Add(CounterProductOption option)
    {
        if (Lines.FirstOrDefault(l => l.Sku == option.Sku) is { } line)
        {
            line.Increase.Execute(null);
        }
        else if (Lines.Count >= PcBuild.MaxLines)
        {
            Error = $"Una reserva admite como máximo {PcBuild.MaxLines} productos distintos.";
            return;
        }
        else
        {
            Lines.Add(new CounterCartLine(option.Product, Changed));
        }
        Error = null;
        Search = string.Empty;
    }

    private void FilterSuggestions()
    {
        var q = _search.Trim();
        Suggestions.ReplaceAll(q.Length == 0
            ? []
            : _products.Where(p => p.Sku.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Barcodes.Any(b => b.Contains(q, StringComparison.Ordinal))
                                   || FilterChoices.Contains(p.Name, q) || FilterChoices.Contains(p.Category, q))
                .OrderByDescending(p => p.Sku.Equals(q, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(p => p.Available > 0)
                .ThenBy(p => p.Name, StringComparer.Create(Fmt.Culture, true))
                .Take(8).Select(p => new CounterProductOption(p)));
        OnPropertyChanged(nameof(NoSuggestions));
    }

    private void SetField(ref string field, string? value)
    {
        if (!string.Equals(field, value ?? string.Empty, StringComparison.Ordinal))
        {
            field = value ?? string.Empty;
            OnPropertiesChanged(nameof(ContactName), nameof(ContactPhone));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private void Changed()
    {
        OnPropertiesChanged(nameof(HasLines), nameof(Total), nameof(TotalText), nameof(LinesText));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
}
