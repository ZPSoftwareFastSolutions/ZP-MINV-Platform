using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using MINV.Application.Integration;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Sales;

namespace MINV.DesktopClient.ViewModels;

/// <summary>V7 · Fila de la cola de correos.</summary>
public sealed class MailQueueItem(OutgoingMailRow row)
{
    public OutgoingMailRow Row { get; } = row;

    public string Reservation => Row.Reservation;

    public string KindText => ReservationText.Kind(Row.ReservationKind);

    public string BranchCode => Row.BranchCode;

    public string MailKind => Row.KindText;

    public string Recipient => Row.Recipient;

    public OutgoingMailStatus Status => Row.Status;

    public string StatusText => Row.StatusText;

    public string StatusBrush => ReservationText.MailBrush(Row.Status);

    public string StatusSoftBrush => StatusBrush + "Soft";

    public DateTimeOffset RequestedAt => Row.RequestedAt;

    public string RequestedText => Fmt.DateTime(Row.RequestedAt);

    public string AttemptsText => $"{Row.Attempts} de {Row.MaxAttempts}";

    public int Attempts => Row.Attempts;

    public string LastAttemptText => Row.LastAttemptAt is { } at ? Fmt.DateTime(at) : "—";

    /// <summary>Próximo intento (pendiente), cuándo terminó (enviado, agotado o cancelado) o el último error.</summary>
    public string DetailText => Row.Status switch
    {
        OutgoingMailStatus.Pending when Row.NextAttemptAt is { } next => $"Próximo intento {Fmt.DateTime(next)}",
        OutgoingMailStatus.Sent when Row.CompletedAt is { } done => $"Enviado el {Fmt.DateTime(done)}",
        _ when Row.LastError is { Length: > 0 } error => error,
        _ when Row.CompletedAt is { } done => $"Cerrado el {Fmt.DateTime(done)}",
        _ => "—",
    };

    public bool HasError => !string.IsNullOrWhiteSpace(Row.LastError);

    public string ErrorText => Row.LastError ?? string.Empty;
}

