using System.Globalization;
using Npgsql;

namespace MINV.Infrastructure.Persistence;

/// <summary>Resultado de comprobar la base de datos al arrancar el cliente (pantalla de carga e inicio de sesión).</summary>
public sealed record DatabaseStatus(bool IsReachable, bool IsSupported, string Server, string Database, string User, string? Version,
    string Message)
{
    public bool IsReady => IsReachable && IsSupported;
}

/// <summary>
/// Comprueba en pocos segundos si PostgreSQL responde y si su versión es la que exige la V3 (15 o superior), sin
/// esperar el tiempo de espera largo de una conexión normal. Nunca muestra la contraseña de la cadena de conexión.
/// </summary>
public static class DatabaseProbe
{
    public const int MinimumMajorVersion = 15;

    public static async Task<DatabaseStatus> CheckAsync(string connectionString, TimeSpan timeout, CancellationToken ct = default)
    {
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            return new DatabaseStatus(false, false, "", "", "", null, "La cadena de conexión no es válida: " + ex.Message);
        }
        var server = $"{builder.Host}:{builder.Port}";
        var database = builder.Database ?? "";
        var user = builder.Username ?? "";
        builder.Timeout = Math.Clamp((int)Math.Ceiling(timeout.TotalSeconds), 1, 60);
        builder.Pooling = false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout + TimeSpan.FromSeconds(1));
        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cts.Token);
            await using var command = new NpgsqlCommand("SHOW server_version_num", connection);
            var number = int.Parse((string)(await command.ExecuteScalarAsync(cts.Token))!, CultureInfo.InvariantCulture);
            var version = $"{number / 10000}.{number % 10000 / 100}";
            var supported = number / 10000 >= MinimumMajorVersion;
            return new DatabaseStatus(true, supported, server, database, user, version, supported
                ? $"Conectado a PostgreSQL {version}"
                : $"PostgreSQL {version}: M-INV V3 requiere PostgreSQL {MinimumMajorVersion} o superior");
        }
        catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException or TimeoutException or System.Net.Sockets.SocketException
                                       or InvalidOperationException or FormatException)
        {
            var reason = ex switch
            {
                OperationCanceledException or TimeoutException => "no respondió a tiempo",
                PostgresException { SqlState: "28P01" or "28000" } => "usuario o contraseña de la base rechazados",
                PostgresException { SqlState: "3D000" } => $"la base «{database}» no existe",
                _ => ex.GetBaseException().Message,
            };
            return new DatabaseStatus(false, false, server, database, user, null, $"Sin conexión con {server}: {reason}");
        }
    }
}
