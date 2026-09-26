using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MINV.Application.Abstractions;
using MINV.Infrastructure.Billing.Hosting;
using MINV.Infrastructure.Billing.Simulator;
using MINV.Infrastructure.Billing.Soap;

namespace MINV.Infrastructure.Billing;

/// <summary>V4.1 · Con qué SIN habla M-INV.</summary>
public enum SiatGatewayMode
{
    /// <summary>Servicios SOAP reales (o el simulador HTTP MINV.SiatSimulator) en las URL configuradas por ambiente.</summary>
    Soap,

    /// <summary>Simulador en memoria dentro del mismo proceso (demostración y pruebas; sin red).</summary>
    InProcessSimulator,
}

/// <summary>V4.1 · Opciones de la facturación SIAT en la infraestructura.</summary>
public sealed class SiatOptions
{
    public SiatGatewayMode Mode { get; set; } = SiatGatewayMode.Soap;

    /// <summary>Opciones del simulador (solo con <see cref="SiatGatewayMode.InProcessSimulator"/>).</summary>
    public SiatSimulatorOptions Simulator { get; set; } = new();

    /// <summary>Tiempo máximo para abrir la conexión TCP/TLS con el SIN (el tiempo total de cada llamada es el de la
    /// configuración del ambiente).</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// V4.1 · Registro de la facturación SIAT en la infraestructura: cliente HTTP «siat», bitácora técnica de llamadas y el
/// <see cref="ISiatGateway"/> del modo elegido. Requiere la persistencia (<c>AddMinvInfrastructure</c> o
/// <c>AddMinvDemoInfrastructure</c>): la bitácora escribe con su propio contexto y la empresa sale de
/// <see cref="ITenantContext"/>.
/// </summary>
public static class SiatInfrastructureRegistration
{
    public static IServiceCollection AddMinvSiat(this IServiceCollection services, SiatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        options ??= new SiatOptions();
        services.AddHttpClient(SiatSoapGateway.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                ConnectTimeout = options.ConnectTimeout,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });
        services.TryAddScoped<ISiatCallLog, SiatCallLog>();
        if (options.Mode == SiatGatewayMode.InProcessSimulator)
        {
            // El simulador marca la misma hora que la aplicación (en la demostración, el reloj desplazado de la V2.1)
            services.TryAddSingleton(sp => new SiatSimulatorEngine(options.Simulator, new SiatClockTimeProvider(sp.GetRequiredService<IClock>())));
            services.Replace(ServiceDescriptor.Scoped<ISiatGateway, InProcessSiatGateway>());
        }
        else
        {
            services.Replace(ServiceDescriptor.Scoped<ISiatGateway, SiatSoapGateway>());
        }
        return services;
    }

    /// <summary>Servicio en segundo plano que envía los documentos pendientes y mantiene los códigos del SIN de cada empresa
    /// con facturación activa. Necesita un <c>ISiatWorker</c> registrado (capa de aplicación).</summary>
    public static IServiceCollection AddMinvSiatBackground(this IServiceCollection services, SiatBackgroundOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(options ?? new SiatBackgroundOptions());
        services.TryAddScoped<ISiatTenantSource, SiatTenantSource>();
        services.AddHostedService<SiatBackgroundService>();
        return services;
    }
}

/// <summary>V4.1 · <see cref="TimeProvider"/> sobre el reloj de la aplicación (<see cref="IClock"/>).</summary>
public sealed class SiatClockTimeProvider(IClock clock) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => clock.UtcNow;
}
