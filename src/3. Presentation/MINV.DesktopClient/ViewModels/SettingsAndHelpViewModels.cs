using System.ComponentModel;
using System.Runtime.InteropServices;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Hardware;
using MINV.Hardware.EscPos;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Estado del semáforo y lo que hay que hacer (tblEstados de la V2.1).</summary>
public sealed record StatusMeaning(MINV.Domain.Inventory.StockStatusCode Status, string Action, bool RequiresAction);

/// <summary>Opción de impresora de la pantalla de configuración.</summary>
public sealed record PrinterKindOption(string? Kind, string Name, string Example);

/// <summary>
/// Configuración de esta estación: apariencia, impresora de comprobantes (con página de prueba), escáner, conexión,
/// sesión (permisos y cambio de contraseña) y «Acerca de».
/// </summary>
public sealed class SettingsViewModel : PageViewModel, IScannerTarget
{
    private PrinterKindOption _kind;
    private string _target;
    private int _baudRate;
    private int _columns;
    private string _lastScan = "Todavía no se leyó ningún código.";

    public SettingsViewModel(AppServices app) : base(app, "configuracion", "Configuración", "Apariencia, periféricos y sesión", Glyphs.Settings)
    {
        var s = app.Settings;
        PrinterKinds = [new PrinterKindOption(null, "Sin impresora", ""), .. ReceiptPrinters.Kinds.Select(k => new PrinterKindOption(k.Kind, k.Name, k.Example))];
        _kind = PrinterKinds.FirstOrDefault(k => k.Kind == s.PrinterKind) ?? PrinterKinds[0];
        _target = s.PrinterTarget ?? "";
        _baudRate = s.BaudRate;
        _columns = s.PrinterColumns;
        SavePrinter = new RelayCommand(Save);
        TestPrint = new AsyncRelayCommand(TestPrintAsync, () => _kind.Kind is not null && _target.Trim().Length > 0);
        ChangePassword = new RelayCommand(() => app.Navigator.RequestChangePassword());
        app.Theme.Changed += (_, _) => OnPropertiesChanged(nameof(IsSystemTheme), nameof(IsLightTheme), nameof(IsDarkTheme));
        Permissions = PermissionCodes.All.Where(p => app.Session.Can(p.Code)).Select(p => p.Description).ToList();
    }

    // -------------------------------------------------------------------------------------------- apariencia
    public bool IsSystemTheme
    {
        get => App.Theme.Mode == ThemeMode.System;
        set
        {
            if (value)
            {
                App.Theme.Apply(ThemeMode.System);
            }
        }
    }

    public bool IsLightTheme
    {
        get => App.Theme.Mode == ThemeMode.Light;
        set
        {
            if (value)
            {
                App.Theme.Apply(ThemeMode.Light);
            }
        }
    }

    public bool IsDarkTheme
    {
        get => App.Theme.Mode == ThemeMode.Dark;
        set
        {
            if (value)
            {
                App.Theme.Apply(ThemeMode.Dark);
            }
        }
    }

    // -------------------------------------------------------------------------------------------- impresora
    public IReadOnlyList<PrinterKindOption> PrinterKinds { get; }

    public PrinterKindOption SelectedKind
    {
        get => _kind;
        set
        {
            if (Set(ref _kind, value ?? PrinterKinds[0]))
            {
                OnPropertiesChanged(nameof(HasPrinter), nameof(IsSerial), nameof(TargetHint), nameof(TargetLabel));
            }
        }
    }

    public bool HasPrinter => _kind.Kind is not null;

    public bool IsSerial => _kind.Kind == "serial";

    public string TargetLabel => _kind.Kind switch
    {
        "serial" => "Puerto",
        "network" => "Dirección IP y puerto",
        "windows" => "Nombre de la impresora en Windows",
        _ => "Destino",
    };

    public string TargetHint => _kind.Example.Length > 0 ? "Ej.: " + _kind.Example : "";

    public string PrinterTarget
    {
        get => _target;
        set => Set(ref _target, value ?? "");
    }

    public IReadOnlyList<int> BaudRates { get; } = [9600, 19200, 38400, 57600, 115200];

    public int BaudRate
    {
        get => _baudRate;
        set => Set(ref _baudRate, value);
    }

    public IReadOnlyList<int> ColumnOptions { get; } = [32, 42, 48];

    public int Columns
    {
        get => _columns;
        set => Set(ref _columns, value);
    }

    public string LastScan
    {
        get => _lastScan;
        private set => Set(ref _lastScan, value);
    }

    public RelayCommand SavePrinter { get; }

    public AsyncRelayCommand TestPrint { get; }

    // -------------------------------------------------------------------------------------------- sesión y conexión
    public string UserName => App.Session.DisplayName;

    public string UserEmail => App.Session.Email;

    public string Initials => App.Session.Initials;

    public string RoleNames => App.Session.RoleNames;

    public string CompanyName => App.Session.Workspace.CompanyName;

    public string CompanyDetail => $"Código {App.Session.Workspace.TenantCode}" +
                                   (App.Session.Workspace.TaxId is { Length: > 0 } nit ? $" · NIT {nit}" : "") +
                                   $" · moneda {App.Session.Workspace.CurrencyCode} · zona {App.Session.Workspace.TimeZoneId}";

    public string SessionStarted => "Sesión iniciada el " + App.Session.StartedAt.ToString("dd/MM/yyyy 'a las' HH:mm", Fmt.Culture) +
                                    " en " + Environment.MachineName;

    public IReadOnlyList<string> Permissions { get; }

    public bool IsDemo => App.Session.IsDemo;

