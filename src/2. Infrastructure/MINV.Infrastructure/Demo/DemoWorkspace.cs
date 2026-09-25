using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Sales;
using MINV.Infrastructure.Importing.V21;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Demo;

/// <summary>Usuario de la demostración (los de la V2.1, con su rol).</summary>
public sealed record DemoUser(string Email, string DisplayName, string RoleCode, string RoleName);

/// <summary>
/// Empresa de demostración lista para ingresar. <see cref="Password"/> es aleatoria en cada ejecución (nunca se guarda
/// en disco ni en el repositorio) y la comparten todos los usuarios de la demostración.
/// </summary>
public sealed record DemoSession(string TenantCode, string CompanyName, DateOnly Today, string Password,
    IReadOnlyList<DemoUser> Users, V21ImportResult Import);

/// <summary>Estado de la demostración (uno por proceso): se prepara una vez y se reutiliza al volver a ingresar.</summary>
public sealed class DemoState
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    public DemoSession? Session { get; internal set; }
}

/// <summary>
/// Prepara el modo demostración: migra el libro colaborativo de la V2.1 a la base en memoria con el mismo importador
/// que <c>minv import-v21</c> (reglas del dominio, poka-yoke y paridad incluidos) y habilita el ingreso de sus usuarios.
/// </summary>
public sealed class DemoWorkspace(MINVDbContext db, V21Importer importer, DemoClock clock, IPasswordHasher hasher, DemoState state)
{
    public const string TenantCode = "DEMO";
    public const string AdminEmail = "admin@distribuidorademo.example";
    public const string TimeZoneId = "America/Bogota";

    /// <summary>Libro de la V2.1 que se distribuye con el cliente (carpeta <c>Demo</c> junto al ejecutable).</summary>
    public static string DefaultWorkbookPath => Path.Combine(AppContext.BaseDirectory, "Demo", "M-INV_V2_Colaborativo.xlsx");

    public async Task<DemoSession> PrepareAsync(string workbookPath, CancellationToken ct = default)
    {
        await state.Gate.WaitAsync(ct);
        try
        {
            return state.Session ??= await CreateAsync(workbookPath, ct);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private async Task<DemoSession> CreateAsync(string workbookPath, CancellationToken ct)
    {
        if (!File.Exists(workbookPath))
        {
            throw new FileNotFoundException("No se encontró el libro de demostración de la V2.1.", workbookPath);
        }
        // «Hoy» de la demostración = el momento de los últimos datos de la V2.1 (instantánea, movimientos y actividad).
        var wb = V21Workbook.Open(workbookPath);
        var last = new[] { wb.SnapshotSerial ?? 0 }
            .Concat(wb.Movements().Select(m => m.TimestampSerial))
            .Concat(wb.Activity().Select(a => a.TimestampSerial))
            .Max();
        if (last > 0)
        {
            clock.StartAt(V21ImportPlanner.ToUtc(last, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)).AddMinutes(10));
        }

        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', 'x').Replace('/', 'y');
        var result = await importer.ImportAsync(new V21ImportRequest(workbookPath, TenantCode, AdminEmail, "Administrador M-INV",
            password, TimeZoneId, CountryIso: "CO", CountryName: "Colombia"), ct);

        // La V2.1 no tenía contraseñas (identificaba con Microsoft 365): en la demostración todos usan la misma, aleatoria.
        var hash = hasher.Hash(password);
        var withCredential = await db.UserCredentials.Select(c => c.UserId).ToListAsync(ct);
        foreach (var user in await db.Users.Where(u => u.IsActive && !withCredential.Contains(u.Id)).ToListAsync(ct))
        {
            db.UserCredentials.Add(new UserCredential(user.TenantId, user.Id, hash, hasher.Algorithm, hasher.Iterations, clock.UtcNow,
                mustChangePassword: false));
        }

        // La V2.1 no tenía fotos: cada producto recibe la ilustración que corresponde a su nombre (galería del catálogo)
        var variants = await (from v in db.ProductVariants
                              join p in db.Products on v.ProductId equals p.Id
                              select new { v.TenantId, v.Id, p.Name }).ToListAsync(ct);
        // Tampoco tenía precios de venta: 60 % sobre el costo promedio (IVA incluido) para poder vender en el punto de venta
        var priceList = await db.PriceLists.FirstAsync(l => l.IsDefault, ct);
        var costs = (await db.AverageCostHistory.Select(c => new { c.VariantId, c.EffectiveAt, c.AverageCost }).ToListAsync(ct))
            .GroupBy(c => c.VariantId).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.EffectiveAt).First().AverageCost);
        var priced = (await db.PriceListItems.Select(i => i.VariantId).ToListAsync(ct)).ToHashSet();
        foreach (var v in variants)
        {
            db.ProductImages.Add(new ProductImage(v.TenantId, v.Id, ProductImageLibrary.ForProduct(v.Name), "image/png",
                ProductImageLibrary.KindFor(v.Name) + ".png"));
            if (!priced.Contains(v.Id))
            {
                var cost = costs.GetValueOrDefault(v.Id);
                db.PriceListItems.Add(new PriceListItem(v.TenantId, priceList.Id, v.Id,
                    cost > 0 ? decimal.Round(cost * 1.6m, 1, MidpointRounding.AwayFromZero) : 10m));
            }
        }
        await db.SaveChangesAsync(ct);

        var order = RoleCodes.All.Select((r, i) => (r.Code, i)).ToDictionary(x => x.Code, x => x.i);
        var users = (await (from u in db.Users
                            join ur in db.UserRoles on u.Id equals ur.UserId
                            join r in db.Roles on ur.RoleId equals r.Id
                            where u.IsActive
                            select new DemoUser(u.Email, u.DisplayName, r.Code, r.Name)).ToListAsync(ct))
            .OrderBy(u => order.GetValueOrDefault(u.RoleCode, 99)).ThenBy(u => u.DisplayName, StringComparer.CurrentCulture).ToList();
        var company = await db.Tenants.Where(t => t.Code == TenantCode).Select(t => t.LegalName).FirstAsync(ct);
        return new DemoSession(TenantCode, company, clock.TodayIn(TimeZoneId), password, users, result);
    }
}
