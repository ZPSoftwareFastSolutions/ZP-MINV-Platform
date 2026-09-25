using MINV.Application.Common;
using MINV.Application.Corporate;
using MINV.Application.Iam;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Tarjeta de una sucursal en el tablero.</summary>
public sealed record BranchCard(string Code, string Name, string RevenueText, string TicketsText, string AverageText, string StockText, double Share,
    string ShareText, string WarehousesText, string UsersText, string TransfersText, bool IsActive, bool IsVisible);

/// <summary>Fila del stock consolidado.</summary>
public sealed record ConsolidatedItem(string Sku, string Name, string Category, string ByBranchText, string InTransitText, string TotalText,
    string ValueText, bool HasTransit);

/// <summary>Usuario elegible para asignarle sucursales.</summary>
public sealed record UserChoice(string Email, string Label, IReadOnlyList<string> Branches)
{
    public override string ToString() => Label;
}

/// <summary>Casilla de una sucursal (asignación de usuarios).</summary>
public sealed class BranchCheck(string code, string label, bool isChecked) : ObservableObject
{
    private bool _checked = isChecked;

    public string Code { get; } = code;

    public string Label { get; } = label;

    public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
}

/// <summary>
/// V4 · Sucursales: tablero gerencial por sucursal (sale del modelo de LECTURA: no carga la base de las cajas), stock
/// consolidado con lo que está en tránsito (contado una vez) y, para el administrador, alta de sucursales y asignación de
/// usuarios a sucursales.
/// </summary>
public sealed class BranchesViewModel : PageViewModel
{
    private PeriodOption _period;
    private string _search = string.Empty;
    private string? _reportNote;
    private string? _refreshed;
    private BranchEditor? _editor;
    private UserBranchesEditor? _assign;

    public BranchesViewModel(AppServices app)
        : base(app, "sucursales", "Sucursales", "Ventas y stock por sucursal, consolidado y mercadería en tránsito", Glyphs.Branch)
    {
        var today = DateOnly.FromDateTime(app.Now.ToLocalTime().DateTime);
        Periods = PeriodOption.Presets(today);
        _period = Periods.First(p => p.Label == "Últimos 30 días");
        New = new RelayCommand(() => { Assign = null; Editor = new BranchEditor(this, App); }, () => CanManage);
        AssignUsers = new AsyncRelayCommand(OpenAssignAsync, () => CanManage && App.Session.Can(PermissionCodes.UsersManage));
        Search = new AsyncRelayCommand(LoadStockAsync);
        OpenTransit = new RelayCommand(() => App.Navigator.Navigate("transferencias", Domain.Inventory.TransferStatus.Dispatched));
    }

    public IReadOnlyList<PeriodOption> Periods { get; }

    public PeriodOption Period
    {
        get => _period;
        set
        {
            if (Set(ref _period, value ?? _period))
            {
                _ = LoadAsync(force: true);
            }
        }
    }

    public string SearchText { get => _search; set => Set(ref _search, value ?? string.Empty); }

    public bool CanManage => App.Session.Can(PermissionCodes.BranchesManage);

    public KpiCard RevenueKpi { get; } = new("Ventas del período", Glyphs.Money, "Success", "SuccessSoft");

    public KpiCard StockKpi { get; } = new("Stock valorizado", Glyphs.Box);

    public KpiCard TransitKpi { get; } = new("En tránsito", Glyphs.Transfer, "Info", "InfoSoft");

    public KpiCard BranchesKpi { get; } = new("Sucursales visibles", Glyphs.Branch, "Warning", "WarningSoft");

    public BulkObservableCollection<BranchCard> Cards { get; } = [];

    public BulkObservableCollection<ConsolidatedItem> Stock { get; } = [];

    public string BranchColumns { get; private set; } = string.Empty;

    public string? ReportNote { get => _reportNote; private set => Set(ref _reportNote, value); }

    public string? RefreshedText { get => _refreshed; private set => Set(ref _refreshed, value); }

    public BranchEditor? Editor
    {
        get => _editor;
        private set
        {
            if (Set(ref _editor, value))
            {
                OnPropertiesChanged(nameof(IsEditing), nameof(IsCreating));
            }
        }
    }

    public UserBranchesEditor? Assign
    {
        get => _assign;
        private set
        {
            if (Set(ref _assign, value))
            {
                OnPropertiesChanged(nameof(IsEditing), nameof(IsAssigning));
            }
        }
    }

