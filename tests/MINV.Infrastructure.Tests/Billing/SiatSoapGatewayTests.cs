using System.Net;
using System.Text;
using System.Xml.Linq;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Infrastructure.Billing.Soap;
using MINV.Tests.Siat;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>Manejador HTTP de prueba: guarda cada solicitud (con su cuerpo) y responde lo que se le indique.</summary>
internal sealed class StubSoapHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public static StubSoapHandler Xml(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/xml") }));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return await respond(request, cancellationToken);
    }
}

/// <summary>V4.1 · Cliente SOAP del SIN: sobre, cabeceras, lectura tolerante, errores de comunicación y bitácora sin token.</summary>
public sealed class SiatSoapGatewayTests
{
    private const string Ns = "https://siat.impuestos.gob.bo/";
    private static readonly SiatPlace Main = new(0, 0);
    private static readonly SiatCodesForCall Codes = new("C2FC8F2F", "BQUE+QytqQUDBKVUFOSVRPQkxVRFZNVFVJBMDAwMDAwM");

    private readonly RecordingCallLog _log = new();

    private SiatSoapGateway Gateway(HttpMessageHandler handler) => new(new TestHttpClientFactory(handler), _log, new TestClock());

    private static string Envelope(string body, string prefix = "S") =>
        $"<?xml version=\"1.0\"?><{prefix}:Envelope xmlns:{prefix}=\"http://schemas.xmlsoap.org/soap/envelope/\"><{prefix}:Body>{body}</{prefix}:Body></{prefix}:Envelope>";

