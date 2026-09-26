using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using MINV.Application.Billing;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Billing;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Llegar a «Documentos fiscales» enfocando un documento (desde Ventas o la caja) y, si se pide, empezar a anularlo.</summary>
public sealed record FiscalDocumentFocus(Guid DocumentId, bool StartVoid = false);

/// <summary>Línea del detalle de un documento fiscal.</summary>
public sealed record FiscalLineItem(FiscalDocumentLineView Line)
{
    public string Title => $"{Line.ProductCode} · {Line.Description}";

    public string Detail => $"{Fmt.Qty(Line.Quantity)} {Line.Unit} × {Fmt.Money(Line.UnitPrice)}" +
                            (Line.Discount > 0 ? $" − desc. {Fmt.Money(Line.Discount)}" : string.Empty) +
                            $" · SIN {Line.SinProductCode} (act. {Line.ActivityCode})" +
                            (Line.TransactionCode is { } tx ? tx == 1 ? " · factura original" : " · devuelto" : string.Empty);

    public string SubtotalText => Fmt.Money(Line.Subtotal);
}

/// <summary>Paso de la bitácora del SIN (línea de tiempo del detalle).</summary>
public sealed record FiscalEventItem(FiscalDocumentEventView Event)
{
    public string Title => FiscalText.Action(Event.Action) + (Event.SiatCode is { } code ? $" · código {code}" : string.Empty);

    public string When => Event.OccurredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) +
                          (Event.User is { } user ? " · " + user : string.Empty);

    public string? Description => Event.Description;

    public string? Messages => string.IsNullOrWhiteSpace(Event.Messages) ? null : Event.Messages;

    public string? Reception => Event.ReceptionCode is { } r ? "Recepción " + r : null;

    public string Brush => FiscalText.ActionBrush(Event.Action);
}

/// <summary>Entrega al comprador (correo, impresión, PDF).</summary>
public sealed record FiscalDeliveryItem(FiscalDeliveryView Delivery)
{
    public string Text => $"{FiscalText.Channel(Delivery.Channel)}{(Delivery.Recipient is { } r ? " · " + r : string.Empty)}";

    public string When => Fmt.DateTime(Delivery.OccurredAt);

    public string ResultText => Delivery.Succeeded ? "✔ entregado" : "✖ " + (Delivery.Error ?? "no se pudo entregar");

    public string Brush => Delivery.Succeeded ? "Success" : "Danger";
}

/// <summary>
/// V4.1 · Documentos fiscales: búsqueda por período, estado, tipo y texto (número, CUF, NIT/CI o nombre), detalle con la
/// bitácora del SIN y las entregas, y todo lo que se hace con un documento: imprimir el rollo, ver el PDF, enviarlo por
/// correo, verificarlo en el SIN, anularlo (con el plazo a la vista), revertir la anulación, devolución con nota
/// crédito-débito, re-emitir y ver el XML exacto.
/// </summary>
public sealed class FiscalDocumentsViewModel : PageViewModel
{
    private static readonly Choice<FiscalDocumentStatus?> AllStatuses = new("Todos los estados", null);
    private static readonly Choice<FiscalDocumentKind?> AllKinds = new("Facturas y notas", null);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private List<FiscalDocumentItem> _items = [];
    private PeriodOption? _period;
    private Choice<FiscalDocumentStatus?> _status = AllStatuses;
    private Choice<FiscalDocumentKind?> _kind = AllKinds;
    private string _search = string.Empty;
    private FiscalDocumentItem? _selected;
    private FiscalDocumentDetail? _detail;
    private string _summary = string.Empty;
    private FiscalDocumentFocus? _focus;
    private IReadOnlyList<SiatCatalogItemView>? _voidReasons;
    private IReadOnlyList<SiatCatalogItemView>? _documentTypes;
    private string _qrUrl = string.Empty;

