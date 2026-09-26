using System.Security.Cryptography;
using MINV.Domain.Iam;
using MINV.Infrastructure.Seeding;

namespace MINV.Infrastructure.Demo;

/// <summary>Usuario de la demostración (uno por rol, con su rol).</summary>
public sealed record DemoUser(string Email, string DisplayName, string RoleCode, string RoleName);

/// <summary>
/// Empresa de demostración lista para ingresar. <see cref="Password"/> es aleatoria en cada ejecución (nunca se guarda
/// en disco ni en el repositorio) y la comparten todos los usuarios de la demostración. V4.2: <see cref="Seed"/> resume la
/// carga de Tech Zone Gaming (productos, ventas, series, RMA, armados) y <see cref="Billing"/> su facturación.
/// </summary>
public sealed record DemoSession(string TenantCode, string CompanyName, DateOnly Today, string Password, IReadOnlyList<DemoUser> Users, SeedResult Seed)
{
    /// <summary>V4.1 · Facturación de la demostración (simulador del SIN en memoria).</summary>
    public SeedBilling? Billing => Seed.Billing;

    public override string ToString() => $"DemoSession {TenantCode} · {CompanyName} · {Today:dd/MM/yyyy}";
}

/// <summary>Estado de la demostración (uno por proceso): se prepara una vez y se reutiliza al volver a ingresar.</summary>
public sealed class DemoState
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    public DemoSession? Session { get; internal set; }
}

/// <summary>
/// Prepara el modo demostración (regla A-12): V4.2 · la empresa de prueba <b>Tech Zone Gaming S.R.L.</b> cargada en la base
/// en memoria con el MISMO generador que la base local (<see cref="LocalDataSeeder"/>, los mismos casos de uso y la misma
/// tubería) con menos días de operación para que abra rápido: tres sucursales, catálogo de tecnología con fichas técnicas,
/// series e IMEI, casos RMA, armados de PC y facturación con el simulador del SIN EN MEMORIA (sin red ni disco). La
/// contraseña, aleatoria, la comparten todos los usuarios; la pantalla de inicio ofrece un acceso por rol. El libro de la
/// V2.1 ya no es la fuente de la demostración (su importador sigue en <c>minv import-v21</c>). Medido en frío (proceso
/// nuevo, incluido el modelo de EF Core): unos 17 s, de ellos unos 2 s de la primera distribución a Cochabamba y Santa Cruz
/// (90 productos con pocas unidades, para que esas sucursales no queden casi agotadas). Lo que más pesa es la primera vez
/// de cada caso de uso (compilación de métodos y de consultas), no la cantidad de días: por eso las altas independientes
/// (usuarios, fichas, saldos iniciales) van en paralelo y los procesos cortos corren sin la PGO dinámica
/// (Directory.Build.props).
/// </summary>
public sealed class DemoWorkspace(LocalDataSeeder seeder, DemoState state)
{
    public const string TenantCode = "TECHZONE";
    public const string AdminEmail = "admin@" + Domain;
    public const string TimeZoneId = "America/La_Paz";

    /// <summary>Días de operación simulados de la demostración (la base local carga 60): lo justo para que haya ventas,
    /// transferencias, compras recibidas, casos RMA en varios estados y los 8 armados, y abra en pocos segundos.</summary>
    public const int Days = 8;

    /// <summary>Días facturados de la demostración.</summary>
    public const int BillingDays = 6;

    /// <summary>Volumen de ventas diarias de la demostración (1 = el de la base local).</summary>
    public const double Volume = 0.4;

    private const string Domain = "techzone.example";

    /// <summary>Prepara (una vez por proceso) la demostración de Tech Zone Gaming.</summary>
    public async Task<DemoSession> PrepareAsync(CancellationToken ct = default)
    {
        await state.Gate.WaitAsync(ct);
        try
        {
            return state.Session ??= await CreateAsync(ct);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private async Task<DemoSession> CreateAsync(CancellationToken ct)
    {
        // Una sola contraseña aleatoria para todos (nunca en disco) y un token de SIMULACIÓN del SIN solo en memoria
        // (siempre con letras y números: es lo que exige la validación de las contraseñas)
        var password = "Tz" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', 'x').Replace('/', 'y') +
                       RandomNumberGenerator.GetInt32(10, 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var token = "DEMO-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
        // Factura cada venta con el simulador en memoria; los escenarios de contingencia (corte de internet, CAFC, anulaciones,
        // notas) y el pedido sugerido completo quedan para la base local: así la demostración abre rápido (regla A-12, mismos
        // casos de uso)
        var result = await seeder.SeedAsync(new SeedOptions(TenantCode, Domain: Domain, Days: Days, TimeZoneId: TimeZoneId, BillingDays: BillingDays,
            SiatToken: token, SharedPassword: password, Volume: Volume, ResetClock: false, BillingScenarios: false, SuggestedOrders: false), _ => { }, ct);

        // Un acceso por rol (el primero de cada uno: la casa matriz), en el orden de los roles
        var order = RoleCodes.All.Select((r, i) => (r.Code, i)).ToDictionary(x => x.Code, x => x.i);
        var users = result.Users.GroupBy(u => u.RoleCode).Select(g => g.First())
            .OrderBy(u => order.GetValueOrDefault(u.RoleCode, 99))
            .Select(u => new DemoUser(u.Email, u.Name, u.RoleCode, u.RoleName)).ToList();
        return new DemoSession(TenantCode, result.CompanyName, result.To, password, users, result);
    }
}
