using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using MINV.ApiGateway;
using MINV.CloudServer;
using MINV.Infrastructure.Seeding;

namespace MINV.Integration.Tests;

/// <summary>
/// Servidor en la nube o gateway REAL (Kestrel en un puerto de loopback) sobre la base en MEMORIA, con su empresa de
/// prueba multi-sucursal generada por <see cref="LocalDataSeeder"/> (los mismos casos de uso que la base local). Se
/// prefiere Kestrel a TestServer: prueba el mismo camino HTTP que producción y corre sobre cualquier runtime ≥ .NET 8.
/// </summary>
public abstract class SeededServer : IAsyncLifetime
{
    public WebApplication App { get; private set; } = null!;

    public SeedResult Seed { get; private set; } = null!;

    public Uri BaseAddress { get; private set; } = null!;

    public IServiceProvider Services => App.Services;

    protected abstract WebApplication Build(string[] args);

    public async Task InitializeAsync()
    {
        App = Build(["--urls", "http://127.0.0.1:0", "--Minv:Storage", "memoria", "--Minv:Webhooks:Enabled", "false",
            "--Minv:Webhooks:AllowPrivateTargets", "true", "--Minv:LoginsPerMinute", "1000", "--Logging:LogLevel:Default", "Warning"]);
        await App.StartAsync();
        BaseAddress = new Uri(App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        Seed = await App.Services.GetRequiredService<LocalDataSeeder>().SeedAsync(new SeedOptions("NUBE", Days: 4, Seed: 11), _ => { });
    }

    public HttpClient CreateClient() => new() { BaseAddress = BaseAddress };

    public async Task DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

public sealed class CloudServerFixture : SeededServer
{
    protected override WebApplication Build(string[] args) => CloudServerApp.Build(args);
}

public sealed class ApiGatewayFixture : SeededServer
{
    protected override WebApplication Build(string[] args) => ApiGatewayApp.Build(args);
}
