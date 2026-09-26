using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Infrastructure.Billing.Soap;

/// <summary>
/// V4.1 · Cliente SOAP 1.1 de los servicios del SIN (modalidad Computarizada en Línea) sobre el <see cref="HttpClient"/>
/// con nombre <see cref="HttpClientName"/>. Contrato en <see cref="SiatSoapContract"/> (FUERA DE DOC: confirmar con el
/// WSDL del piloto); cabecera <c>apikey: TokenApi &lt;token&gt;</c> en todas las llamadas.
/// <list type="bullet">
/// <item>Falta de comunicación (tiempo agotado, red, HTTP 5xx/404/400, falla SOAP, respuesta ilegible) →
/// <see cref="SiatUnavailableException"/>: es lo que lleva al modo fuera de línea.</item>
/// <item>Token rechazado (HTTP 401/403 o mensaje 989) → <see cref="DomainException"/> <c>siat.token_rejected</c>: NO es
/// falta de comunicación.</item>
/// <item>Cualquier otra respuesta del SIN (aceptada o rechazada) vuelve como respuesta del puerto.</item>
/// </list>
/// Cada llamada queda en la bitácora técnica (<see cref="ISiatCallLog"/>) sin el token; un fallo de la bitácora no
/// interrumpe la llamada.
/// </summary>
public sealed class SiatSoapGateway(IHttpClientFactory httpClients, ISiatCallLog callLog, IClock clock) : ISiatGateway
{
    /// <summary>Nombre del cliente HTTP registrado para el SIN.</summary>
    public const string HttpClientName = "siat";

