using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Provisioning;
using Npgsql;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// V6 · Tienda web conectada contra un PostgreSQL real (<c>MINV_TEST_PG</c>): el aprovisionamiento crea el usuario técnico
/// TIENDA_WEB con sus permisos; el arco de origen de las reservas (CHECK), los CHECK del armado (Reserved exige vigencia; un
/// armado web exige contacto), la bitácora <c>sales.pc_build_events</c> es append-only y de la sucursal (RLS RESTRICTIVA con un
/// rol sin privilegios), y una reserva completa (armado + reservas de stock + processed_requests) cabe en las restricciones.
/// </summary>
public sealed class V6StorefrontPostgresTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private const string AdminEmail = "admin@tienda.example";
    private const string AdminPassword = "Clave-Tienda-2026";
    private static readonly UnitRule Und = new("UND", false);

    private static async Task<(IServiceScope Scope, ProvisionedTenant Tenant)> NewTenantAsync(ServiceProvider provider, string code)
    {
        var scope = provider.CreateScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().ProvisionAsync(
            new ProvisionTenantRequest(code, "Tienda " + code, null, AdminEmail, "Administrador", AdminPassword));
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand(code, AdminEmail, AdminPassword, "pruebas", "6.0.0"));
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

    [PostgresFact]
    public async Task El_aprovisionamiento_crea_el_usuario_tecnico_y_las_restricciones_de_la_tienda_se_cumplen()
    {
        await using var provider = pg.Services();
        var (scope, tenant) = await NewTenantAsync(provider, "TIENDA1");
        using (scope)
        {
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            var technical = await db.Users.SingleAsync(u => u.Email == "tienda-web@tienda.example");
            var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == technical.Id select r.Code).ToListAsync();
            Assert.Equal([RoleCodes.Storefront], roles);
            var (_, permissions) = await UserAccess.PermissionsAsync(db, technical.Id, default);
            Assert.Equal([PermissionCodes.StockView, PermissionCodes.StorefrontRead, PermissionCodes.StorefrontReserve], permissions.Order());
            Assert.True(await db.BranchUsers.AnyAsync(b => b.UserId == technical.Id && b.BranchId == tenant.BranchId));
            Assert.True(await db.UserCredentials.AnyAsync(c => c.UserId == technical.Id));

            var (product, level) = await StockAsync(db, tenant, "CASE-01", 5);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTimeOffset.UtcNow;

            // Armado web con contacto, cotizado, reservado y con su reserva de stock (arco: línea de armado) → cabe
            var build = PcBuild.CreateWeb(tenant.TenantId, tenant.BranchId, "ARM-WEB-000001", "Armado web de Ana", "Ana Quispe", "71234567", null, null,
                today.AddDays(2), tenant.AdminUserId, now);
            var line = build.AddLine(PcSlot.Case, product.DefaultVariant.Id, 2, 450m);
            build.Quote(today.AddDays(2), today, new PcCompatibilityReport([], 0, 0, null), false, now, tenant.AdminUserId);
            build.Reserve(now, now.AddHours(48), today, tenant.AdminUserId);
            var reservation = level.Reserve(2, now.AddHours(48), now, pcBuildLineId: line.Id);
            db.AddRange(build, reservation);
            await db.SaveChangesAsync();
            // (la otra prueba de la clase comparte la base: se cuenta solo lo de este armado y esta empresa)
            Assert.Equal(3, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM sales.pc_build_events WHERE pc_build_id = {build.Id}").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM integration.outbox_events WHERE event_type = 'pcbuild.reserved' AND tenant_id = {tenant.TenantId}").SingleAsync());

            // El arco de origen se rechaza en la base aunque el dominio no lo vea (SQL directo)
            var badArc = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                INSERT INTO inventory.stock_reservations (id, tenant_id, branch_id, stock_level_id, quantity, status, expires_at, pos_session_id, sales_order_line_id, pc_build_line_id)
                VALUES (gen_random_uuid(), {tenant.TenantId}, {tenant.BranchId}, {level.Id}, 1, 'Active', now() + interval '1 hour', NULL, {line.Id}, {line.Id})
                """));
            Assert.Equal("23514", badArc.SqlState);
            // Reservado sin vigencia y armado web sin contacto: los CHECK de pc_builds
            var noWindow = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE sales.pc_builds SET reserved_until = NULL WHERE id = {0}", build.Id));
            Assert.Equal("23514", noWindow.SqlState);
            var noContact = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE sales.pc_builds SET contact_phone = NULL WHERE id = {0}", build.Id));
            Assert.Equal("23514", noContact.SqlState);
            // La bitácora es append-only (trigger)
            var appendOnly = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM sales.pc_build_events"));
            Assert.Equal("P0001", appendOnly.SqlState);
            // Vender consume: el dominio marca la reserva y la existencia baja una sola vez
            db.ChangeTracker.Clear();
            var tracked = await db.PcBuilds.Include(b => b.Lines).SingleAsync(b => b.Id == build.Id);
            var trackedLevel = await db.StockLevels.SingleAsync(l => l.Id == level.Id);
            var trackedReservation = await db.StockReservations.SingleAsync(r => r.Id == reservation.Id);
            trackedLevel.Fulfill(trackedReservation);
            var sale = await db.MovementTypes.SingleAsync(t => t.Code == MovementTypeCodes.Sale);
            db.Add(trackedLevel.Register(sale, 2, Und, new MovementContext(tenant.AdminUserId, today, now, "F-CM-000001")));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var final = await db.StockLevels.AsNoTracking().SingleAsync(l => l.Id == level.Id);
            Assert.Equal((3m, 0m), (final.QuantityOnHand, final.QuantityReserved));
        }
    }

    /// <summary>Con un rol SIN privilegios (como minv_server) el armado web y su bitácora solo se ven desde su sucursal.</summary>
    [PostgresFact]
    public async Task La_bitacora_del_armado_solo_se_ve_desde_su_sucursal_con_un_rol_sin_privilegios()
    {
        await using var provider = pg.Services();
        var (scope, tenant) = await NewTenantAsync(provider, "TIENDA2");
        Guid otherBranch;
        using (scope)
        {
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            var (product, _) = await StockAsync(db, tenant, "CASE-02", 5);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTimeOffset.UtcNow;
            var address = await db.Set<Domain.Sales.Address>().FirstAsync();
            var branch = new Domain.Warehousing.Branch(tenant.TenantId, "SB", "Sucursal B", address.Id);
            db.Add(branch);
            otherBranch = branch.Id;
            var build = PcBuild.CreateWeb(tenant.TenantId, tenant.BranchId, "ARM-WEB-000001", "Armado web", "Ana", "71234567", null, null, today.AddDays(2),
                tenant.AdminUserId, now);
            build.AddLine(PcSlot.Case, product.DefaultVariant.Id, 1, 450m);
            db.Add(build);
            await db.SaveChangesAsync();
        }
        var role = "minv_t6_" + Guid.NewGuid().ToString("N")[..8];
        await using (var admin = new NpgsqlConnection(pg.ConnectionString))
        {
            await admin.OpenAsync();
            await using var grant = new NpgsqlCommand($"""
                CREATE ROLE {role} LOGIN PASSWORD 'clave-{role}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting, integration, billing, service TO {role};
                GRANT SELECT ON ALL TABLES IN SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting, integration, billing, service TO {role};
                """, admin);
            await grant.ExecuteNonQueryAsync();
        }
        try
        {
            var restricted = new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Username = role, Password = "clave-" + role }.ConnectionString;
            var services = new ServiceCollection();
            services.AddMinvApplication();
            services.AddMinvInfrastructure(restricted);
            await using var limited = services.BuildServiceProvider();
            async Task<(int Builds, int Events)> CountAsync(Guid branch)
            {
                using var s = limited.CreateScope();
                var ctx = s.ServiceProvider.GetRequiredService<ITenantContext>();
                ctx.Set(tenant.TenantId);
                ctx.SetBranches(new BranchScope(false, [branch], branch));
                var db = s.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
                return (await db.PcBuilds.IgnoreQueryFilters().CountAsync(), await db.Set<PcBuildEvent>().IgnoreQueryFilters().CountAsync());
            }
            Assert.Equal((1, 1), await CountAsync(tenant.BranchId));   // la casa matriz ve el armado y su fila «creado»
            Assert.Equal((0, 0), await CountAsync(otherBranch));        // la otra sucursal no ve nada (RLS RESTRICTIVA)
        }
        finally
        {
            await using var admin = new NpgsqlConnection(pg.ConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"""
                REVOKE ALL ON ALL TABLES IN SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting, integration, billing, service FROM {role};
                REVOKE ALL ON SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting, integration, billing, service FROM {role};
                DROP ROLE {role};
                """, admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
