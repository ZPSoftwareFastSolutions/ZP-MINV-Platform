using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using MINV.Application.Iam;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.ViewModels;

public sealed class UserItem(UserRow r)
{
    public UserRow Row { get; } = r;

    public string Email => Row.Email;

    public string Name => Row.Name;

    public string Initials => Fmt.Initials(Row.Name);

    public string RoleCode => Row.Roles.FirstOrDefault() ?? "";

    public string RoleText => string.Join(" · ", Row.Roles.Select(RoleName));

    public string StatusText => !Row.IsActive ? "Inactivo" : Row.IsLocked ? "Bloqueado" : Row.MustChangePassword ? "Debe cambiar clave" : "Activo";

    public string StatusBrush => !Row.IsActive ? "TextMuted" : Row.IsLocked ? "Danger" : Row.MustChangePassword ? "Warning" : "Success";

    public string StatusSoftBrush => !Row.IsActive ? "SurfaceHover" : StatusBrush + "Soft";

    public string LastAccessText => Row.LastAccess is { } t ? t.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "Nunca ingresó";

    public static string RoleName(string code) => RoleCodes.All.FirstOrDefault(r => r.Code == code).Name ?? code;
}

/// <summary>Rol con sus permisos marcados (matriz de solo lectura: la define el aprovisionamiento).</summary>
public sealed record RoleCard(string Code, string Name, int Users, IReadOnlyList<PermissionCheck> Permissions)
{
    public string UsersText => Users == 1 ? "1 usuario" : $"{Users} usuarios";

    public string CountText => $"{Permissions.Count(p => p.Granted)} de {Permissions.Count} funciones";
}

public sealed record PermissionCheck(string Description, bool Granted);

/// <summary>
/// Administración: usuarios (alta, edición, rol en combo, activación, restablecer contraseña con clave temporal), roles
/// con su matriz de funciones y parámetros de la empresa (margen de alerta y días sin rotación en combos).
/// </summary>
public sealed class UsersViewModel : PageViewModel
{
    private static readonly Choice<string?> AllRoles = new("Todos los roles", null);
    private List<UserItem> _items = [];
    private string _tab = "users";
    private string _search = string.Empty;
    private Choice<string?> _role = AllRoles;
    private UserEditor? _editor;
    private IReadOnlyList<RoleCard> _roles = [];
    private CompanySettings? _company;
    private Choice<decimal>? _margin;
    private Choice<int>? _days;

    public UsersViewModel(AppServices app) : base(app, "usuarios", "Usuarios y roles", "Accesos, funciones por rol y empresa", Glyphs.Shield)
    {
        Rows = CollectionViewSource.GetDefaultView(_items);
        RoleFilters = [AllRoles, .. RoleCodes.All.Select(r => new Choice<string?>(r.Name, r.Code))];
        Margins = [.. new[] { 0.10m, 0.15m, 0.20m, 0.25m, 0.30m, 0.40m, 0.50m }.Select(m => new Choice<decimal>($"{m:P0} sobre el mínimo", m))];
        DaysOptions = [.. new[] { 15, 30, 45, 60, 90, 120, 180 }.Select(d => new Choice<int>($"{d} días", d))];
        New = new RelayCommand(() => Editor = new UserEditor(this, App, null));
        Edit = new RelayCommand<UserItem>(u => Editor = new UserEditor(this, App, u));
        ResetPassword = new AsyncRelayCommand<UserItem>(ResetPasswordAsync);
        SaveCompany = new AsyncRelayCommand(SaveCompanyAsync, () => _margin is not null && _days is not null);
    }

    public ICollectionView Rows { get; private set; }

    public IReadOnlyList<Choice<string?>> RoleFilters { get; }

    public IReadOnlyList<Choice<decimal>> Margins { get; }

    public IReadOnlyList<Choice<int>> DaysOptions { get; }

    public string Tab
    {
        get => _tab;
        set
        {
            if (Set(ref _tab, value))
            {
                OnPropertiesChanged(nameof(IsUsers), nameof(IsRoles), nameof(IsCompany));
            }
        }
    }

    public bool IsUsers { get => _tab == "users"; set { if (value) { Tab = "users"; } } }

    public bool IsRoles { get => _tab == "roles"; set { if (value) { Tab = "roles"; } } }

    public bool IsCompany { get => _tab == "company"; set { if (value) { Tab = "company"; } } }

    public string Search { get => _search; set { if (Set(ref _search, value ?? string.Empty)) { Rows.Refresh(); } } }

