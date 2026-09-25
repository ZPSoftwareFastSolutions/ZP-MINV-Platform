using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using MINV.Application.Catalog;
using MINV.Application.Partners;
using MINV.Application.Sales;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.DesktopClient.ViewModels;

public sealed class SaleItem(SaleRow r)
{
    public SaleRow Row { get; } = r;

    public string Invoice => Row.InvoiceNumber;

    public string WhenText => Row.IssuedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public DateTimeOffset IssuedAt => Row.IssuedAt;

    public string Customer => Row.Customer;

    public string Cashier => Row.Cashier;

    public string Method => Row.PaymentMethod;

    public int Items => Row.Items;

    public string ItemsText => Row.Items == 1 ? "1 línea" : $"{Row.Items} líneas";

    public decimal Total => Row.Total;

    public string TotalText => Fmt.Money(Row.Total);

    public bool IsVoided => Row.Status == InvoiceStatus.Voided;

    public string StatusText => IsVoided ? "ANULADA" : "EMITIDA";

    public string Initials => Fmt.Initials(Row.Cashier);
}

/// <summary>
/// Historial de ventas (facturas): período, medio de pago, cajero y estado en combos; detalle de la factura, anulación
/// con motivo (devuelve el stock y registra el asiento inverso) y exportación.
/// </summary>
public sealed class SalesViewModel : PageViewModel
{
    private static readonly Choice<string?> AllMethods = new("Todos los medios de pago", null);
    private static readonly Choice<string?> AllCashiers = new("Todos los cajeros", null);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private List<SaleItem> _items = [];
    private PeriodOption? _period;
    private Choice<string?> _method = AllMethods;
    private Choice<string?> _cashier = AllCashiers;
    private Choice<string> _status;
    private string _search = string.Empty;
    private SaleItem? _selected;
    private IReadOnlyList<SaleLineRow> _lines = [];
    private string _summary = string.Empty;

