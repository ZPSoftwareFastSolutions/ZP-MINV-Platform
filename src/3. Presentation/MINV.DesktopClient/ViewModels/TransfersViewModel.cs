using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using MINV.Application.Catalog;
using MINV.Application.Corporate;
using MINV.Application.Inventory.Transfers;
using MINV.Application.Tech;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Fila de la lista de transferencias.</summary>
public sealed class TransferItem(TransferRow r)
{
    public TransferRow Row { get; } = r;

    public string Number => Row.Number;

    public string Route => $"{Row.FromBranchCode} → {Row.ToBranchCode} · {Row.ToBranch}";

    public string RouteLong => $"{Row.FromBranch} → {Row.ToBranch}";

    public string WarehousesText => $"{Row.FromWarehouse} → {Row.ToWarehouse}";

    public TransferStatus Status => Row.Status;

    public string StatusText => TransferText.Status(Row.Status);

    public string StatusBrush => TransferText.Brush(Row.Status);

    public string StatusSoftBrush => TransferText.Brush(Row.Status) + "Soft";

    public DateTimeOffset RequestedAt => Row.RequestedAt;

    public string RequestedText => Fmt.DateTime(Row.RequestedAt);

    public string LinesText => Row.Lines == 1 ? "1 producto" : $"{Row.Lines} productos";

    public string QuantityText => Fmt.Qty(Row.Quantity);

    public decimal Value => Row.Value;

    public string ValueText => Row.Status == TransferStatus.Pending ? "—" : Fmt.Money(Row.Value);

    public string ShortageText => Row.Shortage > 0 ? $"Faltante {Fmt.Qty(Row.Shortage)}" : "";

    public string DatesText => Row.Status switch
    {
        TransferStatus.Dispatched => $"despachada {Fmt.DateTime(Row.DispatchedAt!.Value)}",
        TransferStatus.Received => $"recibida {Fmt.DateTime(Row.ReceivedAt!.Value)}",
        _ => $"solicitada {Fmt.DateTime(Row.RequestedAt)}",
    };
}

public static class TransferText
{
    public static string Status(TransferStatus status) => status switch
    {
        TransferStatus.Pending => "Pendiente",
        TransferStatus.Dispatched => "En tránsito",
        TransferStatus.Received => "Recibida",
        _ => "Anulada",
    };

    public static string Brush(TransferStatus status) => status switch
    {
        TransferStatus.Pending => "Warning",
        TransferStatus.Dispatched => "Info",
        TransferStatus.Received => "Success",
        _ => "StatusInactive",
    };
}

/// <summary>
/// V4 · Transferencias entre sucursales: el ORIGEN solicita y despacha (la mercadería sale y queda en tránsito), el
/// DESTINO recibe (con faltantes y su motivo, que quedan como merma en tránsito). Cada paso es una transacción en el
/// servidor; la pantalla solo ofrece lo que la sesión puede hacer con cada transferencia.
/// </summary>
public sealed class TransfersViewModel : PageViewModel
{
    private static readonly Choice<TransferStatus?> AllStatuses = new("Todos los estados", null);
    private List<TransferItem> _items = [];
    private Choice<TransferStatus?> _status = AllStatuses;
    private Choice<string?> _from;
    private Choice<string?> _to;
    private Choice<PeriodOption?> _period;
    private string _search = string.Empty;
    private TransferItem? _selected;
    private TransferDetail? _detail;
    private TransferEditor? _editor;
    private TransferReceiptEditor? _receipt;
    private string _summary = string.Empty;