    public async Task<SiatReply> CheckCommunicationAsync(SiatConnection connection, SiatResource resource, CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Communication(await CallAsync(connection, SiatSoapRequests.CheckCommunication(resource), cancellationToken));

    public async Task<SiatCuisReply> RequestCuisAsync(SiatConnection connection, SiatPlace place, CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Cuis(await CallAsync(connection, SiatSoapRequests.Cuis(connection, place), cancellationToken));

    public async Task<SiatCufdReply> RequestCufdAsync(SiatConnection connection, SiatPlace place, string cuis, CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Cufd(await CallAsync(connection, SiatSoapRequests.Cufd(connection, place, cuis), cancellationToken));

    public async Task<SiatNitReply> VerifyNitAsync(SiatConnection connection, SiatPlace place, string cuis, long nitToVerify,
        CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Nit(await CallAsync(connection, SiatSoapRequests.VerifyNit(connection, place, cuis, nitToVerify), cancellationToken));

    public async Task<SiatCatalogReply> SyncCatalogAsync(SiatConnection connection, SiatPlace place, string cuis, string catalog,
        CancellationToken cancellationToken = default)
    {
        var call = SiatSoapRequests.Sync(connection, place, cuis, catalog);
        return SiatSoapReplies.Catalog(await CallAsync(connection, call, cancellationToken), SiatSoapContract.SyncFor(catalog).Shape);
    }

    public async Task<SiatClockReply> SyncClockAsync(SiatConnection connection, SiatPlace place, string cuis, CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Clock(await CallAsync(connection, SiatSoapRequests.Clock(connection, place, cuis), cancellationToken));

    public async Task<SiatPointOfSaleReply> RegisterPointOfSaleAsync(SiatConnection connection, SiatPlace place, string cuis, int typeCode, string name,
        string description, CancellationToken cancellationToken = default) =>
        SiatSoapReplies.PointOfSale(await CallAsync(connection,
            SiatSoapRequests.RegisterPointOfSale(connection, place, cuis, typeCode, name, description), cancellationToken));

    public async Task<SiatPointOfSaleListReply> ListPointsOfSaleAsync(SiatConnection connection, SiatPlace place, string cuis,
        CancellationToken cancellationToken = default) =>
        SiatSoapReplies.PointsOfSale(await CallAsync(connection, SiatSoapRequests.ListPointsOfSale(connection, place, cuis), cancellationToken));

    public async Task<SiatReply> ClosePointOfSaleAsync(SiatConnection connection, SiatPlace place, string cuis, CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Close(await CallAsync(connection, SiatSoapRequests.ClosePointOfSale(connection, place, cuis), cancellationToken));

    public async Task<SiatEventReply> RegisterEventAsync(SiatConnection connection, SiatPlace place, string cuis, string cufd, int eventCode,
        string description, DateTime startedAt, DateTime endedAt, string eventCufd, CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Event(await CallAsync(connection,
            SiatSoapRequests.RegisterEvent(connection, place, cuis, cufd, eventCode, description, startedAt, endedAt, eventCufd), cancellationToken));

    public async Task<SiatReply> SendDocumentAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        byte[] gzip, string sha256, DateTime sentAt, CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Service(await CallAsync(connection,
            SiatSoapRequests.SendDocument(connection, place, codes, document, gzip, sha256, sentAt), cancellationToken));

    public async Task<SiatReply> SendPackageAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        byte[] tarGz, string sha256, DateTime sentAt, int documentCount, string eventReceptionCode, string? cafc,
        CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Service(await CallAsync(connection,
            SiatSoapRequests.SendPackage(connection, place, codes, document, tarGz, sha256, sentAt, documentCount, eventReceptionCode, cafc),
            cancellationToken));

    public async Task<SiatReply> ValidatePackageAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        string receptionCode, CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Service(await CallAsync(connection,
            SiatSoapRequests.ValidatePackage(connection, place, codes, document, receptionCode), cancellationToken));

    public async Task<SiatReply> VoidDocumentAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        string cuf, int reasonCode, CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Service(await CallAsync(connection,
            SiatSoapRequests.VoidDocument(connection, place, codes, document, cuf, reasonCode), cancellationToken));

    public async Task<SiatReply> RevertVoidAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        string cuf, CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Service(await CallAsync(connection,
            SiatSoapRequests.RevertVoid(connection, place, codes, document, cuf), cancellationToken));

    public async Task<SiatReply> CheckDocumentStatusAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        string cuf, CancellationToken cancellationToken = default) =>
        SiatSoapReplies.Service(await CallAsync(connection,
            SiatSoapRequests.CheckDocumentStatus(connection, place, codes, document, cuf), cancellationToken));

    // ------------------------------------------------------------------------------------------------ transporte
    /// <summary>Lo que se sabe de una llamada para la bitácora.</summary>
    private sealed class Outcome
    {
        public int? HttpStatus { get; set; }

        public string? ResponseBody { get; set; }

        public int? SiatCode { get; set; }

        public bool Succeeded { get; set; }

        public string? Error { get; set; }
    }

    private async Task<SiatSoapReader> CallAsync(SiatConnection connection, SiatSoapCall call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(connection.Token))
        {
            throw SiatGatewayErrors.MissingToken();
        }
        var envelope = call.Envelope(connection.Endpoints);
        var outcome = new Outcome();
        var occurredAt = clock.UtcNow;
        var watch = Stopwatch.StartNew();
        try
        {
            return await SendAsync(connection, call, SiatSoapEnvelope.Serialize(envelope), outcome, ct);
        }
        catch (Exception ex)
        {
            outcome.Error ??= ex.Message;
            throw;
        }
        finally
        {
            watch.Stop();
            await LogAsync(new SiatCallRecord(connection.Environment, call.Resource, call.Operation.Name, call.Place, occurredAt,
                (int)watch.ElapsedMilliseconds, outcome.HttpStatus, outcome.SiatCode, outcome.Succeeded,
                Scrub(SiatSoapEnvelope.ForLog(envelope), connection.Token), Scrub(outcome.ResponseBody, connection.Token),
                Scrub(outcome.Error, connection.Token)));
        }
    }

    private async Task<SiatSoapReader> SendAsync(SiatConnection connection, SiatSoapCall call, string body, Outcome outcome, CancellationToken ct)
    {
        var operation = call.Operation.Name;
        var url = SiatSoapContract.Url(connection.Endpoints, call.Resource);
        var limit = TimeLimit(connection.Timeout);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(body, new UTF8Encoding(false), SiatSoapContract.MediaType);
        request.Headers.TryAddWithoutValidation(SiatSoapContract.SoapActionHeader, SiatSoapContract.SoapActionValue);
        request.Headers.TryAddWithoutValidation(SiatSoapContract.ApiKeyHeader, SiatSoapContract.ApiKeyValue(connection.Token.Trim()));
        string text;
        HttpStatusCode status;
        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            status = response.StatusCode;
            outcome.HttpStatus = (int)status;
            text = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new SiatUnavailableException(
                $"El SIN no respondió {operation} en {limit.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s (tiempo agotado).", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SiatUnavailableException($"No hay comunicación con el SIN ({operation}): {ex.Message}", ex);
        }
        outcome.ResponseBody = text;
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw SiatGatewayErrors.TokenRejected($"HTTP {(int)status} en {operation}");
        }
        if ((int)status is < 200 or > 299)
        {
            var fault = SiatSoapReader.FaultText(text);
            throw new SiatUnavailableException(fault is null
                ? $"El SIN respondió HTTP {(int)status} en {operation}."
                : $"El SIN respondió HTTP {(int)status} en {operation}: {fault}");
        }
        var reader = SiatSoapReader.ParseResponse(text, operation);
        var messages = reader.Messages;
        outcome.SiatCode = SiatSoapReplies.SummaryCode(reader);
        outcome.Succeeded = reader.Transaction ?? outcome.SiatCode is SiatCodes.CommunicationOk or SiatCodes.ReceptionValidated;
        if (outcome.SiatCode == SiatCodes.InvalidToken || messages.Any(m => m.Code == SiatCodes.InvalidToken))
        {
            outcome.Succeeded = false;
            throw SiatGatewayErrors.TokenRejected($"código {SiatCodes.InvalidToken} en {operation}");
        }
        return reader;
    }

    private async Task LogAsync(SiatCallRecord record)
    {
        try
        {
            await callLog.RecordAsync(record, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("M-INV · la bitácora del SIN falló en {0}: {1}", record.Operation, ex.Message);
        }
    }

    /// <summary>Tiempo máximo de una llamada: el del ambiente; si no es válido, 30 s (nunca más de 10 minutos).</summary>
    internal static TimeSpan TimeLimit(TimeSpan configured) =>
        configured <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : configured > TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(10) : configured;

    /// <summary>Defensa en profundidad (regla F-12): si el token apareciera en un cuerpo o un error, no llega a la bitácora.</summary>
    internal static string? Scrub(string? text, string token) =>
        text is null || string.IsNullOrWhiteSpace(token) ? text : text.Replace(token.Trim(), "***", StringComparison.Ordinal);
}
