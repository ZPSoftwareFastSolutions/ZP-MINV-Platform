using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using MINV.Application.Tech;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.ViewModels;

/// <summary>V4.2 · Fila de la lista de series e IMEI.</summary>
public sealed class SerialItem(SerialRow row, DateOnly today)
{
    public SerialRow Row { get; } = row;

    public string Serial => Row.Serial;

    public string KindBadge => TechText.KindBadge(Row.Kind);

    public string Sku => Row.Sku;

    public string Product => Row.Product;

    public SerialNumberStatus Status => Row.Status;

    public string StatusText => TechText.SerialStatus(Row.Status);

    public string StatusBrush => TechText.SerialBrush(Row.Status);

    public string StatusSoftBrush => StatusBrush + "Soft";

    public string Location => string.Join(" · ", new[] { Row.Branch, Row.Warehouse }.Where(t => !string.IsNullOrWhiteSpace(t)));

    public string SoldText => Row.SoldAt is { } at ? (Row.InvoiceNumber is { Length: > 0 } invoice ? $"{Fmt.Date(DateOnly.FromDateTime(at.ToLocalTime().DateTime))} · {invoice}" : Fmt.DateTime(at)) : "—";

    public string CustomerText => Row.Customer ?? string.Empty;

    public DateOnly? WarrantyUntil => Row.WarrantyUntil;

    public string WarrantyText => Row.WarrantyUntil is { } until ? (until >= today ? $"Hasta {Fmt.Date(until)}" : $"Venció {Fmt.Date(until)}") : "—";

    public string WarrantyBrush => Row.WarrantyUntil is { } until ? until >= today ? "Success" : "Danger" : "TextMuted";

    public DateTimeOffset? ReceivedAt => Row.ReceivedAt;
}

/// <summary>V4.2 · Hecho de la línea de tiempo de una serie.</summary>
public sealed class SerialEventItem(SerialEventView e)
{
    public string Title => TechText.SerialAction(e.Action);

    public string Glyph => TechText.SerialActionGlyph(e.Action);

    public string Brush => TechText.SerialActionBrush(e.Action);

    public string SoftBrush => Brush == "Brand" ? "BrandSoft" : Brush + "Soft";

    public string When => Fmt.DateTime(e.OccurredAt);

    public string Detail => string.Join(" · ", new[] { e.Branch, e.DocumentNumber, e.User }.Where(t => !string.IsNullOrWhiteSpace(t)));

    public string? Note => e.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(e.Note);
}

/// <summary>
/// V4.2 · Series e IMEI: búsqueda por serie, IMEI o SKU, estado y sucursal; trazabilidad completa de cada unidad (línea de
/// tiempo con documentos y usuarios), garantía vigente DERIVADA (regla T-04), casos RMA de la unidad y acceso a abrir uno.
/// También registra las series de unidades que ya estaban en stock y da destino a las devueltas o en garantía.
/// </summary>
public sealed class SerialsViewModel : PageViewModel
{
    private static readonly Choice<SerialNumberStatus?> AllStatuses = new("Todos los estados", null);
    private static readonly Choice<string?> AllBranches = new("Todas las sucursales", null);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private List<SerialItem> _items = [];
    private string _search = string.Empty;
    private Choice<SerialNumberStatus?> _status = AllStatuses;
    private Choice<string?> _branch = AllBranches;
    private SerialItem? _selected;
    private SerialTraceView? _trace;
    private bool _loadingTrace;
    private string _summary = string.Empty;
    private string? _pendingSerial;
    private bool _truncated;

    /// <summary>La lista trae las series más recientes hasta este tope (los indicadores cuentan todas).</summary>
    private const int ListMax = 1000;

