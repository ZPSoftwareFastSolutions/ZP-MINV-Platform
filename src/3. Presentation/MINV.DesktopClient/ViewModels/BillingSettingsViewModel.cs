using System.Globalization;
using MINV.Application.Billing;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Billing;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Sucursal de M-INV con su código del Padrón (fila editable).</summary>
public sealed class SiatBranchItem : ObservableObject
{
    private string _siatCode;
    private string _municipality;
    private string _phone;

    public SiatBranchItem(SiatBranchView row)
    {
        Row = row;
        _siatCode = row.SiatCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _municipality = row.Municipality ?? string.Empty;
        _phone = row.Phone ?? string.Empty;
    }

    public SiatBranchView Row { get; }

    public string BranchText => $"{Row.BranchCode} · {Row.BranchName}";

    public bool IsMapped => Row.SiatCode is not null;

    public string StatusText => IsMapped ? (Row.SiatCode == 0 ? "Casa matriz" : "Sucursal del Padrón") : "Sin código del Padrón";

    public string StatusBrush => IsMapped ? "Success" : "Warning";

    public string StatusSoftBrush => StatusBrush + "Soft";

    public string SiatCode { get => _siatCode; set => Set(ref _siatCode, value ?? string.Empty); }

    public string Municipality { get => _municipality; set => Set(ref _municipality, value ?? string.Empty); }

    public string Phone { get => _phone; set => Set(ref _phone, value ?? string.Empty); }
}

/// <summary>
/// V4.1 · «Facturación SIAT» (Administración): datos del Padrón y del sistema autorizado, ambiente (pruebas o producción),
/// conexión con el SIN (URL de cada servicio, namespace, QR, tiempo de espera y el TOKEN delegado, que solo se escribe y
/// nunca se muestra), sucursales del Padrón, leyendas de modo y correo SMTP (contraseña de solo escritura). Las
/// contraseñas y el token se leen del PasswordBox solo al guardar (regla A-09) y se cifran en el servidor (F-12).
/// </summary>
public sealed class BillingSettingsViewModel : PageViewModel
{
    public const string SimulatorUrl = "http://localhost:5095";
    public const string PilotUrl = "https://pilotosiatservicios.impuestos.gob.bo";
    public const string ProductionUrl = "https://siatrest.impuestos.gob.bo";
    public const string PilotQrUrl = "https://pilotosiat.impuestos.gob.bo/consulta/QR";

    private SiatSettingsView? _view;
    private string _nit = string.Empty;
    private string _businessName = string.Empty;
    private string _systemCode = string.Empty;
    private int _environment = SiatCodes.EnvironmentTest;
    private bool _enabled;
    private string _onlineLegend = string.Empty;
    private string _offlineLegend = string.Empty;
    private int _profileEnvironment = SiatCodes.EnvironmentTest;
    private string _codesUrl = string.Empty;
    private string _syncUrl = string.Empty;
    private string _operationsUrl = string.Empty;
    private string _purchaseSaleUrl = string.Empty;
    private string _computerizedUrl = string.Empty;
    private string _adjustmentUrl = string.Empty;
    private string _namespace = "https://siat.impuestos.gob.bo/";
    private string _qrUrl = string.Empty;
    private string _timeout = "15";
    private string _tokenValidUntil = string.Empty;
    private string _mailHost = string.Empty;
    private string _mailPort = "587";
    private bool _mailSsl = true;
    private string _mailUser = string.Empty;
    private string _mailFrom = string.Empty;
    private string _mailFromName = string.Empty;
    private bool _mailEnabled;
    private string? _connectionResult;
    private string _connectionBrush = "Info";

    public BillingSettingsViewModel(AppServices app)
        : base(app, "facturacion-siat", "Facturación SIAT", "Padrón, ambiente, conexión con el SIN, sucursales, leyendas y correo", Glyphs.Shield)
    {
        SaveSettings = new AsyncRelayCommand(SaveSettingsAsync, () => CanConfigure);
        UseSimulator = new RelayCommand(() => ApplyPattern(SimulatorUrl, PilotQrUrl), () => CanConfigure);
        UsePattern = new RelayCommand(() => ApplyPattern(_profileEnvironment == SiatCodes.EnvironmentProduction ? ProductionUrl : PilotUrl,
            _profileEnvironment == SiatCodes.EnvironmentProduction ? string.Empty : PilotQrUrl), () => CanConfigure);
        TestConnection = new AsyncRelayCommand(TestConnectionAsync);
        SaveBranch = new AsyncRelayCommand<SiatBranchItem>(SaveBranchAsync, _ => CanConfigure);
        GoToStatus = new RelayCommand(() => App.Navigator.Navigate("estado-siat"));
        UseTest = new RelayCommand(() => Environment = SiatCodes.EnvironmentTest, () => CanConfigure);
        UseProduction = new RelayCommand(() => Environment = SiatCodes.EnvironmentProduction, () => CanConfigure);
    }