/// <summary>
/// V7 · Administración › Correos: la cola de correos de confirmación de las reservas (GetOutgoingMailsQuery, permiso
/// sales.pcbuild.manage) con filtros por estado, tipo de reserva, sucursal, fechas y búsqueda; «Reenviar» encola otra
/// confirmación de la reserva (ResendReservationMailCommand: solo si sigue vigente) y «Ver reserva» abre la pantalla Reservas.
/// Ninguna acción envía nada: el despachador del servidor envía después del COMMIT (regla P-06).
/// </summary>
public sealed class MailQueueViewModel : PageViewModel
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private List<MailQueueItem> _items = [];
    private MailQueueItem? _selected;
    private string _search = string.Empty;
    private Choice<OutgoingMailStatus?> _status;
    private Choice<PcBuildKind?> _kind;
    private Choice<string?> _branch;
    private Choice<PeriodOption?> _period;
    private string _summary = string.Empty;
    private int _visible;

    public MailQueueViewModel(AppServices app)
        : base(app, "correos", "Correos", "Cola de correos de confirmación de las reservas", Glyphs.Mail)
    {
        Statuses =
        [
            new("Todos los estados", null), new("Pendientes", OutgoingMailStatus.Pending), new("Enviados", OutgoingMailStatus.Sent),
            new("Agotados (no se pudieron enviar)", OutgoingMailStatus.Exhausted), new("Cancelados", OutgoingMailStatus.Cancelled),
        ];
        Kinds = [new("Todos los tipos de reserva", null), new("Compra (carrito)", PcBuildKind.Cart), new("Armado de PC", PcBuildKind.Build)];
        Branches.ReplaceAll([new Choice<string?>("Todas las sucursales", null)]);
        Periods.ReplaceAll(FilterChoices.Periods(Today));
        _status = Statuses[0];
        _kind = Kinds[0];
        _branch = Branches[0];
        _period = Periods[0];
        Rows = CollectionViewSource.GetDefaultView(_items);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ApplyFilter();
        };
        Resend = new AsyncRelayCommand(ResendAsync, () => _selected is not null && App.Session.Can(PermissionCodes.PcBuildManage));
        OpenReservation = new RelayCommand(() =>
        {
            if (_selected is { } mail)
            {
                App.Navigator.Navigate("reservas", new ReservationFocus(mail.Reservation));
            }
        }, () => _selected is not null);
        Export = new RelayCommand(() => App.ExportCsv(App.CsvName("correos"), "Cola de correos", ExportTable()), () => _items.Count > 0);
        ClearFilters = new RelayCommand(ClearAllFilters, () => HasFilters);
    }

    public ICollectionView Rows { get; private set; }

    public IReadOnlyList<Choice<OutgoingMailStatus?>> Statuses { get; }

    public IReadOnlyList<Choice<PcBuildKind?>> Kinds { get; }

    public BulkObservableCollection<Choice<string?>> Branches { get; } = [];

    public BulkObservableCollection<Choice<PeriodOption?>> Periods { get; } = [];

    public bool HasBranches => Branches.Count > 2;

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

    public Choice<OutgoingMailStatus?> Status { get => _status; set => SetFilter(ref _status, value ?? Statuses[0]); }

    public Choice<PcBuildKind?> Kind { get => _kind; set => SetFilter(ref _kind, value ?? Kinds[0]); }

    public Choice<string?> Branch { get => _branch; set => SetFilter(ref _branch, value ?? Branches[0]); }

    public Choice<PeriodOption?> Period { get => _period; set => SetFilter(ref _period, value ?? Periods[0]); }

    public bool HasFilters => _search.Trim().Length > 0 || _status.Value is not null || _kind.Value is not null || _branch.Value is not null
                              || _period.Value is not null;

    public MailQueueItem? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public int VisibleCount { get => _visible; private set => Set(ref _visible, value); }

    public bool NoMails => HasLoaded && _items.Count == 0;

    public bool NoMatches => HasLoaded && _items.Count > 0 && VisibleCount == 0;

    public KpiCard PendingKpi { get; } = new("En cola", Glyphs.Clock, "Info", "InfoSoft");

    public KpiCard SentKpi { get; } = new("Enviados", Glyphs.CheckCircle, "Success", "SuccessSoft");

    public KpiCard FailedKpi { get; } = new("No se pudieron enviar", Glyphs.Error, "Danger", "DangerSoft");

    public AsyncRelayCommand Resend { get; }

    public RelayCommand OpenReservation { get; }

    public RelayCommand Export { get; }

    public RelayCommand ClearFilters { get; }

    private DateOnly Today => DateOnly.FromDateTime(App.Now.ToLocalTime().DateTime);

    protected override async Task LoadCoreAsync(bool force)
    {
        var rows = await App.SendAsync(new GetOutgoingMailsQuery(Take: 500));
        _items = rows.Select(r => new MailQueueItem(r)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = Matches;
        Rows.SortDescriptions.Add(new SortDescription(nameof(MailQueueItem.RequestedAt), ListSortDirection.Descending));
        OnPropertyChanged(nameof(Rows));
        Branches.ReplaceAll(FilterChoices.Of("Todas las sucursales", _items.Select(i => i.BranchCode)));
        _branch = FilterChoices.Keep(Branches, _branch);
        Periods.ReplaceAll(FilterChoices.Periods(Today));
        _period = FilterChoices.Keep(Periods, _period);
        OnPropertiesChanged(nameof(Branch), nameof(Period), nameof(HasBranches));
        PendingKpi.Value = rows.Count(r => r.Status == OutgoingMailStatus.Pending).ToString("N0", Fmt.Culture);
        PendingKpi.Detail = "Los envía el servidor en segundo plano";
        SentKpi.Value = rows.Count(r => r.Status == OutgoingMailStatus.Sent).ToString("N0", Fmt.Culture);
        SentKpi.Detail = $"De {rows.Count} pedidos";
        FailedKpi.Value = rows.Count(r => r.Status == OutgoingMailStatus.Exhausted).ToString("N0", Fmt.Culture);
        FailedKpi.Detail = rows.Count(r => r.Status == OutgoingMailStatus.Cancelled) is var cancelled and > 0
            ? $"{cancelled} cancelado{(cancelled == 1 ? "" : "s")} (reserva cerrada o reenviada)"
            : "Ninguno cancelado";
        if (_selected is { } previous)
        {
            _selected = _items.FirstOrDefault(i => i.Row.Id == previous.Row.Id);
            OnPropertyChanged(nameof(Selected));
        }
        ApplyFilter();
    }

    private void SetFilter<T>(ref Choice<T> field, Choice<T> value)
    {
        if (!EqualityComparer<Choice<T>>.Default.Equals(field, value))
        {
            field = value;
            OnPropertiesChanged(nameof(Status), nameof(Kind), nameof(Branch), nameof(Period));
            ApplyFilter();
        }
    }

    private bool Matches(object o)
    {
        if (o is not MailQueueItem m)
        {
            return false;
        }
        if (_status.Value is { } status && m.Status != status)
        {
            return false;
        }
        if (_kind.Value is { } kind && m.Row.ReservationKind != kind)
        {
            return false;
        }
        if (_branch.Value is { } branch && m.BranchCode != branch)
        {
            return false;
        }
        if (_period.Value is { } period)
        {
            var day = DateOnly.FromDateTime(m.RequestedAt.ToLocalTime().DateTime);
            if (day < period.From || day > period.To)
            {
                return false;
            }
        }
        var q = _search.Trim();
        return q.Length == 0 || m.Reservation.Contains(q, StringComparison.OrdinalIgnoreCase) || m.Recipient.Contains(q, StringComparison.OrdinalIgnoreCase)
               || FilterChoices.Contains(m.ErrorText, q);
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        VisibleCount = Rows.Cast<object>().Count();
        Summary = VisibleCount == _items.Count
            ? (_items.Count == 1 ? "1 correo" : $"{_items.Count} correos")
            : $"{VisibleCount} de {_items.Count} correos";
        OnPropertiesChanged(nameof(HasFilters), nameof(NoMails), nameof(NoMatches));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void ClearAllFilters()
    {
        _search = string.Empty;
        _status = Statuses[0];
        _kind = Kinds[0];
        _branch = Branches[0];
        _period = Periods[0];
        OnPropertiesChanged(nameof(Search), nameof(Status), nameof(Kind), nameof(Branch), nameof(Period));
        ApplyFilter();
    }

    /// <summary>Otra confirmación de la reserva del correo elegido (al mismo destinatario o a otro): solo si la reserva sigue
    /// vigente; los pendientes de esa reserva se reemplazan.</summary>
    private async Task ResendAsync()
    {
        if (_selected is not { } mail)
        {
            return;
        }
        var email = await App.Dialogs.PromptAsync($"Reenviar la confirmación de {mail.Reservation}",
            "Se encola otro correo con el número, los productos y el vencimiento de la reserva (solo si la reserva sigue vigente). " +
            "Puede enviarlo a otro correo si el anterior no llegó.",
            "Correo del cliente", [mail.Recipient], "Reenviar correo", mail.Recipient, "nombre@correo.com", glyph: Glyphs.Mail);
        if (email is null)
        {
            return;
        }
        try
        {
            var row = await App.SendAsync(new ResendReservationMailCommand(mail.Reservation, email.Length == 0 ? null : email));
            App.Notify.Success("Correo en cola", $"La confirmación de {row.Reservation} sale en unos minutos a {row.Recipient}.");
            await LoadAsync(force: true);
            Selected = _items.FirstOrDefault(i => i.Row.Id == row.Id);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo reenviar el correo", AppServices.Describe(ex));
        }
    }

    public CsvTable ExportTable() => CsvTable.Of(
        ["Pedido el", "Reserva", "Tipo de reserva", "Sucursal", "Correo", "Destinatario", "Estado", "Intentos", "Último intento", "Próximo intento", "Terminado",
            "Último error"],
        Rows.Cast<MailQueueItem>(),
        m => [m.RequestedAt, m.Reservation, m.KindText, m.BranchCode, m.MailKind, m.Recipient, m.StatusText, m.Attempts, m.Row.LastAttemptAt,
            m.Row.NextAttemptAt, m.Row.CompletedAt, m.Row.LastError]);
}
