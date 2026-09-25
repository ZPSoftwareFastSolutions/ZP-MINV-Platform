using MINV.Domain.Common;

namespace MINV.Domain.Integration;

/// <summary>
/// V4 · Destino de webhooks de una empresa (URL https del e-commerce o del ERP) y los eventos a los que se suscribe.
/// El secreto de la firma HMAC es aleatorio (32 bytes) y se guarda CIFRADO (AES-GCM) con la clave maestra del servidor
/// (<see cref="SecretKeyId"/> identifica esa clave para poder rotarla). Al rotar el secreto, el anterior sigue firmando
/// en paralelo durante un período de gracia (cabecera con dos firmas) para que el receptor cambie sin cortes.
/// </summary>
public sealed class WebhookEndpoint : Entity, IConcurrencyAware, IAggregateRoot
{
    private readonly List<WebhookEndpointEvent> _events = new();

    private WebhookEndpoint()
    {
    }

    public WebhookEndpoint(Guid tenantId, string url, string? description, IEnumerable<string> eventTypes, Guid createdByUserId,
        Guid? apiKeyId, Guid? branchId, string secretCiphertext, string secretKeyId, DateTimeOffset createdAt)
        : base(tenantId)
    {
        Url = ValidateUrl(url);
        Description = Guard.OptionalText(description, "La descripción", 200);
        CreatedByUserId = Guard.NotEmpty(createdByUserId, nameof(createdByUserId));
        ApiKeyId = Guard.NotEmptyIfPresent(apiKeyId, nameof(apiKeyId));
        BranchId = Guard.NotEmptyIfPresent(branchId, nameof(branchId));
        SecretCiphertext = Guard.Text(secretCiphertext, "El secreto cifrado", 500);
        SecretKeyId = Guard.Text(secretKeyId, "La clave maestra", 40);
        SecretVersion = 1;
        CreatedAt = createdAt.ToUniversalTime();
        IsActive = true;
        foreach (var type in eventTypes.Select(e => e.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal))
        {
            Guard.That(IntegrationEvents.All.Any(e => e.Code == type), "webhook.event", $"El evento «{type}» no existe.");
            _events.Add(new WebhookEndpointEvent(tenantId, Id, type));
        }
        Guard.That(_events.Count > 0, "webhook.events", "Suscriba el webhook a al menos un evento.");
    }

    public string Url { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }

    /// <summary>API Key que lo registró (null = lo registró un usuario desde el escritorio).</summary>
    public Guid? ApiKeyId { get; private set; }

    /// <summary>Sucursal cuyos eventos recibe (null = todas).</summary>
    public Guid? BranchId { get; private set; }

    /// <summary>Secreto vigente, cifrado con la clave maestra (nonce + texto + etiqueta, base64).</summary>
    public string SecretCiphertext { get; private set; } = string.Empty;

    public string SecretKeyId { get; private set; } = string.Empty;

    public int SecretVersion { get; private set; }

    /// <summary>Secreto anterior (cifrado) que sigue firmando hasta <see cref="PreviousSecretExpiresAt"/>.</summary>
    public string? PreviousSecretCiphertext { get; private set; }

    public string? PreviousSecretKeyId { get; private set; }