    public bool CanConfigure => App.Session.Can(PermissionCodes.BillingConfigure);

    public bool IsCloud => App.Session.IsCloud;

    public bool ModuleActive => _view?.ModuleActive == true;

    public bool ModuleMissing => HasLoaded && !ModuleActive;

    // -------------------------------------------------------------------------------------------- datos del Padrón
    public string Nit { get => _nit; set => Set(ref _nit, value ?? string.Empty); }

    public string BusinessName { get => _businessName; set => Set(ref _businessName, value ?? string.Empty); }

    public string SystemCode { get => _systemCode; set => Set(ref _systemCode, value ?? string.Empty); }

    /// <summary>Ambiente activo de la empresa: 2 = pruebas (piloto), 1 = producción.</summary>
    public int Environment
    {
        get => _environment;
        set
        {
            if (Set(ref _environment, value))
            {
                OnPropertiesChanged(nameof(IsTest), nameof(IsProduction));
                ProfileEnvironment = value;
            }
        }
    }

    public bool IsTest { get => _environment == SiatCodes.EnvironmentTest; set { if (value) { Environment = SiatCodes.EnvironmentTest; } } }

    public bool IsProduction { get => _environment == SiatCodes.EnvironmentProduction; set { if (value) { Environment = SiatCodes.EnvironmentProduction; } } }

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    public string OnlineLegend { get => _onlineLegend; set => Set(ref _onlineLegend, value ?? string.Empty); }

    public string OfflineLegend { get => _offlineLegend; set => Set(ref _offlineLegend, value ?? string.Empty); }

    public string StateText => _view is { } v
        ? !v.Configured ? "Sin configurar: complete los datos y guarde."
        : v.IsEnabled ? $"ACTIVA en el ambiente {v.Environment} ({(v.Environment == SiatCodes.EnvironmentTest ? "pruebas" : "producción")}): las ventas emiten factura."
        : "Guardada pero DESACTIVADA: las ventas salen sin documento fiscal."
        : string.Empty;

    public string StateBrush => _view is { IsEnabled: true } ? "Success" : "Warning";

    public string StateSoftBrush => StateBrush + "Soft";

    public string ClockText => _view?.ClockSyncedAt is { } at
        ? $"Hora sincronizada con el SIN {Fmt.Relative(at, App.Now)}: {FiscalText.ClockOffset(_view.ClockOffsetMs)} (las facturas llevan la hora del SIN)."
        : "La hora todavía no se sincronizó con el SIN («Preparar SIAT» en Estado SIAT).";

    // -------------------------------------------------------------------------------------------- conexión
    /// <summary>Ambiente cuya conexión se edita (por defecto, el activo).</summary>
    public int ProfileEnvironment
    {
        get => _profileEnvironment;
        set
        {
            if (Set(ref _profileEnvironment, value))
            {
                LoadProfile();
                OnPropertiesChanged(nameof(ProfileTitle), nameof(IsProfileTest), nameof(IsProfileProduction));
            }
        }
    }

    public bool IsProfileTest { get => _profileEnvironment == SiatCodes.EnvironmentTest; set { if (value) { ProfileEnvironment = SiatCodes.EnvironmentTest; } } }

    public bool IsProfileProduction
    {
        get => _profileEnvironment == SiatCodes.EnvironmentProduction;
        set
        {
            if (value)
            {
                ProfileEnvironment = SiatCodes.EnvironmentProduction;
            }
        }
    }

    public string ProfileTitle => _profileEnvironment == SiatCodes.EnvironmentTest ? "Conexión del ambiente de pruebas (2)" : "Conexión del ambiente de producción (1)";

    public string CodesUrl { get => _codesUrl; set => Set(ref _codesUrl, value ?? string.Empty); }

    public string SyncUrl { get => _syncUrl; set => Set(ref _syncUrl, value ?? string.Empty); }

    public string OperationsUrl { get => _operationsUrl; set => Set(ref _operationsUrl, value ?? string.Empty); }

    public string PurchaseSaleUrl { get => _purchaseSaleUrl; set => Set(ref _purchaseSaleUrl, value ?? string.Empty); }

    public string ComputerizedUrl { get => _computerizedUrl; set => Set(ref _computerizedUrl, value ?? string.Empty); }

