using System.Globalization;
using System.IO;
using MINV.Application.Inventory.Queries;
using MINV.Application.Reports;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.ViewModels;

public sealed class MovementReportItem(MovementReportRow r)
{
    public MovementReportRow Row { get; } = r;

    public string WhenText => Row.RecordedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public string Sku => Row.Sku;

    public string Name => Row.Name;

    public string TypeName => Row.TypeName;

    public string QuantityText => $"{Fmt.Signed(Row.Signed)} {Row.Unit}";

    public bool IsIn => Row.Signed > 0;

    public string BinCode => Row.BinCode;

    public string User => Row.User ?? "—";

    public string Document => Row.Document ?? "—";

    public string Notes => Row.Notes ?? string.Empty;
}

/// <summary>
/// Reportes: ventas (ingresos, utilidad, margen, ticket promedio, por día y agrupadas por producto, categoría, cajero,
/// cliente o medio de pago), compras (por proveedor y día), movimientos (filtrables por tipo) e inventario (valor y
/// semáforo por categoría). Período, agrupación y tipo se eligen de combos; todo se exporta.
/// </summary>
public sealed class ReportsViewModel : PageViewModel
{
    private static readonly string[] Palette = ["Brand", "Info", "Success", "Warning", "Danger", "StatusOverstock", "StatusLow", "ChartIssues"];
    private PeriodOption? _period;
    private Choice<string> _groupBy;
    private Choice<string?> _movementType;
    private string _tab = "sales";
    private SalesReport? _sales;
    private PurchasesReport? _purchases;
    private IReadOnlyList<RankRow> _ranking = [];
    private IReadOnlyList<MovementReportItem> _movements = [];

    public ReportsViewModel(AppServices app) : base(app, "reportes", "Reportes", "Ventas, compras, movimientos e inventario", Glyphs.Chart)
    {
        Groupings =
        [
            new("Por producto", "product"), new("Por categoría", "category"), new("Por cajero", "cashier"), new("Por cliente", "customer"),
            new("Por medio de pago", "payment"),
        ];
        _groupBy = Groupings[0];
        _movementType = new Choice<string?>("Todos los tipos", null);
        Export = new RelayCommand(ExportCsv);
        SelectTab = new RelayCommand<string>(t => Tab = t);
    }

    public BulkObservableCollection<PeriodOption> Periods { get; } = [];

    public IReadOnlyList<Choice<string>> Groupings { get; }

    public BulkObservableCollection<Choice<string?>> MovementTypes { get; } = [];

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

    public Choice<string> GroupBy
    {
        get => _groupBy;
        set
        {
            if (Set(ref _groupBy, value ?? Groupings[0]))
            {
                BuildRanking();
            }
        }
    }

    public Choice<string?> MovementType
    {
        get => _movementType;
        set
        {
            if (Set(ref _movementType, value ?? MovementTypes.FirstOrDefault()!) && HasLoaded)
            {
                _ = LoadAsync(force: false);
            }
        }
    }

    /// <summary>Pestaña: sales · purchases · movements · inventory.</summary>
    public string Tab
    {
        get => _tab;
        set
        {
            if (Set(ref _tab, value))
            {
                OnPropertiesChanged(nameof(IsSales), nameof(IsPurchases), nameof(IsMovements), nameof(IsInventory));
                _ = LoadAsync(force: false);
            }
        }
    }

    public bool IsSales { get => _tab == "sales"; set { if (value) { Tab = "sales"; } } }

    public bool IsPurchases { get => _tab == "purchases"; set { if (value) { Tab = "purchases"; } } }

    public bool IsMovements { get => _tab == "movements"; set { if (value) { Tab = "movements"; } } }

    public bool IsInventory { get => _tab == "inventory"; set { if (value) { Tab = "inventory"; } } }

    // ------------------------------------------------------------------------------------------------ ventas
    public KpiCard Revenue { get; } = new("Ingresos (con IVA)", Glyphs.Money, "Success", "SuccessSoft");

    public KpiCard Profit { get; } = new("Utilidad bruta", Glyphs.ArrowUp, "Brand", "BrandSoft");

    public KpiCard Tickets { get; } = new("Ventas", Glyphs.Report, "Info", "InfoSoft");

    public KpiCard Average { get; } = new("Ticket promedio", Glyphs.Cart, "Warning", "WarningSoft");

