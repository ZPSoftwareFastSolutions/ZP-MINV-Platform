using System.Collections;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using MINV.Application.Integration;
using MINV.Application.Tech;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Sales;

namespace MINV.DesktopClient.ViewModels;

// =====================================================================================================================
// V7 · Pantalla «Reservas» (sección Ventas, permiso sales.pcbuild.manage): los carritos (RES-…) y los armados (ARM-…) que se
// reservaron, de la tienda web y del mostrador, con su contacto, su vencimiento y su correo de confirmación. Usa los MISMOS
// casos de uso que el panel web (regla P-12): GetPcBuildsQuery, GetPcBuildQuery, ReleasePcBuildReservationCommand,
// ResendReservationMailCommand, ReserveCartCommand y GetOutgoingMailsQuery. La caja vende la reserva con SellPcBuildCommand.
// =====================================================================================================================

/// <summary>V7 · Cómo está una reserva para el personal (se deriva de la fila: estado, vencimiento y motivo del cierre).</summary>
public enum ReservationState
{
    /// <summary>Reservada y vigente: el cliente puede pasar a recogerla.</summary>
    Active,

    /// <summary>Reservada con el plazo cumplido: la cierra el trabajo automático (mientras tanto se puede cobrar o liberar).</summary>
    Overdue,

    Sold,

    /// <summary>Liberada por el personal o cancelada por el cliente (el stock volvió).</summary>
    Released,

    /// <summary>Vencida y ya cerrada por el trabajo automático (el stock volvió).</summary>
    Expired,

    /// <summary>Carrito cotizado que nunca se reservó (raro: el mostrador reserva al crear).</summary>
    Quoted,
}

/// <summary>V7 · Filtro «Vence».</summary>
public enum ReservationDue
{
    Today,
    Tomorrow,
    Expired,
}

/// <summary>V7 · Textos y colores (claves de la paleta) de las reservas y de su correo.</summary>
public static class ReservationText
{
    public static ReservationState StateOf(PcBuildRow row, DateTimeOffset now) => row.Status switch
    {
        PcBuildStatus.Reserved => row.IsReservationActive(now) ? ReservationState.Active : ReservationState.Overdue,
        PcBuildStatus.Sold => ReservationState.Sold,
        PcBuildStatus.Cancelled when row.CancelReason == PcBuild.ExpiredReason => ReservationState.Expired,
        PcBuildStatus.Cancelled => ReservationState.Released,
        _ => ReservationState.Quoted,
    };

    public static string State(ReservationState state) => state switch
    {
        ReservationState.Active => "Reservada",
        ReservationState.Overdue => "Vencida, por cerrar",
        ReservationState.Sold => "Vendida",
        ReservationState.Released => "Liberada",
        ReservationState.Expired => "Vencida",
        _ => "Sin reservar",
    };

    public static string StateBrush(ReservationState state) => state switch
    {
        ReservationState.Active => "Brand",
        ReservationState.Overdue => "Warning",
        ReservationState.Sold => "Success",
        ReservationState.Released => "Danger",
        ReservationState.Expired => "StatusInactive",
        _ => "Info",
    };

    /// <summary>«Compra» (carrito) o «Armado» (armado de PC).</summary>
    public static string Kind(PcBuildKind kind) => kind == PcBuildKind.Cart ? "Compra" : "Armado";

    public static string Channel(PcBuildChannel channel) => channel == PcBuildChannel.Web ? "Web" : "Mostrador";

    public static string MailBrush(OutgoingMailStatus? status) => status switch
    {
        OutgoingMailStatus.Sent => "Success",
        OutgoingMailStatus.Pending => "Info",
        OutgoingMailStatus.Exhausted => "Danger",
        _ => "StatusInactive",
    };

    /// <summary>Tipo de documento para la factura (CI, NIT…) como lo guarda la reserva.</summary>
    public static string Document(int? type, string? number, string? complement) => type is null || string.IsNullOrWhiteSpace(number)
        ? "—"
        : (DocumentTypeOption.From(null).FirstOrDefault(t => t.Code == type)?.Short ?? "Documento") + " " + number
          + (string.IsNullOrWhiteSpace(complement) ? string.Empty : "-" + complement);
}

/// <summary>V7 · Fila de la lista de reservas.</summary>
public sealed class ReservationItem(PcBuildRow row, OutgoingMailRow? mail, DateTimeOffset now)
{
    private readonly DateTimeOffset _now = now;

    public PcBuildRow Row { get; } = row;

