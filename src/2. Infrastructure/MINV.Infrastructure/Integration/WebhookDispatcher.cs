using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Integration;
using MINV.Domain.Integration;
using MINV.Infrastructure.Persistence;

namespace MINV.Infrastructure.Integration;

/// <summary>
/// V4 · Cliente HTTP para los webhooks, protegido contra SSRF: resuelve el nombre y se conecta SOLO a direcciones públicas
/// (rechaza loopback, redes privadas, enlace local, CGNAT, multicast y la IP de metadatos de la nube, también si el DNS
/// apunta ahí después de registrar la URL), sin redirecciones, con tiempo máximo de 10 s y respuesta limitada.
/// <paramref name="allowPrivate"/> solo para pruebas locales (webhook en http://localhost).
/// </summary>
public static class SafeWebhookHttp
{
    public static HttpClient Create(bool allowPrivate = false)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxResponseHeadersLength = 32,
            ConnectCallback = async (context, ct) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                var target = addresses.FirstOrDefault(a => allowPrivate || IsPublic(a))
                             ?? throw new HttpRequestException($"El destino {context.DnsEndPoint.Host} no es una dirección pública (protección SSRF).");
                var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 64 * 1024 };
    }

    /// <summary>¿Dirección enrutable en internet?</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast))
        {
            return false;
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal);
        }
        var b = address.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 0 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                 || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] >= 224
                 || (b[0] == 192 && b[1] == 0 && b[2] == 0) || (b[0] == 198 && (b[1] == 18 || b[1] == 19)));
    }
}

/// <summary>Resultado de una pasada del despachador.</summary>
public sealed record DispatchSummary(int Events, int Delivered, int Failed);

/// <summary>
/// V4 · Despachador de webhooks (lo hospeda el API Gateway en segundo plano): toma los eventos vencidos de la cola
/// (<c>integration.claim_deliveries</c>, FOR UPDATE SKIP LOCKED: varias réplicas nunca toman el mismo) y, por cada uno,
/// en el contexto de SU empresa, entrega el JSON firmado a cada webhook suscrito que aún no confirmó; cada intento queda
/// en <c>webhook_deliveries</c> (append-only) y la cola se reprograma con espera exponencial.
/// </summary>
public sealed class WebhookDispatcher(IServiceScopeFactory scopes, HttpClient http, IClock clock)
{
    public const string SignatureHeader = "X-MINV-Signature";
    public const string EventHeader = "X-MINV-Event";
    public const string DeliveryHeader = "X-MINV-Delivery";

    private sealed record Claimed(Guid TenantId, Guid OutboxEventId);

    public async Task<DispatchSummary> RunOnceAsync(int batch, CancellationToken ct)
    {
        IReadOnlyList<Claimed> claimed;
        using (var scope = scopes.CreateScope())
        {
            claimed = await ClaimAsync(scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>(), batch, ct);
        }
        int delivered = 0, failed = 0;
        foreach (var item in claimed)
        {
            using var scope = scopes.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.Set(item.TenantId);   // proceso de plataforma: todas las sucursales de ESA empresa
            var (ok, ko) = await DeliverAsync(scope.ServiceProvider, item.OutboxEventId, ct);
            delivered += ok;
            failed += ko;
        }
        return new DispatchSummary(claimed.Count, delivered, failed);
    }

