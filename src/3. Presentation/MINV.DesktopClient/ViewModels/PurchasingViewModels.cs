using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MINV.Application.Catalog;
using MINV.Application.Partners;
using MINV.Application.Purchasing;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Purchasing;

namespace MINV.DesktopClient.ViewModels;

public sealed class PurchaseOrderItem(PurchaseOrderRow r)
{
    public PurchaseOrderRow Row { get; } = r;

    public string Number => Row.Number;

    public string Supplier => Row.Supplier;

    public string DateText => Fmt.Date(Row.OrderDate);

    public DateOnly OrderDate => Row.OrderDate;

    public string ExpectedText => Row.ExpectedDate is { } d ? Fmt.Date(d) : "—";

    public decimal Total => Row.Total;

    public string TotalText => Fmt.Money(Row.Total);

    public int Lines => Row.Lines;

    public string LinesText => Row.Lines == 1 ? "1 línea" : $"{Row.Lines} líneas";

    public PurchaseOrderStatus Status => Row.Status;

    public string StatusText => PurchasingText.Status(Row.Status);

    public string StatusBrush => PurchasingText.Brush(Row.Status);

    public string StatusSoftBrush => PurchasingText.Brush(Row.Status) + "Soft";

    /// <summary>Avance de la recepción (el caso de uso lo entrega de 0 a 100).</summary>
    public double Received => (double)Math.Clamp(Row.ReceivedPercent / 100m, 0, 1);

    public string ReceivedText => $"{Row.ReceivedPercent:0} %";
}

public static class PurchasingText
{
    public static string Status(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => "Borrador",
        PurchaseOrderStatus.Approved => "Aprobada",
        PurchaseOrderStatus.PartiallyReceived => "Recibida en parte",
        PurchaseOrderStatus.Received => "Recibida",
        PurchaseOrderStatus.Cancelled => "Anulada",
        _ => status.ToString(),
    };

    public static string Brush(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => "Warning",
        PurchaseOrderStatus.Approved => "Info",
        PurchaseOrderStatus.PartiallyReceived => "Info",
        PurchaseOrderStatus.Received => "Success",
        _ => "Danger",
    };
}

/// <summary>
/// Órdenes de compra: estado y proveedor en combos, detalle con líneas y recepciones, y el ciclo completo — crear (a mano o
/// desde el pedido sugerido), aprobar, recibir (entra el stock, se actualiza el costo promedio y se contabiliza) y anular.
/// </summary>
public sealed class PurchaseOrdersViewModel : PageViewModel
{
    private static readonly Choice<PurchaseOrderStatus?> AllStatuses = new("Todos los estados", null);
    private static readonly Choice<string?> AllSuppliers = new("Todos los proveedores", null);
    private List<PurchaseOrderItem> _items = [];
    private Choice<PurchaseOrderStatus?> _status = AllStatuses;
    private Choice<string?> _supplier = AllSuppliers;
    private PurchaseOrderItem? _selected;
    private PurchaseOrderDetail? _detail;
    private PurchaseOrderEditor? _editor;
    private string _summary = string.Empty;

    public PurchaseOrdersViewModel(AppServices app) : base(app, "compras", "Órdenes de compra", "Pedir, aprobar y recibir mercadería", Glyphs.Clipboard)
    {
        Statuses =
        [
            AllStatuses, new("Borradores", PurchaseOrderStatus.Draft), new("Aprobadas (por recibir)", PurchaseOrderStatus.Approved),
            new("Recibidas en parte", PurchaseOrderStatus.PartiallyReceived), new("Recibidas", PurchaseOrderStatus.Received),
            new("Anuladas", PurchaseOrderStatus.Cancelled),
        ];
        Rows = CollectionViewSource.GetDefaultView(_items);
        New = new AsyncRelayCommand(OpenEditorAsync, () => CanManage);
        FromSuggestion = new AsyncRelayCommand(FromSuggestionAsync, () => CanManage);
        Approve = new AsyncRelayCommand(ApproveAsync, () => CanManage && _selected?.Status == PurchaseOrderStatus.Draft);
        Receive = new AsyncRelayCommand(ReceiveAsync, () => CanReceive && _selected?.Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived);
        Cancel = new AsyncRelayCommand(CancelAsync, () => CanManage && _selected?.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Approved);
        Export = new RelayCommand(ExportCsv, () => _items.Count > 0);
    }

