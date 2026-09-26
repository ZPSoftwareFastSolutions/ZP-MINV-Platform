using System.Globalization;
using System.Windows.Threading;
using MINV.Application.Billing;
using MINV.Application.Sales;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Billing;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Alerta del tablero con su cuenta regresiva (se actualiza sola mientras la pantalla está abierta).</summary>
public sealed class SiatAlertItem(SiatAlert alert) : ObservableObject
{
    private string? _countdown;

    public SiatAlert Alert { get; } = alert;

    public string Title => Alert.Title;

    public string Detail => Alert.Detail;

    public string Brush => Alert.Severity switch
    {
        SiatAlertSeverity.Danger => "Danger",
        SiatAlertSeverity.Warning => "Warning",
        _ => "Info",
    };

    public string SoftBrush => Brush + "Soft";

    public string Glyph => Alert.Severity switch
    {
        SiatAlertSeverity.Danger => Glyphs.Error,
        SiatAlertSeverity.Warning => Glyphs.Warning,
        _ => Glyphs.Info,
    };

    public string? Countdown { get => _countdown; private set => Set(ref _countdown, value); }

    public bool HasCountdown => Alert.Deadline is not null;

    public void Update(DateTimeOffset now) => Countdown = Alert.Deadline is { } deadline
        ? $"{FiscalText.Countdown(deadline, now)} · {deadline.ToLocalTime():dd/MM HH:mm}"
        : null;
}

/// <summary>Punto de venta del SIN (grilla del tablero).</summary>
public sealed class SiatPointItem(SiatPointOfSaleStatus p, DateTimeOffset now)
{
    public SiatPointOfSaleStatus Point { get; } = p;

    public string BranchText => $"{Point.BranchCode} · {Point.BranchName}";

    public string SiatBranchText => $"Sucursal {Point.SiatBranchCode} del Padrón";

    public string PointText => $"{Point.Code} · {Point.Name}";

    public string RegisterText => Point.RegisterCode ?? "Sin caja";

    public string ModeText => Point.IsClosed ? "Cerrado" : FiscalText.Mode(Point.Mode);

    public string ModeBrush => Point.IsClosed ? "StatusInactive" : FiscalText.ModeBrush(Point.Mode);

    public string ModeSoftBrush => ModeBrush + "Soft";

    public string ModeSinceText => Point.Mode == SiatConnectionMode.Online ? string.Empty : "desde " + Fmt.DateTime(Point.ModeSince);

    public string CuisText => Point.CuisValidUntil is { } until ? "CUIS hasta " + Expiry(until, "dd/MM/yyyy") : "Sin CUIS";

    public string CuisBrush => Point.CuisValidUntil is { } until && until > now ? until.AddDays(-5) <= now ? "Warning" : "Success" : "Danger";

    public string CufdText => Point.CufdValidUntil is { } until ? "CUFD hasta " + Expiry(until, "dd/MM HH:mm") : "Sin CUFD";

    public string CufdBrush => Point.CufdValidUntil is { } until && until > now ? until.AddHours(-2) <= now ? "Warning" : "Success" : "Danger";

    public string PendingText => Point.PendingDocuments + Point.OfflineDocuments == 0
        ? "—"
        : $"{Point.PendingDocuments} por enviar";

    public string OfflineText => Point.OfflineDocuments == 0 ? string.Empty : $"{Point.OfflineDocuments} fuera de línea";

    public string EventText => Point.OpenEvent is { } e
        ? e.Kind == SignificantEventKind.ManualCafc ? "Contingencia manual" : "Fuera de línea"
        : "—";

    public string EventStatusText => Point.OpenEvent is { } e ? FiscalText.EventStatus(e.Status) : string.Empty;

    public string? LastError => Point.LastError;

    public bool IsOnline => !Point.IsClosed && Point.Mode == SiatConnectionMode.Online;

    public bool IsOffline => !Point.IsClosed && Point.Mode is SiatConnectionMode.Offline or SiatConnectionMode.Recovering;

    public bool IsManual => !Point.IsClosed && Point.Mode == SiatConnectionMode.ManualContingency;

    private static string Expiry(DateTimeOffset until, string format) => until.ToLocalTime().ToString(format, CultureInfo.InvariantCulture);
}

