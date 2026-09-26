using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Inventory.Movements;
using MINV.Application.Inventory.Queries;
using MINV.Domain.Billing;
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
        await scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>().Database.MigrateAsync();
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
        var db = sp.GetRequiredService<MinvWriteDbContext>();
        var category = new Domain.Catalog.Category(tenant.TenantId, "GEN" + sku[..3], "General " + sku);
        var product = Domain.Catalog.Product.Create(tenant.TenantId, sku, "Producto " + sku, category.Id, tenant.Units["UND"]);
        db.AddRange(category, product, Batch.CreateDefault(tenant.TenantId, product.DefaultVariant.Id),
            new Domain.Catalog.ProductStockPolicy(tenant.TenantId, tenant.BranchId, product.DefaultVariant.Id, tenant.WarehouseId, 5, 50));
        await db.SaveChangesAsync();
        return product.DefaultVariant.Id;
    }

    [PostgresFact]
    public async Task Las_migraciones_crean_140_tablas_triggers_RLS_por_sucursal_y_vistas()
    {
        await using var provider = pg.Services();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        async Task<int> Count(string sql) => await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
        Assert.Equal(140, await Count("SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema IN ('iam','catalog','warehouse','inventory','purchasing','sales','accounting','integration','billing') AND table_name <> '__ef_migrations_history'"));
        Assert.Equal(27, await Count("SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema = 'billing'"));
        Assert.Equal(24, await Count("SELECT count(*)::int AS \"Value\" FROM pg_trigger WHERE tgname = 'trg_append_only'"));
        Assert.Equal(24, await Count("SELECT count(*)::int AS \"Value\" FROM pg_trigger WHERE tgname = 'trg_append_only_truncate'"));
        Assert.Equal(138, await Count("SELECT count(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation'"));
        Assert.Equal(55, await Count("SELECT count(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'branch_isolation' AND permissive = 'RESTRICTIVE'"));
        // Toda tabla con tenant_id tiene la política de empresa (incluidas las 30 de la V4.1)
        Assert.Equal(0, await Count("SELECT count(*)::int AS \"Value\" FROM information_schema.columns c JOIN pg_tables t ON t.schemaname = c.table_schema AND t.tablename = c.table_name WHERE c.column_name = 'tenant_id' AND NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname = c.table_schema AND p.tablename = c.table_name AND p.policyname = 'tenant_isolation')"));
        Assert.Equal(5, await Count("SELECT count(*)::int AS \"Value\" FROM information_schema.views WHERE table_name IN ('v_stock_by_variant','v_conservation_breaches','v_activity','v_transfer_breaches','v_fiscal_document_totals')"));
        Assert.Equal(2, await Count("SELECT count(*)::int AS \"Value\" FROM pg_matviews WHERE schemaname = 'reporting'"));
        Assert.Equal(5, await Count("SELECT count(*)::int AS \"Value\" FROM pg_proc WHERE prosecdef AND proname IN ('resolve_api_key','resolve_session','claim_deliveries','refresh_all','siat_active_tenants')"));
        Assert.Equal(10, await db.Modules.CountAsync());
        Assert.True(await db.Modules.AnyAsync(m => m.Code == LicenseModuleCodes.FiscalSiat));
        // Las listas de las migraciones coinciden con el modelo (una tabla nueva por sucursal no puede quedar sin política)
        var model = db.Model.GetEntityTypes().Where(e => !e.IsOwned()).ToList();
        string Name(Microsoft.EntityFrameworkCore.Metadata.IEntityType e) => $"{e.GetSchema()}.{e.GetTableName()}";
        Assert.Equal(model.Where(e => typeof(IBranchScoped).IsAssignableFrom(e.ClrType)).Select(Name).Order(),
            Persistence.Migrations.V4MultiBranchCloud.BranchTables.Concat(Persistence.Migrations.V41SiatBilling.BranchTablesV41).Order());
        Assert.Equal(model.Where(e => typeof(IInterBranch).IsAssignableFrom(e.ClrType)).Select(Name).Order(),
            Persistence.Migrations.V4MultiBranchCloud.InterBranchTables.Order());
        Assert.Equal(model.Where(e => typeof(IAppendOnly).IsAssignableFrom(e.ClrType)).Select(Name).Order(),
            Persistence.Migrations.GuardsRlsAndViews.AppendOnlyTables.Concat(Persistence.Migrations.V4MultiBranchCloud.AppendOnlyTablesV4)
                .Concat(Persistence.Migrations.V41SiatBilling.AppendOnlyTablesV41).Order());
        // Cada tabla de las listas tiene de verdad su política y su trigger
        var branchPolicies = await db.Database.SqlQueryRaw<string>(
            "SELECT schemaname || '.' || tablename AS \"Value\" FROM pg_policies WHERE policyname = 'branch_isolation'").ToListAsync();
        Assert.All(Persistence.Migrations.V41SiatBilling.BranchTablesV41, t => Assert.Contains(t, branchPolicies));
        var appendOnly = await db.Database.SqlQueryRaw<string>(
            "SELECT n.nspname || '.' || c.relname AS \"Value\" FROM pg_trigger g JOIN pg_class c ON c.oid = g.tgrelid " +
            "JOIN pg_namespace n ON n.oid = c.relnamespace WHERE g.tgname = 'trg_append_only'").ToListAsync();
        Assert.All(Persistence.Migrations.V41SiatBilling.AppendOnlyTablesV41, t => Assert.Contains(t, appendOnly));
    }

    /// <summary>
    /// V4.1 · Facturación sobre PostgreSQL real con un rol sin privilegios (como minv_server): los documentos fiscales, sus
    /// líneas, los puntos de venta y los totales derivados solo se ven desde la sucursal que los emitió; los libros fiscales
    /// son append-only; la función SECURITY DEFINER del despachador no la puede ejecutar cualquiera.
    /// </summary>
    [PostgresFact]
    public async Task La_facturacion_aisla_las_sucursales_y_protege_sus_libros()
    {
        await using var provider = pg.Services();
        var (scope, tenant) = await NewTenantAsync(provider, "F" + Random.Shared.Next(1000, 9999));
        var role = "minv_fis_" + Guid.NewGuid().ToString("N")[..8];
        const string password = "Fis-Prueba-2026-x";
        Guid otherBranchId;
        using (scope)
        {
            // Una segunda sucursal y, en la principal, un punto de venta con CUIS, CUFD y una factura emitida en línea
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            var now = DateTimeOffset.UtcNow;
            var other = new Domain.Warehousing.Branch(tenant.TenantId, "SB", "Sucursal B", null);
            otherBranchId = other.Id;
            var settings = new SiatSettings(tenant.TenantId, 1234567019, "EMPRESA DE PRUEBA", "SIS-PRUEBA", SiatCodes.EnvironmentTest);
            settings.Enable();
            var pos = new SiatPointOfSale(tenant.TenantId, tenant.BranchId, SiatCodes.EnvironmentTest, 0, 0, "Sin punto de venta", null, null, now);
            var cuis = new SiatCuis(tenant.TenantId, tenant.BranchId, pos.Id, "C2FC6F36", now.AddDays(365), now);
            var cufd = new SiatCufd(tenant.TenantId, tenant.BranchId, pos.Id, cuis.Id, "BQUFDQ0FDREFBQkM=", "A19E23EF34124CD", "AV. PRUEBA 123",
                now.AddHours(24), now);
            var issuedAt = new DateTime(2026, 9, 25, 10, 30, 15, 123, DateTimeKind.Unspecified);
            var invoice = FiscalDocument.IssueInvoice(tenant.TenantId,
                new FiscalEmission(tenant.BranchId, SiatCodes.EnvironmentTest, settings.Nit, pos.Id, 0, 0, cuis.Id, cufd.Id, cufd.ControlCode,
                    SiatCodes.EmissionOnline, issuedAt, 1, "Ley N° 453: prueba", "ADMIN"),
                FiscalBuyer.Create(null, "CLI-1", SiatCodes.DocumentCi, "1234567", "1A", "JUAN PEREZ", null), null,
                [new FiscalLineInput(null, "4610000", 12345, "FER-001", "Martillo", 2m, 57, 10.50m, null),
                 new FiscalLineInput(null, "4610000", 12346, "FER-002", "Clavos", 1m, 57, 5m, 0.50m)],
                1, null, 1m, 0m, false, now);
            db.AddRange(other, settings, pos, cuis, cufd, invoice,
                new FiscalDocumentEvent(tenant.TenantId, tenant.BranchId, invoice.Id, FiscalDocumentAction.Issued, now, null, "Emitida", null, null,
                    tenant.AdminUserId));
            await db.SaveChangesAsync();

            // Libros fiscales append-only también en la base
            var update = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync("UPDATE billing.fiscal_document_lines SET quantity = 99"));
            Assert.Contains("append-only", update.MessageText);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM billing.siat_cufds"));
            // FK compuesta con la sucursal: un CUFD de otra sucursal no puede colgar de este punto de venta
            var foreign = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO billing.siat_cufds (id, tenant_id, branch_id, point_of_sale_id, cuis_id, code, control_code, address, valid_until, obtained_at) " +
                $"VALUES (gen_random_uuid(), '{tenant.TenantId}', '{other.Id}', '{pos.Id}', '{cuis.Id}', 'X', 'Y', 'Z', now() + interval '1 day', now())"));
            Assert.Equal("23503", foreign.SqlState);

            await using (var owner = new NpgsqlConnection(pg.ConnectionString))
            {
                await owner.OpenAsync();
                var sql = $"""
                    CREATE ROLE {role} LOGIN NOBYPASSRLS PASSWORD '{password}';
                    GRANT CONNECT ON DATABASE {pg.Database} TO {role};
                    GRANT USAGE ON SCHEMA iam, billing TO {role};
                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA billing TO {role};
                    GRANT EXECUTE ON FUNCTION iam.current_tenant_id(), iam.branch_visible(uuid) TO {role};
                    """;
                await using var cmd = new NpgsqlCommand(sql, owner);
                await cmd.ExecuteNonQueryAsync();
                // Como dueño (sin RLS) la función devuelve las empresas con la facturación activa
                await using var active = new NpgsqlCommand($"SELECT count(*) FROM billing.siat_active_tenants() t WHERE t = '{tenant.TenantId}'", owner);
                Assert.Equal(1L, Convert.ToInt64(await active.ExecuteScalarAsync()));
                // Nadie la hereda por PUBLIC; si existe minv_server, es el único rol de aplicación que la ejecuta
                await using var acl = new NpgsqlCommand(
                    "SELECT count(*) FROM pg_proc p WHERE p.proname = 'siat_active_tenants' " +
                    "AND (p.proacl IS NULL OR EXISTS (SELECT 1 FROM aclexplode(p.proacl) a WHERE a.grantee = 0))", owner);
                Assert.Equal(0L, Convert.ToInt64(await acl.ExecuteScalarAsync()));
                await using var config = new NpgsqlCommand(
                    "SELECT array_to_string(proconfig, ',') FROM pg_proc WHERE proname = 'siat_active_tenants' AND prosecdef", owner);
                Assert.Contains("search_path=pg_catalog", (string)(await config.ExecuteScalarAsync())!, StringComparison.Ordinal);
                await using var roles = new NpgsqlCommand(
                    "SELECT rolname, has_function_privilege(rolname, 'billing.siat_active_tenants()', 'EXECUTE') FROM pg_roles " +
                    "WHERE rolname IN ('minv_server', 'minv_app')", owner);
                await using var reader = await roles.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    Assert.Equal(reader.GetString(0) == "minv_server", reader.GetBoolean(1));
                }
            }
        }
        try
        {
            var limited = new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Username = role, Password = password }.ConnectionString;
            await using var conn = new NpgsqlConnection(limited);
            await conn.OpenAsync();
            async Task<long> Scalar(string text)
            {
                await using var c = new NpgsqlCommand(text, conn);
                return Convert.ToInt64(await c.ExecuteScalarAsync());
            }
            async Task Session(string branches)
            {
                await using var c = new NpgsqlCommand(
                    $"SELECT set_config('minv.tenant_id', '{tenant.TenantId}', false), set_config('minv.branch_ids', '{branches}', false)", conn);
                await c.ExecuteNonQueryAsync();
            }
            // Sin empresa en la sesión: nada visible
            Assert.Equal(0, await Scalar("SELECT count(*) FROM billing.fiscal_documents"));
            await Session("*");
            Assert.Equal(1, await Scalar("SELECT count(*) FROM billing.fiscal_documents"));
            Assert.Equal(2, await Scalar("SELECT count(*) FROM billing.fiscal_document_lines"));
            Assert.Equal(1, await Scalar("SELECT count(*) FROM billing.siat_points_of_sale"));
            // Desde otra sucursal: ni documentos, ni líneas, ni puntos de venta, ni totales
            await Session(otherBranchId.ToString());
            Assert.Equal(0, await Scalar("SELECT count(*) FROM billing.fiscal_documents"));
            Assert.Equal(0, await Scalar("SELECT count(*) FROM billing.fiscal_document_lines"));
            Assert.Equal(0, await Scalar("SELECT count(*) FROM billing.fiscal_document_events"));
            Assert.Equal(0, await Scalar("SELECT count(*) FROM billing.siat_points_of_sale"));
            Assert.Equal(0, await Scalar("SELECT count(*) FROM billing.siat_cufds"));
            Assert.Equal(0, await Scalar("SELECT count(*) FROM billing.v_fiscal_document_totals"));
            Assert.Equal(1, await Scalar("SELECT count(*) FROM billing.siat_settings"));   // la configuración es de la empresa
            // Escribir un punto de venta en la sucursal ajena lo rechaza la política restrictiva (WITH CHECK)
            var error = await Assert.ThrowsAsync<PostgresException>(async () => await new NpgsqlCommand(
                "INSERT INTO billing.siat_points_of_sale (id, tenant_id, branch_id, environment, code, type_code, name, pos_register_id, mode, " +
                "mode_since, consecutive_failures) " +
                $"VALUES (gen_random_uuid(), '{tenant.TenantId}', '{tenant.BranchId}', 2, 1, 5, 'PV ajeno', NULL, 'Online', now(), 0)",
                conn).ExecuteNonQueryAsync());
            Assert.Equal("42501", error.SqlState);
            // Desde la sucursal emisora: los totales derivados de la vista coinciden con el dominio
            await Session(tenant.BranchId.ToString());
            await using (var totals = new NpgsqlCommand(
                "SELECT lines_subtotal, total_amount, total_subject_to_vat, vat_amount FROM billing.v_fiscal_document_totals", conn))
            await using (var reader = await totals.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(25.50m, reader.GetDecimal(0));   // 2 × 10,50 + (1 × 5 − 0,50)
                Assert.Equal(24.50m, reader.GetDecimal(1));   // − descuento adicional 1
                Assert.Equal(24.50m, reader.GetDecimal(2));
                Assert.Equal(3.19m, reader.GetDecimal(3));    // round2(24,50 × 0,13) = 3,185 → 3,19 (HALF-UP)
            }
            // La función del despachador no la ejecuta un rol cualquiera
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await new NpgsqlCommand("SELECT count(*) FROM billing.siat_active_tenants()", conn).ExecuteScalarAsync());
            Assert.Equal("42501", denied.SqlState);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var owner = new NpgsqlConnection(pg.ConnectionString);
            await owner.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE IF EXISTS {role};", owner);
            await cmd.ExecuteNonQueryAsync();
        }
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
            var db = sp.GetRequiredService<MinvWriteDbContext>();
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
            var db = sp.GetRequiredService<MinvWriteDbContext>();
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

            (MinvWriteDbContext, StockLevel, MovementType) Session(IServiceProvider sp)
            {
                sp.GetRequiredService<ITenantContext>().Set(tenant.TenantId);
                var db = sp.GetRequiredService<MinvWriteDbContext>();
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
        var db = sp.GetRequiredService<MinvWriteDbContext>();
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
        var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
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
        await m.Send(new LoginCommand("SEMILLA", admin.Email, admin.Password, "pruebas", "4.0.0"));
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

        // V4 · Sucursales, transferencias, integraciones y modelo de lectura sobre PostgreSQL real
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM inventory.v_transfer_breaches").SingleAsync());
        Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT reporting.refresh_all() AS \"Value\"").SingleAsync());
        Assert.Equal(3, (await m.Send(new MINV.Application.Corporate.GetBranchesQuery())).Count);
        var consolidated = await m.Send(new MINV.Application.Corporate.ConsolidatedStockQuery());
        Assert.True(consolidated.InTransitValue > 0);
        var report = await m.Send(new MINV.Application.Corporate.GetBranchReportQuery(from, to));
        Assert.Equal(3, report.Branches.Count);
        Assert.True(report.TotalRevenue > 0 && report.RefreshedAt is not null);
        var transfers = await m.Send(new MINV.Application.Inventory.Transfers.GetTransfersQuery());
        Assert.NotNull(await m.Send(new MINV.Application.Inventory.Transfers.GetTransferQuery(transfers[0].Id)));
        Assert.NotEmpty(await m.Send(new MINV.Application.Integration.GetApiKeysQuery()));
        Assert.NotNull(await m.Send(new MINV.Application.Integration.GetWebhooksQuery()));
        Assert.NotNull(await m.Send(new MINV.Application.Integration.GetWebhookDeliveriesQuery()));
        Assert.Equal(61, (await m.Send(new MINV.Application.Integration.GetApiCatalogQuery(1, 500))).Total);
        Assert.NotEmpty((await m.Send(new MINV.Application.Integration.GetApiStockQuery())).Items);
    }

    /// <summary>
    /// V4 · Con un rol que NO es dueño ni tiene BYPASSRLS (como minv_server en la nube), la base misma aísla empresas y
    /// sucursales aunque el código olvidara un filtro: otra sucursal ve cero filas y no puede escribir en una ajena. Las
    /// funciones SECURITY DEFINER son la única vía para resolver una API Key antes de conocer la empresa.
    /// </summary>
    [PostgresFact]
    public async Task Con_un_rol_sin_privilegios_la_base_aisla_las_sucursales()
    {
        var role = "minv_rls_" + Guid.NewGuid().ToString("N")[..8];
        const string password = "Rls-Prueba-2026-x";
        var services = new ServiceCollection();
        services.AddSingleton<MINV.Infrastructure.Services.DemoClock>();
        services.AddSingleton<IClock>(sp => sp.GetRequiredService<MINV.Infrastructure.Services.DemoClock>());
        services.AddMinvApplication();
        services.AddMinvInfrastructure(pg.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        var seed = await provider.GetRequiredService<MINV.Infrastructure.Seeding.LocalDataSeeder>()
            .SeedAsync(new MINV.Infrastructure.Seeding.SeedOptions("RLS", Days: 3, Seed: 5), _ => { });
        await using (var owner = new NpgsqlConnection(pg.ConnectionString))
        {
            await owner.OpenAsync();
            var sql = $"""
                CREATE ROLE {role} LOGIN NOBYPASSRLS PASSWORD '{password}';
                GRANT CONNECT ON DATABASE {pg.Database} TO {role};
                GRANT USAGE ON SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting, integration, reporting TO {role};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting, integration TO {role};
                GRANT SELECT ON reporting.v_branch_stock, reporting.v_branch_daily_sales TO {role};
                GRANT EXECUTE ON FUNCTION integration.resolve_api_key(text), iam.resolve_session(text), integration.claim_deliveries(integer, integer) TO {role};
                """;
            await using var cmd = new NpgsqlCommand(sql, owner);
            await cmd.ExecuteNonQueryAsync();
        }
        try
        {
            var limited = new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Username = role, Password = password }.ConnectionString;
            await using var conn = new NpgsqlConnection(limited);
            await conn.OpenAsync();
            async Task<long> Scalar(string text)
            {
                await using var c = new NpgsqlCommand(text, conn);
                return Convert.ToInt64(await c.ExecuteScalarAsync());
            }
            var tenant = (Guid)(await new NpgsqlCommand("SELECT (SELECT id FROM iam.tenants WHERE code = 'RLS')", conn).ExecuteScalarAsync())!;
            // Sin empresa en la sesión: nada visible
            Assert.Equal(0, await Scalar("SELECT count(*) FROM inventory.stock_movements"));
            await new NpgsqlCommand($"SELECT set_config('minv.tenant_id', '{tenant}', false), set_config('minv.branch_ids', '*', false)", conn).ExecuteNonQueryAsync();
            var all = await Scalar("SELECT count(*) FROM inventory.stock_movements");
            var ea = (Guid)(await new NpgsqlCommand("SELECT id FROM warehouse.branches WHERE code = 'EA'", conn).ExecuteScalarAsync())!;
            await new NpgsqlCommand($"SELECT set_config('minv.branch_ids', '{ea}', false)", conn).ExecuteNonQueryAsync();
            var onlyEa = await Scalar("SELECT count(*) FROM inventory.stock_movements");
            Assert.True(all > onlyEa && onlyEa > 0, $"todas {all}, El Alto {onlyEa}");
            Assert.Equal(0, await Scalar($"SELECT count(*) FROM inventory.stock_movements WHERE branch_id <> '{ea}'"));
            Assert.Equal(3, await Scalar("SELECT count(*) FROM warehouse.branches"));   // el directorio es de la empresa
            Assert.True(await Scalar("SELECT count(*) FROM inventory.stock_transfers") > 0);   // las que salen o llegan a El Alto
            // Escribir en otra sucursal lo rechaza la política restrictiva (WITH CHECK)
            var cm = (Guid)(await new NpgsqlCommand($"SELECT branch_id FROM warehouse.warehouses WHERE code = 'ALM01'", conn).ExecuteScalarAsync())!;
            var error = await Assert.ThrowsAsync<PostgresException>(async () => await new NpgsqlCommand(
                $"UPDATE sales.pos_registers SET name = name WHERE branch_id = '{ea}'; INSERT INTO warehouse.zones (id, tenant_id, branch_id, warehouse_id, code, name) " +
                $"SELECT gen_random_uuid(), '{tenant}', '{cm}', id, 'ZX', 'Zona ajena' FROM warehouse.warehouses WHERE code = 'ALM01'", conn).ExecuteNonQueryAsync());
            Assert.Equal("42501", error.SqlState);
            // La API Key solo se puede resolver con la función SECURITY DEFINER (antes de conocer la empresa)
            await new NpgsqlCommand("SELECT set_config('minv.tenant_id', '', false)", conn).ExecuteNonQueryAsync();
            Assert.Equal(0, await Scalar("SELECT count(*) FROM integration.api_keys"));
            var prefix = MINV.Application.Integration.ApiKeyTokens.PrefixOf(seed.ApiKeyToken)!;
            Assert.Equal(1, await Scalar($"SELECT count(*) FROM integration.resolve_api_key('{prefix}')"));

            // El arranque del servidor acepta este rol y el servidor completo funciona con él (login, alcance, consultas)
            var server = new ServiceCollection();
            server.AddMinvApplication();
            server.AddMinvInfrastructure(limited);
            await using var sp = server.BuildServiceProvider();
            await MINV.Infrastructure.Hosting.ServerHosting.VerifyDatabaseAsync(sp);
            using var scope = sp.CreateScope();
            var principal = await scope.ServiceProvider.GetRequiredService<MINV.Infrastructure.Integration.ApiKeyAuthenticator>()
                .AuthenticateAsync(seed.ApiKeyToken, default);
            Assert.NotNull(principal);
            var stock = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new MINV.Application.Integration.GetApiStockQuery(PageSize: 500));
            Assert.All(stock.Items, i => Assert.Equal("CM", i.BranchCode));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var owner = new NpgsqlConnection(pg.ConnectionString);
            await owner.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE IF EXISTS {role};", owner);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>V4 · El arranque del servidor rechaza un rol que puede saltarse la seguridad por filas (el dueño).</summary>
    [PostgresFact]
    public async Task El_servidor_no_arranca_con_un_rol_que_salta_la_seguridad_por_filas()
    {
        await using var provider = pg.Services();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => MINV.Infrastructure.Hosting.ServerHosting.VerifyDatabaseAsync(provider));
        Assert.Contains("seguridad por filas", error.Message, StringComparison.Ordinal);
    }
}