    /// <summary>Último correo de confirmación pedido para esta reserva (null si nunca se encoló uno).</summary>
    public OutgoingMailRow? Mail { get; } = mail;

    public string Number => Row.Number;

    public string Name => Row.Name;

    public bool IsCart => Row.Kind == PcBuildKind.Cart;

    public string KindText => ReservationText.Kind(Row.Kind);

    public string KindGlyph => IsCart ? Glyphs.Cart : Glyphs.Monitor;

    public bool IsWeb => Row.Channel == PcBuildChannel.Web;

    public string ChannelText => ReservationText.Channel(Row.Channel);

    public string ChannelGlyph => IsWeb ? Glyphs.Globe : Glyphs.Home;

    public string BranchCode => Row.BranchCode;

    /// <summary>El cliente registrado o, si no hay, quien reservó (contacto).</summary>
    public string Customer => Row.Customer ?? Row.ContactName ?? "Sin nombre";

    public string CustomerDetail => Row.Customer is not null && !string.IsNullOrWhiteSpace(Row.ContactName) && Row.ContactName != Row.Customer
        ? $"Recoge: {Row.ContactName}"
        : ItemsText;

    public string ItemsText => IsCart
        ? (Row.Items == 1 ? "1 producto" : $"{Row.Items} productos")
        : (Row.Items == 1 ? "1 pieza" : $"{Row.Items} piezas");

    public string Phone => Row.ContactPhone ?? string.Empty;

    public bool HasPhone => Phone.Length > 0;

    public string PhoneText => HasPhone ? Phone : "—";

    public string Email => Row.ContactEmail ?? string.Empty;

    public bool HasEmail => Email.Length > 0;

    public decimal Total => Row.Total;

    public string TotalText => Fmt.Money(Row.Total);

    public DateTimeOffset CreatedAt => Row.CreatedAt;

    public string CreatedText => Fmt.DateTime(Row.CreatedAt);

    public ReservationState State { get; } = ReservationText.StateOf(row, now);

    public string StatusText => ReservationText.State(State);

    public string StatusBrush => ReservationText.StateBrush(State);

    public string StatusSoftBrush => StatusBrush + "Soft";

    /// <summary>Reservada (vigente o con el plazo cumplido sin cerrar): se puede cobrar, liberar y avisar.</summary>
    public bool IsReserved => Row.Status == PcBuildStatus.Reserved;

    public bool IsActive => State == ReservationState.Active;

    public bool IsExpired => State is ReservationState.Overdue or ReservationState.Expired;

    /// <summary>Se cobra en la caja una reserva todavía reservada con la cotización vigente (la venta consume la reserva).</summary>
    public bool CanSell => IsReserved && !Row.IsExpired;

    public DateTimeOffset ReservedUntilSort => Row.ReservedUntil ?? DateTimeOffset.MaxValue;

    public string ReservedUntilText => Row.ReservedUntil is { } until ? Fmt.DateTime(until) : "—";

    /// <summary>«vence en 5 h 20 min», «venció hace 2 h», «vendida (F-CM-000123)», «liberada: el cliente desistió».</summary>
    public string ReservedUntilDetail => State switch
    {
        ReservationState.Active or ReservationState.Overdue when Row.ReservedUntil is { } until => FiscalText.Countdown(until, _now),
        ReservationState.Sold => Row.InvoiceNumber is { } invoice ? $"vendida · {invoice}" : "vendida",
        ReservationState.Released => string.IsNullOrWhiteSpace(Row.CancelReason) ? "liberada" : $"liberada: {Row.CancelReason}",
        ReservationState.Expired => "el stock volvió a estar disponible",
        _ => string.Empty,
    };

    /// <summary>Vence en menos de 6 h o ya venció sin cerrar: se resalta.</summary>
    public bool IsUrgent => State == ReservationState.Overdue
                            || State == ReservationState.Active && Row.ReservedUntil is { } until && until - _now < PcBuildItem.ExpiringSoon;

    public string ReservedUntilBrush => State == ReservationState.Overdue ? "Danger" : IsUrgent ? "Warning" : "TextSecondary";

    public string MailText => Mail?.StatusText ?? (HasEmail ? "Sin envío" : "Sin correo");

    public string MailBrush => ReservationText.MailBrush(Mail?.Status);

    public string MailSoftBrush => MailBrush + "Soft";

    public string MailTooltip => Mail is { } m
        ? $"{m.StatusText} · {m.Recipient} · {m.Attempts} de {m.MaxAttempts} intentos" + (m.LastError is { Length: > 0 } error ? $" · {error}" : string.Empty)
        : HasEmail ? "Todavía no se pidió enviar la confirmación" : "La reserva no tiene correo de contacto";

