using System.Collections.ObjectModel;
using System.Windows.Threading;
using MINV.Application.Iam;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Grupo del menú lateral («Inventario», «Reposición»…).</summary>
public sealed record NavSection(string Title, IReadOnlyList<PageViewModel> Pages);

/// <summary>V4 · Opción del selector de sucursal de la barra superior (null = todas, solo gerencia global).</summary>
public sealed record BranchOption(Guid? Id, string Code, string Label);

/// <summary>
/// Ventana principal: menú lateral según los permisos del rol, navegación, búsqueda global de productos (Ctrl+K), ficha
/// del producto en panel lateral, avisos, confirmaciones, tema y cierre de sesión.
/// </summary>
public sealed class ShellViewModel : ObservableObject, INavigator
{
    private readonly AppServices _app;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };
    private PageViewModel _current;
    private ProductDetailViewModel? _product;
    private bool _compact;
    private bool _userMenuOpen;
    private string _clockText = string.Empty;
    private BranchOption? _branch;
    private bool _changingBranch;

    public ShellViewModel(AppServices app, DashboardViewModel dashboard, StockViewModel stock, MovementViewModel movement,
        PhysicalCountViewModel count, AlertsViewModel alerts, OrderViewModel order, ActivityViewModel activity,
        SettingsViewModel settings, HelpViewModel help, CatalogViewModel catalog, PosViewModel pos, SalesViewModel sales,
        CustomersViewModel customers, PurchaseOrdersViewModel purchases, SuppliersViewModel suppliers, ReportsViewModel reports,
        AccountingViewModel accounting, UsersViewModel users, BranchesViewModel branches, TransfersViewModel transfers,
        IntegrationsViewModel integrations)
    {
        _app = app;
        app.Navigator = this;
        var s = app.Session;
        // Cada rol ve solo lo que puede hacer (la tubería vuelve a verificar el permiso en cada caso de uso)
        var sections = new List<NavSection> { new("General", [dashboard]) };
        Add(sections, "Ventas",
            (pos, s.Can(PermissionCodes.PosOperate)),
            (sales, s.Can(PermissionCodes.SalesView)),
            (customers, s.Can(PermissionCodes.CustomersManage) || s.Can(PermissionCodes.SalesView)));
        Add(sections, "Inventario",
            (stock, true),
            (catalog, true),
            (movement, s.Can(PermissionCodes.MovementsRegisterWarehouse) || s.Can(PermissionCodes.MovementsRegisterSales)),
            (count, s.Can(PermissionCodes.PhysicalCountRecord)));
        Add(sections, "Compras y reposición",
            (alerts, true),
            (order, true),
            (purchases, s.Can(PermissionCodes.PurchasingManage)),
            (suppliers, s.Can(PermissionCodes.PurchasingManage)));
        // V4 · Multi-sucursal: tablero corporativo y mercadería entre sucursales
        Add(sections, "Sucursales",
            (branches, s.Can(PermissionCodes.ReportsView) || s.Can(PermissionCodes.BranchesManage) || s.Can(PermissionCodes.BranchesAll)),
            (transfers, s.Can(PermissionCodes.TransfersManage) || s.Can(PermissionCodes.BranchesAll)));
        Add(sections, "Análisis",
            (reports, s.Can(PermissionCodes.ReportsView)),
            (accounting, s.Can(PermissionCodes.AccountingManage)));
        Add(sections, "Administración",
            (users, s.Can(PermissionCodes.UsersManage)),
            (integrations, s.Can(PermissionCodes.IntegrationManage)),
            (activity, s.Can(PermissionCodes.AuditView)));
        Sections = sections;
        Footer = [settings, help];
        AllPages = [.. sections.SelectMany(x => x.Pages), .. Footer];
        for (var i = 0; i < Math.Min(9, AllPages.Count - 2); i++)
        {
            AllPages[i].Shortcut = $"Ctrl+{i + 1}";
        }
        Alerts = alerts;
        Search = new ProductPickerViewModel(app.Data, 10);
        Search.Picked += (_, item) =>
        {
            OpenProduct(item.Sku);
            Search.Clear();
        };
        _compact = app.Settings.CompactSidebar;
        _current = dashboard;
        dashboard.IsSelected = true;

        NavigateCommand = new RelayCommand<PageViewModel>(p => Navigate(p.Key));
        ToggleSidebar = new RelayCommand(() => IsCompact = !IsCompact);
        ToggleTheme = new RelayCommand(() => app.Theme.Apply(app.Theme.IsDark ? ThemeMode.Light : ThemeMode.Dark));
        CloseProduct = new RelayCommand(() => ProductDetail = null);
        ToggleUserMenu = new RelayCommand(() => IsUserMenuOpen = !IsUserMenuOpen);
        Logout = new AsyncRelayCommand(LogoutAsync);
        ChangePassword = new RelayCommand(RequestChangePassword);
        app.Data.Changed += async (_, _) => await UpdateBadgesAsync();
        BranchOptions = BuildBranchOptions(s);
        _branch = BranchOptions.FirstOrDefault(o => o.Id == s.Access.ActiveBranchId) ?? BranchOptions.FirstOrDefault();
        app.Theme.Changed += (_, _) => OnPropertyChanged(nameof(IsDark));
        _clock.Tick += (_, _) => UpdateClock();
        UpdateClock();
        _clock.Start();
    }

    public AppServices App => _app;

    private static void Add(List<NavSection> sections, string title, params (PageViewModel Page, bool Allowed)[] pages)
    {
        var allowed = pages.Where(p => p.Allowed).Select(p => p.Page).ToList();
        if (allowed.Count > 0)
        {
            sections.Add(new NavSection(title, allowed));
        }
    }

    /// <summary>Copia un texto (p. ej. la contraseña temporal del cuadro) al portapapeles.</summary>
    public RelayCommand<string> CopyText { get; } = new(text =>
    {
        _ = ClipboardText.TrySet(text);
    });

    public IReadOnlyList<NavSection> Sections { get; }

    public IReadOnlyList<PageViewModel> Footer { get; }

    public IReadOnlyList<PageViewModel> AllPages { get; }

    public AlertsViewModel Alerts { get; }

    public ProductPickerViewModel Search { get; }

    public NotificationService Notifications => _app.Notify;

    public DialogService Dialogs => _app.Dialogs;

    public SessionContext Session => _app.Session;

    public string UserName => Session.DisplayName;

    public string UserEmail => Session.Email;

    public string Initials => Session.Initials;

    public string RoleNames => Session.RoleNames;

    public string CompanyName => Session.Workspace.CompanyName;

    public string WarehouseText => $"{Session.Workspace.WarehouseCode} · {Session.Workspace.WarehouseName}";

    public bool IsDemo => Session.IsDemo;

    /// <summary>V4 · Conectado por el servidor M-INV en la nube.</summary>
    public bool IsCloud => Session.IsCloud;

    /// <summary>V4 · Sucursales entre las que puede cambiar (la gerencia global además ve «Todas»).</summary>
    public IReadOnlyList<BranchOption> BranchOptions { get; }

    public bool ShowBranchSelector => BranchOptions.Count > 1;

    public string BranchText => Session.BranchText;

    /// <summary>V4 · Sucursal activa: al cambiarla, el servidor recalcula el alcance y las pantallas recargan.</summary>
    public BranchOption? SelectedBranch
    {
        get => _branch;
        set
        {
            if (value is null || _changingBranch || Equals(value, _branch))
            {
                return;
            }
            _ = ChangeBranchAsync(value);
        }
    }

    public bool IsChangingBranch
    {
        get => _changingBranch;
        private set => Set(ref _changingBranch, value);
    }

    public string ConnectionText => Session.Connection.Description;

    public bool IsDark => _app.Theme.IsDark;

    public string ClockText
    {
        get => _clockText;
        private set => Set(ref _clockText, value);
    }

    public PageViewModel Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    public ProductDetailViewModel? ProductDetail
    {
        get => _product;
        private set
        {
            if (Set(ref _product, value))
            {
                OnPropertyChanged(nameof(IsProductOpen));
            }
        }
    }

    public bool IsProductOpen => _product is not null;

    public bool IsCompact
    {
        get => _compact;
        set
        {
            if (Set(ref _compact, value))
            {
                _app.Settings.CompactSidebar = value;
                _app.Settings.Save();
            }
        }
    }

    public bool IsUserMenuOpen
    {
        get => _userMenuOpen;
        set => Set(ref _userMenuOpen, value);
    }

    public RelayCommand<PageViewModel> NavigateCommand { get; }

    public RelayCommand ToggleSidebar { get; }

    public RelayCommand ToggleTheme { get; }

    public RelayCommand CloseProduct { get; }

    public RelayCommand ToggleUserMenu { get; }

    public AsyncRelayCommand Logout { get; }

    public RelayCommand ChangePassword { get; }

    /// <summary>La ventana confirma el cierre de sesión y vuelve al inicio de sesión.</summary>
    public event EventHandler? LogoutRequested;

    /// <summary>La ventana abre el cuadro de cambio de contraseña.</summary>
    public event EventHandler? ChangePasswordRequested;

    public async Task StartAsync()
    {
        await Search.EnsureLoadedAsync();
        await Current.EnsureLoadedAsync();
        await UpdateBadgesAsync();
    }

    public void Navigate(string key, object? parameter = null)
    {
        var page = AllPages.FirstOrDefault(p => p.Key == key);
        if (page is null)
        {
            _app.Notify.Warning("Sin acceso", "Su rol no tiene acceso a esa pantalla.");
            return;
        }
        IsUserMenuOpen = false;
        ProductDetail = null;   // cambiar de pantalla cierra la ficha lateral
        foreach (var p in AllPages)
        {
            p.IsSelected = ReferenceEquals(p, page);
        }
        Current = page;
        page.OnNavigatedTo(parameter);
        _ = page.EnsureLoadedAsync();
    }

    /// <summary>Atajos Ctrl+1…Ctrl+9 (en el orden del menú).</summary>
    public void NavigateByIndex(int index)
    {
        var pages = Sections.SelectMany(s => s.Pages).ToList();
        if (index >= 0 && index < pages.Count)
        {
            Navigate(pages[index].Key);
        }
    }

    public void OpenProduct(string sku)
    {
        var detail = new ProductDetailViewModel(_app, sku);
        ProductDetail = detail;
        _ = detail.LoadAsync();
    }

    public void RequestChangePassword()
    {
        IsUserMenuOpen = false;
        ChangePasswordRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lectura del escáner: la pantalla actual decide qué hacer (registro, conteo); si no, abre la ficha.</summary>
    public void OnScanned(string code)
    {
        if (Current is IScannerTarget target && target.OnScanned(code))
        {
            return;
        }
        if (Search.TrySelectCode(code))
        {
            return;
        }
        _app.Notify.Warning("Código no encontrado", $"«{code}» no es un SKU ni un código de barras del catálogo.");
    }

    private async Task LogoutAsync()
    {
        IsUserMenuOpen = false;
        if (await _app.Dialogs.ConfirmAsync("Cerrar sesión", "¿Desea cerrar su sesión en este equipo?", "Cerrar sesión", "Seguir trabajando",
                glyph: Glyphs.SignOut))
        {
            try
            {
                await _app.SendAsync(new LogoutCommand(Session.Login.SessionId));
            }
            catch (Exception ex) when (!AppServices.IsExpected(ex))
            {
                System.Diagnostics.Trace.TraceWarning("M-INV · no se pudo cerrar la sesión en la base: {0}", ex.Message);
            }
            _clock.Stop();
            LogoutRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private static List<BranchOption> BuildBranchOptions(SessionContext s)
    {
        var options = s.Access.Branches.Select(b => new BranchOption(b.Id, b.Code, $"{b.Code} · {b.Name}")).ToList();
        if (s.Access.AllBranches && options.Count > 1)
        {
            options.Insert(0, new BranchOption(null, "*", "Todas las sucursales"));
        }
        return options;
    }

    private async Task ChangeBranchAsync(BranchOption option)
    {
        var previous = _branch;
        IsChangingBranch = true;
        try
        {
            _branch = option;
            OnPropertyChanged(nameof(SelectedBranch));
            await _app.SelectBranchAsync(option.Id);
            OnPropertiesChanged(nameof(BranchText), nameof(WarehouseText));
            _app.Notify.Success("Sucursal activa", option.Id is null
                ? "Vista consolidada de todas las sucursales (para vender o mover stock, elija una sucursal)."
                : $"Ahora trabaja en {option.Label}: caja, movimientos y compras se registran aquí.");
            await Current.LoadAsync(force: true);
        }
        catch (Exception ex)
        {
            _branch = previous;
            OnPropertyChanged(nameof(SelectedBranch));
            _app.Notify.Error("No se pudo cambiar de sucursal", AppServices.Describe(ex));
        }
        finally
        {
            IsChangingBranch = false;
        }
    }

    private async Task UpdateBadgesAsync()
    {
        try
        {
            var view = await _app.Data.ProjectionAsync();
            Alerts.Badge = view.Result.Alerts.Count > 0 ? view.Result.Alerts.Count.ToString(Fmt.Culture) : null;
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Alerts.Badge = null;
        }
    }

    private void UpdateClock() =>
        ClockText = _app.Now.ToLocalTime().ToString("ddd d MMM · HH:mm", Fmt.Culture);
}

/// <summary>Pantalla que usa las lecturas del escáner (devuelve false si no la aprovechó).</summary>
public interface IScannerTarget
{
    bool OnScanned(string code);
}

/// <summary>Colección observable que se puede reemplazar de una vez (una sola notificación a la vista).</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }
}
