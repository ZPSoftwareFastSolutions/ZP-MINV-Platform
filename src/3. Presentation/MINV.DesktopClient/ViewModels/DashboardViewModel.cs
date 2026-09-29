using MINV.Application.Iam;
using MINV.Application.Inventory.Queries;
using MINV.Application.Tech;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;

namespace MINV.DesktopClient.ViewModels;

/// <summary>V7 · Botón grande del inicio: abre una pantalla o hace una función (registrar una entrada, nueva reserva…).</summary>
public sealed class HomeAction(string key, string title, string detail, string glyph, Action run)
{
    /// <summary>Pantalla o función que abre (para las pruebas y las capturas).</summary>
    public string Key { get; } = key;

    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public string Glyph { get; } = glyph;

    public RelayCommand Run { get; } = new(run);
}

/// <summary>V7 · Grupo de botones del inicio (una sección del menú: Ventas, Inventario…).</summary>
public sealed record HomeGroup(string Title, IReadOnlyList<HomeAction> Actions);

/// <summary>
/// V7 · Sección plegable del inicio: un botón «Ver … ^» CERRADO al entrar. La primera vez que se abre lee sus datos (nunca
/// antes); si los datos cambiaron mientras estaba cerrada, los vuelve a leer al abrirla. Una falla queda en la sección con
/// «Reintentar» (el resto del inicio sigue).
/// </summary>
public sealed class FoldSection : ObservableObject
{
    private readonly Func<Task> _load;
    private bool _open;
    private bool _loaded;
    private bool _loading;
    private bool _stale;
    private string? _error;

    public FoldSection(string key, string title, string subtitle, string glyph, Func<Task> load)
    {
        Key = key;
        Title = title;
        Subtitle = subtitle;
        Glyph = glyph;
        _load = load;
        Toggle = new AsyncRelayCommand(() => IsOpen ? CloseAsync() : OpenAsync());
        Retry = new AsyncRelayCommand(LoadAsync);
    }

    public string Key { get; }

    /// <summary>«Ver indicadores del inventario».</summary>
    public string Title { get; }

    public string Subtitle { get; }

    public string Glyph { get; }

    public bool IsOpen
    {
        get => _open;
        private set
        {
            if (Set(ref _open, value))
            {
                OnPropertiesChanged(nameof(ToggleGlyph), nameof(ToggleText));
            }
        }
    }

    /// <summary>Ya leyó sus datos al menos una vez.</summary>
    public bool IsLoaded { get => _loaded; private set => Set(ref _loaded, value); }

    public bool IsLoading { get => _loading; private set => Set(ref _loading, value); }

    public string? ErrorMessage
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _error is not null;

    /// <summary>«^» abierta, «⌄» cerrada.</summary>
    public string ToggleGlyph => _open ? Glyphs.ChevronUp : Glyphs.ChevronDown;

    public string ToggleText => _open ? "Ocultar" : "Ver";

    public AsyncRelayCommand Toggle { get; }

    public AsyncRelayCommand Retry { get; }

    public async Task OpenAsync()
    {
        IsOpen = true;
        if (!_loaded || _stale)
        {
            await LoadAsync();
        }
    }

    public Task CloseAsync()
    {
        IsOpen = false;
        return Task.CompletedTask;
    }

