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
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private List<PurchaseOrderItem> _items = [];
    private Choice<PurchaseOrderStatus?> _status = AllStatuses;
    private Choice<string?> _supplier = AllSuppliers;
    private Choice<PeriodOption?> _period;
    private string _search = string.Empty;
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
        Periods.ReplaceAll(FilterChoices.Periods(DateOnly.FromDateTime(app.Now.ToLocalTime().DateTime)));
        _period = Periods[0];
        Rows = CollectionViewSource.GetDefaultView(_items);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ApplyFilter();
        };
        New = new AsyncRelayCommand(OpenEditorAsync, () => CanManage);
        FromSuggestion = new AsyncRelayCommand(FromSuggestionAsync, () => CanManage);
        Approve = new AsyncRelayCommand(ApproveAsync, () => CanManage && _selected?.Status == PurchaseOrderStatus.Draft);
        Receive = new AsyncRelayCommand(ReceiveAsync, () => CanReceive && _selected?.Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived);
        Cancel = new AsyncRelayCommand(CancelAsync, () => CanManage && _selected?.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Approved);
        Export = new RelayCommand(ExportCsv, () => _items.Count > 0);
        ClearFilters = new RelayCommand(() =>
        {
            _search = string.Empty;
            _status = AllStatuses;
            _supplier = AllSuppliers;
            _period = Periods[0];
            OnPropertiesChanged(nameof(Search), nameof(Status), nameof(Supplier), nameof(Period));
            ApplyFilter();
        }, () => HasFilters);
    }

    public ICollectionView Rows { get; private set; }

    public IReadOnlyList<Choice<PurchaseOrderStatus?>> Statuses { get; }

    public BulkObservableCollection<Choice<string?>> Suppliers { get; } = [];

    /// <summary>V7 · Período por la fecha del pedido.</summary>
    public BulkObservableCollection<Choice<PeriodOption?>> Periods { get; } = [];

    public bool CanManage => App.Session.Can(PermissionCodes.PurchasingManage);

    public bool CanReceive => CanManage && App.Session.Can(PermissionCodes.MovementsRegisterWarehouse);

    public Choice<PurchaseOrderStatus?> Status { get => _status; set { if (Set(ref _status, value ?? AllStatuses)) { ApplyFilter(); } } }

    public Choice<string?> Supplier { get => _supplier; set { if (Set(ref _supplier, value ?? AllSuppliers)) { ApplyFilter(); } } }

    public Choice<PeriodOption?> Period { get => _period; set { if (Set(ref _period, value ?? Periods[0])) { ApplyFilter(); } } }

    /// <summary>V7 · Búsqueda por número, proveedor o notas.</summary>
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

    public bool HasFilters => _search.Trim().Length > 0 || _status.Value is not null || _supplier.Value is not null || _period.Value is not null;

    public RelayCommand ClearFilters { get; }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public KpiCard DraftKpi { get; } = new("Borradores", Glyphs.Clipboard, "Warning", "WarningSoft");

    public KpiCard PendingKpi { get; } = new("Por recibir", Glyphs.Clock, "Info", "InfoSoft");

    // V7 · La fila no trae la fecha de recepción: el indicador cuenta las órdenes PEDIDAS este mes que ya se recibieron (antes decía
    // «Recibido este mes», que no era lo que calculaba)
    public KpiCard ReceivedKpi { get; } = new("Pedidas este mes y recibidas", Glyphs.CheckCircle, "Success", "SuccessSoft");

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
        Rows.Filter = Matches;
        // V7 · Lo más reciente arriba en todas las sucursales (el número lleva la sucursal: ordenar por él agrupaba por sucursal)
        Rows.SortDescriptions.Add(new SortDescription(nameof(PurchaseOrderItem.OrderDate), ListSortDirection.Descending));
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
        App.Data.Invalidate();   // V7 · Proveedores y el pedido sugerido cuentan las órdenes abiertas
        await LoadAsync(force: true);
        Selected = _items.FirstOrDefault(i => i.Row.Id == created.Id);
    }

    private bool Matches(object o)
    {
        if (o is not PurchaseOrderItem p)
        {
            return false;
        }
        if (_status.Value is { } s && p.Status != s)
        {
            return false;
        }
        if (_supplier.Value is { } c && p.Row.SupplierCode != c)
        {
            return false;
        }
        if (_period.Value is { } period && (p.OrderDate < period.From || p.OrderDate > period.To))
        {
            return false;
        }
        var q = _search.Trim();
        return q.Length == 0 || p.Number.Contains(q, StringComparison.OrdinalIgnoreCase) || FilterChoices.Contains(p.Supplier, q)
               || FilterChoices.Contains(p.Row.Notes, q);
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        var visible = Rows.Cast<object>().Count();
        Summary = visible == _items.Count ? $"{_items.Count} órdenes" : $"{visible} de {_items.Count} órdenes";
        OnPropertiesChanged(nameof(IsEmpty), nameof(HasFilters));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async Task LoadDetailAsync()
    {
        // V7 · Sin mostrar el detalle de la orden anterior mientras se lee la nueva
        Detail = null;
        if (_selected is not { } order)
        {
            return;
        }
        try
        {
            var detail = await App.SendAsync(new GetPurchaseOrderQuery(order.Row.Id));
            if (ReferenceEquals(order, _selected))
            {
                Detail = detail;
            }
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
            App.Data.Invalidate();
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
            App.Data.Invalidate();
            await LoadAsync(force: true);
        }
    }

    private async Task ReceiveAsync()
    {
        var order = _selected!;
        // V4.2 · Los productos serializados pendientes se reciben con sus series o IMEI (regla T-02)
        var serialized = new List<SerialCaptureLineSpec>();
        try
        {
            var detail = await App.SendAsync(new GetPurchaseOrderQuery(order.Row.Id));
            foreach (var line in detail.Lines.Where(l => l.Quantity - l.Received > 0))
            {
                if (await TechCatalog.ProductAsync(App, line.Sku) is { TrackSerials: true } tech)
                {
                    serialized.Add(new SerialCaptureLineSpec(line.Sku, line.Name, tech.SerialKind, (int)Math.Ceiling(line.Quantity - line.Received),
                        SerialCaptureMode.Entry));
                }
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo leer la orden", AppServices.Describe(ex));
            return;
        }
        var document = await App.Dialogs.PromptAsync($"Recibir {order.Number}",
            $"Entra al stock todo lo pendiente de {order.Supplier} ({order.TotalText}), se actualiza el costo promedio y se registra el asiento " +
            "(inventario contra proveedores)." + (serialized.Count > 0 ? $" Después se piden las series de {serialized.Count} producto(s) serializado(s)." : ""),
            "Factura o remisión del proveedor (opcional)", [], serialized.Count > 0 ? "Continuar con las series" : "Recibir mercadería",
            placeholder: "Ej.: FAC-12345", glyph: Glyphs.Box);
        if (document is null)
        {
            return;
        }
        var supplierDocument = string.IsNullOrWhiteSpace(document) ? null : document;

        async Task ReceiveWithAsync(IReadOnlyList<MINV.Application.Tech.SkuSerials>? serials)
        {
            var result = await App.SendAsync(new ReceivePurchaseOrderCommand(order.Row.Id, supplierDocument, serials));
            App.Notify.Success($"Recepción {result.ReceiptNumber} registrada", $"{result.Lines} líneas · {Fmt.Money(result.Total)} · asiento {result.JournalNumber}");
            App.Data.Invalidate();
        }

        if (serialized.Count > 0)
        {
            var units = serialized.Sum(l => l.Expected);
            var captured = await SerialsDialog.AskAsync(App, $"Series de la recepción de {order.Number}",
                $"Escanee (o pegue) las {units} series o IMEI de las unidades que llegan de {order.Supplier}: una por unidad.", "Recibir mercadería",
                serialized, async serials =>
                {
                    await ReceiveWithAsync(serials);
                    return true;
                });
            if (captured is not null)
            {
                await LoadAsync(force: true);
            }
            return;
        }
        try
        {
            await ReceiveWithAsync(null);
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
            App.Data.Invalidate();
            await LoadAsync(force: true);
        }
    }

    /// <summary>V7 · Lo que se exporta: las órdenes visibles con sus filtros.</summary>
    public CsvTable ExportTable() => CsvTable.Of(
        ["Número", "Proveedor", "Fecha", "Entrega esperada", "Estado", "Líneas", "Total", "Recibido (%)", "Notas"],
        Rows.Cast<PurchaseOrderItem>(),
        r => [r.Number, r.Supplier, r.Row.OrderDate, r.Row.ExpectedDate, r.StatusText, r.Lines, r.Total, r.Row.ReceivedPercent, r.Row.Notes]);

    private void ExportCsv() => App.ExportCsv(App.CsvName("ordenes-compra"), "Órdenes de compra", ExportTable());
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

    public string StateText => Row.IsActive ? "Activo" : "Inactivo";
}

/// <summary>Proveedores: datos de contacto, días de entrega (combo), productos, órdenes abiertas y lo comprado. V7: filtros por
/// estado y órdenes abiertas en listas desplegables, búsqueda también por código y NIT, «Limpiar filtros» y «Exportar CSV».</summary>
public sealed class SuppliersViewModel : PageViewModel
{
    private List<SupplierItem> _items = [];
    private string _search = string.Empty;
    private SupplierEditor? _editor;
    private Choice<bool?> _state;
    private Choice<bool?> _orders;
    private string _summary = string.Empty;
    private int _visible;

    public SuppliersViewModel(AppServices app) : base(app, "proveedores", "Proveedores", "Contactos, plazos de entrega y compras", Glyphs.Briefcase)
    {
        States = [new("Activos e inactivos", null), new("Solo activos", true), new("Solo inactivos", false)];
        OrderFilters = [new("Con y sin órdenes abiertas", null), new("Con órdenes abiertas", true), new("Sin órdenes abiertas", false)];
        _state = States[0];
        _orders = OrderFilters[0];
        Rows = CollectionViewSource.GetDefaultView(_items);
        New = new RelayCommand(() => Editor = new SupplierEditor(this, App, null), () => CanEdit);
        Edit = new RelayCommand<SupplierItem>(s => Editor = new SupplierEditor(this, App, s.Row), _ => CanEdit);
        NewOrder = new RelayCommand(() => app.Navigator.Navigate("compras"));
        Export = new RelayCommand(() => App.ExportCsv(App.CsvName("proveedores"), "Proveedores", ExportTable()), () => _items.Count > 0);
        ClearFilters = new RelayCommand(() =>
        {
            _search = string.Empty;
            _state = States[0];
            _orders = OrderFilters[0];
            OnPropertiesChanged(nameof(Search), nameof(State), nameof(Orders));
            ApplyFilter();
        }, () => HasFilters);
    }

    public ICollectionView Rows { get; private set; }

    public bool CanEdit => App.Session.Can(PermissionCodes.PurchasingManage);

    public IReadOnlyList<Choice<bool?>> States { get; }

    public IReadOnlyList<Choice<bool?>> OrderFilters { get; }

    public Choice<bool?> State { get => _state; set { if (Set(ref _state, value ?? States[0])) { ApplyFilter(); } } }

    public Choice<bool?> Orders { get => _orders; set { if (Set(ref _orders, value ?? OrderFilters[0])) { ApplyFilter(); } } }

    public bool HasFilters => _search.Trim().Length > 0 || _state.Value is not null || _orders.Value is not null;

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public bool IsEmpty => HasLoaded && _visible == 0;

    public RelayCommand Export { get; }

    public RelayCommand ClearFilters { get; }

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    private bool Matches(object o)
    {
        if (o is not SupplierItem s)
        {
            return false;
        }
        if (_state.Value is { } active && s.IsActive != active)
        {
            return false;
        }
        if (_orders.Value is { } open && (s.OpenOrders > 0) != open)
        {
            return false;
        }
        var q = _search.Trim();
        return q.Length == 0 || s.Code.Contains(q, StringComparison.OrdinalIgnoreCase) || FilterChoices.Contains(s.Name, q) || FilterChoices.Contains(s.Contact, q)
               || (s.Row.TaxId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        _visible = Rows.Cast<object>().Count();
        Summary = _visible == _items.Count ? $"{_items.Count} proveedores" : $"{_visible} de {_items.Count} proveedores";
        OnPropertiesChanged(nameof(IsEmpty), nameof(HasFilters));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>V7 · Lo que se exporta: los proveedores visibles con sus filtros.</summary>
    public CsvTable ExportTable() => CsvTable.Of(
        ["Código", "Proveedor", "NIT", "Contacto", "Teléfono", "Correo", "Días de entrega", "Productos", "Órdenes abiertas", "Comprado", "Activo"],
        Rows.Cast<SupplierItem>(),
        s => [s.Code, s.Name, s.Row.TaxId, s.Row.Contact, s.Row.Phone, s.Row.Email, s.Row.LeadTimeDays, s.Products, s.OpenOrders, s.Purchased, s.IsActive]);

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
        Rows.Filter = Matches;
        OnPropertyChanged(nameof(Rows));
        ApplyFilter();
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
        App.Data.Invalidate();   // V7 · Compras y el catálogo muestran el proveedor (nombre y plazo) al volver
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