    public FiscalDocumentsViewModel(AppServices app)
        : base(app, "documentos-fiscales", "Documentos fiscales", "Facturas y notas crédito-débito del SIN: consulta, reimpresión, anulación y devoluciones",
            Glyphs.Invoice)
    {
        Statuses =
        [
            AllStatuses,
            .. Enum.GetValues<FiscalDocumentStatus>().Select(s => new Choice<FiscalDocumentStatus?>(FiscalText.Status(s), s)),
        ];
        Kinds = [AllKinds, new("Solo facturas", FiscalDocumentKind.Invoice), new("Solo notas crédito-débito", FiscalDocumentKind.CreditDebitNote)];
        Rows = CollectionViewSource.GetDefaultView(_items);
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await LoadAsync(force: false);
        };
        QuickToday = new RelayCommand(() => UsePreset("Hoy"));
        QuickWeek = new RelayCommand(() => UsePreset("Últimos 7 días"));
        QuickMonth = new RelayCommand(() => UsePreset("Este mes"));
        PrintRoll = new AsyncRelayCommand(PrintRollAsync, () => _detail is not null);
        ViewPdf = new AsyncRelayCommand(ViewPdfAsync, () => _detail is not null);
        SendEmail = new AsyncRelayCommand(SendEmailAsync, () => _detail is not null && CanIssue && App.Session.IsBillingEnabled);
        Verify = new AsyncRelayCommand(VerifyAsync, () => _detail is not null && App.Session.IsBillingEnabled);
        Void = new AsyncRelayCommand(VoidAsync, () => _detail?.Row.CanVoid == true && CanVoidDocuments);
        Revert = new AsyncRelayCommand(RevertAsync, () => _detail?.Row.CanRevert == true && CanVoidDocuments);
        CreditNote = new AsyncRelayCommand(CreditNoteAsync, () => _detail?.Row is { CanCreditNote: true, SaleNumber: not null } && CanReturn);
        Reissue = new AsyncRelayCommand(ReissueAsync, () => CanReissueSelected);
        ShowXml = new AsyncRelayCommand(ShowXmlAsync, () => _detail is not null);
        CopyCuf = new RelayCommand(() =>
        {
            if (_detail is { } d && ClipboardText.TrySet(d.Row.Cuf))
            {
                App.Notify.Success("CUF copiado", FiscalText.ShortCuf(d.Row.Cuf));
            }
        }, () => _detail is not null);
        OpenRelated = new AsyncRelayCommand<string>(OpenRelatedAsync);
    }

    public ICollectionView Rows { get; private set; }

    public BulkObservableCollection<PeriodOption> Periods { get; } = [];

    public IReadOnlyList<Choice<FiscalDocumentStatus?>> Statuses { get; }

    public IReadOnlyList<Choice<FiscalDocumentKind?>> Kinds { get; }

    public bool CanIssue => App.Session.Can(PermissionCodes.BillingIssue);

    public bool CanVoidDocuments => App.Session.Can(PermissionCodes.BillingVoid);

    /// <summary>La devolución con nota exige además operar la caja (reembolso en un medio de pago).</summary>
    public bool CanReturn => CanVoidDocuments && App.Session.Can(PermissionCodes.PosOperate);

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

    public Choice<FiscalDocumentStatus?> Status { get => _status; set { if (Set(ref _status, value ?? AllStatuses)) { Reload(); } } }

    public Choice<FiscalDocumentKind?> Kind { get => _kind; set { if (Set(ref _kind, value ?? AllKinds)) { Reload(); } } }

    /// <summary>Número, CUF (o su comienzo), NIT/CI o nombre del comprador (se busca en el servidor).</summary>
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

    public KpiCard DocumentsKpi { get; } = new("Documentos", Glyphs.Invoice);

    public KpiCard ValidKpi { get; } = new("Facturado válido", Glyphs.CheckCircle, "Success", "SuccessSoft");

    public KpiCard PendingKpi { get; } = new("Por enviar o validar", Glyphs.Clock, "Warning", "WarningSoft");

    public KpiCard VoidedKpi { get; } = new("Anulados y rechazados", Glyphs.Error, "Danger", "DangerSoft");

    public FiscalDocumentItem? Selected
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

    public FiscalDocumentDetail? Detail
    {
        get => _detail;
        private set
        {
            if (Set(ref _detail, value))
            {
                OnPropertiesChanged(nameof(HasDetail), nameof(Lines), nameof(Events), nameof(Deliveries), nameof(HasDeliveries), nameof(TotalsText),
                    nameof(SubtotalText), nameof(DiscountText), nameof(HasDiscount), nameof(TaxText), nameof(TotalText), nameof(BuyerText), nameof(PaymentText),
                    nameof(OriginalText), nameof(HasOriginal), nameof(HasReplaces), nameof(HasReplacedBy), nameof(StatusHelp), nameof(DeadlineText),
                    nameof(VoidReasonText), nameof(HasVoidReason), nameof(EmissionText), nameof(IsNote), nameof(CufText), nameof(RejectionText),
                    nameof(HasRejection), nameof(CanReissueSelected));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasDetail => _detail is not null;

    public bool IsNote => _detail?.Row.Kind == FiscalDocumentKind.CreditDebitNote;

    public IReadOnlyList<FiscalLineItem> Lines => _detail?.Lines.Select(l => new FiscalLineItem(l)).ToList() ?? [];

    public IReadOnlyList<FiscalEventItem> Events => _detail?.Events.OrderByDescending(e => e.OccurredAt).Select(e => new FiscalEventItem(e)).ToList() ?? [];

    public IReadOnlyList<FiscalDeliveryItem> Deliveries => _detail?.Deliveries.Select(d => new FiscalDeliveryItem(d)).ToList() ?? [];

    public bool HasDeliveries => _detail?.Deliveries.Count > 0;

    public string CufText => _detail?.Row.Cuf ?? string.Empty;

    public string BuyerText => _detail is { } d
        ? $"{d.Row.BuyerName} · {d.Row.BuyerDocument}{(d.ExceptionCode == 1 ? " · NIT sin validar (excepción)" : string.Empty)}{(d.BuyerEmail is { } e ? " · " + e : string.Empty)}"
        : string.Empty;

    public string PaymentText => _detail is { } d
        ? (d.PaymentMethod ?? "—") + (d.CardNumberMasked is { } card ? " · tarjeta " + card : string.Empty) + (d.Row.SaleNumber is { } s ? $" · venta {s}" : string.Empty)
        : string.Empty;

    public string EmissionText => _detail is { } d
        ? d.Row.EmissionType == SiatCodes.EmissionOffline
            ? d.Cafc is { } cafc ? $"Factura manual transcrita · CAFC {cafc}" : "Emitida fuera de línea (tipo de emisión 2)"
            : "Emitida en línea (tipo de emisión 1)"
        : string.Empty;

    private decimal LinesSubtotal => _detail?.Lines.Where(l => !IsNote || l.TransactionCode != 1).Sum(l => l.Subtotal) ?? 0m;

    public string SubtotalText => Fmt.Money(LinesSubtotal);

    public bool HasDiscount => !IsNote && LinesSubtotal - (_detail?.Row.Total ?? 0) > 0;

    public string DiscountText => Fmt.Money(LinesSubtotal - (_detail?.Row.Total ?? 0));

    public string TotalText => Fmt.Money(_detail?.Row.Total ?? 0);

    public string TaxText => Fmt.Money(_detail?.TaxAmount ?? 0);

    public string TotalsText => IsNote ? "MONTO DEVUELTO" : "TOTAL";

    public string StatusHelp => _detail is { } d ? FiscalText.StatusHelp(d.Row.Status) : string.Empty;

    public string DeadlineText => _detail is { } d
        ? d.Row.Status is FiscalDocumentStatus.Valid or FiscalDocumentStatus.DuplicateToVoid or FiscalDocumentStatus.Voided
            ? $"Plazo de anulación y reversión: hasta el {d.Row.VoidDeadline:dd/MM/yyyy} (día 9 del mes siguiente)."
            : string.Empty
        : string.Empty;

    public string? VoidReasonText => _detail?.VoidReason is { } r ? "Motivo de la anulación: " + Fmt.SentenceCase(r) : null;

    public bool HasVoidReason => _detail?.VoidReason is not null;

    public string? OriginalText => _detail?.Original is { } o
        ? $"Factura original N° {o.Number} del {o.IssuedAt:dd/MM/yyyy} · CUF {FiscalText.ShortCuf(o.Cuf)}"
        : null;

    public bool HasOriginal => _detail?.Original is not null;

    public bool HasReplaces => _detail?.ReplacesDocumentId is not null;

    public bool HasReplacedBy => _detail?.ReplacedByDocumentId is not null;

    /// <summary>Mensajes del SIN del último rechazo u observación.</summary>
    public string? RejectionText => _detail is { } d && d.Row.Status is FiscalDocumentStatus.Rejected or FiscalDocumentStatus.PackageRejected
        ? d.Events.Where(e => e.Messages is not null).OrderByDescending(e => e.OccurredAt).Select(e => e.Messages).FirstOrDefault()
        : null;

    public bool HasRejection => RejectionText is not null;

    /// <summary>URL del QR de la representación gráfica (consulta del documento en el SIN).</summary>
    public string QrUrl { get => _qrUrl; private set => Set(ref _qrUrl, value); }

    public bool CanReissueSelected => _detail is { } d && CanIssue && d.ReplacedByDocumentId is null
                                      && d.Row.Status is FiscalDocumentStatus.Rejected or FiscalDocumentStatus.PackageRejected or FiscalDocumentStatus.Voided;

    public bool IsEmpty => HasLoaded && Rows.IsEmpty;

    public RelayCommand QuickToday { get; }

    public RelayCommand QuickWeek { get; }

    public RelayCommand QuickMonth { get; }

    public AsyncRelayCommand PrintRoll { get; }

    public AsyncRelayCommand ViewPdf { get; }

    public AsyncRelayCommand SendEmail { get; }

    public AsyncRelayCommand Verify { get; }

    public AsyncRelayCommand Void { get; }

    public AsyncRelayCommand Revert { get; }

    public AsyncRelayCommand CreditNote { get; }

    public AsyncRelayCommand Reissue { get; }

    public AsyncRelayCommand ShowXml { get; }

    public RelayCommand CopyCuf { get; }

    /// <summary>Abre el documento reemplazado («replaces») o el que lo reemplaza («replacedBy»).</summary>
    public AsyncRelayCommand<string> OpenRelated { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        if (Periods.Count == 0)
        {
            Periods.ReplaceAll(PeriodOption.Presets(BillingClock.Today(App)));
            _period = Periods.First(p => p.Label == "Últimos 30 días");
            OnPropertyChanged(nameof(Period));
        }
        var period = _period ?? Periods[0];
        var rows = await App.SendAsync(new GetFiscalDocumentsQuery(period.From, period.To, _status.Value, _kind.Value,
            string.IsNullOrWhiteSpace(_search) ? null : _search.Trim()));
        var selected = _selected?.Id;
        _items = rows.Select(r => new FiscalDocumentItem(r)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.SortDescriptions.Add(new SortDescription(nameof(FiscalDocumentItem.IssuedAt), ListSortDirection.Descending));
        OnPropertyChanged(nameof(Rows));
        var valid = rows.Where(r => r.Status == FiscalDocumentStatus.Valid && r.Kind == FiscalDocumentKind.Invoice).ToList();
        DocumentsKpi.Value = rows.Count.ToString("N0", Fmt.Culture);
        DocumentsKpi.Detail = $"{rows.Count(r => r.Kind == FiscalDocumentKind.Invoice)} facturas · {rows.Count(r => r.Kind == FiscalDocumentKind.CreditDebitNote)} notas";
        ValidKpi.Value = Fmt.Money(valid.Sum(r => r.Total));
        ValidKpi.Detail = $"{valid.Count} facturas válidas en el SIN";
        var waiting = rows.Where(r => r.Status is FiscalDocumentStatus.Pending or FiscalDocumentStatus.Offline or FiscalDocumentStatus.InPackage
            or FiscalDocumentStatus.NoResponse).ToList();
        PendingKpi.Value = waiting.Count.ToString("N0", Fmt.Culture);
        PendingKpi.Detail = waiting.Count == 0 ? "Todo validado" : $"{waiting.Count(r => r.EmissionType == SiatCodes.EmissionOffline)} emitidos fuera de línea";
        var bad = rows.Where(r => r.Status is FiscalDocumentStatus.Voided or FiscalDocumentStatus.Rejected or FiscalDocumentStatus.PackageRejected
            or FiscalDocumentStatus.DuplicateToVoid).ToList();
        VoidedKpi.Value = bad.Count.ToString("N0", Fmt.Culture);
        VoidedKpi.Detail = bad.Count == 0 ? "Sin anulaciones ni rechazos" : $"{bad.Count(r => r.Status == FiscalDocumentStatus.Voided)} anulados";
        Summary = $"{rows.Count} documentos · {period.Label} ({period.RangeText})";
        Subtitle = App.Session.IsBillingEnabled
            ? "Facturas y notas crédito-débito del SIN: consulta, reimpresión, anulación y devoluciones"
            : "La facturación SIAT no está activa: aquí verá los documentos cuando se active (Administración › Facturación SIAT)";
        OnPropertiesChanged(nameof(Subtitle), nameof(IsEmpty));
        if (_focus is { } focus)
        {
            _focus = null;
            await FocusAsync(focus);
            return;
        }
        if (selected is { } id)
        {
            _selected = _items.FirstOrDefault(i => i.Id == id);
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(HasSelection));
            await LoadDetailAsync();
        }
    }

    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is FiscalDocumentFocus focus)
        {
            if (HasLoaded)
            {
                _ = FocusAsync(focus);
            }
            else
            {
                _focus = focus;
            }
        }
    }

    /// <summary>Selecciona un documento (cambia el período si hace falta) y, si se pide, abre la anulación.</summary>
    public async Task FocusAsync(FiscalDocumentFocus focus)
    {
        try
        {
            var detail = await App.SendAsync(new GetFiscalDocumentQuery(focus.DocumentId));
            var day = DateOnly.FromDateTime(detail.Row.IssuedAt);
            if (_period is null || day < _period.From || day > _period.To || _status.Value is not null || _kind.Value is not null || _search.Length > 0)
            {
                var option = new PeriodOption($"Día {Fmt.Date(day)}", day, day);
                Periods.ReplaceAll(PeriodOption.Presets(BillingClock.Today(App)).Prepend(option));
                _period = option;
                _status = AllStatuses;
                _kind = AllKinds;
                _search = string.Empty;
                OnPropertiesChanged(nameof(Period), nameof(Status), nameof(Kind), nameof(Search));
                await LoadAsync(force: true);
            }
            Selected = _items.FirstOrDefault(i => i.Id == focus.DocumentId);
            if (focus.StartVoid && Selected is not null)
            {
                await LoadDetailAsync();
                if (Void.CanExecute(null))
                {
                    await VoidAsync();
                }
                else if (_detail is { } d)
                {
                    App.Notify.Warning("No se puede anular", d.Row.Status == FiscalDocumentStatus.Voided
                        ? "El documento ya está anulado."
                        : $"El documento está {FiscalText.Status(d.Row.Status).ToLower(Fmt.Culture)}: solo se anula un documento válido y dentro del plazo (hasta el {d.Row.VoidDeadline:dd/MM/yyyy}).");
                }
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo abrir el documento", AppServices.Describe(ex));
        }
    }

    private void UsePreset(string label)
    {
        if (Periods.FirstOrDefault(p => p.Label == label) is { } option)
        {
            Period = option;
        }
    }

    private void Reload()
    {
        if (HasLoaded)
        {
            _ = LoadAsync(force: false);
        }
    }

    private async Task LoadDetailAsync()
    {
        if (_selected is not { } item)
        {
            Detail = null;
            QrUrl = string.Empty;
            return;
        }
        try
        {
            var detail = await App.SendAsync(new GetFiscalDocumentQuery(item.Id));
            var qr = string.Empty;
            try
            {
                qr = (await App.SendAsync(new GetFiscalPrintModelQuery(item.Id))).QrUrl;
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                System.Diagnostics.Trace.TraceInformation("M-INV · QR del documento {0}: {1}", item.Id, ex.Message);
            }
            if (ReferenceEquals(item, _selected))
            {
                Detail = detail;
                QrUrl = qr;
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Detail = null;
            App.Notify.Error("No se pudo leer el documento", AppServices.Describe(ex));
        }
    }

    private async Task AfterChangeAsync()
    {
        App.Data.Invalidate();
        await LoadAsync(force: true);
    }

    private async Task PrintRollAsync()
    {
        var d = _detail!;
        try
        {
            if (!await FiscalOutput.PrintRollAsync(App, d.Row.Id))
            {
                App.Notify.Warning("Sin impresora de rollo",
                    "Esta caja no tiene impresora configurada (Configuración › Impresora de tickets). Use «Ver PDF» para imprimir en hoja.");
                return;
            }
            App.Notify.Success("Rollo impreso", $"{FiscalText.Kind(d.Row.Kind)} N° {d.Row.Number}");
            await LoadDetailAsync();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex) || ex is IOException or UnauthorizedAccessException or TimeoutException
                                       or OperationCanceledException or System.Net.Sockets.SocketException or ArgumentException)
        {
            App.Notify.Error("No se pudo imprimir", AppServices.IsExpected(ex) ? AppServices.Describe(ex) : ex.Message);
        }
    }

    private async Task ViewPdfAsync()
    {
        var d = _detail!;
        try
        {
            var path = await FiscalOutput.SavePdfAsync(App, d.Row.Id);
            App.Notify.Success("PDF listo", $"{Path.GetFileName(path)} (carpeta {FiscalOutput.Folder})");
            await LoadDetailAsync();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex) || ex is IOException or UnauthorizedAccessException)
        {
            App.Notify.Error("No se pudo generar el PDF", AppServices.IsExpected(ex) ? AppServices.Describe(ex) : ex.Message);
        }
    }

    private async Task SendEmailAsync()
    {
        var d = _detail!;
        var email = await App.Dialogs.PromptAsync("Enviar por correo", $"Se envían el XML y el PDF de {FiscalText.Kind(d.Row.Kind).ToLower(Fmt.Culture)} N° {d.Row.Number}.",
            "Correo del comprador", d.BuyerEmail is { } e ? [e] : [], "Enviar", d.BuyerEmail, "correo@ejemplo.com", glyph: Glyphs.Mail);
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }
        if (await RunAsync(async () =>
            {
                var message = await App.SendAsync(new SendFiscalDocumentEmailCommand(d.Row.Id, email));
                App.Notify.Success("Correo enviado", FiscalText.Plain(message));
            }, "No se pudo enviar el correo"))
        {
            await LoadDetailAsync();
        }
    }

    private async Task VerifyAsync()
    {
        var d = _detail!;
        if (await RunAsync(async () =>
            {
                var message = await App.SendAsync(new CheckFiscalDocumentStatusCommand(d.Row.Id));
                App.Notify.Info("Estado en el SIN", FiscalText.Plain(message));
            }, "No se pudo verificar en el SIN"))
        {
            await AfterChangeAsync();
        }
    }

    private async Task VoidAsync()
    {
        var d = _detail!;
        try
        {
            _voidReasons ??= await App.SendAsync(new GetSiatCatalogQuery(SiatCatalogNames.VoidReasons));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo leer el catálogo de motivos", AppServices.Describe(ex));
            return;
        }
        var dialog = new VoidFiscalDialog(App, d, _voidReasons);
        if (!dialog.HasReasons)
        {
            App.Notify.Warning("Falta el catálogo de motivos de anulación", "Sincronice los catálogos del SIN (Facturación › Estado SIAT).");
            return;
        }
        if (await App.Dialogs.ShowAsync(dialog))
        {
            App.Notify.Success("Documento anulado", FiscalText.Plain(dialog.ResultMessage ?? string.Empty));
            await AfterChangeAsync();
        }
    }

    private async Task RevertAsync()
    {
        var d = _detail!;
        if (!await App.Dialogs.ConfirmAsync($"Revertir la anulación de N° {d.Row.Number}",
                $"El documento vuelve a ser VÁLIDO ante el SIN. La reversión se puede hacer UNA sola vez y dentro del plazo (hasta el {d.Row.VoidDeadline:dd/MM/yyyy}): " +
                "después ya no se podrá anular este documento. El comprador recibe el aviso por correo.", "Revertir anulación", glyph: Glyphs.Undo,
                details: ["Úselo solo si la anulación fue un error.", "La mercadería devuelta (si la hubo) no vuelve a salir: revise el stock."]))
        {
            return;
        }
        if (await RunAsync(async () =>
            {
                var message = await App.SendAsync(new RevertFiscalVoidCommand(d.Row.Id));
                App.Notify.Success("Anulación revertida", FiscalText.Plain(message));
            }, "No se pudo revertir"))
        {
            await AfterChangeAsync();
        }
    }

    private async Task CreditNoteAsync()
    {
        var d = _detail!;
        try
        {
            var result = await SalesReturnDialog.OpenAsync(App, d.Row.SaleNumber!);
            if (result is null)
            {
                return;
            }
            App.Notify.Success($"Devolución {result.Number} registrada", FiscalText.Plain(result.Message));
            await AfterChangeAsync();
            if (result.CreditNoteId is { } noteId)
            {
                await SendNowAsync(noteId);
                await FocusAsync(new FiscalDocumentFocus(noteId));
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo registrar la devolución", AppServices.Describe(ex));
        }
    }

    private async Task ReissueAsync()
    {
        var d = _detail!;
        try
        {
            _documentTypes ??= await App.SendAsync(new GetSiatCatalogQuery(SiatCatalogNames.IdentityDocumentTypes));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            _documentTypes = [];
        }
        var dialog = new ReissueDialog(App, d, _documentTypes);
        if (await App.Dialogs.ShowAsync(dialog) && dialog.Result is { } row)
        {
            App.Notify.Success($"Documento N° {row.Number} emitido", dialog.DispatchMessage ?? "Se envía al SIN automáticamente.");
            await AfterChangeAsync();
            await FocusAsync(new FiscalDocumentFocus(row.Id));
        }
    }

    private async Task SendNowAsync(Guid documentId)
    {
        try
        {
            await App.SendAsync(new DispatchFiscalDocumentsCommand(documentId));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            System.Diagnostics.Trace.TraceInformation("M-INV · envío inmediato de {0}: {1}", documentId, ex.Message);
        }
    }

    private async Task ShowXmlAsync()
    {
        var d = _detail!;
        await App.Dialogs.ShowAsync(new XmlDialog($"XML de {FiscalText.Kind(d.Row.Kind).ToLower(Fmt.Culture)} N° {d.Row.Number}", d.Xml));
    }

    private async Task OpenRelatedAsync(string which)
    {
        var target = which == "replaces" ? _detail?.ReplacesDocumentId : _detail?.ReplacedByDocumentId;
        if (target is { } id)
        {
            await FocusAsync(new FiscalDocumentFocus(id));
        }
    }
}
