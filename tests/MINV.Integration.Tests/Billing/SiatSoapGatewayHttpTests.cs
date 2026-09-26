using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Infrastructure.Billing.Simulator;
using MINV.Infrastructure.Billing.Soap;
using MINV.SiatSimulator;
using MINV.Tests.Siat;

namespace MINV.Integration.Tests.Billing;

/// <summary>Simulador HTTP del SIN real (Kestrel en un puerto de loopback), como en producción pero sin token del SIN.</summary>
public sealed class SiatSimulatorServer : IAsyncLifetime
{
    public WebApplication App { get; private set; } = null!;

    public Uri BaseAddress { get; private set; } = null!;

    public SiatSimulatorEngine Engine => App.Services.GetRequiredService<SiatSimulatorEngine>();

    public async Task InitializeAsync()
    {
        App = SiatSimulatorApp.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Warning"]);
        await App.StartAsync();
        BaseAddress = new Uri(App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
    }

    public HttpClient CreateClient() => new() { BaseAddress = BaseAddress };

    public async Task DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

/// <summary>V4.1 · Cliente SOAP del SIN contra el simulador HTTP: todas las operaciones del puerto de punta a punta, token
/// ausente o rechazado, simulador apagado y tiempo agotado.</summary>
public sealed class SiatSoapGatewayHttpTests(SiatSimulatorServer server) : IClassFixture<SiatSimulatorServer>
{
    private static readonly SiatPlace Main = new(0, 0);

    private readonly RecordingCallLog _log = new();

    /// <summary>Quita la cabecera apikey antes de salir (simula un cliente mal configurado).</summary>
    private sealed class StripApiKey() : DelegatingHandler(new SocketsHttpHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Remove(SiatSoapContract.ApiKeyHeader);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private SiatSoapGateway Gateway(HttpMessageHandler? handler = null) => new(new TestHttpClientFactory(handler), _log, new TestClock());

    private SiatConnection Connection(long nit, string token = SiatTestKit.Token, TimeSpan? timeout = null) =>
        SiatTestKit.Connection(server.BaseAddress.ToString().TrimEnd('/'), token, timeout, nit);

    private static DateTime BoliviaNow => SiatTestKit.Bolivia(DateTimeOffset.UtcNow);

    private static string CufOf(string xml) => XDocument.Parse(xml).Root!.Element("cabecera")!.Element("cuf")!.Value;

    [Fact]
    public async Task Todas_las_operaciones_del_puerto_funcionan_de_punta_a_punta()
    {
        const long nit = 4_000_000_001;
        var c = Connection(nit);
        var gateway = Gateway();

        foreach (var resource in Enum.GetValues<SiatResource>())
        {
            Assert.Equal(SiatCodes.CommunicationOk, (await gateway.CheckCommunicationAsync(c, resource)).StatusCode);
        }

        // Códigos y operaciones: CUIS de la sucursal, punto de venta 1, su CUIS y su CUFD
        var cuis0 = await gateway.RequestCuisAsync(c, Main);
        Assert.True(cuis0.Transaction);
        Assert.True(cuis0.ValidUntil > DateTimeOffset.UtcNow.AddDays(364));
        var registered = await gateway.RegisterPointOfSaleAsync(c, Main, cuis0.Code!, SiatCodes.PointOfSaleCashier, "CAJA 1", "Caja de pruebas");
        Assert.Equal(1, registered.Code);
        var listed = Assert.Single((await gateway.ListPointsOfSaleAsync(c, Main, cuis0.Code!)).Items);
        Assert.Equal(new SiatPointOfSaleInfo(1, "CAJA 1", SiatCodes.PointOfSaleCashier), listed);
        var place = new SiatPlace(0, 1);
        var cuis = (await gateway.RequestCuisAsync(c, place)).Code!;
        var cufd = await gateway.RequestCufdAsync(c, place, cuis);
        Assert.True(cufd.Transaction);
        Assert.Matches("^[0-9A-F]{15}$", cufd.ControlCode!);
        Assert.Equal("AV. SIMULADA N° 0", cufd.Address);
        Assert.Equal(TimeSpan.FromHours(-4), cufd.ValidUntil!.Value.Offset);
        var clock = await gateway.SyncClockAsync(c, place, cuis);
        Assert.InRange(clock.SiatTime!.Value, BoliviaNow.AddMinutes(-1), BoliviaNow.AddMinutes(1));
        var nitCheck = await gateway.VerifyNitAsync(c, place, cuis, 1003579028);
        Assert.Equal((true, SiatCodes.NitActive), (nitCheck.IsValid, nitCheck.Code!.Value));
        Assert.Equal(SiatCodes.NitNotFound, (await gateway.VerifyNitAsync(c, place, cuis, 12)).Code);

        // Sincronización de los 17 catálogos (la fecha y hora va aparte)
        foreach (var catalog in SiatCatalogNames.All.Where(n => n != SiatCatalogNames.DateTime))
        {
            var reply = await gateway.SyncCatalogAsync(c, place, cuis, catalog);
            Assert.True(reply.Transaction && reply.Rows.Count > 0, catalog);
            Assert.Equal(SiatSimulatorCatalogs.Rows(catalog), reply.Rows);   // el cable no cambia los datos
        }

        // Factura en línea: recepción, estado, anulación, reversión
        var codes = new SiatCodesForCall(cuis, cufd.Code!);
        var invoice = SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, BoliviaNow, 1, pointOfSale: 1, nit: nit);
        var gzip = SiatTestKit.Gzip(invoice);
        var sent = await gateway.SendDocumentAsync(c, place, codes, SiatDocumentRef.PurchaseSale, gzip, SiatTestKit.Sha256(gzip), BoliviaNow);
        Assert.Equal((true, SiatCodes.ReceptionValidated), (sent.Transaction, sent.StatusCode!.Value));
        Assert.True(Guid.TryParse(sent.ReceptionCode, out _));
        var status = await gateway.CheckDocumentStatusAsync(c, place, codes, SiatDocumentRef.PurchaseSale, CufOf(invoice));
        Assert.Equal((SiatCodes.ReceptionValidated, sent.ReceptionCode), (status.StatusCode!.Value, status.ReceptionCode));
        Assert.Equal(SiatCodes.VoidConfirmed,
            (await gateway.VoidDocumentAsync(c, place, codes, SiatDocumentRef.PurchaseSale, CufOf(invoice), 1)).StatusCode);
        Assert.Equal("ANULADA", (await gateway.CheckDocumentStatusAsync(c, place, codes, SiatDocumentRef.PurchaseSale, CufOf(invoice))).StatusDescription);
        var twice = await gateway.VoidDocumentAsync(c, place, codes, SiatDocumentRef.PurchaseSale, CufOf(invoice), 1);
        Assert.True(!twice.Transaction && twice.Has(SiatCodes.AlreadyVoided));
        Assert.Equal(SiatCodes.RevertConfirmed, (await gateway.RevertVoidAsync(c, place, codes, SiatDocumentRef.PurchaseSale, CufOf(invoice))).StatusCode);
        Assert.True((await gateway.RevertVoidAsync(c, place, codes, SiatDocumentRef.PurchaseSale, CufOf(invoice))).Has(968));

        // Rechazo: el hash no es el de los bytes enviados
        var rejected = await gateway.SendDocumentAsync(c, place, codes, SiatDocumentRef.PurchaseSale, gzip, new string('0', 64), BoliviaNow);
        Assert.Equal((false, SiatCodes.ReceptionRejected), (rejected.Transaction, rejected.StatusCode!.Value));
        Assert.Equal(969, Assert.Single(rejected.Messages).Code);

        // Nota crédito-débito por Documentos de Ajuste
        var note = SiatTestKit.Note(cufd.Code!, cufd.ControlCode!, BoliviaNow, 1, pointOfSale: 1, nit: nit);
        var noteGzip = SiatTestKit.Gzip(note);
        Assert.Equal(SiatCodes.ReceptionValidated, (await gateway.SendDocumentAsync(c, place, codes, SiatDocumentRef.CreditDebitNote, noteGzip,
            SiatTestKit.Sha256(noteGzip), BoliviaNow)).StatusCode);
        Assert.Equal(SiatCodes.VoidConfirmed,
            (await gateway.VoidDocumentAsync(c, place, codes, SiatDocumentRef.CreditDebitNote, CufOf(note), 2)).StatusCode);

        // Fuera de línea: documentos con el CUFD del evento → CUFD nuevo → evento → paquete → validación
        var start = BoliviaNow.AddHours(-2);
        var offline = new[]
        {
            SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, start.AddMinutes(10), 2, pointOfSale: 1, emission: SiatCodes.EmissionOffline, nit: nit),
            SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, start.AddMinutes(20), 3, pointOfSale: 1, emission: SiatCodes.EmissionOffline, nit: nit),
        };
        var newCufd = await gateway.RequestCufdAsync(c, place, cuis);
        var evt = await gateway.RegisterEventAsync(c, place, cuis, newCufd.Code!, 2, "INACCESIBILIDAD AL SERVICIO WEB DE LA ADMINISTRACIÓN TRIBUTARIA",
            start, start.AddHours(1), cufd.Code!);
        Assert.True(evt.Transaction);
        var newCodes = new SiatCodesForCall(cuis, newCufd.Code!);
        var package = SiatTestKit.Package(offline);
        var packageReply = await gateway.SendPackageAsync(c, place, newCodes, SiatDocumentRef.PurchaseSale, package, SiatTestKit.Sha256(package),
            BoliviaNow, offline.Length, evt.ReceptionCode!, null);
        Assert.Equal(SiatCodes.ReceptionPending, packageReply.StatusCode);
        var validation = await gateway.ValidatePackageAsync(c, place, newCodes, SiatDocumentRef.PurchaseSale, packageReply.ReceptionCode!);
        Assert.Equal((true, SiatCodes.ReceptionValidated), (validation.Transaction, validation.StatusCode!.Value));
        Assert.Equal(SiatCodes.ReceptionValidated,
            (await gateway.CheckDocumentStatusAsync(c, place, newCodes, SiatDocumentRef.PurchaseSale, CufOf(offline[1]))).StatusCode);

        // Cierre del punto de venta (con el CUIS de la sucursal)
        Assert.True((await gateway.ClosePointOfSaleAsync(c, place, cuis0.Code!)).Transaction);

        // La bitácora registró cada llamada, sin el token
        Assert.True(_log.Records.Count >= 40);
        Assert.All(_log.Records, r =>
        {
            Assert.Equal(200, r.HttpStatus);
            Assert.DoesNotContain(SiatTestKit.Token, r.RequestBody!, StringComparison.Ordinal);
            Assert.DoesNotContain(SiatTestKit.Token, r.ResponseBody!, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Sin_la_cabecera_apikey_o_con_un_token_no_aceptado_el_error_es_de_token()
    {
        using var http = server.CreateClient();
        var envelope = SiatSoapEnvelope.Serialize(SiatSoapRequests.Cuis(Connection(1_234_567), Main).Envelope(SiatSoapContract.DefaultNamespace));
        using var raw = await http.PostAsync("/v2/FacturacionCodigos", new StringContent(envelope, Encoding.UTF8, "text/xml"));
        Assert.Equal(HttpStatusCode.Unauthorized, raw.StatusCode);

        using var strip = new StripApiKey();
        var missing = await Assert.ThrowsAsync<DomainException>(() => Gateway(strip).RequestCuisAsync(Connection(1_234_567), Main));
        Assert.Equal("siat.token_rejected", missing.Code);
        Assert.Equal(401, _log.Records.Last().HttpStatus);

        var invalid = await Assert.ThrowsAsync<DomainException>(() => Gateway().RequestCuisAsync(Connection(1_234_567, "corto"), Main));
        Assert.Equal("siat.token_rejected", invalid.Code);
        Assert.DoesNotContain("corto", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Con_el_simulador_apagado_no_hay_comunicacion()
    {
        using var http = server.CreateClient();
        var c = Connection(4_000_000_002);
        try
        {
            using (var off = await http.PostAsync("/control/offline?on=true", null))
            {
                off.EnsureSuccessStatusCode();
            }
            Assert.False(server.Engine.Available);
            var error = await Assert.ThrowsAsync<SiatUnavailableException>(() => Gateway().CheckCommunicationAsync(c, SiatResource.PurchaseSale));
            Assert.Contains("503", error.Message, StringComparison.Ordinal);
            using var state = JsonDocument.Parse(await http.GetStringAsync("/control/estado"));
            Assert.False(state.RootElement.GetProperty("available").GetBoolean());
        }
        finally
        {
            using var on = await http.PostAsync("/control/offline?on=false", null);
        }
        Assert.True(server.Engine.Available);
        Assert.True((await Gateway().CheckCommunicationAsync(c, SiatResource.PurchaseSale)).Transaction);
        using var health = await http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Pasado_el_tiempo_maximo_de_espera_no_hay_comunicacion()
    {
        var c = Connection(4_000_000_003, timeout: TimeSpan.FromMilliseconds(400));
        server.Engine.Latency = TimeSpan.FromSeconds(3);
        try
        {
            var error = await Assert.ThrowsAsync<SiatUnavailableException>(() => Gateway().RequestCuisAsync(c, Main));
            Assert.Contains("tiempo agotado", error.Message, StringComparison.Ordinal);
            Assert.Null(_log.Records.Last().HttpStatus);
        }
        finally
        {
            server.Engine.Latency = TimeSpan.Zero;
        }
        Assert.True((await Gateway().RequestCuisAsync(c, Main)).Transaction);
    }

    [Fact]
    public async Task Cada_recurso_publica_una_descripcion_minima_y_las_rutas_desconocidas_no_existen()
    {
        using var http = server.CreateClient();
        var wsdl = await http.GetStringAsync("/v2/ServicioFacturacionCompraVenta?wsdl");
        Assert.Contains("recepcionPaqueteFactura", wsdl, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/v2/NoExiste?wsdl")).StatusCode);
        using var unknown = await http.PostAsync("/v2/NoExiste", new StringContent("<x/>", Encoding.UTF8, "text/xml"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        // Una operación que el recurso no publica (paquetes en Documentos de Ajuste) es una falla SOAP
        var c = Connection(4_000_000_004);
        var package = SiatSoapRequests.SendPackage(c, Main, new SiatCodesForCall("A", "B"), SiatDocumentRef.PurchaseSale, [1], "h", BoliviaNow, 1, "1", null);
        var body = SiatSoapEnvelope.Serialize(package.Envelope(SiatSoapContract.DefaultNamespace));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v2/ServicioFacturacionDocumentoAjuste")
        {
            Content = new StringContent(body, Encoding.UTF8, "text/xml"),
        };
        request.Headers.Add(SiatSoapContract.ApiKeyHeader, SiatSoapContract.ApiKeyValue(SiatTestKit.Token));
        using var fault = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, fault.StatusCode);
        Assert.NotNull(SiatSoapReader.FaultText(await fault.Content.ReadAsStringAsync()));
    }
}