    public SalesViewModel(AppServices app) : base(app, "ventas", "Ventas", "Facturas emitidas, detalle y anulaciones", Glyphs.Money)
    {
        Statuses = [new("Todas", "all"), new("Emitidas", "issued"), new("Anuladas", "voided")];
        _status = Statuses[0];
        Rows = CollectionViewSource.GetDefaultView(_items);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ApplyFilter();
        };
        Void = new AsyncRelayCommand(VoidAsync, () => _selected is { IsVoided: false } && CanVoid);
        Export = new RelayCommand(ExportCsv, () => _items.Count > 0);
        GoToPos = new RelayCommand(() => app.Navigator.Navigate("pos"));
    }

    public ICollectionView Rows { get; private set; }

    public BulkObservableCollection<PeriodOption> Periods { get; } = [];

    public BulkObservableCollection<Choice<string?>> Methods { get; } = [];

    public BulkObservableCollection<Choice<string?>> Cashiers { get; } = [];

    public IReadOnlyList<Choice<string>> Statuses { get; }

    public bool CanVoid => App.Session.Can(PermissionCodes.PosOperate);

    public bool CanSell => App.Session.Can(PermissionCodes.PosOperate);

    public PeriodOption? Period
    {
        get => _period;
        set
        {
            if (Set(ref _period, value) && value is not null && HasLoaded)
            {
                _ = LoadAsync(force: false);
            }
        }
    }

    public Choice<string?> Method { get => _method; set { if (Set(ref _method, value ?? AllMethods)) { ApplyFilter(); } } }

    public Choice<string?> Cashier { get => _cashier; set { if (Set(ref _cashier, value ?? AllCashiers)) { ApplyFilter(); } } }

    public Choice<string> Status { get => _status; set { if (Set(ref _status, value ?? Statuses[0])) { ApplyFilter(); } } }

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

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public KpiCard TotalKpi { get; } = new("Vendido", Glyphs.Money, "Success", "SuccessSoft");

    public KpiCard TicketsKpi { get; } = new("Facturas", Glyphs.Report);

    public KpiCard AverageKpi { get; } = new("Ticket promedio", Glyphs.Cart, "Info", "InfoSoft");

    public KpiCard VoidedKpi { get; } = new("Anuladas", Glyphs.Error, "Danger", "DangerSoft");

    public SaleItem? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                _ = LoadLinesAsync();
            }
        }
    }

    public bool HasSelection => _selected is not null;

    public IReadOnlyList<SaleLineRow> Lines { get => _lines; private set => Set(ref _lines, value); }

    public bool IsEmpty => HasLoaded && Rows.IsEmpty;

    public AsyncRelayCommand Void { get; }

    public RelayCommand Export { get; }

    public RelayCommand GoToPos { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        if (Periods.Count == 0)
        {
            var today = DateOnly.FromDateTime(App.Now.ToLocalTime().DateTime);
            Periods.ReplaceAll(PeriodOption.Presets(today));
            _period = Periods.First(p => p.Label == "Últimos 7 días");
            OnPropertyChanged(nameof(Period));
        }
        var period = _period ?? Periods[0];
        var rows = await App.SendAsync(new GetSalesQuery(period.From, period.To));
        _items = rows.Select(r => new SaleItem(r)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = Matches;
        Rows.SortDescriptions.Add(new SortDescription(nameof(SaleItem.IssuedAt), ListSortDirection.Descending));
        OnPropertyChanged(nameof(Rows));
        var method = _method.Value;
        var cashier = _cashier.Value;
        Methods.ReplaceAll(new[] { AllMethods }.Concat(rows.Select(r => r.PaymentMethod).Distinct().Order().Select(m => new Choice<string?>(m, m))));
        Cashiers.ReplaceAll(new[] { AllCashiers }.Concat(rows.Select(r => r.Cashier).Distinct().Order().Select(c => new Choice<string?>(c, c))));
        _method = Methods.FirstOrDefault(m => m.Value == method) ?? AllMethods;
        _cashier = Cashiers.FirstOrDefault(c => c.Value == cashier) ?? AllCashiers;
        OnPropertiesChanged(nameof(Method), nameof(Cashier));
        Subtitle = $"{period.Label} · {period.RangeText}";
        OnPropertyChanged(nameof(Subtitle));
        ApplyFilter();
    }

    private bool Matches(object o)
    {
        if (o is not SaleItem s)
        {
            return false;
        }
        if ((_method.Value is { } m && s.Method != m) || (_cashier.Value is { } c && s.Cashier != c))
        {
            return false;
        }
        if ((_status.Value == "issued" && s.IsVoided) || (_status.Value == "voided" && !s.IsVoided))
        {
            return false;
        }
        var q = _search.Trim();
        return q.Length == 0 || s.Invoice.Contains(q, StringComparison.OrdinalIgnoreCase)
               || Fmt.Culture.CompareInfo.IndexOf(s.Customer, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        var visible = Rows.Cast<SaleItem>().ToList();
        var issued = visible.Where(v => !v.IsVoided).ToList();
        TotalKpi.Value = Fmt.Money(issued.Sum(v => v.Total));
        TotalKpi.Detail = $"IVA incluido {Fmt.Money(issued.Sum(v => v.Row.Tax))}";
        TicketsKpi.Value = issued.Count.ToString("N0", Fmt.Culture);
        TicketsKpi.Detail = $"{issued.Sum(v => v.Items)} líneas vendidas";
        AverageKpi.Value = issued.Count > 0 ? Fmt.Money(issued.Average(v => v.Total)) : "—";
        AverageKpi.Detail = issued.Count > 0 ? $"Mayor: {Fmt.Money(issued.Max(v => v.Total))}" : null;
        VoidedKpi.Value = visible.Count(v => v.IsVoided).ToString("N0", Fmt.Culture);
        VoidedKpi.Detail = visible.Any(v => v.IsVoided) ? $"{Fmt.Money(visible.Where(v => v.IsVoided).Sum(v => v.Total))} anulados" : "Sin anulaciones";
        Summary = $"{visible.Count} de {_items.Count} facturas";
        OnPropertyChanged(nameof(IsEmpty));
        if (_selected is not null && !visible.Contains(_selected))
        {
            Selected = null;
        }
    }

    private async Task LoadLinesAsync()
    {
        if (_selected is not { } sale)
        {
            Lines = [];
            return;
        }
        try
        {
            Lines = await App.SendAsync(new GetSaleLinesQuery(sale.Invoice));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Lines = [];
            App.Notify.Error("No se pudo leer la factura", AppServices.Describe(ex));
        }
    }

    private async Task VoidAsync()
    {
        var sale = _selected!;
        var reason = await App.Dialogs.PromptAsync($"Anular la factura {sale.Invoice}",
            $"Total {sale.TotalText} de {sale.Customer}. El stock vuelve con una devolución y se registra el asiento inverso. No se puede deshacer.",
            "Motivo de la anulación",
            ["Error de cobro: el cliente cambió de producto", "Devolución del cliente", "Producto defectuoso", "Venta duplicada", "Error en el precio"],
            "Anular factura", isDanger: true, placeholder: "Elija o escriba el motivo");
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }
        try
        {
            var message = await App.SendAsync(new VoidSaleCommand(sale.Invoice, reason));
            App.Notify.Success("Factura anulada", message.TrimStart('✔', ' '));
            App.Data.Invalidate();
            await LoadAsync(force: true);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo anular", AppServices.Describe(ex));
        }
    }

    private void ExportCsv()
    {
        var rows = Rows.Cast<SaleItem>().ToList();
        var path = FileDialogs.SaveCsv($"ventas-{_period?.From:yyyyMMdd}-{_period?.To:yyyyMMdd}.csv");
        if (path is null)
        {
            return;
        }
        Csv.Write(path, ["Factura", "Pedido", "Fecha", "Cliente", "Cajero", "Medio de pago", "Líneas", "Total", "IVA", "Estado", "Motivo de anulación"],
            rows.Select(r => new object?[]
            {
                r.Invoice, r.Row.OrderNumber, r.WhenText, r.Customer, r.Cashier, r.Method, r.Items, r.Total, r.Row.Tax, r.StatusText, r.Row.VoidReason,
            }));
        App.Notify.Success("Ventas exportadas", $"{rows.Count} facturas en {Path.GetFileName(path)}");
    }
}

