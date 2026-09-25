using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Inventory.Movements;
using MINV.Application.Inventory.Queries;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Infrastructure.Importing.V21;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Provisioning;
using Npgsql;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// Pruebas contra un PostgreSQL real (15+). Se ejecutan solo si la variable <c>MINV_TEST_PG</c> tiene una cadena de
/// conexión con permiso para crear bases de datos, p. ej.:
/// <c>Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres</c>.
/// Cada ejecución crea una base temporal, aplica las migraciones y la elimina al final.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFixture.Variable)))
        {
            Skip = $"Defina {PostgresFixture.Variable} para ejecutar las pruebas contra PostgreSQL.";
        }
    }
}

public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Variable = "MINV_TEST_PG";
    private string? _admin;

    public string ConnectionString { get; private set; } = string.Empty;

    public string Database { get; } = "minv_test_" + Guid.NewGuid().ToString("N")[..10];

    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(_admin))
        {
            return;
        }
        await using (var conn = new NpgsqlConnection(_admin))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {Database}", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        ConnectionString = new NpgsqlConnectionStringBuilder(_admin) { Database = Database, IncludeErrorDetail = true }.ConnectionString;
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MINVDbContext>().Database.MigrateAsync();
    }

    public ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddMinvApplication();
        services.AddMinvInfrastructure(ConnectionString);
        return services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_admin))
        {
            return;
        }
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(_admin);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {Database} WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

