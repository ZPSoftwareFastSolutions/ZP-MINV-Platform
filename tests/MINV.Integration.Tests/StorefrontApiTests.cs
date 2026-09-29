using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MINV.ApiGateway;
using MINV.ApiGateway.Background;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Sales;
using MINV.Application.Tech;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Services;

namespace MINV.Integration.Tests;

/// <summary>
/// V6 · API pública de tienda (<c>/storefront/v1</c>) sobre el gateway REAL (Kestrel, base en memoria, empresa NUBE): catálogo,
/// producto, imagen, CORS, reservar (idempotente), insuficiente (409), consultar y cancelar con el teléfono, vencimiento por el
/// trabajo en segundo plano y la venta en caja que consume la reserva (regla S-10, recorrido de punta a punta).
/// </summary>
public sealed class StorefrontApiTests(ApiGatewayFixture server) : IClassFixture<ApiGatewayFixture>
{
    private const string Sku = "CASE-COR-4000D";   // gabinete sin serie con existencia en la casa matriz

    private HttpClient Client() => server.CreateClient();

    /// <summary>Hora del servidor (el reloj simulado de la carga de prueba, no el del equipo): la vigencia de una reserva se mide
    /// contra él.</summary>
    private DateTimeOffset Now => server.Services.GetRequiredService<IClock>().UtcNow;

    private static object Reservation(string phone = "71234567", string name = "Valentina Aguirre", int quantity = 1, string sku = Sku, string? email = null) => new
    {
        lines = new[] { new { sku, quantity, slot = "case" } },
        contact = new { name, phone, email },
        notes = "Pruebas de integración",
    };

    private static HttpRequestMessage Post(string url, object body, string key) =>
        new(HttpMethod.Post, url) { Content = JsonContent.Create(body), Headers = { { "Idempotency-Key", key } } };

