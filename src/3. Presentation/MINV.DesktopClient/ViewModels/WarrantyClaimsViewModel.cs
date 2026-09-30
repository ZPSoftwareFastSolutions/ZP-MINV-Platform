using System.ComponentModel;
using System.Windows.Data;
using MINV.Application.Catalog;
using MINV.Application.Tech;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Service;

namespace MINV.DesktopClient.ViewModels;

/// <summary>V4.2 · Fila de la lista de casos RMA.</summary>
public sealed class WarrantyClaimItem(WarrantyClaimRow row)
{
    public WarrantyClaimRow Row { get; } = row;

    public string Number => Row.Number;

    public string Serial => Row.Serial;

    public string Product => $"{Row.Product} · {Row.Sku}";

    public string Customer => Row.Customer;

    public string Issue => Row.Issue;

    public WarrantyClaimStatus Status => Row.Status;

    public string StatusText => TechText.ClaimStatus(Row.Status);

    public string StatusBrush => TechText.ClaimBrush(Row.Status);

    public string StatusSoftBrush => StatusBrush + "Soft";

    public string CoverageText => Row.IsInWarranty ? "En garantía" : "Con cargo";

    public string CoverageBrush => Row.IsInWarranty ? "Success" : "Warning";

    public string CoverageSoftBrush => CoverageBrush + "Soft";

    public int DaysOpen => Row.DaysOpen;

    public string DaysText => Row.ClosedAt is { } closed ? $"Cerrado {Fmt.DateTime(closed)}" : Row.DaysOpen == 1 ? "1 día abierto" : $"{Row.DaysOpen} días abierto";

    public DateTimeOffset ReceivedAt => Row.ReceivedAt;

    public string ReceivedText => Fmt.DateTime(Row.ReceivedAt);

    public bool IsOpen => Row.Status != WarrantyClaimStatus.Delivered;
}

/// <summary>V4.2 · Hecho de la bitácora de un caso RMA.</summary>
public sealed class ClaimEventItem(WarrantyClaimEventView e)
{
    public string Title => TechText.ClaimAction(e.Action) + (e.Action is WarrantyClaimAction.StatusChanged or WarrantyClaimAction.Closed
        ? " · " + TechText.ClaimStatus(e.Status).ToLower(Fmt.Culture)
        : string.Empty);

    public string Brush => TechText.ClaimBrush(e.Status);

    public string Note => e.Note ?? string.Empty;

    public bool HasNote => !string.IsNullOrWhiteSpace(e.Note);

    public string Detail => $"{Fmt.DateTime(e.OccurredAt)} · {e.User}";
}

/// <summary>V4.2 · Acción disponible sobre un caso (según la tabla de transiciones del dominio).</summary>
public sealed record ClaimActionItem(WarrantyClaimStatus Next, string Label, string Glyph, bool IsPrimary, bool IsDanger)
{
    public bool IsNormal => !IsPrimary && !IsDanger;
}

/// <summary>
/// V4.2 · Garantías y RMA: lista por estado con indicadores, detalle con bitácora, acciones según el estado (diagnóstico,
/// enviar al proveedor, reparado, reemplazar con otra serie, rechazar, entregar) y apertura de un caso desde una serie
/// vendida. Las transiciones y la garantía vigente las decide el dominio (reglas T-04 y T-05).
/// </summary>
public sealed class WarrantyClaimsViewModel : PageViewModel
{
    private List<WarrantyClaimItem> _items = [];
    private string _filter = "open";
    private string _search = string.Empty;
    private Choice<string?> _branch;
    private Choice<bool?> _coverage;
    private WarrantyClaimItem? _selected;
    private WarrantyClaimDetail? _detail;
    private OpenClaimRequest? _pending;
    private string _summary = string.Empty;

