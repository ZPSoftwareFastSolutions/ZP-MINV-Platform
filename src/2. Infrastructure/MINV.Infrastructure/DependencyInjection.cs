using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MINV.Application.Abstractions;
using MINV.Infrastructure.Demo;
using MINV.Infrastructure.Importing.V21;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Persistence.Interceptors;
using MINV.Infrastructure.Provisioning;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringVariable = "MINV_DB";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=minv;Username=minv_app;Password=minv-dev;Include Error Detail=true";

    /// <summary>Registra PostgreSQL (EF Core + Npgsql), el contexto multi-tenant, los interceptores y los servicios.
    /// Todo es «scoped»: el cliente de escritorio abre un scope por sesión de usuario.</summary>
    public static IServiceCollection AddMinvInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.TryAddSingleton<IClock, SystemClock>();   // los datos de prueba registran antes un reloj simulado
        services.AddScoped<TenantSessionInterceptor>();
        return services.AddMinvPersistence((sp, options) => Configure(options, connectionString)
            .AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()));
    }

    /// <summary>
    /// Modo demostración: el mismo modelo, los mismos casos de uso y las mismas guardas, pero sobre una base en memoria
    /// (sin PostgreSQL) que <see cref="DemoWorkspace"/> llena con el libro de la V2.1. Los datos se pierden al cerrar.
    /// No hay Row Level Security ni triggers (son de PostgreSQL): el aislamiento lo dan los filtros globales de EF Core.
    /// </summary>
    public static IServiceCollection AddMinvDemoInfrastructure(this IServiceCollection services)
    {
        var root = new InMemoryDatabaseRoot();
        var name = "minv-demo-" + Guid.NewGuid().ToString("N");
        services.AddSingleton<DemoClock>();
        services.AddSingleton<DemoState>();
        services.AddSingleton<IClock>(sp => sp.GetRequiredService<DemoClock>());
        services.AddScoped<DemoWorkspace>();
        return services.AddMinvPersistence((_, options) => ConfigureInMemory(options, name, root));
    }

    private static IServiceCollection AddMinvPersistence(this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> provider)
    {
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<MinvSaveChangesInterceptor>();
        services.AddDbContextFactory<MINVDbContext>((sp, options) =>
        {
            provider(sp, options);
            options.AddInterceptors(sp.GetRequiredService<MinvSaveChangesInterceptor>());
        }, ServiceLifetime.Scoped);
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<MINVDbContext>>().CreateDbContext());
        services.AddScoped<IMinvDbContext>(sp => sp.GetRequiredService<MINVDbContext>());
        services.AddScoped<ILicenseService, LicenseService>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<TenantProvisioner>();
        services.AddScoped<V21Importer>();
        services.AddTransient<Seeding.LocalDataSeeder>();
        return services;
    }

    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.Iam));

    /// <summary>Base en memoria compartida por todos los contextos del mismo <paramref name="root"/> (la auditoría usa un
    /// contexto propio). Las transacciones no existen en memoria: se aceptan y se ignoran.</summary>
    internal static DbContextOptionsBuilder ConfigureInMemory(DbContextOptionsBuilder options, string name, InMemoryDatabaseRoot root) =>
        options.UseInMemoryDatabase(name, root).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
}

/// <summary>Fábrica de diseño para <c>dotnet ef</c> (migraciones y script SQL). Cadena: variable MINV_DB o la de
/// desarrollo.</summary>
public sealed class MINVDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MINVDbContext>
{
    public MINVDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable(DependencyInjection.ConnectionStringVariable)
                 ?? DependencyInjection.DefaultConnectionString;
        var options = new DbContextOptionsBuilder<MINVDbContext>();
        DependencyInjection.Configure(options, cs);
        return new MINVDbContext(options.Options, new TenantContext());
    }
}
