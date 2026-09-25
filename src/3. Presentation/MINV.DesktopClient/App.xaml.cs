using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MINV.Application;
using MINV.DesktopClient.ViewModels;
using MINV.DesktopClient.Views;
using MINV.Hardware;
using MINV.Infrastructure;

namespace MINV.DesktopClient;

/// <summary>
/// Arranque del cliente de escritorio: configuración (appsettings.json + variables de entorno, MINV_DB tiene
/// prioridad), inyección de dependencias (aplicación, PostgreSQL, hardware) y una sesión de trabajo por usuario.
/// </summary>
public partial class App
{
    private IHost? _host;
    private IServiceScope? _session;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureAppConfiguration(c => c.SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true))
            .ConfigureServices((ctx, services) =>
            {
                var connection = Environment.GetEnvironmentVariable(Infrastructure.DependencyInjection.ConnectionStringVariable)
                                 ?? ctx.Configuration.GetConnectionString("Minv")
                                 ?? Infrastructure.DependencyInjection.DefaultConnectionString;
                var hw = ctx.Configuration.GetSection("Hardware");
                services.AddMinvApplication();
                services.AddMinvInfrastructure(connection);
                services.AddMinvHardware(new HardwareOptions(hw["PrinterKind"], hw["PrinterTarget"],
                    int.TryParse(hw["BaudRate"], out var baud) ? baud : 9600));
                services.AddScoped<LoginViewModel>();
                services.AddScoped<StockViewModel>();
                services.AddScoped<MovementViewModel>();
                services.AddScoped<AlertsViewModel>();
                services.AddScoped<ActivityViewModel>();
            })
            .Build();
        ShowLogin();
    }

    private void ShowLogin()
    {
        _session?.Dispose();
        _session = _host!.Services.CreateScope();
        var sp = _session.ServiceProvider;
        var login = new LoginWindow(sp.GetRequiredService<LoginViewModel>());
        if (login.ShowDialog() != true || login.ViewModel.Result is not { } result)
        {
            Shutdown();
            return;
        }
        var main = new MainWindow(new MainViewModel(result, sp.GetRequiredService<StockViewModel>(), sp.GetRequiredService<MovementViewModel>(),
            sp.GetRequiredService<AlertsViewModel>(), sp.GetRequiredService<ActivityViewModel>()));
        main.Closed += (_, _) => Shutdown();
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _session?.Dispose();
        _host?.Dispose();
        base.OnExit(e);
    }
}