    public string AdjustmentUrl { get => _adjustmentUrl; set => Set(ref _adjustmentUrl, value ?? string.Empty); }

    public string Namespace { get => _namespace; set => Set(ref _namespace, value ?? string.Empty); }

    public string QrUrl { get => _qrUrl; set => Set(ref _qrUrl, value ?? string.Empty); }

    public string Timeout { get => _timeout; set => Set(ref _timeout, value ?? string.Empty); }

    /// <summary>Vigencia del token («Hasta» elegido en el Portal SIAT), dd/MM/yyyy.</summary>
    public string TokenValidUntil { get => _tokenValidUntil; set => Set(ref _tokenValidUntil, value ?? string.Empty); }

    /// <summary>El token nunca vuelve del servidor: solo se sabe si hay uno guardado y hasta cuándo vale.</summary>
    public string TokenStatus => ProfileView is { HasToken: true } p
        ? $"Hay un token guardado (cifrado){(p.TokenValidUntil is { } until ? $", vigente hasta el {until:dd/MM/yyyy}" : string.Empty)}" +
          (p.TokenUpdatedAt is { } at ? $" · cargado {Fmt.Relative(at, App.Now)}" : string.Empty) + ". Escriba uno nuevo solo para reemplazarlo."
        : "No hay token guardado para este ambiente: genérelo en el Portal SIAT (Token Delegado) y péguelo aquí.";

    public string TokenBrush => ProfileView is { HasToken: true } ? "Success" : "Warning";

    public string TokenSoftBrush => TokenBrush + "Soft";

    public string KeysHint => IsCloud
        ? "En modo nube el token viaja al servidor y se cifra allí: este equipo nunca lo guarda."
        : "En modo Base local el token se cifra con la clave maestra de este equipo (MINV_INTEGRATION_KEYS o el archivo claves-integracion.txt de %LOCALAPPDATA%\\M-INV).";

    public string? ConnectionResult
    {
        get => _connectionResult;
        private set
        {
            if (Set(ref _connectionResult, value))
            {
                OnPropertyChanged(nameof(HasConnectionResult));
            }
        }
    }

    public bool HasConnectionResult => _connectionResult is not null;

    public string ConnectionBrush { get => _connectionBrush; private set => Set(ref _connectionBrush, value); }

    public string ConnectionSoftBrush => _connectionBrush + "Soft";

    private SiatProfileView? ProfileView => _view?.Profiles.FirstOrDefault(p => p.Environment == _profileEnvironment);

    // -------------------------------------------------------------------------------------------- sucursales y correo
    public BulkObservableCollection<SiatBranchItem> Branches { get; } = [];

    public string MailHost { get => _mailHost; set => Set(ref _mailHost, value ?? string.Empty); }

    public string MailPort { get => _mailPort; set => Set(ref _mailPort, value ?? string.Empty); }

    public bool MailSsl { get => _mailSsl; set => Set(ref _mailSsl, value); }

    public string MailUser { get => _mailUser; set => Set(ref _mailUser, value ?? string.Empty); }

    public string MailFrom { get => _mailFrom; set => Set(ref _mailFrom, value ?? string.Empty); }

    public string MailFromName { get => _mailFromName; set => Set(ref _mailFromName, value ?? string.Empty); }

    public bool MailEnabled { get => _mailEnabled; set => Set(ref _mailEnabled, value); }

    public string MailPasswordStatus => _view?.Mail is { HasPassword: true }
        ? "Hay una contraseña guardada (cifrada). Escriba otra solo para reemplazarla."
        : "Sin contraseña guardada.";

    public AsyncRelayCommand SaveSettings { get; }

    public RelayCommand UseSimulator { get; }

    public RelayCommand UsePattern { get; }

    public AsyncRelayCommand TestConnection { get; }

    public AsyncRelayCommand<SiatBranchItem> SaveBranch { get; }

    public RelayCommand GoToStatus { get; }

    public RelayCommand UseTest { get; }