    [Fact]
    public async Task El_catalogo_es_publico_viene_de_la_base_y_respeta_CORS()
    {
        var http = Client();
        var response = await http.GetAsync("/storefront/v1/catalog");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("max-age=30", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        var catalog = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NUBE", catalog.GetProperty("company").GetProperty("code").GetString());
        Assert.Equal("CM", catalog.GetProperty("branch").GetProperty("code").GetString());
        var products = catalog.GetProperty("products").EnumerateArray().ToList();
        Assert.InRange(products.Count, 150, 165);   // el catálogo de Tech Zone Gaming (159) con precio en la lista
        var product = products.Single(p => p.GetProperty("sku").GetString() == Sku);
        Assert.Equal(Sku.ToLowerInvariant(), product.GetProperty("slug").GetString());
        Assert.Equal($"/storefront/v1/products/{Sku}/image", product.GetProperty("image").GetString());
        Assert.Equal(product.GetProperty("onHand").GetDecimal() - product.GetProperty("reserved").GetDecimal(), product.GetProperty("available").GetDecimal());
        Assert.True(product.GetProperty("price").GetDecimal() > 0);
        Assert.NotEmpty(product.GetProperty("specs").EnumerateArray());
        Assert.False(string.IsNullOrEmpty(product.GetProperty("description").GetString()));
        Assert.Contains(catalog.GetProperty("categories").EnumerateArray(), c => c.GetProperty("code").GetString() == "COMP" && c.GetProperty("parent").ValueKind == JsonValueKind.Null
                                                                                 && c.GetProperty("icon").GetString() == "Cpu" && c.GetProperty("productCount").GetInt32() > 0);
        Assert.NotEmpty(catalog.GetProperty("brands").EnumerateArray());
        var presets = catalog.GetProperty("presets").EnumerateArray().ToList();
        // Los armados sugeridos publicados por la carga de prueba (S-09) DE LA SUCURSAL DE LA TIENDA (el principal técnico solo ve CM)
        using (var scope = server.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var admin = server.Seed.Users.First(u => u.RoleCode == RoleCodes.Admin);
            await mediator.Send(new LoginCommand("NUBE", admin.Email, admin.Password, "PRUEBAS", "test"));
            var published = (await mediator.Send(new GetPcBuildsQuery())).Where(b => b.PublishedToWeb && b.BranchCode == "CM").Select(b => b.Number).Order().ToList();
            Assert.Equal(published, presets.Select(p => p.GetProperty("number").GetString()!).Order());
        }
        Assert.NotEmpty(presets);
        Assert.All(presets, p => Assert.NotEmpty(p.GetProperty("lines").EnumerateArray()));
        // Nada fuera del contrato: ni costos ni clientes
        Assert.DoesNotContain("cost", catalog.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.False(product.TryGetProperty("customer", out _));

        // CORS: solo el origen configurado
        using var allowed = new HttpRequestMessage(HttpMethod.Get, "/storefront/v1/presets") { Headers = { { "Origin", "http://localhost:5173" } } };
        var allowedResponse = await http.SendAsync(allowed);
        Assert.Equal("http://localhost:5173", allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        using var other = new HttpRequestMessage(HttpMethod.Get, "/storefront/v1/presets") { Headers = { { "Origin", "https://otro.example" } } };
        var otherResponse = await http.SendAsync(other);
        Assert.False(otherResponse.Headers.Contains("Access-Control-Allow-Origin"));
        // La API B2B sigue exigiendo su llave
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/v1/catalog")).StatusCode);
    }

    [Fact]
    public async Task El_producto_y_su_imagen_se_sirven_con_cache()
    {
        var http = Client();
        var product = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku.ToLowerInvariant()}");
        Assert.Equal(Sku, product.GetProperty("sku").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/storefront/v1/products/no-existe")).StatusCode);

        var image = await http.GetAsync($"/storefront/v1/products/{Sku}/image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(image.Headers.ETag);
        Assert.Contains("max-age=3600", image.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/storefront/v1/products/{Sku}/image");
        conditional.Headers.IfNoneMatch.Add(image.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await http.SendAsync(conditional)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/storefront/v1/products/NO-EXISTE/image")).StatusCode);
    }

    [Fact]
    public async Task Reservar_es_idempotente_reserva_stock_y_el_cliente_consulta_y_cancela_con_su_telefono()
    {
        var http = Client();
        var before = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku}");
        var key = "web-" + Guid.NewGuid().ToString("N");
        var created = await http.SendAsync(Post("/storefront/v1/reservations", Reservation(), key));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reservation = await created.Content.ReadFromJsonAsync<JsonElement>();
        var number = reservation.GetProperty("number").GetString()!;
        Assert.StartsWith("ARM-WEB-", number, StringComparison.Ordinal);
        Assert.Equal("Reserved", reservation.GetProperty("status").GetString());
        Assert.Equal("Reservada", reservation.GetProperty("statusText").GetString());
        Assert.Equal("Valentina Aguirre", reservation.GetProperty("contactName").GetString());
        Assert.Equal("CM", reservation.GetProperty("branch").GetString());
        var until = reservation.GetProperty("reservedUntil").GetDateTimeOffset();
        Assert.InRange((until - Now).TotalHours, 47, 49);
        var line = Assert.Single(reservation.GetProperty("lines").EnumerateArray());
        Assert.Equal((Sku, "case", 1), (line.GetProperty("sku").GetString(), line.GetProperty("slot").GetString(), line.GetProperty("quantity").GetInt32()));
        Assert.Equal(line.GetProperty("subtotal").GetDecimal(), reservation.GetProperty("total").GetDecimal());
        Assert.Equal($"/storefront/v1/reservations/{number}", created.Headers.Location?.ToString());
        Assert.DoesNotContain("71234567", reservation.GetRawText(), StringComparison.Ordinal);   // el teléfono no se devuelve (S-06)

        // La disponibilidad fresca baja en 1 y lo reservado sube en 1
        var after = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku}");
        Assert.Equal(before.GetProperty("available").GetDecimal() - 1, after.GetProperty("available").GetDecimal());
        Assert.Equal(before.GetProperty("reserved").GetDecimal() + 1, after.GetProperty("reserved").GetDecimal());
        Assert.Equal(before.GetProperty("onHand").GetDecimal(), after.GetProperty("onHand").GetDecimal());

        // Idempotencia: la misma llave devuelve la misma reserva; la misma llave con otro contenido se rechaza
        var again = await http.SendAsync(Post("/storefront/v1/reservations", Reservation(), key));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(number, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("number").GetString());
        var conflict = await http.SendAsync(Post("/storefront/v1/reservations", Reservation(quantity: 2), key));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, conflict.StatusCode);
        Assert.Equal("idempotency", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());

        // Consulta: número Y teléfono (con o sin +591); con otro teléfono, no existe
        var status = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/reservations/{number}?phone=%2B591%2071234567");
        Assert.Equal("Reserved", status.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/storefront/v1/reservations/{number}?phone=79999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/storefront/v1/reservations/ARM-WEB-999999?phone=71234567")).StatusCode);

        // Cancelar devuelve el stock
        var cancelled = await http.PostAsJsonAsync($"/storefront/v1/reservations/{number}/cancel", new { phone = "71234567" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var view = await cancelled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("Cancelled", "Cancelada"), (view.GetProperty("status").GetString(), view.GetProperty("statusText").GetString()));
        var restored = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku}");
        Assert.Equal(before.GetProperty("available").GetDecimal(), restored.GetProperty("available").GetDecimal());
        var twice = await http.PostAsJsonAsync($"/storefront/v1/reservations/{number}/cancel", new { phone = "71234567" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, twice.StatusCode);
    }

    [Fact]
    public async Task Sin_stock_suficiente_no_se_reserva_nada_y_se_informa_que_falta()
    {
        var http = Client();
        var catalog = await http.GetFromJsonAsync<JsonElement>("/storefront/v1/catalog");
        var products = catalog.GetProperty("products").EnumerateArray().ToList();
        var scarce = products.Where(p => p.GetProperty("available").GetDecimal() is > 0 and < 16 && p.GetProperty("category").GetString() == "CASE")
            .OrderBy(p => p.GetProperty("available").GetDecimal()).First();
        var plenty = products.Single(p => p.GetProperty("sku").GetString() == Sku);
        var available = (int)scarce.GetProperty("available").GetDecimal();
        var body = new
        {
            lines = new[]
            {
                new { sku = Sku, quantity = 1, slot = "peripherals" },
                new { sku = scarce.GetProperty("sku").GetString()!, quantity = available + 1, slot = "case" },
            },
            contact = new { name = "Mateo Condori", phone = "76543210" },
        };
        var response = await http.SendAsync(Post("/storefront/v1/reservations", body, "web-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("storefront.insufficient_stock", problem.GetProperty("code").GetString());
        var shortage = Assert.Single(problem.GetProperty("shortages").EnumerateArray());
        Assert.Equal(scarce.GetProperty("sku").GetString(), shortage.GetProperty("sku").GetString());
        Assert.Equal(available + 1, shortage.GetProperty("requested").GetInt32());
        Assert.Equal(available, shortage.GetProperty("available").GetDecimal());
        // Todo o nada: el gabinete que sí alcanzaba no quedó reservado
        var unchanged = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku}");
        Assert.Equal(plenty.GetProperty("reserved").GetDecimal(), unchanged.GetProperty("reserved").GetDecimal());

        // Contacto inválido: teléfono corto (regla del dominio) y sin nombre (validación)
        var badPhone = await http.SendAsync(Post("/storefront/v1/reservations", Reservation(phone: "123"), "web-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badPhone.StatusCode);
        Assert.Equal("pcbuild.contact_phone", (await badPhone.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var noName = await http.SendAsync(Post("/storefront/v1/reservations", Reservation(name: ""), "web-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        // Sin llave de idempotencia
        var noKey = await http.PostAsJsonAsync("/storefront/v1/reservations", Reservation());
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
    }

    [Fact]
    public async Task Una_reserva_vencida_la_cierra_el_trabajo_en_segundo_plano_y_el_stock_vuelve()
    {
        var http = Client();
        var before = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku}");
        var created = await http.SendAsync(Post("/storefront/v1/reservations", Reservation(phone: "+591 70011223", name: "Lucía Rojas"), "web-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var number = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("number").GetString()!;
        var clock = server.Services.GetRequiredService<DemoClock>();
        var real = clock.UtcNow;
        try
        {
            // Antes de vencer no pasa nada; 49 horas después, el trabajo la cierra
            var expiry = new StorefrontReservationExpiryService(server.Services.GetRequiredService<IServiceScopeFactory>(),
                server.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<MINV.ApiGateway.Security.StorefrontSettings>>(),
                server.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorefrontReservationExpiryService>>());
            var still = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/reservations/{number}?phone=70011223");
            Assert.Equal("Reserved", still.GetProperty("status").GetString());
            clock.StartAt(real.AddHours(49));
            var pending = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/reservations/{number}?phone=70011223");
            Assert.Equal("Expired", pending.GetProperty("status").GetString());   // se muestra vencida, pero no se cierra «al leer»
            Assert.True(await expiry.RunOnceAsync(default) >= 1);
            var expired = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/reservations/{number}?phone=70011223");
            Assert.Equal(("Expired", "Vencida", PcBuild.ExpiredReason), (expired.GetProperty("status").GetString(), expired.GetProperty("statusText").GetString(),
                expired.GetProperty("cancelReason").GetString()));
            var restored = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku}");
            Assert.Equal(before.GetProperty("available").GetDecimal(), restored.GetProperty("available").GetDecimal());
        }
        finally
        {
            clock.StartAt(real);
        }
    }

    [Fact]
    public async Task La_venta_en_caja_consume_la_reserva_y_la_web_ve_la_unidad_vendida()
    {
        var http = Client();
        var before = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku}");
        var created = await http.SendAsync(Post("/storefront/v1/reservations", Reservation(phone: "72223334", name: "Diego Mamani"), "web-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var number = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("number").GetString()!;

        // El escritorio ve la reserva web (canal, contacto, vence) y la vende en la caja de la casa matriz
        using var scope = server.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var cashier = server.Seed.Users.First(u => u.RoleCode == RoleCodes.Cashier && u.Branches == "CM");
        await mediator.Send(new LoginCommand("NUBE", cashier.Email, cashier.Password, "PRUEBAS", "test"));
        var rows = await mediator.Send(new GetPcBuildsQuery(PcBuildStatus.Reserved, PcBuildChannel.Web));
        var row = rows.Single(r => r.Number == number);
        Assert.Equal(("Diego Mamani", "72223334", 1m), (row.ContactName, row.ContactPhone, row.Reserved));   // el cajero tiene sales.pcbuild.manage
        Assert.NotNull(row.ReservedUntil);
        CheckoutResult sale;
        try
        {
            sale = await mediator.Send(new SellPcBuildCommand(number, "EFECTIVO", null, 100000m));
        }
        catch (DomainException ex) when (ex.Code == "pos.closed")
        {
            await mediator.Send(new OpenPosSessionCommand("CAJA03", 500));
            sale = await mediator.Send(new SellPcBuildCommand(number, "EFECTIVO", null, 100000m));
        }
        Assert.StartsWith("F-CM-", sale.InvoiceNumber, StringComparison.Ordinal);

        // La web: la reserva quedó vendida y la unidad ya no está disponible (se descontó UNA sola vez)
        var sold = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/reservations/{number}?phone=72223334");
        Assert.Equal(("Sold", "Vendida"), (sold.GetProperty("status").GetString(), sold.GetProperty("statusText").GetString()));
        var after = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku}");
        Assert.Equal(before.GetProperty("onHand").GetDecimal() - 1, after.GetProperty("onHand").GetDecimal());
        Assert.Equal(before.GetProperty("reserved").GetDecimal(), after.GetProperty("reserved").GetDecimal());
        Assert.Equal(before.GetProperty("available").GetDecimal() - 1, after.GetProperty("available").GetDecimal());
        // El tablero cuenta las reservas web vigentes y la bitácora del armado tiene cada paso
        var detail = await mediator.Send(new GetPcBuildQuery(number));
        Assert.Equal([PcBuildEventAction.Created, PcBuildEventAction.Quoted, PcBuildEventAction.Reserved, PcBuildEventAction.Sold],
            detail.History!.Select(h => h.Action));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.PostAsJsonAsync($"/storefront/v1/reservations/{number}/cancel", new { phone = "72223334" })).StatusCode);
    }
}

/// <summary>Gateway con límites bajos para probar el 429 de la tienda sin afectar a las demás pruebas (otra instancia, otro puerto).</summary>
public sealed class StorefrontLimitsFixture : SeededServer
{
    protected override string[] ExtraArgs => ["--Minv:Storefront:ReadsPerMinute", "5", "--Minv:Storefront:ReservationsPerMinute", "2"];

    protected override SeedOptions Options => new("NUBE", Days: 2, Seed: 11, Billing: false);

    protected override WebApplication Build(string[] args) => ApiGatewayApp.Build(args);
}

public sealed class StorefrontLimitsTests(StorefrontLimitsFixture server) : IClassFixture<StorefrontLimitsFixture>
{
    [Fact]
    public async Task Las_lecturas_y_las_reservas_tienen_su_limite_por_IP()
    {
        var http = server.CreateClient();
        // 2 reservas por minuto: la tercera se rechaza (aunque las anteriores fueran inválidas: el límite corre antes)
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/storefront/v1/reservations")
            {
                Content = JsonContent.Create(new { lines = Array.Empty<object>(), contact = new { name = "x", phone = "71234567" } }),
                Headers = { { "Idempotency-Key", "limite-" + i } },
            };
            statuses.Add((await http.SendAsync(request)).StatusCode);
        }
        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests], statuses);
        // 5 lecturas por minuto (las 3 reservas anteriores también contaron en el presupuesto global de la tienda)
        var reads = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            reads.Add((await http.GetAsync("/storefront/v1/presets")).StatusCode);
        }
        Assert.Contains(HttpStatusCode.TooManyRequests, reads);
        // El resto del gateway no se ve afectado
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
    }
}