    public bool HasBuyer => Row.BuyerDocumentType is not null && !string.IsNullOrWhiteSpace(Row.BuyerDocumentNumber);

    public string BuyerDocumentText => ReservationText.Document(Row.BuyerDocumentType, Row.BuyerDocumentNumber, Row.BuyerComplement);

    public string BuyerNameText => string.IsNullOrWhiteSpace(Row.BuyerName) ? "—" : Row.BuyerName;
}

/// <summary>V7 · Producto o pieza de una reserva (con su precio congelado).</summary>
public sealed class ReservationLineItem(PcBuildItemView item, bool isCart)
{
    public string Name => item.Name;

    public string Sku => item.Sku;

    public string SlotText => isCart && item.Slot is null ? "Producto" : TechText.Slot(item.Slot);

    public string QuantityText => $"× {item.Quantity}";

    public string UnitPriceText => Fmt.Money(item.UnitPrice);

    public string SubtotalText => Fmt.Money(item.Subtotal);
}

/// <summary>V7 · Fila de la bitácora de la reserva (regla S-04).</summary>
public sealed class ReservationEventItem(PcBuildEventView e)
{
    public string ActionText => TechText.BuildAction(e.Action);

    public string ActionBrush => TechText.BuildActionBrush(e.Action);

    public string Detail => e.Detail;

    public string WhenText => Fmt.DateTime(e.OccurredAt);

    public string User => e.User;
}

/// <summary>V7 · Correo de confirmación de la reserva (cola de correos).</summary>
public sealed class ReservationMailItem(OutgoingMailRow mail)
{
    public OutgoingMailRow Row { get; } = mail;

    public string Recipient => Row.Recipient;

    public string StatusText => Row.StatusText;

    public string StatusBrush => ReservationText.MailBrush(Row.Status);

    public string StatusSoftBrush => StatusBrush + "Soft";

    public string RequestedText => Fmt.DateTime(Row.RequestedAt);

    public string AttemptsText => $"{Row.Attempts} de {Row.MaxAttempts} intentos";

    public string Detail => Row.Status switch
    {
        OutgoingMailStatus.Pending when Row.NextAttemptAt is { } next => $"Próximo intento {Fmt.DateTime(next)}",
        OutgoingMailStatus.Sent when Row.CompletedAt is { } done => $"Enviado el {Fmt.DateTime(done)}",
        _ => Row.LastError is { Length: > 0 } error ? error : string.Empty,
    };

    public bool HasDetail => Detail.Length > 0;
}

/// <summary>V7 · Abrir la pantalla Reservas con una reserva elegida (cola de correos), filtrada por canal (inicio) o con el
/// formulario de una reserva nueva en mostrador abierto (botón del inicio).</summary>
public sealed record ReservationFocus(string? Number = null, PcBuildChannel? Channel = null, bool New = false);

