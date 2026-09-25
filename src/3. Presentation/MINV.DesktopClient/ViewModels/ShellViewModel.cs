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

    public ShellViewModel(AppServices app, DashboardViewModel dashboard, StockViewModel stock, MovementViewModel movement,
        PhysicalCountViewModel count, AlertsViewModel alerts, OrderViewModel order, ActivityViewModel activity,
        SettingsViewModel settings, HelpViewModel help)
    {
        _app = app;
        app.Navigator = this;
        var s = app.Session;
        var inventory = new List<PageViewModel> { stock };
        if (s.Can(PermissionCodes.MovementsRegisterWarehouse) || s.Can(PermissionCodes.MovementsRegisterSales))
        {
            inventory.Add(movement);
        }
        if (s.Can(PermissionCodes.PhysicalCountRecord))
        {
            inventory.Add(count);
        }
        var sections = new List<NavSection>
        {
            new("General", [dashboard]),
            new("Inventario", inventory),
            new("Reposición", [alerts, order]),
        };
        if (s.Can(PermissionCodes.AuditView))
        {
            sections.Add(new NavSection("Control", [activity]));
        }
        Sections = sections;
        Footer = [settings, help];
        AllPages = [.. sections.SelectMany(x => x.Pages), .. Footer];
        for (var i = 0; i < AllPages.Count - 2; i++)
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
        app.Theme.Changed += (_, _) => OnPropertyChanged(nameof(IsDark));
        _clock.Tick += (_, _) => UpdateClock();
        UpdateClock();
        _clock.Start();
    }

    public AppServices App => _app;

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
