using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MINV.DesktopClient.Hosting;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.DesktopClient.ViewModels;
using MINV.DesktopClient.Views;
using MINV.Infrastructure.Persistence;

namespace MINV.DesktopClient;

/// <summary>
/// Arranque del cliente de escritorio: pantalla de carga (preferencias, tema y comprobación de la base de datos) →
/// inicio de sesión (PostgreSQL o demostración) → ventana principal; al cerrar sesión se vuelve al inicio de sesión.
/// Opciones: <c>--capturas &lt;carpeta&gt;</c> genera imágenes de todas las pantallas con la demostración (documentación y
/// revisión visual).
/// </summary>
public partial class App
{
    private ClientSettings _settings = new();
    private ThemeService? _theme;
    private ClientHost? _host;
    private DatabaseStatus? _lastStatus;
    private string? _capturesFolder;

    /// <summary>Versión visible (la de Directory.Build.props, sin el sufijo de compilación).</summary>
    public static string Version { get; } = (typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                              ?? "3.1.0").Split('+')[0];

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigureCulture();
        DispatcherUnhandledException += OnUnhandled;
        AsyncRelayCommand.UnhandledError = ex => Report(ex);

        _settings = ClientSettings.Load();
        var captures = e.Args.SkipWhile(a => a != "--capturas").Skip(1).FirstOrDefault();
        if (captures is not null)
        {
            _settings = new ClientSettings { IsReadOnly = true };
            _capturesFolder = System.IO.Path.GetFullPath(captures);
            System.IO.Directory.CreateDirectory(_capturesFolder);
        }
        _theme = new ThemeService(_settings);
        _theme.Apply(_theme.Mode, save: false);
        _host = new ClientHost(e.Args, _settings, _theme);

        if (captures is not null)
        {
            var code = await new ScreenshotRunner(_host, _settings, _theme).RunAsync(captures);
            Shutdown(code);
            return;
        }

        var splash = new SplashViewModel();
        var splashWindow = new SplashWindow(splash);
        splashWindow.Show();
        var started = DateTime.UtcNow;
        splash.Begin(0, "Preparando la aplicación…");
        splash.Progress = 20;
        await Task.Delay(300);
        splash.Complete(0);
        splash.Begin(1, "Cargando sus preferencias…");
        splash.Progress = 40;
        await Task.Delay(250);
        splash.Complete(1, text: $"Preferencias cargadas · tema {ThemeService.Name(_theme.Mode)}");
        splash.Begin(2, "Comprobando la base de datos…");
        splash.Progress = 55;
        _lastStatus = await _host.ProbeAsync();
        splash.Complete(2, !_lastStatus.IsReady, _lastStatus.IsReady
            ? $"Base de datos conectada · PostgreSQL {_lastStatus.Version}"
            : "Sin base de datos: puede usar la demostración");
        splash.Progress = 90;
        splash.Begin(3, "Listo");
        var elapsed = DateTime.UtcNow - started;
        if (elapsed < TimeSpan.FromMilliseconds(1500))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500) - elapsed);
        }
        splash.Complete(3);
        splash.Progress = 100;
        await Task.Delay(300);
        ShowLogin();
        await splashWindow.FadeOutAsync();
    }

    private void ShowLogin()
    {
        var vm = new LoginViewModel(_host!, _settings, _lastStatus);
        var window = new LoginWindow(vm);
        vm.SignedIn += (_, _) =>
        {
            var session = vm.Result!;
            OpenMain(session);
            window.Close();
        };
        window.Closed += (_, _) =>
        {
            if (vm.Result is null)
            {
                Shutdown();
            }
        };
        MainWindow = window;
        window.Show();
    }

    private void OpenMain(SessionHandle session)
    {
        var shell = session.Services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow(shell);
        var loggingOut = false;
        shell.LogoutRequested += (_, _) =>
        {
            loggingOut = true;
            window.Close();
            session.Dispose();
            ShowLogin();
        };
        shell.ChangePasswordRequested += (_, _) => ChangePassword(window, session, mandatory: false);
        window.Closed += (_, _) =>
        {
            if (!loggingOut)
            {
                session.Dispose();
                Shutdown();
            }
        };
        MainWindow = window;
        window.Show();
        if (session.Login.MustChangePassword)
        {
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => ChangePassword(window, session, mandatory: true));
        }
    }

    private static void ChangePassword(Window owner, SessionHandle session, bool mandatory)
    {
        var dialog = new ChangePasswordWindow(new ChangePasswordViewModel(session.Services.GetRequiredService<AppServices>(), mandatory)) { Owner = owner };
        if (dialog.ShowDialog() != true && mandatory)
        {
            session.Services.GetRequiredService<ShellViewModel>().Logout.Execute(null);
        }
    }

    /// <summary>Español en toda la interfaz (formatos de número y fecha de los enlaces de WPF).</summary>
    private static void ConfigureCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = Fmt.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Fmt.Culture;
        Thread.CurrentThread.CurrentCulture = Fmt.Culture;
        Thread.CurrentThread.CurrentUICulture = Fmt.Culture;
        var language = XmlLanguage.GetLanguage(Fmt.Culture.IetfLanguageTag);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(language));
        FrameworkContentElement.LanguageProperty.OverrideMetadata(typeof(System.Windows.Documents.TextElement),
            new FrameworkPropertyMetadata(language));
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Report(e.Exception);
    }

    /// <summary>Un error inesperado nunca cierra la aplicación: se avisa en la ventana activa.</summary>
    private void Report(Exception ex)
    {
        System.Diagnostics.Trace.TraceError("M-INV · error no controlado: {0}", ex);
        if (_capturesFolder is not null)
        {
            // Modo capturas (sin nadie frente a la pantalla): se registra el error y se termina, nunca un cuadro modal.
            System.IO.File.AppendAllText(System.IO.Path.Combine(_capturesFolder, "capturas.log"), "✖ " + ex + Environment.NewLine);
            Shutdown(1);
            return;
        }
        if (MainWindow is MainWindow { DataContext: ShellViewModel shell })
        {
            shell.Notifications.Error("Algo salió mal", AppServices.Describe(ex));
        }
        else
        {
            MessageBox.Show(AppServices.Describe(ex), "M-INV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