    public WarrantyClaimsViewModel(AppServices app)
        : base(app, "garantias", "Garantías y RMA", "Recibir, diagnosticar, reparar o reemplazar y entregar equipos en garantía", Glyphs.Shield)
    {
        Branches.ReplaceAll([new Choice<string?>("Todas las sucursales", null)]);
        _branch = Branches[0];
        Coverages = [new("En garantía y con cargo", null), new("En garantía", true), new("Con cargo (fuera de garantía)", false)];
        _coverage = Coverages[0];
        Export = new RelayCommand(() => App.ExportCsv(App.CsvName("garantias"), "Casos de garantía", ExportTable()), () => _items.Count > 0);
        ClearFilters = new RelayCommand(() =>
        {
            _search = string.Empty;
            _branch = Branches[0];
            _coverage = Coverages[0];
            _filter = "all";
            foreach (var c in Filters)
            {
                c.IsSelected = (string?)c.Value == "all";
            }
            OnPropertiesChanged(nameof(Search), nameof(Branch), nameof(Coverage));
            ApplyFilter();
        }, () => HasFilters);
        Rows = CollectionViewSource.GetDefaultView(_items);
        SelectFilter = new RelayCommand<FilterChip>(chip =>
        {
            _filter = chip.Value as string ?? "all";
            foreach (var c in Filters)
            {
                c.IsSelected = ReferenceEquals(c, chip);
            }
            ApplyFilter();
        });
        OpenNew = new AsyncRelayCommand(() => OpenClaimAsync(null, null), () => CanOpen);
        RunAction = new AsyncRelayCommand<ClaimActionItem>(RunActionAsync, _ => CanManage && _detail is not null && _detail.Claim.Number == _selected?.Number);
        AddNote = new AsyncRelayCommand(AddNoteAsync, () => App.Session.Can(PermissionCodes.ServiceOpen) && _selected?.IsOpen == true);
        OpenSerial = new RelayCommand(() => App.Navigator.Navigate("series", _selected!.Serial), () => _selected is not null);
    }

    public ICollectionView Rows { get; private set; }

    public BulkObservableCollection<FilterChip> Filters { get; } = [];

    public BulkObservableCollection<ClaimEventItem> Events { get; } = [];

    public BulkObservableCollection<ClaimActionItem> Actions { get; } = [];

    public bool CanOpen => App.Session.Can(PermissionCodes.ServiceOpen);

    public bool CanManage => App.Session.Can(PermissionCodes.ServiceManage);

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public bool IsEmpty => HasLoaded && Rows.IsEmpty;

    public string EmptyTitle => _items.Count == 0 ? "Todavía no hay casos RMA" : "No hay casos con ese filtro";

    /// <summary>V7 · Búsqueda por número, serie, producto, cliente o falla.</summary>
    public string Search { get => _search; set { if (Set(ref _search, value ?? string.Empty)) { ApplyFilter(); } } }

    /// <summary>V7 · Sucursal del caso y cobertura (en garantía o con cargo) en listas desplegables.</summary>
    public BulkObservableCollection<Choice<string?>> Branches { get; } = [];

    public Choice<string?> Branch { get => _branch; set { if (Set(ref _branch, value ?? Branches[0])) { ApplyFilter(); } } }

    public IReadOnlyList<Choice<bool?>> Coverages { get; }

    public Choice<bool?> Coverage { get => _coverage; set { if (Set(ref _coverage, value ?? Coverages[0])) { ApplyFilter(); } } }

    /// <summary>Hay algo distinto de lo de siempre (los casos abiertos, sin búsqueda).</summary>
    public bool HasFilters => _search.Trim().Length > 0 || _branch.Value is not null || _coverage.Value is not null || _filter != "open";

    public RelayCommand Export { get; }

    public RelayCommand ClearFilters { get; }

    /// <summary>V7 · Lo que se exporta: los casos visibles con sus filtros.</summary>
    public CsvTable ExportTable() => CsvTable.Of(
        ["Número", "Sucursal", "Estado", "Cobertura", "Serie o IMEI", "SKU", "Producto", "Cliente", "Falla", "Recibido", "Cerrado", "Días abierto", "Proveedor",
            "Resolución", "Serie de reemplazo"],
        Rows.Cast<WarrantyClaimItem>(),
        c => [c.Number, c.Row.BranchCode, c.StatusText, c.CoverageText, c.Serial, c.Row.Sku, c.Row.Product, c.Customer, c.Issue, c.Row.ReceivedAt, c.Row.ClosedAt,
            c.DaysOpen, c.Row.Supplier, c.Row.Resolution, c.Row.ReplacementSerial]);

    public KpiCard OpenKpi { get; } = new("Casos abiertos", Glyphs.Shield, "Info", "InfoSoft");

    public KpiCard WorkshopKpi { get; } = new("En diagnóstico o en el proveedor", Glyphs.Wrench, "Warning", "WarningSoft");