    public bool IsEditing => _editor is not null || _assign is not null;

    public bool IsCreating => _editor is not null;

    public bool IsAssigning => _assign is not null;

    public bool IsStockEmpty => HasLoaded && Stock.Count == 0;

    public RelayCommand New { get; }

    public AsyncRelayCommand AssignUsers { get; }

    public AsyncRelayCommand Search { get; }

    public RelayCommand OpenTransit { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var branches = await App.SendAsync(new GetBranchesQuery());
        BranchReport? report = null;
        try
        {
            report = await App.SendAsync(new GetBranchReportQuery(_period.From, _period.To));
            ReportNote = null;
        }
        catch (AccessDeniedException ex)
        {
            ReportNote = "Ventas por sucursal no disponibles: " + ex.Message;
        }
        var total = report?.TotalRevenue ?? 0;
        Cards.ReplaceAll(branches.Select(b =>
        {
            var row = report?.Branches.FirstOrDefault(r => r.Code == b.Code);
            return new BranchCard(b.Code, b.Name, row is null ? "—" : Fmt.Money(row.Revenue), row is null ? "" : $"{row.Tickets} tickets",
                row is null ? "" : $"Ticket promedio {Fmt.Money(row.AverageTicket)}", b.StockValue is { } v ? Fmt.Money(v) : "Sin acceso",
                total == 0 || row is null ? 0 : (double)(row.Revenue / total), row is null ? "" : $"{row.SharePercent:0.#} %",
                b.Warehouses.Count == 0 ? "Sin almacén" : "Almacén " + string.Join(", ", b.Warehouses), b.Users == 1 ? "1 usuario" : $"{b.Users} usuarios",
                $"{b.TransfersOut} salen · {b.TransfersIn} llegan", b.IsActive, b.IsVisible);
        }));
        RevenueKpi.Value = report is null ? "—" : Fmt.Money(report.TotalRevenue);
        RevenueKpi.Detail = _period.RangeText;
        StockKpi.Value = Fmt.Money(branches.Where(b => b.IsVisible).Sum(b => b.StockValue ?? 0));
        StockKpi.Detail = "Al costo promedio de cada almacén";
        TransitKpi.Value = report is null ? "—" : Fmt.Money(report.InTransitValue);
        TransitKpi.Detail = "Despachado y aún no recibido";
        BranchesKpi.Value = $"{branches.Count(b => b.IsVisible)} de {branches.Count}";
        BranchesKpi.Detail = App.Session.Access.AllBranches ? "Gerencia global: ve todas" : "Las asignadas a su usuario";
        RefreshedText = report?.RefreshedAt is { } at ? $"Modelo de lectura actualizado {Fmt.DateTime(at)} (se refresca cada 5 minutos)"
            : report is null ? null : "Datos en vivo";
        await LoadStockAsync();
    }

    private async Task LoadStockAsync()
    {
        try
        {
            var stock = await App.SendAsync(new ConsolidatedStockQuery(string.IsNullOrWhiteSpace(_search) ? null : _search.Trim()));
            BranchColumns = string.Join(" · ", stock.Branches.Select(b => b.Code));
            OnPropertyChanged(nameof(BranchColumns));
            Stock.ReplaceAll(stock.Rows.OrderByDescending(r => r.InTransit > 0).ThenBy(r => r.Sku).Select(r => new ConsolidatedItem(r.Sku, r.Name, r.Category,
                string.Join("   ", stock.Branches.Select((b, i) => $"{b.Code} {Fmt.Qty(r.ByBranch[i])}")), r.InTransit > 0 ? Fmt.Qty(r.InTransit) : "—",
                Fmt.Qty(r.Total, r.Unit), Fmt.Money(r.Value), r.InTransit > 0)));
            OnPropertyChanged(nameof(IsStockEmpty));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo leer el stock consolidado", AppServices.Describe(ex));
        }
    }

    internal void CloseEditors()
    {
        Editor = null;
        Assign = null;
    }

    internal async Task AfterChangeAsync()
    {
        CloseEditors();
        await LoadAsync(force: true);
    }

    private async Task OpenAssignAsync()
    {
        try
        {
            var users = await App.SendAsync(new GetUsersQuery());
            var branches = await App.SendAsync(new GetBranchesQuery());
            Editor = null;
            Assign = new UserBranchesEditor(this, App, users.Where(u => u.IsActive)
                .Select(u => new UserChoice(u.Email, $"{u.Name} · {string.Join(", ", u.Roles)}", u.BranchCodes)).ToList(), branches);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo abrir la asignación", AppServices.Describe(ex));
        }
    }
}

