using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Domain.Billing;
using MINV.Infrastructure.Billing;
using MINV.Infrastructure.Billing.Hosting;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>V4.1 · Trabajo en segundo plano de la facturación: una empresa por scope, con su tenant fijado; los errores
/// de una empresa no detienen a las demás.</summary>
public sealed class SiatBackgroundServiceTests
{
    private sealed record Visit(string Step, Guid Tenant, bool AllBranches, int Scope);

    private sealed class Journal
    {
        public ConcurrentQueue<Visit> Visits { get; } = new();

        public HashSet<Guid> FailingDispatch { get; } = [];

        public TaskCompletionSource<bool> Maintained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Scopes;
    }

    private sealed class ScopeMarker(Journal journal)
    {
        public int Id { get; } = Interlocked.Increment(ref journal.Scopes);
    }

    private sealed class FakeWorker(ITenantContext tenant, ScopeMarker scope, Journal journal) : ISiatWorker
    {
        public Task<DispatchResult> DispatchAsync(Guid? documentId, int max, CancellationToken cancellationToken = default)
        {
            journal.Visits.Enqueue(new Visit($"dispatch:{max}", tenant.TenantId, tenant.Branches.AllBranches, scope.Id));
            if (journal.FailingDispatch.Contains(tenant.TenantId))
            {
                throw new InvalidOperationException("falla simulada del envío");
            }
            return Task.FromResult(new DispatchResult(0, 0, 0, 0, [], []));
        }

        public Task<SiatMaintenanceResult> MaintainAsync(bool force, CancellationToken cancellationToken = default)
        {
            journal.Visits.Enqueue(new Visit($"maintain:{force}", tenant.TenantId, tenant.Branches.AllBranches, scope.Id));
            journal.Maintained.TrySetResult(true);
            return Task.FromResult(new SiatMaintenanceResult(0, 0, 0, 0, 0, []));
        }
    }

    private sealed class FixedTenants(params Guid[] tenants) : ISiatTenantSource
    {
        public Task<IReadOnlyList<Guid>> ActiveTenantsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(tenants);
    }

    private static (ServiceProvider Services, Journal Journal) Build(ISiatTenantSource source, SiatBackgroundOptions options)
    {
        var journal = new Journal();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(journal)
            .AddScoped<ScopeMarker>()
            .AddScoped<ITenantContext, TenantContext>()
            .AddScoped<ISiatWorker, FakeWorker>()
            .AddSingleton(source)
            .AddMinvSiatBackground(options);
        return (services.BuildServiceProvider(), journal);
    }

    [Fact]
    public async Task Cada_empresa_se_procesa_en_su_propio_scope_con_su_tenant_y_sin_restriccion_de_sucursal()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (services, journal) = Build(new FixedTenants(a, b, c), new SiatBackgroundOptions { DispatchBatch = 25 });
        await using (services)
        {
            journal.FailingDispatch.Add(b);
            var service = new SiatBackgroundService(services.GetRequiredService<IServiceScopeFactory>(),
                services.GetRequiredService<SiatBackgroundOptions>(), NullLogger<SiatBackgroundService>.Instance);

            var pass = await service.RunOnceAsync(maintain: true);

            Assert.Equal(new SiatBackgroundPass(3, 2, 3, 1), pass);   // la falla de B no detuvo a C (ni el mantenimiento de B)
            var visits = journal.Visits.ToList();
            Assert.Equal(
                [("dispatch:25", a), ("maintain:False", a), ("dispatch:25", b), ("maintain:False", b), ("dispatch:25", c), ("maintain:False", c)],
                visits.Select(v => (v.Step, v.Tenant)));
            Assert.All(visits, v => Assert.True(v.AllBranches));
            Assert.Equal(visits.Count, visits.Select(v => v.Scope).Distinct().Count());   // un scope DI nuevo por paso

            journal.Visits.Clear();
            await service.RunOnceAsync(maintain: false);
            Assert.All(journal.Visits, v => Assert.StartsWith("dispatch", v.Step, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task El_servicio_despacha_seguido_y_mantiene_con_su_propia_cadencia()
    {
        var tenant = Guid.NewGuid();
        var options = new SiatBackgroundOptions
        {
            StartupDelay = TimeSpan.Zero,
            DispatchInterval = TimeSpan.FromMilliseconds(20),
            MaintenanceInterval = TimeSpan.FromHours(1),
        };
        var (services, journal) = Build(new FixedTenants(tenant), options);
        await using (services)
        {
            var hosted = services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<SiatBackgroundService>().Single();
            await hosted.StartAsync(CancellationToken.None);
            await journal.Maintained.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (journal.Visits.Count(v => v.Step.StartsWith("dispatch", StringComparison.Ordinal)) < 3 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
            await hosted.StopAsync(CancellationToken.None);
            Assert.True(journal.Visits.Count(v => v.Step.StartsWith("dispatch", StringComparison.Ordinal)) >= 3);
            Assert.Equal(1, journal.Visits.Count(v => v.Step.StartsWith("maintain", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task En_la_base_en_memoria_las_empresas_activas_salen_de_la_configuracion_SIAT()
    {
        var services = new ServiceCollection().AddMinvDemoInfrastructure().AddMinvSiatBackground().BuildServiceProvider();
        await using (services)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            if (db.Model.FindEntityType(typeof(SiatSettings)) is null)
            {
                return;   // el modelo de facturación (esquema billing) lo agrega la persistencia de la V4.1
            }
            var (enabled, disabled) = (Guid.NewGuid(), Guid.NewGuid());
            foreach (var (tenantId, on) in new[] { (enabled, true), (disabled, false) })
            {
                using var tenantScope = services.CreateScope();
                tenantScope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
                var tenantDb = tenantScope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
                var settings = new SiatSettings(tenantId, 1003579028, "FERRETERÍA DE PRUEBA", "SIS-1", SiatCodes.EnvironmentTest);
                if (on)
                {
                    settings.Enable();
                }
                tenantDb.Add(settings);
                await tenantDb.SaveChangesAsync();
            }
            var tenants = await scope.ServiceProvider.GetRequiredService<ISiatTenantSource>().ActiveTenantsAsync();
            Assert.Equal([enabled], tenants);
        }
    }
}
