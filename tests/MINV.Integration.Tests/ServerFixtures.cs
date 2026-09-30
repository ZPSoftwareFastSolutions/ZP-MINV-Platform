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

    /// <summary>Argumentos adicionales de una prueba (p. ej. límites bajos de la tienda web).</summary>
    protected virtual string[] ExtraArgs => [];

    /// <summary>Empresa de prueba de la instancia (NUBE, 4 días).</summary>
    protected virtual SeedOptions Options => new("NUBE", Days: 4, Seed: 11);

    public async Task InitializeAsync()
    {
        // V6 · La tienda web pública del gateway atiende a la empresa NUBE desde la casa matriz (CM) y admite el origen del catálogo web
        string[] args =
        [
            "--urls", "http://127.0.0.1:0", "--Minv:Storage", "memoria", "--Minv:Webhooks:Enabled", "false",
            "--Minv:Webhooks:AllowPrivateTargets", "true", "--Minv:LoginsPerMinute", "1000", "--Minv:Siat:Background", "false", "--Logging:LogLevel:Default", "Warning",
            "--Minv:Storefront:TenantCode", "NUBE", "--Minv:Storefront:BranchCode", "CM", "--Minv:Storefront:AllowedOrigins:0", "http://localhost:5173",
            "--Minv:Storefront:ExpiryMinutes", "60",
            // Las pruebas de la tienda hacen muchas reservas y consultas seguidas desde la misma IP; el límite se prueba aparte (StorefrontLimitsFixture)
            "--Minv:Storefront:ReservationsPerMinute", "1000", "--Minv:Storefront:LookupsPerMinute", "1000",
        ];
        App = Build([.. args, .. ExtraArgs]);
        await App.StartAsync();
        BaseAddress = new Uri(App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        Seed = await App.Services.GetRequiredService<LocalDataSeeder>().SeedAsync(Options, _ => { });
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
