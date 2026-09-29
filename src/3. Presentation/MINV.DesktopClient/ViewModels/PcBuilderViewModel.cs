using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MINV.Application.Catalog;
using MINV.Application.Partners;
using MINV.Application.Tech;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Sales;
using MINV.Hardware;
using MINV.Hardware.EscPos;

namespace MINV.DesktopClient.ViewModels;

/// <summary>V4.2 · Pieza elegida en una ranura del armado.</summary>
public sealed class PcPartItem : ObservableObject
{
    private int _quantity;

    public PcPartItem(PcSlot slot, string sku, string name, decimal unitPrice, decimal stock, IReadOnlyList<string> keySpecs, ImageSource? image, int quantity,
        bool isMulti, Action changed)
    {
        Slot = slot;
        Sku = sku;
        Name = name;
        UnitPrice = unitPrice;
        Stock = stock;
        KeySpecs = string.Join(" · ", keySpecs);
        Image = image;
        _quantity = quantity;
        IsMulti = isMulti;
        Increase = new RelayCommand(() =>
        {
            Quantity = Math.Min(PcBuild.MaxQuantity, _quantity + 1);
            changed();
        }, () => IsMulti && _quantity < PcBuild.MaxQuantity);
        Decrease = new RelayCommand(() =>
        {
            Quantity = Math.Max(1, _quantity - 1);
            changed();
        }, () => IsMulti && _quantity > 1);
    }

    public PcSlot Slot { get; }

    public string Sku { get; }

    public string Name { get; }

    public decimal UnitPrice { get; private set; }

    public decimal Stock { get; private set; }

    public string KeySpecs { get; }

    public bool HasKeySpecs => KeySpecs.Length > 0;

    public ImageSource? Image { get; }

    public bool IsMulti { get; }

    public int Quantity
    {
        get => _quantity;
        private set
        {
            if (Set(ref _quantity, value))
            {
                OnPropertiesChanged(nameof(SubtotalText), nameof(QuantityText), nameof(ExceedsStock));
            }
        }
    }

    public string QuantityText => $"× {_quantity}";

    public string PriceText => Fmt.Money(UnitPrice);

    public string SubtotalText => Fmt.Money(UnitPrice * _quantity);

    public string StockText => Stock <= 0 ? "Sin stock en la sucursal" : $"Stock {Fmt.Qty(Stock)}";

    /// <summary>V7 · El aviso de stock solo vale mientras se arma (un armado cotizado, reservado o vendido ya descontó o apartó sus
    /// piezas: sus propias unidades reservadas no son «falta de stock»).</summary>
    public bool IsEditable { get; set; } = true;

    public bool ExceedsStock => IsEditable && _quantity > Stock;

    public RelayCommand Increase { get; }

    public RelayCommand Decrease { get; }

    /// <summary>Precio y stock vigentes (la revisión los devuelve de la lista de precios y de la sucursal).</summary>
    public void Refresh(decimal unitPrice, decimal stock)
    {
        UnitPrice = unitPrice;
        Stock = stock;
        OnPropertiesChanged(nameof(UnitPrice), nameof(PriceText), nameof(SubtotalText), nameof(Stock), nameof(StockText), nameof(ExceedsStock));
    }

    public PcBuildItemInput ToInput() => new(Slot, Sku, _quantity);
}

/// <summary>V4.2 · Ranura del armador: procesador, placa, RAM… con sus piezas.</summary>
public sealed class PcSlotItem : ObservableObject
{
    private bool _selected;

    public PcSlotItem(PcSlot slot)
    {
        Slot = slot;
        Parts.CollectionChanged += (_, _) => OnPropertiesChanged(nameof(IsEmpty), nameof(HasParts), nameof(Summary));
    }

    public PcSlot Slot { get; }

    public string Title => TechText.Slot(Slot);

    public string Glyph => TechText.SlotGlyph(Slot);

    public bool IsRequired => PcCompatibility.Required.Contains(Slot);

    public bool IsMulti => PcBuild.MultiSlots.Contains(Slot);

    /// <summary>Monitor, periféricos, software y servicios se eligen por categoría (no tienen especificación que los identifique).</summary>
    public bool NeedsCategory => Slot is PcSlot.Monitor or PcSlot.Peripheral or PcSlot.Software or PcSlot.Service;

    public ObservableCollection<PcPartItem> Parts { get; } = [];

    public bool IsEmpty => Parts.Count == 0;

    public bool HasParts => Parts.Count > 0;

    public string Summary => Parts.Count == 0
        ? (IsRequired ? "Obligatoria · elija una pieza" : "Opcional")
        : Parts.Sum(p => p.Quantity) is var n && n == 1 ? "1 pieza" : $"{n} piezas";

    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }

    public void OnSummaryChanged() => OnPropertyChanged(nameof(Summary));
}

/// <summary>V4.2 · Candidato de una ranura (compatible con lo elegido o atenuado con el motivo), con sus plataformas (opciones
/// de la especificación «plataforma», regla T-07) para los periféricos, el software y los accesorios.</summary>
public sealed class PcCandidateItem(PcBuildCandidate candidate, ImageSource? image, IReadOnlyList<string>? platforms = null)
{
    public PcBuildCandidate Candidate { get; } = candidate;

    public IReadOnlyList<string> Platforms { get; } = platforms ?? [];

    public bool HasPlatforms => Platforms.Count > 0;

    public string Sku => Candidate.Sku;

    public string Name => Candidate.Name;

    public string Brand => Candidate.Brand ?? string.Empty;

    /// <summary>SKU y marca (sin el separador colgando si el producto no tiene marca).</summary>
    public string SkuLine => string.IsNullOrWhiteSpace(Candidate.Brand) ? Candidate.Sku : $"{Candidate.Sku} · {Candidate.Brand}";

    public ImageSource? Image { get; } = image;

    public bool IsCompatible => Candidate.IsCompatible;

    public bool IsIncompatible => !Candidate.IsCompatible;

    public string Reason => Candidate.Reason ?? string.Empty;

    public string PriceText => Fmt.Money(Candidate.Price);

    public string StockText => Candidate.Stock <= 0 ? "Sin stock" : $"Stock {Fmt.Qty(Candidate.Stock)}";

    public bool IsOut => Candidate.Stock <= 0;

    public string KeySpecs => string.Join(" · ", Candidate.KeySpecs);
}

/// <summary>V4.2 · Problema de compatibilidad (error o aviso) del panel en vivo.</summary>
public sealed class PcIssueItem(PcIssue issue)
{
    public PcIssue Issue { get; } = issue;

    public string Message => Issue.Message;

    public bool IsError => Issue.IsError;

    public string Glyph => Issue.IsError ? Glyphs.Error : Glyphs.Warning;

    public string Brush => Issue.IsError ? "Danger" : "Warning";

    public string SoftBrush => Brush + "Soft";
}

/// <summary>V4.2 · Fila de la lista de cotizaciones del armador. V6: canal (insignia «Web»), contacto (teléfono solo con
/// <c>sales.pcbuild.manage</c>, regla S-06), «Reservado hasta» con resaltado (vence en menos de 6 h o ya venció) y publicado.</summary>
public sealed class PcBuildItem(PcBuildRow row, DateTimeOffset now)
{
    /// <summary>Una reserva que vence dentro de este plazo se resalta (queda poco tiempo para que el cliente pase a cobrarla).</summary>
    public static readonly TimeSpan ExpiringSoon = TimeSpan.FromHours(6);

