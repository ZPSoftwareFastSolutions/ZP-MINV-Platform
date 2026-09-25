using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using MINV.Application;
using MINV.Application.Iam;
using MINV.DesktopClient.Services;
using MINV.DesktopClient.ViewModels;
using MINV.Hardware;
using MINV.Infrastructure;
using MINV.Infrastructure.Demo;
using MINV.Infrastructure.Persistence;

namespace MINV.DesktopClient.Hosting;

/// <summary>Sesión abierta: su scope de servicios (contexto de datos, usuario, pantallas) y cómo se conectó.</summary>
public sealed class SessionHandle(IServiceScope scope, LoginResult login, string email, ConnectionInfo connection) : IDisposable
{
    public IServiceProvider Services => scope.ServiceProvider;

    public LoginResult Login { get; } = login;

    public string Email { get; } = email;

    public ConnectionInfo Connection { get; } = connection;

    public void Dispose() => scope.Dispose();
}

/// <summary>
/// Contenedores de servicios del cliente. Hay tres, con las mismas pantallas: PostgreSQL directo (base local), el servidor
/// M-INV en la nube (V4: sin credenciales de base de datos en el equipo; los comandos viajan por HTTPS) y la demostración
/// en memoria. Cada ingreso abre un scope nuevo: al cerrar sesión se descarta todo lo de ese usuario.
/// </summary>
public sealed class ClientHost : IDisposable
{
    private readonly string[] _args;
    private readonly ClientSettings _settings;
    private readonly ThemeService _theme;
    private IHost? _postgres;
    private IHost? _demo;
    private IHost? _cloud;

    public ClientHost(string[] args, ClientSettings settings, ThemeService theme)
    {
        _args = args;
        _settings = settings;
        _theme = theme;
        Configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        ConnectionString = Environment.GetEnvironmentVariable(Infrastructure.DependencyInjection.ConnectionStringVariable)
                           ?? Configuration.GetConnectionString("Minv")
                           ?? Infrastructure.DependencyInjection.DefaultConnectionString;
    }

    public IConfiguration Configuration { get; }

    public string ConnectionString { get; }

    public string DefaultTenantCode => Configuration["Client:TenantCode"] ?? "";

    public Task<DatabaseStatus> ProbeAsync(CancellationToken ct = default) =>
        DatabaseProbe.CheckAsync(ConnectionString, TimeSpan.FromSeconds(4), ct);

    /// <summary>Ingreso normal (PostgreSQL). Si falla, el scope se descarta y la excepción sube al inicio de sesión.</summary>
    public async Task<SessionHandle> SignInAsync(string tenantCode, string email, string password, CancellationToken ct = default)
    {
        _postgres ??= Build(services => services.AddMinvInfrastructure(ConnectionString).AddScoped<IRequestTransport, LocalTransport>());
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        return await SignInAsync(_postgres, tenantCode, email, password,
            new ConnectionInfo(false, $"{builder.Host}:{builder.Port}", builder.Database ?? "", builder.Username ?? ""), ct);
    }

    /// <summary>Prepara la demostración (la primera vez migra el libro de la V2.1 a memoria).</summary>
    public async Task<DemoSession> PrepareDemoAsync(CancellationToken ct = default)
    {
        _demo ??= BuildDemo();
        using var scope = _demo.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DemoWorkspace>().PrepareAsync(DemoWorkspace.DefaultWorkbookPath, ct);
    }

    public async Task<SessionHandle> SignInDemoAsync(DemoSession demo, string email, CancellationToken ct = default)
    {
        _demo ??= BuildDemo();
        return await SignInAsync(_demo, demo.TenantCode, email, demo.Password,
            new ConnectionInfo(true, "Memoria de este equipo", "Demostración V2.1", email), ct);
    }

    private IHost BuildDemo() => Build(services => services.AddMinvDemoInfrastructure().AddScoped<IRequestTransport, LocalTransport>());

    /// <summary>V4 · Estado del servidor en la nube (pantalla de inicio de sesión).</summary>
    public static Task<ServerStatus> ProbeCloudAsync(string? server, CancellationToken ct = default) => CloudConnection.ProbeAsync(server, ct);