    /// <summary>Los datos cambiaron: una sección abierta se vuelve a leer ya; una cerrada, al abrirla.</summary>
    public async Task RefreshAsync()
    {
        _stale = true;
        if (_open)
        {
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await _load();
            IsLoaded = true;
            _stale = false;
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            ErrorMessage = AppServices.Describe(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }
}

/// <summary>
/// Inicio (sucesor de 00_PORTADA de la V1/V2.1). V7 · SIMPLIFICADO (tarea 14): saludo, sucursal activa y «¿Qué querés hacer?»:
/// las funciones del rol como botones grandes agrupados por sección (las mismas del menú, según los permisos, más atajos como
/// «Registrar entrada» o «Nueva reserva en mostrador»). Los indicadores y gráficos de antes viven en secciones plegables «Ver …
/// ^», CERRADAS al entrar, que no leen nada hasta abrirse: inventario, gráficos, alertas y más vendidos, actividad y el tablero
/// Tecnología (regla P-10 llevada al escritorio).
/// </summary>
public sealed class DashboardViewModel : PageViewModel
{
    private static readonly StockStatusCode[] ChartOrder =
    [
        StockStatusCode.Optimal, StockStatusCode.Low, StockStatusCode.Critical, StockStatusCode.OutOfStock,
        StockStatusCode.Overstock, StockStatusCode.Inconsistent, StockStatusCode.Inactive,
    ];

    private string _greeting = string.Empty;
    private string _dateText = string.Empty;
    private string _nextStep = string.Empty;
    private int _activeProducts;
    private string _inventoryValue = "—";
    private string _inventoryValueFull = string.Empty;
    private int _alertCount;
    private int _outOfStock;
    private int _belowMinimum;
    private string _orderTotal = "—";
    private string _orderDetail = string.Empty;
    private string _todayMovements = "0";
    private string _todayDetail = string.Empty;
    private string _trendSummary = string.Empty;
    private IReadOnlyList<HomeGroup> _groups = [];

    public DashboardViewModel(AppServices app) : base(app, "inicio", "Inicio", "Sus funciones a un clic", Glyphs.Home)
    {
        OpenProduct = new RelayCommand<string>(sku => app.Navigator.OpenProduct(sku));
        Go = new RelayCommand<string>(key => app.Navigator.Navigate(key));
        NewEntry = new RelayCommand(() => app.Navigator.Navigate("registro", new MovementPrefill(null, MovementTypeCodes.Receipt)));
        NewIssue = new RelayCommand(() => app.Navigator.Navigate("registro", new MovementPrefill(null, MovementTypeCodes.Issue)));
        Replenish = new RelayCommand<AlertItem>(a => app.Navigator.Navigate("registro", new MovementPrefill(a.Sku, MovementTypeCodes.Receipt)));
        FilterStock = new RelayCommand<LegendItem>(l => app.Navigator.Navigate("stock", l.Status));
        // V7 · Reservas web vigentes: abre la pantalla Reservas con el canal Web y las reservadas
        GoWebReservations = new RelayCommand(() => app.Navigator.Navigate("reservas", new ReservationFocus(Channel: PcBuildChannel.Web)));
        Inventory = new FoldSection("inventario", "Ver indicadores del inventario", "Valor, alertas, pedido sugerido, movimientos de hoy y el próximo paso",
            Glyphs.Money, LoadInventoryAsync);
        Charts = new FoldSection("graficos", "Ver entradas, salidas y semáforo", "Gráfico de los últimos 14 días y estado de cada producto",
            Glyphs.Chart, LoadChartsAsync);
        Lists = new FoldSection("listas", "Ver alertas urgentes, más vendidos y últimos movimientos", "Lo que conviene revisar hoy", Glyphs.Warning,
            LoadListsAsync);
        ActivitySection = new FoldSection("actividad", "Ver actividad reciente", "Quién hizo qué y cuándo (auditoría inmutable)", Glyphs.History,
            LoadActivityAsync);
        Tech = new FoldSection("tecnologia", "Ver tablero Tecnología", "Series, garantías, armados, reservas web y ventas de los últimos 30 días",
            Glyphs.Monitor, LoadTechAsync);
    }

    public string Greeting { get => _greeting; private set => Set(ref _greeting, value); }

    public string DateText { get => _dateText; private set => Set(ref _dateText, value); }

    /// <summary>V7 · Sucursal en la que trabaja (la de la barra superior).</summary>
    public string BranchText => App.Session.BranchText;

    public string RoleText => App.Session.RoleNames;

    // ------------------------------------------------------------------------------------------------ V7 · botones por rol
    /// <summary>«¿Qué querés hacer?»: las funciones del rol agrupadas por sección (las arma el menú con los permisos).</summary>
    public IReadOnlyList<HomeGroup> Groups { get => _groups; private set => Set(ref _groups, value); }

    /// <summary>Todas las funciones (para buscar una por su clave).</summary>
    public IEnumerable<HomeAction> Actions => _groups.SelectMany(g => g.Actions);

    /// <summary>
    /// V7 · El menú (con los permisos ya aplicados) arma los botones del inicio: un botón por pantalla de cada sección (sin el
    /// inicio) y, donde el rol puede, atajos a funciones: registrar entrada o salida, nueva reserva en mostrador.
    /// </summary>
    public void UseSections(IReadOnlyList<NavSection> sections, IReadOnlyList<PageViewModel> footer)
    {
        var s = App.Session;
        var groups = new List<HomeGroup>();
        foreach (var section in sections)
        {
            var actions = section.Pages.Where(p => p.Key != Key).Select(Page).ToList();
            switch (section.Title)
            {
                case "Ventas" when s.Can(PermissionCodes.PcBuildManage) && section.Pages.Any(p => p.Key == "reservas"):
                    actions.Add(new HomeAction("nueva-reserva", "Nueva reserva en mostrador", "Aparte productos para un cliente que pasa a pagar después",
                        Glyphs.Add, () => App.Navigator.Navigate("reservas", new ReservationFocus(New: true))));
                    break;
                case "Inventario":
                    if (s.Can(PermissionCodes.MovementsRegisterWarehouse))
                    {
                        actions.Insert(Math.Min(2, actions.Count), new HomeAction("registrar-entrada", "Registrar entrada", "Compras, saldo inicial o ajustes que suman",
                            Glyphs.Download, () => NewEntry.Execute(null)));
                    }
                    if (s.Can(PermissionCodes.MovementsRegisterSales))
                    {
                        actions.Insert(Math.Min(3, actions.Count), new HomeAction("registrar-salida", "Registrar salida", "Ventas, bajas o consumo interno",
                            Glyphs.Export, () => NewIssue.Execute(null)));
                    }
                    break;
            }
            if (actions.Count > 0)
            {
                groups.Add(new HomeGroup(section.Title, actions));
            }
        }
        if (footer.Count > 0)
        {
            groups.Add(new HomeGroup("Ayuda y preferencias", footer.Select(Page).ToList()));
        }
        Groups = groups;
        OnPropertyChanged(nameof(Actions));

        HomeAction Page(PageViewModel page) => new(page.Key, page.Title, page.Subtitle, page.Glyph, () => App.Navigator.Navigate(page.Key));
    }

    /// <summary>La función con esa clave (null si el rol no la tiene).</summary>
    public HomeAction? Action(string key) => Actions.FirstOrDefault(a => a.Key == key);

    // ------------------------------------------------------------------------------------------------ V7 · secciones plegables
    public FoldSection Inventory { get; }

    public FoldSection Charts { get; }

    public FoldSection Lists { get; }

    public FoldSection ActivitySection { get; }

    public FoldSection Tech { get; }

    /// <summary>Las secciones que el rol puede ver (actividad con auditoría; Tecnología con reportes).</summary>
    public IEnumerable<FoldSection> Sections
    {
        get
        {
            yield return Inventory;
            yield return Charts;
            yield return Lists;
            if (CanSeeActivity)
            {
                yield return ActivitySection;
            }
            if (CanSeeTech)
            {
                yield return Tech;
            }
        }
    }

    /// <summary>Abre (y lee) todas las secciones que el rol puede ver: «Ver todo».</summary>
    public async Task OpenAllAsync()
    {
        foreach (var section in Sections.ToList())
        {
            await section.OpenAsync();
        }
    }

    // ------------------------------------------------------------------------------------------------ indicadores del inventario
    /// <summary>Próximo paso sugerido (como el asistente de la portada de la V1.2).</summary>
    public string NextStep { get => _nextStep; private set => Set(ref _nextStep, value); }

    public int ActiveProducts { get => _activeProducts; private set => Set(ref _activeProducts, value); }

    public string InventoryValue { get => _inventoryValue; private set => Set(ref _inventoryValue, value); }

    public string InventoryValueFull { get => _inventoryValueFull; private set => Set(ref _inventoryValueFull, value); }

    public int AlertCount { get => _alertCount; private set => Set(ref _alertCount, value); }

    public int OutOfStock { get => _outOfStock; private set => Set(ref _outOfStock, value); }

    public int BelowMinimum { get => _belowMinimum; private set => Set(ref _belowMinimum, value); }

    public string OrderTotal { get => _orderTotal; private set => Set(ref _orderTotal, value); }

    public string OrderDetail { get => _orderDetail; private set => Set(ref _orderDetail, value); }

    public string TodayMovements { get => _todayMovements; private set => Set(ref _todayMovements, value); }

    public string TodayDetail { get => _todayDetail; private set => Set(ref _todayDetail, value); }

    public string TrendSummary { get => _trendSummary; private set => Set(ref _trendSummary, value); }

    public string ActiveProductsText => $"{ActiveProducts} productos activos";

    public BulkObservableCollection<ChartSegment> StatusSegments { get; } = [];

    public BulkObservableCollection<LegendItem> Legend { get; } = [];

    public BulkObservableCollection<ColumnPoint> Trend { get; } = [];

    public BulkObservableCollection<TopSellerItem> TopSellers { get; } = [];

    public BulkObservableCollection<AlertItem> UrgentAlerts { get; } = [];

    public BulkObservableCollection<MovementItem> Recent { get; } = [];

    public BulkObservableCollection<ActivityItem> Activity { get; } = [];

    // ------------------------------------------------------------------------------------------------ V4.2 · Tecnología
    private static readonly string[] TechPalette = ["Brand", "ChartEntries", "ChartIssues", "Success", "Warning", "StatusOverstock", "Danger", "Info"];
    private bool _hasTech;

    /// <summary>La sección Tecnología (ventas por categoría y plataforma, GPU y consolas, RMA y armados) se ve con reportes.</summary>
    public bool CanSeeTech => App.Session.Can(PermissionCodes.ReportsView);

    public bool HasTech { get => _hasTech; private set => Set(ref _hasTech, value); }

    public KpiCard SerialsKpi { get; } = new("Unidades con serie en stock", Glyphs.Barcode, "Info", "InfoSoft");

    public KpiCard ClaimsKpi { get; } = new("Casos RMA abiertos", Glyphs.Shield, "Warning", "WarningSoft");

    public KpiCard QuotesKpi { get; } = new("Armados cotizados vigentes", Glyphs.Report, "Brand", "BrandSoft");

    public KpiCard BuildsKpi { get; } = new("Armados vendidos · 30 días", Glyphs.Monitor, "Success", "SuccessSoft");

    /// <summary>V6 · Reservas hechas desde la tienda web todavía vigentes (cantidad y Bs). V7: abre la pantalla Reservas (canal Web).</summary>
    public KpiCard WebReservationsKpi { get; } = new("Reservas web activas", Glyphs.Globe, "Brand", "BrandSoft");

    public RelayCommand GoWebReservations { get; }

    public BulkObservableCollection<TechBarItem> SalesByCategory { get; } = [];

    public BulkObservableCollection<ChartSegment> PlatformSegments { get; } = [];

    public BulkObservableCollection<LegendRow> PlatformLegend { get; } = [];

    public BulkObservableCollection<TechBarItem> TopGpus { get; } = [];

    public BulkObservableCollection<TechBarItem> TopConsoles { get; } = [];

    public BulkObservableCollection<TechBarItem> ClaimsByStatus { get; } = [];

    public BulkObservableCollection<TechBarItem> QuotesVsSold { get; } = [];

    public string TechWarning { get; private set; } = string.Empty;

    public bool HasTechWarning => TechWarning.Length > 0;

    public bool CanSeeActivity => App.Session.Can(PermissionCodes.AuditView);

    public bool CanRegister => App.Session.Can(PermissionCodes.MovementsRegisterWarehouse) || App.Session.Can(PermissionCodes.MovementsRegisterSales);

    public bool CanRegisterEntries => App.Session.Can(PermissionCodes.MovementsRegisterWarehouse);

    public bool CanRegisterIssues => App.Session.Can(PermissionCodes.MovementsRegisterSales);

    public bool CanCount => App.Session.Can(PermissionCodes.PhysicalCountRecord);

    public RelayCommand<string> OpenProduct { get; }

    public RelayCommand<string> Go { get; }

    public RelayCommand NewEntry { get; }

    public RelayCommand NewIssue { get; }

    public RelayCommand<AlertItem> Replenish { get; }

    public RelayCommand<LegendItem> FilterStock { get; }

    /// <summary>
    /// Al entrar solo se arma el saludo (no se lee ningún indicador: las secciones están cerradas). Si los datos cambiaron (otra
    /// pantalla registró algo), las secciones abiertas se vuelven a leer y las cerradas lo harán al abrirse.
    /// </summary>
    protected override async Task LoadCoreAsync(bool force)
    {
        var now = App.Now;
        var first = App.Session.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        Greeting = $"{Fmt.Greeting(now)}, {first}";
        DateText = $"{Fmt.LongDate(DateOnly.FromDateTime(now.ToLocalTime().DateTime))} · {App.Session.Workspace.CompanyName}";
        OnPropertiesChanged(nameof(BranchText), nameof(RoleText));
        if (force || HasLoaded)
        {
            foreach (var section in Sections.ToList())
            {
                await section.RefreshAsync();
            }
        }
    }

    /// <summary>Indicadores del inventario: valor, alertas, pedido sugerido, movimientos de hoy y el próximo paso.</summary>
    private async Task LoadInventoryAsync()
    {
        var view = await App.Data.ProjectionAsync();
        var trend = await App.SendAsync(new GetMovementTrendQuery(14));
        var r = view.Result;
        ActiveProducts = r.Stock.Count(s => s.IsActive);
        OnPropertyChanged(nameof(ActiveProductsText));
        var value = r.Stock.Sum(s => s.InventoryValue);
        InventoryValue = Fmt.MoneyShort(value);
        InventoryValueFull = Fmt.Money(value);
        AlertCount = r.Alerts.Count;
        OutOfStock = r.Stock.Count(s => s.IsActive && s.Status == StockStatusCode.OutOfStock);
        BelowMinimum = r.Stock.Count(s => s.IsActive && s.Status is StockStatusCode.Critical or StockStatusCode.Low);
        var orderTotal = r.Order.Sum(o => o.Subtotal);
        OrderTotal = r.Order.Count == 0 ? Fmt.Money(0) : Fmt.MoneyShort(orderTotal);
        var suppliers = r.Order.Select(o => o.Supplier).Distinct().Count();
        OrderDetail = r.Order.Count == 0 ? "Nada que reponer" : $"{r.Order.Count} productos · {suppliers} proveedor{(suppliers == 1 ? "" : "es")}";
        var today = trend.Count > 0 ? trend[^1] : null;
        TodayMovements = (today?.Movements ?? 0).ToString(Fmt.Culture);
        TodayDetail = today is null || today.Movements == 0
            ? "Sin movimientos hoy"
            : $"+{Fmt.Qty(today.Entries)} entradas · −{Fmt.Qty(today.Issues)} salidas";
        NextStep = r.Stock.Count == 0
            ? "Cargue su catálogo e inventario inicial (SALDO INICIAL por producto)."
            : OutOfStock > 0 && !CanRegisterEntries
                ? $"Hay {OutOfStock} producto{(OutOfStock == 1 ? "" : "s")} agotado{(OutOfStock == 1 ? "" : "s")}: confirme el stock antes de ofrecerlos y avise a Bodega."
            : OutOfStock > 0
                ? $"Hay {OutOfStock} producto{(OutOfStock == 1 ? "" : "s")} agotado{(OutOfStock == 1 ? "" : "s")}: revise el pedido sugerido y registre las entradas."
                : AlertCount > 0
                    ? $"Tiene {AlertCount} alerta{(AlertCount == 1 ? "" : "s")} de stock: programe la reposición antes de que se agote."
                    : "Todo en orden: el inventario está dentro de los niveles definidos.";
    }

    /// <summary>Entradas y salidas de los últimos 14 días y el semáforo por producto.</summary>
    private async Task LoadChartsAsync()
    {
        var view = await App.Data.ProjectionAsync();
        var trend = await App.SendAsync(new GetMovementTrendQuery(14));
        var r = view.Result;
        // El gráfico muestra la OPERACIÓN: el saldo inicial (apertura del inventario) no es una entrada; se nombra aparte
        var totalEntries = trend.Sum(t => t.Entries);
        var totalIssues = trend.Sum(t => t.Issues);
        var totalOpening = trend.Sum(t => t.Opening);
        TrendSummary = $"{trend.Sum(t => t.Movements)} movimientos · entradas {Fmt.Qty(totalEntries)} · salidas {Fmt.Qty(totalIssues)}"
                       + (totalOpening > 0 ? $" · saldo inicial aparte: {Fmt.Qty(totalOpening)}" : string.Empty);
        ActiveProducts = r.Stock.Count(s => s.IsActive);
        OnPropertyChanged(nameof(ActiveProductsText));
        var counts = r.Stock.GroupBy(s => s.Status).ToDictionary(g => g.Key, g => g.Count());
        var total = Math.Max(1, r.Stock.Count);
        StatusSegments.ReplaceAll(ChartOrder.Where(counts.ContainsKey)
            .Select(s => new ChartSegment(Fmt.StatusName(s), counts[s], Fmt.StatusBrushKey(s))));
        Legend.ReplaceAll(ChartOrder.Where(counts.ContainsKey).Select(s => new LegendItem(Fmt.StatusName(s), counts[s],
            (counts[s] * 100.0 / total).ToString("0", Fmt.Culture) + " %", Fmt.StatusBrushKey(s), s)));
        Trend.ReplaceAll(trend.Select(d => new ColumnPoint(d.Date.ToString("dd/MM", Fmt.Culture), (double)d.Entries, (double)d.Issues,
            $"{Fmt.LongDate(d.Date)}\nEntradas: {Fmt.Qty(d.Entries)}\nSalidas: {Fmt.Qty(d.Issues)}\nMovimientos: {d.Movements}"
            + (d.Opening > 0 ? $"\nSaldo inicial: {Fmt.Qty(d.Opening)} (apertura, fuera del gráfico)" : string.Empty))));
    }

    /// <summary>Alertas urgentes, más vendidos de 30 días y últimos movimientos.</summary>
    private async Task LoadListsAsync()
    {
        var view = await App.Data.ProjectionAsync();
        var recent = await App.SendAsync(new GetRecentMovementsQuery(6));
        var now = App.Now;
        var r = view.Result;
        var ranked = r.Stock.Where(s => s.SalesRank is not null).OrderBy(s => s.SalesRank).Take(5).ToList();
        var top = ranked.Count > 0 ? (double)ranked.Max(s => s.Sales30Days) : 1;
        TopSellers.ReplaceAll(ranked.Select(s => new TopSellerItem(s.SalesRank!.Value, s.Sku, s.Name, $"{Fmt.Qty(s.Sales30Days)} {s.Unit}",
            top > 0 ? (double)s.Sales30Days / top : 0)));
        UrgentAlerts.ReplaceAll(r.Alerts.Take(5).Select(a => new AlertItem(a)));
        Recent.ReplaceAll(recent.Select(m => new MovementItem(m, now)));
    }

    private async Task LoadActivityAsync()
    {
        IReadOnlyList<ActivityRow> activity = CanSeeActivity ? await App.SendAsync(new GetActivityQuery(8)) : [];
        var now = App.Now;
        Activity.ReplaceAll(activity.Select(a => new ActivityItem(a, now)));
    }

    /// <summary>Tablero Tecnología: una lectura aparte (si falla, queda el aviso en la sección y el resto del inicio sigue).</summary>
    private async Task LoadTechAsync()
    {
        if (!CanSeeTech)
        {
            HasTech = false;
            return;
        }
        var t = await App.SendAsync(new GetTechDashboardQuery(30));
        SerialsKpi.Value = t.SerialsInStock.ToString("N0", Fmt.Culture);
        SerialsKpi.Detail = t.SerialsInStockByCategory.Count == 0 ? "Sin productos serializados" : string.Join(" · ", t.SerialsInStockByCategory.Take(3).Select(c => $"{c.Name} {c.Count}"));
        ClaimsKpi.Value = t.OpenClaims.ToString("N0", Fmt.Culture);
        ClaimsKpi.Detail = t.ClaimsOutOfWarranty > 0 ? $"{t.ClaimsOutOfWarranty} con cargo (fuera de garantía)" : "Todos en garantía";
        QuotesKpi.Value = t.QuotesOpen.ToString("N0", Fmt.Culture);
        QuotesKpi.Detail = Fmt.Money(t.QuotesValue) + " por cobrar";
        BuildsKpi.Value = t.BuildsSold.ToString("N0", Fmt.Culture);
        BuildsKpi.Detail = Fmt.Money(t.BuildsSoldValue);
        // Reservas web vigentes (carritos y armados): cantidad y total reservado (el clic abre la pantalla Reservas)
        WebReservationsKpi.Value = t.WebReservationsActive.ToString("N0", Fmt.Culture);
        WebReservationsKpi.Detail = t.WebReservationsActive == 0 ? "Ninguna reserva vigente" : $"{Fmt.Money(t.WebReservationsValue)} reservados · ver las reservas";
        SalesByCategory.ReplaceAll(Bars(t.SalesByCategory, money: true));
        var platformTotal = t.SalesByPlatform.Sum(p => p.Amount);
        PlatformSegments.ReplaceAll(t.SalesByPlatform.Select((p, i) => new ChartSegment(p.Name, (double)p.Amount, TechPalette[i % TechPalette.Length])));
        PlatformLegend.ReplaceAll(t.SalesByPlatform.Select((p, i) => new LegendRow(p.Name, Fmt.MoneyShort(p.Amount),
            platformTotal > 0 ? (p.Amount / platformTotal).ToString("P0", Fmt.Culture) : "—", TechPalette[i % TechPalette.Length])));
        TopGpus.ReplaceAll(Bars(t.TopGpus, money: false));
        TopConsoles.ReplaceAll(Bars(t.TopConsoles, money: false));
        var claimMax = Math.Max(1, t.OpenClaimsByStatus.Select(c => c.Count).DefaultIfEmpty(0).Max());
        ClaimsByStatus.ReplaceAll(t.OpenClaimsByStatus.Select(c => new TechBarItem(Fmt.SentenceCase(c.Name), c.Count.ToString("N0", Fmt.Culture), string.Empty,
            (double)c.Count / claimMax, "Warning")));
        var buildMax = Math.Max(1m, Math.Max(t.QuotesValue, t.BuildsSoldValue));
        QuotesVsSold.ReplaceAll(
        [
            new TechBarItem($"Cotizados vigentes ({t.QuotesOpen})", Fmt.MoneyShort(t.QuotesValue), string.Empty, (double)(t.QuotesValue / buildMax), "Info"),
            new TechBarItem($"Vendidos ({t.BuildsSold})", Fmt.MoneyShort(t.BuildsSoldValue), string.Empty, (double)(t.BuildsSoldValue / buildMax), "Success"),
        ]);
        // Existencias (producto y sucursal) cuyo stock no coincide con sus series en stock (regla T-02)
        TechWarning = t.SerializedWithoutSerials switch
        {
            0 => string.Empty,
            1 => "1 producto serializado no tiene todas sus unidades con serie en su sucursal: regístrelas en «Series e IMEI».",
            var n => $"{n} existencias de productos serializados (producto y sucursal) no tienen todas sus unidades con serie: regístrelas en «Series e IMEI».",
        };
        OnPropertiesChanged(nameof(TechWarning), nameof(HasTechWarning));
        HasTech = true;
    }

    private static IEnumerable<TechBarItem> Bars(IReadOnlyList<NamedAmount> rows, bool money)
    {
        var max = money ? Math.Max(1m, rows.Select(r => r.Amount).DefaultIfEmpty(0).Max()) : Math.Max(1m, rows.Select(r => r.Quantity).DefaultIfEmpty(0).Max());
        return rows.Take(6).Select(r => new TechBarItem(r.Name, money ? Fmt.MoneyShort(r.Amount) : $"{Fmt.Qty(r.Quantity)} u.",
            money ? $"{Fmt.Qty(r.Quantity)} u." : Fmt.MoneyShort(r.Amount), (double)((money ? r.Amount : r.Quantity) / max), "Brand"));
    }
}

/// <summary>V4.2 · Barra de un ranking del tablero Tecnología (texto principal, secundario y proporción 0..1).</summary>
public sealed record TechBarItem(string Name, string ValueText, string DetailText, double Share, string BrushKey);