    public Choice<string?> Role { get => _role; set { if (Set(ref _role, value ?? AllRoles)) { Rows.Refresh(); } } }

    public KpiCard UsersKpi { get; } = new("Usuarios activos", Glyphs.People);

    public KpiCard RolesKpi { get; } = new("Roles", Glyphs.Shield, "Info", "InfoSoft");

    public KpiCard LockedKpi { get; } = new("Bloqueados", Glyphs.Lock, "Danger", "DangerSoft");

    public KpiCard TodayKpi { get; } = new("Ingresaron hoy", Glyphs.Clock, "Success", "SuccessSoft");

    public UserEditor? Editor
    {
        get => _editor;
        private set
        {
            if (Set(ref _editor, value))
            {
                OnPropertyChanged(nameof(IsEditing));
            }
        }
    }

    public bool IsEditing => _editor is not null;

    public IReadOnlyList<RoleCard> Roles { get => _roles; private set => Set(ref _roles, value); }

    public CompanySettings? Company { get => _company; private set => Set(ref _company, value); }

    public Choice<decimal>? Margin { get => _margin; set => Set(ref _margin, value); }

    public Choice<int>? Days { get => _days; set => Set(ref _days, value); }

    public RelayCommand New { get; }

    public RelayCommand<UserItem> Edit { get; }

    public AsyncRelayCommand<UserItem> ResetPassword { get; }

    public AsyncRelayCommand SaveCompany { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var users = await App.SendAsync(new GetUsersQuery());
        _items = users.Select(u => new UserItem(u)).OrderBy(u => RoleOrder(u.RoleCode)).ThenBy(u => u.Name, StringComparer.Create(Fmt.Culture, true)).ToList();
        Rows = CollectionViewSource.GetDefaultView(_items);
        Rows.Filter = o => o is UserItem u && (_role.Value is not { } r || u.Row.Roles.Contains(r))
                                           && (_search.Trim().Length == 0
                                               || u.Email.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase)
                                               || Fmt.Culture.CompareInfo.IndexOf(u.Name, _search.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
        OnPropertyChanged(nameof(Rows));
        var today = DateOnly.FromDateTime(App.Now.ToLocalTime().DateTime);
        UsersKpi.Value = _items.Count(i => i.Row.IsActive).ToString("N0", Fmt.Culture);
        UsersKpi.Detail = $"{_items.Count} registrados";
        LockedKpi.Value = _items.Count(i => i.Row.IsLocked).ToString("N0", Fmt.Culture);
        LockedKpi.Detail = "Por intentos fallidos";
        TodayKpi.Value = _items.Count(i => i.Row.LastAccess is { } t && DateOnly.FromDateTime(t.ToLocalTime().DateTime) == today).ToString("N0", Fmt.Culture);

        var roles = await App.SendAsync(new GetRolesQuery());
        Roles = roles.Roles.OrderBy(r => RoleOrder(r.Code)).Select(r => new RoleCard(r.Code, r.Name, r.Users,
            roles.Permissions.Select(p => new PermissionCheck(p.Description, r.Permissions.Contains(p.Code))).ToList())).ToList();
        RolesKpi.Value = roles.Roles.Count.ToString("N0", Fmt.Culture);
        RolesKpi.Detail = $"{roles.Permissions.Count} funciones controladas";

        var company = await App.SendAsync(new GetCompanySettingsQuery());
        Company = company;
        Margin = Margins.FirstOrDefault(m => m.Value == company.AlertMargin) ?? Margins[2];
        Days = DaysOptions.FirstOrDefault(d => d.Value == company.DaysWithoutRotation) ?? DaysOptions[3];
    }

    internal void CloseEditor() => Editor = null;

    internal async Task AfterSaveAsync()
    {
        Editor = null;
        await LoadAsync(force: true);
    }

    private static int RoleOrder(string code) => RoleCodes.All.Select((r, i) => (r.Code, i)).FirstOrDefault(x => x.Code == code).i;

    private async Task ResetPasswordAsync(UserItem user)
    {
        if (!await App.Dialogs.ConfirmAsync($"Restablecer la contraseña de {user.Name}",
                "Se genera una contraseña temporal; la persona deberá cambiarla al ingresar. También se desbloquea la cuenta.",
                "Generar contraseña", glyph: Glyphs.Key))
        {
            return;
        }
        var password = Numbers.NewPassword();
        try
        {
            await App.SendAsync(new ResetUserPasswordCommand(user.Email, password));
            await App.Dialogs.ShowSecretAsync("Contraseña temporal", $"Entréguela a {user.Name} ({user.Email}). Se muestra una sola vez.", password);
            await LoadAsync(force: true);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo restablecer", AppServices.Describe(ex));
        }
    }

    private async Task SaveCompanyAsync()
    {
        if (await RunAsync(() => App.SendAsync(new UpdateCompanySettingsCommand(_margin!.Value, _days!.Value)), "No se pudo guardar"))
        {
            App.Notify.Success("Parámetros guardados", $"Alerta {_margin!.Label} · rotación {_days!.Label}. El semáforo y el pedido se recalculan.");
            App.Data.Invalidate();
            await LoadAsync(force: true);
        }
    }
}

public sealed class UserEditor : ObservableObject
{
    private readonly UsersViewModel _owner;
    private readonly AppServices _app;
    private readonly string? _originalEmail;
    private string _email;
    private string _name;
    private Choice<string>? _role;
    private bool _isActive;
    private string? _password;
    private string? _error;