    public string ConnectionMode => App.Session.IsDemo ? "Demostración (en memoria)" : "PostgreSQL";

    public string ConnectionServer => App.Session.Connection.Server;

    public string ConnectionDatabase => App.Session.Connection.Database;

    public string ConnectionUser => App.Session.Connection.User;

    public RelayCommand ChangePassword { get; }

    // -------------------------------------------------------------------------------------------- acerca de
    public string Version => MINV.DesktopClient.App.Version;

    public string Runtime => $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.OSDescription}";

    protected override Task LoadCoreAsync(bool force) => Task.CompletedTask;

    public bool OnScanned(string code)
    {
        LastScan = $"✔ Lectura recibida: {code}  ({DateTime.Now:HH:mm:ss})";
        return true;
    }

    private void Save()
    {
        var s = App.Settings;
        s.PrinterKind = _kind.Kind;
        s.PrinterTarget = _kind.Kind is null || _target.Trim().Length == 0 ? null : _target.Trim();
        s.BaudRate = _baudRate;
        s.PrinterColumns = _columns;
        s.Save();
        App.Notify.Success("Impresora guardada", _kind.Kind is null
            ? "Esta estación no imprimirá comprobantes."
            : $"{_kind.Name} · {_target.Trim()} (se usará desde el próximo ingreso).");
    }

    private async Task TestPrintAsync()
    {
        try
        {
            var printer = ReceiptPrinters.Create(new HardwareOptions(_kind.Kind, _target.Trim(), _baudRate))!;
            var document = ReceiptRenderer.RenderTestPage(CompanyName, printer.Name, DateTimeOffset.Now, _columns);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Task.Run(() => printer.PrintAsync(document, timeout.Token), timeout.Token);
            App.Notify.Success("Página de prueba enviada", $"Revise la impresora {printer.Name}.");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException
                                       or OperationCanceledException or System.Net.Sockets.SocketException or Win32Exception or ArgumentException
                                       or FormatException)
        {
            App.Notify.Error("No se pudo imprimir", ex.Message);
        }
    }
}

/// <summary>Ayuda: guía rápida por rol, atajos de teclado y significado del semáforo.</summary>
public sealed class HelpViewModel(AppServices app) : PageViewModel(app, "ayuda", "Ayuda", "Guía rápida, atajos y semáforo", Glyphs.Help)
{
    public IReadOnlyList<HelpEntry> Shortcuts { get; } =
    [
        new("Ctrl + K", "Buscar un producto y abrir su ficha", Glyphs.Search),
        new("Ctrl + 1 … 7", "Ir a cada pantalla del menú", Glyphs.Menu),
        new("Ctrl + N", "Registrar un movimiento", Glyphs.Add),
        new("F5", "Actualizar la pantalla actual", Glyphs.Refresh),
        new("Ctrl + B", "Ocultar o mostrar el menú lateral", Glyphs.ChevronLeft),
        new("Ctrl + Shift + L", "Cambiar entre tema claro y oscuro", Glyphs.Moon),
        new("Esc", "Cerrar la ficha o el cuadro abierto", Glyphs.Close),
        new("F1", "Esta ayuda", Glyphs.Help),
        new("Escáner", "Lea un código en cualquier pantalla: registra, cuenta o abre la ficha", Glyphs.Barcode),
    ];

    public IReadOnlyList<HelpGuide> Guides { get; } =
    [
        new("Registrar una entrada o una salida", "Bodega y Ventas", Glyphs.Swap,
        [
            "Abra «Registrar movimiento» (Ctrl+N) y elija el tipo: Entrada, Salida, Ajuste…",
            "Busque el producto por nombre, SKU o léalo con el escáner.",
            "Escriba la cantidad: la tarjeta de la derecha muestra el stock que quedará.",
            "Si la salida dejaría la posición en negativo, se pinta de rojo y no se puede registrar (poka-yoke).",
            "Los ajustes exigen observaciones. Un error se corrige con otro movimiento: nunca se borra.",
        ]),
        new("Reponer lo que falta", "Bodega y Gerencia", Glyphs.Cart,
        [
            "En «Alertas» vea los productos agotados, críticos o bajos, de lo más urgente a lo menos urgente.",
            "«Pedido sugerido» agrupa por proveedor lo que hay que comprar para volver al nivel ideal.",
            "Copie el pedido de un proveedor y envíelo por correo o WhatsApp, o expórtelo a Excel.",
            "Al recibir la mercadería, registre la ENTRADA desde la alerta con un clic.",
        ]),
        new("Hacer una toma física", "Bodega", Glyphs.Checklist,
        [
            "En «Toma física» pulse «Iniciar toma física».",
            "Cuente cada producto y registre la cantidad (varias personas pueden contar a la vez).",
            "La tabla muestra la diferencia contra el stock exacto de ese momento.",
            "Al terminar, «Generar ajustes» crea todos los AJUSTE (+) y (−) en una sola operación.",
        ]),
        new("Consultar un producto", "Todos", Glyphs.Search,
        [
            "Pulse Ctrl+K, escriba parte del nombre o del SKU y elija el producto.",
            "La ficha muestra el semáforo, las existencias por posición y el kardex con el saldo acumulado.",
            "Desde la ficha puede registrar una entrada o una salida de ese producto.",
        ]),
    ];

    public IReadOnlyList<StatusMeaning> Legend { get; } =
        MINV.Domain.Inventory.StockRules.Defaults.Select(d => new StatusMeaning(d.Status, d.Action, d.RequiresAction)).ToList();

    public string Version => MINV.DesktopClient.App.Version;

    protected override Task LoadCoreAsync(bool force) => Task.CompletedTask;
}
