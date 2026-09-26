using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Iam;
using MINV.Domain.Billing;
using MINV.Domain.Iam;
using MINV.Infrastructure.Billing;
using MINV.Infrastructure.Billing.Simulator;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// V4.1 (agente H) · <c>minv datos-prueba</c> CON facturación contra PostgreSQL real (se ejecuta con <c>MINV_TEST_PG</c>):
/// la carga factura con el simulador del SIN en proceso (archivo de estado como en tools\bd_local.ps1) y todo lo que
/// escribe respeta los CHECK, las FK compuestas con la sucursal, los índices únicos de numeración (también los del
/// talonario CAFC), los triggers append-only de la facturación y la partida doble; los totales derivados de la vista
/// <c>billing.v_fiscal_document_totals</c> son los cobrados y el simulador HTTP que lea el estado conoce lo emitido.
/// </summary>
public sealed class H_SeedBillingPostgresTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    [PostgresFact]
    public async Task V41_los_datos_de_prueba_con_facturacion_respetan_todas_las_restricciones_de_PostgreSQL()
    {
        var stateFile = Path.Combine(Path.GetTempPath(), $"minv-siat-prueba-{Guid.NewGuid():N}.json");
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<DemoClock>();
            services.AddSingleton<IClock>(sp => sp.GetRequiredService<DemoClock>());
            services.AddSingleton<ISecretProtector>(AesGcmSecretProtector.Ephemeral());   // la clave maestra de este equipo
            services.AddMinvApplication();
            services.AddMinvInfrastructure(pg.ConnectionString);
            services.AddMinvSiat(new SiatOptions
            {
                Mode = SiatGatewayMode.InProcessSimulator,
                Simulator = new SiatSimulatorOptions { StateFile = stateFile },
            });
            await using var provider = services.BuildServiceProvider();
            var result = await provider.GetRequiredService<LocalDataSeeder>()
                .SeedAsync(new SeedOptions("FACTPG", Days: 14, Seed: 13), _ => { });
            var billing = Assert.IsType<SeedBilling>(result.Billing);
            Assert.True(billing.ValidInvoices > 20, $"Solo {billing.ValidInvoices} facturas válidas");
            Assert.Equal(3, billing.CafcInvoices);
            Assert.Equal(2, billing.CreditNotes);
            Assert.Equal(1, billing.Reverted);
            Assert.Equal(stateFile, billing.SimulatorStateFile);

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            async Task<int> Count(string sql) => await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
            Assert.Equal(0, await Count("SELECT count(*)::int AS \"Value\" FROM inventory.v_conservation_breaches"));
            Assert.Equal(0, await Count("SELECT count(*)::int AS \"Value\" FROM inventory.v_transfer_breaches"));
            Assert.Equal(0, await Count("SELECT count(*)::int AS \"Value\" FROM (SELECT journal_entry_id FROM accounting.journal_lines " +
                                        "GROUP BY journal_entry_id HAVING sum(debit) <> sum(credit)) x"));
            // Totales fiscales DERIVADOS (vista) = importe cobrado de cada venta con factura válida
            Assert.Equal(0, await Count("SELECT count(*)::int AS \"Value\" FROM billing.v_fiscal_document_totals t " +
                                        "JOIN billing.fiscal_documents d ON d.id = t.document_id " +
                                        "JOIN sales.payments p ON p.invoice_id = d.invoice_id " +
                                        "WHERE t.kind = 'Invoice' AND t.status = 'Valid' AND t.total_amount <> p.amount"));
            // Un documento vigente por venta y numeración sin huecos rotos por el talonario CAFC
            Assert.Equal(0, await Count("SELECT count(*)::int AS \"Value\" FROM (SELECT invoice_id FROM billing.fiscal_documents " +
                                        "WHERE invoice_id IS NOT NULL AND status IN ('Valid', 'Pending', 'Offline', 'InPackage') " +
                                        "GROUP BY invoice_id HAVING count(*) > 1) x"));
            Assert.Equal(3, await Count("SELECT count(*)::int AS \"Value\" FROM billing.fiscal_documents WHERE cafc IS NOT NULL AND status = 'Valid'"));
            Assert.True(await Count("SELECT count(*)::int AS \"Value\" FROM billing.fiscal_document_events") > billing.Documents);
            Assert.True(await Count("SELECT count(*)::int AS \"Value\" FROM billing.siat_service_calls") > billing.Documents);
            Assert.True(await Count("SELECT count(*)::int AS \"Value\" FROM billing.siat_service_calls WHERE request_body ILIKE '%" +
                                    billing.SiatToken + "%'") == 0);   // la bitácora SOAP nunca guarda el token (F-12)
            // Append-only de la facturación: la bitácora de un documento no se puede borrar
            await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(
                "DELETE FROM billing.fiscal_document_events WHERE id = (SELECT id FROM billing.fiscal_document_events LIMIT 1)"));

            // Las pantallas de facturación con el administrador sobre PostgreSQL (numéricos redondeados, RLS por sucursal)
            var admin = result.Users.First(u => u.RoleCode == RoleCodes.Admin);
            using var session = provider.CreateScope();
            var m = session.ServiceProvider.GetRequiredService<IMediator>();
            await m.Send(new LoginCommand("FACTPG", admin.Email, admin.Password, "pruebas", "4.1.0"));
            var status = await m.Send(new GetSiatStatusQuery());
            Assert.All(status.Points, p => Assert.Equal(SiatConnectionMode.Online, p.Mode));
            var documents = await m.Send(new GetFiscalDocumentsQuery(billing.From, result.To));
            Assert.NotNull(await m.Send(new GetFiscalDocumentQuery(documents[0].Id)));
            Assert.NotEmpty((await m.Send(new RenderFiscalDocumentQuery(documents[0].Id))).Content);
            var book = await m.Send(new GetSalesBookQuery(result.To.Year, result.To.Month));
            Assert.True(book.Total > 0 && book.TaxDebit > 0);
            Assert.NotNull(await m.Send(new GetTaxSummaryQuery(result.To.Year, result.To.Month)));
            Assert.NotEmpty((await m.Send(new ExportFiscalBookQuery(result.To.Year, result.To.Month, false, "xlsx"))).Content);
            Assert.NotEmpty(await m.Send(new GetSignificantEventsQuery(billing.From, result.To)));
            Assert.NotEmpty(await m.Send(new GetFiscalPackagesQuery()));
            Assert.NotEmpty(await m.Send(new GetSiatServiceCallsQuery(billing.From, result.To, null, 20)));

            // El simulador HTTP que arranque con ese archivo conoce los CUIS, los CUFD y los documentos emitidos
            var http = new SiatSimulatorEngine(new SiatSimulatorOptions { StateFile = stateFile, AcceptedTokens = [billing.SiatToken] });
            var simulated = http.Status();
            Assert.True(simulated.Documents >= billing.ValidInvoices);
            Assert.True(simulated.Cuis >= 8 && simulated.Cufds >= 8 && simulated.PointsOfSale == 5 && simulated.Packages >= 2);
            Assert.True(http.IsTokenAccepted(billing.SiatToken));
        }
        finally
        {
            foreach (var file in new[] { stateFile, stateFile + ".tmp" })
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