    private static async Task<IReadOnlyList<Claimed>> ClaimAsync(MinvWriteDbContext db, int batch, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            return await db.Database.SqlQuery<Claimed>(
                $"SELECT tenant_id AS \"TenantId\", outbox_event_id AS \"OutboxEventId\" FROM integration.claim_deliveries({batch}, {120})").ToListAsync(ct);
        }
        var now = DateTimeOffset.UtcNow;
        return await db.OutboxDispatches.IgnoreQueryFilters().Where(d => d.Status == OutboxDispatchStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).Take(batch).Select(d => new Claimed(d.TenantId, d.OutboxEventId)).ToListAsync(ct);
    }

    private async Task<(int Delivered, int Failed)> DeliverAsync(IServiceProvider sp, Guid eventId, CancellationToken ct)
    {
        var db = sp.GetRequiredService<MinvWriteDbContext>();
        var protector = sp.GetRequiredService<ISecretProtector>();
        var outbox = await db.OutboxEvents.AsNoTracking().FirstAsync(e => e.Id == eventId, ct);
        var dispatch = await db.OutboxDispatches.FirstAsync(d => d.OutboxEventId == eventId, ct);
        if (dispatch.Status != OutboxDispatchStatus.Pending)
        {
            return (0, 0);
        }
        var endpoints = (await db.WebhookEndpoints.AsNoTracking().Include(e => e.Events).ToListAsync(ct))
            .Where(e => e.Wants(outbox.EventType, outbox.BranchId, outbox.OccurredAt)).ToList();
        var done = await db.WebhookDeliveries.Where(d => d.OutboxEventId == eventId).GroupBy(d => d.EndpointId)
            .Select(g => new { g.Key, Attempts = g.Count(), Ok = g.Any(d => d.Succeeded) }).ToDictionaryAsync(x => x.Key, ct);
        var body = JsonSerializer.Serialize(new
        {
            id = outbox.Id,
            type = outbox.EventType,
            occurredAt = outbox.OccurredAt,
            tenantId = outbox.TenantId,
            branchId = outbox.BranchId,
            data = JsonDocument.Parse(outbox.Payload).RootElement,
        }, MinvWriteDbContext.EventJson);
        int delivered = 0, failed = 0;
        string? lastError = null;
        foreach (var endpoint in endpoints)
        {
            var previous = done.GetValueOrDefault(endpoint.Id);
            if (previous?.Ok == true || previous?.Attempts >= WebhookDelivery.MaxAttempts)
            {
                continue;
            }
            var attempt = (previous?.Attempts ?? 0) + 1;
            var (status, error, ms) = await PostAsync(endpoint, protector, outbox, body, attempt, ct);
            var ok = status is >= 200 and < 300;
            db.WebhookDeliveries.Add(new WebhookDelivery(outbox.TenantId, outbox.Id, endpoint.Id, attempt, status, ok, error, clock.UtcNow, ms));
            if (ok)
            {
                delivered++;
            }
            else
            {
                failed++;
                lastError = $"{endpoint.Url}: {error ?? $"HTTP {status}"}";
            }
        }
        dispatch.RecordRound(failed == 0, lastError, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return (delivered, failed);
    }

    private async Task<(int? Status, string? Error, int Ms)> PostAsync(WebhookEndpoint endpoint, ISecretProtector protector, OutboxEvent outbox,
        string body, int attempt, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var now = clock.UtcNow;
            var secrets = new List<string> { protector.Unprotect(endpoint.SecretCiphertext, endpoint.SecretKeyId) };
            if (endpoint.SignsWithPreviousAt(now))
            {
                secrets.Add(protector.Unprotect(endpoint.PreviousSecretCiphertext!, endpoint.PreviousSecretKeyId!));
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add(SignatureHeader, ApiKeyTokens.Sign(secrets, now.ToUnixTimeSeconds(), body));
            request.Headers.Add(EventHeader, outbox.EventType);
            request.Headers.Add(DeliveryHeader, $"{outbox.Id:N}-{attempt}");
            request.Headers.UserAgent.ParseAdd("M-INV-Webhooks/4.0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return ((int)response.StatusCode, response.IsSuccessStatusCode ? null : response.ReasonPhrase, (int)watch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Red, tiempo agotado, secreto que no se puede descifrar (clave maestra ausente o rotada)…: el intento queda
            // registrado y la cola se reprograma; nunca se reintenta en silencio
            return (null, ex.Message, (int)watch.ElapsedMilliseconds);
        }
    }
}
