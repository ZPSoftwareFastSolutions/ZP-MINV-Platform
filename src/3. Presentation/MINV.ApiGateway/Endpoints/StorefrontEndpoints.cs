using MediatR;
using Microsoft.Net.Http.Headers;
using MINV.ApiGateway.Security;
using MINV.Application.Storefront;

namespace MINV.ApiGateway.Endpoints;

/// <summary>Reserva de un armado desde la tienda web (cuerpo de <c>POST /storefront/v1/reservations</c>).</summary>
/// <param name="Lines">Piezas: SKU, cantidad (1 a 16) y ranura de la web (opcional: se deduce de la ficha).</param>
/// <param name="Contact">Nombre y teléfono (Bolivia: 7 u 8 dígitos, con o sin +591) obligatorios; correo opcional.</param>
/// <param name="Notes">Notas para la tienda (≤ 500).</param>
/// <param name="Name">Nombre del armado (por defecto «Armado web de &lt;nombre&gt;»).</param>
/// <param name="IdempotencyKey">Alternativa a la cabecera <c>Idempotency-Key</c>.</param>
public sealed record StorefrontReservationRequest(IReadOnlyList<StorefrontReservationLineInput> Lines, StorefrontContactInput Contact, string? Notes = null,
    string? Name = null, string? IdempotencyKey = null);

/// <summary>El cliente cancela su reserva con el teléfono con que la hizo.</summary>
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
            .WithSummary("Instantánea del catálogo web: empresa, sucursal de la tienda, categorías, marcas, productos con ficha y disponibilidad, armados publicados");
        sf.MapGet("/products/{slug}", async Task<IResult> (ISender s, HttpContext http, string slug, CancellationToken ct) =>
            {
                var product = await s.Send(new GetStorefrontProductQuery(slug), ct);
                http.Response.Headers.CacheControl = "no-cache";
                return Results.Ok(product);
            })
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
                var result = await s.Send(new CreateStorefrontReservationCommand(body.Lines, body.Contact, body.Notes, key, body.Name), ct);
                if (result.Replayed)
                {
                    http.Response.Headers["Idempotent-Replayed"] = "true";
                    return Results.Ok(result.Reservation);
                }
                return Results.Created($"{Prefix}/reservations/{Uri.EscapeDataString(result.Reservation.Number)}", result.Reservation);
            })
            .RequireRateLimiting(ReservePolicy)
            .WithSummary("Reserva un armado: cotización ARM-WEB con el stock de cada pieza reservado 48 h (idempotente por Idempotency-Key; 409 si falta stock)");
        sf.MapGet("/reservations/{number}", (ISender s, string number, string phone, CancellationToken ct) =>
                s.Send(new GetStorefrontReservationQuery(number, phone), ct))
            .WithSummary("Estado de una reserva (Reserved, Sold, Cancelled o Expired) con el número y el teléfono con que se hizo");
        sf.MapPost("/reservations/{number}/cancel", (ISender s, string number, StorefrontCancelRequest body, CancellationToken ct) =>
                s.Send(new CancelStorefrontReservationCommand(number, body.Phone), ct))
            .RequireRateLimiting(ReservePolicy)
            .WithSummary("El cliente libera su reserva (el stock vuelve a estar disponible)");
    }
}