/// <summary>Evento significativo (pestaña Eventos).</summary>
public sealed record SiatEventItem(SignificantEventRow Row)
{
    public string KindText => Row.Kind == SignificantEventKind.ManualCafc ? "Contingencia manual (CAFC)" : "Fuera de línea";

    public string PlaceText => $"{Row.BranchCode} · punto {Row.PointOfSaleCode}";

    public string Description => $"{Row.EventCode} · {Fmt.SentenceCase(Row.Description)}";

    public string PeriodText => $"{Row.StartedAt:dd/MM/yyyy HH:mm} → {(Row.EndedAt is { } end ? end.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "abierto")}";

    public string StatusText => FiscalText.EventStatus(Row.Status);

    public string StatusBrush => FiscalText.EventBrush(Row.Status);

    public string StatusSoftBrush => StatusBrush + "Soft";

    public string DocumentsText => Row.Documents == 1 ? "1 documento" : $"{Row.Documents} documentos";

    public string DeadlineText => Row.TranscriptionDeadline is { } t
        ? $"Transcribir hasta {t:dd/MM HH:mm}"
        : Row.RegistrationDeadline is { } r ? $"Registrar hasta {r:dd/MM HH:mm}" : string.Empty;

    public string ReceptionText => Row.ReceptionCode ?? "—";
}

/// <summary>Paquete de contingencia (pestaña Paquetes).</summary>
public sealed record SiatPackageItem(FiscalPackageRow Row)
{
    public string PlaceText => $"{Row.BranchCode} · punto {Row.PointOfSaleCode}";

    public string SectorText => Row.DocumentSector == SiatCodes.SectorPurchaseSale ? "Facturas" : $"Sector {Row.DocumentSector}";

    public string DocumentsText => $"{Row.Documents} documentos" + (Row.Cafc is { } c ? $" · CAFC {c}" : string.Empty);

    public string SentText => Fmt.DateTime(Row.SentAt);

    public string ValidatedText => Row.ValidatedAt is { } v ? Fmt.DateTime(v) : "—";

    public string StatusText => FiscalText.PackageStatus(Row.Status) + (Row.LastSiatCode is { } code ? $" ({code})" : string.Empty);

    public string StatusBrush => FiscalText.PackageBrush(Row.Status);

    public string StatusSoftBrush => StatusBrush + "Soft";
}

/// <summary>Talonario CAFC (pestaña CAFC).</summary>
public sealed record SiatCafcItem(ContingencyCodeRow Row)
{
    public string SectorText => Row.DocumentSector == SiatCodes.SectorPurchaseSale ? "Factura compra venta" : "Nota crédito-débito";

    public string RangeText => $"N° {Row.NumberFrom} a {Row.NumberTo}";

    public string UsedText => $"{Row.Used} de {Row.NumberTo - Row.NumberFrom + 1} usados";

    public string ValidText => Row.ValidUntil is { } until ? Fmt.Date(until) : "Sin vencimiento";

    public string StatusText => Row.IsActive ? "Activo" : "Inactivo";

    public string StatusBrush => Row.IsActive ? "Success" : "StatusInactive";

    public string StatusSoftBrush => StatusBrush + "Soft";
}

/// <summary>Llamada al SIN (bitácora técnica, sin token).</summary>
public sealed record SiatCallItem(SiatServiceCallRow Row)
{
    public string WhenText => Row.OccurredAt.ToLocalTime().ToString("dd/MM HH:mm:ss", CultureInfo.InvariantCulture);

    public string OperationText => $"{Row.Operation}" + (Row.PointOfSaleCode is { } p ? $" · punto {p}" : string.Empty);

    public string ResultText => Row.Succeeded
        ? $"✔ {Row.SiatCode?.ToString(CultureInfo.InvariantCulture) ?? "OK"}"
        : $"✖ {Row.Error ?? Row.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "sin respuesta"}";

    public string ResultBrush => Row.Succeeded ? "Success" : "Danger";

    public string DurationText => $"{Row.DurationMs} ms";
}