/// <summary>
/// V7 · Reservas: lista con filtros (tipo, canal, estado, vence, sucursal, fechas y búsqueda), detalle con productos, datos de
/// factura, notas, bitácora y correos, y las acciones Vender en caja, Liberar, Reenviar correo, Copiar teléfono, Nueva reserva
/// en mostrador y Exportar CSV. Las reservas por atender (reservadas) van primero, las que vencen antes arriba.
/// </summary>
public sealed class ReservationsViewModel : PageViewModel
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private List<ReservationItem> _items = [];
    private ReservationItem? _selected;
    private PcBuildDetail? _detail;
    private int _detailVersion;
    private bool _loadingDetail;
    private string _search = string.Empty;
    private Choice<PcBuildKind?> _kind;
    private Choice<PcBuildChannel?> _channel;
    private Choice<ReservationState?> _state;
    private Choice<ReservationDue?> _due;
    private Choice<string?> _branch;
    private Choice<PeriodOption?> _period;
    private string _summary = string.Empty;
    private int _visible;
    private string? _focusNumber;

    public ReservationsViewModel(AppServices app)
        : base(app, "reservas", "Reservas", "Carritos y armados reservados de la tienda web y del mostrador", Glyphs.Clock)
    {
        Kinds = [new("Todos los tipos", null), new("Compra (carrito)", PcBuildKind.Cart), new("Armado de PC", PcBuildKind.Build)];
        Channels = [new("Todos los canales", null), new("Web", PcBuildChannel.Web), new("Mostrador", PcBuildChannel.Desktop)];
        States =
        [
            new("Todos los estados", null), new("Reservadas", ReservationState.Active), new("Vencidas por cerrar", ReservationState.Overdue),
            new("Vendidas", ReservationState.Sold), new("Liberadas", ReservationState.Released), new("Vencidas", ReservationState.Expired),
        ];
        Dues = [new("Cualquier vencimiento", null), new("Vencen hoy", ReservationDue.Today), new("Vencen mañana", ReservationDue.Tomorrow),
            new("Vencidas", ReservationDue.Expired)];
        Branches.ReplaceAll([new Choice<string?>("Todas las sucursales", null)]);
        Periods.ReplaceAll(FilterChoices.Periods(Today));
        _kind = Kinds[0];
        _channel = Channels[0];
        _state = States[0];
        _due = Dues[0];
        _branch = Branches[0];
        _period = Periods[0];
        Rows = CollectionViewSource.GetDefaultView(_items);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ApplyFilter();
        };
        SellInPos = new RelayCommand(SellInPos_, () => _selected is { CanSell: true } && CanSellInPos);
        Release = new AsyncRelayCommand(ReleaseAsync, () => CanManage && _selected is { IsReserved: true });
        ResendMail = new AsyncRelayCommand(ResendAsync, () => CanManage && _selected is { IsActive: true });
        CopyPhone = new RelayCommand(CopyPhone_, () => _selected is { HasPhone: true });
        NewReservation = new AsyncRelayCommand(NewReservationAsync, () => CanManage);
        Export = new RelayCommand(ExportCsv, () => _items.Count > 0);
        ClearFilters = new RelayCommand(ClearAllFilters, () => HasFilters);
        CloseDetail = new RelayCommand(() => Selected = null);
    }

    public ICollectionView Rows { get; private set; }

    public IReadOnlyList<Choice<PcBuildKind?>> Kinds { get; }

    public IReadOnlyList<Choice<PcBuildChannel?>> Channels { get; }

    public IReadOnlyList<Choice<ReservationState?>> States { get; }

    public IReadOnlyList<Choice<ReservationDue?>> Dues { get; }

    public BulkObservableCollection<Choice<string?>> Branches { get; } = [];

    public BulkObservableCollection<Choice<PeriodOption?>> Periods { get; } = [];

    /// <summary>El filtro de sucursal solo se muestra si la lista tiene reservas de más de una sucursal.</summary>
    public bool HasBranches => Branches.Count > 2;

    public bool CanManage => App.Session.Can(PermissionCodes.PcBuildManage);

    public bool CanSellInPos => App.Session.Can(PermissionCodes.PosOperate);

    // ------------------------------------------------------------------------------------------------ filtros
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

    public Choice<PcBuildKind?> Kind { get => _kind; set => SetFilter(ref _kind, value ?? Kinds[0]); }

    public Choice<PcBuildChannel?> Channel { get => _channel; set => SetFilter(ref _channel, value ?? Channels[0]); }

    public Choice<ReservationState?> State { get => _state; set => SetFilter(ref _state, value ?? States[0]); }

    public Choice<ReservationDue?> Due { get => _due; set => SetFilter(ref _due, value ?? Dues[0]); }

    public Choice<string?> Branch { get => _branch; set => SetFilter(ref _branch, value ?? Branches[0]); }

    public Choice<PeriodOption?> Period { get => _period; set => SetFilter(ref _period, value ?? Periods[0]); }

    public bool HasFilters => _search.Trim().Length > 0 || _kind.Value is not null || _channel.Value is not null || _state.Value is not null
                              || _due.Value is not null || _branch.Value is not null || _period.Value is not null;

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public int VisibleCount { get => _visible; private set => Set(ref _visible, value); }

    public bool NoReservations => HasLoaded && _items.Count == 0;

    public bool NoMatches => HasLoaded && _items.Count > 0 && VisibleCount == 0;

    public KpiCard ActiveKpi { get; } = new("Reservas vigentes", Glyphs.Clock, "Brand", "BrandSoft");

    public KpiCard DueKpi { get; } = new("Vencen hoy o ya vencieron", Glyphs.Warning, "Warning", "WarningSoft");

    public KpiCard WebKpi { get; } = new("Desde la tienda web", Glyphs.Globe, "Info", "InfoSoft");

    public KpiCard MailKpi { get; } = new("Correos de confirmación", Glyphs.Mail, "Success", "SuccessSoft");

    // ------------------------------------------------------------------------------------------------ detalle
    public ReservationItem? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                OnPropertiesChanged(nameof(HasSelection), nameof(SellHint));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                _ = LoadDetailAsync(value);
            }
        }
    }

    public bool HasSelection => _selected is not null;

    public PcBuildDetail? Detail { get => _detail; private set => Set(ref _detail, value); }

    public bool IsLoadingDetail { get => _loadingDetail; private set => Set(ref _loadingDetail, value); }

    public BulkObservableCollection<ReservationLineItem> Lines { get; } = [];

    public BulkObservableCollection<ReservationEventItem> History { get; } = [];

    public BulkObservableCollection<ReservationMailItem> Mails { get; } = [];

    public bool HasMails => Mails.Count > 0;

    /// <summary>Por qué «Vender en caja» no está disponible para la reserva elegida (vacío si lo está).</summary>
    public string SellHint => _selected switch
    {
        null => string.Empty,
        { CanSell: true } when !CanSellInPos => "Su rol no opera la caja: pida a un cajero que la cobre.",
        { CanSell: true } => string.Empty,
        { IsReserved: true } => "La cotización de la reserva venció: vuelva a cotizarla.",
        _ => $"La reserva está {_selected.StatusText.ToLower(Fmt.Culture)}: ya no se cobra.",
    };

    // ------------------------------------------------------------------------------------------------ comandos
    public RelayCommand SellInPos { get; }

    public AsyncRelayCommand Release { get; }

    public AsyncRelayCommand ResendMail { get; }

    public RelayCommand CopyPhone { get; }

    public AsyncRelayCommand NewReservation { get; }

    public RelayCommand Export { get; }

    public RelayCommand ClearFilters { get; }

    public RelayCommand CloseDetail { get; }

    private DateOnly Today => DateOnly.FromDateTime(App.Now.ToLocalTime().DateTime);

    /// <summary>Desde la cola de correos o el inicio: la reserva elegida o el canal.</summary>
    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is not ReservationFocus focus)
        {
            return;
        }
        if (focus.Channel is { } channel)
        {
            ClearAllFilters();
            Channel = Channels.First(c => c.Value == channel);
            State = States.First(s => s.Value == ReservationState.Active);
        }
        if (focus.Number is { } number)
        {
            _focusNumber = number;
            if (HasLoaded && !IsBusy)
            {
                FocusPending();
            }
        }
        if (focus.New && CanManage)
        {
            // El formulario se abre cuando termina la carga de la pantalla (lo pide el despachador, después de navegar)
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
            {
                while (IsBusy)
                {
                    await Task.Delay(50);
                }
                await NewReservation.ExecuteAsync();
            }, DispatcherPriority.Background);
        }
    }

    protected override async Task LoadCoreAsync(bool force)
    {
        var now = App.Now;
        var rows = await App.SendAsync(new GetPcBuildsQuery());
        IReadOnlyList<OutgoingMailRow> mails = [];
        try
        {
            mails = await App.SendAsync(new GetOutgoingMailsQuery(Take: 500));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            // Sin la cola la lista sigue (la columna del correo queda «Sin envío»)
            System.Diagnostics.Trace.TraceWarning("M-INV · cola de correos: {0}", ex.Message);
        }
        var latest = mails.GroupBy(m => m.Reservation, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.RequestedAt).First(), StringComparer.OrdinalIgnoreCase);
        // Una reserva es un carrito (siempre) o un armado que llegó a reservarse (tiene «reservado hasta»)
        _items = rows.Where(r => r.Kind == PcBuildKind.Cart || r.ReservedUntil is not null)
            .Select(r => new ReservationItem(r, latest.GetValueOrDefault(r.Number), now)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = Matches;
        if (Rows is ListCollectionView list)
        {
            list.CustomSort = new AttentionOrder();
        }
        OnPropertyChanged(nameof(Rows));

        Branches.ReplaceAll(FilterChoices.Of("Todas las sucursales", _items.Select(i => i.BranchCode)));
        _branch = FilterChoices.Keep(Branches, _branch);
        Periods.ReplaceAll(FilterChoices.Periods(Today));
        _period = FilterChoices.Keep(Periods, _period);
        OnPropertiesChanged(nameof(Branch), nameof(Period), nameof(HasBranches));

        var active = _items.Where(i => i.IsActive).ToList();
        ActiveKpi.Value = active.Count.ToString("N0", Fmt.Culture);
        ActiveKpi.Detail = active.Count == 0 ? "Ninguna por recoger" : $"{Fmt.Money(active.Sum(i => i.Total))} por cobrar";
        var dueToday = _items.Count(i => i.IsReserved && IsDue(i, ReservationDue.Today));
        var overdue = _items.Count(i => i.State == ReservationState.Overdue);
        DueKpi.Value = (dueToday + overdue).ToString("N0", Fmt.Culture);
        DueKpi.Detail = overdue > 0 ? $"{overdue} vencida{(overdue == 1 ? "" : "s")} sin cerrar" : dueToday > 0 ? "Avise al cliente" : "Nada urgente";
        var web = active.Count(i => i.IsWeb);
        WebKpi.Value = web.ToString("N0", Fmt.Culture);
        WebKpi.Detail = $"{active.Count - web} del mostrador";
        var queued = mails.Count(m => m.Status == OutgoingMailStatus.Pending);
        var failed = mails.Count(m => m.Status == OutgoingMailStatus.Exhausted);
        MailKpi.Value = mails.Count(m => m.Status == OutgoingMailStatus.Sent).ToString("N0", Fmt.Culture) + " enviados";
        MailKpi.Detail = failed > 0 ? $"{failed} no se pudieron enviar · {queued} en cola" : $"{queued} en cola";

        ApplyFilter();
        if (_focusNumber is not null)
        {
            FocusPending();
        }
        else if (_selected is { } previous)
        {
            // Después de recargar, la misma reserva sigue elegida con sus datos nuevos
            _selected = null;
            Selected = _items.FirstOrDefault(i => i.Number == previous.Number);
        }
    }

    private void FocusPending()
    {
        var number = _focusNumber;
        _focusNumber = null;
        if (number is null)
        {
            return;
        }
        if (_items.FirstOrDefault(i => string.Equals(i.Number, number, StringComparison.OrdinalIgnoreCase)) is { } item)
        {
            if (!Rows.Cast<ReservationItem>().Contains(item))
            {
                ClearAllFilters();
            }
            Selected = item;
        }
        else
        {
            App.Notify.Info("Reserva fuera de la lista", $"{number} no está entre las reservas de sus sucursales.");
        }
    }

    private void SetFilter<T>(ref Choice<T> field, Choice<T> value)
    {
        if (!EqualityComparer<Choice<T>>.Default.Equals(field, value))
        {
            field = value;
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(Channel));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(Due));
            OnPropertyChanged(nameof(Branch));
            OnPropertyChanged(nameof(Period));
            ApplyFilter();
        }
    }

    private bool Matches(object o)
    {
        if (o is not ReservationItem r)
        {
            return false;
        }
        if (_kind.Value is { } kind && r.Row.Kind != kind)
        {
            return false;
        }
        if (_channel.Value is { } channel && r.Row.Channel != channel)
        {
            return false;
        }
        if (_state.Value is { } state && r.State != state)
        {
            return false;
        }
        if (_due.Value is { } due && !IsDue(r, due))
        {
            return false;
        }
        if (_branch.Value is { } branch && r.BranchCode != branch)
        {
            return false;
        }
        if (_period.Value is { } period)
        {
            var created = DateOnly.FromDateTime(r.CreatedAt.ToLocalTime().DateTime);
            if (created < period.From || created > period.To)
            {
                return false;
            }
        }
        var q = _search.Trim();
        return q.Length == 0
               || r.Number.Contains(q, StringComparison.OrdinalIgnoreCase)
               || FilterChoices.Contains(r.Customer, q)
               || FilterChoices.Contains(r.Row.ContactName, q)
               || FilterChoices.Contains(r.Name, q)
               || r.Phone.Replace(" ", string.Empty, StringComparison.Ordinal).Contains(q.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal)
               || r.Email.Contains(q, StringComparison.OrdinalIgnoreCase)
               || (r.Row.BuyerDocumentNumber?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>«Vence hoy / mañana» (reservas todavía reservadas, por la fecha local de su vencimiento) o «vencidas» (con el
    /// plazo cumplido, cerradas o no).</summary>
    private bool IsDue(ReservationItem r, ReservationDue due)
    {
        if (due == ReservationDue.Expired)
        {
            return r.IsExpired;
        }
        if (!r.IsActive || r.Row.ReservedUntil is not { } until)
        {
            return false;
        }
        var day = DateOnly.FromDateTime(until.ToLocalTime().DateTime);
        return due == ReservationDue.Today ? day == Today : day == Today.AddDays(1);
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        VisibleCount = Rows.Cast<object>().Count();
        Summary = VisibleCount == _items.Count
            ? (_items.Count == 1 ? "1 reserva" : $"{_items.Count} reservas")
            : $"{VisibleCount} de {_items.Count} reservas";
        OnPropertiesChanged(nameof(HasFilters), nameof(NoReservations), nameof(NoMatches));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void ClearAllFilters()
    {
        _search = string.Empty;
        _kind = Kinds[0];
        _channel = Channels[0];
        _state = States[0];
        _due = Dues[0];
        _branch = Branches[0];
        _period = Periods[0];
        OnPropertiesChanged(nameof(Search), nameof(Kind), nameof(Channel), nameof(State), nameof(Due), nameof(Branch), nameof(Period));
        ApplyFilter();
    }

    /// <summary>Productos, bitácora y correos de la reserva elegida (una lectura a la vez; la última gana).</summary>
    private async Task LoadDetailAsync(ReservationItem? item)
    {
        var version = ++_detailVersion;
        if (item is null)
        {
            Detail = null;
            Lines.ReplaceAll([]);
            History.ReplaceAll([]);
            Mails.ReplaceAll([]);
            OnPropertyChanged(nameof(HasMails));
            return;
        }
        IsLoadingDetail = true;
        try
        {
            var detail = await App.SendAsync(new GetPcBuildQuery(item.Number));
            IReadOnlyList<OutgoingMailRow> mails = [];
            try
            {
                mails = await App.SendAsync(new GetOutgoingMailsQuery(Number: item.Number, Take: 50));
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                System.Diagnostics.Trace.TraceWarning("M-INV · correos de {0}: {1}", item.Number, ex.Message);
            }
            if (version != _detailVersion)
            {
                return;
            }
            Detail = detail;
            var cart = detail.Build.Kind == PcBuildKind.Cart;
            Lines.ReplaceAll((detail.QuotedItems.Count > 0 ? detail.QuotedItems : detail.Check.Items).Select(i => new ReservationLineItem(i, cart)));
            History.ReplaceAll((detail.History ?? []).OrderByDescending(e => e.OccurredAt).Select(e => new ReservationEventItem(e)));
            Mails.ReplaceAll(mails.Select(m => new ReservationMailItem(m)));
            OnPropertyChanged(nameof(HasMails));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            if (version == _detailVersion)
            {
                App.Notify.Error($"No se pudo abrir la reserva {item.Number}", AppServices.Describe(ex));
            }
        }
        finally
        {
            if (version == _detailVersion)
            {
                IsLoadingDetail = false;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ acciones
    /// <summary>La caja carga la reserva (como «Vender en caja» del armador): la venta consume la reserva (regla S-04) y, si el
    /// cajero no cambia el comprador, factura con los datos que dejó quien reservó.</summary>
    private void SellInPos_()
    {
        if (_selected is not { CanSell: true } item)
        {
            return;
        }
        App.Notify.Info("La venta consume la reserva",
            $"Al cobrar {item.Number} en la caja, las unidades reservadas salen del stock con la venta (no se descuentan dos veces).");
        App.Navigator.Navigate("pos", new PcBuildToSell(item.Number));
    }

    private async Task ReleaseAsync()
    {
        if (_selected is not { IsReserved: true } item)
        {
            return;
        }
        var reason = await App.Dialogs.PromptAsync($"Liberar la reserva {item.Number}",
            $"El stock reservado para {item.Customer} ({item.TotalText}) vuelve a estar disponible y la reserva queda liberada con el motivo. Esto no se deshace.",
            "Motivo", ["El cliente desistió", "El cliente no pasó a recoger", "Sin respuesta del cliente", "Reservado por error"], "Liberar reserva",
            isDanger: true, placeholder: "Escriba o elija el motivo");
        if (reason is null)
        {
            return;
        }
        if (reason.Length == 0)
        {
            App.Notify.Warning("Falta el motivo", "Indique por qué se libera la reserva: queda en su bitácora.");
            return;
        }
        try
        {
            var row = await App.SendAsync(new ReleasePcBuildReservationCommand(item.Number, reason));
            App.Notify.Success($"Reserva {row.Number} liberada", "El stock volvió a estar disponible en la sucursal.");
            App.Data.Invalidate();
            await LoadAsync(force: true);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo liberar la reserva", AppServices.Describe(ex));
        }
    }

    /// <summary>Encola otra confirmación para el correo de la reserva o para otro que indique el personal (el despachador la
    /// envía después; los pendientes de esa reserva se reemplazan).</summary>
    private async Task ResendAsync()
    {
        if (_selected is not { IsActive: true } item)
        {
            return;
        }
        var email = await App.Dialogs.PromptAsync($"Reenviar la confirmación de {item.Number}",
            "Se envía otra vez el correo con el número, los productos y el vencimiento de la reserva. Puede enviarlo a otro correo (por ejemplo, " +
            "si el cliente escribió mal el suyo).",
            "Correo del cliente", item.HasEmail ? [item.Email] : [], "Reenviar correo", item.Email, "nombre@correo.com", glyph: Glyphs.Mail);
        if (email is null)
        {
            return;
        }
        if (email.Length == 0 && !item.HasEmail)
        {
            App.Notify.Warning("Falta el correo", "La reserva no tiene correo de contacto: escriba a qué correo enviar la confirmación.");
            return;
        }
        try
        {
            var mail = await App.SendAsync(new ResendReservationMailCommand(item.Number,
                email.Length == 0 || string.Equals(email, item.Email, StringComparison.OrdinalIgnoreCase) ? null : email));
            App.Notify.Success("Correo en cola", $"La confirmación de {item.Number} sale en unos minutos a {mail.Recipient}.");
            await LoadAsync(force: true);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo reenviar el correo", AppServices.Describe(ex));
        }
    }

    private void CopyPhone_()
    {
        if (_selected is not { HasPhone: true } item)
        {
            return;
        }
        if (ClipboardText.TrySet(item.Phone))
        {
            App.Notify.Info("Teléfono copiado", $"{item.Customer} · {item.Phone}");
        }
        else
        {
            App.Notify.Warning("No se pudo copiar", "Otro programa está usando el portapapeles: intente de nuevo.");
        }
    }

    /// <summary>Carrito de mostrador (ReserveCartCommand): cliente, teléfono, correo opcional, productos con buscador y días para
    /// recoger. El stock se reserva todo o nada en la sucursal activa.</summary>
    private async Task NewReservationAsync()
    {
        if (App.Session.Access.Active is null)
        {
            App.Notify.Warning("Elija una sucursal", "La reserva se hace en la sucursal activa: elíjala arriba (no «Todas las sucursales»).");
            return;
        }
        var dialog = new CounterReservationDialog(App);
        await dialog.LoadAsync();
        if (!await App.Dialogs.ShowAsync(dialog) || dialog.Result is not { } row)
        {
            return;
        }
        App.Notify.Success($"Reserva {row.Number} creada",
            $"{Fmt.Qty(row.Reserved)} unidad{(row.Reserved == 1 ? "" : "es")} reservada{(row.Reserved == 1 ? "" : "s")} hasta el {Fmt.DateTime(row.ReservedUntil!.Value)}"
            + (string.IsNullOrWhiteSpace(row.ContactEmail) ? string.Empty : " · la confirmación sale por correo"));
        App.Data.Invalidate();
        _focusNumber = row.Number;
        await LoadAsync(force: true);
    }

    /// <summary>Lo que se exporta: las filas visibles con sus filtros.</summary>
    public CsvTable ExportTable() => CsvTable.Of(
        ["Número", "Tipo", "Canal", "Sucursal", "Cliente", "Contacto", "Teléfono", "Correo", "Productos", "Total", "Reservado hasta", "Estado", "Correo de confirmación",
            "Documento para la factura", "Nombre para la factura", "Creada", "Notas"],
        Rows.Cast<ReservationItem>(),
        r => [r.Number, r.KindText, r.ChannelText, r.BranchCode, r.Customer, r.Row.ContactName, r.Phone, r.Email, r.Row.Items, r.Total, r.Row.ReservedUntil,
            r.StatusText, r.MailText, r.HasBuyer ? r.BuyerDocumentText : null, r.Row.BuyerName, r.CreatedAt, r.Row.Notes]);

    private void ExportCsv() => App.ExportCsv(App.CsvName("reservas"), "Reservas", ExportTable());

    /// <summary>Primero las reservadas (las que vencen antes arriba), después las demás de la más nueva a la más vieja.</summary>
    private sealed class AttentionOrder : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not ReservationItem a || y is not ReservationItem b)
            {
                return 0;
            }
            var group = (a.IsReserved ? 0 : 1).CompareTo(b.IsReserved ? 0 : 1);
            if (group != 0)
            {
                return group;
            }
            return a.IsReserved ? a.ReservedUntilSort.CompareTo(b.ReservedUntilSort) : b.CreatedAt.CompareTo(a.CreatedAt);
        }
    }
}