    [Fact]
    public async Task La_solicitud_es_SOAP_11_con_la_cabecera_apikey_y_la_bitacora_no_guarda_el_token()
    {
        var handler = StubSoapHandler.Xml(Envelope(
            $"<ns2:cuisResponse xmlns:ns2=\"{Ns}\"><RespuestaCuis><codigo>C2FC8F2F</codigo><fechaVigencia>2027-09-25T10:00:00.000-04:00</fechaVigencia>" +
            "<transaccion>true</transaccion></RespuestaCuis></ns2:cuisResponse>"));
        var reply = await Gateway(handler).RequestCuisAsync(SiatTestKit.Connection("https://pilotosiatservicios.example"), new SiatPlace(3, 1));

        Assert.True(reply.Transaction);
        Assert.Equal("C2FC8F2F", reply.Code);
        Assert.Equal(new DateTimeOffset(2027, 9, 25, 14, 0, 0, TimeSpan.Zero), reply.ValidUntil);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://pilotosiatservicios.example/v2/FacturacionCodigos", request.RequestUri!.ToString());
        Assert.Equal("TokenApi " + SiatTestKit.Token, Assert.Single(request.Headers.GetValues("apikey")));
        Assert.Equal("\"\"", Assert.Single(request.Headers.GetValues("SOAPAction")));
        Assert.Equal("text/xml", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", request.Content.Headers.ContentType.CharSet);

        var operation = XDocument.Parse(body).Descendants().Single(e => e.Name.LocalName == "Body").Elements().Single();
        Assert.Equal(XName.Get("cuis", Ns), operation.Name);
        var parameter = Assert.Single(operation.Elements());
        Assert.Equal(XName.Get("SolicitudCuis"), parameter.Name);   // parámetro y campos SIN namespace
        Assert.Equal(
            ["codigoAmbiente=2", "codigoModalidad=2", "codigoPuntoVenta=1", $"codigoSistema={SiatTestKit.SystemCode}", "codigoSucursal=3",
                $"nit={SiatTestKit.Nit}"],
            parameter.Elements().Select(e => $"{e.Name}={e.Value}"));

        var record = Assert.Single(_log.Records);
        Assert.Equal((SiatResource.Codes, "cuis", 200, true), (record.Resource, record.Operation, record.HttpStatus!.Value, record.Succeeded));
        Assert.Equal(new SiatPlace(3, 1), record.Place);
        Assert.DoesNotContain(SiatTestKit.Token, record.RequestBody!, StringComparison.Ordinal);
        Assert.Contains("C2FC8F2F", record.ResponseBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_lectura_es_tolerante_a_prefijos_envoltorios_y_nombres_de_listas()
    {
        var handler = StubSoapHandler.Xml(Envelope(
            "<x:cufdResponse xmlns:x=\"urn:otro\"><return><codigoCUFD>BQUE123</codigoCUFD><codigoControl>A19E23EF34124CD</codigoControl>" +
            "<direccion>AV. JORGE LOPEZ #123</direccion><fechaVigencia>2026-09-26T10:00:00.000</fechaVigencia>" +
            "<codigosRespuestas><item><codigo>3008</codigo><descripcion>Advertencia: El Cuis Esta A Punto De Caducar</descripcion></item></codigosRespuestas>" +
            "<transaccion>1</transaccion></return></x:cufdResponse>", "soapenv"));
        var reply = await Gateway(handler).RequestCufdAsync(SiatTestKit.Connection(), Main, "C2FC8F2F");
        Assert.True(reply.Transaction);
        Assert.Equal(("BQUE123", "A19E23EF34124CD", "AV. JORGE LOPEZ #123"), (reply.Code, reply.ControlCode, reply.Address));
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.FromHours(-4)), reply.ValidUntil);   // sin zona = hora de Bolivia
        var warning = Assert.Single(reply.Messages);
        Assert.Equal(SiatCodes.CuisAboutToExpire, warning.Code);
        Assert.True(warning.IsWarning);
    }

    [Fact]
    public async Task La_respuesta_de_los_servicios_de_facturacion_trae_estado_recepcion_y_mensajes_por_archivo()
    {
        var handler = StubSoapHandler.Xml(Envelope(
            $"<ns2:validacionRecepcionPaqueteFacturaResponse xmlns:ns2=\"{Ns}\"><RespuestaServicioFacturacion>" +
            "<codigoDescripcion>OBSERVADA</codigoDescripcion><codigoEstado>904</codigoEstado><codigoRecepcion>abc-123</codigoRecepcion>" +
            "<mensajesList><codigo>1040</codigo><descripcion>Fecha fuera del evento</descripcion><numeroArchivo>3</numeroArchivo></mensajesList>" +
            "<mensajesList><codigo>1018</codigo><descripcion>Subtotal</descripcion><numeroArchivo>4</numeroArchivo><numeroDetalle>2</numeroDetalle></mensajesList>" +
            "<transaccion>true</transaccion></RespuestaServicioFacturacion></ns2:validacionRecepcionPaqueteFacturaResponse>"));
        var reply = await Gateway(handler).ValidatePackageAsync(SiatTestKit.Connection(), Main, Codes, SiatDocumentRef.PurchaseSale, "abc-123");
        Assert.Equal((true, 904, "OBSERVADA", "abc-123"), (reply.Transaction, reply.StatusCode!.Value, reply.StatusDescription, reply.ReceptionCode));
        Assert.Equal([new SiatMessage(1040, "Fecha fuera del evento", 3), new SiatMessage(1018, "Subtotal", 4, 2)], reply.Messages);
        Assert.Equal(904, Assert.Single(_log.Records).SiatCode);

        var body = XDocument.Parse(handler.Requests[0].Body);
        Assert.Equal("ServicioFacturacionCompraVenta", handler.Requests[0].Request.RequestUri!.Segments[^1]);
        var fields = body.Descendants().Single(e => e.Name.LocalName == "SolicitudServicioValidacionRecepcionPaquete").Elements()
            .ToDictionary(e => e.Name.LocalName, e => e.Value);
        Assert.Equal(("2", "1", "1", "abc-123"), (fields["codigoEmision"], fields["codigoDocumentoSector"], fields["tipoFacturaDocumento"],
            fields["codigoRecepcion"]));
    }

    [Fact]
    public async Task Los_catalogos_se_leen_con_cualquiera_de_las_dos_formas_de_lista()
    {
        var handler = StubSoapHandler.Xml(Envelope(
            $"<ns2:sincronizarParametricaTipoMetodoPagoResponse xmlns:ns2=\"{Ns}\"><RespuestaListaParametricas>" +
            "<listaCodigos><codigoClasificador>1</codigoClasificador><descripcion>EFECTIVO</descripcion></listaCodigos>" +
            "<listaCodigos><codigoClasificador>2</codigoClasificador><descripcion>TARJETA</descripcion></listaCodigos>" +
            "<transaccion>true</transaccion></RespuestaListaParametricas></ns2:sincronizarParametricaTipoMetodoPagoResponse>"));
        var methods = await Gateway(handler).SyncCatalogAsync(SiatTestKit.Connection(), Main, "C2FC8F2F", SiatCatalogNames.PaymentMethods);
        Assert.Equal([new SiatCatalogRow("1", "EFECTIVO"), new SiatCatalogRow("2", "TARJETA")], methods.Rows);
        Assert.Equal(XName.Get("sincronizarParametricaTipoMetodoPago", Ns),
            XDocument.Parse(handler.Requests[0].Body).Descendants().Single(e => e.Name.LocalName == "Body").Elements().Single().Name);

        var products = StubSoapHandler.Xml(Envelope(
            $"<ns2:sincronizarListaProductosServiciosResponse xmlns:ns2=\"{Ns}\"><RespuestaListaProductos><listaCodigos>" +
            "<item><codigoActividad>4752100</codigoActividad><codigoProducto>1001903</codigoProducto><descripcionProducto>alicates</descripcionProducto>" +
            "<nandina>8203.20</nandina></item></listaCodigos><transaccion>true</transaccion></RespuestaListaProductos>" +
            "</ns2:sincronizarListaProductosServiciosResponse>"));
        var rows = (await Gateway(products).SyncCatalogAsync(SiatTestKit.Connection(), Main, "C2FC8F2F", SiatCatalogNames.Products)).Rows;
        Assert.Equal(new SiatCatalogRow("1001903", "alicates", "4752100", "8203.20"), Assert.Single(rows));

        var clock = StubSoapHandler.Xml(Envelope(
            $"<ns2:sincronizarFechaHoraResponse xmlns:ns2=\"{Ns}\"><RespuestaFechaHora><fechaHora>2026-09-25T10:15:30.123</fechaHora>" +
            "<transaccion>true</transaccion></RespuestaFechaHora></ns2:sincronizarFechaHoraResponse>"));
        var time = await Gateway(clock).SyncClockAsync(SiatTestKit.Connection(), Main, "C2FC8F2F");
        Assert.Equal(new DateTime(2026, 9, 25, 10, 15, 30, 123), time.SiatTime);
        await Assert.ThrowsAsync<DomainException>(() =>
            Gateway(clock).SyncCatalogAsync(SiatTestKit.Connection(), Main, "C2FC8F2F", SiatCatalogNames.DateTime));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task HTTP_401_o_403_es_token_rechazado_y_no_falta_de_comunicacion(HttpStatusCode status)
    {
        var handler = StubSoapHandler.Xml("<html>no autorizado</html>", status);
        var error = await Assert.ThrowsAsync<DomainException>(() => Gateway(handler).CheckCommunicationAsync(SiatTestKit.Connection(), SiatResource.PurchaseSale));
        Assert.Equal("siat.token_rejected", error.Code);
        Assert.DoesNotContain(SiatTestKit.Token, error.Message, StringComparison.Ordinal);
        var record = Assert.Single(_log.Records);
        Assert.False(record.Succeeded);
        Assert.Equal((int)status, record.HttpStatus);
    }

    [Fact]
    public async Task El_mensaje_989_es_token_rechazado()
    {
        var handler = StubSoapHandler.Xml(Envelope(
            $"<ns2:cuisResponse xmlns:ns2=\"{Ns}\"><RespuestaCuis><mensajesList><codigo>989</codigo><descripcion>Token Invalido</descripcion></mensajesList>" +
            "<transaccion>false</transaccion></RespuestaCuis></ns2:cuisResponse>"));
        var error = await Assert.ThrowsAsync<DomainException>(() => Gateway(handler).RequestCuisAsync(SiatTestKit.Connection(), Main));
        Assert.Equal("siat.token_rejected", error.Code);
        Assert.False(Assert.Single(_log.Records).Succeeded);
    }

    public static TheoryData<int, string> NoCommunication => new()
    {
        { 500, Envelope("<S:Fault><faultcode>S:Server</faultcode><faultstring>Error interno del SIN</faultstring></S:Fault>") },
        { 503, "Servicio no disponible" },
        { 404, "<html>No encontrado</html>" },
        { 400, "<html>Solicitud incorrecta</html>" },
        { 200, "esto no es XML" },
        { 200, Envelope("<S:Fault><faultcode>S:Server</faultcode><faultstring>java.lang.NullPointerException</faultstring></S:Fault>") },
        { 200, "<html><body>portal</body></html>" },
    };

    [Theory]
    [MemberData(nameof(NoCommunication))]
    public async Task Las_fallas_HTTP_las_fallas_SOAP_y_las_respuestas_ilegibles_son_falta_de_comunicacion(int status, string body)
    {
        var handler = StubSoapHandler.Xml(body, (HttpStatusCode)status);
        var error = await Assert.ThrowsAsync<SiatUnavailableException>(() =>
            Gateway(handler).SendDocumentAsync(SiatTestKit.Connection(), Main, Codes, SiatDocumentRef.PurchaseSale, [1, 2, 3], "hash", DateTime.Now));
        Assert.DoesNotContain(SiatTestKit.Token, error.Message, StringComparison.Ordinal);
        var record = Assert.Single(_log.Records);
        Assert.False(record.Succeeded);
        Assert.NotNull(record.Error);
        Assert.Contains("[GZIP en base64", record.RequestBody!, StringComparison.Ordinal);   // el archivo no se guarda en la bitácora
    }

    [Fact]
    public async Task Un_error_de_red_es_falta_de_comunicacion()
    {
        var handler = new StubSoapHandler((_, _) => throw new HttpRequestException("No se puede establecer una conexión"));
        await Assert.ThrowsAsync<SiatUnavailableException>(() => Gateway(handler).RequestCuisAsync(SiatTestKit.Connection(), Main));
        Assert.Null(Assert.Single(_log.Records).HttpStatus);
    }

    [Fact]
    public async Task El_tiempo_agotado_es_falta_de_comunicacion_pero_la_cancelacion_del_llamador_no()
    {
        var slow = new StubSoapHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var connection = SiatTestKit.Connection(timeout: TimeSpan.FromMilliseconds(200));
        var error = await Assert.ThrowsAsync<SiatUnavailableException>(() => Gateway(slow).CheckCommunicationAsync(connection, SiatResource.Codes));
        Assert.Contains("tiempo agotado", error.Message, StringComparison.Ordinal);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Gateway(slow).CheckCommunicationAsync(SiatTestKit.Connection(), SiatResource.Codes, cancelled.Token));
    }

    [Fact]
    public async Task Sin_token_no_se_llama_y_las_notas_no_tienen_paquetes()
    {
        var handler = StubSoapHandler.Xml(Envelope("<x/>"));
        var gateway = Gateway(handler);
        Assert.Equal("siat.no_token",
            (await Assert.ThrowsAsync<DomainException>(() => gateway.RequestCuisAsync(SiatTestKit.Connection(token: " "), Main))).Code);
        Assert.Equal("siat.package_unsupported", (await Assert.ThrowsAsync<DomainException>(() => gateway.SendPackageAsync(SiatTestKit.Connection(), Main,
            Codes, SiatDocumentRef.CreditDebitNote, [1], "h", DateTime.Now, 1, "1000001", null))).Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Si_el_token_apareciera_en_una_respuesta_no_llega_a_la_bitacora()
    {
        var handler = StubSoapHandler.Xml(Envelope(
            $"<ns2:verificarComunicacionResponse xmlns:ns2=\"{Ns}\"><RespuestaComunicacion><mensajesList><codigo>926</codigo>" +
            $"<descripcion>eco {SiatTestKit.Token}</descripcion></mensajesList><transaccion>true</transaccion></RespuestaComunicacion>" +
            "</ns2:verificarComunicacionResponse>"));
        var reply = await Gateway(handler).CheckCommunicationAsync(SiatTestKit.Connection(), SiatResource.Adjustment);
        Assert.Equal(SiatCodes.CommunicationOk, reply.StatusCode);
        Assert.Equal("http://127.0.0.1:5095/v2/ServicioFacturacionDocumentoAjuste", handler.Requests[0].Request.RequestUri!.ToString());
        var record = Assert.Single(_log.Records);
        Assert.DoesNotContain(SiatTestKit.Token, record.ResponseBody!, StringComparison.Ordinal);
        Assert.Contains("***", record.ResponseBody!, StringComparison.Ordinal);
    }

    [Fact]
    public void El_contrato_centraliza_recursos_operaciones_y_la_cabecera_del_token()
    {
        Assert.Equal("TokenApi abc", SiatSoapContract.ApiKeyValue("abc"));
        Assert.Equal("abc", SiatSoapContract.TokenFrom("TokenApi abc"));
        Assert.Null(SiatSoapContract.TokenFrom("Bearer abc"));
        Assert.Null(SiatSoapContract.TokenFrom(null));
        Assert.Equal(SiatResource.Adjustment, SiatSoapContract.ResourceFromPath("/v2/ServicioFacturacionDocumentoAjuste/"));
        Assert.Equal(18, SiatSoapContract.SyncOperations.Count);
        Assert.All(SiatCatalogNames.All, catalog => Assert.NotNull(SiatSoapContract.SyncFor(catalog)));
        Assert.Contains("recepcionPaqueteFactura", SiatSoapContract.OperationsOf(SiatResource.PurchaseSale));
        Assert.DoesNotContain("recepcionPaqueteFactura", SiatSoapContract.OperationsOf(SiatResource.Adjustment));
        Assert.Contains("recepcionDocumentoAjuste", SiatSoapContract.OperationsOf(SiatResource.Adjustment));
        Assert.All(Enum.GetValues<SiatResource>(), r => Assert.Contains("verificarComunicacion", SiatSoapContract.OperationsOf(r)));
        var endpoints = SiatEndpointSet.ForBaseUrl("https://x.example");
        Assert.All(Enum.GetValues<SiatResource>(), r =>
            Assert.EndsWith("/v2/" + SiatSoapContract.ResourcePath(r), SiatSoapContract.Url(endpoints, r), StringComparison.Ordinal));
    }
}