    public RelayCommand UseProduction { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        var view = await App.SendAsync(new GetSiatSettingsQuery());
        _view = view;
        _nit = view.Nit?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _businessName = view.BusinessName ?? App.Session.Workspace.CompanyName;
        _systemCode = view.SystemCode ?? string.Empty;
        _environment = view.Environment;
        _enabled = view.IsEnabled;
        _onlineLegend = view.OnlineLegend;
        _offlineLegend = view.OfflineLegend;
        _profileEnvironment = view.Environment;
        LoadProfile();
        Branches.ReplaceAll(view.Branches.Select(b => new SiatBranchItem(b)));
        if (view.Mail is { } mail)
        {
            _mailHost = mail.Host;
            _mailPort = mail.Port.ToString(CultureInfo.InvariantCulture);
            _mailSsl = mail.UseSsl;
            _mailUser = mail.UserName ?? string.Empty;
            _mailFrom = mail.FromAddress;
            _mailFromName = mail.FromName;
            _mailEnabled = mail.IsEnabled;
        }
        else
        {
            _mailFromName = view.BusinessName ?? App.Session.Workspace.CompanyName;
        }
        OnPropertiesChanged(nameof(Nit), nameof(BusinessName), nameof(SystemCode), nameof(Environment), nameof(IsTest), nameof(IsProduction), nameof(Enabled),
            nameof(OnlineLegend), nameof(OfflineLegend), nameof(ProfileEnvironment), nameof(IsProfileTest), nameof(IsProfileProduction), nameof(ProfileTitle),
            nameof(StateText), nameof(StateBrush), nameof(StateSoftBrush), nameof(ClockText), nameof(ModuleActive), nameof(ModuleMissing), nameof(MailHost),
            nameof(MailPort), nameof(MailSsl), nameof(MailUser), nameof(MailFrom), nameof(MailFromName), nameof(MailEnabled), nameof(MailPasswordStatus));
    }

    private void LoadProfile()
    {
        var profile = ProfileView;
        var endpoints = profile?.Endpoints;
        _codesUrl = endpoints?.Codes ?? string.Empty;
        _syncUrl = endpoints?.Sync ?? string.Empty;
        _operationsUrl = endpoints?.Operations ?? string.Empty;
        _purchaseSaleUrl = endpoints?.PurchaseSale ?? string.Empty;
        _computerizedUrl = endpoints?.Computerized ?? string.Empty;
        _adjustmentUrl = endpoints?.Adjustment ?? string.Empty;
        _namespace = endpoints?.Namespace ?? "https://siat.impuestos.gob.bo/";
        _qrUrl = profile?.QrBaseUrl ?? string.Empty;
        _timeout = (profile?.TimeoutSeconds ?? 15).ToString(CultureInfo.InvariantCulture);
        _tokenValidUntil = profile?.TokenValidUntil?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
        OnPropertiesChanged(nameof(CodesUrl), nameof(SyncUrl), nameof(OperationsUrl), nameof(PurchaseSaleUrl), nameof(ComputerizedUrl), nameof(AdjustmentUrl),
            nameof(Namespace), nameof(QrUrl), nameof(Timeout), nameof(TokenValidUntil), nameof(TokenStatus), nameof(TokenBrush), nameof(TokenSoftBrush));
    }

    /// <summary>«Usar el simulador local» / «Usar el patrón del SIN»: llena las URL con <see cref="SiatEndpointSet.ForBaseUrl"/>.</summary>
    private void ApplyPattern(string baseUrl, string qr)
    {
        var set = SiatEndpointSet.ForBaseUrl(baseUrl);
        CodesUrl = set.Codes;
        SyncUrl = set.Sync;
        OperationsUrl = set.Operations;
        PurchaseSaleUrl = set.PurchaseSale;
        ComputerizedUrl = set.Computerized;
        AdjustmentUrl = set.Adjustment;
        Namespace = set.Namespace;
        if (qr.Length > 0 || QrUrl.Length == 0)
        {
            QrUrl = qr;
        }
        App.Notify.Info("URL cargadas", baseUrl == SimulatorUrl
            ? "Simulador del SIN en este equipo (tools: MINV.SiatSimulator, puerto 5095). Guarde la conexión para usarlo."
            : "Patrón conocido de los servicios del SIN: confírmelo con el WSDL del ambiente y guarde la conexión.");
    }

    private async Task SaveSettingsAsync()
    {
        if (!long.TryParse(_nit.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var nit))
        {
            App.Notify.Warning("NIT no válido", "El NIT lleva solo números (tal como figura en el Padrón).");
            return;
        }
        if (_environment == SiatCodes.EnvironmentProduction && _view?.Environment != SiatCodes.EnvironmentProduction
            && !await App.Dialogs.ConfirmAsync("Pasar a PRODUCCIÓN", "En producción cada factura tiene VALOR LEGAL ante el SIN. Solo se usa después de aprobar " +
                "las pruebas del piloto y la inspección del SIN, con el token y las URL de producción.", "Usar producción", isDanger: true, glyph: Glyphs.Warning))
        {
            return;
        }
        if (await RunAsync(async () =>
            {
                var message = await App.SendAsync(new SaveSiatSettingsCommand(nit, _businessName.Trim(), _systemCode.Trim(), _environment,
                    string.IsNullOrWhiteSpace(_onlineLegend) ? null : _onlineLegend.Trim(), string.IsNullOrWhiteSpace(_offlineLegend) ? null : _offlineLegend.Trim(),
                    _enabled));
                App.Notify.Success("Facturación SIAT guardada", FiscalText.Plain(message));
            }, "No se pudo guardar la configuración"))
        {
            await App.RefreshBillingAsync();
            await LoadAsync(force: true);
        }
    }

