using MINV.Application.Iam;
using MINV.Application.Inventory.Queries;
using MINV.Application.Tech;
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

    public BulkObservableCollection<TechBarItem> SalesByCategory { get; } = [];

    public BulkObservableCollection<ChartSegment> PlatformSegments { get; } = [];

    public BulkObservableCollection<LegendRow> PlatformLegend { get; } = [];

    public BulkObservableCollection<TechBarItem> TopGpus { get; } = [];

    public BulkObservableCollection<TechBarItem> TopConsoles { get; } = [];

    public BulkObservableCollection<TechBarItem> ClaimsByStatus { get; } = [];

    public BulkObservableCollection<TechBarItem> QuotesVsSold { get; } = [];

    public string TechWarning { get; private set; } = string.Empty;

    public bool HasTechWarning => TechWarning.Length > 0;

    /// <summary>Sección Tecnología: una lectura aparte (si falla, el resto del tablero sigue).</summary>
    private async Task LoadTechAsync()
    {
        if (!CanSeeTech)
        {
            HasTech = false;
            return;
        }
        try
        {
            var t = await App.SendAsync(new GetTechDashboardQuery(30));
            SerialsKpi.Value = t.SerialsInStock.ToString("N0", Fmt.Culture);
            SerialsKpi.Detail = t.SerialsInStockByCategory.Count == 0 ? "Sin productos serializados" : string.Join(" · ", t.SerialsInStockByCategory.Take(3).Select(c => $"{c.Name} {c.Count}"));
            ClaimsKpi.Value = t.OpenClaims.ToString("N0", Fmt.Culture);
            ClaimsKpi.Detail = t.ClaimsOutOfWarranty > 0 ? $"{t.ClaimsOutOfWarranty} con cargo (fuera de garantía)" : "Todos en garantía";
            QuotesKpi.Value = t.QuotesOpen.ToString("N0", Fmt.Culture);
            QuotesKpi.Detail = Fmt.Money(t.QuotesValue) + " por cobrar";
            BuildsKpi.Value = t.BuildsSold.ToString("N0", Fmt.Culture);
            BuildsKpi.Detail = Fmt.Money(t.BuildsSoldValue);
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
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            System.Diagnostics.Trace.TraceWarning("M-INV · tablero Tecnología: {0}", ex.Message);
            HasTech = false;
        }
    }

    private static IEnumerable<TechBarItem> Bars(IReadOnlyList<NamedAmount> rows, bool money)
    {
        var max = money ? Math.Max(1m, rows.Select(r => r.Amount).DefaultIfEmpty(0).Max()) : Math.Max(1m, rows.Select(r => r.Quantity).DefaultIfEmpty(0).Max());
        return rows.Take(6).Select(r => new TechBarItem(r.Name, money ? Fmt.MoneyShort(r.Amount) : $"{Fmt.Qty(r.Quantity)} u.",
            money ? $"{Fmt.Qty(r.Quantity)} u." : Fmt.MoneyShort(r.Amount), (double)((money ? r.Amount : r.Quantity) / max), "Brand"));
    }

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
        // El gráfico muestra la OPERACIÓN: el saldo inicial (apertura del inventario) no es una entrada; se nombra aparte
        var totalEntries = trend.Sum(t => t.Entries);
        var totalIssues = trend.Sum(t => t.Issues);
        var totalOpening = trend.Sum(t => t.Opening);
        TrendSummary = $"{trend.Sum(t => t.Movements)} movimientos · entradas {Fmt.Qty(totalEntries)} · salidas {Fmt.Qty(totalIssues)}"
                       + (totalOpening > 0 ? $" · saldo inicial aparte: {Fmt.Qty(totalOpening)}" : string.Empty);

        var counts = r.Stock.GroupBy(s => s.Status).ToDictionary(g => g.Key, g => g.Count());
        var total = Math.Max(1, r.Stock.Count);
        StatusSegments.ReplaceAll(ChartOrder.Where(counts.ContainsKey)
            .Select(s => new ChartSegment(Fmt.StatusName(s), counts[s], Fmt.StatusBrushKey(s))));
        Legend.ReplaceAll(ChartOrder.Where(counts.ContainsKey).Select(s => new LegendItem(Fmt.StatusName(s), counts[s],
            (counts[s] * 100.0 / total).ToString("0", Fmt.Culture) + " %", Fmt.StatusBrushKey(s), s)));
        Trend.ReplaceAll(trend.Select(d => new ColumnPoint(d.Date.ToString("dd/MM", Fmt.Culture), (double)d.Entries, (double)d.Issues,
            $"{Fmt.LongDate(d.Date)}\nEntradas: {Fmt.Qty(d.Entries)}\nSalidas: {Fmt.Qty(d.Issues)}\nMovimientos: {d.Movements}"
            + (d.Opening > 0 ? $"\nSaldo inicial: {Fmt.Qty(d.Opening)} (apertura, fuera del gráfico)" : string.Empty))));
        var ranked = r.Stock.Where(s => s.SalesRank is not null).OrderBy(s => s.SalesRank).Take(5).ToList();
        var top = ranked.Count > 0 ? (double)ranked.Max(s => s.Sales30Days) : 1;
        TopSellers.ReplaceAll(ranked.Select(s => new TopSellerItem(s.SalesRank!.Value, s.Sku, s.Name, $"{Fmt.Qty(s.Sales30Days)} {s.Unit}",
            top > 0 ? (double)s.Sales30Days / top : 0)));
        UrgentAlerts.ReplaceAll(r.Alerts.Take(5).Select(a => new AlertItem(a)));
        Recent.ReplaceAll(recent.Select(m => new MovementItem(m, now)));
        Activity.ReplaceAll(activity.Select(a => new ActivityItem(a, now)));
        await LoadTechAsync();

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

/// <summary>V4.2 · Barra de un ranking del tablero Tecnología (texto principal, secundario y proporción 0..1).</summary>
public sealed record TechBarItem(string Name, string ValueText, string DetailText, double Share, string BrushKey);