    public ICollectionView Rows { get; private set; }

    public IReadOnlyList<Choice<PurchaseOrderStatus?>> Statuses { get; }

    public BulkObservableCollection<Choice<string?>> Suppliers { get; } = [];

    public bool CanManage => App.Session.Can(PermissionCodes.PurchasingManage);

    public bool CanReceive => CanManage && App.Session.Can(PermissionCodes.MovementsRegisterWarehouse);

    public Choice<PurchaseOrderStatus?> Status { get => _status; set { if (Set(ref _status, value ?? AllStatuses)) { ApplyFilter(); } } }

    public Choice<string?> Supplier { get => _supplier; set { if (Set(ref _supplier, value ?? AllSuppliers)) { ApplyFilter(); } } }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public KpiCard DraftKpi { get; } = new("Borradores", Glyphs.Clipboard, "Warning", "WarningSoft");

    public KpiCard PendingKpi { get; } = new("Por recibir", Glyphs.Clock, "Info", "InfoSoft");

    public KpiCard ReceivedKpi { get; } = new("Recibido este mes", Glyphs.CheckCircle, "Success", "SuccessSoft");

    public KpiCard SuppliersKpi { get; } = new("Proveedores con órdenes", Glyphs.Briefcase);

    public PurchaseOrderItem? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                _ = LoadDetailAsync();
            }
        }
    }

    public bool HasSelection => _selected is not null;

    public PurchaseOrderDetail? Detail
    {
        get => _detail;
        private set
        {
            if (Set(ref _detail, value))
            {
                OnPropertyChanged(nameof(ReceiptsText));
            }
        }
    }

    public string? ReceiptsText => _detail is { Receipts.Count: > 0 } d ? "Recepciones: " + string.Join(", ", d.Receipts) : null;

    public PurchaseOrderEditor? Editor
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

    public AsyncRelayCommand New { get; }

    public AsyncRelayCommand FromSuggestion { get; }

    public AsyncRelayCommand Approve { get; }

    public AsyncRelayCommand Receive { get; }

    public AsyncRelayCommand Cancel { get; }

    public RelayCommand Export { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var rows = await App.SendAsync(new GetPurchaseOrdersQuery());
        var selected = _selected?.Row.Id;
        _items = rows.Select(r => new PurchaseOrderItem(r)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = o => o is PurchaseOrderItem p && (_status.Value is not { } s || p.Status == s) && (_supplier.Value is not { } c || p.Row.SupplierCode == c);
        Rows.SortDescriptions.Add(new SortDescription(nameof(PurchaseOrderItem.Number), ListSortDirection.Descending));
        OnPropertyChanged(nameof(Rows));
        var supplier = _supplier.Value;
        Suppliers.ReplaceAll(new[] { AllSuppliers }.Concat(rows.GroupBy(r => (r.SupplierCode, r.Supplier)).OrderBy(g => g.Key.Supplier)
            .Select(g => new Choice<string?>($"{g.Key.Supplier} ({g.Count()})", g.Key.SupplierCode))));
        _supplier = Suppliers.FirstOrDefault(s => s.Value == supplier) ?? AllSuppliers;
        OnPropertyChanged(nameof(Supplier));
        var today = DateOnly.FromDateTime(App.Now.ToLocalTime().DateTime);
        var drafts = rows.Where(r => r.Status == PurchaseOrderStatus.Draft).ToList();
        var pending = rows.Where(r => r.Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived).ToList();
        var month = rows.Where(r => r.Status is PurchaseOrderStatus.Received && r.OrderDate >= new DateOnly(today.Year, today.Month, 1)).ToList();
        DraftKpi.Value = drafts.Count.ToString("N0", Fmt.Culture);
        DraftKpi.Detail = drafts.Count > 0 ? $"{Fmt.Money(drafts.Sum(d => d.Total))} por aprobar" : "Nada por aprobar";
        PendingKpi.Value = pending.Count.ToString("N0", Fmt.Culture);
        PendingKpi.Detail = pending.Count > 0 ? $"{Fmt.Money(pending.Sum(d => d.Total))} · {pending.Count(p => p.ExpectedDate <= today)} vencen hoy o antes" : "Sin pendientes";
        ReceivedKpi.Value = Fmt.Money(month.Sum(m => m.Total));
        ReceivedKpi.Detail = $"{month.Count} órdenes recibidas";
        SuppliersKpi.Value = rows.Select(r => r.SupplierCode).Distinct().Count().ToString("N0", Fmt.Culture);
        SuppliersKpi.Detail = $"{rows.Count} órdenes en total";
        ApplyFilter();
        if (selected is { } id)
        {
            Selected = _items.FirstOrDefault(i => i.Row.Id == id);
        }
    }

    internal void CloseEditor() => Editor = null;

    internal async Task AfterSaveAsync(PurchaseOrderRow created)
    {
        Editor = null;
        await LoadAsync(force: true);
        Selected = _items.FirstOrDefault(i => i.Row.Id == created.Id);
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        var visible = Rows.Cast<object>().Count();
        Summary = visible == _items.Count ? $"{_items.Count} órdenes" : $"{visible} de {_items.Count} órdenes";
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task LoadDetailAsync()
    {
        if (_selected is not { } order)
        {
            Detail = null;
            return;
        }
        try
        {
            Detail = await App.SendAsync(new GetPurchaseOrderQuery(order.Row.Id));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Detail = null;
            App.Notify.Error("No se pudo leer la orden", AppServices.Describe(ex));
        }
    }

    private async Task OpenEditorAsync()
    {
        try
        {
            var options = await App.SendAsync(new GetCatalogOptionsQuery());
            var catalog = await App.SendAsync(new GetCatalogQuery());
            var images = await App.Images.AllAsync();
            var today = DateOnly.FromDateTime(App.Now.ToLocalTime().DateTime);
            Editor = new PurchaseOrderEditor(this, App, options.Suppliers, catalog.Where(c => c.IsActive).ToList(), images, today);
        }
        catch (Exception ex)
        {
            App.Notify.Error("No se pudo abrir la orden nueva", AppServices.Describe(ex));
        }
    }

    private async Task FromSuggestionAsync()
    {
        if (!await App.Dialogs.ConfirmAsync("Órdenes desde el pedido sugerido",
                "Se crea una orden en borrador por cada proveedor con productos bajo el mínimo (hasta el máximo). Los proveedores que ya " +
                "tienen una orden abierta se omiten.", "Crear órdenes", glyph: Glyphs.Sparkle))
        {
            return;
        }
        try
        {
            var numbers = await App.SendAsync(new CreateSuggestedPurchaseOrdersCommand());
            App.Notify.Success($"{numbers.Count} órdenes creadas en borrador", string.Join(", ", numbers));
            await LoadAsync(force: true);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Warning("Sin órdenes nuevas", AppServices.Describe(ex));
        }
    }

    private async Task ApproveAsync()
    {
        var order = _selected!;
        if (!await App.Dialogs.ConfirmAsync($"Aprobar {order.Number}", $"{order.Supplier} · {order.Lines} líneas · {order.TotalText}. " +
                "Una orden aprobada ya no se edita: queda lista para recibir.", "Aprobar", glyph: Glyphs.CheckCircle))
        {
            return;
        }
        if (await RunAsync(() => App.SendAsync(new ApprovePurchaseOrderCommand(order.Row.Id)), "No se pudo aprobar"))
        {
            App.Notify.Success("Orden aprobada", order.Number);
            await LoadAsync(force: true);
        }
    }

    private async Task ReceiveAsync()
    {
        var order = _selected!;
        var document = await App.Dialogs.PromptAsync($"Recibir {order.Number}",
            $"Entra al stock todo lo pendiente de {order.Supplier} ({order.TotalText}), se actualiza el costo promedio y se registra el asiento " +
            "(inventario contra proveedores).", "Factura o remisión del proveedor (opcional)", [], "Recibir mercadería",
            placeholder: "Ej.: FAC-12345", glyph: Glyphs.Box);
        if (document is null)
        {
            return;
        }
        try
        {
            var result = await App.SendAsync(new ReceivePurchaseOrderCommand(order.Row.Id, string.IsNullOrWhiteSpace(document) ? null : document));
            App.Notify.Success($"Recepción {result.ReceiptNumber} registrada", $"{result.Lines} líneas · {Fmt.Money(result.Total)} · asiento {result.JournalNumber}");
            App.Data.Invalidate();
            await LoadAsync(force: true);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo recibir", AppServices.Describe(ex));
        }
    }

    private async Task CancelAsync()
    {
        var order = _selected!;
        if (!await App.Dialogs.ConfirmAsync($"Anular {order.Number}", $"La orden a {order.Supplier} por {order.TotalText} queda anulada (no se borra).",
                "Anular orden", "Volver", isDanger: true))
        {
            return;
        }
        if (await RunAsync(() => App.SendAsync(new CancelPurchaseOrderCommand(order.Row.Id)), "No se pudo anular"))
        {
            App.Notify.Success("Orden anulada", order.Number);
            await LoadAsync(force: true);
        }
    }

    private void ExportCsv()
    {
        var rows = Rows.Cast<PurchaseOrderItem>().ToList();
        var path = FileDialogs.SaveCsv($"ordenes-compra-{DateTime.Now:yyyyMMdd}.csv");
        if (path is null)
        {
            return;
        }
        Csv.Write(path, ["Número", "Proveedor", "Fecha", "Entrega esperada", "Estado", "Líneas", "Total", "Recibido", "Notas"],
            rows.Select(r => new object?[] { r.Number, r.Supplier, r.Row.OrderDate, r.Row.ExpectedDate, r.StatusText, r.Lines, r.Total, r.Row.ReceivedPercent, r.Row.Notes }));
        App.Notify.Success("Órdenes exportadas", $"{rows.Count} filas en {Path.GetFileName(path)}");
    }
}

/// <summary>Línea de una orden nueva (cantidad y costo editables).</summary>
public sealed class DraftLine(CatalogItem product, ImageSource? image, decimal quantity, Action changed) : ObservableObject
{
    private string _quantity = Numbers.Plain(quantity);
    private string _cost = Numbers.Plain(product.UnitCost);

    public CatalogItem Product { get; } = product;

    public ImageSource? Image { get; } = image;

    public string Quantity
    {
        get => _quantity;
        set
        {
            if (Set(ref _quantity, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(SubtotalText));
                changed();
            }
        }
    }

    public string Cost
    {
        get => _cost;
        set
        {
            if (Set(ref _cost, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(SubtotalText));
                changed();
            }
        }
    }

    public decimal Subtotal => Numbers.TryParse(_quantity, out var q) && Numbers.TryParse(_cost, out var c) ? q * c : 0;

    public string SubtotalText => Fmt.Money(Subtotal);
}

public sealed class PurchaseOrderEditor : ObservableObject
{
    private readonly PurchaseOrdersViewModel _owner;
    private readonly AppServices _app;
    private readonly IReadOnlyList<CatalogItem> _catalog;
    private readonly IReadOnlyDictionary<string, ImageSource> _images;
    private OptionItem? _supplier;
    private DateOption? _expected;
    private string _notes = string.Empty;
    private CatalogItem? _product;
    private bool _onlySupplier = true;
    private string? _error;

    public PurchaseOrderEditor(PurchaseOrdersViewModel owner, AppServices app, IReadOnlyList<OptionItem> suppliers, IReadOnlyList<CatalogItem> catalog,
        IReadOnlyDictionary<string, ImageSource> images, DateOnly today)
    {
        _owner = owner;
        _app = app;
        _catalog = catalog;
        _images = images;
        Suppliers = suppliers;
        Dates = DateOption.Future(today);
        _expected = Dates[4];
        _supplier = suppliers.FirstOrDefault();
        Lines.CollectionChanged += (_, _) => Changed();
        AddLine = new RelayCommand(OnAddLine, () => _product is not null);
        RemoveLine = new RelayCommand<DraftLine>(l => Lines.Remove(l));
        Save = new AsyncRelayCommand(SaveAsync, () => Lines.Count > 0 && _supplier is not null);
        Cancel = new RelayCommand(owner.CloseEditor);
        RefreshProducts();
    }

    public IReadOnlyList<OptionItem> Suppliers { get; }

    public IReadOnlyList<DateOption> Dates { get; }

    public BulkObservableCollection<CatalogItem> Products { get; } = [];

    public ObservableCollection<DraftLine> Lines { get; } = [];

    public OptionItem? Supplier
    {
        get => _supplier;
        set
        {
            if (Set(ref _supplier, value))
            {
                RefreshProducts();
            }
        }
    }

    /// <summary>Mostrar solo los productos de este proveedor (o todo el catálogo).</summary>
    public bool OnlySupplier
    {
        get => _onlySupplier;
        set
        {
            if (Set(ref _onlySupplier, value))
            {
                RefreshProducts();
            }
        }
    }

    public DateOption? Expected { get => _expected; set => Set(ref _expected, value); }

    public string Notes { get => _notes; set => Set(ref _notes, value ?? string.Empty); }

    public CatalogItem? Product { get => _product; set => Set(ref _product, value); }

    public string TotalText => Fmt.Money(Lines.Sum(l => l.Subtotal));

    public string LinesText => Lines.Count == 0 ? "Agregue productos con el combo" : $"{Lines.Count} líneas";

    public string? Error { get => _error; private set => Set(ref _error, value); }

    public RelayCommand AddLine { get; }

    public RelayCommand<DraftLine> RemoveLine { get; }

    public AsyncRelayCommand Save { get; }

    public RelayCommand Cancel { get; }

    private void RefreshProducts()
    {
        var list = _onlySupplier && _supplier is not null ? _catalog.Where(c => c.SupplierCode == _supplier.Code).ToList() : _catalog.ToList();
        Products.ReplaceAll(list.OrderBy(c => c.Name, StringComparer.Create(Fmt.Culture, true)));
        Product = Products.FirstOrDefault(p => Lines.All(l => l.Product.Sku != p.Sku));
    }

    private void OnAddLine()
    {
        var product = _product!;
        if (Lines.FirstOrDefault(l => l.Product.Sku == product.Sku) is { } existing)
        {
            existing.Quantity = Numbers.Plain((Numbers.TryParse(existing.Quantity, out var q) ? q : 0) + 1);
            return;
        }
        var suggested = Math.Max(1, product.Maximum > 0 ? product.Maximum - product.Minimum : 1);
        Lines.Add(new DraftLine(product, _images.GetValueOrDefault(product.Sku), suggested, Changed));
        Product = Products.FirstOrDefault(p => Lines.All(l => l.Product.Sku != p.Sku));
    }

    private void Changed() => OnPropertiesChanged(nameof(TotalText), nameof(LinesText));

    private async Task SaveAsync()
    {
        Error = null;
        var inputs = new List<PurchaseLineInput>();
        foreach (var line in Lines)
        {
            if (!Numbers.TryParse(line.Quantity, out var q) || q <= 0 || !Numbers.TryParse(line.Cost, out var c) || c < 0)
            {
                Error = $"Revise la cantidad y el costo de {line.Product.Name}.";
                return;
            }
            inputs.Add(new PurchaseLineInput(line.Product.Sku, q, c));
        }
        try
        {
            var order = await _app.SendAsync(new CreatePurchaseOrderCommand(_supplier!.Code, _expected?.Date, CustomerEditor.Blank(_notes), inputs));
            _app.Notify.Success($"Orden {order.Number} creada en borrador", $"{order.Supplier} · {Fmt.Money(order.Total)}. Apruébela para poder recibirla.");
            await _owner.AfterSaveAsync(order);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Error = AppServices.Describe(ex);
        }
    }
}

public sealed class SupplierItem(SupplierRow r)
{
    public SupplierRow Row { get; } = r;

    public string Code => Row.Code;

    public string Name => Row.Name;

    public string Initials => Fmt.Initials(Row.Name);

    public string Contact => string.Join(" · ", new[] { Row.Contact, Row.Phone, Row.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public string LeadText => Row.LeadTimeDays == 1 ? "1 día" : $"{Row.LeadTimeDays} días";

    public int Products => Row.Products;

    public int OpenOrders => Row.OpenOrders;

    public decimal Purchased => Row.Purchased;

    public string PurchasedText => Row.Purchased > 0 ? Fmt.Money(Row.Purchased) : "—";

    public bool IsActive => Row.IsActive;
}

/// <summary>Proveedores: datos de contacto, días de entrega (combo), productos, órdenes abiertas y lo comprado.</summary>
public sealed class SuppliersViewModel : PageViewModel
{
    private List<SupplierItem> _items = [];
    private string _search = string.Empty;
    private SupplierEditor? _editor;

    public SuppliersViewModel(AppServices app) : base(app, "proveedores", "Proveedores", "Contactos, plazos de entrega y compras", Glyphs.Briefcase)
    {
        Rows = CollectionViewSource.GetDefaultView(_items);
        New = new RelayCommand(() => Editor = new SupplierEditor(this, App, null), () => CanEdit);
        Edit = new RelayCommand<SupplierItem>(s => Editor = new SupplierEditor(this, App, s.Row), _ => CanEdit);
        NewOrder = new RelayCommand(() => app.Navigator.Navigate("compras"));
    }

    public ICollectionView Rows { get; private set; }

    public bool CanEdit => App.Session.Can(PermissionCodes.PurchasingManage);

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? string.Empty))
            {
                Rows.Refresh();
            }
        }
    }

    public KpiCard SuppliersKpi { get; } = new("Proveedores activos", Glyphs.Briefcase);

    public KpiCard OpenKpi { get; } = new("Órdenes abiertas", Glyphs.Clipboard, "Warning", "WarningSoft");

    public KpiCard PurchasedKpi { get; } = new("Comprado (recibido)", Glyphs.Money, "Success", "SuccessSoft");

    public KpiCard LeadKpi { get; } = new("Entrega promedio", Glyphs.Clock, "Info", "InfoSoft");

    public SupplierEditor? Editor
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

    public RelayCommand New { get; }

    public RelayCommand<SupplierItem> Edit { get; }

    public RelayCommand NewOrder { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var rows = await App.SendAsync(new GetSuppliersQuery());
        _items = rows.Select(r => new SupplierItem(r)).OrderBy(r => r.Name, StringComparer.Create(Fmt.Culture, true)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = o => o is SupplierItem s && (_search.Trim().Length == 0
                                                   || Fmt.Culture.CompareInfo.IndexOf(s.Name, _search.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
                                                   || Fmt.Culture.CompareInfo.IndexOf(s.Contact, _search.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
        OnPropertyChanged(nameof(Rows));
        var active = _items.Where(i => i.IsActive).ToList();
        SuppliersKpi.Value = active.Count.ToString("N0", Fmt.Culture);
        SuppliersKpi.Detail = $"{_items.Sum(i => i.Products)} productos asignados";
        OpenKpi.Value = _items.Sum(i => i.OpenOrders).ToString("N0", Fmt.Culture);
        OpenKpi.Detail = $"{_items.Count(i => i.OpenOrders > 0)} proveedores con pedidos en curso";
        PurchasedKpi.Value = Fmt.Money(_items.Sum(i => i.Purchased));
        PurchasedKpi.Detail = _items.MaxBy(i => i.Purchased) is { Purchased: > 0 } top ? $"Principal: {top.Name}" : null;
        LeadKpi.Value = active.Count > 0 ? $"{active.Average(i => i.Row.LeadTimeDays):0.#} días" : "—";
        LeadKpi.Detail = "Plazo que usa el pedido sugerido";
    }

    internal void CloseEditor() => Editor = null;

    internal async Task AfterSaveAsync()
    {
        Editor = null;
        await LoadAsync(force: true);
    }
}

public sealed class SupplierEditor : ObservableObject
{
    private readonly SuppliersViewModel _owner;
    private readonly AppServices _app;
    private readonly string? _code;
    private string _name;
    private string _taxId;
    private string _lead;
    private string _contact;
    private string _phone;
    private string _email;
    private bool _isActive;
    private string? _error;

    public SupplierEditor(SuppliersViewModel owner, AppServices app, SupplierRow? row)
    {
        _owner = owner;
        _app = app;
        _code = row?.Code;
        _name = row?.Name ?? string.Empty;
        _taxId = row?.TaxId ?? string.Empty;
        _lead = (row?.LeadTimeDays ?? 3).ToString(CultureInfo.InvariantCulture);
        _contact = row?.Contact ?? string.Empty;
        _phone = row?.Phone ?? string.Empty;
        _email = row?.Email ?? string.Empty;
        _isActive = row?.IsActive ?? true;
        Save = new AsyncRelayCommand(SaveAsync);
        Cancel = new RelayCommand(owner.CloseEditor);
    }

    public string Heading => _code is null ? "Nuevo proveedor" : $"Proveedor {_code}";

    public IReadOnlyList<string> LeadOptions { get; } = ["1", "2", "3", "5", "7", "10", "15", "21", "30", "45"];

    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }

    public string TaxId { get => _taxId; set => Set(ref _taxId, value ?? string.Empty); }

    public string LeadTime { get => _lead; set => Set(ref _lead, value ?? string.Empty); }

    public string Contact { get => _contact; set => Set(ref _contact, value ?? string.Empty); }

    public string Phone { get => _phone; set => Set(ref _phone, value ?? string.Empty); }

    public string Email { get => _email; set => Set(ref _email, value ?? string.Empty); }

    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    public string? Error { get => _error; private set => Set(ref _error, value); }

    public AsyncRelayCommand Save { get; }

    public RelayCommand Cancel { get; }

    private async Task SaveAsync()
    {
        Error = null;
        if (!int.TryParse(_lead.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lead) || lead < 0)
        {
            Error = "Los días de entrega deben ser un número entero (0 o más).";
            return;
        }
        try
        {
            var code = await _app.SendAsync(new SaveSupplierCommand(_code, _name.Trim(), CustomerEditor.Blank(_taxId), lead, CustomerEditor.Blank(_contact),
                CustomerEditor.Blank(_phone), CustomerEditor.Blank(_email), _isActive));
            _app.Notify.Success(_code is null ? "Proveedor creado" : "Proveedor actualizado", $"{code} · {_name.Trim()}");
            await _owner.AfterSaveAsync();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Error = AppServices.Describe(ex);
        }
    }
}
