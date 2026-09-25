using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.DesktopClient.Hosting;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Infrastructure.Demo;
using MINV.Infrastructure.Persistence;

namespace MINV.DesktopClient.ViewModels;

public enum StepState
{
    Pending,
    Running,
    Done,
    Warning,
}

/// <summary>Paso de la pantalla de carga.</summary>
public sealed class StartupStep(string text) : ObservableObject
{
    private StepState _state;
    private string _text = text;

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public StepState State
    {
        get => _state;
        set => Set(ref _state, value);
    }
}

/// <summary>Pantalla de carga: prepara la aplicación y comprueba la base de datos antes del inicio de sesión.</summary>
public sealed class SplashViewModel : ObservableObject
{
    private double _progress;
    private string _status = "Iniciando…";

    public IReadOnlyList<StartupStep> Steps { get; } =
    [
        new("Preparando la aplicación"),
        new("Cargando sus preferencias"),
        new("Comprobando la base de datos"),
        new("Listo para ingresar"),
    ];

    public double Progress
    {
        get => _progress;
        set => Set(ref _progress, value);
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string Version => "Versión " + MINV.DesktopClient.App.Version;

    public void Begin(int index, string status)
    {
        Steps[index].State = StepState.Running;
        Status = status;
    }

    public void Complete(int index, bool warning = false, string? text = null)
    {
        Steps[index].State = warning ? StepState.Warning : StepState.Done;
        if (text is not null)
        {
            Steps[index].Text = text;
        }
    }
}

/// <summary>Usuario de la demostración con la descripción de lo que puede hacer su rol.</summary>
public sealed record DemoUserOption(string Email, string Name, string RoleCode, string RoleName, string Description)
{
    public string Initials => Fmt.Initials(Name);
}

/// <summary>
/// Inicio de sesión: empresa, correo y contraseña (la contraseña no se enlaza: la vista la entrega solo al ingresar,
/// regla A-09). Muestra el estado de la base de datos y permite entrar a la demostración con los datos de la V2.1.
/// </summary>
public sealed class LoginViewModel : ObservableObject
{
    private readonly ClientHost _host;
    private readonly ClientSettings _settings;
    private string _tenantCode;
    private string _email;
    private bool _remember;
    private string? _error;
    private bool _busy;
    private DatabaseStatus? _db;
    private bool _checkingDb;
    private bool _capsLock;
    private bool _demoOpen;
    private bool _demoBusy;
    private string _demoStatus = string.Empty;
    private DemoSession? _demo;

    public LoginViewModel(ClientHost host, ClientSettings settings, DatabaseStatus? status)
    {
        _host = host;
        _settings = settings;
        _db = status;
        _remember = settings.Remember;
        _tenantCode = (settings.Remember ? settings.TenantCode : null) ?? host.DefaultTenantCode;
        _email = (settings.Remember ? settings.Email : null) ?? string.Empty;
        RetryDb = new AsyncRelayCommand(CheckDbAsync);
        OpenDemo = new AsyncRelayCommand(OpenDemoAsync);
        CloseDemo = new RelayCommand(() => IsDemoOpen = false);
        EnterDemo = new AsyncRelayCommand<DemoUserOption>(EnterDemoAsync);
    }

    public string TenantCode
    {
        get => _tenantCode;
        set => Set(ref _tenantCode, (value ?? "").ToUpperInvariant());
    }

    public string Email
    {
        get => _email;
        set => Set(ref _email, value ?? "");
    }

    public bool Remember
    {
        get => _remember;
        set => Set(ref _remember, value);
    }

    public string? Error
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

    public bool IsBusy
    {
        get => _busy;
        private set => Set(ref _busy, value);
    }

    public bool CapsLockOn
    {
        get => _capsLock;
        set => Set(ref _capsLock, value);
    }

    public DatabaseStatus? Db
    {
        get => _db;
        private set
        {
            if (Set(ref _db, value))
            {
                OnPropertiesChanged(nameof(DbText), nameof(DbDetail), nameof(DbReady), nameof(DbProblem));
            }
        }
    }

    public bool IsCheckingDb
    {
        get => _checkingDb;
        private set => Set(ref _checkingDb, value);
    }

    public bool DbReady => _db?.IsReady == true;

    public bool DbProblem => _db is not null && !_db.IsReady;

    public string DbText => _db is null ? "Comprobando la base de datos…" : _db.IsReady ? $"PostgreSQL {_db.Version} conectado" : "Sin conexión con la base de datos";

    public string DbDetail => _db is null ? "" : _db.IsReady ? $"{_db.Server} · {_db.Database}" : _db.Message;

    // -------------------------------------------------------------------------------------------- demostración
    public bool IsDemoOpen
    {
        get => _demoOpen;
        set => Set(ref _demoOpen, value);
    }

    public bool IsDemoBusy
    {
        get => _demoBusy;
        private set => Set(ref _demoBusy, value);
    }

    public string DemoStatus
    {
        get => _demoStatus;
        private set => Set(ref _demoStatus, value);
    }

    public string DemoCompany => _demo?.CompanyName ?? "";

    public string DemoSummary => _demo is { } d
        ? $"{d.Import.Report.Products} productos · {d.Import.Report.Movements} movimientos · {d.Import.Report.Suppliers} proveedores · " +
          (d.Import.Parity.Ok ? "paridad con la V2.1 ✔" : "paridad con la V2.1 con diferencias")
        : "";

