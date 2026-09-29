using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MINV.ApiGateway;
using MINV.Application.Iam;
using MINV.Application.Sales;
using MINV.Application.Tech;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;
using MINV.Infrastructure.Seeding;

namespace MINV.Integration.Tests;

/// <summary>
/// V7 · Carrito por la API pública de tienda (<c>POST /storefront/v1/reservations</c> con <c>kind = cart</c>) sobre el gateway
/// REAL (Kestrel, base en memoria, empresa NUBE): reservar cualquier producto sin ranura, días para recogerlo (<c>holdDays</c>),
/// datos para la factura que nunca se devuelven, idempotencia con los campos nuevos, 409 por falta de stock, validación de los
/// textos de una línea y el contrato anterior intacto (regla P-05).
/// </summary>
public sealed class StorefrontCartApiTests(ApiGatewayFixture server) : IClassFixture<ApiGatewayFixture>
{
    private HttpClient Client() => server.CreateClient();

    private static string Key() => "cart-" + Guid.NewGuid().ToString("N");

    private static HttpRequestMessage Post(object body, string key) =>
        new(HttpMethod.Post, "/storefront/v1/reservations") { Content = JsonContent.Create(body), Headers = { { "Idempotency-Key", key } } };

    private static async Task<IReadOnlyList<JsonElement>> ProductsAsync(HttpClient http, string category, decimal available)
    {
        var catalog = await http.GetFromJsonAsync<JsonElement>("/storefront/v1/catalog");
        return catalog.GetProperty("products").EnumerateArray()
            .Where(p => p.GetProperty("category").GetString() == category && p.GetProperty("available").GetDecimal() >= available)
            .OrderBy(p => p.GetProperty("sku").GetString(), StringComparer.Ordinal).ToList();
    }

    private static string Sku(JsonElement product) => product.GetProperty("sku").GetString()!;

    [Fact]
    public async Task Reservar_un_carrito_es_idempotente_reserva_stock_sin_ranura_y_no_devuelve_los_datos_de_factura()
    {
        var http = Client();
        var catalog = await http.GetFromJsonAsync<JsonElement>("/storefront/v1/catalog");
        Assert.Equal((48, 3), (catalog.GetProperty("reservationHours").GetInt32(), catalog.GetProperty("maxHoldDays").GetInt32()));
        var monitor = (await ProductsAsync(http, "MON", 1))[0];
        var game = (await ProductsAsync(http, "JUE", 2))[0];
        var before = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku(monitor)}");