/// <summary>
/// V4.1 · «Estado SIAT»: tablero de la facturación (ambiente, modo de cada punto de venta, documentos de hoy, pendientes,
/// eventos), ALERTAS con cuenta regresiva de los plazos, puntos de venta con sus acciones (CUIS, CUFD, comunicación,
/// fuera de línea, contingencia manual, recuperación, caja, cierre), eventos, paquetes, talonarios CAFC, transcripción de
/// facturas manuales y la bitácora técnica del SIN (administración).
/// </summary>
public sealed class SiatStatusViewModel : PageViewModel
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };
    private SiatStatusView? _status;
    private int _tab;
    private SiatPointItem? _selectedPoint;
    private bool _callsLoaded;
    private IReadOnlyList<SiatCatalogItemView>? _events;
    private IReadOnlyList<SiatCatalogItemView>? _documentTypes;

    public SiatStatusViewModel(AppServices app)
        : base(app, "estado-siat", "Estado SIAT", "Conexión con el SIN, puntos de venta, plazos y contingencias", Glyphs.Pulse)
    {
        _tick.Tick += (_, _) => UpdateCountdowns();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsSelected))
            {
                if (IsSelected)
                {
                    _tick.Start();
                }
                else
                {
                    _tick.Stop();
                }
            }
        };
        Prepare = new AsyncRelayCommand(() => RunGeneralAsync("Preparar SIAT", async () =>
        {
            var r = await App.SendAsync(new PrepareSiatCommand());
            return $"CUIS pedidos {r.CuisRequested} · CUFD pedidos {r.CufdRequested} · documentos enviados {r.DocumentsSent}" +
                   (r.Messages.Count > 0 ? " · " + string.Join(" ", r.Messages.Take(3)) : string.Empty);
        }), () => CanConfigure);
        Sync = new AsyncRelayCommand(() => RunGeneralAsync("Catálogos sincronizados", async () =>
        {
            var r = await App.SendAsync(new SyncSiatCatalogsCommand());
            return $"{r.Catalogs} catálogos · {r.Items} filas" + (r.Errors.Count > 0 ? " · con avisos: " + string.Join(" ", r.Errors.Take(2)) : string.Empty);
        }), () => CanConfigure);
        ProcessNow = new AsyncRelayCommand(() => RunGeneralAsync("Trabajo de facturación ejecutado", async () =>
        {
            var r = await App.SendAsync(new RunSiatWorkCommand());
            return $"{r.Dispatch.Sent} enviados · {r.Dispatch.Valid} válidos · {r.Dispatch.Rejected} rechazados" +
                   (r.Maintenance is { } m ? $" · recuperados {m.Recovered} · paquetes validados {m.PackagesValidated}" : string.Empty);
        }), () => App.Session.Can(PermissionCodes.BillingIssue));
        RegisterPoint = new AsyncRelayCommand(RegisterPointAsync, () => CanConfigure);
        RequestCuis = PointCommand("CUIS solicitado", p => App.SendAsync(new RequestCuisCommand(p.Point.Id)), p => CanConfigure && !p.Point.IsClosed);
        RequestCufd = PointCommand("CUFD solicitado", p => App.SendAsync(new RequestCufdCommand(p.Point.Id)), p => CanConfigure && !p.Point.IsClosed);
        CheckCommunication = PointCommand("Comunicación con el SIN", p => App.SendAsync(new CheckSiatCommunicationCommand(p.Point.Id)), p => !p.Point.IsClosed);
        GoOffline = new AsyncRelayCommand(GoOfflineAsync, () => CanContingency && _selectedPoint?.IsOnline == true);
        StartManual = new AsyncRelayCommand(StartManualAsync, () => CanContingency && _selectedPoint?.IsOnline == true);
        EndContingency = new AsyncRelayCommand(EndContingencyAsync, () => CanContingency && (_selectedPoint?.IsManual == true || _selectedPoint?.Point.Mode == SiatConnectionMode.Offline));
        Recover = PointCommand("Recuperación", p => App.SendAsync(new RecoverPointOfSaleCommand(p.Point.Id)), p => CanContingency && p.IsOffline);
        LinkRegister = new AsyncRelayCommand(LinkRegisterAsync, () => CanConfigure && _selectedPoint is { Point.IsClosed: false });
        ClosePoint = new AsyncRelayCommand(ClosePointAsync, () => CanConfigure && _selectedPoint is { Point.IsClosed: false } && _selectedPoint.Point.Code > 0);
        RegisterCafc = new AsyncRelayCommand(RegisterCafcAsync, () => CanContingency);
        Transcribe = new AsyncRelayCommand(TranscribeAsync, () => CanContingency);
        GoToSettings = new RelayCommand(() => App.Navigator.Navigate("facturacion-siat"), () => CanConfigure);
    }

    public bool CanConfigure => App.Session.Can(PermissionCodes.BillingConfigure);

    public bool CanContingency => App.Session.Can(PermissionCodes.BillingContingency);

    /// <summary>0 = puntos de venta, 1 = eventos, 2 = paquetes, 3 = CAFC, 4 = bitácora del SIN.</summary>
    public int Tab
    {
        get => _tab;
        set
        {
            if (Set(ref _tab, value))
            {
                OnPropertiesChanged(nameof(IsPointsTab), nameof(IsEventsTab), nameof(IsPackagesTab), nameof(IsCafcTab), nameof(IsCallsTab));
                if (value == 4 && !_callsLoaded)
                {
                    _ = LoadCallsAsync();
                }
            }
        }
    }

    public bool IsPointsTab { get => _tab == 0; set { if (value) { Tab = 0; } } }

    public bool IsEventsTab { get => _tab == 1; set { if (value) { Tab = 1; } } }

    public bool IsPackagesTab { get => _tab == 2; set { if (value) { Tab = 2; } } }

    public bool IsCafcTab { get => _tab == 3; set { if (value) { Tab = 3; } } }

    public bool IsCallsTab { get => _tab == 4; set { if (value) { Tab = 4; } } }

    /// <summary>La bitácora técnica del SIN es solo para la administración (configurar la facturación).</summary>
    public bool ShowCalls => CanConfigure;

    public KpiCard EnvironmentKpi { get; } = new("Ambiente", Glyphs.Shield, "Info", "InfoSoft");

    public KpiCard ModeKpi { get; } = new("Puntos de venta", Glyphs.Cloud, "Success", "SuccessSoft");

    public KpiCard TodayKpi { get; } = new("Emitidos hoy", Glyphs.Invoice);

    public KpiCard PendingKpi { get; } = new("Por enviar", Glyphs.Clock, "Warning", "WarningSoft");

    public KpiCard OfflineKpi { get; } = new("Fuera de línea", Glyphs.Offline, "Warning", "WarningSoft");

    public KpiCard EventsKpi { get; } = new("Eventos abiertos", Glyphs.Flag, "Danger", "DangerSoft");

    public bool IsTestEnvironment => _status?.Environment == SiatCodes.EnvironmentTest;

    public string HeaderText => _status is { Configured: true } s
        ? $"{s.BusinessName} · NIT {s.Nit} · {(s.Enabled ? "facturación ACTIVA" : "facturación desactivada")}" +
          (s.ClockSyncedAt is { } clock ? $" · hora del SIN sincronizada {Fmt.Relative(clock, App.Now)}" : string.Empty) +
          (s.LastCatalogSync is { } catalogs ? $" · catálogos {Fmt.Relative(catalogs, App.Now)}" : string.Empty)
        : "La facturación SIAT todavía no está configurada.";

    public BulkObservableCollection<SiatAlertItem> Alerts { get; } = [];

    public bool HasAlerts => Alerts.Count > 0;

    public BulkObservableCollection<SiatPointItem> Points { get; } = [];

    public SiatPointItem? SelectedPoint
    {
        get => _selectedPoint;
        set
        {
            if (Set(ref _selectedPoint, value))
            {
                OnPropertyChanged(nameof(HasPoint));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasPoint => _selectedPoint is not null;

    public bool NoPoints => HasLoaded && Points.Count == 0;

    public BulkObservableCollection<SiatEventItem> Events { get; } = [];

    public BulkObservableCollection<SiatPackageItem> Packages { get; } = [];

    public BulkObservableCollection<SiatCafcItem> Cafcs { get; } = [];

    public BulkObservableCollection<SiatCallItem> Calls { get; } = [];

    public AsyncRelayCommand Prepare { get; }

    public AsyncRelayCommand Sync { get; }

    public AsyncRelayCommand ProcessNow { get; }

    public AsyncRelayCommand RegisterPoint { get; }

    public AsyncRelayCommand RequestCuis { get; }

    public AsyncRelayCommand RequestCufd { get; }

    public AsyncRelayCommand CheckCommunication { get; }

    public AsyncRelayCommand GoOffline { get; }

    public AsyncRelayCommand StartManual { get; }

    public AsyncRelayCommand EndContingency { get; }

    public AsyncRelayCommand Recover { get; }

    public AsyncRelayCommand LinkRegister { get; }

    public AsyncRelayCommand ClosePoint { get; }

    public AsyncRelayCommand RegisterCafc { get; }

    public AsyncRelayCommand Transcribe { get; }

    public RelayCommand GoToSettings { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var status = await App.SendAsync(new GetSiatStatusQuery());
        _status = status;
        var now = App.Now;
        Alerts.ReplaceAll(status.Alerts.Select(a => new SiatAlertItem(a)));
        UpdateCountdowns();
        var selected = _selectedPoint?.Point.Id;
        Points.ReplaceAll(status.Points.Select(p => new SiatPointItem(p, now)));
        _selectedPoint = Points.FirstOrDefault(p => p.Point.Id == selected) ?? Points.FirstOrDefault(p => !p.Point.IsClosed);
        OnPropertiesChanged(nameof(SelectedPoint), nameof(HasPoint), nameof(NoPoints), nameof(HasAlerts), nameof(IsTestEnvironment), nameof(HeaderText));

        EnvironmentKpi.Value = status.Environment == SiatCodes.EnvironmentTest ? "Pruebas" : "Producción";
        EnvironmentKpi.Detail = status.Environment == SiatCodes.EnvironmentTest ? "Sin valor legal" : "Con valor legal";
        var active = status.Points.Where(p => !p.IsClosed).ToList();
        var offline = active.Count(p => p.Mode != SiatConnectionMode.Online);
        ModeKpi.Value = active.Count == 0 ? "Sin puntos" : offline == 0 ? "En línea" : $"{offline} sin conexión";
        ModeKpi.Detail = active.Count == 0 ? "Ejecute «Preparar SIAT»"
            : string.Join(" · ", active.GroupBy(p => p.Mode).Select(g => $"{g.Count()} {FiscalText.Mode(g.Key).ToLower(Fmt.Culture)}"));
        TodayKpi.Value = status.DocumentsToday.ToString("N0", Fmt.Culture);
        TodayKpi.Detail = Fmt.Money(status.BilledToday);
        PendingKpi.Value = status.PendingDocuments.ToString("N0", Fmt.Culture);
        PendingKpi.Detail = status.PendingDocuments == 0 ? "Nada por enviar" : "Se envían solos cada 20 s";
        OfflineKpi.Value = status.OfflineDocuments.ToString("N0", Fmt.Culture);
        OfflineKpi.Detail = status.OfflineDocuments == 0 ? "Ninguno" : "Se envían al volver la conexión";
        EventsKpi.Value = status.OpenEvents.ToString("N0", Fmt.Culture);
        EventsKpi.Detail = status.OpenEvents == 0 ? "Sin contingencias" : "Contingencias en curso";

        var today = BillingClock.Today(App);
        var events = await App.SendAsync(new GetSignificantEventsQuery(today.AddDays(-45), today));
        Events.ReplaceAll(events.Select(e => new SiatEventItem(e)));
        var packages = await App.SendAsync(new GetFiscalPackagesQuery());
        Packages.ReplaceAll(packages.Select(p => new SiatPackageItem(p)));
        var cafcs = await App.SendAsync(new GetContingencyCodesQuery());
        Cafcs.ReplaceAll(cafcs.Select(c => new SiatCafcItem(c)));
        if (_callsLoaded || _tab == 4)
        {
            await LoadCallsAsync();
        }
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateCountdowns()
    {
        var now = App.Now;
        foreach (var alert in Alerts)
        {
            alert.Update(now);
        }
    }

    private async Task LoadCallsAsync()
    {
        if (!ShowCalls)
        {
            return;
        }
        try
        {
            var today = BillingClock.Today(App);
            var calls = await App.SendAsync(new GetSiatServiceCallsQuery(today.AddDays(-2), today, null, 300));
            Calls.ReplaceAll(calls.Select(c => new SiatCallItem(c)));
            _callsLoaded = true;
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo leer la bitácora del SIN", AppServices.Describe(ex));
        }
    }

    private AsyncRelayCommand PointCommand(string title, Func<SiatPointItem, Task<string>> action, Func<SiatPointItem, bool> allowed) =>
        new(async () =>
        {
            var point = _selectedPoint!;
            if (await RunAsync(async () =>
                {
                    var message = await action(point);
                    App.Notify.Success(title, $"{point.BranchText} · punto {point.Point.Code}: {FiscalText.Plain(message)}");
                }, title + ": no se pudo completar"))
            {
                await LoadAsync(force: true);
            }
        }, () => _selectedPoint is { } p && allowed(p));

    private async Task RunGeneralAsync(string title, Func<Task<string>> action)
    {
        if (await RunAsync(async () => App.Notify.Success(title, FiscalText.Plain(await action())), title + ": no se pudo completar"))
        {
            App.Data.Invalidate();
            await LoadAsync(force: true);
        }
    }

    private async Task GoOfflineAsync()
    {
        var point = _selectedPoint!;
        if (!await App.Dialogs.ConfirmAsync("Pasar a fuera de línea", $"{point.BranchText} · punto {point.Point.Code} deja de enviar en línea: la caja " +
                "sigue facturando (tipo de emisión 2) y las facturas se envían solas en paquetes al volver la conexión.", "Pasar a fuera de línea",
                glyph: Glyphs.Offline))
        {
            return;
        }
        if (await RunAsync(async () => App.Notify.Warning("Fuera de línea", FiscalText.Plain(await App.SendAsync(new GoOfflineCommand(point.Point.Id)))),
                "No se pudo pasar a fuera de línea"))
        {
            await LoadAsync(force: true);
        }
    }

    private async Task StartManualAsync()
    {
        var point = _selectedPoint!;
        try
        {
            _events ??= await App.SendAsync(new GetSiatCatalogQuery(SiatCatalogNames.SignificantEvents));
            var cafcs = await App.SendAsync(new GetContingencyCodesQuery());
            var dialog = new ManualContingencyDialog(App, point.Point, _events, cafcs);
            if (await App.Dialogs.ShowAsync(dialog))
            {
                App.Notify.Warning("Contingencia manual declarada", FiscalText.Plain(dialog.ResultMessage ?? string.Empty));
                await LoadAsync(force: true);
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo declarar la contingencia", AppServices.Describe(ex));
        }
    }

    private async Task EndContingencyAsync()
    {
        var point = _selectedPoint!;
        if (!await App.Dialogs.ConfirmAsync("Fin de la contingencia", $"Se cierra el evento de {point.BranchText} · punto {point.Point.Code} con la hora actual y " +
                "empieza la recuperación: CUFD nuevo → registro del evento en el SIN → envío de los paquetes. Si no hay comunicación, queda recuperando y " +
                "se reintenta solo.", "Terminar contingencia", glyph: Glyphs.CheckCircle,
                details: point.IsManual ? ["Después transcriba las facturas manuales del talonario CAFC (72 h desde el fin)."] : null))
        {
            return;
        }
        if (await RunAsync(async () => App.Notify.Success("Contingencia terminada",
                FiscalText.Plain(await App.SendAsync(new EndContingencyCommand(point.Point.Id)))), "No se pudo terminar la contingencia"))
        {
            await LoadAsync(force: true);
        }
    }

    private async Task RegisterPointAsync()
    {
        try
        {
            var settings = await App.SendAsync(new GetSiatSettingsQuery());
            IReadOnlyList<PosOption> registers = App.Session.Can(PermissionCodes.PosOperate) ? (await App.SendAsync(new GetPosStateQuery())).Registers : [];
            var dialog = new RegisterPointDialog(App, settings.Branches, registers);
            if (await App.Dialogs.ShowAsync(dialog) && dialog.Result is { } point)
            {
                App.Notify.Success("Punto de venta registrado", $"{point.BranchCode} · punto {point.Code} · {point.Name}");
                await LoadAsync(force: true);
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo registrar el punto de venta", AppServices.Describe(ex));
        }
    }

    private async Task LinkRegisterAsync()
    {
        var point = _selectedPoint!;
        IReadOnlyList<string> options = [];
        if (App.Session.Can(PermissionCodes.PosOperate))
        {
            try
            {
                options = (await App.SendAsync(new GetPosStateQuery())).Registers.Select(r => r.Code).ToList();
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                options = [];
            }
        }
        var code = await App.Dialogs.PromptAsync("Vincular caja", $"La caja elegida factura con el punto {point.Point.Code} de {point.BranchText}. " +
            "Deje el campo vacío para desvincularla.", "Código de la caja", options, "Vincular", point.Point.RegisterCode, "CAJA01", glyph: Glyphs.Link);
        if (code is null)
        {
            return;
        }
        if (await RunAsync(async () => App.Notify.Success("Caja vinculada", FiscalText.Plain(
                await App.SendAsync(new LinkPointOfSaleRegisterCommand(point.Point.Id, code.Length == 0 ? null : code.ToUpperInvariant())))), "No se pudo vincular"))
        {
            await LoadAsync(force: true);
        }
    }

    private async Task ClosePointAsync()
    {
        var point = _selectedPoint!;
        if (!await App.Dialogs.ConfirmAsync($"Cerrar el punto de venta {point.Point.Code}", $"El cierre en el SIN es DEFINITIVO: el punto {point.Point.Code} de " +
                $"{point.BranchText} no podrá volver a emitir. Sus documentos siguen consultables.", "Continuar", isDanger: true, glyph: Glyphs.Warning))
        {
            return;
        }
        if (!await App.Dialogs.ConfirmAsync("Confirme el cierre definitivo", $"¿Seguro que cierra para siempre el punto {point.Point.Code} · {point.Point.Name}?",
                "Cerrar definitivamente", "No cerrar", isDanger: true, glyph: Glyphs.Delete))
        {
            return;
        }
        if (await RunAsync(async () => App.Notify.Success("Punto de venta cerrado",
                FiscalText.Plain(await App.SendAsync(new CloseSiatPointOfSaleCommand(point.Point.Id)))), "No se pudo cerrar el punto de venta"))
        {
            await LoadAsync(force: true);
        }
    }

    private async Task RegisterCafcAsync()
    {
        try
        {
            var settings = await App.SendAsync(new GetSiatSettingsQuery());
            var dialog = new RegisterCafcDialog(App, settings.Branches);
            if (await App.Dialogs.ShowAsync(dialog))
            {
                App.Notify.Success("Talonario CAFC registrado", FiscalText.Plain(dialog.ResultMessage ?? string.Empty));
                Tab = 3;
                await LoadAsync(force: true);
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo registrar el talonario", AppServices.Describe(ex));
        }
    }

    private async Task TranscribeAsync()
    {
        try
        {
            var today = BillingClock.Today(App);
            var events = await App.SendAsync(new GetSignificantEventsQuery(today.AddDays(-10), today));
            // Medios de pago: los de la caja o, sin permiso de caja, los del catálogo de homologación
            IReadOnlyList<PosOption> methods = App.Session.Can(PermissionCodes.PosOperate)
                ? (await App.SendAsync(new GetPosStateQuery())).PaymentMethods
                : (await App.SendAsync(new GetHomologationQuery())).PaymentMethods.Select(m => new PosOption(m.Code, m.Name)).ToList();
            _documentTypes ??= await App.SendAsync(new GetSiatCatalogQuery(SiatCatalogNames.IdentityDocumentTypes));
            var picker = new ProductPickerViewModel(App.Data, 8);
            await picker.EnsureLoadedAsync();
            var dialog = new TranscribeDialog(App, events, methods, _documentTypes, picker);
            if (await App.Dialogs.ShowAsync(dialog) && dialog.Result is { } row)
            {
                App.Notify.Success($"Factura manual N° {row.Number} transcrita", "Quedó fuera de línea: viaja en el paquete de la contingencia al recuperarse.");
                App.Data.Invalidate();
                await LoadAsync(force: true);
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo abrir la transcripción", AppServices.Describe(ex));
        }
    }
}