    public KpiCard ChargeableKpi { get; } = new("Fuera de garantía (con cargo)", Glyphs.Money, "Danger", "DangerSoft");

    public KpiCard ClosedKpi { get; } = new("Entregados", Glyphs.CheckCircle, "Success", "SuccessSoft");

    public WarrantyClaimItem? Selected
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

    public WarrantyClaimDetail? Detail { get => _detail; private set => Set(ref _detail, value); }

    public string WarrantyText => _detail?.Warranty is { } w
        ? w.WarrantyUntil is { } until ? (w.InWarranty ? $"En garantía hasta el {Fmt.Date(until)}" : $"Garantía vencida el {Fmt.Date(until)}") : "Sin garantía"
        : string.Empty;

    // V7 · «Sin garantía» (producto sin meses de garantía) ya no sale en rojo como si hubiera vencido
    public string WarrantyBrush => _detail?.Warranty is { } w ? w.InWarranty ? "Success" : w.WarrantyUntil is null ? "Info" : "Danger" : "Info";

    public string WarrantySoftBrush => WarrantyBrush + "Soft";

    public string SaleText => _detail is { } d
        ? string.Join(" · ", new[] { d.Warranty.SoldOn is { } s ? "vendido el " + Fmt.Date(s) : null, d.InvoiceNumber ?? d.Warranty.InvoiceNumber,
            d.Warranty.Customer }.Where(t => !string.IsNullOrWhiteSpace(t)))
        : string.Empty;

    public string ResolutionText => _detail?.Claim is { } c
        ? string.Join(" · ", new[] { c.Resolution, c.Supplier is { } s ? "proveedor " + s : null, c.ReplacementSerial is { } r ? "reemplazo " + r : null }
            .Where(t => !string.IsNullOrWhiteSpace(t)))
        : string.Empty;

    public bool HasResolution => ResolutionText.Length > 0;

    public bool HasActions => Actions.Count > 0;

    public RelayCommand<FilterChip> SelectFilter { get; }

    public AsyncRelayCommand OpenNew { get; }

    public AsyncRelayCommand<ClaimActionItem> RunAction { get; }

    public AsyncRelayCommand AddNote { get; }