public sealed class CustomerItem(CustomerRow r)
{
    public CustomerRow Row { get; } = r;

    public string Code => Row.Code;

    public string Name => Row.Name;

    public string Initials => Fmt.Initials(Row.Name);

    public string Contact => string.Join(" · ", new[] { Row.Email, Row.Phone }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public string TaxText => string.IsNullOrWhiteSpace(Row.TaxId) ? "Sin NIT" : "NIT " + Row.TaxId;

    public string Category => Row.Category;

    public int Purchases => Row.Purchases;

    public decimal Total => Row.Total;

    public string TotalText => Row.Total > 0 ? Fmt.Money(Row.Total) : "—";

    public string LastText => Row.LastPurchase is { } d ? Fmt.Date(d) : "Nunca";

    public bool IsActive => Row.IsActive;
}

/// <summary>Clientes: búsqueda, categoría y estado en combos, historial de compras y editor lateral.</summary>
public sealed class CustomersViewModel : PageViewModel
{
    private static readonly Choice<string?> AllCategories = new("Todas las categorías", null);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private List<CustomerItem> _items = [];
    private IReadOnlyList<OptionItem> _categoryOptions = [];
    private string _search = string.Empty;
    private Choice<string?> _category = AllCategories;
    private Choice<string> _sort;
    private CustomerEditor? _editor;
    private string _summary = string.Empty;