    public BulkObservableCollection<DemoUserOption> DemoUsers { get; } = [];

    /// <summary>Lo que ofrece M-INV (panel de marca).</summary>
    public IReadOnlyList<HelpEntry> Features { get; } =
    [
        new("Stock al instante", "Sin «Recalcular»: cada movimiento se refleja ya", Controls.Glyphs.Box),
        new("Alertas y pedido sugerido", "Qué reponer y a qué proveedor, por prioridad", Controls.Glyphs.Cart),
        new("Nada se borra", "Auditoría inmutable de cada operación", Controls.Glyphs.Shield),
    ];

    public AsyncRelayCommand RetryDb { get; }

    public AsyncRelayCommand OpenDemo { get; }

    public RelayCommand CloseDemo { get; }

    public AsyncRelayCommand<DemoUserOption> EnterDemo { get; }

    /// <summary>Sesión abierta (la ventana se cierra y la aplicación abre la ventana principal).</summary>
    public SessionHandle? Result { get; private set; }

    public event EventHandler? SignedIn;

    public async Task CheckDbAsync()
    {
        IsCheckingDb = true;
        Db = null;
        try
        {
            Db = await _host.ProbeAsync();
        }
        finally
        {
            IsCheckingDb = false;
        }
    }

    public async Task<bool> LoginAsync(string password)
    {
        if (IsBusy)
        {
            return false;
        }
        IsBusy = true;
        Error = null;
        try
        {
            Result = await _host.SignInAsync(TenantCode.Trim(), Email.Trim(), password);
            _settings.Remember = Remember;
            _settings.TenantCode = Remember ? TenantCode.Trim() : null;
            _settings.Email = Remember ? Email.Trim() : null;
            _settings.Save();
            SignedIn?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex) when (ex is AuthenticationFailedException or RequestValidationException)
        {
            Error = ex is RequestValidationException v ? string.Join(" ", v.Errors) : ex.Message;
        }
        catch (Exception ex)
        {
            Error = "No se pudo conectar con la base de datos. " + (_db is { IsReady: false } db ? db.Message : ex.GetBaseException().Message) +
                    " · Puede probar el sistema con la demostración.";
        }
        finally
        {
            IsBusy = false;
        }
        return false;
    }

    private async Task OpenDemoAsync()
    {
        IsDemoOpen = true;
        if (_demo is not null)
        {
            return;
        }
        IsDemoBusy = true;
        DemoStatus = "Migrando el libro de la V2.1 a la demostración (productos, proveedores, 473 movimientos y la auditoría)…";
        try
        {
            _demo = await Task.Run(() => _host.PrepareDemoAsync());
            DemoUsers.ReplaceAll(_demo.Users.Select(u => new DemoUserOption(u.Email, u.DisplayName, u.RoleCode, u.RoleName, Describe(u.RoleCode))));
            OnPropertiesChanged(nameof(DemoCompany), nameof(DemoSummary));
            DemoStatus = string.Empty;
        }
        catch (Exception ex)
        {
            DemoStatus = "No se pudo preparar la demostración: " + ex.GetBaseException().Message;
        }
        finally
        {
            IsDemoBusy = false;
        }
    }

    private async Task EnterDemoAsync(DemoUserOption user)
    {
        if (_demo is null || IsBusy)
        {
            return;
        }
        IsBusy = true;
        Error = null;
        try
        {
            Result = await _host.SignInDemoAsync(_demo, user.Email);
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            DemoStatus = "No se pudo ingresar: " + ex.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Qué puede hacer cada rol (matriz RBAC por defecto).</summary>
    public static string Describe(string roleCode) => roleCode switch
    {
        RoleCodes.Admin => "Todo: registrar, contar, auditar y configurar",
        RoleCodes.Warehouse => "Entradas, ajustes y toma física",
        RoleCodes.Sales => "Salidas y consulta de stock",
        RoleCodes.Cashier => "Caja, ventas y consulta de stock",
        RoleCodes.Management => "Tablero, auditoría y costos",
        RoleCodes.ReadOnly => "Solo consulta",
        _ => "Sin permisos asignados",
    };

    /// <summary>Solo para las capturas automáticas: fija el estado de la base sin conectarse.</summary>
    internal void SetDbStatus(DatabaseStatus status) => Db = status;
}

/// <summary>Cambio de contraseña (obligatorio si el administrador la asignó con «debe cambiarla»).</summary>
public sealed class ChangePasswordViewModel(AppServices app, bool mandatory) : ObservableObject
{
    private string? _error;
    private bool _busy;

    public bool IsMandatory { get; } = mandatory;

    public string Intro => IsMandatory
        ? "El administrador le asignó una contraseña temporal. Elija una propia para continuar."
        : "Elija una contraseña de al menos 8 caracteres que combine letras y números.";

    public string? Error
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

    public bool IsBusy
    {
        get => _busy;
        private set => Set(ref _busy, value);
    }

    public async Task<bool> ChangeAsync(string current, string next, string confirm)
    {
        Error = null;
        if (next != confirm)
        {
            Error = "La confirmación no coincide con la nueva contraseña.";
            return false;
        }
        IsBusy = true;
        try
        {
            await app.SendAsync(new ChangePasswordCommand(current, next));
            app.Notify.Success("Contraseña actualizada", "Úsela la próxima vez que ingrese.");
            return true;
        }
        catch (Exception ex)
        {
            Error = AppServices.Describe(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