    /// <summary>
    /// V4 · Ingreso en modo nube: el servidor valida la contraseña, calcula permisos y sucursales y entrega un token de
    /// sesión. El escritorio no tiene la cadena de conexión: solo la dirección del servidor.
    /// </summary>
    public async Task<SessionHandle> SignInCloudAsync(string server, string tenantCode, string email, string password, CancellationToken ct = default)
    {
        var uri = CloudConnection.ParseServer(server);
        _cloud ??= Build(services =>
        {
            services.AddSingleton<Application.Abstractions.IClock, Infrastructure.Services.SystemClock>();
            services.AddScoped<CloudConnection>();
            services.AddScoped<IRequestTransport, CloudTransport>();
        }, withApplication: false);
        var scope = _cloud.Services.CreateScope();
        try
        {
            var connection = scope.ServiceProvider.GetRequiredService<CloudConnection>();
            connection.Configure(uri);
            var login = await connection.LoginAsync(tenantCode, email, password, ct);
            var info = new ConnectionInfo(false, uri.Authority, "Servidor M-INV " + login.ServerVersion, email, IsCloud: true);
            await scope.ServiceProvider.GetRequiredService<SessionContext>().StartAsync(scope.ServiceProvider.GetRequiredService<SerialMediator>(),
                login.Login, email.Trim().ToLowerInvariant(), info, ct);
            return new SessionHandle(scope, login.Login, email, info);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private static async Task<SessionHandle> SignInAsync(IHost host, string tenantCode, string email, string password, ConnectionInfo info,
        CancellationToken ct)
    {
        var scope = host.Services.CreateScope();
        try
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var login = await mediator.Send(new LoginCommand(tenantCode, email, password, Environment.MachineName, App.Version), ct);
            await scope.ServiceProvider.GetRequiredService<SessionContext>().StartAsync(scope.ServiceProvider.GetRequiredService<SerialMediator>(),
                login, email.Trim().ToLowerInvariant(), info, ct);
            return new SessionHandle(scope, login, email, info);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private IHost Build(Action<IServiceCollection> data, bool withApplication = true) =>
        Host.CreateDefaultBuilder(_args)
            .ConfigureAppConfiguration(c => c.SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true))
            .ConfigureServices((ctx, services) =>
            {
                if (withApplication)
                {
                    services.AddMinvApplication();
                }
                data(services);
                services.AddMinvHardware(HardwareFrom(ctx.Configuration));
                services.AddSingleton(_settings);
                services.AddSingleton(_theme);
                services.AddMinvClient();
            })
            .Build();

    /// <summary>Periféricos: los de esta estación (Configuración del cliente) tienen prioridad sobre appsettings.json.</summary>
    private HardwareOptions HardwareFrom(IConfiguration configuration)
    {
        var hw = configuration.GetSection("Hardware");
        return new HardwareOptions(
            _settings.PrinterKind ?? hw["PrinterKind"],
            _settings.PrinterTarget ?? hw["PrinterTarget"],
            _settings.PrinterTarget is not null ? _settings.BaudRate : int.TryParse(hw["BaudRate"], out var baud) ? baud : 9600);
    }

    public void Dispose()
    {
        _postgres?.Dispose();
        _demo?.Dispose();
        _cloud?.Dispose();
    }
}

public static class ClientServices
{
    /// <summary>Pantallas y servicios de la interfaz: todos por sesión (scope), salvo tema y preferencias.</summary>
    public static IServiceCollection AddMinvClient(this IServiceCollection services)
    {
        services.AddScoped<SessionContext>();
        services.AddScoped<SerialMediator>();
        services.AddScoped<DataCache>();
        services.AddScoped<ImageCache>();
        services.AddScoped<NotificationService>();
        services.AddScoped<DialogService>();
        services.AddScoped<AppServices>();
        services.AddScoped<ShellViewModel>();
        services.AddScoped<DashboardViewModel>();
        services.AddScoped<StockViewModel>();
        services.AddScoped<MovementViewModel>();
        services.AddScoped<PhysicalCountViewModel>();
        services.AddScoped<AlertsViewModel>();
        services.AddScoped<OrderViewModel>();
        services.AddScoped<ActivityViewModel>();
        services.AddScoped<SettingsViewModel>();
        services.AddScoped<HelpViewModel>();
        services.AddScoped<CatalogViewModel>();
        services.AddScoped<PosViewModel>();
        services.AddScoped<SalesViewModel>();
        services.AddScoped<CustomersViewModel>();
        services.AddScoped<PurchaseOrdersViewModel>();
        services.AddScoped<SuppliersViewModel>();
        services.AddScoped<ReportsViewModel>();
        services.AddScoped<AccountingViewModel>();
        services.AddScoped<UsersViewModel>();
        services.AddScoped<BranchesViewModel>();
        services.AddScoped<TransfersViewModel>();
        services.AddScoped<IntegrationsViewModel>();
        return services;
    }
}