    public SerialsViewModel(AppServices app)
        : base(app, "series", "Series e IMEI", "Cada unidad: dónde está, a quién se vendió, su garantía y sus casos RMA", Glyphs.Barcode)
    {
        Statuses = [AllStatuses, .. Enum.GetValues<SerialNumberStatus>().Where(s => s != SerialNumberStatus.Reserved)
            .Select(s => new Choice<SerialNumberStatus?>(TechText.SerialStatus(s), s))];
        Rows = CollectionViewSource.GetDefaultView(_items);
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await LoadAsync(force: true);
        };
        OpenClaim = new RelayCommand(() => App.Navigator.Navigate("garantias", new OpenClaimRequest(_selected!.Serial, _selected.Sku)),
            () => _selected?.Status is SerialNumberStatus.Sold or SerialNumberStatus.Returned
                  && _trace?.Claims.All(c => c.Status == MINV.Domain.Service.WarrantyClaimStatus.Delivered) != false
                  && App.Session.Can(PermissionCodes.ServiceOpen));
        DisposeUnit = new AsyncRelayCommand(DisposeAsync, () => CanManage && _selected?.Status is SerialNumberStatus.Returned or SerialNumberStatus.InRma);
        RegisterStock = new AsyncRelayCommand(RegisterStockAsync, () => CanManage);
        OpenProduct = new RelayCommand(() => App.Navigator.OpenProduct(_selected!.Sku), () => _selected is not null);
        ClearFilters = new RelayCommand(() =>
        {
            _search = string.Empty;
            _status = AllStatuses;
            _branch = AllBranches;
            OnPropertiesChanged(nameof(Search), nameof(Status), nameof(Branch));
            _ = LoadAsync(force: true);
        });
    }

    public ICollectionView Rows { get; private set; }

    public IReadOnlyList<Choice<SerialNumberStatus?>> Statuses { get; }

    public BulkObservableCollection<Choice<string?>> Branches { get; } = [];

    public BulkObservableCollection<SerialEventItem> Events { get; } = [];

    public BulkObservableCollection<WarrantyClaimItem> Claims { get; } = [];

    public bool CanManage => App.Session.Can(PermissionCodes.SerialsManage);

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

    public Choice<SerialNumberStatus?> Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value ?? AllStatuses))
            {
                _ = LoadAsync(force: true);
            }
        }
    }

    public Choice<string?> Branch
    {
        get => _branch;
        set
        {
            if (Set(ref _branch, value ?? AllBranches))
            {
                ApplyFilter();
            }
        }
    }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public bool IsEmpty => HasLoaded && Rows.IsEmpty;

    /// <summary>Hay búsqueda, estado o sucursal elegidos (el estado vacío ofrece quitarlos).</summary>
    public bool HasFilters => !string.IsNullOrWhiteSpace(_search) || _status.Value is not null || _branch.Value is not null;

    public string EmptyTitle => HasFilters ? "No hay series con ese filtro" : "Todavía no hay unidades con serie o IMEI";

    public KpiCard InStockKpi { get; } = new("En stock", Glyphs.Box, "Success", "SuccessSoft");

    public KpiCard SoldKpi { get; } = new("Vendidas con garantía vigente", Glyphs.Shield, "Info", "InfoSoft");

    public KpiCard RmaKpi { get; } = new("En garantía (RMA) o devueltas", Glyphs.Wrench, "Warning", "WarningSoft");

    public KpiCard OutKpi { get; } = new("Bajas y devueltas al proveedor", Glyphs.Delete, "Danger", "DangerSoft");

    public SerialItem? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                _ = LoadTraceAsync();
            }
        }
    }

    public bool HasSelection => _selected is not null;

    public SerialTraceView? Trace { get => _trace; private set => Set(ref _trace, value); }

    public bool IsLoadingTrace { get => _loadingTrace; private set => Set(ref _loadingTrace, value); }

    public string WarrantyTitle => _trace switch
    {
        null => string.Empty,
        { WarrantyMonths: <= 0 } => "El producto no tiene garantía",
        { Serial.WarrantyUntil: null } t => $"Garantía de {TechFormat.Months(t.WarrantyMonths)} (corre desde la venta)",
        { InWarranty: true } t => $"En garantía hasta el {Fmt.Date(t.Serial.WarrantyUntil)}",
        var t => $"Garantía vencida el {Fmt.Date(t.Serial.WarrantyUntil)}",
    };

    public string WarrantyDetail => _trace is { } t
        ? $"{TechFormat.Months(t.WarrantyMonths)} desde la venta (se calcula al consultar: fecha de la venta + meses del producto)."
        : string.Empty;

    public string WarrantyBrush => _trace switch
    {
        { InWarranty: true } => "Success",
        { Serial.WarrantyUntil: not null } => "Danger",
        _ => "Info",
    };

    public string WarrantySoftBrush => WarrantyBrush + "Soft";

    public string OriginText => _trace is { } t
        ? string.Join(" · ", new[] { t.Supplier is { } s ? "Proveedor " + s : null, t.ReceiptNumber is { } r ? "recepción " + r : null,
            t.Serial.ReceivedAt is { } at ? "ingresó " + Fmt.DateTime(at) : null }.Where(x => x is not null))
        : string.Empty;

    public string SaleText => _trace?.Serial is { SoldAt: { } at } s ? $"Vendida el {Fmt.DateTime(at)} · {s.InvoiceNumber} · {s.Customer ?? "cliente de otra sucursal"}" : "Sin venta vigente";

    public bool HasClaims => Claims.Count > 0;

    public RelayCommand OpenClaim { get; }

    public AsyncRelayCommand DisposeUnit { get; }

    public AsyncRelayCommand RegisterStock { get; }

    public RelayCommand OpenProduct { get; }

    public RelayCommand ClearFilters { get; }

    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is string serial)
        {
            _pendingSerial = serial;
            _search = serial;
            OnPropertyChanged(nameof(Search));
            if (HasLoaded)
            {
                _ = LoadAsync(force: true);
            }
        }
    }

    protected override async Task LoadCoreAsync(bool force)
    {
        var rows = await App.SendAsync(new SearchSerialsQuery(string.IsNullOrWhiteSpace(_search) ? null : _search.Trim(), _status.Value, Max: ListMax));
        var totals = await App.SendAsync(new GetSerialSummaryQuery());   // V4.2: los indicadores cuentan TODAS las series
        var today = App.Session.Workspace.Today;
        var selected = _pendingSerial ?? _selected?.Serial;
        _pendingSerial = null;
        _items = rows.Select(r => new SerialItem(r, today)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = o => o is SerialItem s && (_branch.Value is not { } b || s.Row.Branch == b);
        Rows.SortDescriptions.Add(new SortDescription(nameof(SerialItem.ReceivedAt), ListSortDirection.Descending));
        OnPropertyChanged(nameof(Rows));
        var previous = _branch.Value;
        Branches.ReplaceAll([AllBranches, .. rows.Where(r => r.Branch is not null).Select(r => r.Branch!).Distinct().Order()
            .Select(b => new Choice<string?>(b, b))]);
        _branch = Branches.FirstOrDefault(b => b.Value == previous) ?? AllBranches;
        OnPropertyChanged(nameof(Branch));

        InStockKpi.Value = totals.InStock.ToString("N0", Fmt.Culture);
        InStockKpi.Detail = totals.InStockProducts == 1 ? "1 producto" : $"{totals.InStockProducts.ToString("N0", Fmt.Culture)} productos";
        SoldKpi.Value = totals.SoldInWarranty.ToString("N0", Fmt.Culture);
        SoldKpi.Detail = $"de {totals.Sold.ToString("N0", Fmt.Culture)} vendidas";
        RmaKpi.Value = totals.InRmaOrReturned.ToString("N0", Fmt.Culture);
        RmaKpi.Detail = "Esperan diagnóstico o destino";
        OutKpi.Value = totals.Out.ToString("N0", Fmt.Culture);
        OutKpi.Detail = "Fuera del inventario";
        _truncated = rows.Count >= ListMax;
        ApplyFilter();
        _selected = selected is null ? null : _items.FirstOrDefault(i => i.Serial.Equals(selected, StringComparison.OrdinalIgnoreCase));
        OnPropertiesChanged(nameof(Selected), nameof(HasSelection));
        await LoadTraceAsync();
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        var visible = Rows.Cast<object>().Count();
        Summary = visible == _items.Count ? (_items.Count == 1 ? "1 serie" : $"{_items.Count.ToString("N0", Fmt.Culture)} series") : $"{visible} de {_items.Count} series";
        if (_truncated)
        {
            Summary += " · las más recientes: afine la búsqueda";   // la lista se limita; los indicadores cuentan todas
        }
        OnPropertiesChanged(nameof(IsEmpty), nameof(HasFilters), nameof(EmptyTitle));
    }

    private async Task LoadTraceAsync()
    {
        if (_selected is not { } item)
        {
            Trace = null;
            Events.ReplaceAll([]);
            Claims.ReplaceAll([]);
            RaiseTrace();
            return;
        }
        IsLoadingTrace = true;
        try
        {
            var trace = await App.SendAsync(new GetSerialTraceQuery(item.Serial, item.Sku));
            if (!ReferenceEquals(item, _selected))
            {
                return;
            }
            Trace = trace;
            Events.ReplaceAll(trace.Events.OrderByDescending(e => e.OccurredAt).Select(e => new SerialEventItem(e)));
            Claims.ReplaceAll(trace.Claims.Select(c => new WarrantyClaimItem(c)));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo leer la trazabilidad", AppServices.Describe(ex));
        }
        finally
        {
            IsLoadingTrace = false;
            RaiseTrace();
        }
    }

    private void RaiseTrace()
    {
        OnPropertiesChanged(nameof(WarrantyTitle), nameof(WarrantyDetail), nameof(WarrantyBrush), nameof(WarrantySoftBrush), nameof(OriginText), nameof(SaleText),
            nameof(HasClaims));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async Task DisposeAsync()
    {
        var item = _selected!;
        var dialog = new DisposeSerialDialog(App, item);
        if (await App.Dialogs.ShowAsync(dialog))
        {
            App.Notify.Success("Destino registrado", dialog.Message);
            App.Data.Invalidate();
            await LoadAsync(force: true);
        }
    }

    private async Task RegisterStockAsync()
    {
        var products = (await TechCatalog.LoadAsync(App)).Values.Where(p => p.TrackSerials || p.Stock > 0).OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
        var dialog = new RegisterSerialsDialog(App, products);
        if (await App.Dialogs.ShowAsync(dialog))
        {
            App.Notify.Success("Series registradas", dialog.Message);
            await LoadAsync(force: true);
        }
    }
}

/// <summary>V4.2 · Parámetro de navegación: abrir un caso RMA de esta serie.</summary>
public sealed record OpenClaimRequest(string Serial, string? Sku);

/// <summary>V4.2 · Destino de una unidad devuelta o en garantía: al proveedor o de baja, con el motivo.</summary>
public sealed class DisposeSerialDialog : FormDialog
{
    private readonly AppServices _app;
    private readonly SerialItem _item;
    private Choice<SerialDisposal> _disposal;
    private string _reason = string.Empty;

    public DisposeSerialDialog(AppServices app, SerialItem item)
        : base($"Destino de {item.Serial}", "Registrar destino", Glyphs.Delete, isDanger: true)
    {
        _app = app;
        _item = item;
        Options = [new("Devolver al proveedor (reemplazo o nota de crédito)", SerialDisposal.ReturnToSupplier), new("Dar de baja (irreparable)", SerialDisposal.Scrap)];
        _disposal = Options[0];
    }

    public override string? Subtitle => $"{_item.Product} · {_item.StatusText}. La unidad sale definitivamente del inventario y queda en su bitácora.";

    public IReadOnlyList<Choice<SerialDisposal>> Options { get; }

    public Choice<SerialDisposal> Disposal { get => _disposal; set => Set(ref _disposal, value ?? Options[0]); }

    public IReadOnlyList<string> Reasons { get; } = ["Falla de fábrica confirmada", "Reemplazo del proveedor", "Equipo irreparable", "Daño físico"];

    public string Reason { get => _reason; set => Set(ref _reason, value ?? string.Empty); }

    public string Message { get; private set; } = string.Empty;

    protected override bool CanConfirm() => _reason.Trim().Length >= 3;

    protected override async Task<bool> SubmitAsync()
    {
        Message = await _app.SendAsync(new DisposeSerialCommand(_item.Serial, _disposal.Value, _reason.Trim(), _item.Sku));
        return true;
    }
}

/// <summary>V4.2 · Registrar series de unidades que ya estaban en stock SIN serie (inventario inicial o antes de pasar el
/// producto a «lleva serie»).</summary>
public sealed class RegisterSerialsDialog : FormDialog, IScannerTarget
{
    private readonly AppServices _app;
    private readonly Action _changed;
    private TechProductRow? _product;
    private SerialCaptureLine? _line;
    private string _note = "Inventario inicial de series";

    public RegisterSerialsDialog(AppServices app, IReadOnlyList<TechProductRow> products)
        : base("Registrar series de unidades en stock", "Registrar series", Glyphs.Barcode, width: 660)
    {
        _app = app;
        Products = products;
        _changed = () => System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        Product = products.FirstOrDefault(p => p.TrackSerials) ?? products.FirstOrDefault();
    }

    public override string? Subtitle => "Para unidades que ya están en el stock de la sucursal activa sin serie: nunca más series que unidades sin serie.";

    public IReadOnlyList<TechProductRow> Products { get; }

    public TechProductRow? Product
    {
        get => _product;
        set
        {
            if (Set(ref _product, value))
            {
                Line = value is null ? null : new SerialCaptureLine(value.Sku, value.Name, value.SerialKind, 1, SerialCaptureMode.Entry, _changed, flexible: true);
            }
        }
    }

    public SerialCaptureLine? Line { get => _line; private set => Set(ref _line, value); }

    public string Note { get => _note; set => Set(ref _note, value ?? string.Empty); }

    public string Message { get; private set; } = string.Empty;

    public bool OnScanned(string code) => _line?.Accept(code) ?? false;

    protected override bool CanConfirm() => _line is { IsComplete: true };

    protected override async Task<bool> SubmitAsync()
    {
        Message = await _app.SendAsync(new RegisterStockSerialsCommand(_product!.Sku, _line!.Serials, string.IsNullOrWhiteSpace(_note) ? null : _note.Trim()));
        return true;
    }
}