    public CustomersViewModel(AppServices app) : base(app, "clientes", "Clientes", "Cartera, categorías y compras", Glyphs.People)
    {
        Sorts = [new("Nombre (A → Z)", "name"), new("Mayor compra", "total"), new("Más compras", "count"), new("Compra más reciente", "last")];
        _sort = Sorts[0];
        Rows = CollectionViewSource.GetDefaultView(_items);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ApplyFilter();
        };
        New = new RelayCommand(() => Editor = new CustomerEditor(this, App, _categoryOptions, null), () => CanEdit);
        Edit = new RelayCommand<CustomerItem>(c => Editor = new CustomerEditor(this, App, _categoryOptions, c.Row), _ => CanEdit);
        Export = new RelayCommand(ExportCsv, () => _items.Count > 0);
    }

    public ICollectionView Rows { get; private set; }

    public BulkObservableCollection<Choice<string?>> Categories { get; } = [];

    public IReadOnlyList<Choice<string>> Sorts { get; }

    public bool CanEdit => App.Session.Can(PermissionCodes.CustomersManage);

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

    public Choice<string?> Category { get => _category; set { if (Set(ref _category, value ?? AllCategories)) { ApplyFilter(); } } }

    public Choice<string> Sort { get => _sort; set { if (Set(ref _sort, value ?? Sorts[0])) { ApplySort(); } } }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public KpiCard CustomersKpi { get; } = new("Clientes", Glyphs.People);

    public KpiCard BuyersKpi { get; } = new("Con compras", Glyphs.Cart, "Info", "InfoSoft");

    public KpiCard TotalKpi { get; } = new("Vendido a clientes", Glyphs.Money, "Success", "SuccessSoft");

    public KpiCard TopKpi { get; } = new("Mejor cliente", Glyphs.Flag, "Warning", "WarningSoft");

    public CustomerEditor? Editor
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

    public bool IsEmpty => HasLoaded && Rows.IsEmpty;

    public RelayCommand New { get; }

    public RelayCommand<CustomerItem> Edit { get; }

