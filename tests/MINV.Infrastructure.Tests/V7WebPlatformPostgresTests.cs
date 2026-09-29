using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Iam;
using MINV.Domain.Catalog;
using MINV.Domain.Inventory;
using MINV.Domain.Integration;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Persistence.Migrations;
using MINV.Infrastructure.Provisioning;
using Npgsql;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// V7 · Plataforma web contra un PostgreSQL real (<c>MINV_TEST_PG</c>): las 4 tablas nuevas con su RLS (empresa y, en el correo
/// pedido, sucursal RESTRICTIVA), los libros append-only del correo, los triggers del carrito (ranura nula solo en un carrito; el
/// tipo no cambia), los CHECK nuevos del armado, la función SECURITY DEFINER <c>integration.claim_outgoing_mails</c> (solo quien
/// tiene EXECUTE la llama y reclama la cola de todas las empresas sin ver sus filas) y la migración sobre una base de la V6 CON
/// datos: relleno del tipo, datos de la V7 para la empresa existente, guardia del rol CLIENTE y reversa.
/// </summary>
public sealed class V7WebPlatformPostgresTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private const string AdminEmail = "admin@web.example";
    private const string AdminPassword = "Clave-Web-2026";
    private const string V6Migration = "20260927173304_V6Storefront";
    private static readonly UnitRule Und = new("UND", false);

    private static async Task<(IServiceScope Scope, ProvisionedTenant Tenant)> NewTenantAsync(ServiceProvider provider, string code)
    {
        var scope = provider.CreateScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().ProvisionAsync(
            new ProvisionTenantRequest(code, "Web " + code, null, AdminEmail, "Administrador", AdminPassword));
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand(code, AdminEmail, AdminPassword, "pruebas", "7.0.0"));
        return (scope, tenant);
    }

    private static async Task<(Product Product, StockLevel Level)> StockAsync(MinvWriteDbContext db, ProvisionedTenant tenant, string sku, int quantity)
    {
        var now = DateTimeOffset.UtcNow;
        var category = new Category(tenant.TenantId, "C" + sku.Replace("-", string.Empty, StringComparison.Ordinal), "Categoría " + sku);
        var product = Product.Create(tenant.TenantId, sku, "Producto " + sku, category.Id, tenant.Units["UND"], TrackingMode.None);
        var batch = Batch.CreateDefault(tenant.TenantId, product.DefaultVariant.Id);
        var level = StockLevel.Open(tenant.TenantId, tenant.BranchId, tenant.DefaultBinId, batch.Id);
        db.AddRange(category, product, batch, level);
        var initial = await db.MovementTypes.SingleAsync(t => t.Code == MovementTypeCodes.InitialBalance);
        db.Add(level.Register(initial, quantity, Und, new MovementContext(tenant.AdminUserId, DateOnly.FromDateTime(now.UtcDateTime), now, "INV-INICIAL")));
        await db.SaveChangesAsync();
        return (product, level);
    }

    private static async Task<int> CountAsync(MinvWriteDbContext db, string sql) => await db.Database.SqlQueryRaw<int>(sql).SingleAsync();

    /// <summary>Políticas, append-only, triggers del carrito, CHECK nuevos y la definición de la función del correo.</summary>
    [PostgresFact]
    public async Task V7_las_tablas_nuevas_tienen_RLS_append_only_y_el_carrito_sus_reglas_en_la_base()
    {
        await using var provider = pg.Services();
        var (scope, tenant) = await NewTenantAsync(provider, "WEB" + Random.Shared.Next(100, 999));
        using (scope)
        {
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            // 1. Tablas nuevas: política de empresa en las 4; de sucursal (RESTRICTIVA) solo en el correo pedido; append-only en el
            //    correo y sus intentos (la cola y las cuentas son mutables)
            foreach (var table in V7WebPlatform.NewTablesV7)
            {
                var (schema, name) = (table.Split('.')[0], table.Split('.')[1]);
                Assert.Equal(1, await CountAsync(db, "SELECT count(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation' " +
                                                     $"AND schemaname = '{schema}' AND tablename = '{name}'"));
                Assert.Equal(V7WebPlatform.BranchTablesV7.Contains(table) ? 1 : 0, await CountAsync(db,
                    "SELECT count(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'branch_isolation' AND permissive = 'RESTRICTIVE' " +
                    $"AND schemaname = '{schema}' AND tablename = '{name}'"));
                Assert.Equal(V7WebPlatform.AppendOnlyTablesV7.Contains(table) ? 2 : 0, await CountAsync(db,
                    "SELECT count(*)::int AS \"Value\" FROM pg_trigger g JOIN pg_class c ON c.oid = g.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace " +
                    $"WHERE g.tgname IN ('trg_append_only', 'trg_append_only_truncate') AND n.nspname = '{schema}' AND c.relname = '{name}'"));
            }
            Assert.Equal(2, await CountAsync(db, "SELECT count(*)::int AS \"Value\" FROM pg_trigger WHERE tgname IN ('trg_pc_build_line_slot', 'trg_pc_build_kind_immutable')"));
            Assert.Contains("'web'", await db.Database.SqlQueryRaw<string>(
                "SELECT pg_get_constraintdef(oid) AS \"Value\" FROM pg_constraint WHERE conname = 'ck_audit_logs_canal'").SingleAsync(), StringComparison.Ordinal);
            // 2. La función del despachador: SECURITY DEFINER, search_path fijo, sin EXECUTE para PUBLIC; minv_server sí y minv_app no
            //    (cuando esos roles existen en el servidor de prueba)
            Assert.Equal(1, await CountAsync(db, "SELECT count(*)::int AS \"Value\" FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace " +
                                                 "WHERE n.nspname = 'integration' AND p.proname = 'claim_outgoing_mails' AND p.prosecdef " +
                                                 "AND 'search_path=pg_catalog, integration' = ANY (p.proconfig)"));
            const string function = "'integration.claim_outgoing_mails(integer, integer)'";
            Assert.Equal(0, await CountAsync(db, $"SELECT CASE WHEN has_function_privilege('public', {function}, 'EXECUTE') THEN 1 ELSE 0 END AS \"Value\""));
            foreach (var (role, expected) in new[] { ("minv_server", 1), ("minv_app", 0) })
            {
                if (await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM pg_roles WHERE rolname = '{role}' AND NOT rolsuper") == 1)
                {
                    Assert.Equal(expected, await CountAsync(db,
                        $"SELECT CASE WHEN has_function_privilege('{role}', {function}, 'EXECUTE') THEN 1 ELSE 0 END AS \"Value\""));
                }
            }

            // 3. Carrito (regla P-05): un armado con su ranura y un carrito de mostrador con una pieza SIN ranura caben (el trigger deja
            //    pasar la línea del carrito); los CHECK del tipo y de los datos para la factura
            var (product, _) = await StockAsync(db, tenant, "RAM-16", 10);
            var now = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var build = new PcBuild(tenant.TenantId, tenant.BranchId, "ARM-CM-000001", "PC de oficina", null, today.AddDays(7), tenant.AdminUserId, now);
            var buildLine = build.AddLine(PcSlot.Ram, product.DefaultVariant.Id, 1, 350m);
            var cart = PcBuild.CreateDesktopCart(tenant.TenantId, tenant.BranchId, "RES-CM-000001", "Carrito de mostrador", null, today.AddDays(2),
                tenant.AdminUserId, now);
            var cartLine = cart.AddLine(null, product.DefaultVariant.Id, 2, 350m);
            db.AddRange(build, cart);
            await db.SaveChangesAsync();
            Assert.Equal(1, await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM sales.pc_build_lines WHERE id = '{cartLine.Id}' AND slot IS NULL"));
            // Una pieza sin ranura en un ARMADO: el trigger la rechaza al insertar y al vaciar la ranura
            var noSlot = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO sales.pc_build_lines (id, tenant_id, branch_id, pc_build_id, slot, variant_id, quantity, quoted_unit_price) " +
                "VALUES (gen_random_uuid(), {0}, {1}, {2}, NULL, {3}, 1, 10)", tenant.TenantId, tenant.BranchId, build.Id, product.DefaultVariant.Id));
            Assert.Equal("P0001", noSlot.SqlState);
            Assert.Contains("ARM-CM-000001", noSlot.MessageText, StringComparison.Ordinal);
            var cleared = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE sales.pc_build_lines SET slot = NULL WHERE id = {0}", buildLine.Id));
            Assert.Equal("P0001", cleared.SqlState);
            // El carrito sí admite otra pieza sin ranura (SQL directo) y una con ranura
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO sales.pc_build_lines (id, tenant_id, branch_id, pc_build_id, slot, variant_id, quantity, quoted_unit_price) " +
                "VALUES (gen_random_uuid(), {0}, {1}, {2}, NULL, {3}, 1, 10), (gen_random_uuid(), {0}, {1}, {2}, 'Monitor', {3}, 1, 10)",
                tenant.TenantId, tenant.BranchId, cart.Id, product.DefaultVariant.Id);
            // El tipo no cambia después de insertar (en ningún sentido); repetir el mismo valor no es un cambio
            var toCart = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE sales.pc_builds SET kind = 'Cart' WHERE id = {0}", build.Id));
            Assert.Equal("P0001", toCart.SqlState);
            Assert.Contains("no cambia", toCart.MessageText, StringComparison.Ordinal);
            var toBuild = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE sales.pc_builds SET kind = 'Build' WHERE id = {0}", cart.Id));
            Assert.Equal("P0001", toBuild.SqlState);
            await db.Database.ExecuteSqlRawAsync("UPDATE sales.pc_builds SET kind = kind, name = 'Carrito de mostrador 2' WHERE id = {0}", cart.Id);
            // CHECK: un tipo desconocido, un carrito publicado en la web y datos de factura incoherentes
            var unknown = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO sales.pc_builds (id, tenant_id, branch_id, number, name, valid_until, status, quoted_with_errors, created_by_user_id, channel, kind) " +
                "VALUES (gen_random_uuid(), {0}, {1}, 'ARM-CM-000009', 'Raro', current_date, 'Draft', false, {2}, 'Desktop', 'Combo')",
                tenant.TenantId, tenant.BranchId, tenant.AdminUserId));
            Assert.Equal("ck_pc_builds_tipo", unknown.ConstraintName);
            var published = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE sales.pc_builds SET status = 'Quoted', quoted_at = now(), published_to_web = true WHERE id = {0}", cart.Id));
            Assert.Equal("ck_pc_builds_tipo_publicado", published.ConstraintName);
            var buyer = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE sales.pc_builds SET buyer_document_number = '1234567' WHERE id = {0}", cart.Id));
            Assert.Equal("ck_pc_builds_factura_documento", buyer.ConstraintName);
            var complement = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE sales.pc_builds SET buyer_document_type = 5, buyer_document_number = '1234567019', buyer_complement = '1A' WHERE id = {0}", cart.Id));
            Assert.Equal("ck_pc_builds_factura_complemento", complement.ConstraintName);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE sales.pc_builds SET buyer_document_type = 1, buyer_document_number = '1234567', buyer_complement = '1A', buyer_name = 'ANA QUISPE' WHERE id = {0}",
                cart.Id);

            // 4. Cuenta de cliente: un usuario y un cliente a lo sumo en una cuenta
            var customer = await db.Customers.SingleAsync(c => c.Code == "CF");
            db.Add(new CustomerAccount(tenant.TenantId, tenant.AdminUserId, customer.Id));
            await db.SaveChangesAsync();
            var twice = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO sales.customer_accounts (id, tenant_id, user_id, customer_id) VALUES (gen_random_uuid(), {0}, {1}, {2})",
                tenant.TenantId, tenant.AdminUserId, customer.Id));
            Assert.Equal("23505", twice.SqlState);

            // 5. Correo pedido, su cola y un intento: el hecho y el intento son append-only; la cola es mutable
            var mail = new OutgoingMail(tenant.TenantId, tenant.BranchId, OutgoingMailKind.ReservationConfirmed, cart.Id, "cliente@correo.example",
                tenant.AdminUserId, now);
            db.AddRange(mail, new OutgoingMailDispatch(tenant.TenantId, mail.Id, now),
                new OutgoingMailAttempt(tenant.TenantId, mail.Id, 1, false, "El servidor del destinatario no respondió", now, 120));
            await db.SaveChangesAsync();
            var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE integration.outgoing_mails SET recipient = 'otro@correo.example' WHERE id = {0}", mail.Id));
            Assert.Contains("append-only", update.MessageText, StringComparison.Ordinal);
            var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "DELETE FROM integration.outgoing_mail_attempts WHERE outgoing_mail_id = {0}", mail.Id));
            Assert.Equal("P0001", delete.SqlState);
            var truncate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE integration.outgoing_mail_attempts"));
            Assert.Equal("P0001", truncate.SqlState);
            Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(
                "UPDATE integration.outgoing_mail_dispatch SET last_error = 'Pospuesto en la prueba' WHERE outgoing_mail_id = {0}", mail.Id));
            // FK compuesta con la sucursal: el correo no puede ser de otra sucursal que su reserva
            var other = new Branch(tenant.TenantId, "SB", "Sucursal B", null);
            db.Add(other);
            await db.SaveChangesAsync();
            var foreign = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO integration.outgoing_mails (id, tenant_id, branch_id, kind, pc_build_id, recipient, requested_at, requested_by_user_id) " +
                "VALUES (gen_random_uuid(), {0}, {1}, 'ReservationConfirmed', {2}, 'cliente@correo.example', now(), {3})",
                tenant.TenantId, other.Id, cart.Id, tenant.AdminUserId));
            Assert.Equal("23503", foreign.SqlState);
        }
    }

    /// <summary>Con un rol SIN privilegios (como minv_server): el correo pedido solo se ve desde la sucursal de su reserva; la cola, los
    /// intentos y las cuentas son de la empresa; sin EXECUTE la función del despachador se rechaza y, con EXECUTE, reclama la cola
    /// aunque el rol no vea esas filas (SECURITY DEFINER) y adelanta el próximo intento (arrendamiento).</summary>
    [PostgresFact]
    public async Task V7_el_correo_solo_se_ve_desde_su_sucursal_y_la_cola_se_reclama_solo_con_la_funcion()
    {
        await using var provider = pg.Services();
        var (scope, tenant) = await NewTenantAsync(provider, "MAIL" + Random.Shared.Next(100, 999));
        var role = "minv_v7_" + Guid.NewGuid().ToString("N")[..8];
        const string password = "V7-Prueba-2026-x";
        Guid otherBranchId;
        Guid mailId;
        using (scope)
        {
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            var (product, _) = await StockAsync(db, tenant, "MON-24", 5);
            var now = DateTimeOffset.UtcNow;
            var other = new Branch(tenant.TenantId, "SB", "Sucursal B", null);
            otherBranchId = other.Id;
            var cart = PcBuild.CreateWebCart(tenant.TenantId, tenant.BranchId, "RES-WEB-000001", "Carrito de Ana", "Ana Quispe", "71234567",
                "ana@correo.example", null, DateOnly.FromDateTime(now.UtcDateTime).AddDays(2), tenant.AdminUserId, now);
            cart.AddLine(null, product.DefaultVariant.Id, 1, 1299m);
            var mail = new OutgoingMail(tenant.TenantId, tenant.BranchId, OutgoingMailKind.ReservationConfirmed, cart.Id, "ana@correo.example",
                tenant.AdminUserId, now);
            mailId = mail.Id;
            db.AddRange(other, cart, mail, new OutgoingMailDispatch(tenant.TenantId, mail.Id, now.AddMinutes(-1)),
                new OutgoingMailAttempt(tenant.TenantId, mail.Id, 1, false, "Buzón lleno", now, 80));
            await db.SaveChangesAsync();

            await using var owner = new NpgsqlConnection(pg.ConnectionString);
            await owner.OpenAsync();
            await using var cmd = new NpgsqlCommand($"""
                CREATE ROLE {role} LOGIN NOBYPASSRLS PASSWORD '{password}';
                GRANT CONNECT ON DATABASE {pg.Database} TO {role};
                GRANT USAGE ON SCHEMA iam, sales, integration TO {role};
                GRANT SELECT ON {string.Join(", ", V7WebPlatform.NewTablesV7)}, sales.pc_builds TO {role};
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
            async Task Session(string tenantId, string branches)
            {
                await using var c = new NpgsqlCommand(
                    $"SELECT set_config('minv.tenant_id', '{tenantId}', false), set_config('minv.branch_ids', '{branches}', false)", conn);
                await c.ExecuteNonQueryAsync();
            }
            var mine = $"outgoing_mail_id = '{mailId}'";
            // Sin empresa en la sesión no se ve nada: tampoco la cola
            Assert.Equal(0, await Scalar($"SELECT count(*) FROM integration.outgoing_mail_dispatch WHERE {mine}"));
            // Sin EXECUTE, la función del despachador se rechaza (REVOKE ... FROM PUBLIC)
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => await Scalar("SELECT count(*) FROM integration.claim_outgoing_mails(10, 120)"));
            Assert.Equal("42501", denied.SqlState);
            // Desde la casa matriz: el correo, su cola y su intento; desde la otra sucursal: la cola y el intento (de la empresa) pero no
            // el correo (RLS RESTRICTIVA)
            await Session(tenant.TenantId.ToString(), tenant.BranchId.ToString());
            Assert.Equal(1, await Scalar($"SELECT count(*) FROM integration.outgoing_mails WHERE id = '{mailId}'"));
            Assert.Equal(1, await Scalar($"SELECT count(*) FROM integration.outgoing_mail_dispatch WHERE {mine}"));
            Assert.Equal(1, await Scalar($"SELECT count(*) FROM integration.outgoing_mail_attempts WHERE {mine}"));
            await Session(tenant.TenantId.ToString(), otherBranchId.ToString());
            Assert.Equal(0, await Scalar($"SELECT count(*) FROM integration.outgoing_mails WHERE id = '{mailId}'"));
            Assert.Equal(1, await Scalar($"SELECT count(*) FROM integration.outgoing_mail_dispatch WHERE {mine}"));
            Assert.Equal(1, await Scalar($"SELECT count(*) FROM integration.outgoing_mail_attempts WHERE {mine}"));
            // Con EXECUTE (como minv_server) y SIN empresa en la sesión, la función reclama el pendiente vencido una sola vez
            await Session(string.Empty, string.Empty);
            await using (var owner = new NpgsqlConnection(pg.ConnectionString))
            {
                await owner.OpenAsync();
                await new NpgsqlCommand($"GRANT EXECUTE ON FUNCTION integration.claim_outgoing_mails(integer, integer) TO {role}", owner).ExecuteNonQueryAsync();
            }
            Assert.Equal(1, await Scalar($"SELECT count(*) FROM integration.claim_outgoing_mails(500, 120) WHERE {mine}"));
            Assert.Equal(0, await Scalar($"SELECT count(*) FROM integration.claim_outgoing_mails(500, 120) WHERE {mine}"));
            await using (var owner = new NpgsqlConnection(pg.ConnectionString))
            {
                await owner.OpenAsync();
                await using var lease = new NpgsqlCommand(
                    $"SELECT extract(epoch FROM next_attempt_at - now())::int FROM integration.outgoing_mail_dispatch WHERE {mine}", owner);
                Assert.InRange(Convert.ToInt32(await lease.ExecuteScalarAsync()), 100, 120);
            }
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

    /// <summary>
    /// Migración sobre una base de la V6 CON datos (regla B-15): los armados existentes (borrador, cotizado y publicado, reserva web
    /// con su reserva de stock y anulado) quedan de tipo Build sin valor por defecto; la empresa existente recibe el rol CLIENTE, los
    /// permisos account.* y su matriz; después de migrar se reserva un carrito sin ranura; la reversa deja la V6 como estaba (la
    /// pieza sin ranura pasa a Periféricos) y se vuelve a migrar. Con un rol CLIENTE del personal, la guardia detiene la migración
    /// sin cambios.
    /// </summary>
    [PostgresFact]
    public async Task V7_la_migracion_rellena_el_tipo_de_una_base_V6_con_datos_y_su_guardia_protege_el_rol_CLIENTE()
    {
        foreach (var staffCustomerRole in new[] { false, true })
        {
            var database = "minv_v7mig_" + Guid.NewGuid().ToString("N")[..10];
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
                await migrator.MigrateAsync(V6Migration);
                async Task<int> Count(string sql) => await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
                const string tables = "SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_type = 'BASE TABLE' " +
                                      "AND table_schema IN ('iam','catalog','warehouse','inventory','purchasing','sales','accounting','integration','billing','service') " +
                                      "AND table_name <> '__ef_migrations_history'";
                const string kindColumn = "SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_schema = 'sales' " +
                                          "AND table_name = 'pc_builds' AND column_name = 'kind'";
                Assert.Equal(153, await Count(tables));

                // Empresa de la V6: el aprovisionamiento actual ya siembra lo de la V7 (rol CLIENTE y permisos account.*), así que se quita
                // para que lo agregue la migración. Stock con tablas que la V7 no cambia (por EF) y armados con las columnas de la V6 (SQL).
                var tenant = await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().ProvisionAsync(
                    new ProvisionTenantRequest("LEGV6", "Legado V6", null, AdminEmail, "Administrador", AdminPassword));
                var tenantId = tenant.TenantId;
                var codes = string.Join(", ", V7WebPlatform.AccountPermissions.Select(p => $"'{p.Code}'"));
                var cleanup = $"""
                    DELETE FROM iam.role_permissions rp USING iam.permissions p WHERE p.id = rp.permission_id AND p.code IN ({codes});
                    DELETE FROM iam.permissions WHERE code IN ({codes});
                    """;
                await db.Database.ExecuteSqlRawAsync(cleanup);
                if (staffCustomerRole)
                {
                    // Un rol CLIENTE con un permiso del personal: el registro de la tienda se lo daría a cualquier visitante
                    var staff = $"""
                        INSERT INTO iam.role_permissions (tenant_id, role_id, permission_id)
                        SELECT r.tenant_id, r.id, p.id FROM iam.roles r JOIN iam.permissions p ON p.tenant_id = r.tenant_id AND p.code = 'sales.view'
                        WHERE r.code = '{V7WebPlatform.CustomerRoleCode}' AND r.tenant_id = '{tenantId}';
                        """;
                    await db.Database.ExecuteSqlRawAsync(staff);
                    var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
                    Assert.Contains("M-INV V7", error.MessageText, StringComparison.Ordinal);
                    Assert.Equal(153, await Count(tables));
                    Assert.Equal(0, await Count(kindColumn));
                    continue;
                }
                var noCustomerRole = "DELETE FROM iam.roles WHERE code = '" + V7WebPlatform.CustomerRoleCode + "';";
                await db.Database.ExecuteSqlRawAsync(noCustomerRole);

                var (product, level) = await StockAsync(db, tenant, "GPU-4060", 5);
                var variant = product.DefaultVariant.Id;
                var (draft, quoted, reserved, cancelled, reservedLine) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
                var (t, b, u) = (tenantId, tenant.BranchId, tenant.AdminUserId);
                var legacy = $"""
                    INSERT INTO sales.pc_builds (id, tenant_id, branch_id, number, name, customer_id, valid_until, status, quoted_with_errors,
                                                 created_by_user_id, quoted_at, channel, contact_name, contact_phone, contact_email, reserved_at,
                                                 reserved_until, cancel_reason, published_to_web)
                    VALUES ('{draft}', '{t}', '{b}', 'ARM-CM-000001', 'Borrador', NULL, current_date + 7, 'Draft', false, '{u}', NULL, 'Desktop',
                            NULL, NULL, NULL, NULL, NULL, NULL, false),
                           ('{quoted}', '{t}', '{b}', 'ARM-CM-000002', 'PC Gamer sugerida', NULL, current_date + 7, 'Quoted', false, '{u}', now(),
                            'Desktop', NULL, NULL, NULL, NULL, NULL, NULL, true),
                           ('{reserved}', '{t}', '{b}', 'ARM-WEB-000001', 'Armado web de Ana', NULL, current_date + 2, 'Reserved', false, '{u}', now(),
                            'Web', 'Ana Quispe', '71234567', 'ana@correo.example', now(), now() + interval '48 hours', NULL, false),
                           ('{cancelled}', '{t}', '{b}', 'ARM-CM-000003', 'Anulado', NULL, current_date + 7, 'Cancelled', false, '{u}', NULL,
                            'Desktop', NULL, NULL, NULL, NULL, NULL, 'Anulado por el vendedor', false);
                    INSERT INTO sales.pc_build_lines (id, tenant_id, branch_id, pc_build_id, slot, variant_id, quantity, quoted_unit_price)
                    VALUES (gen_random_uuid(), '{t}', '{b}', '{draft}', 'Gpu', '{variant}', 1, 3199),
                           (gen_random_uuid(), '{t}', '{b}', '{quoted}', 'Gpu', '{variant}', 1, 3199),
                           ('{reservedLine}', '{t}', '{b}', '{reserved}', 'Gpu', '{variant}', 2, 3199),
                           (gen_random_uuid(), '{t}', '{b}', '{cancelled}', 'Gpu', '{variant}', 1, 3199);
                    """;
                await db.Database.ExecuteSqlRawAsync(legacy);
                var reservation = level.Reserve(2, DateTimeOffset.UtcNow.AddHours(48), DateTimeOffset.UtcNow, pcBuildLineId: reservedLine);
                db.Add(reservation);
                await db.SaveChangesAsync();

                // Migrar a la V7 sin recrear
                await migrator.MigrateAsync();
                Assert.Equal(157, await Count(tables));
                Assert.Equal(4, await Count($"SELECT count(*)::int AS \"Value\" FROM sales.pc_builds WHERE tenant_id = '{t}' AND kind = 'Build'"));
                Assert.Equal(0, await Count($"SELECT count(*)::int AS \"Value\" FROM sales.pc_builds WHERE kind <> 'Build' OR buyer_document_type IS NOT NULL " +
                                            "OR buyer_document_number IS NOT NULL OR buyer_complement IS NOT NULL OR buyer_name IS NOT NULL"));
                Assert.Equal(0, await Count("SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_schema = 'sales' " +
                                            "AND table_name = 'pc_builds' AND column_name = 'kind' AND column_default IS NOT NULL"));
                Assert.Equal(1, await Count("SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_schema = 'sales' " +
                                            "AND table_name = 'pc_build_lines' AND column_name = 'slot' AND is_nullable = 'YES' AND column_default IS NULL"));
                Assert.Equal(1, await Count($"SELECT count(*)::int AS \"Value\" FROM sales.pc_builds WHERE id = '{quoted}' AND published_to_web"));
                Assert.Equal(1, await Count($"SELECT count(*)::int AS \"Value\" FROM inventory.stock_reservations WHERE pc_build_line_id = '{reservedLine}' " +
                                            "AND status = 'Active'"));
                Assert.Equal(1, await Count($"SELECT count(*)::int AS \"Value\" FROM inventory.stock_levels WHERE id = '{level.Id}' AND quantity_reserved = 2"));
                // Datos V7 de la empresa existente: rol CLIENTE de sistema, los dos permisos y la matriz (ADMIN y CLIENTE)
                Assert.Equal(1, await Count($"SELECT count(*)::int AS \"Value\" FROM iam.roles WHERE tenant_id = '{t}' " +
                                            $"AND code = '{V7WebPlatform.CustomerRoleCode}' AND name = '{V7WebPlatform.CustomerRoleName}' AND is_system"));
                Assert.Equal(2, await Count($"SELECT count(*)::int AS \"Value\" FROM iam.permissions WHERE tenant_id = '{t}' AND code IN ({codes})"));
                Assert.Equal(V7WebPlatform.AccountRolePermissions.Length, await Count(
                    "SELECT count(*)::int AS \"Value\" FROM iam.role_permissions rp JOIN iam.permissions p ON p.id = rp.permission_id " +
                    $"JOIN iam.roles r ON r.id = rp.role_id WHERE rp.tenant_id = '{t}' AND p.code IN ({codes}) AND r.code IN ('ADMIN', 'CLIENTE')"));
                // El modelo de la V7 lee los armados de la V6 como armados; y ya se puede guardar un carrito con una pieza sin ranura
                db.ChangeTracker.Clear();
                var kinds = await db.PcBuilds.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == t).Select(x => x.Kind).ToListAsync();
                Assert.Equal(4, kinds.Count(k => k == PcBuildKind.Build));
                var now = DateTimeOffset.UtcNow;
                var cart = PcBuild.CreateDesktopCart(t, b, "RES-CM-000001", "Carrito de mostrador", null, DateOnly.FromDateTime(now.UtcDateTime).AddDays(2), u, now);
                var cartLine = cart.AddLine(null, variant, 1, 3199m);
                db.Add(cart);
                await db.SaveChangesAsync();

                // Reversa: vuelve a la V6 (sin tablas, columnas, función, triggers ni datos de la V7; la pieza sin ranura a Periféricos)
                await migrator.MigrateAsync(V6Migration);
                Assert.Equal(153, await Count(tables));
                Assert.Equal(0, await Count(kindColumn));
                Assert.Equal(0, await Count("SELECT count(*)::int AS \"Value\" FROM pg_proc WHERE proname IN ('claim_outgoing_mails', " +
                                            "'minv_pc_build_line_slot', 'minv_pc_build_kind_immutable')"));
                Assert.Equal(1, await Count($"SELECT count(*)::int AS \"Value\" FROM sales.pc_build_lines WHERE id = '{cartLine.Id}' AND slot = 'Peripheral'"));
                Assert.Equal(1, await Count("SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_schema = 'sales' " +
                                            "AND table_name = 'pc_build_lines' AND column_name = 'slot' AND is_nullable = 'NO' AND column_default IS NULL"));
                Assert.Equal(0, await Count($"SELECT count(*)::int AS \"Value\" FROM iam.roles WHERE code = '{V7WebPlatform.CustomerRoleCode}'"));
                Assert.Equal(0, await Count($"SELECT count(*)::int AS \"Value\" FROM iam.permissions WHERE code IN ({codes})"));
                Assert.Equal(1, await Count($"SELECT count(*)::int AS \"Value\" FROM inventory.stock_reservations WHERE pc_build_line_id = '{reservedLine}' " +
                                            "AND status = 'Active'"));
                // Y se vuelve a migrar: el carrito de la prueba ya es un armado de la V6, así que también queda Build
                await migrator.MigrateAsync();
                Assert.Equal(157, await Count(tables));
                Assert.Equal(5, await Count($"SELECT count(*)::int AS \"Value\" FROM sales.pc_builds WHERE tenant_id = '{t}' AND kind = 'Build'"));
                Assert.Equal(1, await Count($"SELECT count(*)::int AS \"Value\" FROM iam.roles WHERE tenant_id = '{t}' AND code = '{V7WebPlatform.CustomerRoleCode}'"));
            }
            finally
            {
                NpgsqlConnection.ClearAllPools();
                await using var conn = new NpgsqlConnection(admin);
                await conn.OpenAsync();
                await new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", conn).ExecuteNonQueryAsync();
            }
        }
    }
}