    public PcBuildRow Row { get; } = row;

    public string Number => Row.Number;

    public string Name => Row.Name;

    public string Customer => Row.Customer ?? (HasContact ? Row.ContactName! : "Sin cliente");

    public string StatusText => TechText.BuildStatus(Row.Status, Row.IsExpired);

    public string StatusBrush => TechText.BuildBrush(Row.Status, Row.IsExpired);

    public string StatusSoftBrush => StatusBrush + "Soft";

    public decimal Total => Row.Total;

    public string TotalText => Fmt.Money(Row.Total);

    public DateTimeOffset CreatedAt => Row.CreatedAt;

    public string CreatedText => Fmt.DateTime(Row.CreatedAt);

    public string ValidText => Row.Status is PcBuildStatus.Quoted or PcBuildStatus.Reserved
        ? (Row.IsExpired ? $"Venció el {Fmt.Date(Row.ValidUntil)}" : $"Vigente hasta {Fmt.Date(Row.ValidUntil)}")
        : Row.InvoiceNumber is { } i ? $"Venta {i}" : "—";

    /// <summary>V7 · Carrito (RES-…) o armado de PC (ARM-…).</summary>
    public bool IsCart => Row.Kind == PcBuildKind.Cart;

    public string KindText => IsCart ? "Reserva de compra" : "Armado de PC";

    public string ItemsText => IsCart
        ? (Row.Items == 1 ? "1 producto" : $"{Row.Items} productos")
        : (Row.Items == 1 ? "1 pieza" : $"{Row.Items} piezas");

    public string CompatibilityText => Row.IsCompatible ? "Compatible" : Row.QuotedWithErrors ? "Con errores aceptados" : "Con errores";

    public string CompatibilityBrush => Row.IsCompatible ? "Success" : "Danger";

    /// <summary>V6 · Se cobra en la caja una cotización o una reserva vigentes (la venta consume la reserva, regla S-04).</summary>
    public bool CanSell => Row.Status is PcBuildStatus.Quoted or PcBuildStatus.Reserved && !Row.IsExpired;

    // ------------------------------------------------------------------------------------------------ V6 · canal y contacto
    public bool IsWeb => Row.Channel == PcBuildChannel.Web;

    public string ChannelText => IsWeb ? "Web" : "Escritorio";

    public string ChannelGlyph => IsWeb ? Glyphs.Globe : Glyphs.Monitor;

    public bool HasContact => !string.IsNullOrWhiteSpace(Row.ContactName);

    public string ContactName => Row.ContactName ?? "—";

    /// <summary>Teléfono del cliente web (vacío si la sesión no puede verlo, regla S-06).</summary>
    public string ContactPhone => Row.ContactPhone ?? string.Empty;

    public bool HasPhone => ContactPhone.Length > 0;

    // ------------------------------------------------------------------------------------------------ V6 · reserva
    public bool IsReserved => Row.Status == PcBuildStatus.Reserved;

    public bool IsReservationActive => Row.IsReservationActive(now);

    /// <summary>Reservado y con el plazo cumplido (la cierra el trabajo en segundo plano; mientras tanto se resalta).</summary>
    public bool IsReservationExpired => IsReserved && !IsReservationActive;

    public bool IsExpiringSoon => IsReservationActive && Row.ReservedUntil!.Value - now < ExpiringSoon;

    public bool HighlightReservation => IsReservationExpired || IsExpiringSoon;

    public string ReservedUntilText => !IsReserved || Row.ReservedUntil is not { } until
        ? "—"
        : IsReservationExpired ? $"Venció {Fmt.DateTime(until)}" : IsExpiringSoon ? $"{Fmt.DateTime(until)} · vence pronto" : Fmt.DateTime(until);

    public DateTimeOffset ReservedUntilSort => Row.ReservedUntil ?? DateTimeOffset.MaxValue;

    public string ReservedUntilBrush => IsReservationExpired ? "Danger" : IsExpiringSoon ? "Warning" : "TextSecondary";

    public bool IsPublished => Row.PublishedToWeb;

    public string PublishedText => Row.PublishedToWeb ? "Publicado" : "—";
}

/// <summary>V6 · Cómo abrir el armador desde otra pantalla (la tarjeta «Reservas web activas» del tablero).</summary>
public enum PcBuilderFilter
{
    /// <summary>Pestaña Cotizaciones con el filtro rápido «Reservas web».</summary>
    WebReservations,
}

/// <summary>V6 · Línea de una reserva con la disponibilidad actual de la pieza en la sucursal (lo que hay ADEMÁS de lo reservado).</summary>
public sealed class PcReservationLine(PcBuildItemView item, bool reserved)
{
    public string Slot => TechText.Slot(item.Slot);

    public string Name => item.Name;

    public string Sku => item.Sku;

    public string QuantityText => $"× {item.Quantity}";

    public string AvailabilityText => reserved
        ? item.Stock > 0 ? $"Reservada · {Fmt.Qty(item.Stock)} más disponible{(item.Stock == 1 ? "" : "s")}" : "Reservada · sin más unidades"
        : item.Stock >= item.Quantity ? $"Disponible {Fmt.Qty(item.Stock)}" : item.Stock > 0 ? $"Solo {Fmt.Qty(item.Stock)} disponible{(item.Stock == 1 ? "" : "s")}" : "Sin stock";

    public string AvailabilityBrush => reserved ? "Brand" : item.Stock >= item.Quantity ? "Success" : "Danger";
}

/// <summary>
/// V4.2 · Armador de PC: ranuras (CPU, placa, RAM, GPU, almacenamiento, fuente, gabinete, refrigeración y extras) con los
/// candidatos de la sucursal (los incompatibles atenuados con el motivo), panel de compatibilidad EN VIVO (errores y avisos
/// de <see cref="CheckPcBuildQuery"/>, consumo estimado y fuente recomendada), total, cliente y vigencia; guarda el
/// borrador, cotiza (con confirmación explícita si hay errores, regla T-06), imprime la proforma (ticket, rollo o PDF) y la
/// manda a la caja. La compatibilidad la decide SOLO el dominio (<c>PcCompatibility.Check</c>): aquí no hay reglas.
/// </summary>
public sealed class PcBuilderViewModel : PageViewModel
{
    private static readonly PcSlot[] CoreSlots = [PcSlot.Cpu, PcSlot.Motherboard, PcSlot.Ram, PcSlot.Gpu, PcSlot.Storage, PcSlot.Psu, PcSlot.Case, PcSlot.Cooler];
    private static readonly PcSlot[] ExtraSlots = [PcSlot.Monitor, PcSlot.Peripheral, PcSlot.Software, PcSlot.Service];
    private static readonly Choice<string?> NoCustomer = new("Sin cliente (consumidor final)", null);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IReadOnlyDictionary<string, ImageSource> _images = new Dictionary<string, ImageSource>();
    private IReadOnlyDictionary<string, TechProductRow> _tech = new Dictionary<string, TechProductRow>();
    private List<PcBuildItem> _builds = [];
    private PcSlotItem _slot;
    private string _search = string.Empty;
    private bool _onlyInStock;
    private Choice<string?>? _extraCategory;
    private readonly Dictionary<PcSlot, Choice<string?>?> _extraCategories = [];
    private bool _loadingCandidates;
    private PcBuildCheckView? _check;
    private PcBuildRow? _current;
    private string _name = string.Empty;
    private Choice<string?> _customer = NoCustomer;
    private Choice<int> _validity;
    private string _tab = "build";
    private PcBuildItem? _selectedBuild;
    private Choice<PcBuildStatus?> _statusFilter;
    private bool _onlyWeb;
    private int _candidateVersion;
    private int _checkVersion;