    public BulkObservableCollection<ColumnPoint> SalesByDay { get; } = [];

    public BulkObservableCollection<ChartSegment> SalesByMethod { get; } = [];

    public BulkObservableCollection<LegendRow> MethodLegend { get; } = [];

    public IReadOnlyList<RankRow> Ranking { get => _ranking; private set => Set(ref _ranking, value); }

    public string RankingTitle => $"Ventas {_groupBy.Label.ToLower(Fmt.Culture)}";

    // ------------------------------------------------------------------------------------------------ compras
    public KpiCard Received { get; } = new("Compras recibidas", Glyphs.Box, "Success", "SuccessSoft");

    public KpiCard Receipts { get; } = new("Recepciones", Glyphs.CheckCircle, "Info", "InfoSoft");

    public KpiCard OpenOrders { get; } = new("Órdenes abiertas", Glyphs.Clipboard, "Warning", "WarningSoft");

    public BulkObservableCollection<ColumnPoint> PurchasesByDay { get; } = [];

    public BulkObservableCollection<RankRow> BySupplier { get; } = [];

    // ------------------------------------------------------------------------------------------------ movimientos
    public IReadOnlyList<MovementReportItem> Movements { get => _movements; private set => Set(ref _movements, value); }

    public KpiCard EntriesKpi { get; } = new("Entradas", Glyphs.ArrowDown, "Success", "SuccessSoft");

    public KpiCard IssuesKpi { get; } = new("Salidas", Glyphs.ArrowUp, "Warning", "WarningSoft");

    public KpiCard MovementsKpi { get; } = new("Movimientos", Glyphs.Swap);

    // ------------------------------------------------------------------------------------------------ inventario
    public KpiCard InventoryValue { get; } = new("Valor del inventario", Glyphs.Money, "Success", "SuccessSoft");

    public KpiCard InventoryAlerts { get; } = new("Productos en alerta", Glyphs.Warning, "Danger", "DangerSoft");

    public KpiCard InventoryRotation { get; } = new("Sin movimiento", Glyphs.Clock, "Warning", "WarningSoft");

    public BulkObservableCollection<ChartSegment> ValueByCategory { get; } = [];

    public BulkObservableCollection<LegendRow> CategoryLegend { get; } = [];

    public BulkObservableCollection<RankRow> CategoryRows { get; } = [];

    public RelayCommand Export { get; }

    public RelayCommand<string> SelectTab { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        if (Periods.Count == 0)
        {
            var today = DateOnly.FromDateTime(App.Now.ToLocalTime().DateTime);
            Periods.ReplaceAll(PeriodOption.Presets(today));
            _period = Periods.First(p => p.Label == "Últimos 30 días");
            OnPropertyChanged(nameof(Period));
            var types = await App.SendAsync(new GetMovementTypesQuery());
            MovementTypes.ReplaceAll(new[] { _movementType }.Concat(types.Select(t => new Choice<string?>(t.Name, t.Code))));
        }
        var period = _period ?? Periods[0];
        Subtitle = $"{period.Label} · {period.RangeText}";
        OnPropertyChanged(nameof(Subtitle));
        switch (_tab)
        {
            case "purchases":
                await LoadPurchasesAsync(period);
                break;
            case "movements":
                await LoadMovementsAsync(period);
                break;
            case "inventory":
                await LoadInventoryAsync(force);
                break;
            default:
                await LoadSalesAsync(period);
                break;
        }
    }

    private async Task LoadSalesAsync(PeriodOption period)
    {
        var r = await App.SendAsync(new GetSalesReportQuery(period.From, period.To));
        _sales = r;
        Revenue.Value = Fmt.Money(r.Revenue);
        Revenue.Detail = $"Sin IVA {Fmt.Money(r.NetRevenue)} · IVA {Fmt.Money(r.Tax)}";
        Profit.Value = Fmt.Money(r.GrossProfit);
        Profit.Detail = $"Margen {(r.MarginPercent / 100).ToString("P1", Fmt.Culture)} · costo {Fmt.Money(r.Cost)}";
        Tickets.Value = r.Tickets.ToString("N0", Fmt.Culture);
        Tickets.Detail = $"{Fmt.Qty(r.Units)} unidades · {r.Voided} anuladas";
        Average.Value = Fmt.Money(r.AverageTicket);
        Average.Detail = r.ByDay.Count > 0 ? $"{Fmt.Money(r.Revenue / Math.Max(1, r.ByDay.Count))} por día" : null;
        SalesByDay.ReplaceAll(Compress(r.ByDay).Select(p => new ColumnPoint(p.Label, (double)p.Amount, 0,
            $"{p.Tooltip}: {Fmt.Money(p.Amount)} en {p.Count} ventas")));
        var methods = r.ByPaymentMethod.OrderByDescending(m => m.Amount).ToList();
        var total = methods.Sum(m => m.Amount);
        SalesByMethod.ReplaceAll(methods.Select((m, i) => new ChartSegment(m.Name, (double)m.Amount, Palette[i % Palette.Length])));
        MethodLegend.ReplaceAll(methods.Select((m, i) => new LegendRow(m.Name, Fmt.Money(m.Amount), total > 0 ? (m.Amount / total).ToString("P0", Fmt.Culture) : "—",
            Palette[i % Palette.Length])));
        BuildRanking();
    }

