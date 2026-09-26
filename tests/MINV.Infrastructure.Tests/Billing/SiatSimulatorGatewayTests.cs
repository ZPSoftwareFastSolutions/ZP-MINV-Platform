using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Infrastructure.Billing;
using MINV.Infrastructure.Billing.Simulator;
using MINV.Infrastructure.Billing.Soap;
using MINV.Infrastructure.Persistence;
using MINV.Tests.Siat;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>V4.1 · Gateway sobre el simulador en proceso (demostración) y registro de la facturación en el contenedor.</summary>
public sealed class SiatSimulatorGatewayTests
{
    private static readonly SiatPlace Main = new(0, 0);

    private readonly FakeTime _time = new(SiatTestKit.Start);
    private readonly RecordingCallLog _log = new();
    private readonly SiatSimulatorEngine _engine;
    private readonly InProcessSiatGateway _gateway;

    public SiatSimulatorGatewayTests()
    {
        _engine = new SiatSimulatorEngine(new SiatSimulatorOptions(), _time);
        _gateway = new InProcessSiatGateway(_engine, _log, new TestClock(_time));
    }

    private static SiatConnection Connection(string token = SiatTestKit.Token) => SiatTestKit.Connection(token: token);

    [Fact]
    public async Task Emite_y_anula_una_factura_de_punta_a_punta_con_la_bitacora_sin_token()
    {
        var connection = Connection();
        Assert.Equal(SiatCodes.CommunicationOk, (await _gateway.CheckCommunicationAsync(connection, SiatResource.PurchaseSale)).StatusCode);
        var cuis = (await _gateway.RequestCuisAsync(connection, Main)).Code!;
        var cufd = await _gateway.RequestCufdAsync(connection, Main, cuis);
        var clock = await _gateway.SyncClockAsync(connection, Main, cuis);
        Assert.Equal(SiatTestKit.Bolivia(_time.Now), clock.SiatTime);
        Assert.True((await _gateway.VerifyNitAsync(connection, Main, cuis, 1003579028)).IsValid);
        foreach (var catalog in SiatCatalogNames.All.Where(c => c != SiatCatalogNames.DateTime))
        {
            Assert.NotEmpty((await _gateway.SyncCatalogAsync(connection, Main, cuis, catalog)).Rows);
        }

        var xml = SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, SiatTestKit.Bolivia(_time.Now), 1);
        var gzip = SiatTestKit.Gzip(xml);
        var codes = new SiatCodesForCall(cuis, cufd.Code!);
        var sent = await _gateway.SendDocumentAsync(connection, Main, codes, SiatDocumentRef.PurchaseSale, gzip, SiatTestKit.Sha256(gzip),
            SiatTestKit.Bolivia(_time.Now).AddTicks(1234));
        Assert.Equal(SiatCodes.ReceptionValidated, sent.StatusCode);
        var cuf = XDocument.Parse(xml).Root!.Element("cabecera")!.Element("cuf")!.Value;
        Assert.Equal(SiatCodes.VoidConfirmed, (await _gateway.VoidDocumentAsync(connection, Main, codes, SiatDocumentRef.PurchaseSale, cuf, 1)).StatusCode);
        Assert.Equal("ANULADA", (await _gateway.CheckDocumentStatusAsync(connection, Main, codes, SiatDocumentRef.PurchaseSale, cuf)).StatusDescription);
        Assert.Equal(SiatCodes.RevertConfirmed, (await _gateway.RevertVoidAsync(connection, Main, codes, SiatDocumentRef.PurchaseSale, cuf)).StatusCode);

        Assert.All(_log.Records, r =>
        {
            Assert.DoesNotContain(SiatTestKit.Token, r.RequestBody ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(SiatTestKit.Token, r.ResponseBody ?? string.Empty, StringComparison.Ordinal);
        });
        var reception = _log.Records.Single(r => r.Operation == "recepcionFactura");
        Assert.Equal((SiatResource.PurchaseSale, SiatCodes.ReceptionValidated, true), (reception.Resource, reception.SiatCode, reception.Succeeded));
        Assert.Contains("[GZIP en base64", reception.RequestBody!, StringComparison.Ordinal);
        Assert.Contains("<codigoEstado>908</codigoEstado>", reception.ResponseBody!, StringComparison.Ordinal);
        Assert.Contains(_log.Records, r => r.Operation == "anulacionFactura" && r.SiatCode == SiatCodes.VoidConfirmed);
    }

