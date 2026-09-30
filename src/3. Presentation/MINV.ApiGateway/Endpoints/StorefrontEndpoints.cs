using MediatR;
using Microsoft.Net.Http.Headers;
using MINV.ApiGateway.Security;
using MINV.Application.Storefront;

namespace MINV.ApiGateway.Endpoints;

/// <summary>Reserva de un armado o de un carrito desde la tienda web (cuerpo de <c>POST /storefront/v1/reservations</c>).</summary>
/// <param name="Lines">Piezas: SKU, cantidad (1 a 16) y ranura de la web (opcional: en un armado se deduce de la ficha; en un
/// carrito la línea queda sin ranura).</param>
/// <param name="Contact">Nombre y teléfono (Bolivia: 7 u 8 dígitos, con o sin +591) obligatorios; correo opcional.</param>
/// <param name="Notes">Notas para la tienda (≤ 500, una sola línea).</param>
/// <param name="Name">Nombre de la reserva (por defecto «Armado web de &lt;nombre&gt;» o «Reserva de &lt;nombre&gt;»).</param>
/// <param name="IdempotencyKey">Alternativa a la cabecera <c>Idempotency-Key</c>.</param>
/// <param name="Kind">V7 · <c>"build"</c> (armado de PC, por defecto) o <c>"cart"</c> (carrito: cualquier producto).</param>
/// <param name="HoldDays">V7 · Días para recoger la reserva (1 a 3); sin valor, las horas configuradas en el servidor.</param>
/// <param name="Buyer">V7 · Datos para la factura (opcionales): tipo de documento (1 CI … 5 NIT), número, complemento y nombre o
/// razón social. Nunca se devuelven.</param>
public sealed record StorefrontReservationRequest(IReadOnlyList<StorefrontReservationLineInput> Lines, StorefrontContactInput Contact, string? Notes = null,
    string? Name = null, string? IdempotencyKey = null, string? Kind = null, int? HoldDays = null, ReservationBuyerInput? Buyer = null);

/// <summary>El cliente cancela su reserva con el teléfono con que la hizo (V7: sigue siendo obligatorio aunque la consulta admita
/// solo el código).</summary>
public sealed record StorefrontCancelRequest(string Phone);

/// <summary>
/// V6 · API pública de tienda (<c>/storefront/v1</c>, regla S-02): sin API Key, cada petición corre como el usuario técnico
/// de la tienda configurada (esquema <see cref="StorefrontAuthenticationHandler"/>), pasa por MediatR (validación, permisos,
/// auditoría), respeta los límites por IP (lecturas y reservas) y solo responde a los orígenes configurados (CORS). El contrato
/// JSON exacto está en <c>docs/integration/storefront-api-v1.md</c>.
/// </summary>
public static class StorefrontEndpoints
{
    public const string Prefix = "/storefront/v1";
    public const string ReadPolicy = "storefront";
    public const string ReservePolicy = "storefront-reserve";

    /// <summary>V7 · Consultas de una reserva (por código, por teléfono o con los dos) por minuto y por IP.</summary>
    public const string LookupPolicy = "storefront-lookup";
    public const string CorsPolicy = "storefront";
    public const string Tag = "Tienda web";