    public PcBuilderViewModel(AppServices app) : base(app, "armador", "Armador de PC", "Arme una PC compatible, cotícela y cóbrela en la caja", Glyphs.Monitor)
    {
        Slots = [.. CoreSlots.Select(s => new PcSlotItem(s))];
        Extras = [.. ExtraSlots.Select(s => new PcSlotItem(s))];
        _slot = Slots[0];
        _slot.IsSelected = true;
        Validities = [new("3 días", 3), new("7 días", 7), new("15 días", 15), new("30 días", 30)];
        _validity = Validities[1];
        Statuses =
        [
            new("Todos los estados", null), new("Borradores", PcBuildStatus.Draft), new("Cotizados", PcBuildStatus.Quoted), new("Reservados", PcBuildStatus.Reserved),
            new("Vendidos", PcBuildStatus.Sold), new("Anulados", PcBuildStatus.Cancelled),
        ];
        _statusFilter = Statuses[0];
        Builds = CollectionViewSource.GetDefaultView(_builds);
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await LoadCandidatesAsync();
        };
        SelectSlot = new RelayCommand<PcSlotItem>(s => _ = SelectSlotAsync(s));
        AddPart = new AsyncRelayCommand<PcCandidateItem>(AddPartAsync, c => CanEdit);
        RemovePart = new AsyncRelayCommand<PcPartItem>(RemovePartAsync, _ => CanEdit);
        NewBuild = new AsyncRelayCommand(NewBuildAsync, () => CanManage);
        SaveDraft = new AsyncRelayCommand(() => SaveAsync(quote: false), () => CanManage && CanEdit && HasParts);
        Quote = new AsyncRelayCommand(() => SaveAsync(quote: true), () => CanManage && CanEdit && HasParts);
        Proforma = new AsyncRelayCommand(ProformaAsync, () => _current is { Status: PcBuildStatus.Quoted or PcBuildStatus.Reserved or PcBuildStatus.Sold });
        SellInPos = new RelayCommand(SellInPos_, () => CanSellCurrent);
        CancelBuild = new AsyncRelayCommand(CancelBuildAsync, () => CanCancel);
        OpenBuild = new AsyncRelayCommand<PcBuildItem>(b => OpenBuildAsync(b.Number));
        // V6 · Reservas y publicación en la tienda web (regla S-08)
        ReserveBuild = new AsyncRelayCommand(ReserveAsync, () => CanReserve);
        ReleaseReservation = new AsyncRelayCommand(ReleaseAsync, () => CanRelease);
        TogglePublish = new AsyncRelayCommand(TogglePublishAsync, () => CanPublish);
        CopyPhone = new RelayCommand(CopyPhone_, () => HasContactPhone);
    }

    public ObservableCollection<PcSlotItem> Slots { get; }

    public ObservableCollection<PcSlotItem> Extras { get; }

    public PcSlotItem SelectedSlot => _slot;

    public BulkObservableCollection<PcCandidateItem> Candidates { get; } = [];

    public BulkObservableCollection<PcIssueItem> Issues { get; } = [];

    public BulkObservableCollection<Choice<string?>> Customers { get; } = [];

    public BulkObservableCollection<Choice<string?>> Categories { get; } = [];

    public IReadOnlyList<Choice<int>> Validities { get; }

    public IReadOnlyList<Choice<PcBuildStatus?>> Statuses { get; }

    public ICollectionView Builds { get; private set; }

    public bool CanManage => App.Session.Can(PermissionCodes.PcBuildManage);

    /// <summary>Se puede cambiar el armado: nuevo o en borrador (una cotización queda congelada).</summary>
    public bool CanEdit => CanManage && (_current is null || _current.Status == PcBuildStatus.Draft);

    public bool IsReadOnly => !CanEdit;

    public bool HasParts => AllSlots.Any(s => s.HasParts);

    public IEnumerable<PcSlotItem> AllSlots => Slots.Concat(Extras);

    // ------------------------------------------------------------------------------------------------ pestañas
    public string Tab
    {
        get => _tab;
        set
        {
            if (Set(ref _tab, value ?? "build"))
            {
                OnPropertiesChanged(nameof(IsBuildTab), nameof(IsQuotesTab));
            }
        }
    }

    public bool IsBuildTab { get => _tab == "build"; set { if (value) { Tab = "build"; } } }

    public bool IsQuotesTab { get => _tab == "quotes"; set { if (value) { Tab = "quotes"; } } }

    public string QuotesTabText => $"Cotizaciones ({_builds.Count})";

    /// <summary>V6 · Filtro rápido «Reservas web»: solo los armados que nacieron en la tienda web (reservas de clientes).</summary>
    public bool OnlyWebReservations
    {
        get => _onlyWeb;
        set
        {
            if (Set(ref _onlyWeb, value))
            {
                Builds.Refresh();
                OnPropertiesChanged(nameof(NoBuildsMatch), nameof(BuildsSummary));
            }
        }
    }

    /// <summary>Hay armados pero ninguno pasa los filtros.</summary>
    public bool NoBuildsMatch => HasLoaded && _builds.Count > 0 && Builds.Cast<object>().Any() == false;

    public string BuildsSummary => Builds.Cast<object>().Count() is var n && n == _builds.Count ? $"{n} armados" : $"{n} de {_builds.Count} armados";

    // ------------------------------------------------------------------------------------------------ candidatos
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

    public bool OnlyInStock
    {
        get => _onlyInStock;
        set
        {
            if (Set(ref _onlyInStock, value))
            {
                _ = LoadCandidatesAsync();
            }
        }
    }

    public Choice<string?>? ExtraCategory
    {
        get => _extraCategory;
        set
        {
            if (Set(ref _extraCategory, value))
            {
                if (_slot.NeedsCategory)
                {
                    _extraCategories[_slot.Slot] = value;   // cada ranura extra recuerda su categoría
                }
                _ = LoadCandidatesAsync();
            }
        }
    }

    public bool NeedsCategory => _slot.NeedsCategory;

    public bool IsLoadingCandidates { get => _loadingCandidates; private set => Set(ref _loadingCandidates, value); }

    public bool NoCandidates => !_loadingCandidates && Candidates.Count == 0;

    public string CandidatesTitle => $"{TechText.Slot(_slot.Slot)}: {(Candidates.Count == 1 ? "1 opción" : $"{Candidates.Count} opciones")}" +
                                     (Candidates.Count > 0 ? $" · {Candidates.Count(c => c.IsCompatible)} {(Candidates.Count(c => c.IsCompatible) == 1 ? "compatible" : "compatibles")}" : string.Empty);

    public string NoCandidatesText => _slot.NeedsCategory && _extraCategory?.Value is null
        ? "Elija la categoría de los productos de esta ranura."
        : "No hay productos para esta ranura en el catálogo técnico (fichas con la especificación de compatibilidad).";

    // ------------------------------------------------------------------------------------------------ revisión en vivo
    public PcBuildCheckView? Check { get => _check; private set => Set(ref _check, value); }

    public bool IsCompatible => _check?.IsCompatible ?? true;

    public int ErrorCount => _check?.Issues.Count(i => i.IsError) ?? 0;

    public int WarningCount => _check?.Issues.Count(i => !i.IsError) ?? 0;

    public string CompatibilityTitle => !HasParts ? "Elija las piezas" : ErrorCount > 0
        ? $"{ErrorCount} error{(ErrorCount == 1 ? "" : "es")} de compatibilidad"
        : WarningCount > 0 ? $"Compatible · {WarningCount} aviso{(WarningCount == 1 ? "" : "s")}" : "Compatible";

    public string CompatibilityBrush => !HasParts ? "Info" : ErrorCount > 0 ? "Danger" : WarningCount > 0 ? "Warning" : "Success";

    public string CompatibilitySoftBrush => CompatibilityBrush + "Soft";

    public string CompatibilityGlyph => !HasParts ? Glyphs.Info : ErrorCount > 0 ? Glyphs.Error : WarningCount > 0 ? Glyphs.Warning : Glyphs.CheckCircle;

    public string DrawText => _check is { } c ? $"{c.EstimatedDrawW} W" : "—";

    public string RecommendedText => _check is { } c ? $"{c.RecommendedPsuW} W" : "—";

    public string PsuText => _check?.PsuW is { } w ? $"{w} W" : "Sin fuente";

    /// <summary>Carga de la fuente (consumo / potencia) para la barra: 0..1.</summary>
    public double PsuLoad => _check is { PsuW: > 0 } c ? Math.Min(1, (double)c.EstimatedDrawW / c.PsuW!.Value) : 0;

    public string PsuLoadText => _check is { PsuW: > 0 } c ? $"Con el consumo estimado, la fuente trabajaría al {Math.Min(999, c.EstimatedDrawW * 100 / c.PsuW!.Value)} % de su potencia" : string.Empty;

    public decimal Total => _current is { Status: not PcBuildStatus.Draft } q ? q.Total : _check?.Total ?? 0;

    public string TotalText => Fmt.Money(Total);

    public string PartsText => AllSlots.Sum(s => s.Parts.Sum(p => p.Quantity)) is var n && n == 1 ? "1 pieza" : $"{n} piezas";

    // ------------------------------------------------------------------------------------------------ cotización
    public PcBuildRow? Current
    {
        get => _current;
        private set
        {
            if (Set(ref _current, value))
            {
                OnPropertiesChanged(nameof(CanEdit), nameof(IsReadOnly), nameof(CurrentTitle), nameof(CurrentStatusText), nameof(CurrentStatusBrush),
                    nameof(CurrentStatusSoftBrush), nameof(HasCurrent), nameof(Total), nameof(TotalText), nameof(CurrentDetail), nameof(CanReserve), nameof(CanRelease),
                    nameof(CanPublish), nameof(CanCancel), nameof(CanSellCurrent), nameof(PublishText), nameof(ShowPublishBeside), nameof(ShowPublishBelow),
                    nameof(IsWebReservation), nameof(HasContact), nameof(ContactName),
                    nameof(ContactPhone), nameof(HasContactPhone), nameof(ContactPhoneText), nameof(ContactEmail), nameof(HasContactEmail), nameof(ContactNotes),
                    nameof(HasContactNotes), nameof(ReservationText), nameof(ReservationBrush), nameof(ReservationSoftBrush), nameof(HasReservationLines),
                    nameof(IsPublished), nameof(ShowReservation));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasCurrent => _current is not null;

    public string CurrentTitle => _current is { } c ? $"{c.Number} · {c.Name}" : "Armado nuevo";

    public string CurrentStatusText => _current is { } c ? TechText.BuildStatus(c.Status, c.IsExpired) : "Sin guardar";

    public string CurrentStatusBrush => _current is { } c ? TechText.BuildBrush(c.Status, c.IsExpired) : "StatusInactive";

    public string CurrentStatusSoftBrush => CurrentStatusBrush + "Soft";

    public string CurrentDetail => _current switch
    {
        null => "Elija las piezas por ranura: el panel revisa la compatibilidad mientras arma.",
        { Status: PcBuildStatus.Quoted, IsExpired: true } c => $"La cotización venció el {Fmt.Date(c.ValidUntil)}: arme una nueva para cotizar otra vez.",
        { Status: PcBuildStatus.Quoted } c => $"Precios congelados hasta el {Fmt.Date(c.ValidUntil)}" + (c.QuotedWithErrors ? " · cotizado con errores aceptados" : "")
                                              + (c.PublishedToWeb ? " · publicado en la tienda web" : ""),
        { Status: PcBuildStatus.Reserved } c when !c.IsReservationActive(App.Now) =>
            $"La reserva venció el {Fmt.DateTime(c.ReservedUntil!.Value)}: el trabajo automático la libera; puede liberarla ahora o cobrarla si el cliente llegó.",
        { Status: PcBuildStatus.Reserved } c => $"Stock reservado hasta el {Fmt.DateTime(c.ReservedUntil!.Value)} · precios congelados hasta el {Fmt.Date(c.ValidUntil)}"
                                                + (c.Channel == PcBuildChannel.Web ? " · reserva hecha por el cliente en la tienda web" : "")
                                                + " · al cobrarla en la caja se consume la reserva",
        { Status: PcBuildStatus.Sold } c => $"Vendido en la venta {c.InvoiceNumber}." + (c.PublishedToWeb ? " Sigue publicado como armado sugerido en la web." : ""),
        { Status: PcBuildStatus.Cancelled, CancelReason: { Length: > 0 } reason } => $"Armado anulado: {reason}.",
        { Status: PcBuildStatus.Cancelled } => "Armado anulado.",
        _ => "Borrador: puede cambiar las piezas y cotizarlo cuando el cliente lo apruebe.",
    };

    // ------------------------------------------------------------------------------------------------ V6 · reserva y tienda web
    /// <summary>Se reserva una cotización vigente (el dominio exige Cotizado y no vencido; aquí solo se muestra el botón).</summary>
    public bool CanReserve => CanManage && _current is { Status: PcBuildStatus.Quoted, IsExpired: false };

    public bool CanRelease => CanManage && _current is { Status: PcBuildStatus.Reserved };

    /// <summary>Solo los armados del escritorio se publican (los de la web son reservas de clientes, regla del dominio). V7: un
    /// carrito nunca se publica (el dominio lo rechaza con pcbuild.publish_kind: el botón ni se muestra).</summary>
    public bool CanPublish => CanManage && _current is { Kind: PcBuildKind.Build, Channel: PcBuildChannel.Desktop,
        Status: PcBuildStatus.Quoted or PcBuildStatus.Reserved or PcBuildStatus.Sold };

    public bool CanCancel => CanManage && _current is { Status: PcBuildStatus.Draft or PcBuildStatus.Quoted };

    public bool CanSellCurrent => _current is { Status: PcBuildStatus.Quoted or PcBuildStatus.Reserved, IsExpired: false } && App.Session.Can(PermissionCodes.PosOperate);

    public bool IsPublished => _current?.PublishedToWeb == true;

    public string PublishText => IsPublished ? "Quitar de la web" : "Publicar en la web";

    /// <summary>El botón de publicar ocupa el lugar de «Anular» cuando el armado ya no se anula (reservado o vendido).</summary>
    public bool ShowPublishBeside => CanPublish && !CanCancel;

    public bool ShowPublishBelow => CanPublish && CanCancel;

    public bool IsWebReservation => _current?.Channel == PcBuildChannel.Web;

    public bool HasContact => !string.IsNullOrWhiteSpace(_current?.ContactName);

    public string ContactName => _current?.ContactName ?? string.Empty;

    public string ContactPhone => _current?.ContactPhone ?? string.Empty;

    public bool HasContactPhone => ContactPhone.Length > 0;

    /// <summary>El teléfono solo lo ve quien gestiona armados (regla S-06); a los demás se les dice por qué no aparece.</summary>
    public string ContactPhoneText => HasContactPhone ? ContactPhone : "Teléfono visible solo con el permiso de gestión de armados";

    public string ContactEmail => _current?.ContactEmail ?? string.Empty;

    public bool HasContactEmail => ContactEmail.Length > 0;

    public string ContactNotes => _current?.Notes ?? string.Empty;

    public bool HasContactNotes => ContactNotes.Length > 0;

    public string ReservationText => _current switch
    {
        { Status: PcBuildStatus.Reserved, ReservedUntil: { } until } c when !c.IsReservationActive(App.Now) => $"Reserva vencida el {Fmt.DateTime(until)}",
        { Status: PcBuildStatus.Reserved, ReservedUntil: { } until } c when until - App.Now < PcBuildItem.ExpiringSoon => $"Reservado hasta {Fmt.DateTime(until)} · vence pronto",
        { Status: PcBuildStatus.Reserved, ReservedUntil: { } until } => $"Reservado hasta {Fmt.DateTime(until)}",
        { Status: PcBuildStatus.Sold } c => $"Vendido · {c.InvoiceNumber}",
        { Status: PcBuildStatus.Cancelled, CancelReason: { Length: > 0 } reason } => $"Liberada: {reason}",
        { Status: PcBuildStatus.Cancelled } => "Liberada",
        _ => string.Empty,
    };

    public string ReservationBrush => _current switch
    {
        { Status: PcBuildStatus.Reserved, ReservedUntil: { } until } c when !c.IsReservationActive(App.Now) => "Danger",
        { Status: PcBuildStatus.Reserved, ReservedUntil: { } until } when until - App.Now < PcBuildItem.ExpiringSoon => "Warning",
        { Status: PcBuildStatus.Reserved } => "Brand",
        { Status: PcBuildStatus.Sold } => "Success",
        _ => "StatusInactive",
    };

    public string ReservationSoftBrush => ReservationBrush + "Soft";

    /// <summary>Piezas del armado abierto con su disponibilidad en la sucursal (detalle de una reserva).</summary>
    public BulkObservableCollection<PcReservationLine> ReservationLines { get; } = [];

    /// <summary>V7 · El bloque de la reserva (vencimiento, contacto y piezas) se muestra también en una reserva hecha en el escritorio,
    /// que no tiene contacto (antes solo aparecía con el contacto de la tienda web).</summary>
    public bool ShowReservation => HasContact || _current?.Status == PcBuildStatus.Reserved;

    public bool HasReservationLines => ShowReservation && ReservationLines.Count > 0;

    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }

    public Choice<string?> Customer { get => _customer; set => Set(ref _customer, value ?? NoCustomer); }

    public Choice<int> Validity { get => _validity; set => Set(ref _validity, value ?? Validities[1]); }

    public Choice<PcBuildStatus?> StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (Set(ref _statusFilter, value ?? Statuses[0]))
            {
                Builds.Refresh();
                OnPropertiesChanged(nameof(NoBuildsMatch), nameof(BuildsSummary));
            }
        }
    }

    public PcBuildItem? SelectedBuild
    {
        get => _selectedBuild;
        set => Set(ref _selectedBuild, value);
    }

    public bool NoBuilds => HasLoaded && _builds.Count == 0;

    public KpiCard QuotedKpi { get; } = new("Cotizaciones vigentes", Glyphs.Report, "Info", "InfoSoft");

    public KpiCard SoldKpi { get; } = new("Armados vendidos", Glyphs.Cart, "Success", "SuccessSoft");

    public KpiCard DraftKpi { get; } = new("Borradores", Glyphs.Clipboard, "Warning", "WarningSoft");

    /// <summary>V6 · Reservas hechas desde la tienda web todavía vigentes (cantidad y total en Bs). V7: solo armados (los carritos se
    /// ven en la pantalla Reservas).</summary>
    public KpiCard WebKpi { get; } = new("Armados reservados en la web", Glyphs.Globe, "Brand", "BrandSoft");

    public RelayCommand<PcSlotItem> SelectSlot { get; }

    public AsyncRelayCommand<PcCandidateItem> AddPart { get; }

    public AsyncRelayCommand<PcPartItem> RemovePart { get; }

    public AsyncRelayCommand NewBuild { get; }

    public AsyncRelayCommand SaveDraft { get; }

    public AsyncRelayCommand Quote { get; }

    public AsyncRelayCommand Proforma { get; }

    public RelayCommand SellInPos { get; }

    public AsyncRelayCommand CancelBuild { get; }

    public AsyncRelayCommand<PcBuildItem> OpenBuild { get; }

    /// <summary>V6 · Reserva el stock de la cotización abierta (pide las horas; 48 por defecto).</summary>
    public AsyncRelayCommand ReserveBuild { get; }

    /// <summary>V6 · Libera la reserva (pide el motivo): el stock vuelve y el armado queda anulado.</summary>
    public AsyncRelayCommand ReleaseReservation { get; }

    /// <summary>V6 · Publica el armado como sugerido en la tienda web o lo retira.</summary>
    public AsyncRelayCommand TogglePublish { get; }

    /// <summary>V6 · Copia el teléfono del cliente web al portapapeles.</summary>
    public RelayCommand CopyPhone { get; }

    /// <summary>V6 · Desde el tablero: la pestaña Cotizaciones con el filtro «Reservas web».</summary>
    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is PcBuilderFilter.WebReservations)
        {
            // V7 · Las reservadas (vigentes o por cerrar), como cuenta la tarjeta; antes mostraba también las vendidas y anuladas
            StatusFilter = Statuses.First(s => s.Value == PcBuildStatus.Reserved);
            OnlyWebReservations = true;
            Tab = "quotes";
        }
    }

    protected override async Task LoadCoreAsync(bool force)
    {
        _images = await App.Images.AllAsync(force);
        _tech = await TechCatalog.LoadAsync(App);
        await LoadBuildsAsync();
        if (Customers.Count == 0)
        {
            try
            {
                var customers = await App.SendAsync(new GetCustomersQuery());
                Customers.ReplaceAll([NoCustomer, .. customers.Customers.Where(c => c.IsActive && c.Code != "CF")
                    .OrderBy(c => c.Name, StringComparer.Create(Fmt.Culture, true)).Select(c => new Choice<string?>($"{c.Name} ({c.Code})", c.Code))]);
                var options = await App.SendAsync(new GetCatalogOptionsQuery());
                Categories.ReplaceAll(options.Categories.Select(c => new Choice<string?>(c.Name, c.Code)));
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                Customers.ReplaceAll([NoCustomer]);
                System.Diagnostics.Trace.TraceWarning("M-INV · armador: {0}", ex.Message);
            }
        }
        await LoadCandidatesAsync();
        await RecheckAsync();
    }

    private async Task LoadBuildsAsync()
    {
        // V7 · Solo armados de PC: los carritos (RES-…) se atienden en la pantalla Reservas (el armador los mostraba como armados)
        var rows = await App.SendAsync(new GetPcBuildsQuery(Kind: PcBuildKind.Build));
        var now = App.Now;
        _builds = rows.Select(r => new PcBuildItem(r, now)).ToList();
        Builds = CollectionViewSource.GetDefaultView(_builds);
        Builds.Filter = o => o is PcBuildItem b && (_statusFilter.Value is not { } s || b.Row.Status == s) && (!_onlyWeb || b.IsWeb);
        Builds.SortDescriptions.Add(new SortDescription(nameof(PcBuildItem.CreatedAt), ListSortDirection.Descending));
        OnPropertiesChanged(nameof(Builds), nameof(NoBuilds), nameof(NoBuildsMatch), nameof(BuildsSummary), nameof(QuotesTabText));
        var valid = rows.Where(r => r.Status is PcBuildStatus.Quoted or PcBuildStatus.Reserved && !r.IsExpired).ToList();
        QuotedKpi.Value = valid.Count.ToString("N0", Fmt.Culture);
        QuotedKpi.Detail = valid.Count == 0 ? "Ninguna vigente" : $"{Fmt.Money(valid.Sum(v => v.Total))} por cobrar";
        var sold = rows.Where(r => r.Status == PcBuildStatus.Sold).ToList();
        SoldKpi.Value = sold.Count.ToString("N0", Fmt.Culture);
        SoldKpi.Detail = sold.Count == 0 ? "Todavía ninguno" : Fmt.Money(sold.Sum(s => s.Total));
        DraftKpi.Value = rows.Count(r => r.Status == PcBuildStatus.Draft).ToString("N0", Fmt.Culture);
        DraftKpi.Detail = "Por cotizar";
        // V6 · Reservas web vigentes (reservadas y no vencidas) y cuántas vencen pronto
        var web = _builds.Where(b => b.IsWeb && b.IsReservationActive).ToList();
        var soon = web.Count(b => b.IsExpiringSoon);
        WebKpi.Value = web.Count.ToString("N0", Fmt.Culture);
        WebKpi.Detail = web.Count == 0
            ? "Ninguna reserva vigente"
            : $"{Fmt.Money(web.Sum(b => b.Total))} reservados" + (soon > 0 ? $" · {soon} vence{(soon == 1 ? "" : "n")} en menos de {PcBuildItem.ExpiringSoon.TotalHours:0} h" : string.Empty);
    }

    private async Task SelectSlotAsync(PcSlotItem slot)
    {
        foreach (var s in AllSlots)
        {
            s.IsSelected = ReferenceEquals(s, slot);
        }
        _slot = slot;
        _search = string.Empty;
        OnPropertiesChanged(nameof(SelectedSlot), nameof(Search), nameof(NeedsCategory));
        if (slot.NeedsCategory)
        {
            // Cada ranura extra tiene su categoría (la de monitores no sirve para periféricos): la elegida antes o, la primera
            // vez, la que se parece al nombre de la ranura (el usuario la cambia en la lista)
            if (!_extraCategories.TryGetValue(slot.Slot, out var category))
            {
                category = Categories.FirstOrDefault(c => c.Label.Contains(TechText.Slot(slot.Slot)[..4], StringComparison.OrdinalIgnoreCase));
                _extraCategories[slot.Slot] = category;
            }
            _extraCategory = category;
            OnPropertyChanged(nameof(ExtraCategory));
        }
        await LoadCandidatesAsync();
    }

    /// <summary>Candidatos de la ranura elegida, marcados compatibles o no contra lo que ya se eligió (lo decide el dominio).</summary>
    private async Task LoadCandidatesAsync()
    {
        var version = ++_candidateVersion;
        var slot = _slot;
        if (slot.NeedsCategory && _extraCategory?.Value is null)
        {
            Candidates.ReplaceAll([]);
            OnPropertiesChanged(nameof(NoCandidates), nameof(CandidatesTitle), nameof(NoCandidatesText));
            return;
        }
        IsLoadingCandidates = true;
        try
        {
            var rows = await App.SendAsync(new GetPcBuildCandidatesQuery(slot.Slot, Inputs(), string.IsNullOrWhiteSpace(_search) ? null : _search.Trim(),
                _onlyInStock, slot.NeedsCategory ? _extraCategory?.Value : null));
            if (version == _candidateVersion)
            {
                Candidates.ReplaceAll(rows.Take(200).Select(r => new PcCandidateItem(r, _images.GetValueOrDefault(r.Sku), _tech.GetValueOrDefault(r.Sku)?.Platforms)));
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            if (version == _candidateVersion)
            {
                Candidates.ReplaceAll([]);
                App.Notify.Warning("No se pudieron leer los candidatos", AppServices.Describe(ex));
            }
        }
        finally
        {
            if (version == _candidateVersion)
            {
                IsLoadingCandidates = false;
                OnPropertiesChanged(nameof(NoCandidates), nameof(CandidatesTitle), nameof(NoCandidatesText));
            }
        }
    }

    private List<PcBuildItemInput> Inputs() => AllSlots.SelectMany(s => s.Parts).Select(p => p.ToInput()).ToList();

    private async Task AddPartAsync(PcCandidateItem candidate)
    {
        var slot = _slot;
        if (!slot.IsMulti)
        {
            slot.Parts.Clear();
        }
        else if (slot.Parts.FirstOrDefault(p => p.Sku == candidate.Sku) is { } existing)
        {
            existing.Increase.Execute(null);
            return;
        }
        slot.Parts.Add(new PcPartItem(slot.Slot, candidate.Sku, candidate.Name, candidate.Candidate.Price, candidate.Candidate.Stock, candidate.Candidate.KeySpecs,
            candidate.Image, 1, slot.IsMulti, () => _ = RecheckAsync()));
        if (candidate.IsIncompatible)
        {
            App.Notify.Warning("Pieza incompatible", candidate.Reason);
        }
        if (string.IsNullOrWhiteSpace(_name))
        {
            Name = AutoName();
        }
        await RecheckAsync();
        // Siguiente ranura obligatoria vacía (ayuda a armar en orden)
        if (!slot.IsMulti && Slots.FirstOrDefault(s => s.IsRequired && s.IsEmpty) is { } next)
        {
            await SelectSlotAsync(next);
        }
        else
        {
            await LoadCandidatesAsync();
        }
    }

    private async Task RemovePartAsync(PcPartItem part)
    {
        foreach (var slot in AllSlots)
        {
            slot.Parts.Remove(part);
        }
        await RecheckAsync();
        await LoadCandidatesAsync();
    }

    /// <summary>Revisión en vivo con <see cref="CheckPcBuildQuery"/> (precios de la lista y stock de la sucursal).</summary>
    private async Task RecheckAsync()
    {
        var version = ++_checkVersion;
        var inputs = Inputs();
        PcBuildCheckView? check = null;
        if (inputs.Count > 0)
        {
            try
            {
                check = await App.SendAsync(new CheckPcBuildQuery(inputs));
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                App.Notify.Warning("No se pudo revisar la compatibilidad", AppServices.Describe(ex));
            }
        }
        if (version != _checkVersion)
        {
            return;
        }
        Check = check;
        if (check is not null && (_current is null || _current.Status == PcBuildStatus.Draft))
        {
            foreach (var item in check.Items)
            {
                foreach (var part in AllSlots.SelectMany(s => s.Parts).Where(p => p.Slot == item.Slot && p.Sku == item.Sku))
                {
                    part.Refresh(item.UnitPrice, item.Stock);
                }
            }
        }
        Issues.ReplaceAll((check?.Issues ?? []).OrderByDescending(i => i.IsError).Select(i => new PcIssueItem(i)));
        foreach (var slot in AllSlots)
        {
            slot.OnSummaryChanged();
        }
        OnPropertiesChanged(nameof(IsCompatible), nameof(ErrorCount), nameof(WarningCount), nameof(CompatibilityTitle), nameof(CompatibilityBrush),
            nameof(CompatibilitySoftBrush), nameof(CompatibilityGlyph), nameof(DrawText), nameof(RecommendedText), nameof(PsuText), nameof(PsuLoad),
            nameof(PsuLoadText), nameof(Total), nameof(TotalText), nameof(PartsText), nameof(HasParts));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private string AutoName()
    {
        var parts = new[] { Slots[0].Parts.FirstOrDefault()?.Name, Slots.First(s => s.Slot == PcSlot.Gpu).Parts.FirstOrDefault()?.Name }
            .Where(n => n is not null).Select(n => string.Join(' ', n!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3))).ToList();
        return parts.Count == 0 ? "PC a medida" : "PC " + string.Join(" + ", parts);
    }

    private async Task NewBuildAsync()
    {
        if (HasParts && _current is null
            && !await App.Dialogs.ConfirmAsync("Armado nuevo", "Se descartan las piezas elegidas (no se guardaron).", "Empezar de nuevo", "Volver"))
        {
            return;
        }
        Reset();
        await RecheckAsync();
        await SelectSlotAsync(Slots[0]);
    }

    private void Reset()
    {
        foreach (var slot in AllSlots)
        {
            slot.Parts.Clear();
        }
        ReservationLines.ReplaceAll([]);
        Current = null;
        Name = string.Empty;
        Customer = NoCustomer;
        Tab = "build";
    }

    /// <summary>Guarda el borrador o lo cotiza (vigencia y precios congelados). Con errores de compatibilidad, cotizar pide
    /// confirmación expresa y la cotización queda marcada (regla T-06).</summary>
    private async Task SaveAsync(bool quote)
    {
        if (string.IsNullOrWhiteSpace(_name))
        {
            Name = AutoName();
        }
        await RecheckAsync();
        var accept = false;
        if (quote && ErrorCount > 0)
        {
            accept = await App.Dialogs.ConfirmAsync("Cotizar con errores de compatibilidad",
                "El armado tiene errores de compatibilidad. Si el cliente lo pide igual, la cotización queda marcada «con errores aceptados».",
                "Cotizar igual", "Volver a revisar", isDanger: true, details: Issues.Where(i => i.IsError).Select(i => i.Message).ToList());
            if (!accept)
            {
                return;
            }
        }
        try
        {
            var row = await App.SendAsync(new SavePcBuildCommand(_current?.Id, _name.Trim(), _customer.Value, Inputs(), quote, _validity.Value, accept));
            Current = row;
            App.Notify.Success(quote ? $"Cotización {row.Number} emitida" : $"Borrador {row.Number} guardado",
                quote ? $"{Fmt.Money(row.Total)} · vigente hasta el {Fmt.Date(row.ValidUntil)}" : $"{row.Items} piezas · {Fmt.Money(row.Total)}");
            await LoadBuildsAsync();
            await RecheckAsync();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error(quote ? "No se pudo cotizar" : "No se pudo guardar", AppServices.Describe(ex));
        }
    }

    /// <summary>Abre un armado guardado: sus piezas en las ranuras (con los precios cotizados si ya se cotizó).</summary>
    public async Task OpenBuildAsync(string number)
    {
        try
        {
            var detail = await App.SendAsync(new GetPcBuildQuery(number));
            foreach (var slot in AllSlots)
            {
                slot.Parts.Clear();
            }
            var frozen = detail.Build.Status != PcBuildStatus.Draft;
            var items = frozen ? detail.QuotedItems : detail.Check.Items;
            foreach (var item in items)
            {
                // V7 · El producto sin ranura de un carrito se muestra entre los periféricos del armador
                var place = item.Slot ?? PcSlot.Peripheral;
                var slot = AllSlots.First(s => s.Slot == place);
                slot.Parts.Add(new PcPartItem(place, item.Sku, item.Name, item.UnitPrice, item.Stock, item.KeySpecs, _images.GetValueOrDefault(item.Sku),
                    item.Quantity, slot.IsMulti, () => _ = RecheckAsync()) { IsEditable = !frozen });
            }
            // V6 · Detalle de una reserva: cada pieza con lo que hay disponible ADEMÁS de lo reservado (regla S-03)
            ReservationLines.ReplaceAll(items.Select(i => new PcReservationLine(i, detail.Build.Status == PcBuildStatus.Reserved)));
            Current = detail.Build;
            Name = detail.Build.Name;
            Customer = Customers.FirstOrDefault(c => detail.Build.Customer is { } n && c.Label.StartsWith(n + " (", StringComparison.Ordinal)) ?? NoCustomer;
            Tab = "build";
            OnPropertyChanged(nameof(HasReservationLines));
            await RecheckAsync();
            await LoadCandidatesAsync();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo abrir el armado", AppServices.Describe(ex));
        }
    }

    // ------------------------------------------------------------------------------------------------ V6 · reservas y web
    /// <summary>Reserva el stock de la cotización abierta por las horas que indique el vendedor (48 por defecto): una reserva
    /// por pieza en la sucursal del armado, todo o nada; si falta stock el dominio dice qué piezas y cuánto hay (regla S-03).</summary>
    private async Task ReserveAsync()
    {
        var build = _current!;
        var answer = await App.Dialogs.PromptAsync($"Reservar el stock de {build.Number}",
            $"Se reserva cada pieza de «{build.Name}» en la sucursal del armado durante las horas indicadas: mientras dure la reserva no se vende a otro cliente ni en la web. " +
            "Si falta stock de alguna pieza no se reserva nada. El cliente la cobra en la caja (la venta consume la reserva).",
            "Horas de reserva", ["24", "48", "72", "168"], "Reservar", "48", "48", glyph: Glyphs.Clock);
        if (answer is null)
        {
            return;
        }
        if (!int.TryParse(answer, NumberStyles.Integer, Fmt.Culture, out var hours) || hours is < 1 or > 24 * 30)
        {
            App.Notify.Warning("Horas de reserva", "Escriba un número entero de horas entre 1 y 720 (30 días).");
            return;
        }
        try
        {
            var row = await App.SendAsync(new ReservePcBuildCommand(build.Number, hours));
            Current = row;
            App.Notify.Success($"Stock reservado para {row.Number}",
                $"{Fmt.Qty(row.Reserved)} unidad{(row.Reserved == 1 ? "" : "es")} reservada{(row.Reserved == 1 ? "" : "s")} hasta el {Fmt.DateTime(row.ReservedUntil!.Value)}");
            await AfterReservationChangeAsync(row.Number);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo reservar", AppServices.Describe(ex));
        }
    }

    /// <summary>Libera la reserva con su motivo: el stock vuelve a disponible y el armado queda anulado (regla S-04).</summary>
    private async Task ReleaseAsync()
    {
        var build = _current!;
        var reason = await App.Dialogs.PromptAsync($"Liberar la reserva de {build.Number}",
            $"El stock reservado de «{build.Name}» ({Fmt.Money(build.Total)}) vuelve a estar disponible y el armado queda anulado con el motivo. Esto no se deshace.",
            "Motivo", ["El cliente desistió", "El cliente no pasó a recoger", "Sin respuesta del cliente", "Reservado por error"], "Liberar reserva",
            isDanger: true, placeholder: "Escriba o elija el motivo");
        if (reason is null)
        {
            return;
        }
        if (reason.Length == 0)
        {
            App.Notify.Warning("Falta el motivo", "Indique por qué se libera la reserva: queda en la bitácora del armado.");
            return;
        }
        try
        {
            var row = await App.SendAsync(new ReleasePcBuildReservationCommand(build.Number, reason));
            Current = row;
            App.Notify.Success($"Reserva de {row.Number} liberada", "El stock volvió a estar disponible en la sucursal.");
            await AfterReservationChangeAsync(row.Number);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo liberar la reserva", AppServices.Describe(ex));
        }
    }

    private async Task TogglePublishAsync()
    {
        var build = _current!;
        var publish = !build.PublishedToWeb;
        try
        {
            var row = await App.SendAsync(new PublishPcBuildCommand(build.Number, publish));
            Current = row;
            App.Notify.Success(publish ? $"{row.Number} publicado en la tienda web" : $"{row.Number} retirado de la tienda web",
                publish ? "Aparece como armado sugerido con sus precios cotizados; la web muestra la disponibilidad de cada pieza." : "Ya no aparece entre los armados sugeridos.");
            await LoadBuildsAsync();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error(publish ? "No se pudo publicar" : "No se pudo retirar de la web", AppServices.Describe(ex));
        }
    }

    private void CopyPhone_()
    {
        if (ClipboardText.TrySet(ContactPhone))
        {
            App.Notify.Info("Teléfono copiado", $"{ContactName} · {ContactPhone}");
        }
        else
        {
            App.Notify.Warning("No se pudo copiar", "Intente de nuevo.");
        }
    }

    /// <summary>Después de reservar o liberar: la lista, la disponibilidad de las piezas y el stock de las demás pantallas cambian.</summary>
    private async Task AfterReservationChangeAsync(string number)
    {
        App.Data.Invalidate();
        await LoadBuildsAsync();
        await OpenBuildAsync(number);
    }

    private async Task CancelBuildAsync()
    {
        var build = _current!;
        if (!await App.Dialogs.ConfirmAsync($"Anular {build.Number}", $"El armado «{build.Name}» por {Fmt.Money(build.Total)} queda anulado (no se borra).",
                "Anular armado", "Volver", isDanger: true))
        {
            return;
        }
        if (await RunAsync(() => App.SendAsync(new CancelPcBuildCommand(build.Number)), "No se pudo anular"))
        {
            App.Notify.Success("Armado anulado", build.Number);
            await LoadBuildsAsync();
            // V7 · Se vuelve a abrir el armado anulado (si no estaba entre los 500 de la lista, la pantalla quedaba como «Armado nuevo»
            // con sus piezas editables)
            await OpenBuildAsync(build.Number);
        }
    }

    private void SellInPos_()
    {
        if (_current is { } build)
        {
            if (build.Status == PcBuildStatus.Reserved)
            {
                // V6 · La venta consume la reserva: el stock reservado se descuenta UNA sola vez (regla S-04)
                App.Notify.Info("La venta consume la reserva",
                    $"Al cobrar {build.Number} en la caja, las {Fmt.Qty(build.Reserved)} unidades reservadas salen del stock con la venta (no se descuentan dos veces).");
            }
            App.Navigator.Navigate("pos", new PcBuildToSell(build.Number));
        }
    }

    /// <summary>Proforma de la cotización: vista previa en pantalla, rollo de la caja o PDF.</summary>
    private async Task ProformaAsync()
    {
        try
        {
            var detail = await App.SendAsync(new GetPcBuildQuery(_current!.Number));
            var dialog = new ProformaDialog(App, await BuildProformaAsync(App, detail));
            await App.Dialogs.ShowAsync(dialog);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo armar la proforma", AppServices.Describe(ex));
        }
    }

    /// <summary>Datos de la proforma a partir del armado guardado (precios cotizados y meses de garantía de cada pieza).</summary>
    public static async Task<ProformaModel> BuildProformaAsync(AppServices app, PcBuildDetail detail)
    {
        var tech = await TechCatalog.LoadAsync(app);
        var lines = detail.QuotedItems.Select(i => new ProformaLine(TechText.Slot(i.Slot), i.Sku, i.Name, i.Quantity, i.UnitPrice, i.Subtotal,
            tech.GetValueOrDefault(i.Sku)?.WarrantyMonths ?? 0)).ToList();
        var notes = detail.Check.Issues.Select(i => (i.IsError ? detail.Build.QuotedWithErrors ? "Error aceptado: " : "Error: " : "Aviso: ") + i.Message).ToList();
        var w = app.Session.Workspace;
        return new ProformaModel(w.CompanyName, w.TaxId, app.Session.BranchText, detail.Build.Number, detail.Build.Name, detail.Build.Customer,
            detail.Build.CreatedAt, detail.Build.ValidUntil, lines, lines.Sum(l => l.Subtotal), detail.Build.IsCompatible, detail.Build.QuotedWithErrors, notes,
            Fmt.CurrencySymbol, app.Session.DisplayName);
    }
}

