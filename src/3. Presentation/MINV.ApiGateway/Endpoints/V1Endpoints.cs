using MediatR;
using Microsoft.AspNetCore.Mvc;
using MINV.Application.Corporate;
using MINV.Application.Integration;
using MINV.Application.Inventory.Transfers;
using MINV.Application.Sales;
using MINV.Domain.Integration;
using MINV.Domain.Inventory;

namespace MINV.ApiGateway.Endpoints;

/// <summary>Pedido del e-commerce (cuerpo de <c>POST /v1/orders</c>).</summary>
/// <param name="ExternalId">Id del pedido en su sistema (idempotencia). Si falta, se usa la cabecera Idempotency-Key.</param>
/// <param name="CustomerCode">Código del cliente en M-INV (CF = consumidor final).</param>
/// <param name="PaymentMethodCode">EFECTIVO, QR, TARJETA o TRANSFERENCIA.</param>
/// <param name="Lines">SKU (o código de barras), cantidad y descuento %.</param>
/// <param name="PaymentReference">Número de operación del pago (obligatorio en QR, tarjeta y transferencia).</param>
/// <param name="WarehouseCode">Almacén que despacha (por defecto, el de la sucursal de la llave).</param>
public sealed record OrderRequest(string? ExternalId, string CustomerCode, string PaymentMethodCode, IReadOnlyList<SaleLineInput> Lines,
    string? PaymentReference = null, string? WarehouseCode = null);

public sealed record CreateTransferRequest(string ToWarehouseCode, IReadOnlyList<TransferLineInput> Lines, string? Notes = null,
    string? FromWarehouseCode = null);

public sealed record ReceiveTransferRequest(IReadOnlyList<TransferReceiptInput>? Lines = null);

public sealed record CancelTransferRequest(string Reason);

public sealed record CreateWebhookRequest(string Url, IReadOnlyList<string> Events, string? Description = null, string? BranchCode = null);