    public UserEditor(UsersViewModel owner, AppServices app, UserItem? user)
    {
        _owner = owner;
        _app = app;
        _originalEmail = user?.Email;
        _email = user?.Email ?? string.Empty;
        _name = user?.Name ?? string.Empty;
        Roles = [.. RoleCodes.All.Select(r => new Choice<string>($"{r.Name} · {Describe(r.Code)}", r.Code))];
        _role = Roles.FirstOrDefault(r => r.Value == user?.RoleCode) ?? Roles.FirstOrDefault(r => r.Value == RoleCodes.Sales);
        _isActive = user?.Row.IsActive ?? true;
        if (user is null)
        {
            _password = Numbers.NewPassword();
        }
        Save = new AsyncRelayCommand(SaveAsync);
        Cancel = new RelayCommand(owner.CloseEditor);
        Regenerate = new RelayCommand(() => Password = Numbers.NewPassword());
    }

    public bool IsNew => _originalEmail is null;

    public bool IsSelf => string.Equals(_originalEmail, _app.Session.Email, StringComparison.OrdinalIgnoreCase);

    public string Heading => IsNew ? "Nuevo usuario" : "Editar usuario";

    public IReadOnlyList<Choice<string>> Roles { get; }

    public string Email { get => _email; set => Set(ref _email, value ?? string.Empty); }

    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }

    public Choice<string>? Role { get => _role; set => Set(ref _role, value); }

    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    /// <summary>Contraseña temporal del usuario nuevo (se muestra para entregarla; debe cambiarla al ingresar).</summary>
    public string? Password { get => _password; private set => Set(ref _password, value); }

    public string? Error { get => _error; private set => Set(ref _error, value); }

    public AsyncRelayCommand Save { get; }

    public RelayCommand Cancel { get; }

    public RelayCommand Regenerate { get; }

    public static string Describe(string role) => role switch
    {
        RoleCodes.Admin => "todo el sistema",
        RoleCodes.Management => "reportes, contabilidad y compras",
        RoleCodes.Warehouse => "stock, conteos y compras",
        RoleCodes.Sales => "ventas, clientes y salidas",
        RoleCodes.Cashier => "punto de venta y caja",
        RoleCodes.ReadOnly => "solo consulta",
        _ => "",
    };

    private async Task SaveAsync()
    {
        Error = null;
        if (_role is null)
        {
            Error = "Elija el rol.";
            return;
        }
        if (IsSelf && (!_isActive || _role.Value != RoleCodes.Admin))
        {
            Error = "No puede desactivarse ni quitarse el rol de administrador a sí mismo.";
            return;
        }
        try
        {
            var email = await _app.SendAsync(new SaveUserCommand(_originalEmail, _email.Trim(), _name.Trim(), _role.Value, _isActive, IsNew ? _password : null));
            if (IsNew)
            {
                await _app.Dialogs.ShowSecretAsync("Usuario creado", $"{_name.Trim()} ({email}) ya puede ingresar con esta contraseña temporal. " +
                                                                    "Se muestra una sola vez: entréguela y pida que la cambie.", _password!);
            }
            else
            {
                _app.Notify.Success("Usuario actualizado", $"{_name.Trim()} · {UserItem.RoleName(_role.Value)}");
            }
            await _owner.AfterSaveAsync();
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Error = AppServices.Describe(ex);
        }
    }
}