    public DateTimeOffset? PreviousSecretExpiresAt { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<WebhookEndpointEvent> Events => _events;

    /// <summary>¿Debe recibir este evento? Solo si está activo, suscrito, el evento es de su sucursal (o no filtra) y
    /// ocurrió después de registrarlo (no se reenvía la historia).</summary>
    public bool Wants(string eventType, Guid? eventBranchId, DateTimeOffset occurredAt) =>
        IsActive && occurredAt >= CreatedAt && _events.Any(e => e.EventType == eventType)
        && (BranchId is null || eventBranchId is null || BranchId == eventBranchId);

    public bool SignsWithPreviousAt(DateTimeOffset now) => PreviousSecretCiphertext is not null && PreviousSecretExpiresAt > now;

    /// <summary>Rota el secreto: el anterior sigue firmando durante <paramref name="grace"/> (máx. 7 días).</summary>
    public void RotateSecret(string secretCiphertext, string secretKeyId, DateTimeOffset now, TimeSpan grace)
    {
        Guard.That(IsActive, "webhook.disabled", "El webhook está desactivado.");
        Guard.That(grace >= TimeSpan.Zero && grace <= TimeSpan.FromDays(7), "webhook.grace", "La gracia debe ser de 0 a 7 días.");
        PreviousSecretCiphertext = SecretCiphertext;
        PreviousSecretKeyId = SecretKeyId;
        PreviousSecretExpiresAt = now.ToUniversalTime() + grace;
        SecretCiphertext = Guard.Text(secretCiphertext, "El secreto cifrado", 500);
        SecretKeyId = Guard.Text(secretKeyId, "La clave maestra", 40);
        SecretVersion++;
    }

    public void Disable(DateTimeOffset now)
    {
        Guard.That(IsActive, "webhook.disabled", "El webhook ya estaba desactivado.");
        IsActive = false;
        DisabledAt = now.ToUniversalTime();
    }

    /// <summary>Solo https (se admite http://localhost para pruebas locales) y sin credenciales en la URL.</summary>
    public static string ValidateUrl(string? url)
    {
        var text = Guard.Text(url, "La URL del webhook", 500);
        Guard.That(Uri.TryCreate(text, UriKind.Absolute, out var uri), "webhook.url", "La URL del webhook no es válida.");
        var local = uri!.IsLoopback && uri.Scheme == Uri.UriSchemeHttp;
        Guard.That(uri.Scheme == Uri.UriSchemeHttps || local, "webhook.https", "El webhook debe usar https.");
        Guard.That(string.IsNullOrEmpty(uri.UserInfo), "webhook.userinfo", "La URL no debe incluir usuario ni contraseña.");
        return uri.ToString();
    }
}

/// <summary>Evento al que se suscribe un destino (5FN: una fila por evento).</summary>
public sealed class WebhookEndpointEvent : BaseEntity
{
    private WebhookEndpointEvent()
    {
    }

    internal WebhookEndpointEvent(Guid tenantId, Guid endpointId, string eventType)
        : base(tenantId)
    {
        EndpointId = Guard.NotEmpty(endpointId, nameof(endpointId));
        EventType = Guard.Text(eventType, "El evento", 60);
    }

    public Guid EndpointId { get; private set; }

    public string EventType { get; private set; } = string.Empty;
}

/// <summary>
/// V4 · Outbox transaccional (append-only): cada evento de dominio se guarda en la MISMA transacción que el cambio que
/// lo produjo; el gateway lo entrega después a los webhooks suscritos. Nunca se pierde un evento confirmado ni se
/// publica uno de una transacción deshecha.
/// </summary>
public sealed class OutboxEvent : Entity, IAppendOnly
{
    private OutboxEvent()
    {
    }

    public OutboxEvent(Guid tenantId, string eventType, Guid? branchId, string payload, DateTimeOffset occurredAt)
        : base(tenantId)
    {
        EventType = Guard.Text(eventType, "El evento", 60);
        BranchId = Guard.NotEmptyIfPresent(branchId, nameof(branchId));
        Payload = Guard.Text(payload, "El contenido", 100_000);
        OccurredAt = occurredAt.ToUniversalTime();
    }

    public string EventType { get; private set; } = string.Empty;

    public Guid? BranchId { get; private set; }

    /// <summary>JSON del evento (camelCase).</summary>
    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>
/// V4 · Cola de despacho de un evento del outbox (1:1, se crea en la misma transacción que el evento). Es la ÚNICA
/// tabla mutable de la integración: el despachador toma los pendientes vencidos con <c>FOR UPDATE SKIP LOCKED</c>
/// (varias réplicas del gateway nunca entregan dos veces a la vez), registra cada intento en
/// <see cref="WebhookDelivery"/> (append-only) y reprograma con espera exponencial hasta 8 rondas.
/// </summary>
public sealed class OutboxDispatch : BaseEntity, IConcurrencyAware
{
    private OutboxDispatch()
    {
    }

    public OutboxDispatch(Guid tenantId, Guid outboxEventId, DateTimeOffset createdAt)
        : base(tenantId)
    {
        OutboxEventId = Guard.NotEmpty(outboxEventId, nameof(outboxEventId));
        Status = OutboxDispatchStatus.Pending;
        NextAttemptAt = createdAt.ToUniversalTime();
    }

    public Guid OutboxEventId { get; private set; }

    public OutboxDispatchStatus Status { get; private set; }