    private void BuildRanking()
    {
        OnPropertyChanged(nameof(RankingTitle));
        if (_sales is not { } r)
        {
            Ranking = [];
            return;
        }
        var groups = _groupBy.Value switch
        {
            "category" => r.ByCategory,
            "cashier" => r.ByCashier,
            "customer" => r.ByCustomer,
            "payment" => r.ByPaymentMethod,
            _ => r.ByProduct,
        };
        Ranking = Rank(groups.OrderByDescending(g => g.Amount).Take(50).ToList(), showQuantity: _groupBy.Value is "product" or "category");
    }

    private async Task LoadPurchasesAsync(PeriodOption period)
    {
        var r = await App.SendAsync(new GetPurchasesReportQuery(period.From, period.To));
        _purchases = r;
        Received.Value = Fmt.Money(r.Received);
        Received.Detail = $"{r.BySupplier.Count} proveedores";
        Receipts.Value = r.Receipts.ToString("N0", Fmt.Culture);
        Receipts.Detail = r.Receipts > 0 ? $"Promedio {Fmt.Money(r.Received / r.Receipts)}" : null;
        OpenOrders.Value = r.OpenOrders.ToString("N0", Fmt.Culture);
        OpenOrders.Detail = $"{Fmt.Money(r.OpenAmount)} por recibir";
        PurchasesByDay.ReplaceAll(Compress(r.ByDay).Select(p => new ColumnPoint(p.Label, 0, (double)p.Amount, $"{p.Tooltip}: {Fmt.Money(p.Amount)}")));
        BySupplier.ReplaceAll(Rank(r.BySupplier, showQuantity: false));
    }

    private async Task LoadMovementsAsync(PeriodOption period)
    {
        var rows = await App.SendAsync(new GetMovementsReportQuery(period.From, period.To, _movementType.Value));
        Movements = rows.Select(r => new MovementReportItem(r)).ToList();
        EntriesKpi.Value = rows.Count(r => r.Signed > 0).ToString("N0", Fmt.Culture);
        EntriesKpi.Detail = $"{Fmt.Qty(rows.Where(r => r.Signed > 0).Sum(r => r.Signed))} unidades";
        IssuesKpi.Value = rows.Count(r => r.Signed < 0).ToString("N0", Fmt.Culture);
        IssuesKpi.Detail = $"{Fmt.Qty(-rows.Where(r => r.Signed < 0).Sum(r => r.Signed))} unidades";
        MovementsKpi.Value = rows.Count.ToString("N0", Fmt.Culture);
        MovementsKpi.Detail = $"{rows.Select(r => r.Sku).Distinct().Count()} productos · {rows.Select(r => r.User).Distinct().Count()} usuarios";
    }

