using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Abstractions;
using MINV.Infrastructure.Billing;
using MINV.Infrastructure.Services;
using MINV.Infrastructure.Tests.Billing;

namespace MINV.Infrastructure.Tests.Tech;

/// <summary>
/// V4.2 · Los mismos flujos de la edición Tecnología contra un PostgreSQL real (<c>MINV_TEST_PG</c>) con el simulador del SIN
/// en proceso: PostgreSQL valida lo que la memoria no conoce (CHECK de ubicación y estado de las series, FK compuestas con
/// la sucursal, trigger de los valores de las fichas, unicidad de un caso abierto por serie, bitácoras append-only) y la
/// vista de control <c>inventory.v_serial_breaches</c> queda vacía.
/// </summary>
public sealed class V42TechPostgresFlowTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DemoClock>();
        services.AddSingleton<IClock>(sp => sp.GetRequiredService<DemoClock>());
        services.AddSingleton<ISecretProtector>(AesGcmSecretProtector.Ephemeral());
        services.AddMinvApplication();
        services.AddMinvInfrastructure(pg.ConnectionString);
        services.AddMinvSiat(new SiatOptions { Mode = SiatGatewayMode.InProcessSimulator });
        return services.BuildServiceProvider();
    }

    private static Task<int> CountAsync(E_BillingTestHost host, string sql) =>
        host.DbAsync(db => db.Database.SqlQueryRaw<int>(sql).SingleAsync());

    private static async Task AssertConsistentAsync(E_BillingTestHost host)
    {
        var tenant = $"'{host.TenantId}'";
        Assert.Equal(0, await CountAsync(host, $"SELECT count(*)::int AS \"Value\" FROM inventory.v_serial_breaches WHERE tenant_id = {tenant}"));
        Assert.Equal(0, await CountAsync(host, $"SELECT count(*)::int AS \"Value\" FROM inventory.v_conservation_breaches WHERE tenant_id = {tenant}"));
        Assert.Equal(0, await CountAsync(host, "SELECT count(*)::int AS \"Value\" FROM (SELECT journal_entry_id FROM accounting.journal_lines " +
                                               $"WHERE tenant_id = {tenant} GROUP BY journal_entry_id HAVING sum(debit) <> sum(credit)) x"));
        // Cada serie tiene su bitácora y la bitácora no se puede reescribir
        Assert.Equal(0, await CountAsync(host, "SELECT count(*)::int AS \"Value\" FROM inventory.serial_numbers s " +
                                               $"WHERE s.tenant_id = {tenant} AND NOT EXISTS (SELECT 1 FROM inventory.serial_events e WHERE e.serial_number_id = s.id)"));
        await Assert.ThrowsAnyAsync<Exception>(() => host.DbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE inventory.serial_events SET note = 'x' WHERE tenant_id = {host.TenantId}")));
    }

    [PostgresFact]
    public async Task V42_ciclo_de_vida_de_una_serie_con_factura_RMA_y_reposicion_en_PostgreSQL()
    {
        await using var provider = Services();
        await using var host = await E_BillingTestHost.CreateAsync(provider);
        await V42TechFlowTests.RunLifecycleAsync(host);
        await AssertConsistentAsync(host);
        Assert.Equal(1, await CountAsync(host, "SELECT count(*)::int AS \"Value\" FROM service.warranty_claims " +
                                               $"WHERE tenant_id = '{host.TenantId}' AND status = 'Delivered' AND replacement_serial_id IS NOT NULL"));
        Assert.Equal(1, await CountAsync(host, "SELECT count(*)::int AS \"Value\" FROM sales.sales_return_line_serials " +
                                               $"WHERE tenant_id = '{host.TenantId}'"));
    }

    [PostgresFact]
    public async Task V42_fichas_tecnicas_y_devolucion_por_falla_en_PostgreSQL()
    {
        await using var provider = Services();
        await using (var host = await E_BillingTestHost.CreateAsync(provider))
        {
            // El trigger catalog.minv_spec_value_matches acepta el reemplazo de una ficha (primero se quitan, luego se agregan)
            await V42TechFlowTests.RunSpecsAsync(host);
            await AssertConsistentAsync(host);
        }
        await using (var host = await E_BillingTestHost.CreateAsync(provider))
        {
            await V42TechFlowTests.RunDefectiveReturnAsync(host);
            await AssertConsistentAsync(host);
        }
    }

    [PostgresFact]
    public async Task V42_transferencia_con_series_y_armador_vendido_en_PostgreSQL()
    {
        await using var provider = Services();
        await using (var host = await E_BillingTestHost.CreateAsync(provider))
        {
            await V42TechFlowTests.RunTransferAsync(host);
            await AssertConsistentAsync(host);
            Assert.Equal(0, await CountAsync(host, "SELECT count(*)::int AS \"Value\" FROM inventory.v_transfer_breaches"));
            Assert.Equal(3, await CountAsync(host, "SELECT count(*)::int AS \"Value\" FROM inventory.stock_transfer_line_serials " +
                                                   $"WHERE tenant_id = '{host.TenantId}'"));
        }
        await using (var host = await E_BillingTestHost.CreateAsync(provider))
        {
            await V42TechFlowTests.RunBuilderAsync(host);
            await AssertConsistentAsync(host);
            Assert.Equal(7, await CountAsync(host, "SELECT count(*)::int AS \"Value\" FROM sales.sales_order_line_serials " +
                                                   $"WHERE tenant_id = '{host.TenantId}'"));
        }
    }
}