/// <summary>
/// V4 · API pública versionada (<c>/v1</c>). Cada ruta exige un alcance de la llave; dentro, el caso de uso vuelve a
/// comprobar permisos, módulo licenciado y sucursal (defensa en profundidad). Los errores salen como ProblemDetails.
/// </summary>
public static class V1Endpoints
{
    public static void Map(WebApplication app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization().RequireRateLimiting("api-key");

        // ---------------------------------------------------------------------------------------------- catálogo
        v1.MapGet("/catalog", (ISender s, int page = 1, int pageSize = 100, string? search = null, CancellationToken ct = default) =>
                s.Send(new GetApiCatalogQuery(page, pageSize, search), ct))
            .RequireAuthorization(ApiScopes.CatalogRead).WithTags("Catálogo").WithSummary("Productos, precios y códigos de barras (paginado, ≤ 500)");
        v1.MapGet("/branches", (ISender s, CancellationToken ct) => s.Send(new GetBranchesQuery(), ct))
            .RequireAuthorization(ApiScopes.CatalogRead).WithTags("Catálogo").WithSummary("Sucursales de la empresa");
        v1.MapGet("/events", () => Results.Ok(IntegrationEvents.All.Select(e => new { code = e.Code, description = e.Description }).ToList()))
            .WithTags("Webhooks").WithSummary("Eventos que publica M-INV");

        // ---------------------------------------------------------------------------------------------- stock
        v1.MapGet("/stock", (ISender s, string? branch = null, string? sku = null, int page = 1, int pageSize = 100, CancellationToken ct = default) =>
                s.Send(new GetApiStockQuery(branch, sku, page, pageSize), ct))
            .RequireAuthorization(ApiScopes.StockRead).WithTags("Stock").WithSummary("Existencias por sucursal y almacén (paginado)");
        v1.MapGet("/stock/consolidated", (ISender s, string? search = null, CancellationToken ct = default) =>
                s.Send(new ConsolidatedStockQuery(search), ct))
            .RequireAuthorization(ApiScopes.StockRead).WithTags("Stock").WithSummary("Stock consolidado por sucursal más lo que está en tránsito");

        // ---------------------------------------------------------------------------------------------- pedidos
        v1.MapPost("/orders", async Task<IResult> (ISender s, HttpContext http, OrderRequest body, CancellationToken ct) =>
            {
                var externalId = body.ExternalId ?? http.Request.Headers["Idempotency-Key"].ToString();
                var result = await s.Send(new CreateExternalOrderCommand(externalId, body.CustomerCode, body.PaymentMethodCode, body.Lines,
                    body.PaymentReference, body.WarehouseCode), ct);
                if (result.Replayed)
                {
                    http.Response.Headers["Idempotent-Replayed"] = "true";
                    return Results.Ok(result);
                }
                return Results.Created($"/v1/orders/{Uri.EscapeDataString(result.ExternalId)}", result);
            })
            .RequireAuthorization(ApiScopes.OrdersWrite).WithTags("Pedidos")
            .WithSummary("Registra un pedido del e-commerce como venta (idempotente por externalId)");
        v1.MapGet("/orders/{externalId}", (ISender s, string externalId, CancellationToken ct) => s.Send(new GetExternalOrderQuery(externalId), ct))
            .RequireAuthorization(ApiScopes.OrdersWrite).WithTags("Pedidos").WithSummary("Consulta un pedido ya registrado por esta llave");

        // ---------------------------------------------------------------------------------------------- transferencias
        v1.MapGet("/transfers", (ISender s, TransferStatus? status = null, CancellationToken ct = default) => s.Send(new GetTransfersQuery(status), ct))
            .RequireAuthorization(ApiScopes.TransfersRead).WithTags("Transferencias").WithSummary("Transferencias de las sucursales de la llave");
        v1.MapGet("/transfers/{id:guid}", (ISender s, Guid id, CancellationToken ct) => s.Send(new GetTransferQuery(id), ct))
            .RequireAuthorization(ApiScopes.TransfersRead).WithTags("Transferencias").WithSummary("Detalle, manifiesto por lote y bitácora");
        v1.MapPost("/transfers", async Task<IResult> (ISender s, CreateTransferRequest body, CancellationToken ct) =>
            {
                var created = await s.Send(new CreateTransferCommand(body.ToWarehouseCode, body.Lines, body.Notes, body.FromWarehouseCode), ct);
                return Results.Created($"/v1/transfers/{created.Id}", created);
            })
            .RequireAuthorization(ApiScopes.TransfersWrite).WithTags("Transferencias").WithSummary("Solicita una transferencia (Pendiente)");
        v1.MapPost("/transfers/{id:guid}/dispatch", (ISender s, Guid id, CancellationToken ct) => s.Send(new DispatchTransferCommand(id), ct))
            .RequireAuthorization(ApiScopes.TransfersWrite).WithTags("Transferencias").WithSummary("Despacha (sale del origen: en tránsito)");
        v1.MapPost("/transfers/{id:guid}/receive", (ISender s, Guid id, ReceiveTransferRequest? body, CancellationToken ct) =>
                s.Send(new ReceiveTransferCommand(id, body?.Lines), ct))
            .RequireAuthorization(ApiScopes.TransfersWrite).WithTags("Transferencias").WithSummary("Recibe en el destino (faltantes con motivo)");
        v1.MapPost("/transfers/{id:guid}/cancel", (ISender s, Guid id, CancelTransferRequest body, CancellationToken ct) =>
                s.Send(new CancelTransferCommand(id, body.Reason), ct))
            .RequireAuthorization(ApiScopes.TransfersWrite).WithTags("Transferencias").WithSummary("Anula una transferencia pendiente");

        // ---------------------------------------------------------------------------------------------- webhooks
        v1.MapGet("/webhooks", (ISender s, CancellationToken ct) => s.Send(new GetWebhooksQuery(), ct))
            .RequireAuthorization(ApiScopes.WebhooksManage).WithTags("Webhooks").WithSummary("Webhooks registrados y su estado de entrega");
        v1.MapPost("/webhooks", async Task<IResult> (ISender s, CreateWebhookRequest body, CancellationToken ct) =>
            {
                var created = await s.Send(new CreateWebhookCommand(body.Url, body.Events, body.Description, body.BranchCode), ct);
                return Results.Created($"/v1/webhooks/{created.Id}", created);
            })
            .RequireAuthorization(ApiScopes.WebhooksManage).WithTags("Webhooks").WithSummary("Registra un webhook (el secreto se muestra una vez)");
        v1.MapPost("/webhooks/{id:guid}/rotate-secret", (ISender s, Guid id, CancellationToken ct) => s.Send(new RotateWebhookSecretCommand(id), ct))
            .RequireAuthorization(ApiScopes.WebhooksManage).WithTags("Webhooks").WithSummary("Rota el secreto (doble firma 24 h)");
        v1.MapDelete("/webhooks/{id:guid}", async Task<IResult> (ISender s, Guid id, CancellationToken ct) =>
            {
                await s.Send(new DisableWebhookCommand(id), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(ApiScopes.WebhooksManage).WithTags("Webhooks").WithSummary("Desactiva un webhook");
        v1.MapGet("/webhooks/deliveries", (ISender s, Guid? endpointId = null, int take = 100, CancellationToken ct = default) =>
                s.Send(new GetWebhookDeliveriesQuery(endpointId, take), ct))
            .RequireAuthorization(ApiScopes.WebhooksManage).WithTags("Webhooks").WithSummary("Últimos intentos de entrega");

        // ---------------------------------------------------------------------------------------------- reportes
        v1.MapGet("/reports/branches", (ISender s, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
                s.Send(new GetBranchReportQuery(from, to), ct))
            .RequireAuthorization(ApiScopes.ReportsRead).WithTags("Reportes").WithSummary("Ventas y stock por sucursal (modelo de lectura)");
    }
}