/// <summary>V4.2 · Vista previa de la proforma con impresión en la caja y PDF.</summary>
public sealed class ProformaDialog : FormDialog
{
    private readonly AppServices _app;

    public ProformaDialog(AppServices app, ProformaModel model)
        : base($"Proforma {model.Number}", "Cerrar", Glyphs.Report, width: 560)
    {
        _app = app;
        Model = model;
        Text = PcBuildProforma.Text(model);
        Print = new AsyncRelayCommand(PrintAsync);
        SavePdf = new AsyncRelayCommand(SavePdfAsync);
    }

    public override string? Subtitle => $"{Model.Name} · {Fmt.Money(Model.Total)} · vigente hasta el {Fmt.Date(Model.ValidUntil)}";

    public override bool ShowConfirm => false;

    public ProformaModel Model { get; }

    public string Text { get; }

    public AsyncRelayCommand Print { get; }

    public AsyncRelayCommand SavePdf { get; }

    /// <summary>Ruta del último PDF generado (pruebas y capturas).</summary>
    public string? PdfPath { get; private set; }

    protected override Task<bool> SubmitAsync() => Task.FromResult(true);

    private async Task PrintAsync()
    {
        var s = _app.Settings;
        if (ReceiptPrinters.Create(new HardwareOptions(s.PrinterKind, s.PrinterTarget, s.BaudRate)) is not { } printer)
        {
            _app.Notify.Info("Sin impresora configurada", "Configure la impresora de la caja en Configuración o use el PDF.");
            return;
        }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var bytes = PcBuildProforma.Roll(Model, Math.Clamp(s.PrinterColumns, 32, 64));
            await Task.Run(() => printer.PrintAsync(bytes, timeout.Token), timeout.Token);
            _app.Notify.Success("Proforma impresa", $"{Model.Number} en {printer.Name}");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException
                                       or OperationCanceledException or System.Net.Sockets.SocketException or ArgumentException)
        {
            _app.Notify.Error("No se pudo imprimir", ex.Message);
        }
    }

    private Task SavePdfAsync()
    {
        PdfPath = FiscalOutput.Save($"proforma-{Model.Number}.pdf", PcBuildProforma.Pdf(Model));
        if (!FiscalOutput.Open(PdfPath, _app.Settings))
        {
            _app.Notify.Success("Proforma en PDF", PdfPath);
        }
        return Task.CompletedTask;
    }
}
