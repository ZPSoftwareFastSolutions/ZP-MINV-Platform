using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Infrastructure.Persistence;

namespace MINV.Infrastructure.Hosting;

/// <summary>Dónde guardan los datos el servidor en la nube y el gateway.</summary>
public enum ServerStorage
{
    /// <summary>PostgreSQL (producción): cadena en <c>MINV_DB</c> con el rol <c>minv_server</c>.</summary>
    Postgres,

    /// <summary>Base en memoria (pruebas y demostraciones sin PostgreSQL).</summary>
    Memory,
}

/// <summary>
/// V4 · Arranque común del servidor en la nube y del API Gateway: registra la aplicación sobre PostgreSQL o memoria y
/// verifica, antes de aceptar tráfico, que la base está al día y que el rol de conexión NO puede saltarse la seguridad por
/// filas (ni superusuario, ni BYPASSRLS, ni dueño de las tablas): si lo fuera, un error de filtro en el código dejaría
/// ver datos de otra empresa o sucursal.
/// </summary>
public static class ServerHosting
{
    public const string AllowPrivilegedVariable = "MINV_ALLOW_PRIVILEGED_ROLE";

    public static string Version => typeof(ServerHosting).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
                                    ?? "4.0.0";

    public static int Major => int.TryParse(Version.Split('.')[0], out var major) ? major : 4;

    public static ServerStorage AddMinvServerStorage(this IServiceCollection services, string? storage, string? connectionString)
    {
        if (string.Equals(storage, "memoria", StringComparison.OrdinalIgnoreCase) || string.Equals(storage, "memory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddMinvDemoInfrastructure();
            return ServerStorage.Memory;
        }
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Falta la cadena de conexión: defina {DependencyInjection.ConnectionStringVariable} (rol minv_server, SSL Mode=VerifyFull).");
        }
        services.AddMinvInfrastructure(connectionString);
        return ServerStorage.Postgres;
    }

    private sealed record RoleInfo(bool IsSuperuser, bool BypassesRls, int OwnedTables, bool CanResolveSessions);

    /// <summary>Verificaciones de arranque (solo PostgreSQL).</summary>
    public static async Task VerifyDatabaseAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count > 0)
        {
            throw new InvalidOperationException($"La base de datos no está al día: faltan {string.Join(", ", pending)} (aplique las migraciones con el rol dueño).");
        }
        var role = await db.Database.SqlQueryRaw<RoleInfo>("""
            SELECT r.rolsuper AS "IsSuperuser", r.rolbypassrls AS "BypassesRls",
                   (SELECT count(*)::int FROM pg_tables t WHERE t.tableowner = current_user
                      AND t.schemaname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration')) AS "OwnedTables",
                   has_function_privilege(current_user, 'iam.resolve_session(text)', 'EXECUTE') AS "CanResolveSessions"
            FROM pg_roles r WHERE r.rolname = current_user
            """).SingleAsync(ct);
        var privileged = role.IsSuperuser || role.BypassesRls || role.OwnedTables > 0;
        if (privileged && Environment.GetEnvironmentVariable(AllowPrivilegedVariable) != "1")
        {
            throw new InvalidOperationException(
                "El rol de conexión puede saltarse la seguridad por filas (superusuario, BYPASSRLS o dueño de las tablas). " +
                $"Conéctese con minv_server (o defina {AllowPrivilegedVariable}=1 solo en desarrollo).");
        }
        if (!role.CanResolveSessions)
        {
            throw new InvalidOperationException("El rol de conexión no puede ejecutar iam.resolve_session: use minv_server (tools\\bd_nube.ps1 lo crea).");
        }
    }
}