    public TransfersViewModel(AppServices app)
        : base(app, "transferencias", "Transferencias", "Mercadería entre sucursales: solicitar, despachar y recibir", Glyphs.Transfer)
    {
        Statuses =
        [
            AllStatuses, new("Pendientes", TransferStatus.Pending), new("En tránsito", TransferStatus.Dispatched), new("Recibidas", TransferStatus.Received),
            new("Anuladas", TransferStatus.Cancelled),
        ];
        FromBranches.ReplaceAll([new Choice<string?>("Desde cualquier sucursal", null)]);
        ToBranches.ReplaceAll([new Choice<string?>("Hacia cualquier sucursal", null)]);
        _from = FromBranches[0];
        _to = ToBranches[0];
        Periods.ReplaceAll(FilterChoices.Periods(Today));
        _period = Periods[0];
        Rows = CollectionViewSource.GetDefaultView(_items);
        New = new AsyncRelayCommand(OpenEditorAsync, () => CanManage);
        Dispatch = new AsyncRelayCommand(DispatchAsync, () => CanManage && _selected?.Row.CanDispatch == true);
        Receive = new RelayCommand(OpenReceipt, () => CanManage && _selected?.Row.CanReceive == true && _detail?.Header.Id == _selected.Row.Id);
        Cancel = new AsyncRelayCommand(CancelAsync, () => CanManage && _selected?.Row.CanCancel == true);
        Export = new RelayCommand(() => App.ExportCsv(App.CsvName("transferencias"), "Transferencias", ExportTable()), () => _items.Count > 0);
        ClearFilters = new RelayCommand(() =>
        {
            _search = string.Empty;
            _status = AllStatuses;
            _from = FromBranches[0];
            _to = ToBranches[0];
            _period = Periods[0];
            OnPropertiesChanged(nameof(Search), nameof(Status), nameof(From), nameof(To), nameof(Period));
            ApplyFilter();
        }, () => HasFilters);
    }

    public ICollectionView Rows { get; private set; }

    public IReadOnlyList<Choice<TransferStatus?>> Statuses { get; }

    /// <summary>V7 · Sucursal de origen y de destino en listas desplegables (las que aparecen en las transferencias).</summary>
    public BulkObservableCollection<Choice<string?>> FromBranches { get; } = [];

    public BulkObservableCollection<Choice<string?>> ToBranches { get; } = [];

    /// <summary>V7 · Fecha de la solicitud: hoy, esta semana, este mes…</summary>
    public BulkObservableCollection<Choice<PeriodOption?>> Periods { get; } = [];

    private DateOnly Today => DateOnly.FromDateTime(App.Now.ToLocalTime().DateTime);

    public bool CanManage => App.Session.Can(PermissionCodes.TransfersManage);

    public Choice<TransferStatus?> Status { get => _status; set { if (Set(ref _status, value ?? AllStatuses)) { ApplyFilter(); } } }

    public Choice<string?> From { get => _from; set { if (Set(ref _from, value ?? FromBranches[0])) { ApplyFilter(); } } }

    public Choice<string?> To { get => _to; set { if (Set(ref _to, value ?? ToBranches[0])) { ApplyFilter(); } } }

    public Choice<PeriodOption?> Period { get => _period; set { if (Set(ref _period, value ?? Periods[0])) { ApplyFilter(); } } }

    /// <summary>V7 · Búsqueda por número, sucursal, almacén o nota.</summary>
    public string Search { get => _search; set { if (Set(ref _search, value ?? string.Empty)) { ApplyFilter(); } } }

    public bool HasFilters => _search.Trim().Length > 0 || _status.Value is not null || _from.Value is not null || _to.Value is not null || _period.Value is not null;

    public RelayCommand Export { get; }

    public RelayCommand ClearFilters { get; }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public KpiCard PendingKpi { get; } = new("Pendientes de despacho", Glyphs.Clock, "Warning", "WarningSoft");

    public KpiCard TransitKpi { get; } = new("En tránsito", Glyphs.Transfer, "Info", "InfoSoft");

    public KpiCard ReceivedKpi { get; } = new("Recibidas este mes", Glyphs.CheckCircle, "Success", "SuccessSoft");

    public KpiCard ShortageKpi { get; } = new("Faltantes este mes", Glyphs.Warning, "Danger", "DangerSoft");

    public TransferItem? Selected
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

    public TransferDetail? Detail { get => _detail; private set => Set(ref _detail, value); }

    public TransferEditor? Editor
    {
        get => _editor;
        private set
        {
            if (Set(ref _editor, value))
            {
                // V7 · IsCreating no se avisaba: el panel «Nueva transferencia» nunca aparecía
                OnPropertiesChanged(nameof(IsEditing), nameof(IsCreating));
            }
        }
    }

    public bool IsEditing => _editor is not null || _receipt is not null;