    public RelayCommand OpenSerial { get; }

    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is OpenClaimRequest request)
        {
            _pending = request;
            if (HasLoaded)
            {
                _ = OpenPendingAsync();
            }
        }
    }

    protected override async Task LoadCoreAsync(bool force)
    {
        var rows = await App.SendAsync(new GetWarrantyClaimsQuery());
        var selected = _selected?.Number;
        _items = rows.Select(r => new WarrantyClaimItem(r)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = Matches;
        Rows.SortDescriptions.Add(new SortDescription(nameof(WarrantyClaimItem.ReceivedAt), ListSortDirection.Descending));
        OnPropertyChanged(nameof(Rows));
        var open = rows.Where(r => r.Status != WarrantyClaimStatus.Delivered).ToList();
        var chips = new List<FilterChip> { new("Abiertos", "open", open.Count), new("Todos", "all", rows.Count) };
        chips.AddRange(Enum.GetValues<WarrantyClaimStatus>().Select(s => new FilterChip(TechText.ClaimStatus(s), s.ToString(), rows.Count(r => r.Status == s),
            TechText.ClaimBrush(s))));
        Filters.ReplaceAll(chips);
        (Filters.FirstOrDefault(c => (string?)c.Value == _filter) ?? Filters[0]).IsSelected = true;
        Branches.ReplaceAll(FilterChoices.Of("Todas las sucursales", rows.Select(r => r.BranchCode)));
        _branch = FilterChoices.Keep(Branches, _branch);
        OnPropertyChanged(nameof(Branch));
        OpenKpi.Value = open.Count.ToString("N0", Fmt.Culture);
        OpenKpi.Detail = open.Count == 0 ? "Ningún equipo en garantía" : $"el más antiguo lleva {open.Max(o => o.DaysOpen)} días";
        var workshop = open.Count(r => r.Status is WarrantyClaimStatus.Diagnosing or WarrantyClaimStatus.SentToSupplier);
        WorkshopKpi.Value = workshop.ToString("N0", Fmt.Culture);
        WorkshopKpi.Detail = $"{open.Count(r => r.Status == WarrantyClaimStatus.SentToSupplier)} en el proveedor";
        ChargeableKpi.Value = rows.Count(r => !r.IsInWarranty).ToString("N0", Fmt.Culture);
        ChargeableKpi.Detail = "Reparaciones con cargo";
        ClosedKpi.Value = rows.Count(r => r.Status == WarrantyClaimStatus.Delivered).ToString("N0", Fmt.Culture);
        ClosedKpi.Detail = $"{rows.Count(r => r.ReplacementSerial is not null)} con reemplazo";
        ApplyFilter();
        _selected = selected is null ? null : _items.FirstOrDefault(i => i.Number == selected);
        OnPropertiesChanged(nameof(Selected), nameof(HasSelection));
        await LoadDetailAsync();
        if (_pending is not null)
        {
            // El formulario se abre al terminar la carga (la pantalla no queda «cargando» mientras se completa)
            _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(async () => await OpenPendingAsync(),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private bool Matches(object o)
    {
        if (o is not WarrantyClaimItem c)
        {
            return false;
        }
        var statusOk = _filter switch
        {
            "all" => true,
            "open" => c.IsOpen,
            var status => c.Status.ToString() == status,
        };
        if (!statusOk || (_branch.Value is { } branch && c.Row.BranchCode != branch) || (_coverage.Value is { } covered && c.Row.IsInWarranty != covered))
        {
            return false;
        }
        var q = _search.Trim();
        return q.Length == 0 || c.Number.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Serial.Contains(q, StringComparison.OrdinalIgnoreCase)
               || FilterChoices.Contains(c.Product, q) || FilterChoices.Contains(c.Customer, q) || FilterChoices.Contains(c.Issue, q);
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        var visible = Rows.Cast<object>().Count();
        Summary = visible == _items.Count ? (_items.Count == 1 ? "1 caso" : $"{_items.Count} casos") : $"{visible} de {_items.Count} casos";
        OnPropertiesChanged(nameof(IsEmpty), nameof(EmptyTitle), nameof(HasFilters));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async Task OpenPendingAsync()
    {
        if (_pending is { } request)
        {
            _pending = null;
            await OpenClaimAsync(request.Serial, request.Sku);
        }
    }

    private async Task LoadDetailAsync()
    {
        // V7 · El detalle de otro caso se quita ANTES de leer el nuevo: mientras llegaba, las acciones se aplicaban al caso anterior
        var item = _selected;
        if (item is null || _detail?.Claim.Number != item.Number)
        {
            Detail = null;
            Events.ReplaceAll([]);
            Actions.ReplaceAll([]);
            RaiseDetail();
        }
        if (item is null)
        {
            return;
        }
        try
        {
            var detail = await App.SendAsync(new GetWarrantyClaimQuery(item.Number));
            if (!ReferenceEquals(item, _selected))
            {
                return;
            }
            Detail = detail;
            Events.ReplaceAll(detail.Events.OrderByDescending(e => e.OccurredAt).Select(e => new ClaimEventItem(e)));
            Actions.ReplaceAll(CanManage
                ? detail.NextStatuses.Select(n => new ClaimActionItem(n, TechText.ClaimAction(n), TechText.ClaimGlyph(n),
                    n is WarrantyClaimStatus.Repaired or WarrantyClaimStatus.Replaced or WarrantyClaimStatus.Delivered or WarrantyClaimStatus.Diagnosing,
                    n == WarrantyClaimStatus.Rejected))
                : []);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo leer el caso", AppServices.Describe(ex));
        }
        RaiseDetail();
    }

    private void RaiseDetail()
    {
        OnPropertiesChanged(nameof(WarrantyText), nameof(WarrantyBrush), nameof(WarrantySoftBrush), nameof(SaleText), nameof(ResolutionText),
            nameof(HasResolution), nameof(HasActions));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Abre un caso para una serie vendida (desde «Series e IMEI» o escaneando la serie en el formulario).</summary>
    public async Task OpenClaimAsync(string? serial, string? sku)
    {
        var dialog = new OpenClaimDialog(App, serial, sku);
        if (serial is not null)
        {
            await dialog.LookupAsync();
        }
        if (await App.Dialogs.ShowAsync(dialog) && dialog.Result is { } row)
        {
            App.Notify.Success($"Caso {row.Number} abierto", $"{row.Product} · {row.Serial} · {(row.IsInWarranty ? "en garantía" : "reparación con cargo")}");
            App.Data.Invalidate();
            _filter = "open";
            await LoadAsync(force: true);
            Selected = _items.FirstOrDefault(i => i.Number == row.Number);
        }
    }

    private async Task RunActionAsync(ClaimActionItem action)
    {
        if (_detail is not { } detail || detail.Claim.Number != _selected?.Number)
        {
            return;
        }
        var claim = detail.Claim;
        FormDialog dialog = action.Next == WarrantyClaimStatus.Replaced
            ? await ReplaceClaimDialog.CreateAsync(App, claim)
            : await MoveClaimDialog.CreateAsync(App, claim, action);
        if (await App.Dialogs.ShowAsync(dialog))
        {
            App.Notify.Success($"Caso {claim.Number}", action.Label);
            App.Data.Invalidate();
            await LoadAsync(force: true);
        }
    }

    private async Task AddNoteAsync()
    {
        var claim = _selected!;
        var note = await App.Dialogs.PromptAsync($"Nota en {claim.Number}", "La nota queda en la bitácora del caso con su fecha y usuario.", "Nota",
            ["Se contactó al cliente", "Se pidió el repuesto al proveedor", "Diagnóstico: falla de fábrica", "Equipo listo para retirar"], "Agregar nota",
            glyph: Glyphs.Clipboard);
        if (note is null)
        {
            return;
        }
        if (note.Length == 0)
        {
            // V7 · Antes, aceptar sin texto no hacía nada y no decía por qué
            App.Notify.Warning("La nota está vacía", "Elija una de la lista o escriba qué pasó con el equipo.");
            return;
        }
        if (await RunAsync(() => App.SendAsync(new AddWarrantyClaimNoteCommand(claim.Number, note)), "No se pudo agregar la nota"))
        {
            App.Notify.Success("Nota agregada", $"Quedó en la bitácora de {claim.Number}.");
            await LoadDetailAsync();
        }
    }
}

/// <summary>
/// V4.2 · Abrir un caso RMA: la serie (escaneada o escrita) muestra al instante el producto, la venta, el cliente y la
/// garantía derivada; fuera de garantía solo se abre como reparación con cargo, marcándolo explícitamente.
/// </summary>
public sealed class OpenClaimDialog : FormDialog, IScannerTarget
{
    private readonly AppServices _app;
    private string _serial;
    private readonly string? _sku;
    private string _issue = string.Empty;
    private bool _chargeable;
    private string _customerCode = string.Empty;
    private WarrantyStatusView? _status;
    private string? _lookupError;

    public OpenClaimDialog(AppServices app, string? serial, string? sku)
        : base("Abrir un caso de garantía (RMA)", "Abrir caso", Glyphs.Shield, width: 600)
    {
        _app = app;
        _serial = serial ?? string.Empty;
        _sku = sku;
        Lookup = new AsyncRelayCommand(LookupAsync, () => _serial.Trim().Length > 0);
    }

    public override string? Subtitle => "El equipo queda «en garantía (RMA)» en la sucursal activa, sin entrar al stock vendible.";

    public string Serial
    {
        get => _serial;
        set
        {
            if (Set(ref _serial, value ?? string.Empty))
            {
                Status = null;
            }
        }
    }

    public WarrantyStatusView? Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value))
            {
                OnPropertiesChanged(nameof(HasStatus), nameof(StatusTitle), nameof(StatusDetail), nameof(StatusBrush), nameof(StatusSoftBrush), nameof(OutOfWarranty),
                    nameof(NeedsCustomer));
            }
        }
    }

    public bool HasStatus => _status is not null;

    public string StatusTitle => _status is { } s ? $"{s.Product} · {s.Sku}" : string.Empty;

    public string StatusDetail => _status is { } s
        ? string.Join(" · ", new[]
        {
            TechText.SerialStatus(s.Status), s.SoldOn is { } d ? "vendido el " + Fmt.Date(d) : null, s.InvoiceNumber, s.Customer,
            s.WarrantyUntil is { } u ? (s.InWarranty ? "en garantía hasta el " : "garantía vencida el ") + Fmt.Date(u) : "sin garantía",
            s.OpenClaim is { } open ? "ya tiene el caso " + open : null,
        }.Where(t => !string.IsNullOrWhiteSpace(t)))
        : string.Empty;

    public string StatusBrush => _status is { InWarranty: true, OpenClaim: null } ? "Success" : "Warning";

    public string StatusSoftBrush => StatusBrush + "Soft";

    public bool OutOfWarranty => _status is { InWarranty: false };

    /// <summary>La venta es de otra sucursal (sin cliente visible): se indica el cliente.</summary>
    public bool NeedsCustomer => _status is { CustomerCode: null };

    public string? LookupError { get => _lookupError; private set => Set(ref _lookupError, value); }

    public string Issue { get => _issue; set => Set(ref _issue, value ?? string.Empty); }

    public IReadOnlyList<string> Issues { get; } =
        ["No enciende", "Se reinicia solo / pantallazos azules", "Artefactos en pantalla", "No carga la batería", "Ruido o temperatura excesiva", "Joystick con drift"];

    public bool ChargeableRepair { get => _chargeable; set => Set(ref _chargeable, value); }

    public string CustomerCode { get => _customerCode; set => Set(ref _customerCode, value ?? string.Empty); }

    public AsyncRelayCommand Lookup { get; }

    public WarrantyClaimRow? Result { get; private set; }

    public bool OnScanned(string code)
    {
        Serial = code;
        _ = LookupAsync();
        return true;
    }

    public async Task LookupAsync()
    {
        LookupError = null;
        try
        {
            Status = await _app.SendAsync(new GetWarrantyStatusQuery(_serial.Trim(), _sku));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Status = null;
            LookupError = AppServices.Describe(ex);
        }
    }

    protected override bool CanConfirm() => _serial.Trim().Length > 0 && _issue.Trim().Length >= 5;

    protected override async Task<bool> SubmitAsync()
    {
        if (_status is null)
        {
            await LookupAsync();
        }
        if (OutOfWarranty && !_chargeable)
        {
            Error = "La unidad está fuera de garantía: marque «Reparación con cargo» para abrir el caso.";
            return false;
        }
        Result = await _app.SendAsync(new OpenWarrantyClaimCommand(_serial.Trim(), _issue.Trim(), _chargeable,
            string.IsNullOrWhiteSpace(_customerCode) ? null : _customerCode.Trim().ToUpperInvariant(), _status?.Sku ?? _sku));
        return true;
    }
}

/// <summary>V4.2 · Avanzar un caso: resolución (reparado, rechazado), proveedor (enviar al proveedor) y nota.</summary>
public sealed class MoveClaimDialog : FormDialog
{
    private static readonly Choice<string?> PreferredSupplier = new("El proveedor preferido del producto", null);
    private readonly AppServices _app;
    private readonly WarrantyClaimRow _claim;
    private string _resolution = string.Empty;
    private string _note = string.Empty;
    private Choice<string?> _supplier = PreferredSupplier;

    private MoveClaimDialog(AppServices app, WarrantyClaimRow claim, ClaimActionItem action, IReadOnlyList<Choice<string?>> suppliers)
        : base($"{action.Label} · {claim.Number}", action.Label, action.Glyph, isDanger: action.IsDanger)
    {
        _app = app;
        _claim = claim;
        Next = action.Next;
        Suppliers = [PreferredSupplier, .. suppliers];
        Resolutions = Next switch
        {
            WarrantyClaimStatus.Repaired => ["Se reemplazó el componente dañado", "Actualización de firmware y pruebas OK", "Limpieza y cambio de pasta térmica"],
            WarrantyClaimStatus.Rejected => ["Daño por mal uso (golpe o líquido)", "Sello de garantía roto", "No se reprodujo la falla"],
            _ => [],
        };
    }

    public static async Task<MoveClaimDialog> CreateAsync(AppServices app, WarrantyClaimRow claim, ClaimActionItem action)
    {
        IReadOnlyList<Choice<string?>> suppliers = [];
        if (action.Next == WarrantyClaimStatus.SentToSupplier)
        {
            try
            {
                suppliers = (await app.SendAsync(new GetCatalogOptionsQuery())).Suppliers.Select(s => new Choice<string?>(s.Name, s.Code)).ToList();
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                System.Diagnostics.Trace.TraceWarning("M-INV · proveedores: {0}", ex.Message);
            }
        }
        return new MoveClaimDialog(app, claim, action, suppliers);
    }

    public override string? Subtitle => $"{_claim.Product} · {_claim.Serial} · {_claim.Customer}";

    public WarrantyClaimStatus Next { get; }

    public bool NeedsResolution => Next is WarrantyClaimStatus.Repaired or WarrantyClaimStatus.Rejected;

    public bool NeedsSupplier => Next == WarrantyClaimStatus.SentToSupplier;

    public string Help => Next switch
    {
        WarrantyClaimStatus.Diagnosing => "El técnico revisa el equipo. Después: repararlo, enviarlo al proveedor, reemplazarlo o rechazar la garantía.",
        WarrantyClaimStatus.SentToSupplier => "El equipo sale al proveedor o al servicio técnico autorizado (sigue en RMA).",
        WarrantyClaimStatus.Repaired => "El equipo quedó reparado: al entregarlo vuelve a su dueño.",
        WarrantyClaimStatus.Rejected => "La garantía no cubre la falla: el equipo se devuelve al cliente sin reparar (o con reparación con cargo).",
        WarrantyClaimStatus.Delivered => "Cierra el caso: el cliente retira su equipo (reparado o rechazado) o ya recibió el reemplazo.",
        _ => string.Empty,
    };

    public IReadOnlyList<string> Resolutions { get; }

    public IReadOnlyList<Choice<string?>> Suppliers { get; }

    public Choice<string?> Supplier { get => _supplier; set => Set(ref _supplier, value ?? PreferredSupplier); }

    public string Resolution { get => _resolution; set => Set(ref _resolution, value ?? string.Empty); }

    public string Note { get => _note; set => Set(ref _note, value ?? string.Empty); }

    protected override bool CanConfirm() => !NeedsResolution || _resolution.Trim().Length > 0;

    protected override async Task<bool> SubmitAsync()
    {
        await _app.SendAsync(new MoveWarrantyClaimCommand(_claim.Number, Next, NeedsResolution ? _resolution.Trim() : null, _supplier.Value,
            string.IsNullOrWhiteSpace(_note) ? null : _note.Trim()));
        return true;
    }
}

/// <summary>
/// V4.2 · Reemplazar con otra unidad: se elige (o escanea) una unidad disponible del mismo producto en la sucursal del
/// caso; sale del stock con su movimiento de reposición y su asiento, queda vendida al cliente y el caso pasa a
/// «reemplazado» (la defectuosa sigue en RMA para devolverla al proveedor).
/// </summary>
public sealed class ReplaceClaimDialog : FormDialog, IScannerTarget
{
    private readonly AppServices _app;
    private readonly WarrantyClaimRow _claim;
    private string _resolution = "Reemplazo por falla de fábrica";

    private ReplaceClaimDialog(AppServices app, WarrantyClaimRow claim, IReadOnlyList<SerialRow> available, Domain.Catalog.SerialKind kind)
        : base($"Reemplazar con otra unidad · {claim.Number}", "Entregar reemplazo", Glyphs.Swap, width: 640)
    {
        _app = app;
        _claim = claim;
        Line = new SerialCaptureLine(claim.Sku, claim.Product, kind, 1, SerialCaptureMode.Pick,
            () => System.Windows.Input.CommandManager.InvalidateRequerySuggested(), available.Where(a => a.Serial != claim.Serial).ToList());
    }

    public static async Task<ReplaceClaimDialog> CreateAsync(AppServices app, WarrantyClaimRow claim)
    {
        IReadOnlyList<SerialRow> available = [];
        try
        {
            available = await app.SendAsync(new GetAvailableSerialsQuery(claim.Sku));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            System.Diagnostics.Trace.TraceWarning("M-INV · series para el reemplazo: {0}", ex.Message);
        }
        var kind = (await TechCatalog.ProductAsync(app, claim.Sku))?.SerialKind ?? Domain.Catalog.SerialKind.Serial;
        return new ReplaceClaimDialog(app, claim, available, kind);
    }

    public override string? Subtitle =>
        $"{_claim.Customer} recibe una unidad nueva de {_claim.Product}: sale del stock (reposición por garantía, asiento al costo de garantías).";

    public SerialCaptureLine Line { get; }

    public string Resolution { get => _resolution; set => Set(ref _resolution, value ?? string.Empty); }

    public bool OnScanned(string code) => Line.Accept(code);

    protected override bool CanConfirm() => Line.IsComplete && _resolution.Trim().Length > 0;

    protected override async Task<bool> SubmitAsync()
    {
        await _app.SendAsync(new IssueWarrantyReplacementCommand(_claim.Number, Line.Serials[0], _resolution.Trim()));
        return true;
    }
}