        // Un monitor y un juego, sin ranura y sin pasar por el armador; 1 día para recogerlo y el CI para la factura
        object Body(int holdDays = 1, string number = "4567890", string? kind = "cart") => new
        {
            kind,
            holdDays,
            lines = new object[] { new { sku = Sku(monitor), quantity = 1 }, new { sku = Sku(game), quantity = 2 } },
            contact = new { name = "Valentina Aguirre", phone = "+591 71234567", email = "valentina.aguirre@correo.example" },
            buyer = new { documentType = 1, documentNumber = number, complement = "1a", name = "Valentina Aguirre Rojas" },
            notes = "Paso el sábado por la mañana",
        };
        var key = Key();
        var created = await http.SendAsync(Post(Body(), key));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reservation = await created.Content.ReadFromJsonAsync<JsonElement>();
        var number = reservation.GetProperty("number").GetString()!;
        Assert.StartsWith("RES-WEB-", number, StringComparison.Ordinal);
        Assert.Equal($"/storefront/v1/reservations/{number}", created.Headers.Location?.ToString());
        Assert.Equal(("cart", "Reserved", "Reservada", "CM"), (reservation.GetProperty("kind").GetString(), reservation.GetProperty("status").GetString(),
            reservation.GetProperty("statusText").GetString(), reservation.GetProperty("branch").GetString()));
        // V7 (B3) · Con correo de contacto, la confirmación queda encolada (mailQueued de verdad; sin correo, false: ver el contrato de la V6)
        Assert.True(reservation.GetProperty("mailQueued").GetBoolean());
        Assert.False(reservation.GetProperty("hasCompatibilityWarnings").GetBoolean());
        Assert.InRange((reservation.GetProperty("reservedUntil").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalHours, 23, 25);
        var lines = reservation.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(JsonValueKind.Null, l.GetProperty("slot").ValueKind));
        Assert.Equal([Sku(game), Sku(monitor)], lines.Select(l => l.GetProperty("sku").GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(lines.Sum(l => l.GetProperty("subtotal").GetDecimal()), reservation.GetProperty("total").GetDecimal());
        // Ni el teléfono, ni el correo, ni los datos para la factura salen en la respuesta (reglas S-06 y P-05)
        var raw = reservation.GetRawText();
        Assert.DoesNotContain("71234567", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("correo.example", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("4567890", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Rojas", raw, StringComparison.Ordinal);
        Assert.False(reservation.TryGetProperty("buyer", out _));

        // La disponibilidad fresca baja y lo reservado sube
        var after = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku(monitor)}");
        Assert.Equal(before.GetProperty("available").GetDecimal() - 1, after.GetProperty("available").GetDecimal());
        Assert.Equal(before.GetProperty("reserved").GetDecimal() + 1, after.GetProperty("reserved").GetDecimal());
        Assert.Equal(before.GetProperty("onHand").GetDecimal(), after.GetProperty("onHand").GetDecimal());

        // Idempotencia: la misma llave devuelve la misma reserva; con otros días, otro documento u otro tipo se rechaza
        var again = await http.SendAsync(Post(Body(), key));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues("Idempotent-Replayed").Single());
        var replayed = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((number, "cart"), (replayed.GetProperty("number").GetString(), replayed.GetProperty("kind").GetString()));
        foreach (var different in new[] { Body(holdDays: 2), Body(number: "4567891"), Body(kind: "build"), Body(kind: null) })
        {
            var conflict = await http.SendAsync(Post(different, key));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, conflict.StatusCode);
            Assert.Equal("idempotency", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        }
        var unchanged = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku(monitor)}");
        Assert.Equal(after.GetProperty("reserved").GetDecimal(), unchanged.GetProperty("reserved").GetDecimal());

        // El cliente consulta con el número Y el teléfono; el personal ve el carrito con su contacto y sus datos de factura
        var status = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/reservations/{number}?phone=71234567");
        Assert.Equal(("cart", "Reserved"), (status.GetProperty("kind").GetString(), status.GetProperty("status").GetString()));
        Assert.DoesNotContain("4567890", status.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/storefront/v1/reservations/{number}?phone=79999999")).StatusCode);
        using (var scope = server.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var cashier = server.Seed.Users.First(u => u.RoleCode == RoleCodes.Cashier && u.Branches == "CM");
            await mediator.Send(new LoginCommand("NUBE", cashier.Email, cashier.Password, "PRUEBAS", "test"));
            var row = (await mediator.Send(new GetPcBuildsQuery(PcBuildStatus.Reserved, PcBuildChannel.Web, PcBuildKind.Cart))).Single(r => r.Number == number);
            Assert.Equal((PcBuildKind.Cart, "Valentina Aguirre", "+59171234567", 3m), (row.Kind, row.ContactName, row.ContactPhone, row.Reserved));
            Assert.Equal((1, "4567890", "1A", "Valentina Aguirre Rojas"), (row.BuyerDocumentType, row.BuyerDocumentNumber, row.BuyerComplement, row.BuyerName));
            Assert.DoesNotContain(await mediator.Send(new GetPcBuildsQuery(Kind: PcBuildKind.Build)), r => r.Number == number);
        }

        // Cancelar devuelve el stock
        var cancelled = await http.PostAsJsonAsync($"/storefront/v1/reservations/{number}/cancel", new { phone = "71234567" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var view = await cancelled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("Cancelled", "cart"), (view.GetProperty("status").GetString(), view.GetProperty("kind").GetString()));
        var restored = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku(monitor)}");
        Assert.Equal(before.GetProperty("available").GetDecimal(), restored.GetProperty("available").GetDecimal());
    }

    [Fact]
    public async Task Los_dias_para_recoger_fijan_el_vencimiento_y_el_contrato_anterior_sigue_igual()
    {
        var http = Client();
        var game = (await ProductsAsync(http, "JUE", 6))[0];
        var cabinet = (await ProductsAsync(http, "CASE", 2))[0];
        object Cart(int? holdDays, string phone) => new
        {
            kind = "cart", holdDays, lines = new[] { new { sku = Sku(game), quantity = 1 } }, contact = new { name = "Lucía Rojas", phone },
        };
        async Task<double> HoursAsync(object body)
        {
            var response = await http.SendAsync(Post(body, Key()));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return (json.GetProperty("reservedUntil").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalHours;
        }
        Assert.InRange(await HoursAsync(Cart(1, "70011221")), 23, 25);
        Assert.InRange(await HoursAsync(Cart(2, "70011222")), 47, 49);
        Assert.InRange(await HoursAsync(Cart(3, "70011223")), 71, 73);
        Assert.InRange(await HoursAsync(Cart(null, "70011224")), 47, 49);          // sin días: las horas configuradas (48)

        // Fuera de 1 a 3 días, o con un tipo que no existe: dato inválido (400) y nada reservado
        var reserved = (await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku(game)}")).GetProperty("reserved").GetDecimal();
        foreach (var invalid in new[] { Cart(0, "70011225"), Cart(4, "70011225"), Cart(-1, "70011225") })
        {
            var response = await http.SendAsync(Post(invalid, Key()));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        }
        var unknown = await http.SendAsync(Post(new { kind = "pedido", lines = new[] { new { sku = Sku(game), quantity = 1 } }, contact = new { name = "Lucía Rojas", phone = "70011225" } }, Key()));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("pedido", (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(reserved, (await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku(game)}")).GetProperty("reserved").GetDecimal());

        // El contrato de la V6 (sin campos nuevos) sigue reservando un ARMADO: ARM-WEB, ranura deducida, 48 h; también con holdDays
        var legacy = await http.SendAsync(Post(new { lines = new[] { new { sku = Sku(cabinet), quantity = 1 } }, contact = new { name = "Diego Mamani", phone = "72223331" } }, Key()));
        Assert.Equal(HttpStatusCode.Created, legacy.StatusCode);
        var build = await legacy.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("ARM-WEB-", build.GetProperty("number").GetString(), StringComparison.Ordinal);
        Assert.Equal(("build", "case", false), (build.GetProperty("kind").GetString(), Assert.Single(build.GetProperty("lines").EnumerateArray()).GetProperty("slot").GetString(),
            build.GetProperty("mailQueued").GetBoolean()));
        Assert.InRange((build.GetProperty("reservedUntil").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalHours, 47, 49);
        var shortBuild = await http.SendAsync(Post(new
        {
            kind = "build", holdDays = 1, lines = new[] { new { sku = Sku(cabinet), quantity = 1, slot = "case" } }, contact = new { name = "Diego Mamani", phone = "72223332" },
        }, Key()));
        Assert.Equal(HttpStatusCode.Created, shortBuild.StatusCode);
        Assert.InRange(((await shortBuild.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reservedUntil").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalHours, 23, 25);

        // La documentación OpenAPI describe los campos nuevos
        var openApi = await http.GetStringAsync("/docs/v1/openapi.json");
        Assert.Contains("/storefront/v1/reservations", openApi, StringComparison.Ordinal);
        Assert.Contains("\"holdDays\"", openApi, StringComparison.Ordinal);
        Assert.Contains("\"buyer\"", openApi, StringComparison.Ordinal);
        Assert.Contains("\"kind\"", openApi, StringComparison.Ordinal);
        Assert.Contains("\"mailQueued\"", openApi, StringComparison.Ordinal);
        Assert.Contains("\"maxHoldDays\"", openApi, StringComparison.Ordinal);
        Assert.Contains("\"reservationHours\"", openApi, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_stock_suficiente_el_carrito_no_reserva_nada_y_los_datos_invalidos_se_rechazan()
    {
        var http = Client();
        var catalog = await http.GetFromJsonAsync<JsonElement>("/storefront/v1/catalog");
        var products = catalog.GetProperty("products").EnumerateArray().ToList();
        var scarce = products.Where(p => p.GetProperty("available").GetDecimal() is > 0 and < 16 && p.GetProperty("category").GetString() == "MON")
            .OrderBy(p => p.GetProperty("available").GetDecimal()).ThenBy(p => Sku(p), StringComparer.Ordinal).First();
        var plenty = (await ProductsAsync(http, "JUE", 2))[0];
        var available = (int)scarce.GetProperty("available").GetDecimal();
        var response = await http.SendAsync(Post(new
        {
            kind = "cart",
            lines = new[] { new { sku = Sku(plenty), quantity = 1 }, new { sku = Sku(scarce), quantity = available + 1 } },
            contact = new { name = "Mateo Condori", phone = "76543210" },
        }, Key()));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("storefront.insufficient_stock", problem.GetProperty("code").GetString());
        var shortage = Assert.Single(problem.GetProperty("shortages").EnumerateArray());
        Assert.Equal((Sku(scarce), available + 1, (decimal)available),
            (shortage.GetProperty("sku").GetString(), shortage.GetProperty("requested").GetInt32(), shortage.GetProperty("available").GetDecimal()));
        // Todo o nada: el juego que sí alcanzaba no quedó reservado
        var unchanged = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku(plenty)}");
        Assert.Equal(plenty.GetProperty("reserved").GetDecimal(), unchanged.GetProperty("reserved").GetDecimal());

        object Body(object? buyer = null, string notes = "Paso el sábado", string name = "Mateo Condori", string? slot = null) => new
        {
            kind = "cart", lines = new[] { new { sku = Sku(plenty), quantity = 1, slot } }, contact = new { name, phone = "76543210" }, buyer, notes,
        };
        async Task<(HttpStatusCode Status, string? Code)> SendAsync(object body)
        {
            var reply = await http.SendAsync(Post(body, Key()));
            var json = await reply.Content.ReadFromJsonAsync<JsonElement>();
            return (reply.StatusCode, json.TryGetProperty("code", out var code) ? code.GetString() : null);
        }
        // Textos de una línea (400): un salto de línea en las notas, en el nombre o en la razón social no entra
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(Body(notes: "Paso el sábado\r\nBcc: otro@correo.example"))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(Body(name: "Mateo\nCondori"))).Status);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(Body(new { documentType = 5, documentNumber = "1023456029", name = "Andina\r\nSubject: oferta" }))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(Body(new { documentType = 8, documentNumber = "123" }))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(Body(new { documentType = 1, documentNumber = "" }))).Status);
        // Reglas del SIN (422): CI y NIT solo dígitos; complemento solo con CI
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "buyer.doc_numeric"), await SendAsync(Body(new { documentType = 1, documentNumber = "45678-LP" })));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "buyer.complement"),
            await SendAsync(Body(new { documentType = 5, documentNumber = "1023456029", complement = "1A" })));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "pcbuild.slot"), await SendAsync(Body(slot: "teclado")));
        // Nada de lo rechazado reservó stock
        var still = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku(plenty)}");
        Assert.Equal(plenty.GetProperty("reserved").GetDecimal(), still.GetProperty("reserved").GetDecimal());
        // Con la ranura que manda la web, el carrito la conserva (dos gabinetes distintos caben en el mismo carrito)
        var cabinets = await ProductsAsync(http, "CASE", 1);
        var two = await http.SendAsync(Post(new
        {
            kind = "cart",
            lines = new[] { new { sku = Sku(cabinets[0]), quantity = 1, slot = "case" }, new { sku = Sku(cabinets[1]), quantity = 1, slot = "case" } },
            contact = new { name = "Mateo Condori", phone = "76543210" },
        }, Key()));
        Assert.Equal(HttpStatusCode.Created, two.StatusCode);
        var both = (await two.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(["case", "case"], both.Select(l => l.GetProperty("slot").GetString()));
    }

    [Fact]
    public async Task La_caja_vende_el_carrito_reservado_en_la_web_y_la_reserva_queda_vendida()
    {
        var http = Client();
        var game = (await ProductsAsync(http, "JUE", 3))[0];
        var before = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku(game)}");
        var created = await http.SendAsync(Post(new
        {
            kind = "cart", holdDays = 2, lines = new[] { new { sku = Sku(game), quantity = 2 } }, contact = new { name = "Diego Mamani", phone = "72223334" },
        }, Key()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var number = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("number").GetString()!;

        using var scope = server.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var cashier = server.Seed.Users.First(u => u.RoleCode == RoleCodes.Cashier && u.Branches == "CM");
        await mediator.Send(new LoginCommand("NUBE", cashier.Email, cashier.Password, "PRUEBAS", "test"));
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
        Assert.Equal(2 * game.GetProperty("price").GetDecimal(), sale.Total);

        var sold = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/reservations/{number}?phone=72223334");
        Assert.Equal(("Sold", "Vendida", "cart"), (sold.GetProperty("status").GetString(), sold.GetProperty("statusText").GetString(), sold.GetProperty("kind").GetString()));
        var after = await http.GetFromJsonAsync<JsonElement>($"/storefront/v1/products/{Sku(game)}");
        Assert.Equal(before.GetProperty("onHand").GetDecimal() - 2, after.GetProperty("onHand").GetDecimal());
        Assert.Equal(before.GetProperty("reserved").GetDecimal(), after.GetProperty("reserved").GetDecimal());
        var detail = await mediator.Send(new GetPcBuildQuery(number));
        Assert.Equal([PcBuildEventAction.Created, PcBuildEventAction.Quoted, PcBuildEventAction.Reserved, PcBuildEventAction.Sold], detail.History!.Select(h => h.Action));
        Assert.Equal((PcBuildKind.Cart, sale.InvoiceNumber), (detail.Build.Kind, detail.Build.InvoiceNumber));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.PostAsJsonAsync($"/storefront/v1/reservations/{number}/cancel", new { phone = "72223334" })).StatusCode);
    }
}