    public TransferReceiptEditor? Receipt
    {
        get => _receipt;
        private set
        {
            if (Set(ref _receipt, value))
            {
                OnPropertiesChanged(nameof(IsEditing), nameof(IsReceiving));
            }
        }
    }

    public bool IsReceiving => _receipt is not null;

    public bool IsCreating => _editor is not null;

    public bool IsEmpty => HasLoaded && Rows.IsEmpty;

    public AsyncRelayCommand New { get; }

    public AsyncRelayCommand Dispatch { get; }

    public RelayCommand Receive { get; }

    public AsyncRelayCommand Cancel { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var rows = await App.SendAsync(new GetTransfersQuery());
        var selected = _selected?.Row.Id;
        _items = rows.Select(r => new TransferItem(r)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = Matches;
        Rows.SortDescriptions.Add(new SortDescription(nameof(TransferItem.RequestedAt), ListSortDirection.Descending));
        OnPropertyChanged(nameof(Rows));
        FromBranches.ReplaceAll(FilterChoices.Of("Desde cualquier sucursal", rows.Select(r => r.FromBranch)));
        ToBranches.ReplaceAll(FilterChoices.Of("Hacia cualquier sucursal", rows.Select(r => r.ToBranch)));
        Periods.ReplaceAll(FilterChoices.Periods(Today));
        _from = FilterChoices.Keep(FromBranches, _from);
        _to = FilterChoices.Keep(ToBranches, _to);
        _period = FilterChoices.Keep(Periods, _period);
        OnPropertiesChanged(nameof(From), nameof(To), nameof(Period));
        var today = DateOnly.FromDateTime(App.Now.ToLocalTime().DateTime);
        var monthStart = new DateTimeOffset(new DateTime(today.Year, today.Month, 1));
        var pending = rows.Where(r => r.Status == TransferStatus.Pending).ToList();
        var transit = rows.Where(r => r.Status == TransferStatus.Dispatched).ToList();
        var received = rows.Where(r => r.Status == TransferStatus.Received && r.ReceivedAt >= monthStart).ToList();
        PendingKpi.Value = pending.Count.ToString("N0", Fmt.Culture);
        PendingKpi.Detail = pending.Count(p => p.CanDispatch) is var mine and > 0 ? $"{mine} esperan su despacho" : "Nada por despachar";
        TransitKpi.Value = Fmt.Money(transit.Sum(t => t.Value));
        TransitKpi.Detail = transit.Count == 0 ? "Nada en camino" : $"{transit.Count} transferencias · {transit.Count(t => t.CanReceive)} por recibir aquí";
        ReceivedKpi.Value = received.Count.ToString("N0", Fmt.Culture);
        ReceivedKpi.Detail = $"{Fmt.Money(received.Sum(r => r.Value))} movidos entre sucursales";
        ShortageKpi.Value = Fmt.Qty(received.Sum(r => r.Shortage));
        ShortageKpi.Detail = received.Count(r => r.Shortage > 0) is var withShortage and > 0 ? $"En {withShortage} recepciones (merma en tránsito)" : "Sin faltantes";
        ApplyFilter();
        if (selected is { } id)
        {
            Selected = _items.FirstOrDefault(i => i.Row.Id == id);
        }
    }

    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is TransferStatus status)
        {
            Status = Statuses.First(s => s.Value == status);
        }
    }

    internal void CloseEditors()
    {
        Editor = null;
        Receipt = null;
    }

    internal async Task AfterChangeAsync(TransferRef result)
    {
        CloseEditors();
        App.Data.Invalidate();
        await LoadAsync(force: true);
        Selected = _items.FirstOrDefault(i => i.Row.Id == result.Id);
    }

    private bool Matches(object o)
    {
        if (o is not TransferItem t)
        {
            return false;
        }
        if ((_status.Value is { } s && t.Status != s) || (_from.Value is { } from && t.Row.FromBranch != from) || (_to.Value is { } to && t.Row.ToBranch != to))
        {
            return false;
        }
        if (_period.Value is { } period)
        {
            var day = DateOnly.FromDateTime(t.RequestedAt.ToLocalTime().DateTime);
            if (day < period.From || day > period.To)
            {
                return false;
            }
        }
        var q = _search.Trim();
        return q.Length == 0 || t.Number.Contains(q, StringComparison.OrdinalIgnoreCase) || FilterChoices.Contains(t.RouteLong, q)
               || FilterChoices.Contains(t.WarehousesText, q) || FilterChoices.Contains(t.Row.Notes, q);
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        var visible = Rows.Cast<object>().Count();
        Summary = visible == _items.Count ? $"{_items.Count} transferencias" : $"{visible} de {_items.Count} transferencias";
        OnPropertiesChanged(nameof(IsEmpty), nameof(HasFilters));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>V7 · Lo que se exporta: las transferencias visibles con sus filtros.</summary>
    public CsvTable ExportTable() => CsvTable.Of(
        ["Número", "Estado", "Sucursal de origen", "Almacén de origen", "Sucursal de destino", "Almacén de destino", "Solicitada", "Despachada", "Recibida",
            "Productos", "Cantidad", "Valor", "Faltante", "Notas"],
        Rows.Cast<TransferItem>(),
        t => [t.Number, t.StatusText, t.Row.FromBranch, t.Row.FromWarehouse, t.Row.ToBranch, t.Row.ToWarehouse, t.Row.RequestedAt, t.Row.DispatchedAt,
            t.Row.ReceivedAt, t.Row.Lines, t.Row.Quantity, t.Row.Status == TransferStatus.Pending ? null : t.Row.Value, t.Row.Shortage, t.Row.Notes]);

    private async Task LoadDetailAsync()
    {
        Receipt = null;
        // V7 · Se limpia el detalle ANTES de leer el nuevo: mientras llegaba, «Recibir» usaba el de la transferencia anterior
        Detail = null;
        if (_selected is not { } transfer)
        {
            return;
        }
        try
        {
            var detail = await App.SendAsync(new GetTransferQuery(transfer.Row.Id));
            if (ReferenceEquals(_selected, transfer))
            {
                Detail = detail;
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            if (ReferenceEquals(_selected, transfer))
            {
                App.Notify.Error("No se pudo leer la transferencia", AppServices.Describe(ex));
            }
        }
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async Task OpenEditorAsync()
    {
        try
        {
            var branches = await App.SendAsync(new GetBranchesQuery());
            var catalog = await App.SendAsync(new GetCatalogQuery());
            var images = await App.Images.AllAsync();
            Selected = null;
            Editor = new TransferEditor(this, App, branches, catalog.Where(c => c.IsActive).ToList(), images);
        }
        catch (Exception ex)
        {
            App.Notify.Error("No se pudo abrir la transferencia nueva", AppServices.Describe(ex));
        }
    }

    private async Task DispatchAsync()
    {
        var t = _selected!;
        if (!await App.Dialogs.ConfirmAsync($"Despachar {t.Number}",
                $"{t.Route} · {t.LinesText}. La mercadería sale ahora del almacén {t.Row.FromWarehouse} (salidas por traslado, lotes que vencen " +
                "primero) y queda EN TRÁNSITO hasta que el destino la reciba. Se registra el asiento de envío.", "Despachar",
                glyph: Glyphs.Transfer))
        {
            return;
        }
        try
        {
            var result = await App.SendAsync(new DispatchTransferCommand(t.Row.Id));
            App.Notify.Success("Transferencia despachada", result.Message);
            await AfterChangeAsync(result);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo despachar", AppServices.Describe(ex));
            await LoadAsync(force: true);
        }
    }

    private void OpenReceipt()
    {
        if (_detail is { } detail && _selected is { } selected && detail.Header.Id == selected.Row.Id)
        {
            Receipt = new TransferReceiptEditor(this, App, detail);
        }
    }

    private async Task CancelAsync()
    {
        var t = _selected!;
        var reason = await App.Dialogs.PromptAsync($"Anular {t.Number}", "La transferencia todavía no movió stock: se anula y queda en la bitácora.",
            "Motivo", ["Ya no se necesita", "Se pidió por error", "Se abastecerá con una compra"], "Anular", isDanger: true);
        if (reason is null)
        {
            return;
        }
        if (reason.Length == 0)
        {
            // V7 · Antes, aceptar sin motivo no hacía nada y no decía por qué
            App.Notify.Warning("Falta el motivo", $"Elija o escriba por qué se anula {t.Number}: queda en la bitácora.");
            return;
        }
        if (await RunAsync(async () =>
            {
                var result = await App.SendAsync(new CancelTransferCommand(t.Row.Id, reason));
                App.Notify.Success("Transferencia anulada", result.Message);
                await AfterChangeAsync(result);
            }, "No se pudo anular"))
        {
            return;
        }
    }
}

/// <summary>Línea de una transferencia nueva.</summary>
public sealed class TransferDraftLine(CatalogItem product, System.Windows.Media.ImageSource? image) : ObservableObject
{
    private string _quantity = "1";

    public CatalogItem Product { get; } = product;

    public System.Windows.Media.ImageSource? Image { get; } = image;

    public string Quantity
    {
        get => _quantity;
        set => Set(ref _quantity, value);
    }
}

/// <summary>Destino posible (almacén de otra sucursal).</summary>
public sealed record WarehouseChoice(string Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Editor de una transferencia nueva: desde un almacén de la sucursal activa hacia el de otra sucursal.</summary>
public sealed class TransferEditor : ObservableObject
{
    private readonly TransfersViewModel _owner;
    private readonly AppServices _app;
    private readonly IReadOnlyDictionary<string, System.Windows.Media.ImageSource> _images;
    private WarehouseChoice? _from;
    private WarehouseChoice? _to;
    private CatalogItem? _product;
    private string _notes = string.Empty;
    private string? _error;

    public TransferEditor(TransfersViewModel owner, AppServices app, IReadOnlyList<BranchRow> branches, IReadOnlyList<CatalogItem> catalog,
        IReadOnlyDictionary<string, System.Windows.Media.ImageSource> images)
    {
        _owner = owner;
        _app = app;
        _images = images;
        var active = app.Session.Access.ActiveBranchId;
        Origins = branches.Where(b => b.IsVisible && b.IsActive && (active is null || b.Id == active))
            .SelectMany(b => b.Warehouses.Select(w => new WarehouseChoice(w, $"{w} · {b.Name}"))).ToList();
        Destinations = branches.Where(b => b.IsActive).SelectMany(b => b.Warehouses.Select(w => new WarehouseChoice(w, $"{w} · {b.Name}"))).ToList();
        _from = Origins.FirstOrDefault(o => o.Code == app.Session.Workspace.WarehouseCode) ?? Origins.FirstOrDefault();
        _to = Destinations.FirstOrDefault(d => d.Code != _from?.Code);
        Products = catalog.OrderBy(c => c.Name).ToList();
        AddLine = new RelayCommand(OnAddLine, () => _product is not null);
        RemoveLine = new RelayCommand<TransferDraftLine>(l =>
        {
            Lines.Remove(l);
            OnPropertyChanged(nameof(LinesText));
        });
        Save = new AsyncRelayCommand(SaveAsync, () => Lines.Count > 0 && _from is not null && _to is not null);
        Cancel = new RelayCommand(owner.CloseEditors);
    }

    public IReadOnlyList<WarehouseChoice> Origins { get; }

    public IReadOnlyList<WarehouseChoice> Destinations { get; }

    public IReadOnlyList<CatalogItem> Products { get; }

    public ObservableCollection<TransferDraftLine> Lines { get; } = [];

    public WarehouseChoice? From { get => _from; set => Set(ref _from, value); }

    public WarehouseChoice? To { get => _to; set => Set(ref _to, value); }

    public CatalogItem? Product { get => _product; set => Set(ref _product, value); }

    public string Notes { get => _notes; set => Set(ref _notes, value ?? string.Empty); }

    public string? Error { get => _error; private set => Set(ref _error, value); }

    public string LinesText => Lines.Count == 1 ? "1 producto" : $"{Lines.Count} productos";

    public RelayCommand AddLine { get; }

    public RelayCommand<TransferDraftLine> RemoveLine { get; }

    public AsyncRelayCommand Save { get; }

    public RelayCommand Cancel { get; }

    private void OnAddLine()
    {
        if (_product is null)
        {
            return;
        }
        if (Lines.All(l => l.Product.Sku != _product.Sku))
        {
            Lines.Add(new TransferDraftLine(_product, _images.GetValueOrDefault(_product.Sku)));
            OnPropertyChanged(nameof(LinesText));
        }
        Product = null;
    }

    private async Task SaveAsync()
    {
        Error = null;
        var parsed = new List<(CatalogItem Product, decimal Quantity)>();
        foreach (var line in Lines)
        {
            if (!Numbers.TryParse(line.Quantity, out var quantity) || quantity <= 0)
            {
                Error = $"Revise la cantidad de {line.Product.Name}.";
                return;
            }
            parsed.Add((line.Product, quantity));
        }
        var from = _from!;
        var to = _to!;
        var notes = string.IsNullOrWhiteSpace(_notes) ? null : _notes.Trim();

        // V4.2 · Productos serializados: las unidades que viajan, una serie por unidad (regla T-02). Se eligen de las
        // disponibles del almacén de origen (o se escanean); el dominio vuelve a validarlas al solicitar y al despachar.
        var serialized = new List<SerialCaptureLineSpec>();
        foreach (var (product, quantity) in parsed)
        {
            if (await TechCatalog.ProductAsync(_app, product.Sku) is not { TrackSerials: true } tech)
            {
                continue;
            }
            if (decimal.Truncate(quantity) != quantity)
            {
                Error = $"{product.Name} lleva {TechText.KindLabel(tech.SerialKind)}: la cantidad debe ser entera (una serie por unidad).";
                return;
            }
            IReadOnlyList<SerialRow> available = [];
            try
            {
                available = await _app.SendAsync(new GetAvailableSerialsQuery(product.Sku, from.Code));
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                System.Diagnostics.Trace.TraceWarning("M-INV · series disponibles de {0}: {1}", product.Sku, ex.Message);
            }
            serialized.Add(new SerialCaptureLineSpec(product.Sku, product.Name, tech.SerialKind, (int)quantity,
                available.Count > 0 ? SerialCaptureMode.Pick : SerialCaptureMode.Entry, available.Count > 0 ? available : null));
        }

        async Task<TransferRef> CreateAsync(IReadOnlyList<SkuSerials>? serials) =>
            await _app.SendAsync(new CreateTransferCommand(to.Code, parsed.Select(p => new TransferLineInput(p.Product.Sku, p.Quantity,
                serials?.FirstOrDefault(s => s.Sku.Equals(p.Product.Sku, StringComparison.OrdinalIgnoreCase))?.Serials)).ToList(), notes, from.Code));

        TransferRef? created = null;
        if (serialized.Count > 0)
        {
            await SerialsDialog.AskAsync(_app, "Series de la transferencia",
                $"Elija (o escanee) las unidades que viajan de {from.Label} a {to.Label}: una serie por unidad.", "Solicitar transferencia", serialized,
                async serials =>
                {
                    created = await CreateAsync(serials);
                    return true;
                });
        }
        else
        {
            try
            {
                created = await CreateAsync(null);
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                Error = AppServices.Describe(ex);
            }
        }
        if (created is not null)
        {
            _app.Notify.Success("Transferencia solicitada", created.Message);
            await _owner.AfterChangeAsync(created);
        }
    }
}

/// <summary>Lo recibido de una línea (por defecto, todo lo despachado).</summary>
public sealed class ReceiptLine : ObservableObject
{
    private string _received;
    private string _reason = string.Empty;

    public ReceiptLine(TransferLineRow line)
    {
        Line = line;
        _received = Numbers.Plain(line.Quantity);
    }

    public TransferLineRow Line { get; }

    public string Received
    {
        get => _received;
        set
        {
            if (Set(ref _received, value))
            {
                OnPropertyChanged(nameof(HasShortage));
            }
        }
    }

    public string Reason { get => _reason; set => Set(ref _reason, value ?? string.Empty); }

    public bool HasShortage => Numbers.TryParse(_received, out var q) && q < Line.Quantity;

    public string ShippedText => $"Despachado {Fmt.Qty(Line.Quantity)} {Line.Unit}";

    /// <summary>V4.2 · Series (o IMEI) que viajan en la línea: si falta alguna, al confirmar se indica cuáles no llegaron.</summary>
    public bool HasSerials => Line.Serials is { Count: > 0 };

    public string SerialsText => Line.Serials is { Count: > 0 } serials ? string.Join(", ", serials) : string.Empty;
}

/// <summary>
/// Recepción en el destino: por cada producto, lo que llegó. Si llegó menos, el faltante exige motivo y queda como
/// registro compensatorio (merma en tránsito); nunca se puede recibir más de lo despachado.
/// </summary>
public sealed class TransferReceiptEditor : ObservableObject
{
    private readonly TransfersViewModel _owner;
    private readonly AppServices _app;
    private readonly TransferDetail _detail;
    private string? _error;

    public TransferReceiptEditor(TransfersViewModel owner, AppServices app, TransferDetail detail)
    {
        _owner = owner;
        _app = app;
        _detail = detail;
        Lines = detail.Lines.Select(l => new ReceiptLine(l)).ToList();
        Save = new AsyncRelayCommand(SaveAsync);
        Cancel = new RelayCommand(owner.CloseEditors);
    }

    public string Title => $"Recibir {_detail.Header.Number}";

    public string RouteText => $"{_detail.Header.FromBranch} → {_detail.Header.ToBranch} · almacén {_detail.Header.ToWarehouse}";

    public IReadOnlyList<ReceiptLine> Lines { get; }

    public string? Error { get => _error; private set => Set(ref _error, value); }

    public AsyncRelayCommand Save { get; }

    public RelayCommand Cancel { get; }

    private async Task SaveAsync()
    {
        Error = null;
        var inputs = new List<TransferReceiptInput>();
        var missing = new List<SerialCaptureLineSpec>();
        foreach (var line in Lines)
        {
            if (!Numbers.TryParse(line.Received, out var received, emptyIsZero: true) || received < 0)
            {
                Error = $"Revise lo recibido de {line.Line.Name}.";
                return;
            }
            if (received < line.Line.Quantity && string.IsNullOrWhiteSpace(line.Reason))
            {
                Error = $"Indique el motivo del faltante de {line.Line.Name}.";
                return;
            }
            inputs.Add(new TransferReceiptInput(line.Line.Sku, received, received < line.Line.Quantity ? line.Reason.Trim() : null));
            // V4.2 · Faltante de un producto serializado: se indica QUÉ series no llegaron (regla T-02); las demás entran
            if (line.Line.Serials is { Count: > 0 } travelling && received < line.Line.Quantity)
            {
                if (decimal.Truncate(received) != received)
                {
                    Error = $"{line.Line.Name} lleva serie: lo recibido debe ser un número entero.";
                    return;
                }
                var kind = (await TechCatalog.ProductAsync(_app, line.Line.Sku))?.SerialKind ?? MINV.Domain.Catalog.SerialKind.Serial;
                missing.Add(new SerialCaptureLineSpec(line.Line.Sku, line.Line.Name, kind, (int)(line.Line.Quantity - received), SerialCaptureMode.Pick,
                    travelling.Select(s => new SerialRow(s, kind, line.Line.Sku, line.Line.Name, SerialNumberStatus.InTransit, _detail.Header.FromBranch,
                        null, null, null, null, null, null)).ToList()));
            }
        }

        async Task<TransferRef> ReceiveAsync(IReadOnlyList<SkuSerials>? lost) =>
            await _app.SendAsync(new ReceiveTransferCommand(_detail.Header.Id, inputs.Select(i =>
                lost?.FirstOrDefault(s => s.Sku.Equals(i.Sku, StringComparison.OrdinalIgnoreCase)) is { } serials ? i with { MissingSerials = serials.Serials } : i)
                .ToList()));

        TransferRef? result = null;
        if (missing.Count > 0)
        {
            await SerialsDialog.AskAsync(_app, "Series que no llegaron",
                "Marque las unidades que faltan (quedan como faltante en tránsito); las demás entran al stock del destino.", "Confirmar recepción", missing,
                async lost =>
                {
                    result = await ReceiveAsync(lost);
                    return true;
                });
        }
        else
        {
            try
            {
                result = await ReceiveAsync(null);
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                Error = AppServices.Describe(ex);
            }
        }
        if (result is not null)
        {
            _app.Notify.Success("Transferencia recibida", result.Message);
            await _owner.AfterChangeAsync(result);
        }
    }
}