    private async Task LoadInventoryAsync(bool force)
    {
        var view = await App.Data.ProjectionAsync(force);
        var rows = view.Result.Stock.Where(r => r.IsActive).ToList();
        var total = rows.Sum(r => r.InventoryValue);
        InventoryValue.Value = Fmt.Money(total);
        InventoryValue.Detail = $"{rows.Count} productos activos";
        InventoryAlerts.Value = view.Result.Alerts.Count.ToString("N0", Fmt.Culture);
        InventoryAlerts.Detail = $"{rows.Count(r => r.Status == StockStatusCode.OutOfStock)} agotados · {rows.Count(r => r.Status == StockStatusCode.Critical)} críticos";
        InventoryRotation.Value = rows.Count(r => r.DaysWithoutMovement is null or > 30).ToString("N0", Fmt.Culture);
        InventoryRotation.Detail = "Más de 30 días sin movimiento";
        var byCategory = rows.GroupBy(r => r.Category).Select(g => new GroupTotal(g.Key, g.Key, g.Sum(r => r.Stock), g.Sum(r => r.InventoryValue), 0, g.Count()))
            .OrderByDescending(g => g.Amount).ToList();
        ValueByCategory.ReplaceAll(byCategory.Select((g, i) => new ChartSegment(g.Name, (double)g.Amount, Palette[i % Palette.Length])));
        CategoryLegend.ReplaceAll(byCategory.Select((g, i) => new LegendRow(g.Name, Fmt.Money(g.Amount),
            total > 0 ? (g.Amount / total).ToString("P0", Fmt.Culture) : "—", Palette[i % Palette.Length])));
        CategoryRows.ReplaceAll(Rank(byCategory, showQuantity: true));
    }

    /// <summary>Los períodos largos se agrupan por semana para que las columnas se lean bien.</summary>
    private static IEnumerable<(string Label, string Tooltip, decimal Amount, int Count)> Compress(IReadOnlyList<SeriesPoint> days)
    {
        if (days.Count <= 35)
        {
            return days.Select(d => (d.Date.ToString("dd/MM", CultureInfo.InvariantCulture), Fmt.LongDate(d.Date), d.Amount, d.Count));
        }
        return days.GroupBy(d => d.Date.DayNumber / 7).Select(g => (g.First().Date.ToString("dd/MM", CultureInfo.InvariantCulture),
            $"Semana del {Fmt.Date(g.First().Date)}", g.Sum(d => d.Amount), g.Sum(d => d.Count)));
    }

    private static List<RankRow> Rank(IReadOnlyList<GroupTotal> groups, bool showQuantity)
    {
        var max = groups.Count == 0 ? 0 : groups.Max(g => g.Amount);
        var total = groups.Sum(g => g.Amount);
        return groups.Select((g, i) => new RankRow(i + 1, g.Key, g.Name, showQuantity ? Fmt.Qty(g.Quantity) : "—", Fmt.Money(g.Amount),
            g.Profit != 0 ? Fmt.Money(g.Profit) : "—", g.Count.ToString("N0", Fmt.Culture), max > 0 ? (double)(g.Amount / max) : 0,
            total > 0 ? (g.Amount / total).ToString("P1", Fmt.Culture) : "—")).ToList();
    }

    private void ExportCsv()
    {
        var name = $"reporte-{_tab}-{_period?.From:yyyyMMdd}-{_period?.To:yyyyMMdd}.csv";
        var path = FileDialogs.SaveCsv(name);
        if (path is null)
        {
            return;
        }
        switch (_tab)
        {
            case "movements":
                Csv.Write(path, ["Fecha", "SKU", "Producto", "Tipo", "Cantidad", "Unidad", "Posición", "Usuario", "Documento", "Observaciones"],
                    _movements.Select(m => new object?[] { m.WhenText, m.Sku, m.Name, m.TypeName, m.Row.Signed, m.Row.Unit, m.BinCode, m.User, m.Row.Document, m.Row.Notes }));
                break;
            case "purchases":
                Csv.Write(path, ["#", "Proveedor", "Monto", "Recepciones", "Participación"],
                    BySupplier.Select(r => new object?[] { r.Rank, r.Name, r.AmountText, r.CountText, r.ShareText }));
                break;
            case "inventory":
                Csv.Write(path, ["#", "Categoría", "Unidades", "Valor", "Productos", "Participación"],
                    CategoryRows.Select(r => new object?[] { r.Rank, r.Name, r.QuantityText, r.AmountText, r.CountText, r.ShareText }));
                break;
            default:
                Csv.Write(path, ["#", _groupBy.Label, "Cantidad", "Ventas", "Utilidad", "Operaciones", "Participación"],
                    _ranking.Select(r => new object?[] { r.Rank, r.Name, r.QuantityText, r.AmountText, r.ProfitText, r.CountText, r.ShareText }));
                break;
        }
        App.Notify.Success("Reporte exportado", Path.GetFileName(path));
    }
}

/// <summary>Leyenda de un gráfico de dona (color de la paleta, nombre, monto y porcentaje).</summary>
public sealed record LegendRow(string Label, string AmountText, string PercentText, string BrushKey);
