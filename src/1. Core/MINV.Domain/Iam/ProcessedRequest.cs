using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>
/// V4 · Comando ya ejecutado por el servidor en la nube (idempotencia sobre internet): si la conexión se corta durante
/// el COMMIT, el escritorio repite el comando con el MISMO id y el servidor devuelve la respuesta guardada en lugar de
/// vender o mover stock dos veces. Se guarda en la misma transacción que el comando (append-only).
/// </summary>
public sealed class ProcessedRequest : Entity, IAppendOnly
{
    private ProcessedRequest()
    {
    }

    public ProcessedRequest(Guid tenantId, Guid requestId, Guid userId, string requestType, string requestHash, string response,
        DateTimeOffset processedAt)
        : base(tenantId)
    {
        RequestId = Guard.NotEmpty(requestId, nameof(requestId));
        UserId = Guard.NotEmpty(userId, nameof(userId));
        RequestType = Guard.Text(requestType, "El tipo de comando", 200);
        Guard.That(requestHash is { Length: 64 } && requestHash.All(char.IsAsciiHexDigit), "request.hash", "Hash de la petición inválido.");
        RequestHash = requestHash.ToLowerInvariant();
        Response = Guard.Text(response, "La respuesta", 2_000_000);
        ProcessedAt = processedAt.ToUniversalTime();
    }

    public Guid RequestId { get; private set; }

    public Guid UserId { get; private set; }

    public string RequestType { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    /// <summary>Respuesta serializada (JSON) que se devuelve al repetir.</summary>
    public string Response { get; private set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; private set; }
}
