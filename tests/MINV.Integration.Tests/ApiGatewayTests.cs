using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Iam;
using MINV.Application.Integration;
using MINV.Application.Sales;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Infrastructure.Integration;

namespace MINV.Integration.Tests;

public sealed class ApiGatewayTests(ApiGatewayFixture server) : IClassFixture<ApiGatewayFixture>
{
    private HttpClient Client(string? token = null)
    {
        var http = server.CreateClient();
        if (token is not null)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return http;
    }

    private static object Order(string externalId, string sku = "FER-001", decimal quantity = 1) => new
    {
        externalId,
        customerCode = "CF",
        paymentMethodCode = "QR",
        paymentReference = "QR-" + externalId,
        lines = new[] { new { sku, quantity } },
    };

    [Fact]
    public async Task Sin_API_Key_valida_responde_401_y_la_documentacion_es_publica()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync("/v1/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client("minv_abcdefgh_" + new string('x', 43)).GetAsync("/v1/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client().GetAsync("/health")).StatusCode);
        var openApi = await Client().GetStringAsync("/docs/v1/openapi.json");
        Assert.Contains("/v1/orders", openApi, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Con_la_llave_de_la_tienda_lee_catalogo_y_stock_de_su_sucursal()
    {
        var http = Client(server.Seed.ApiKeyToken);
        var catalog = await http.GetFromJsonAsync<JsonElement>("/v1/catalog?pageSize=500");
        Assert.Equal(61, catalog.GetProperty("total").GetInt32());
        var stock = await http.GetFromJsonAsync<JsonElement>("/v1/stock?pageSize=500");
        Assert.All(stock.GetProperty("items").EnumerateArray(), i => Assert.Equal("CM", i.GetProperty("branchCode").GetString()));
        // La llave no tiene el alcance transfers:read ni webhooks:manage
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/v1/transfers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/v1/webhooks")).StatusCode);
    }

    [Fact]
    public async Task Los_pedidos_son_idempotentes_por_externalId()
    {
        var http = Client(server.Seed.ApiKeyToken);
        var created = await http.PostAsJsonAsync("/v1/orders", Order("PED-IDEM-1"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = await created.Content.ReadFromJsonAsync<JsonElement>();
        var again = await http.PostAsJsonAsync("/v1/orders", Order("PED-IDEM-1"));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues("Idempotent-Replayed").Single());
        var second = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first.GetProperty("invoiceNumber").GetString(), second.GetProperty("invoiceNumber").GetString());
        Assert.StartsWith("F-CM-", first.GetProperty("invoiceNumber").GetString(), StringComparison.Ordinal);

        var conflict = await http.PostAsJsonAsync("/v1/orders", Order("PED-IDEM-1", quantity: 2));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, conflict.StatusCode);
        var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("idempotency", problem.GetProperty("title").GetString());

        var invalid = await http.PostAsJsonAsync("/v1/orders", new { externalId = "PED-X", customerCode = "CF", paymentMethodCode = "QR", lines = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Los_webhooks_llegan_firmados_con_el_secreto_del_destino()
    {
        // Receptor local (el despachador de pruebas admite destinos privados)
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/hook/");
        listener.Start();

        // El administrador registra el webhook (el secreto se muestra una sola vez)
        CreatedWebhook webhook;
        using (var scope = server.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var admin = server.Seed.Users.First(u => u.RoleCode == RoleCodes.Admin);
            await mediator.Send(new LoginCommand("NUBE", admin.Email, admin.Password, "PRUEBAS", "test"));
            webhook = await mediator.Send(new CreateWebhookCommand($"http://localhost:{port}/hook/", [IntegrationEvents.SaleCompleted], "Prueba"));
        }
        Assert.StartsWith("whsec_", webhook.Secret, StringComparison.Ordinal);

        // Un pedido del e-commerce produce sale.completed en el outbox (misma transacción que la venta)
        var http = Client(server.Seed.ApiKeyToken);
        Assert.Equal(HttpStatusCode.Created, (await http.PostAsJsonAsync("/v1/orders", Order("PED-HOOK-1"))).StatusCode);

        // El receptor responde en paralelo (el despachador espera la respuesta antes de seguir)
        var receive = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            using var reader = new StreamReader(context.Request.InputStream);
            var text = await reader.ReadToEndAsync();
            var header = context.Request.Headers[WebhookDispatcher.SignatureHeader]!;
            context.Response.StatusCode = 204;
            context.Response.Close();
            return (Body: text, Signature: header);
        });
        var summary = await server.Services.GetRequiredService<WebhookDispatcher>().RunOnceAsync(100, default);
        if (summary.Delivered == 0)
        {
            using var scope = server.Services.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var admin = server.Seed.Users.First(u => u.RoleCode == RoleCodes.Admin);
            await mediator.Send(new LoginCommand("NUBE", admin.Email, admin.Password, "PRUEBAS", "test"));
            var errors = (await mediator.Send(new GetWebhookDeliveriesQuery())).Select(d => $"{d.Url}: {d.StatusCode} {d.Error}");
            Assert.Fail($"Entregas: {summary.Delivered}, fallidas {summary.Failed} · {string.Join(" | ", errors)}");
        }
        var (body, signature) = await receive.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(ApiKeyTokens.Verify(webhook.Secret, signature, body, DateTimeOffset.UtcNow, TimeSpan.FromDays(3650)));
        Assert.False(ApiKeyTokens.Verify("whsec_otro", signature, body, DateTimeOffset.UtcNow, TimeSpan.FromDays(3650)));
        using var json = JsonDocument.Parse(body);
        Assert.Equal(IntegrationEvents.SaleCompleted, json.RootElement.GetProperty("type").GetString());
        Assert.Equal(SaleChannels.Api, json.RootElement.GetProperty("data").GetProperty("channel").GetString());
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
