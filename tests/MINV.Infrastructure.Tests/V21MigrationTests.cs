using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Catalog;
using MINV.Infrastructure.Importing.V21;
using MINV.Infrastructure.Provisioning;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// Migración V2.1 → V3 con el libro colaborativo REAL de la rama Inventario-V2.1 (src/M-INV_V2_Colaborativo.xlsx):
/// lectura de las tablas, plan de migración a través del dominio y paridad con la instantánea que la V2.1 guardó.
/// </summary>
public sealed class V21MigrationTests
{
    private static readonly string WorkbookPath = Path.Combine(RepoRoot(), "src", "M-INV_V2_Colaborativo.xlsx");
    private static readonly Lazy<V21Workbook> Wb = new(() => V21Workbook.Open(WorkbookPath));

    internal static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "MINV.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("No se encontró MINV.sln");
    }

    internal static (V21Seeds Seeds, User Admin) Seeds()
    {
        var tenant = Guid.NewGuid();
        var admin = new User(tenant, "admin@distribuidorademo.example", "Administrador M-INV");
        var units = TenantProvisioner.DefaultUnits.ToDictionary(u => u.Code, u => new UnitOfMeasure(tenant, u.Code, u.Name, u.Decimals, u.Description),
            StringComparer.OrdinalIgnoreCase);
        var roles = RoleCodes.All.ToDictionary(r => r.Code, _ => Guid.NewGuid());
        var types = MovementType.CreateDefaults(tenant).ToDictionary(t => t.Code);
        return (new V21Seeds(tenant, admin.Id, Guid.NewGuid(), Guid.NewGuid(), "ALM01", Guid.NewGuid(), Guid.NewGuid(), units, roles, types,
            new Dictionary<string, User> { [admin.Email] = admin }), admin);
    }

    [Fact]
    public void Lee_las_tablas_y_los_parametros_del_libro_V21()
    {
        var wb = Wb.Value;
        Assert.Equal("Distribuidora Demo S.A.S.", wb.CompanyName);
        Assert.Equal(0.20m, wb.AlertMargin);
        Assert.Equal(6, wb.Users().Count);
        Assert.Equal(34, wb.Products().Count);
        Assert.Equal(6, wb.Suppliers().Count);
        Assert.Equal(473, wb.Movements().Count);
        Assert.True(wb.Activity().Count > 400);
        Assert.Equal(2, wb.Counts().Count);
        Assert.NotNull(wb.SnapshotSerial);
        Assert.Equal(34, wb.Snapshot().Count);
    }

    [Fact]
    public void El_plan_reproduce_la_historia_por_el_dominio_sin_errores()
    {
        var (seeds, _) = Seeds();
        var plan = V21ImportPlanner.Build(Wb.Value, seeds, new V21ImportOptions("America/Bogota"), DateTimeOffset.UtcNow);
        Assert.Empty(plan.Errors);
        var report = plan.Report!;
        Assert.Equal(34, report.Products);
        Assert.Equal(473, report.Movements);
        Assert.Equal(5, report.Users);            // el ADMIN de la V2.1 es el administrador del tenant
        Assert.Equal(2, report.CountLines);
        Assert.True(report.ActivityRows > 400);
        // Conservación (regla R-04 de la V1): Σ existencias = Σ movimientos con signo
        var levels = plan.BySku.Values.Select(v => v.Level).Distinct().Sum(l => l.QuantityOnHand);
        var signed = plan.Movements.Sum(m => m.Type.Signed(m.Movement.Quantity));
        Assert.Equal(signed, levels);
        Assert.All(plan.BySku.Values, v => Assert.True(v.Level.QuantityOnHand >= 0));
        Assert.All(plan.Movements, m => Assert.StartsWith(m.Source.IsSalesFragment ? "S-" : "E-", m.Movement.LegacyReference));
    }

    [Fact]
    public void Paridad_V21_V3_stock_semaforo_cobertura_ranking_alertas_y_pedido()
    {
        var (seeds, _) = Seeds();
        var plan = V21ImportPlanner.Build(Wb.Value, seeds, new V21ImportOptions("America/Bogota"), DateTimeOffset.UtcNow);
        var parity = V21Parity.Check(Wb.Value, plan);
        Assert.True(parity.Checked);
        Assert.True(parity.Ok, string.Join(Environment.NewLine, parity.Differences));
        Assert.Equal(34, parity.Products);
        Assert.True(parity.Alerts > 0 && parity.OrderLines > 0);
    }

    [Fact]
    public void Las_ubicaciones_A_01_01_se_convierten_en_zona_pasillo_estanteria_y_posicion()
    {
        var (seeds, _) = Seeds();
        var plan = V21ImportPlanner.Build(Wb.Value, seeds, new V21ImportOptions("America/Bogota"), DateTimeOffset.UtcNow);
        var bins = plan.Entities.OfType<MINV.Domain.Warehousing.Bin>().ToList();
        Assert.Contains(bins, b => b.Code == "ALM01-A-01-01");
        Assert.Equal(bins.Count, bins.Select(b => b.Code).Distinct().Count());
        Assert.Equal(plan.Report!.Bins, bins.Count);
    }

    [Fact]
    public void Los_timestamps_locales_se_guardan_en_UTC()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/La_Paz");
        var utc = V21ImportPlanner.ToUtc(46290.5, zone);          // 25/09/2026 12:00 hora de Bolivia (UTC-4)
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 16, 0, 0, TimeSpan.Zero), utc);
    }
}