    /// <summary>Rondas de entrega ya hechas (cada ronda intenta todos los destinos que aún no confirmaron).</summary>
    public int Rounds { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? LastError { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>Resultado de una ronda: todos entregados (o nadie suscrito) → Completed; si no, se reprograma o se da por
    /// agotado tras <see cref="WebhookDelivery.MaxAttempts"/> rondas.</summary>
    public void RecordRound(bool allDelivered, string? error, DateTimeOffset now)
    {
        Guard.That(Status == OutboxDispatchStatus.Pending, "outbox.done", "El evento ya se despachó.");
        Rounds++;
        if (allDelivered)
        {
            Status = OutboxDispatchStatus.Completed;
            CompletedAt = now.ToUniversalTime();
            LastError = null;
            return;
        }
        LastError = error is null ? null : error.Length > 500 ? error[..500] : error;
        if (Rounds >= WebhookDelivery.MaxAttempts)
        {
            Status = OutboxDispatchStatus.Exhausted;
            CompletedAt = now.ToUniversalTime();
            return;
        }
        NextAttemptAt = now.ToUniversalTime() + WebhookDelivery.BackoffBefore(Rounds + 1);
    }
}

public enum OutboxDispatchStatus
{
    Pending,
    Completed,
    Exhausted,
}

/// <summary>V4 · Intento de entrega de un evento a un webhook (append-only: cada reintento es una fila nueva).</summary>
public sealed class WebhookDelivery : Entity, IAppendOnly
{
    public const int MaxAttempts = 8;

    private WebhookDelivery()
    {
    }

    public WebhookDelivery(Guid tenantId, Guid outboxEventId, Guid endpointId, int attempt, int? statusCode, bool succeeded, string? error,
        DateTimeOffset attemptedAt, int durationMs)
        : base(tenantId)
    {
        OutboxEventId = Guard.NotEmpty(outboxEventId, nameof(outboxEventId));
        EndpointId = Guard.NotEmpty(endpointId, nameof(endpointId));
        Guard.That(attempt is >= 1 and <= MaxAttempts, "webhook.attempt", "Número de intento fuera de rango.");
        Attempt = attempt;
        StatusCode = statusCode;
        Succeeded = succeeded;
        Error = error is null ? null : error.Length > 500 ? error[..500] : error;
        AttemptedAt = attemptedAt.ToUniversalTime();
        DurationMs = Guard.NonNegative(durationMs, "La duración");
    }

    public Guid OutboxEventId { get; private set; }

    public Guid EndpointId { get; private set; }

    public int Attempt { get; private set; }

    public int? StatusCode { get; private set; }

    public bool Succeeded { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset AttemptedAt { get; private set; }

    public int DurationMs { get; private set; }

    /// <summary>Espera antes del intento <paramref name="attempt"/> (1 = inmediato): 1 min, 5 min, 30 min, 2 h, 6 h, 12 h, 24 h.</summary>
    public static TimeSpan BackoffBefore(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.Zero,
        2 => TimeSpan.FromMinutes(1),
        3 => TimeSpan.FromMinutes(5),
        4 => TimeSpan.FromMinutes(30),
        5 => TimeSpan.FromHours(2),
        6 => TimeSpan.FromHours(6),
        7 => TimeSpan.FromHours(12),
        _ => TimeSpan.FromHours(24),
    };
}

/// <summary>Eventos de integración publicados por M-INV.</summary>
public static class IntegrationEvents
{
    public const string SaleCompleted = "sale.completed";
    public const string SaleVoided = "sale.voided";
    public const string PurchaseReceived = "purchase.received";
    public const string TransferDispatched = "transfer.dispatched";
    public const string TransferReceived = "transfer.received";
    public const string TransferDiscrepancy = "transfer.discrepancy";

    public static readonly IReadOnlyList<(string Code, string Description)> All =
    [
        (SaleCompleted, "Venta cobrada (POS o e-commerce)"),
        (SaleVoided, "Venta anulada (el stock volvió)"),
        (PurchaseReceived, "Mercadería recibida de un proveedor"),
        (TransferDispatched, "Transferencia despachada: la mercadería sale del origen y queda en tránsito"),
        (TransferReceived, "Transferencia recibida en la sucursal destino"),
        (TransferDiscrepancy, "Faltante registrado al recibir una transferencia"),
    ];
}
