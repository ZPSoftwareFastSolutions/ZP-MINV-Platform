using MINV.Application.Iam;
using MINV.Application.Inventory.Queries;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.ViewModels;

/// <summary>
/// Inicio: el estado del inventario de un vistazo (sucesor de 00_PORTADA de la V1/V2.1): indicadores, semáforo,
/// entradas y salidas de las últimas dos semanas, más vendidos, alertas urgentes, últimos movimientos y actividad.
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

    public DashboardViewModel(AppServices app) : base(app, "inicio", "Inicio", "Resumen del inventario", Glyphs.Home)
    {
        OpenProduct = new RelayCommand<string>(sku => app.Navigator.OpenProduct(sku));
        Go = new RelayCommand<string>(key => app.Navigator.Navigate(key));
        NewEntry = new RelayCommand(() => app.Navigator.Navigate("registro", new MovementPrefill(null, MovementTypeCodes.Receipt)));
        NewIssue = new RelayCommand(() => app.Navigator.Navigate("registro", new MovementPrefill(null, MovementTypeCodes.Issue)));
        Replenish = new RelayCommand<AlertItem>(a => app.Navigator.Navigate("registro", new MovementPrefill(a.Sku, MovementTypeCodes.Receipt)));
        FilterStock = new RelayCommand<LegendItem>(l => app.Navigator.Navigate("stock", l.Status));
    }

    public string Greeting { get => _greeting; private set => Set(ref _greeting, value); }

    public string DateText { get => _dateText; private set => Set(ref _dateText, value); }

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

    public BulkObservableCollection<ChartSegment> StatusSegments { get; } = [];

    public BulkObservableCollection<LegendItem> Legend { get; } = [];

    public BulkObservableCollection<ColumnPoint> Trend { get; } = [];

    public BulkObservableCollection<TopSellerItem> TopSellers { get; } = [];

    public BulkObservableCollection<AlertItem> UrgentAlerts { get; } = [];

    public BulkObservableCollection<MovementItem> Recent { get; } = [];

    public BulkObservableCollection<ActivityItem> Activity { get; } = [];

    public bool CanSeeActivity => App.Session.Can(PermissionCodes.AuditView);

    public bool CanRegister => App.Session.Can(PermissionCodes.MovementsRegisterWarehouse) || App.Session.Can(PermissionCodes.MovementsRegisterSales);

    public bool CanRegisterEntries => App.Session.Can(PermissionCodes.MovementsRegisterWarehouse);

    public bool CanRegisterIssues => App.Session.Can(PermissionCodes.MovementsRegisterSales);

    public bool CanCount => App.Session.Can(PermissionCodes.PhysicalCountRecord);

    public string ActiveProductsText => $"{ActiveProducts} productos activos";

    public RelayCommand<string> OpenProduct { get; }

    public RelayCommand<string> Go { get; }

    public RelayCommand NewEntry { get; }

    public RelayCommand NewIssue { get; }

    public RelayCommand<AlertItem> Replenish { get; }

    public RelayCommand<LegendItem> FilterStock { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var now = App.Now;
        var view = await App.Data.ProjectionAsync(force);
        var trend = await App.SendAsync(new GetMovementTrendQuery(14));
        var recent = await App.SendAsync(new GetRecentMovementsQuery(6));
        IReadOnlyList<ActivityRow> activity = CanSeeActivity ? await App.SendAsync(new GetActivityQuery(6)) : [];
        var r = view.Result;
        var first = App.Session.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        Greeting = $"{Fmt.Greeting(now)}, {first}";
        DateText = $"{Fmt.LongDate(view.Today)} · {App.Session.Workspace.CompanyName}";
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
        var totalEntries = trend.Sum(t => t.Entries);
        var totalIssues = trend.Sum(t => t.Issues);
        TrendSummary = $"{trend.Sum(t => t.Movements)} movimientos · entradas {Fmt.Qty(totalEntries)} · salidas {Fmt.Qty(totalIssues)}";

        var counts = r.Stock.GroupBy(s => s.Status).ToDictionary(g => g.Key, g => g.Count());
        var total = Math.Max(1, r.Stock.Count);
        StatusSegments.ReplaceAll(ChartOrder.Where(counts.ContainsKey)
            .Select(s => new ChartSegment(Fmt.StatusName(s), counts[s], Fmt.StatusBrushKey(s))));
        Legend.ReplaceAll(ChartOrder.Where(counts.ContainsKey).Select(s => new LegendItem(Fmt.StatusName(s), counts[s],
            (counts[s] * 100.0 / total).ToString("0", Fmt.Culture) + " %", Fmt.StatusBrushKey(s), s)));
        Trend.ReplaceAll(trend.Select(d => new ColumnPoint(d.Date.ToString("dd/MM", Fmt.Culture), (double)d.Entries, (double)d.Issues,
            $"{Fmt.LongDate(d.Date)}\nEntradas: {Fmt.Qty(d.Entries)}\nSalidas: {Fmt.Qty(d.Issues)}\nMovimientos: {d.Movements}")));
        var ranked = r.Stock.Where(s => s.SalesRank is not null).OrderBy(s => s.SalesRank).Take(5).ToList();
        var top = ranked.Count > 0 ? (double)ranked.Max(s => s.Sales30Days) : 1;
        TopSellers.ReplaceAll(ranked.Select(s => new TopSellerItem(s.SalesRank!.Value, s.Sku, s.Name, $"{Fmt.Qty(s.Sales30Days)} {s.Unit}",
            top > 0 ? (double)s.Sales30Days / top : 0)));
        UrgentAlerts.ReplaceAll(r.Alerts.Take(5).Select(a => new AlertItem(a)));
        Recent.ReplaceAll(recent.Select(m => new MovementItem(m, now)));
        Activity.ReplaceAll(activity.Select(a => new ActivityItem(a, now)));

        NextStep = r.Stock.Count == 0
            ? "Cargue su catálogo e inventario inicial (minv import-v21 o SALDO INICIAL por producto)."
            : OutOfStock > 0 && !CanRegisterEntries
                ? $"Hay {OutOfStock} producto{(OutOfStock == 1 ? "" : "s")} agotado{(OutOfStock == 1 ? "" : "s")}: confirme el stock antes de ofrecerlos y avise a Bodega."
            : OutOfStock > 0
                ? $"Hay {OutOfStock} producto{(OutOfStock == 1 ? "" : "s")} agotado{(OutOfStock == 1 ? "" : "s")}: revise el pedido sugerido y registre las entradas."
                : AlertCount > 0
                    ? $"Tiene {AlertCount} alerta{(AlertCount == 1 ? "" : "s")} de stock: programe la reposición antes de que se agote."
                    : "Todo en orden: el inventario está dentro de los niveles definidos.";
    }
}
