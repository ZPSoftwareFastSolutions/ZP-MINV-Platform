using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MINV.Application.Abstractions;
using MINV.Domain.Iam;
using MINV.Infrastructure;
using MINV.Infrastructure.Billing;
using MINV.Infrastructure.Billing.Simulator;
using MINV.Infrastructure.Importing.V21;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Provisioning;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Services;

// =====================================================================================================================
// minv · herramienta de administración de M-INV (V3 + V4 multi-sucursal en la nube)
//   minv migrate                                              aplica las migraciones (rol dueño: minv_owner)
//   minv tenant create --codigo DEMO --razon-social "…" --admin correo --nombre "…" [--clave …] [--moneda BOB]
//   minv import-v21 --archivo libro.xlsx --codigo DEMO --admin correo --nombre "…" [--clave …] [--zona America/Bogota]
//   minv user password --codigo DEMO --correo x@y [--clave …]
//   minv verify [--codigo DEMO]
//   minv datos-prueba [--codigo TECHZONE] [--dias 60] [--semilla 2026] [--credenciales archivo.txt] [--integracion archivo.txt]
//                     [--dias-facturacion 25] [--sin-facturacion] [--siat-estado archivo.json] [--simulador http://localhost:5095]
//   minv siat estado|preparar|sincronizar|procesar [--codigo TECHZONE] [--forzar]        V4.1: facturación SIAT de una empresa
//   minv siat simulador-estado|simulador-apagar|simulador-encender [--simulador http://localhost:5095]
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
    /// <summary>V4.2 · Código de la empresa de prueba (Tech Zone Gaming S.R.L.) cuando no se indica --codigo.</summary>
    public const string DefaultCompany = "TECHZONE";

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
        var command = string.Join(' ', args.TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)));
        if (command.StartsWith("siat simulador", StringComparison.Ordinal))
        {
            // V4.1 · Interruptor del simulador HTTP del SIN (no necesita la base de datos)
            return await SiatCli.SimulatorAsync(command, options);
        }
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);   // V4.1: sin el rastro de cada llamada al SIN
        var billing = command == "datos-prueba" && !options.ContainsKey("sin-facturacion");
        if (command == "datos-prueba")
        {
            // Reloj simulado: la operación de los últimos N días se registra con sus fechas y horas «reales»
            builder.Services.AddSingleton<DemoClock>();
            builder.Services.AddSingleton<IClock>(sp => sp.GetRequiredService<DemoClock>());
            MINV.Application.DependencyInjection.AddMinvApplication(builder.Services);
        }
        if (command.StartsWith("siat", StringComparison.Ordinal))
        {
            // V4.1 · El token del SIN se descifra con la clave maestra: variable MINV_INTEGRATION_KEYS o el archivo local de claves
            SiatCli.LoadIntegrationKeys();
            MINV.Application.DependencyInjection.AddMinvApplication(builder.Services);
        }
        builder.Services.AddMinvInfrastructure(connection);
        if (billing)
        {
            // V4.1 · La carga factura contra el simulador del SIN EN PROCESO (mismo reloj simulado) y deja su estado en un
            // archivo que después lee el simulador HTTP (tools\servidores_locales.ps1): así conoce los CUIS, CUFD y documentos
            builder.Services.AddMinvSiat(new SiatOptions
            {
                Mode = SiatGatewayMode.InProcessSimulator,
                Simulator = new SiatSimulatorOptions { StateFile = SiatCli.FreshStateFile(options.GetValueOrDefault("siat-estado")) },
            });
        }
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MinvWriteDbContext>();

        switch (command)
        {
            case "datos-prueba":
                return await SeedAsync(host.Services, db, options);

            case "siat estado":
            case "siat preparar":
            case "siat sincronizar":
            case "siat procesar":
                return await SiatCli.RunAsync(command[5..], sp, db, options);

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

            case "roles":
                return await RolesAsync(db, options);

            default:
                Console.Error.WriteLine("Comando desconocido.\n" + Help);
                return 1;
        }
    }

    /// <summary>
    /// V4 · Roles de conexión (se ejecuta ANTES de migrar, con el rol dueño o un administrador con CREATEROLE):
    /// <c>minv_server</c> para el servidor en la nube y el API Gateway y <c>minv_app</c> para el escritorio con conexión
    /// directa. Ninguno es superusuario, ninguno tiene BYPASSRLS ni es dueño de las tablas: la seguridad por filas los
    /// alcanza siempre. Los privilegios los otorga la migración.
    /// </summary>
    private static async Task<int> RolesAsync(MinvWriteDbContext db, Dictionary<string, string> options)
    {
        async Task Upsert(string role, string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(password, "^[A-Za-z0-9_.-]{12,64}$"))
            {
                throw new ArgumentException($"La clave de {role} debe tener de 12 a 64 letras, números, «_», «.» o «-».");
            }
            var exists = await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_roles WHERE rolname = {role}").SingleAsync() > 0;
            // DDL: el nombre del rol es fijo y la clave ya se validó (solo letras, números, «_», «.» y «-»)
            var ddl = (exists ? "ALTER" : "CREATE") + " ROLE " + role + " LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '" + password + "'";
            await db.Database.ExecuteSqlRawAsync(ddl);
            var grant = "GRANT CONNECT ON DATABASE \"" + db.Database.GetDbConnection().Database.Replace("\"", "", StringComparison.Ordinal) + "\" TO " + role;
            await db.Database.ExecuteSqlRawAsync(grant);
            Console.WriteLine($"✔ Rol {role} {(exists ? "actualizado" : "creado")} (sin BYPASSRLS, no es dueño de las tablas)");
        }
        await Upsert("minv_server", options.GetValueOrDefault("clave-servidor"));
        await Upsert("minv_app", options.GetValueOrDefault("clave-app"));
        Console.WriteLine("Aplique ahora las migraciones (minv migrate): otorgan los privilegios a estos roles.");
        return 0;
    }

    /// <summary>Comprobaciones de la base: tablas, triggers append-only, RLS y conservación (Σ existencias = Σ movimientos).
    /// Mínimos de la V7 (migración V7WebPlatform): 157 tablas en 10 esquemas (27 de facturación en <c>billing</c>, 2 de
    /// garantías en <c>service</c>), 32 libros append-only, RLS por empresa en las 155 tablas con <c>tenant_id</c> y política
    /// restrictiva por sucursal en 64; con empresa, además, series en stock = stock (<c>inventory.v_serial_breaches</c>) y (V7) lo
    /// reservado de cada existencia = la suma de sus reservas de stock activas (de armados Y de carritos), más un informe de las
    /// cuentas de cliente y de los correos pendientes en la cola.</summary>
    private static async Task<int> VerifyAsync(MinvWriteDbContext db, ITenantContext tenantContext, string? tenantCode)
    {
        const int MinTables = 157, MinBilling = 27, MinService = 2, MinLedgers = 32, MinRls = 155, MinBranch = 64;
        var schemas = string.Join(",", Schemas.All.Select(s => $"'{s}'"));
        async Task<int> Scalar(string sql) => await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
        var tables = await Scalar($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema IN ({schemas}) AND table_name <> '__ef_migrations_history'");
        var billing = await Scalar($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema = '{Schemas.Billing}'");
        var service = await Scalar($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema = '{Schemas.Service}'");
        var triggers = await Scalar("SELECT count(*)::int AS \"Value\" FROM pg_trigger WHERE tgname = 'trg_append_only'");
        var rls = await Scalar($"SELECT count(*)::int AS \"Value\" FROM pg_tables WHERE rowsecurity AND schemaname IN ({schemas})");
        var branchPolicies = await Scalar("SELECT count(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'branch_isolation'");
        var ok = tables >= MinTables && billing >= MinBilling && service >= MinService && triggers >= MinLedgers && rls >= MinRls
                 && branchPolicies >= MinBranch;
        Console.WriteLine($"{(tables >= MinTables ? "✔" : "✖")} {tables} tablas en {Schemas.All.Count} esquemas");
        Console.WriteLine($"{(billing >= MinBilling ? "✔" : "✖")} Facturación SIAT: {billing} tablas en el esquema {Schemas.Billing}");
        Console.WriteLine($"{(service >= MinService ? "✔" : "✖")} Garantías y RMA: {service} tablas en el esquema {Schemas.Service}");
        Console.WriteLine($"{(triggers >= MinLedgers ? "✔" : "✖")} {triggers} libros mayores protegidos (append-only)");
        Console.WriteLine($"{(rls >= MinRls ? "✔" : "✖")} Row Level Security activa en {rls} tablas");
        Console.WriteLine($"{(branchPolicies >= MinBranch ? "✔" : "✖")} Aislamiento por sucursal (política restrictiva) en {branchPolicies} tablas");
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
            var transferBreaches = await Scalar($"SELECT count(*)::int AS \"Value\" FROM inventory.v_transfer_breaches WHERE tenant_id = '{tenant.Id}'");
            var transfers = await db.StockTransfers.CountAsync();
            ok &= transferBreaches == 0;
            Console.WriteLine($"{(transferBreaches == 0 ? "✔" : "✖")} Transferencias: {transfers} en total, {transferBreaches} líneas que no cuadran " +
                              "(despachado = recibido + faltante + en tránsito)");
            // V4.2 · En cada sucursal, las series en stock de una variante serializada = su stock (regla T-02)
            var serialBreaches = await Scalar($"SELECT count(*)::int AS \"Value\" FROM inventory.v_serial_breaches WHERE tenant_id = '{tenant.Id}'");
            var serials = await db.SerialNumbers.CountAsync();
            ok &= serialBreaches == 0;
            Console.WriteLine($"{(serialBreaches == 0 ? "✔" : "✖")} Series e IMEI: {serials} en total, {serialBreaches} variantes con series en stock " +
                              "distintas de su stock en una sucursal");
            // V7 · Reservas de armados Y de carritos: lo reservado de cada existencia es la suma de sus reservas de stock activas
            // (reglas S-03 y P-05; disponible = existencias − reservado)
            var reservationBreaches = await Scalar("SELECT count(*)::int AS \"Value\" FROM inventory.stock_levels l " +
                                                   $"WHERE l.tenant_id = '{tenant.Id}' AND l.quantity_reserved <> coalesce((SELECT sum(r.quantity) " +
                                                   "FROM inventory.stock_reservations r WHERE r.tenant_id = l.tenant_id AND r.stock_level_id = l.id " +
                                                   "AND r.status = 'Active'), 0)");
            var reservedCarts = await Scalar("SELECT count(*)::int AS \"Value\" FROM sales.pc_builds " +
                                             $"WHERE tenant_id = '{tenant.Id}' AND status = 'Reserved' AND kind = 'Cart'");
            var reservedBuilds = await Scalar("SELECT count(*)::int AS \"Value\" FROM sales.pc_builds " +
                                              $"WHERE tenant_id = '{tenant.Id}' AND status = 'Reserved' AND kind = 'Build'");
            var activeReservations = await Scalar("SELECT count(*)::int AS \"Value\" FROM inventory.stock_reservations " +
                                                  $"WHERE tenant_id = '{tenant.Id}' AND status = 'Active'");
            ok &= reservationBreaches == 0;
            Console.WriteLine($"{(reservationBreaches == 0 ? "✔" : "✖")} Reservas: {reservedCarts} carritos y {reservedBuilds} armados reservados " +
                              $"({activeReservations} reservas de stock activas), {reservationBreaches} existencias cuyo reservado no es la suma de sus reservas");
            // V7 · Tienda web (regla P-13, informativo): cuentas de cliente y correos de confirmación que esperan al despachador
            var accounts = await Scalar($"SELECT count(*)::int AS \"Value\" FROM sales.customer_accounts WHERE tenant_id = '{tenant.Id}'");
            var queued = await Scalar("SELECT count(*)::int AS \"Value\" FROM integration.outgoing_mail_dispatch " +
                                      $"WHERE tenant_id = '{tenant.Id}' AND status = 'Pending'");
            Console.WriteLine($"· Tienda web: {accounts} cuentas de cliente y {queued} correos de confirmación pendientes en la cola");
            // V4.1 · Facturación: el total fiscal (derivado de las líneas) es el cobrado y cada venta tiene UN documento vigente
            var fiscal = await Scalar($"SELECT count(*)::int AS \"Value\" FROM billing.fiscal_documents WHERE tenant_id = '{tenant.Id}'");
            if (fiscal > 0)
            {
                var totals = await Scalar("SELECT count(*)::int AS \"Value\" FROM billing.v_fiscal_document_totals t " +
                                          "JOIN billing.fiscal_documents d ON d.id = t.document_id JOIN sales.payments p ON p.invoice_id = d.invoice_id " +
                                          $"WHERE d.tenant_id = '{tenant.Id}' AND t.kind = 'Invoice' AND t.status = 'Valid' AND t.total_amount <> p.amount");
                var duplicated = await Scalar("SELECT count(*)::int AS \"Value\" FROM (SELECT invoice_id FROM billing.fiscal_documents " +
                                              $"WHERE tenant_id = '{tenant.Id}' AND invoice_id IS NOT NULL AND status IN ('Valid', 'Pending', 'Offline', 'InPackage') " +
                                              "GROUP BY invoice_id HAVING count(*) > 1) x");
                ok &= totals == 0 && duplicated == 0;
                Console.WriteLine($"{(totals + duplicated == 0 ? "✔" : "✖")} Facturación SIAT: {fiscal} documentos fiscales, {totals} facturas válidas con " +
                                  $"total distinto del cobrado, {duplicated} ventas con más de un documento vigente");
            }
            else
            {
                Console.WriteLine("· Facturación SIAT: la empresa todavía no emitió documentos fiscales");
            }
        }
        Console.WriteLine(ok ? "RESULTADO: base de datos correcta" : "RESULTADO: hay problemas");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Empresa de prueba con datos aleatorios (reproducibles con --semilla) y N días de operación. Las contraseñas se
    /// generan en cada ejecución: se muestran una sola vez y se guardan en el archivo de --credenciales (fuera del repo).
    /// </summary>
    private static async Task<int> SeedAsync(IServiceProvider services, MinvWriteDbContext db, Dictionary<string, string> options)
    {
        var code = (options.GetValueOrDefault("codigo") ?? DefaultCompany).Trim().ToUpperInvariant();
        if (await db.Tenants.AnyAsync(t => t.Code == code))
        {
            Console.Error.WriteLine($"✖ La empresa {code} ya existe. Recree la base (tools\\bd_local.ps1 -Accion recrear) o use otro --codigo.");
            return 1;
        }
        var seedOptions = new SeedOptions(code,
            Days: int.Parse(options.GetValueOrDefault("dias") ?? "60", CultureInfo.InvariantCulture),
            Seed: int.Parse(options.GetValueOrDefault("semilla") ?? "2026", CultureInfo.InvariantCulture),
            Billing: !options.ContainsKey("sin-facturacion"),
            BillingDays: int.Parse(options.GetValueOrDefault("dias-facturacion") ?? "25", CultureInfo.InvariantCulture),
            SiatSimulatorUrl: options.GetValueOrDefault("simulador") ?? SiatCli.DefaultSimulatorUrl);
        var watch = Stopwatch.StartNew();
        var seeder = services.GetRequiredService<LocalDataSeeder>();
        var result = await seeder.SeedAsync(seedOptions, line => Console.WriteLine("   " + line));
        // V7 · El texto del archivo de usuarios (personal y, aparte, las cuentas de cliente de la tienda web) lo arma el sembrador
        var text = SeedUsersFile.Text(result);
        Console.WriteLine();
        Console.WriteLine(text);
        if (options.GetValueOrDefault("credenciales") is { Length: > 0 } file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            await File.WriteAllTextAsync(file, text, new UTF8Encoding(true));
            Console.WriteLine($"✔ Credenciales guardadas en {file}");
        }
        if (options.GetValueOrDefault("integracion") is { Length: > 0 } keysFile)
        {
            // V4 · Token de la API Key de la tienda y secreto del webhook de prueba (se ven una sola vez: quedan en este equipo)
            var lines = new List<string>
            {
                "M-INV · integraciones de PRUEBA (solo este equipo; no las use en producción)",
                $"API Key «{result.ApiKeyName}» (sucursal CM · catalog:read, stock:read, orders:write):",
                "MINV_API_KEY=" + result.ApiKeyToken,
            };
            if (result.WebhookSecret is { } secret)
            {
                lines.Add("Secreto del webhook de prueba (https://tienda.techzone.example/webhooks/minv):");
                lines.Add("MINV_WEBHOOK_SECRET=" + secret);
            }
            if (result.Billing is { } billed)
            {
                // V4.1 · Token de SIMULACIÓN del SIN: el simulador HTTP de este equipo lo acepta (tools\servidores_locales.ps1)
                lines.Add($"Facturación SIAT de PRUEBA: NIT {billed.Nit} · ambiente {billed.Environment} (pruebas) · simulador del SIN {billed.SimulatorUrl}" +
                          (billed.SimulatorStateFile is { } state ? $" (estado: {state})" : string.Empty));
                lines.Add("Token delegado de SIMULACIÓN (solo lo acepta el simulador de este equipo; nunca lo use con el SIN real):");
                lines.Add("MINV_SIAT_TOKEN=" + billed.SiatToken);
            }
            await File.AppendAllLinesAsync(keysFile, lines, new UTF8Encoding(true));
            Console.WriteLine($"✔ API Key de prueba{(result.Billing is null ? string.Empty : " y token de simulación del SIN")} guardados en {keysFile}");
        }
        Console.WriteLine($"✔ Datos de prueba listos en {watch.Elapsed.TotalSeconds:N0} s");
        return 0;
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
        minv · administración de M-INV (PostgreSQL local o en la nube)
          minv roles --clave-servidor … [--clave-app …]      (V4: roles minv_server / minv_app, antes de migrar)
          minv migrate
          minv tenant create --codigo DEMO --razon-social "Mi empresa" --admin admin@empresa.com --nombre "Administrador" [--clave …] [--moneda BOB] [--zona America/La_Paz]
          minv import-v21 --archivo src/M-INV_V2_Colaborativo.xlsx --codigo DEMO --admin admin@distribuidorademo.example --nombre "Administrador" [--clave …] [--zona America/Bogota] [--moneda COP] [--pais CO --pais-nombre Colombia]
          minv user password --codigo TECHZONE --correo admin@techzone.example [--clave …]
          minv verify [--codigo DEMO]
          minv datos-prueba [--codigo TECHZONE] [--dias 60] [--semilla 2026] [--credenciales %LOCALAPPDATA%\M-INV\usuarios-prueba.txt] [--integracion %LOCALAPPDATA%\M-INV\claves-integracion.txt]
                            [--dias-facturacion 25] [--sin-facturacion] [--siat-estado %LOCALAPPDATA%\M-INV\siat-simulador.json] [--simulador http://localhost:5095]
          minv siat estado [--codigo TECHZONE]             (V4.1: modo de cada punto de venta, CUIS, CUFD, pendientes y alertas)
          minv siat preparar [--codigo TECHZONE]           (hora del SIN, CUIS y CUFD del día en cada punto de venta, catálogos)
          minv siat sincronizar [--codigo TECHZONE]        (los 18 catálogos del SIN)
          minv siat procesar [--codigo TECHZONE] [--forzar] (envía pendientes, recupera fuera de línea, paquetes, notas y correos)
          minv siat simulador-estado | simulador-apagar | simulador-encender [--simulador http://localhost:5095]   (corte de internet simulado)
        Conexión: --conexion "Host=localhost;Database=minv;Username=minv_owner;Password=…" o variable MINV_DB.
        """;
}
