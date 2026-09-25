using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MINV.Application.Abstractions;
using MINV.Domain.Iam;
using MINV.Infrastructure;
using MINV.Infrastructure.Importing.V21;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Provisioning;

// =====================================================================================================================
// minv · herramienta de administración de M-INV V3
//   minv migrate                                              aplica las migraciones (rol dueño: minv_owner)
//   minv tenant create --codigo DEMO --razon-social "…" --admin correo --nombre "…" [--clave …] [--moneda BOB]
//   minv import-v21 --archivo libro.xlsx --codigo DEMO --admin correo --nombre "…" [--clave …] [--zona America/Bogota]
//   minv user password --codigo DEMO --correo x@y [--clave …]
//   minv verify [--codigo DEMO]
// Conexión: --conexion "Host=…" o variable MINV_DB. La clave también puede venir de MINV_CLAVE o se pide sin eco.
// =====================================================================================================================
Console.OutputEncoding = Encoding.UTF8;
try
{
    return await Cli.RunAsync(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine("✖ " + ex.GetBaseException().Message);
    if (ex is V21ImportException import)
    {
        foreach (var e in import.Errors)
        {
            Console.Error.WriteLine("   · " + e);
        }
    }
    return 1;
}

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "ayuda")
        {
            Console.WriteLine(Help);
            return 0;
        }
        var options = Options(args);
        var connection = options.GetValueOrDefault("conexion")
                         ?? Environment.GetEnvironmentVariable(DependencyInjection.ConnectionStringVariable)
                         ?? DependencyInjection.DefaultConnectionString;
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMinvInfrastructure(connection);
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MINVDbContext>();

        switch (string.Join(' ', args.TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal))))
        {
            case "migrate":
                await db.Database.MigrateAsync();
                Console.WriteLine("✔ Base de datos al día: " + string.Join(", ", await db.Database.GetAppliedMigrationsAsync()));
                return 0;

            case "tenant create":
            {
                var t = await sp.GetRequiredService<TenantProvisioner>().ProvisionAsync(new ProvisionTenantRequest(
                    Required(options, "codigo"), Required(options, "razon-social"), options.GetValueOrDefault("nit"),
                    Required(options, "admin"), Required(options, "nombre"), Password(options),
                    options.GetValueOrDefault("moneda") ?? "BOB", options.GetValueOrDefault("zona") ?? "America/La_Paz"));
                Console.WriteLine($"✔ Empresa creada: {t.TenantId} · almacén {t.WarehouseCode} · administrador {options["admin"]}");
                return 0;
            }

            case "import-v21":
            {
                var result = await sp.GetRequiredService<V21Importer>().ImportAsync(new V21ImportRequest(
                    Required(options, "archivo"), Required(options, "codigo"), Required(options, "admin"), Required(options, "nombre"),
                    Password(options), options.GetValueOrDefault("zona") ?? "America/La_Paz", options.GetValueOrDefault("moneda"),
                    options.GetValueOrDefault("pais") ?? "BO", options.GetValueOrDefault("pais-nombre") ?? "Bolivia"));
                var r = result.Report;
                Console.WriteLine($"✔ Migración V2.1 → V3 completa (tenant {result.Tenant.TenantId})");
                Console.WriteLine($"   {r.Products} productos · {r.Suppliers} proveedores · {r.Categories} categorías · {r.Bins} posiciones");
                Console.WriteLine($"   {r.Users} usuarios · {r.Movements} movimientos · {r.RejectedMovements} rechazados (auditoría) · " +
                                  $"{r.ActivityRows} registros de actividad · {r.CountLines} conteos en curso");
                foreach (var w in r.Warnings)
                {
                    Console.WriteLine("   ⚠ " + w);
                }
                var p = result.Parity;
                Console.WriteLine(p.Ok
                    ? $"✔ Paridad con la V2.1: {p.Products} productos, {p.Alerts} alertas y {p.OrderLines} líneas de pedido idénticas"
                    : "⚠ Paridad con la V2.1: " + string.Join(" · ", p.Differences.Take(10)));
                return p.Ok || !p.Checked ? 0 : 2;
            }

            case "user password":
            {
                var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Code == Required(options, "codigo").ToUpperInvariant())
                             ?? throw new InvalidOperationException("La empresa no existe.");
                sp.GetRequiredService<ITenantContext>().Set(tenant.Id);
                var email = User.NormalizeEmail(Required(options, "correo"));
                var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email)
                           ?? throw new InvalidOperationException($"El usuario {email} no existe.");
                var hasher = sp.GetRequiredService<IPasswordHasher>();
                var now = sp.GetRequiredService<IClock>().UtcNow;
                var hash = hasher.Hash(Password(options));
                var credential = await db.UserCredentials.FirstOrDefaultAsync(c => c.UserId == user.Id);
                if (credential is null)
                {
                    db.UserCredentials.Add(new UserCredential(tenant.Id, user.Id, hash, hasher.Algorithm, hasher.Iterations, now, mustChangePassword: true));
                }
                else
                {
                    credential.ChangePassword(hash, hasher.Algorithm, hasher.Iterations, now);
                }
                await db.SaveChangesAsync();
                Console.WriteLine($"✔ Contraseña asignada a {email}");
                return 0;
            }

            case "verify":
                return await VerifyAsync(db, sp.GetRequiredService<ITenantContext>(), options.GetValueOrDefault("codigo"));

            default:
                Console.Error.WriteLine("Comando desconocido.\n" + Help);
                return 1;
        }
    }

    /// <summary>Comprobaciones de la base: tablas, triggers append-only, RLS y conservación (Σ existencias = Σ movimientos).</summary>
    private static async Task<int> VerifyAsync(MINVDbContext db, ITenantContext tenantContext, string? tenantCode)
    {
        var schemas = string.Join(",", Schemas.All.Select(s => $"'{s}'"));
        async Task<int> Scalar(string sql) => await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
        var tables = await Scalar($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema IN ({schemas}) AND table_name <> '__ef_migrations_history'");
        var triggers = await Scalar("SELECT count(*)::int AS \"Value\" FROM pg_trigger WHERE tgname = 'trg_append_only'");
        var rls = await Scalar($"SELECT count(*)::int AS \"Value\" FROM pg_tables WHERE rowsecurity AND schemaname IN ({schemas})");
        var ok = tables >= 80 && triggers == 7 && rls >= 90;
        Console.WriteLine($"{(tables >= 80 ? "✔" : "✖")} {tables} tablas en {Schemas.All.Count} esquemas");
        Console.WriteLine($"{(triggers == 7 ? "✔" : "✖")} {triggers} libros mayores protegidos (append-only)");
        Console.WriteLine($"{(rls >= 90 ? "✔" : "✖")} Row Level Security activa en {rls} tablas");
        if (tenantCode is { Length: > 0 })
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Code == tenantCode.ToUpperInvariant())
                         ?? throw new InvalidOperationException("La empresa no existe.");
            tenantContext.Set(tenant.Id);
            await db.SyncTenantSessionAsync();
            var breaches = await Scalar($"SELECT count(*)::int AS \"Value\" FROM inventory.v_conservation_breaches WHERE tenant_id = '{tenant.Id}'");
            var movements = await db.StockMovements.CountAsync();
            ok &= breaches == 0;
            Console.WriteLine($"{(breaches == 0 ? "✔" : "✖")} Conservación: {movements} movimientos, {breaches} existencias descuadradas");
        }
        Console.WriteLine(ok ? "RESULTADO: base de datos correcta" : "RESULTADO: hay problemas");
        return ok ? 0 : 1;
    }

    private static Dictionary<string, string> Options(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                map[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            }
        }
        return map;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && value.Length > 0 ? value : throw new ArgumentException($"Falta --{name}.");

    /// <summary>Clave: --clave, variable MINV_CLAVE o se pide por consola sin mostrarla.</summary>
    private static string Password(Dictionary<string, string> options)
    {
        if (options.TryGetValue("clave", out var p) || (p = Environment.GetEnvironmentVariable("MINV_CLAVE")) is { Length: > 0 })
        {
            return p;
        }
        Console.Write("Contraseña: ");
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }
            if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
            {
                sb.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
            }
        }
        Console.WriteLine();
        return sb.ToString();
    }

    private const string Help = """
        minv · administración de M-INV V3 (PostgreSQL)
          minv migrate
          minv tenant create --codigo DEMO --razon-social "Mi empresa" --admin admin@empresa.com --nombre "Administrador" [--clave …] [--moneda BOB] [--zona America/La_Paz]
          minv import-v21 --archivo src/M-INV_V2_Colaborativo.xlsx --codigo DEMO --admin admin@distribuidorademo.example --nombre "Administrador" [--clave …] [--zona America/Bogota] [--moneda COP] [--pais CO --pais-nombre Colombia]
          minv user password --codigo DEMO --correo ana.gomez@distribuidorademo.example [--clave …]
          minv verify [--codigo DEMO]
        Conexión: --conexion "Host=localhost;Database=minv;Username=minv_owner;Password=…" o variable MINV_DB.
        """;
}