public sealed class PostgresIntegrationTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private const string AdminEmail = "admin@demo.example";
    private const string AdminPassword = "Clave-Prueba-2026";

    private async Task<(IServiceScope Scope, ProvisionedTenant Tenant)> NewTenantAsync(ServiceProvider provider, string code)
    {
        var scope = provider.CreateScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().ProvisionAsync(
            new ProvisionTenantRequest(code, "Empresa " + code, null, AdminEmail, "Administrador", AdminPassword));
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
            new LoginCommand(code, AdminEmail, AdminPassword, "pruebas", "3.0.0"));
        return (scope, tenant);
    }

    private static async Task<Guid> ProductAsync(IServiceProvider sp, ProvisionedTenant tenant, string sku)
    {
        var db = sp.GetRequiredService<MINVDbContext>();
        var category = new Domain.Catalog.Category(tenant.TenantId, "GEN" + sku[..3], "General " + sku);
        var product = Domain.Catalog.Product.Create(tenant.TenantId, sku, "Producto " + sku, category.Id, tenant.Units["UND"]);
        db.AddRange(category, product, Batch.CreateDefault(tenant.TenantId, product.DefaultVariant.Id),
            new Domain.Catalog.ProductStockPolicy(tenant.TenantId, product.DefaultVariant.Id, tenant.WarehouseId, 5, 50));
        await db.SaveChangesAsync();
        return product.DefaultVariant.Id;
    }

    [PostgresFact]
    public async Task Las_migraciones_crean_97_tablas_triggers_RLS_y_vistas()
    {
        await using var provider = pg.Services();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MINVDbContext>();
        async Task<int> Count(string sql) => await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
        Assert.Equal(97, await Count("SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema IN ('iam','catalog','warehouse','inventory','purchasing','sales','accounting') AND table_name <> '__ef_migrations_history'"));
        Assert.Equal(7, await Count("SELECT count(*)::int AS \"Value\" FROM pg_trigger WHERE tgname = 'trg_append_only'"));
        Assert.Equal(95, await Count("SELECT count(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation'"));
        Assert.Equal(3, await Count("SELECT count(*)::int AS \"Value\" FROM information_schema.views WHERE table_name IN ('v_stock_by_variant','v_conservation_breaches','v_activity')"));
        Assert.Equal(5, await db.Modules.CountAsync());
    }

    [PostgresFact]
    public async Task Registrar_movimientos_por_la_tuberia_completa_con_poka_yoke_y_auditoria()
    {
        await using var provider = pg.Services();
        var (scope, tenant) = await NewTenantAsync(provider, "T" + Random.Shared.Next(1000, 9999));
        using (scope)
        {
            var sp = scope.ServiceProvider;
            await ProductAsync(sp, tenant, "FER-001");
            var mediator = sp.GetRequiredService<IMediator>();
            var bin = $"{tenant.WarehouseCode}-GENERAL";
            await mediator.Send(new RegisterMovementCommand("FER-001", bin, MovementTypeCodes.Receipt, 20, DocumentReference: "FC-1"));
            var result = await mediator.Send(new RegisterMovementCommand("FER-001", bin, MovementTypeCodes.Issue, 8));
            Assert.Equal(12, result.QuantityOnHand);
            await Assert.ThrowsAsync<InsufficientStockException>(() =>
                mediator.Send(new RegisterMovementCommand("FER-001", bin, MovementTypeCodes.Issue, 100)));
            var view = await mediator.Send(new GetStockProjectionQuery());
            var row = Assert.Single(view.Result.Stock);
            Assert.Equal(12, row.Stock);
            Assert.Equal(8, row.Sales30Days);
            var db = sp.GetRequiredService<MINVDbContext>();
            var outcomes = await db.AuditLogs.Select(a => a.Outcome).ToListAsync();
            Assert.Equal(2, outcomes.Count(o => o == AuditOutcome.Succeeded));
            Assert.Equal(1, outcomes.Count(o => o == AuditOutcome.Rejected));
            Assert.Equal(1, await db.AccessLogs.CountAsync(a => a.Succeeded));
        }
    }

    [PostgresFact]
    public async Task El_libro_mayor_es_append_only_tambien_en_la_base_de_datos()
    {
        await using var provider = pg.Services();
        var (scope, tenant) = await NewTenantAsync(provider, "A" + Random.Shared.Next(1000, 9999));
        using (scope)
        {
            var sp = scope.ServiceProvider;
            await ProductAsync(sp, tenant, "PLO-001");
            await sp.GetRequiredService<IMediator>().Send(
                new RegisterMovementCommand("PLO-001", $"{tenant.WarehouseCode}-GENERAL", MovementTypeCodes.Receipt, 3));
            var db = sp.GetRequiredService<MINVDbContext>();
            var update = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync("UPDATE inventory.stock_movements SET quantity = 999"));
            Assert.Contains("append-only", update.MessageText);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM inventory.stock_movements"));
        }
    }

    [PostgresFact]
    public async Task Dos_sesiones_que_venden_a_la_vez_no_dejan_el_stock_negativo()
    {
        await using var provider = pg.Services();
        var (scope, tenant) = await NewTenantAsync(provider, "C" + Random.Shared.Next(1000, 9999));
        using (scope)
        {
            var variant = await ProductAsync(scope.ServiceProvider, tenant, "ELE-001");
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
                new RegisterMovementCommand("ELE-001", $"{tenant.WarehouseCode}-GENERAL", MovementTypeCodes.Receipt, 1));
            // Dos sesiones leen la misma existencia (1 unidad) y ambas intentan vender la última unidad.
            using var s1 = provider.CreateScope();
            using var s2 = provider.CreateScope();
            var (db1, level1, issue1) = Session(s1.ServiceProvider);
            var (db2, level2, issue2) = Session(s2.ServiceProvider);
            var ctx = new MovementContext(tenant.AdminUserId, DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow);
            db1.Add(level1.Register(issue1, 1, new UnitRule("UND", false), ctx));
            db2.Add(level2.Register(issue2, 1, new UnitRule("UND", false), ctx));
            await db1.SaveChangesAsync();
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => db2.SaveChangesAsync());

            (MINVDbContext, StockLevel, MovementType) Session(IServiceProvider sp)
            {
                sp.GetRequiredService<ITenantContext>().Set(tenant.TenantId);
                var db = sp.GetRequiredService<MINVDbContext>();
                var batch = db.Batches.Single(b => b.VariantId == variant);
                return (db, db.StockLevels.Single(l => l.BatchId == batch.Id), db.MovementTypes.Single(t => t.Code == MovementTypeCodes.Issue));
            }
        }
    }

    [PostgresFact]
    public async Task Migrar_el_libro_de_la_V21_con_paridad_y_conservacion()
    {
        await using var provider = pg.Services();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var result = await sp.GetRequiredService<V21Importer>().ImportAsync(new V21ImportRequest(
            Path.Combine(V21MigrationTests.RepoRoot(), "src", "M-INV_V2_Colaborativo.xlsx"), "V21DEMO",
            "admin@distribuidorademo.example", "Administrador M-INV", AdminPassword, "America/Bogota", "COP", "CO", "Colombia"));
        Assert.True(result.Parity.Ok, string.Join(Environment.NewLine, result.Parity.Differences));
        Assert.Equal(473, result.Report.Movements);
        var db = sp.GetRequiredService<MINVDbContext>();
        Assert.Equal(473, await db.StockMovements.CountAsync());
        var breaches = await db.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM inventory.v_conservation_breaches").SingleAsync();
        Assert.Equal(0, breaches);
        var mediator = sp.GetRequiredService<IMediator>();
        await mediator.Send(new LoginCommand("V21DEMO", "admin@distribuidorademo.example", AdminPassword, "pruebas", "3.0.0"));
        var view = await mediator.Send(new GetStockProjectionQuery());
        Assert.Equal(34, view.Result.Stock.Count);
    }

    /// <summary>
    /// <c>minv datos-prueba</c> contra PostgreSQL real: ventas en caja, compras recibidas, anulaciones y asientos respetan
    /// los CHECK (arcos exclusivos de pedidos y recepciones), los triggers de partida doble y los libros append-only.
    /// </summary>
    [PostgresFact]
    public async Task Los_datos_de_prueba_respetan_todas_las_restricciones_de_PostgreSQL()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MINV.Infrastructure.Services.DemoClock>();
        services.AddSingleton<IClock>(sp => sp.GetRequiredService<MINV.Infrastructure.Services.DemoClock>());
        services.AddMinvApplication();
        services.AddMinvInfrastructure(pg.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<MINV.Infrastructure.Seeding.LocalDataSeeder>()
            .SeedAsync(new MINV.Infrastructure.Seeding.SeedOptions("SEMILLA", Days: 20, Seed: 11), _ => { });
        Assert.True(result.Tickets > 10);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MINVDbContext>();
        var breaches = await db.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM inventory.v_conservation_breaches").SingleAsync();
        Assert.Equal(0, breaches);
        var unbalanced = await db.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM (SELECT journal_entry_id FROM accounting.journal_lines GROUP BY journal_entry_id " +
            "HAVING sum(debit) <> sum(credit)) x").SingleAsync();
        Assert.Equal(0, unbalanced);
        Assert.Equal(61, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM catalog.product_images").SingleAsync());

        // Todas las consultas de las pantallas con el administrador: PostgreSQL devuelve numéricos de hasta 1000 cifras y
        // System.Decimal solo admite 28-29; un cálculo con división hecho en SQL no debe llegar sin redondear al cliente.
        var admin = result.Users.First(u => u.RoleCode == RoleCodes.Admin);
        using var session = provider.CreateScope();
        var m = session.ServiceProvider.GetRequiredService<IMediator>();
        await m.Send(new LoginCommand("SEMILLA", admin.Email, admin.Password, "pruebas", "3.1.0"));
        var (from, to) = (result.From, result.To);
        Assert.NotEmpty((await m.Send(new MINV.Application.Partners.GetCustomersQuery())).Customers);
        Assert.NotEmpty(await m.Send(new MINV.Application.Partners.GetSuppliersQuery()));
        Assert.NotEmpty(await m.Send(new MINV.Application.Catalog.GetCatalogQuery()));
        Assert.NotNull(await m.Send(new MINV.Application.Catalog.GetCatalogOptionsQuery()));
        Assert.NotEmpty(await m.Send(new MINV.Application.Catalog.GetProductImagesQuery()));
        Assert.NotEmpty(await m.Send(new MINV.Application.Sales.GetSellableProductsQuery()));
        Assert.NotNull(await m.Send(new MINV.Application.Sales.GetPosStateQuery()));
        var sales = await m.Send(new MINV.Application.Sales.GetSalesQuery(from, to));
        Assert.NotEmpty(await m.Send(new MINV.Application.Sales.GetSaleLinesQuery(sales[0].InvoiceNumber)));
        Assert.True((await m.Send(new MINV.Application.Reports.GetSalesReportQuery(from, to))).Revenue > 0);
        Assert.NotNull(await m.Send(new MINV.Application.Reports.GetPurchasesReportQuery(from, to)));
        Assert.NotEmpty(await m.Send(new MINV.Application.Reports.GetMovementsReportQuery(from, to)));
        var orders = await m.Send(new MINV.Application.Purchasing.GetPurchaseOrdersQuery());
        Assert.NotNull(await m.Send(new MINV.Application.Purchasing.GetPurchaseOrderQuery(orders[0].Id)));
        Assert.NotEmpty(await m.Send(new MINV.Application.Accounting.GetChartOfAccountsQuery()));
        Assert.NotEmpty(await m.Send(new MINV.Application.Accounting.GetJournalQuery(from, to)));
        Assert.True((await m.Send(new MINV.Application.Accounting.GetIncomeStatementQuery(from, to))).TotalRevenue > 0);
        Assert.NotEmpty(await m.Send(new MINV.Application.Iam.GetUsersQuery()));
        Assert.NotEmpty((await m.Send(new MINV.Application.Iam.GetRolesQuery())).Roles);
        Assert.NotNull(await m.Send(new MINV.Application.Iam.GetCompanySettingsQuery()));
        Assert.NotEmpty((await m.Send(new GetStockProjectionQuery())).Result.Stock);
        Assert.NotEmpty(await m.Send(new GetActivityQuery()));
    }
}