/// <summary>Alta de una sucursal con su almacén (y su caja).</summary>
public sealed class BranchEditor : ObservableObject
{
    private readonly BranchesViewModel _owner;
    private readonly AppServices _app;
    private string _code = string.Empty;
    private string _name = string.Empty;
    private string _warehouseCode = string.Empty;
    private string _warehouseName = string.Empty;
    private bool _register = true;
    private string? _error;

    public BranchEditor(BranchesViewModel owner, AppServices app)
    {
        _owner = owner;
        _app = app;
        Save = new AsyncRelayCommand(SaveAsync);
        Cancel = new RelayCommand(owner.CloseEditors);
    }

    public string Code
    {
        get => _code;
        set
        {
            if (Set(ref _code, (value ?? string.Empty).ToUpperInvariant()) && (_warehouseCode.Length == 0 || _warehouseCode.StartsWith("ALM", StringComparison.Ordinal)))
            {
                WarehouseCode = "ALM" + _code;
            }
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value ?? string.Empty) && (_warehouseName.Length == 0 || _warehouseName.StartsWith("Almacén ", StringComparison.Ordinal)))
            {
                WarehouseName = "Almacén " + _name.Replace("Sucursal ", "", StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    public string WarehouseCode { get => _warehouseCode; set => Set(ref _warehouseCode, (value ?? string.Empty).ToUpperInvariant()); }

    public string WarehouseName { get => _warehouseName; set => Set(ref _warehouseName, value ?? string.Empty); }

    public bool CreateRegister { get => _register; set => Set(ref _register, value); }

    public string? Error { get => _error; private set => Set(ref _error, value); }

    public AsyncRelayCommand Save { get; }

    public RelayCommand Cancel { get; }

    private async Task SaveAsync()
    {
        Error = null;
        try
        {
            var message = await _app.SendAsync(new CreateBranchCommand(_code.Trim(), _name.Trim(), _warehouseCode.Trim(), _warehouseName.Trim(), _register));
            _app.Notify.Success("Sucursal creada", message + " Vuelva a ingresar para verla en el selector de sucursales.");
            await _owner.AfterChangeAsync();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Error = AppServices.Describe(ex);
        }
    }
}

/// <summary>Sucursales en las que trabaja un usuario (la gerencia global las ve todas por su permiso).</summary>
public sealed class UserBranchesEditor : ObservableObject
{
    private readonly BranchesViewModel _owner;
    private readonly AppServices _app;
    private readonly IReadOnlyList<BranchRow> _branches;
    private UserChoice? _user;
    private string? _error;

    public UserBranchesEditor(BranchesViewModel owner, AppServices app, IReadOnlyList<UserChoice> users, IReadOnlyList<BranchRow> branches)
    {
        _owner = owner;
        _app = app;
        _branches = branches;
        Users = users;
        Save = new AsyncRelayCommand(SaveAsync, () => _user is not null && Checks.Any(c => c.IsChecked));
        Cancel = new RelayCommand(owner.CloseEditors);
        User = users.FirstOrDefault();
    }

    public IReadOnlyList<UserChoice> Users { get; }

    public BulkObservableCollection<BranchCheck> Checks { get; } = [];

    public UserChoice? User
    {
        get => _user;
        set
        {
            if (Set(ref _user, value) && value is not null)
            {
                Checks.ReplaceAll(_branches.Where(b => b.IsActive).Select(b => new BranchCheck(b.Code, $"{b.Code} · {b.Name}", value.Branches.Contains(b.Code))));
            }
        }
    }

    public string? Error { get => _error; private set => Set(ref _error, value); }

    public AsyncRelayCommand Save { get; }

    public RelayCommand Cancel { get; }

    private async Task SaveAsync()
    {
        Error = null;
        try
        {
            var message = await _app.SendAsync(new AssignUserBranchesCommand(_user!.Email, Checks.Where(c => c.IsChecked).Select(c => c.Code).ToList()));
            _app.Notify.Success("Sucursales asignadas", message + " Rige desde su próximo ingreso.");
            await _owner.AfterChangeAsync();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Error = AppServices.Describe(ex);
        }
    }
}
