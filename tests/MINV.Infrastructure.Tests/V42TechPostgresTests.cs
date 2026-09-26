using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Iam;
using MINV.Domain.Catalog;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Service;
using MINV.Domain.Warehousing;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Provisioning;
using Npgsql;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// V4.2 · Edición Tecnología contra un PostgreSQL real (<c>MINV_TEST_PG</c>): RLS por sucursal y bitácoras append-only en
/// las tablas nuevas, arco y tipo de los valores de las fichas técnicas, unicidad y ubicación de las series, vista de
/// control <c>inventory.v_serial_breaches</c> y relleno de las series existentes al migrar desde la V4.1.
/// </summary>
public sealed class V42TechPostgresTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private const string AdminEmail = "admin@tecno.example";
    private const string AdminPassword = "Clave-Tecno-2026";
    private static readonly UnitRule Und = new("UND", false);

    private static async Task<(IServiceScope Scope, ProvisionedTenant Tenant)> NewTenantAsync(ServiceProvider provider, string code)
    {
        var scope = provider.CreateScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().ProvisionAsync(
            new ProvisionTenantRequest(code, "Tecno " + code, null, AdminEmail, "Administrador", AdminPassword));
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand(code, AdminEmail, AdminPassword, "pruebas", "4.2.0"));
        return (scope, tenant);
    }

    /// <summary>Producto serializado con su saldo inicial y una serie por unidad, en la posición general de la casa matriz.</summary>
    private static async Task<(Product Product, Batch Batch, StockLevel Level, List<SerialNumber> Serials)> SerializedStockAsync(
        MinvWriteDbContext db, ProvisionedTenant tenant, string sku, params string[] serials)
    {
        var now = DateTimeOffset.UtcNow;
        var category = new Category(tenant.TenantId, "C" + sku.Replace("-", string.Empty, StringComparison.Ordinal), "Categoría " + sku);
        var product = Product.Create(tenant.TenantId, sku, "Producto " + sku, category.Id, tenant.Units["UND"], TrackingMode.Serial);
        var batch = Batch.CreateDefault(tenant.TenantId, product.DefaultVariant.Id);
        var level = StockLevel.Open(tenant.TenantId, tenant.BranchId, tenant.DefaultBinId, batch.Id);
        db.AddRange(category, product, batch, level);
        var initial = await db.MovementTypes.SingleAsync(t => t.Code == MovementTypeCodes.InitialBalance);
        db.Add(level.Register(initial, serials.Length, Und, new MovementContext(tenant.AdminUserId, DateOnly.FromDateTime(now.UtcDateTime), now,
            "INV-INICIAL")));
        var context = new SerialContext(tenant.BranchId, tenant.AdminUserId, now, "INV-INICIAL");
        var units = serials.Select(s => SerialNumber.Receive(SerialKind.Serial, s, batch, level, context)).ToList();
        db.AddRange(units);
        await db.SaveChangesAsync();
        return (product, batch, level, units);
    }

    private static async Task<int> CountAsync(MinvWriteDbContext db, string sql) =>
        await db.Database.SqlQueryRaw<int>(sql).SingleAsync();

    /// <summary>Casos RMA, armados y su bitácora solo se ven desde su sucursal con un rol sin privilegios (como minv_server);
    /// la serie y su bitácora son de la empresa; las bitácoras nuevas son append-only.</summary>
    [PostgresFact]
    public async Task V42_las_tablas_nuevas_aislan_sucursales_y_sus_bitacoras_son_append_only()
    {
        await using var provider = pg.Services();
        var (scope, tenant) = await NewTenantAsync(provider, "TZ" + Random.Shared.Next(100, 999));
        var role = "minv_v42_" + Guid.NewGuid().ToString("N")[..8];
        const string password = "V42-Prueba-2026-x";
        Guid otherBranchId;
        using (scope)
        {
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            var now = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var (product, _, level, serials) = await SerializedStockAsync(db, tenant, "GPU-4060", "GPU4060-0001", "GPU4060-0002");
            var customer = await db.Customers.SingleAsync(c => c.Code == "CF");
            var other = new Branch(tenant.TenantId, "SB", "Sucursal B", null);
            otherBranchId = other.Id;
            db.Add(other);
            // Venta de una unidad (movimiento + serie), caso RMA abierto y en diagnóstico, y un armado en cada sucursal
            var sale = await db.MovementTypes.SingleAsync(t => t.Code == MovementTypeCodes.Sale);
            db.Add(level.Register(sale, 1, Und, new MovementContext(tenant.AdminUserId, today, now, "F-CM-000001")));
            var unit = serials[0];
            unit.Sell(level, new SerialContext(tenant.BranchId, tenant.AdminUserId, now, "F-CM-000001"));
            unit.SendToRma(new SerialContext(tenant.BranchId, tenant.AdminUserId, now, "RMA-CM-000001"));
            var claim = new WarrantyClaim(tenant.TenantId, tenant.BranchId, "RMA-CM-000001", unit.Id, customer.Id, null, "No da video al encender",
                true, null, tenant.AdminUserId, now);
            claim.MoveTo(WarrantyClaimStatus.Diagnosing, null, null, tenant.AdminUserId, now);
            var build = new PcBuild(tenant.TenantId, tenant.BranchId, "ARM-CM-000001", "PC Gamer", customer.Id, today.AddDays(7), tenant.AdminUserId, now);
            build.AddLine(PcSlot.Gpu, product.DefaultVariant.Id, 1, 3199m);
            var remote = new PcBuild(tenant.TenantId, other.Id, "ARM-SB-000001", "PC Oficina", null, today.AddDays(7), tenant.AdminUserId, now);
            remote.AddLine(PcSlot.Gpu, product.DefaultVariant.Id, 1, 3199m);
            db.AddRange(claim, build, remote);
            await db.SaveChangesAsync();
            Assert.Equal(0, await CountAsync(db, "SELECT count(*)::int AS \"Value\" FROM inventory.v_serial_breaches WHERE tenant_id = " + $"'{tenant.TenantId}'"));
            Assert.Equal(3, await CountAsync(db, "SELECT count(*)::int AS \"Value\" FROM inventory.serial_events WHERE serial_number_id IN " +
                                                 $"(SELECT id FROM inventory.serial_numbers WHERE serial = 'GPU4060-0001' AND tenant_id = '{tenant.TenantId}')"));

            // Bitácoras append-only también en la base
            var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE inventory.serial_events SET note = 'x'"));
            Assert.Contains("append-only", update.MessageText, StringComparison.Ordinal);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM service.warranty_claim_events"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE sales.sales_order_line_serials"));
            // FK compuesta con la sucursal: una línea no puede colgar de un armado de otra sucursal
            var foreign = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO sales.pc_build_lines (id, tenant_id, branch_id, pc_build_id, slot, variant_id, quantity, quoted_unit_price) " +
                $"VALUES (gen_random_uuid(), '{tenant.TenantId}', '{other.Id}', '{build.Id}', 'Cpu', '{product.DefaultVariant.Id}', 1, 10)"));
            Assert.Equal("23503", foreign.SqlState);
            // Un solo caso abierto por serie
            var twice = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO service.warranty_claims (id, tenant_id, branch_id, number, serial_number_id, customer_id, issue, is_in_warranty, status, " +
                $"opened_by_user_id, received_at) VALUES (gen_random_uuid(), '{tenant.TenantId}', '{tenant.BranchId}', 'RMA-CM-000002', '{unit.Id}', " +
                $"'{customer.Id}', 'Otra falla reportada', true, 'Received', '{tenant.AdminUserId}', now())"));
            Assert.Equal("23505", twice.SqlState);

            await using var owner = new NpgsqlConnection(pg.ConnectionString);
            await owner.OpenAsync();
            await using var cmd = new NpgsqlCommand($"""
                CREATE ROLE {role} LOGIN NOBYPASSRLS PASSWORD '{password}';
                GRANT CONNECT ON DATABASE {pg.Database} TO {role};
                GRANT USAGE ON SCHEMA iam, catalog, inventory, sales, service TO {role};
                GRANT SELECT, INSERT, UPDATE, DELETE ON {string.Join(", ", Persistence.Migrations.V42TechRetail.NewTablesV42)} TO {role};
                GRANT SELECT ON inventory.serial_numbers, inventory.stock_levels, inventory.batches, catalog.product_variants, catalog.products,
                    inventory.v_serial_breaches TO {role};
                GRANT EXECUTE ON FUNCTION iam.current_tenant_id(), iam.branch_visible(uuid) TO {role};
                """, owner);
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
            async Task Session(string branches)
            {
                await using var c = new NpgsqlCommand(
                    $"SELECT set_config('minv.tenant_id', '{tenant.TenantId}', false), set_config('minv.branch_ids', '{branches}', false)", conn);
                await c.ExecuteNonQueryAsync();
            }
            Assert.Equal(0, await Scalar("SELECT count(*) FROM service.warranty_claims"));   // sin empresa en la sesión
            await Session("*");
            Assert.Equal(2, await Scalar("SELECT count(*) FROM sales.pc_builds"));
            Assert.Equal(2, await Scalar("SELECT count(*) FROM sales.pc_build_lines"));
            Assert.Equal(1, await Scalar("SELECT count(*) FROM service.warranty_claims"));
            Assert.Equal(2, await Scalar("SELECT count(*) FROM service.warranty_claim_events"));
            // Desde la otra sucursal: su armado sí; ni el caso RMA ni su bitácora ni el armado de la casa matriz
            await Session(otherBranchId.ToString());
            Assert.Equal(1, await Scalar("SELECT count(*) FROM sales.pc_builds"));
            Assert.Equal(1, await Scalar("SELECT count(*) FROM sales.pc_build_lines"));
            Assert.Equal(0, await Scalar("SELECT count(*) FROM service.warranty_claims"));
            Assert.Equal(0, await Scalar("SELECT count(*) FROM service.warranty_claim_events"));
            // La serie y su bitácora son de la empresa (la serie viaja entre sucursales)
            Assert.Equal(2, await Scalar("SELECT count(*) FROM inventory.serial_numbers"));
            Assert.Equal(4, await Scalar("SELECT count(*) FROM inventory.serial_events"));
            // Escribir un armado en la casa matriz desde la otra sucursal lo rechaza la política restrictiva (WITH CHECK)
            var error = await Assert.ThrowsAsync<PostgresException>(async () => await new NpgsqlCommand(
                "INSERT INTO sales.pc_builds (id, tenant_id, branch_id, number, name, valid_until, status, quoted_with_errors, created_by_user_id) " +
                $"VALUES (gen_random_uuid(), '{tenant.TenantId}', '{tenant.BranchId}', 'ARM-CM-000009', 'Ajeno', current_date, 'Draft', false, " +
                $"'{tenant.AdminUserId}')", conn).ExecuteNonQueryAsync());
            Assert.Equal("42501", error.SqlState);
            // Las bitácoras nuevas tampoco admiten UPDATE para un rol de aplicación: el trigger las protege
            await Session("*");
            var ledger = await Assert.ThrowsAsync<PostgresException>(async () =>
                await new NpgsqlCommand("UPDATE service.warranty_claim_events SET note = 'x'", conn).ExecuteNonQueryAsync());
            Assert.Contains("append-only", ledger.MessageText, StringComparison.Ordinal);
            Assert.Equal(0, await Scalar("SELECT count(*) FROM inventory.v_serial_breaches"));
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

    /// <summary>Fichas técnicas (T-01): arco exclusivo número XOR texto XOR opción, la columna del tipo de la especificación,
    /// la opción de ESA especificación (FK compuesta) y un solo valor si la especificación no es multivalor.</summary>
    [PostgresFact]
    public async Task V42_los_valores_de_la_ficha_tecnica_respetan_el_arco_y_el_tipo_de_su_especificacion()
    {
        await using var provider = pg.Services();
        var (scope, tenant) = await NewTenantAsync(provider, "TS" + Random.Shared.Next(100, 999));
        using (scope)
        {
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            var category = new Category(tenant.TenantId, "CASE", "Gabinetes");
            var product = Product.Create(tenant.TenantId, "CASE-001", "Gabinete ATX", category.Id, tenant.Units["UND"]);
            var length = new SpecDefinition(tenant.TenantId, category.Id, "largo_max_gpu", "Largo máximo de GPU", "mm", SpecDataType.Number, false, true,
                true, CompatibilityKeys.CaseMaxGpuMm, 1);
            var color = new SpecDefinition(tenant.TenantId, category.Id, "color", "Color", null, SpecDataType.Text, false, false, false, null, 2);
            var formats = new SpecDefinition(tenant.TenantId, category.Id, "formatos_placa", "Formatos de placa", null, SpecDataType.Option, true, true,
                true, CompatibilityKeys.CaseFormFactors, 3);
            var condition = new SpecDefinition(tenant.TenantId, category.Id, "condicion", "Condición", null, SpecDataType.Option, false, true, true,
                null, 4);
            var atx = new SpecOption(tenant.TenantId, formats.Id, "ATX", 1);
            var matx = new SpecOption(tenant.TenantId, formats.Id, "Micro-ATX", 2);
            var nuevo = new SpecOption(tenant.TenantId, condition.Id, "Nuevo", 1);
            var usado = new SpecOption(tenant.TenantId, condition.Id, "Usado", 2);
            db.AddRange(category, product, length, color, formats, condition, atx, matx, nuevo, usado);
            db.AddRange(ProductSpecValue.Number(tenant.TenantId, product.Id, length, 330m), ProductSpecValue.Text(tenant.TenantId, product.Id, color, "Negro"),
                ProductSpecValue.Option(tenant.TenantId, product.Id, formats, atx), ProductSpecValue.Option(tenant.TenantId, product.Id, formats, matx),
                ProductSpecValue.Option(tenant.TenantId, product.Id, condition, nuevo),
                new ProductTechProfile(tenant.TenantId, product.Id, SerialKind.Serial, 12));
            var empty = Product.Create(tenant.TenantId, "CASE-002", "Gabinete sin ficha", category.Id, tenant.Units["UND"]);
            db.Add(empty);
            await db.SaveChangesAsync();
            Assert.Equal(5, await CountAsync(db, "SELECT count(*)::int AS \"Value\" FROM catalog.product_spec_values WHERE tenant_id = " +
                                                 $"'{tenant.TenantId}'"));

            async Task<PostgresException> Insert(Product target, Guid spec, string number, string text, string option) =>
                await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO catalog.product_spec_values (id, tenant_id, product_id, spec_definition_id, number_value, text_value, option_id) " +
                    $"VALUES (gen_random_uuid(), '{tenant.TenantId}', '{target.Id}', '{spec}', {number}, {text}, {option})"));
            // Arco exclusivo: ni dos columnas ni ninguna
            Assert.Equal("ck_product_spec_values_arco", (await Insert(empty, length.Id, "300", "'300'", "NULL")).ConstraintName);
            Assert.Equal("ck_product_spec_values_arco", (await Insert(empty, length.Id, "NULL", "NULL", "NULL")).ConstraintName);
            Assert.Equal("ck_product_spec_values_arco", (await Insert(empty, color.Id, "NULL", "'Rojo'", $"'{nuevo.Id}'")).ConstraintName);
            // La columna del tipo de la especificación: un número no se guarda como texto ni un texto como número (trigger)
            Assert.Equal("P0001", (await Insert(empty, length.Id, "NULL", "'330'", "NULL")).SqlState);
            Assert.Equal("P0001", (await Insert(empty, color.Id, "5", "NULL", "NULL")).SqlState);
            Assert.Equal("P0001", (await Insert(empty, condition.Id, "NULL", "'Nuevo'", "NULL")).SqlState);
            // La opción debe ser de ESA especificación (FK compuesta)
            Assert.Equal("23503", (await Insert(empty, formats.Id, "NULL", "NULL", $"'{nuevo.Id}'")).SqlState);
            // Un solo valor en una especificación de un valor (trigger); en una multivalor, cada opción una vez (índice único)
            Assert.Equal("P0001", (await Insert(product, length.Id, "320", "NULL", "NULL")).SqlState);
            Assert.Equal("P0001", (await Insert(product, condition.Id, "NULL", "NULL", $"'{usado.Id}'")).SqlState);
            Assert.Equal("23505", (await Insert(product, formats.Id, "NULL", "NULL", $"'{atx.Id}'")).SqlState);
            // Texto vacío y garantía fuera de rango
            Assert.Equal("ck_product_spec_values_texto", (await Insert(empty, color.Id, "NULL", "'   '", "NULL")).ConstraintName);
            var warranty = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync("UPDATE catalog.product_tech_profiles SET warranty_months = 121 WHERE product_id = " + $"'{product.Id}'"));
            Assert.Equal("23514", warranty.SqlState);
        }
    }

    /// <summary>Series (T-02): única por (empresa, variante, serie), la misma serie en otro producto se admite; en stock ⇔ con
    /// existencia; la existencia es de su lote y el lote de su variante; un IMEI son 15 dígitos; sin separadores.</summary>
    [PostgresFact]
    public async Task V42_la_serie_es_unica_por_variante_y_la_base_valida_su_ubicacion()
    {
        await using var provider = pg.Services();
        var (scope, tenant) = await NewTenantAsync(provider, "TU" + Random.Shared.Next(100, 999));
        using (scope)
        {
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            var (product, batch, level, _) = await SerializedStockAsync(db, tenant, "SSD-990", "S6B0NX0001");
            var (other, otherBatch, otherLevel, _) = await SerializedStockAsync(db, tenant, "SSD-870", "S6B0NX0001");   // misma serie, otro producto
            Assert.Equal(2, await CountAsync(db, "SELECT count(*)::int AS \"Value\" FROM inventory.serial_numbers WHERE serial = 'S6B0NX0001' AND tenant_id = " + $"'{tenant.TenantId}'"));

            async Task<PostgresException> Insert(Guid variant, Guid batchId, string kind, string serial, string status, string stockLevel) =>
                await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO inventory.serial_numbers (id, tenant_id, variant_id, batch_id, kind, serial, stock_level_id, status, received_at) " +
                    $"VALUES (gen_random_uuid(), '{tenant.TenantId}', '{variant}', '{batchId}', '{kind}', '{serial}', {stockLevel}, '{status}', now())"));
            var variantId = product.DefaultVariant.Id;
            Assert.Equal("23505", (await Insert(variantId, batch.Id, "Serial", "S6B0NX0001", "InStock", $"'{level.Id}'")).SqlState);
            Assert.Equal("ck_serial_numbers_ubicacion", (await Insert(variantId, batch.Id, "Serial", "X-1", "InStock", "NULL")).ConstraintName);
            Assert.Equal("ck_serial_numbers_ubicacion", (await Insert(variantId, batch.Id, "Serial", "X-2", "Sold", $"'{level.Id}'")).ConstraintName);
            Assert.Equal("23503", (await Insert(variantId, otherBatch.Id, "Serial", "X-3", "Sold", "NULL")).SqlState);          // lote de otra variante
            Assert.Equal("23503", (await Insert(variantId, batch.Id, "Serial", "X-4", "InStock", $"'{otherLevel.Id}'")).SqlState); // existencia de otro lote
            Assert.Equal("ck_serial_numbers_imei", (await Insert(variantId, batch.Id, "Imei", "35209900176148", "Sold", "NULL")).ConstraintName);
            Assert.Equal("ck_serial_numbers_serie", (await Insert(variantId, batch.Id, "Serial", "A 1", "Sold", "NULL")).ConstraintName);
            Assert.Equal("ck_serial_numbers_estado", (await Insert(variantId, batch.Id, "Serial", "X-5", "Lost", "NULL")).ConstraintName);
            // Por EF Core, la serie repetida es un conflicto de unicidad (la caja reintenta o informa)
            db.Add(SerialNumber.Receive(SerialKind.Serial, "s6b0nx0001", batch, level,
                new SerialContext(tenant.BranchId, tenant.AdminUserId, DateTimeOffset.UtcNow)));
            await Assert.ThrowsAsync<MINV.Application.Common.ConcurrencyConflictException>(() => db.SaveChangesAsync());
            Assert.NotEqual(Guid.Empty, other.Id);
        }
    }

    /// <summary><c>inventory.v_serial_breaches</c> está vacía en un escenario coherente (ingreso, venta, transferencia a otra
    /// posición) y detecta una serie en stock sin su unidad en el kardex.</summary>
    [PostgresFact]
    public async Task V42_v_serial_breaches_esta_vacia_en_un_escenario_coherente()
    {
        await using var provider = pg.Services();
        var (scope, tenant) = await NewTenantAsync(provider, "TV" + Random.Shared.Next(100, 999));
        using (scope)
        {
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            var now = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var (_, batch, level, serials) = await SerializedStockAsync(db, tenant, "PS5-SLIM", "PS5-0001", "PS5-0002", "PS5-0003");
            await SerializedStockAsync(db, tenant, "XSX-1TB", "XSX-0001");
            Assert.Equal(0, await CountAsync(db, "SELECT count(*)::int AS \"Value\" FROM inventory.v_serial_breaches WHERE tenant_id = " + $"'{tenant.TenantId}'"));
            // Un producto sin serie no entra en la vista aunque tenga stock
            var plain = await db.Categories.FirstAsync();
            var cable = Product.Create(tenant.TenantId, "CAB-HDMI", "Cable HDMI", plain.Id, tenant.Units["UND"]);
            var cableBatch = Batch.CreateDefault(tenant.TenantId, cable.DefaultVariant.Id);
            var cableLevel = StockLevel.Open(tenant.TenantId, tenant.BranchId, tenant.DefaultBinId, cableBatch.Id);
            var initial = await db.MovementTypes.SingleAsync(t => t.Code == MovementTypeCodes.InitialBalance);
            db.AddRange(cable, cableBatch, cableLevel);
            db.Add(cableLevel.Register(initial, 10, Und, new MovementContext(tenant.AdminUserId, today, now)));
            // Venta de una consola: movimiento y serie en la misma transacción
            var sale = await db.MovementTypes.SingleAsync(t => t.Code == MovementTypeCodes.Sale);
            db.Add(level.Register(sale, 1, Und, new MovementContext(tenant.AdminUserId, today, now, "F-CM-000001")));
            serials[0].Sell(level, new SerialContext(tenant.BranchId, tenant.AdminUserId, now, "F-CM-000001"));
            await db.SaveChangesAsync();
            Assert.Equal(0, await CountAsync(db, "SELECT count(*)::int AS \"Value\" FROM inventory.v_serial_breaches WHERE tenant_id = " + $"'{tenant.TenantId}'"));
            Assert.Equal(0, await CountAsync(db, "SELECT count(*)::int AS \"Value\" FROM inventory.v_conservation_breaches WHERE tenant_id = " +
                                                 $"'{tenant.TenantId}'"));

            // Una serie en stock sin su unidad en el kardex: la vista la muestra (serie de más en la sucursal y variante)
            db.Add(SerialNumber.Receive(SerialKind.Serial, "PS5-0099", batch, level, new SerialContext(tenant.BranchId, tenant.AdminUserId, now)));
            await db.SaveChangesAsync();
            var breach = await db.Database.SqlQueryRaw<Breach>(
                "SELECT sku AS \"Sku\", stock AS \"Stock\", serials_in_stock AS \"Serials\", difference AS \"Difference\" " +
                "FROM inventory.v_serial_breaches WHERE tenant_id = " + $"'{tenant.TenantId}'").ToListAsync();
            var row = Assert.Single(breach);
            Assert.Equal(("PS5-SLIM", 2m, 3L, 1m), (row.Sku, row.Stock, row.Serials, row.Difference));
        }
    }

    private sealed record Breach(string Sku, decimal Stock, long Serials, decimal Difference);

    /// <summary>
    /// Relleno de la migración (regla B-15): una base en la V4.1 con series (unicidad por lote, sin variante) migra a la
    /// V4.2 con la variante tomada de su lote, tipo Serial y fecha de ingreso = alta de la fila; y la guardia detiene la
    /// migración (sin cambios) si una serie se repite en dos lotes de la misma variante.
    /// </summary>
    [PostgresFact]
    public async Task V42_la_migracion_rellena_las_series_existentes_y_su_guardia_las_protege()
    {
        foreach (var duplicated in new[] { false, true })
        {
            var database = "minv_v42mig_" + Guid.NewGuid().ToString("N")[..10];
            var admin = Environment.GetEnvironmentVariable(PostgresFixture.Variable)!;
            await using (var conn = new NpgsqlConnection(admin))
            {
                await conn.OpenAsync();
                await new NpgsqlCommand($"CREATE DATABASE {database}", conn).ExecuteNonQueryAsync();
            }
            var connection = new NpgsqlConnectionStringBuilder(admin) { Database = database, IncludeErrorDetail = true }.ConnectionString;
            try
            {
                var services = new ServiceCollection();
                services.AddMinvApplication();
                services.AddMinvInfrastructure(connection);
                await using var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync("20260926033017_V41CafcNumbering");

                // Empresa de la V4.1: el aprovisionamiento actual ya siembra lo de la V4.2, así que se quita para que lo agregue la
                // migración. Producto con dos lotes y una existencia (tablas que la V4.2 no cambia) y series con las columnas de la V4.1.
                var tenant = await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().ProvisionAsync(
                    new ProvisionTenantRequest("LEG", "Legado", null, AdminEmail, "Administrador", AdminPassword));
                var tenantId = tenant.TenantId;
                var codes = string.Join(", ", Persistence.Migrations.V42TechRetail.TechPermissions.Select(p => $"'{p.Code}'"));
                var cleanup = $"""
                    DELETE FROM iam.role_permissions rp USING iam.permissions p WHERE p.id = rp.permission_id AND p.code IN ({codes});
                    DELETE FROM iam.permissions WHERE code IN ({codes});
                    DELETE FROM inventory.movement_types WHERE code = 'REPOSICION_GARANTIA';
                    DELETE FROM accounting.accounts WHERE code = '5.1.10';
                    """;
                await db.Database.ExecuteSqlRawAsync(cleanup);
                var category = new Category(tenantId, "LEG", "Legado");
                var product = Product.Create(tenantId, "LEG-001", "Producto legado", category.Id, tenant.Units["UND"], TrackingMode.Serial);
                var variant = product.DefaultVariant.Id;
                var lotA = Batch.CreateDefault(tenantId, variant);
                var lotB = new Batch(tenantId, variant, "L-2", null, null);
                var level = StockLevel.Open(tenantId, tenant.BranchId, tenant.DefaultBinId, lotA.Id);
                db.AddRange(category, product, lotA, lotB, level);
                await db.SaveChangesAsync();
                var legacy = $"""
                    INSERT INTO inventory.serial_numbers (id, tenant_id, batch_id, serial, stock_level_id, status, created_at) VALUES
                        ('{UuidV4()}', '{tenantId}', '{lotA.Id}', 'LEG-0001', '{level.Id}', 'InStock', timestamptz '2026-01-15 10:00:00+00'),
                        ('{UuidV4()}', '{tenantId}', '{lotB.Id}', '{(duplicated ? "LEG-0001" : "LEG-0002")}', NULL, 'Sold', timestamptz '2026-02-01 10:00:00+00');
                    """;
                await db.Database.ExecuteSqlRawAsync(legacy);

                if (duplicated)
                {
                    var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
                    Assert.Contains("M-INV V4.2", error.MessageText, StringComparison.Ordinal);
                    Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                        "SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'service'").SingleAsync());
                    Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                        "SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'serial_numbers' AND column_name = 'variant_id'")
                        .SingleAsync());
                    continue;
                }
                await migrator.MigrateAsync();
                var rows = await db.Database.SqlQueryRaw<LegacySerial>(
                    "SELECT serial AS \"Serial\", variant_id AS \"VariantId\", kind AS \"Kind\", received_at AS \"ReceivedAt\" " +
                    "FROM inventory.serial_numbers ORDER BY serial").ToListAsync();
                Assert.Equal(2, rows.Count);
                Assert.All(rows, r => Assert.Equal((variant, "Serial"), (r.VariantId, r.Kind)));
                Assert.Equal(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero), rows[0].ReceivedAt);
                Assert.Equal(new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero), rows[1].ReceivedAt);
                // Sin valores provisionales: la columna ya no tiene DEFAULT
                Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_schema = 'inventory' " +
                    "AND table_name = 'serial_numbers' AND column_name IN ('variant_id', 'kind', 'received_at') AND column_default IS NOT NULL")
                    .SingleAsync());
                // Datos V4.2 de la empresa existente
                Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM inventory.movement_types WHERE code = 'REPOSICION_GARANTIA' AND tenant_id = " + $"'{tenantId}'")
                    .SingleAsync());
                Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM accounting.accounts WHERE code = '5.1.10' AND tenant_id = " + $"'{tenantId}'").SingleAsync());
                Assert.Equal(Persistence.Migrations.V42TechRetail.TechRolePermissions.Length, await db.Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM iam.role_permissions rp JOIN iam.permissions p ON p.id = rp.permission_id " +
                    $"WHERE rp.tenant_id = '{tenantId}' AND p.code IN ('catalog.specs.manage', 'inventory.serials.view', 'inventory.serials.manage', " +
                    "'service.rma.open', 'service.rma.manage', 'sales.pcbuild.manage')").SingleAsync());
                // La reversa también funciona (vuelve a la V4.1 sin las tablas nuevas)
                await migrator.MigrateAsync("20260926033017_V41CafcNumbering");
                Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM information_schema.schemata WHERE schema_name = 'service'").SingleAsync());
            }
            finally
            {
                NpgsqlConnection.ClearAllPools();
                await using var conn = new NpgsqlConnection(admin);
                await conn.OpenAsync();
                await new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", conn).ExecuteNonQueryAsync();
            }
        }

        static Guid UuidV4() => Guid.NewGuid();
    }

    private sealed record LegacySerial(string Serial, Guid VariantId, string Kind, DateTimeOffset ReceivedAt);
}