/// <summary>Gateway con el tope de las reservas en 24 horas (<c>Minv:Storefront:MaxReservationHours</c>): otra instancia, otro puerto.</summary>
public sealed class StorefrontShortHoldFixture : SeededServer
{
    protected override string[] ExtraArgs => ["--Minv:Storefront:MaxReservationHours", "24"];

    protected override SeedOptions Options => new("NUBE", Days: 2, Seed: 11, Billing: false);

    protected override WebApplication Build(string[] args) => ApiGatewayApp.Build(args);
}

public sealed class StorefrontShortHoldTests(StorefrontShortHoldFixture server) : IClassFixture<StorefrontShortHoldFixture>
{
    [Fact]
    public async Task El_tope_configurado_acota_los_dias_que_se_pueden_pedir()
    {
        var http = server.CreateClient();
        var catalog = await http.GetFromJsonAsync<JsonElement>("/storefront/v1/catalog");
        Assert.Equal((24, 1), (catalog.GetProperty("reservationHours").GetInt32(), catalog.GetProperty("maxHoldDays").GetInt32()));
        var sku = catalog.GetProperty("products").EnumerateArray()
            .Where(p => p.GetProperty("category").GetString() == "JUE" && p.GetProperty("available").GetDecimal() >= 2)
            .Select(p => p.GetProperty("sku").GetString()!).Order(StringComparer.Ordinal).First();
        async Task<HttpResponseMessage> ReserveAsync(int? holdDays)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/storefront/v1/reservations")
            {
                Content = JsonContent.Create(new { kind = "cart", holdDays, lines = new[] { new { sku, quantity = 1 } }, contact = new { name = "Ana Quispe", phone = "71234567" } }),
                Headers = { { "Idempotency-Key", "tope-" + Guid.NewGuid().ToString("N") } },
            };
            return await http.SendAsync(request);
        }
        // 2 días pasan del tope de 24 h: regla del servidor (422); 1 día y «sin días» reservan 24 h
        var tooLong = await ReserveAsync(2);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);
        Assert.Equal("storefront.hold_days", (await tooLong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        foreach (var days in new int?[] { 1, null })
        {
            var ok = await ReserveAsync(days);
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
            var until = (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reservedUntil").GetDateTimeOffset();
            Assert.InRange((until - DateTimeOffset.UtcNow).TotalHours, 23, 25);
        }
    }
}
