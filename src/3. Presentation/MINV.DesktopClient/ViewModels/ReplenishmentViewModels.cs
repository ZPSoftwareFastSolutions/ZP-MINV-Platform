using System.Text;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Alertas priorizadas (en la V2.1: 16_ALERTAS) como tarjetas con la acción sugerida.</summary>
public sealed class AlertsViewModel : PageViewModel
{
    private List<AlertItem> _all = [];
    private FilterChip? _filter;

    public AlertsViewModel(AppServices app) : base(app, "alertas", "Alertas", "Productos que necesitan atención", Glyphs.Warning)
    {
        SelectFilter = new RelayCommand<FilterChip>(chip =>
        {
            foreach (var c in Filters)
            {
                c.IsSelected = ReferenceEquals(c, chip);
            }
            _filter = chip;
            Apply();
        });
        Replenish = new RelayCommand<AlertItem>(a => app.Navigator.Navigate("registro", new MovementPrefill(a.Sku, MovementTypeCodes.Receipt)),
            _ => CanReplenish);
        OpenProduct = new RelayCommand<AlertItem>(a => app.Navigator.OpenProduct(a.Sku));
        GoOrder = new RelayCommand(() => app.Navigator.Navigate("pedido"));
    }

    public BulkObservableCollection<FilterChip> Filters { get; } = [];

    public BulkObservableCollection<AlertItem> Items { get; } = [];

    public bool CanReplenish => App.Session.Can(PermissionCodes.MovementsRegisterWarehouse);

    public bool IsEmpty => HasLoaded && _all.Count == 0;

    public int OutOfStock { get; private set; }

    public int Critical { get; private set; }

    public int Low { get; private set; }

    public int Other { get; private set; }

    public RelayCommand<FilterChip> SelectFilter { get; }

    public RelayCommand<AlertItem> Replenish { get; }

    public RelayCommand<AlertItem> OpenProduct { get; }

    public RelayCommand GoOrder { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var view = await App.Data.ProjectionAsync(force);
        _all = view.Result.Alerts.Select(a => new AlertItem(a)).ToList();
        OutOfStock = _all.Count(a => a.Status == StockStatusCode.OutOfStock);
        Critical = _all.Count(a => a.Status == StockStatusCode.Critical);
        Low = _all.Count(a => a.Status == StockStatusCode.Low);
        Other = _all.Count - OutOfStock - Critical - Low;
        OnPropertiesChanged(nameof(OutOfStock), nameof(Critical), nameof(Low), nameof(Other), nameof(IsEmpty));
        var previous = _filter?.Value;
        var chips = new List<FilterChip> { new("Todas", null, _all.Count) };
        chips.AddRange(StockRules.AlertOrder.Where(s => _all.Any(a => a.Status == s))
            .Select(s => new FilterChip(Fmt.StatusName(s), s, _all.Count(a => a.Status == s), Fmt.StatusBrushKey(s))));
        Filters.ReplaceAll(chips);
        _filter = chips.FirstOrDefault(c => Equals(c.Value, previous)) ?? chips[0];
        _filter.IsSelected = true;
        Badge = _all.Count > 0 ? _all.Count.ToString(Fmt.Culture) : null;
        Apply();
    }

    private void Apply() => Items.ReplaceAll(_filter?.Value is StockStatusCode s ? _all.Where(a => a.Status == s) : _all);
}

/// <summary>Pedido sugerido por proveedor (en la V2.1: 18_PEDIDO), listo para copiar o exportar.</summary>
public sealed class OrderViewModel : PageViewModel
{
    private string _totalText = "—";
    private string _summary = string.Empty;

    public OrderViewModel(AppServices app) : base(app, "pedido", "Pedido sugerido", "Qué comprar y a quién, según mínimos y máximos", Glyphs.Cart)
    {
        CopyGroup = new RelayCommand<SupplierGroup>(g => Copy(Text(g), $"Pedido para {g.Name}"));
        CopyAll = new RelayCommand(() => Copy(string.Join(Environment.NewLine + Environment.NewLine, Groups.Select(Text)), "Pedido completo"),
            () => Groups.Count > 0);
        Export = new RelayCommand(ExportCsv, () => Groups.Count > 0);
        OpenProduct = new RelayCommand<OrderLineItem>(l => app.Navigator.OpenProduct(l.Sku));
    }

    public BulkObservableCollection<SupplierGroup> Groups { get; } = [];

    public string TotalText { get => _totalText; private set => Set(ref _totalText, value); }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public bool IsEmpty => HasLoaded && Groups.Count == 0;

    public RelayCommand<SupplierGroup> CopyGroup { get; }

    public RelayCommand CopyAll { get; }

    public RelayCommand Export { get; }

    public RelayCommand<OrderLineItem> OpenProduct { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var view = await App.Data.ProjectionAsync(force);
        var order = view.Result.Order;
        Groups.ReplaceAll(order.GroupBy(o => o.Supplier).Select(g => new SupplierGroup(g.Key, g.Select(l => new OrderLineItem(l)).ToList())));
        TotalText = Fmt.Money(order.Sum(o => o.Subtotal));
        Summary = order.Count == 0 ? "No hay nada que reponer" : $"{order.Count} productos · {Groups.Count} proveedor{(Groups.Count == 1 ? "" : "es")}";
        OnPropertyChanged(nameof(IsEmpty));
    }

    private string Text(SupplierGroup g)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Pedido sugerido · {App.Session.Workspace.CompanyName} · {Fmt.Date(App.Session.Workspace.Today)}");
        sb.AppendLine($"Proveedor: {g.Name}{(g.HasPhone ? " · " + g.Phone : "")}{(g.HasEmail ? " · " + g.Email : "")}");
        foreach (var l in g.Lines)
        {
            sb.AppendLine($"  {l.Sku}  {l.Name}  →  {l.QuantityText}  ({l.SubtotalText})");
        }
        sb.Append($"Total: {g.SubtotalText}");
        return sb.ToString();
    }

    private void Copy(string text, string what)
    {
        if (ClipboardText.TrySet(text))
        {
            App.Notify.Success("Copiado al portapapeles", what + ": péguelo en un correo o en WhatsApp.");
        }
        else
        {
            App.Notify.Warning("No se pudo copiar", "Otra aplicación está usando el portapapeles. Intente de nuevo.");
        }
    }

    private void ExportCsv()
    {
        var path = FileDialogs.SaveCsv($"pedido-sugerido-{DateTime.Now:yyyyMMdd}.csv");
        if (path is null)
        {
            return;
        }
        var lines = Groups.SelectMany(g => g.Lines).ToList();
        Csv.Write(path, ["Proveedor", "SKU", "Producto", "Unidad", "Estado", "Stock", "Mínimo", "Máximo", "A pedir", "Costo unitario", "Subtotal",
                "Entrega estimada", "Contacto", "Teléfono", "Correo"],
            lines.Select(l => new object?[]
            {
                l.Line.Supplier, l.Sku, l.Name, l.Line.Unit, StockRules.Label(l.Status), l.Line.Stock, l.Line.Minimum, l.Line.Maximum,
                l.Line.QuantityToOrder, l.Line.UnitCost, l.Line.Subtotal, l.Line.EstimatedDelivery, l.Line.Contact, l.Line.Phone, l.Line.Email,
            }));
        App.Notify.Success("Pedido exportado", $"{lines.Count} líneas en {System.IO.Path.GetFileName(path)}");
    }
}