    [Fact]
    public async Task Token_ausente_token_rechazado_y_simulador_apagado_se_comportan_como_el_cliente_real()
    {
        Assert.Equal("siat.no_token", (await Assert.ThrowsAsync<DomainException>(() => _gateway.RequestCuisAsync(Connection(""), Main))).Code);
        var rejected = await Assert.ThrowsAsync<DomainException>(() => _gateway.RequestCuisAsync(Connection("corto"), Main));
        Assert.Equal("siat.token_rejected", rejected.Code);
        Assert.False(_log.Records.Last().Succeeded);

        _engine.Available = false;
        await Assert.ThrowsAsync<SiatUnavailableException>(() => _gateway.CheckCommunicationAsync(Connection(), SiatResource.Codes));
        var failed = _log.Records.Last();
        Assert.False(failed.Succeeded);
        Assert.NotNull(failed.Error);
        _engine.Available = true;
        Assert.True((await _gateway.CheckCommunicationAsync(Connection(), SiatResource.Codes)).Transaction);
        await Assert.ThrowsAsync<DomainException>(() => _gateway.SendPackageAsync(Connection(), Main, new SiatCodesForCall("A", "B"),
            SiatDocumentRef.CreditDebitNote, [1], "h", DateTime.Now, 1, "1", null));
    }

    [Fact]
    public async Task El_registro_arma_el_modo_simulado_en_proceso_y_el_modo_SOAP()
    {
        var simulated = new ServiceCollection().AddMinvDemoInfrastructure()
            .AddMinvSiat(new SiatOptions { Mode = SiatGatewayMode.InProcessSimulator }).BuildServiceProvider();
        await using (simulated)
        {
            using var scope = simulated.CreateScope();
            Assert.IsType<InProcessSiatGateway>(scope.ServiceProvider.GetRequiredService<ISiatGateway>());
            Assert.Same(simulated.GetRequiredService<SiatSimulatorEngine>(), simulated.GetRequiredService<SiatSimulatorEngine>());
            Assert.IsType<SiatCallLog>(scope.ServiceProvider.GetRequiredService<ISiatCallLog>());
        }
        var soap = new ServiceCollection().AddMinvDemoInfrastructure().AddMinvSiat().BuildServiceProvider();
        await using (soap)
        {
            using var scope = soap.CreateScope();
            Assert.IsType<SiatSoapGateway>(scope.ServiceProvider.GetRequiredService<ISiatGateway>());
            var client = soap.GetRequiredService<IHttpClientFactory>().CreateClient(SiatSoapGateway.HttpClientName);
            Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);   // el tiempo máximo es el del ambiente, por llamada
        }
    }

    [Fact]
    public async Task La_bitacora_tecnica_nunca_interrumpe_la_llamada()
    {
        var services = new ServiceCollection().AddMinvDemoInfrastructure()
            .AddMinvSiat(new SiatOptions { Mode = SiatGatewayMode.InProcessSimulator }).BuildServiceProvider();
        await using (services)
        {
            using var scope = services.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.Set(Guid.NewGuid());
            var gateway = scope.ServiceProvider.GetRequiredService<ISiatGateway>();
            var reply = await gateway.RequestCuisAsync(Connection(), Main);
            Assert.True(reply.Transaction);
            // Cuando el modelo de datos incluye la bitácora (esquema billing), la llamada quedó guardada y sin el token
            await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MinvWriteDbContext>>().CreateDbContextAsync();
            if (db.Model.FindEntityType(typeof(SiatServiceCall)) is not null)
            {
                var call = await db.Set<SiatServiceCall>().SingleAsync();
                Assert.Equal(("cuis", "FacturacionCodigos"), (call.Operation, call.Resource));
                Assert.DoesNotContain(SiatTestKit.Token, call.RequestBody!, StringComparison.Ordinal);
            }
        }
    }
}