    public static void Map(WebApplication app)
    {
        var sf = app.MapGroup(Prefix).RequireAuthorization(StorefrontAuthenticationHandler.PolicyName).RequireCors(CorsPolicy)
            .RequireRateLimiting(ReadPolicy).WithTags(Tag);

        // ---------------------------------------------------------------------------------------------- catálogo
        sf.MapGet("/catalog", async Task<IResult> (ISender s, HttpContext http, CancellationToken ct) =>
            {
                var snapshot = await s.Send(new GetStorefrontCatalogQuery(), ct);
                http.Response.Headers.CacheControl = "public, max-age=30";
                return Results.Ok(snapshot);
            })
            .Produces<StorefrontCatalogView>()
            .WithSummary("Instantánea del catálogo web: empresa, sucursal de la tienda, categorías, marcas, productos con ficha y disponibilidad, armados publicados, " +
                         "horas de una reserva (reservationHours) y días que puede pedir quien reserva (maxHoldDays)");
        sf.MapGet("/products/{slug}", async Task<IResult> (ISender s, HttpContext http, string slug, CancellationToken ct) =>
            {
                var product = await s.Send(new GetStorefrontProductQuery(slug), ct);
                http.Response.Headers.CacheControl = "no-cache";
                return Results.Ok(product);
            })
            .Produces<StorefrontProduct>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Un producto por su slug (el SKU en minúsculas) con la disponibilidad fresca");
        sf.MapGet("/products/{sku}/image", async Task<IResult> (ISender s, HttpContext http, string sku, CancellationToken ct) =>
            {
                var image = await s.Send(new GetStorefrontProductImageQuery(sku), ct);
                http.Response.Headers.CacheControl = "public, max-age=3600";
                // Results.Bytes con ETag: responde 304 cuando If-None-Match coincide
                return Results.Bytes(image.Content, image.ContentType, entityTag: new EntityTagHeaderValue(image.ETag));
            })
            .WithSummary("Imagen del producto (PNG o JPEG) con ETag y caché de 1 hora");
        sf.MapGet("/presets", (ISender s, CancellationToken ct) => s.Send(new GetStorefrontPresetsQuery(), ct))
            .WithSummary("Armados sugeridos publicados desde el escritorio, con sus piezas a los precios cotizados");

        // ---------------------------------------------------------------------------------------------- reservas
        sf.MapPost("/reservations", async Task<IResult> (ISender s, HttpContext http, StorefrontReservationRequest body, CancellationToken ct) =>
            {
                var key = http.Request.Headers["Idempotency-Key"].ToString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = body.IdempotencyKey ?? string.Empty;
                }
                var result = await s.Send(new CreateStorefrontReservationCommand(body.Lines, body.Contact, body.Notes, key, body.Name,
                    StorefrontKinds.Parse(body.Kind), body.HoldDays, body.Buyer), ct);
                if (result.Replayed)
                {
                    http.Response.Headers["Idempotent-Replayed"] = "true";
                    return Results.Ok(result.Reservation);
                }
                return Results.Created($"{Prefix}/reservations/{Uri.EscapeDataString(result.Reservation.Number)}", result.Reservation);
            })
            .RequireRateLimiting(ReservePolicy)
            .Produces<StorefrontReservationView>(StatusCodes.Status201Created).Produces<StorefrontReservationView>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Reserva un armado (kind = build, ARM-WEB) o un carrito con cualquier producto (kind = cart, RES-WEB): el stock de cada línea queda " +
                         "reservado los días pedidos (holdDays 1 a 3; sin valor, 48 h). Idempotente por Idempotency-Key; 409 si falta stock");
        // V7 · Consulta con el código O el teléfono (regla S-06): el teléfono es opcional en la ruta de siempre y la colección se
        // filtra por teléfono; sin los dos, el contacto sale enmascarado. Límite propio por IP (LookupPolicy)
        sf.MapGet("/reservations/{number}", (ISender s, string number, string? phone, CancellationToken ct) =>
                s.Send(new GetStorefrontReservationQuery(number, phone), ct))
            .RequireRateLimiting(LookupPolicy)
            .Produces<StorefrontReservationView>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Estado de una reserva (Reserved, Sold, Cancelled o Expired) por su número. Con el teléfono con que se hizo (phone), la vista " +
                         "completa; sin él, la misma reserva con el contacto enmascarado (masked = true). 404 si no existe o el teléfono no coincide");
        sf.MapGet("/reservations", (ISender s, string? phone, CancellationToken ct) =>
                s.Send(new GetStorefrontReservationsByPhoneQuery(phone ?? string.Empty), ct))
            .RequireRateLimiting(LookupPolicy)
            .Produces<IReadOnlyList<StorefrontReservationView>>().ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary($"V7 · Reservas web hechas con un teléfono (phone, obligatorio): las de los últimos {StorefrontPrivacy.PhoneLookupDays} días, " +
                         $"las más nuevas primero y como máximo {StorefrontPrivacy.PhoneLookupLimit}, con el contacto enmascarado; lista vacía si no hay ninguna");
        sf.MapPost("/reservations/{number}/cancel", (ISender s, string number, StorefrontCancelRequest body, CancellationToken ct) =>
                s.Send(new CancelStorefrontReservationCommand(number, body.Phone), ct))
            .RequireRateLimiting(ReservePolicy)
            .Produces<StorefrontReservationView>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("El cliente libera su reserva con el número Y el teléfono con que la hizo (el stock vuelve a estar disponible)");
    }
}