    public RelayCommand Export { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var view = await App.SendAsync(new GetCustomersQuery());
        _categoryOptions = view.Categories;
        _items = view.Customers.Select(c => new CustomerItem(c)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = Matches;
        OnPropertyChanged(nameof(Rows));
        var previous = _category.Value;
        Categories.ReplaceAll(new[] { AllCategories }.Concat(view.Categories.Select(c =>
            new Choice<string?>($"{c.Name} ({_items.Count(i => i.Row.CategoryCode == c.Code)})", c.Code))));
        _category = Categories.FirstOrDefault(c => c.Value == previous) ?? AllCategories;
        OnPropertyChanged(nameof(Category));
        CustomersKpi.Value = $"{_items.Count(i => i.IsActive)} activos";
        CustomersKpi.Detail = $"{_items.Count} registrados";
        BuyersKpi.Value = _items.Count(i => i.Purchases > 0).ToString("N0", Fmt.Culture);
        BuyersKpi.Detail = $"{_items.Sum(i => i.Purchases):N0} compras en total";
        TotalKpi.Value = Fmt.Money(_items.Where(i => i.Code != "CF").Sum(i => i.Total));
        TotalKpi.Detail = $"Consumidor final: {Fmt.Money(_items.Where(i => i.Code == "CF").Sum(i => i.Total))}";
        var top = _items.Where(i => i.Code != "CF").MaxBy(i => i.Total);
        TopKpi.Value = top?.Name ?? "—";
        TopKpi.Detail = top is null ? null : $"{top.TotalText} en {top.Purchases} compras";
        ApplySort();
    }

    internal async Task AfterSaveAsync()
    {
        Editor = null;
        await LoadAsync(force: true);
    }

    internal void CloseEditor() => Editor = null;

    private bool Matches(object o)
    {
        if (o is not CustomerItem c || (_category.Value is { } code && c.Row.CategoryCode != code))
        {
            return false;
        }
        var q = _search.Trim();
        return q.Length == 0 || c.Code.Contains(q, StringComparison.OrdinalIgnoreCase) || (c.Row.TaxId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
               || Fmt.Culture.CompareInfo.IndexOf(c.Name, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
               || Fmt.Culture.CompareInfo.IndexOf(c.Contact, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    private void ApplySort()
    {
        using (Rows.DeferRefresh())
        {
            Rows.SortDescriptions.Clear();
            Rows.SortDescriptions.Add(_sort.Value switch
            {
                "total" => new SortDescription(nameof(CustomerItem.Total), ListSortDirection.Descending),
                "count" => new SortDescription(nameof(CustomerItem.Purchases), ListSortDirection.Descending),
                "last" => new SortDescription("Row.LastPurchase", ListSortDirection.Descending),
                _ => new SortDescription(nameof(CustomerItem.Name), ListSortDirection.Ascending),
            });
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        var visible = Rows.Cast<object>().Count();
        Summary = visible == _items.Count ? $"{_items.Count} clientes" : $"{visible} de {_items.Count} clientes";
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void ExportCsv()
    {
        var rows = Rows.Cast<CustomerItem>().ToList();
        var path = FileDialogs.SaveCsv($"clientes-{DateTime.Now:yyyyMMdd}.csv");
        if (path is null)
        {
            return;
        }
        Csv.Write(path, ["Código", "Nombre", "NIT / CI", "Correo", "Teléfono", "Categoría", "Compras", "Total", "Última compra", "Activo"],
            rows.Select(r => new object?[] { r.Code, r.Name, r.Row.TaxId, r.Row.Email, r.Row.Phone, r.Category, r.Purchases, r.Total, r.Row.LastPurchase, r.IsActive ? "SÍ" : "NO" }));
        App.Notify.Success("Clientes exportados", $"{rows.Count} filas en {Path.GetFileName(path)}");
    }
}

public sealed class CustomerEditor : ObservableObject
{
    private readonly CustomersViewModel _owner;
    private readonly AppServices _app;
    private readonly string? _code;
    private string _name;
    private string _taxId;
    private string _email;
    private string _phone;
    private OptionItem? _category;
    private bool _isActive;
    private string? _error;

    public CustomerEditor(CustomersViewModel owner, AppServices app, IReadOnlyList<OptionItem> categories, CustomerRow? row)
    {
        _owner = owner;
        _app = app;
        _code = row?.Code;
        Categories = categories;
        _name = row?.Name ?? string.Empty;
        _taxId = row?.TaxId ?? string.Empty;
        _email = row?.Email ?? string.Empty;
        _phone = row?.Phone ?? string.Empty;
        _category = categories.FirstOrDefault(c => c.Code == row?.CategoryCode) ?? categories.FirstOrDefault(c => c.Code == "GENERAL") ?? categories.FirstOrDefault();
        _isActive = row?.IsActive ?? true;
        Save = new AsyncRelayCommand(SaveAsync);
        Cancel = new RelayCommand(owner.CloseEditor);
    }

    public bool IsNew => _code is null;

    public bool IsFinalConsumer => _code == "CF";

    public string Heading => IsNew ? "Nuevo cliente" : $"Cliente {_code}";

    public IReadOnlyList<OptionItem> Categories { get; }

    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }

    public string TaxId { get => _taxId; set => Set(ref _taxId, value ?? string.Empty); }

    public string Email { get => _email; set => Set(ref _email, value ?? string.Empty); }

    public string Phone { get => _phone; set => Set(ref _phone, value ?? string.Empty); }

    public OptionItem? Category { get => _category; set => Set(ref _category, value); }

    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    public string? Error { get => _error; private set => Set(ref _error, value); }

    public AsyncRelayCommand Save { get; }

    public RelayCommand Cancel { get; }

    private async Task SaveAsync()
    {
        Error = null;
        if (_category is null)
        {
            Error = "Elija la categoría del cliente.";
            return;
        }
        try
        {
            var code = await _app.SendAsync(new SaveCustomerCommand(_code, _name.Trim(), Blank(_taxId), Blank(_email), Blank(_phone), _category.Code, _isActive));
            _app.Notify.Success(IsNew ? "Cliente creado" : "Cliente actualizado", $"{code} · {_name.Trim()}");
            await _owner.AfterSaveAsync();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Error = AppServices.Describe(ex);
        }
    }

    internal static string? Blank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