    /// <summary>Guarda la conexión del ambiente. El token (si se escribió) llega del PasswordBox y no se conserva.</summary>
    public async Task<bool> SaveProfileAsync(string? newToken)
    {
        if (!int.TryParse(_timeout.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var timeout))
        {
            App.Notify.Warning("Tiempo de espera", "Escriba los segundos de espera (de 3 a 120).");
            return false;
        }
        DateOnly? until = null;
        if (_tokenValidUntil.Trim().Length > 0)
        {
            if (!BillingClock.TryParse(_tokenValidUntil, out var date))
            {
                App.Notify.Warning("Vigencia del token", "Escriba la fecha como día/mes/año (por ejemplo 31/12/2026) o déjela vacía.");
                return false;
            }
            until = DateOnly.FromDateTime(date);
        }
        var endpoints = new SiatEndpointSet(_codesUrl.Trim(), _syncUrl.Trim(), _operationsUrl.Trim(), _purchaseSaleUrl.Trim(), _computerizedUrl.Trim(),
            _adjustmentUrl.Trim(), _namespace.Trim());
        var ok = await RunAsync(async () =>
        {
            var message = await App.SendAsync(new SaveSiatProfileCommand(_profileEnvironment, endpoints, _qrUrl.Trim(), timeout,
                string.IsNullOrWhiteSpace(newToken) ? null : newToken.Trim(), until));
            App.Notify.Success("Conexión guardada", FiscalText.Plain(message));
        }, "No se pudo guardar la conexión");
        if (ok)
        {
            var environment = _profileEnvironment;
            await LoadAsync(force: true);
            ProfileEnvironment = environment;
        }
        return ok;
    }

    /// <summary>Guarda el correo SMTP. La contraseña (si se escribió) llega del PasswordBox y no se conserva.</summary>
    public async Task<bool> SaveMailAsync(string? newPassword)
    {
        if (!int.TryParse(_mailPort.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            App.Notify.Warning("Puerto del correo", "El puerto es un número (587 con STARTTLS, 465 con SSL).");
            return false;
        }
        var ok = await RunAsync(async () =>
        {
            var message = await App.SendAsync(new SaveMailSettingsCommand(_mailHost.Trim(), port, _mailSsl,
                string.IsNullOrWhiteSpace(_mailUser) ? null : _mailUser.Trim(), string.IsNullOrEmpty(newPassword) ? null : newPassword, _mailFrom.Trim(),
                _mailFromName.Trim(), _mailEnabled));
            App.Notify.Success("Correo guardado", FiscalText.Plain(message));
        }, "No se pudo guardar el correo");
        if (ok)
        {
            await LoadAsync(force: true);
        }
        return ok;
    }

    private async Task TestConnectionAsync()
    {
        ConnectionResult = null;
        try
        {
            var message = await App.SendAsync(new CheckSiatCommunicationCommand());
            ConnectionResult = "✔ " + FiscalText.Plain(message);
            ConnectionBrush = "Success";
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            ConnectionResult = "✖ " + AppServices.Describe(ex);
            ConnectionBrush = "Danger";
        }
        OnPropertyChanged(nameof(ConnectionSoftBrush));
    }

    private async Task SaveBranchAsync(SiatBranchItem item)
    {
        if (!int.TryParse(item.SiatCode.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var code))
        {
            App.Notify.Warning("Código del Padrón", $"Escriba el código de {item.Row.BranchCode} en el Padrón (0 = casa matriz).");
            return;
        }
        if (await RunAsync(async () =>
            {
                var message = await App.SendAsync(new SaveSiatBranchCommand(item.Row.BranchCode, code, item.Municipality.Trim(),
                    string.IsNullOrWhiteSpace(item.Phone) ? null : item.Phone.Trim()));
                App.Notify.Success("Sucursal guardada", FiscalText.Plain(message));
            }, "No se pudo guardar la sucursal"))
        {
            await LoadAsync(force: true);
        }
    }
}
