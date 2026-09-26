using System.Diagnostics;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Infrastructure.Billing.Soap;

namespace MINV.Infrastructure.Billing.Simulator;

/// <summary>
/// V4.1 · <see cref="ISiatGateway"/> sobre el simulador EN PROCESO (demostración y pruebas): llama al motor directamente,
/// con las mismas reglas que el cliente SOAP (sin token → <c>siat.no_token</c>; 989 → <c>siat.token_rejected</c>; simulador
/// apagado → <see cref="SiatUnavailableException"/>) y registra cada llamada en la bitácora técnica con los mismos cuerpos
/// SOAP que viajarían por la red (sin el token).
/// </summary>
public sealed class InProcessSiatGateway(SiatSimulatorEngine engine, ISiatCallLog callLog, IClock clock) : ISiatGateway
{
    public Task<SiatReply> CheckCommunicationAsync(SiatConnection connection, SiatResource resource, CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.CheckCommunication(resource), () => engine.CheckCommunication(connection.Token), cancellationToken);

    public Task<SiatCuisReply> RequestCuisAsync(SiatConnection connection, SiatPlace place, CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.Cuis(connection, place),
            () => engine.RequestCuis(connection.Token, SiatSimulatorCaller.From(connection), place), cancellationToken);

    public Task<SiatCufdReply> RequestCufdAsync(SiatConnection connection, SiatPlace place, string cuis, CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.Cufd(connection, place, cuis),
            () => engine.RequestCufd(connection.Token, SiatSimulatorCaller.From(connection), place, cuis), cancellationToken);

    public Task<SiatNitReply> VerifyNitAsync(SiatConnection connection, SiatPlace place, string cuis, long nitToVerify,
        CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.VerifyNit(connection, place, cuis, nitToVerify),
            () => engine.VerifyNit(connection.Token, SiatSimulatorCaller.From(connection), place.BranchCode, cuis, nitToVerify), cancellationToken);

    public Task<SiatCatalogReply> SyncCatalogAsync(SiatConnection connection, SiatPlace place, string cuis, string catalog,
        CancellationToken cancellationToken = default)
    {
        var call = SiatSoapRequests.Sync(connection, place, cuis, catalog);
        var name = SiatSoapContract.SyncFor(catalog).Catalog;
        return RunAsync(connection, call, () => engine.SyncCatalog(connection.Token, SiatSimulatorCaller.From(connection), place, cuis, name),
            cancellationToken);
    }

    public Task<SiatClockReply> SyncClockAsync(SiatConnection connection, SiatPlace place, string cuis, CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.Clock(connection, place, cuis),
            () => engine.SyncClock(connection.Token, SiatSimulatorCaller.From(connection), place, cuis), cancellationToken);

    public Task<SiatPointOfSaleReply> RegisterPointOfSaleAsync(SiatConnection connection, SiatPlace place, string cuis, int typeCode, string name,
        string description, CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.RegisterPointOfSale(connection, place, cuis, typeCode, name, description),
            () => engine.RegisterPointOfSale(connection.Token, SiatSimulatorCaller.From(connection), place.BranchCode, cuis, typeCode, name, description),
            cancellationToken);

    public Task<SiatPointOfSaleListReply> ListPointsOfSaleAsync(SiatConnection connection, SiatPlace place, string cuis,
        CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.ListPointsOfSale(connection, place, cuis),
            () => engine.ListPointsOfSale(connection.Token, SiatSimulatorCaller.From(connection), place.BranchCode, cuis), cancellationToken);

    public Task<SiatReply> ClosePointOfSaleAsync(SiatConnection connection, SiatPlace place, string cuis, CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.ClosePointOfSale(connection, place, cuis),
            () => engine.ClosePointOfSale(connection.Token, SiatSimulatorCaller.From(connection), place, cuis), cancellationToken);

    public Task<SiatEventReply> RegisterEventAsync(SiatConnection connection, SiatPlace place, string cuis, string cufd, int eventCode,
        string description, DateTime startedAt, DateTime endedAt, string eventCufd, CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.RegisterEvent(connection, place, cuis, cufd, eventCode, description, startedAt, endedAt, eventCufd),
            () => engine.RegisterEvent(connection.Token, SiatSimulatorCaller.From(connection), place, cuis, cufd, eventCode, description,
                Milliseconds(startedAt), Milliseconds(endedAt), eventCufd),
            cancellationToken);

    public Task<SiatReply> SendDocumentAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        byte[] gzip, string sha256, DateTime sentAt, CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.SendDocument(connection, place, codes, document, gzip, sha256, sentAt),
            () => engine.ReceiveDocument(connection.Token, Call(connection, place, codes, document, SiatCodes.EmissionOnline), gzip, sha256,
                Milliseconds(sentAt)),
            cancellationToken);

    public Task<SiatReply> SendPackageAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        byte[] tarGz, string sha256, DateTime sentAt, int documentCount, string eventReceptionCode, string? cafc,
        CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.SendPackage(connection, place, codes, document, tarGz, sha256, sentAt, documentCount, eventReceptionCode, cafc),
            () => engine.ReceivePackage(connection.Token, Call(connection, place, codes, document, SiatCodes.EmissionOffline), tarGz, sha256,
                Milliseconds(sentAt), documentCount, eventReceptionCode, cafc),
            cancellationToken);

    public Task<SiatReply> ValidatePackageAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        string receptionCode, CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.ValidatePackage(connection, place, codes, document, receptionCode),
            () => engine.ValidatePackage(connection.Token, Call(connection, place, codes, document, SiatCodes.EmissionOffline), receptionCode),
            cancellationToken);

    public Task<SiatReply> VoidDocumentAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        string cuf, int reasonCode, CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.VoidDocument(connection, place, codes, document, cuf, reasonCode),
            () => engine.VoidDocument(connection.Token, Call(connection, place, codes, document, SiatCodes.EmissionOnline), cuf, reasonCode),
            cancellationToken);

    public Task<SiatReply> RevertVoidAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        string cuf, CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.RevertVoid(connection, place, codes, document, cuf),
            () => engine.RevertVoid(connection.Token, Call(connection, place, codes, document, SiatCodes.EmissionOnline), cuf),
            cancellationToken);

    public Task<SiatReply> CheckDocumentStatusAsync(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        string cuf, CancellationToken cancellationToken = default) =>
        RunAsync(connection, SiatSoapRequests.CheckDocumentStatus(connection, place, codes, document, cuf),
            () => engine.CheckDocumentStatus(connection.Token, Call(connection, place, codes, document, SiatCodes.EmissionOnline), cuf),
            cancellationToken);

    // ------------------------------------------------------------------------------------------------ ejecución
    private async Task<T> RunAsync<T>(SiatConnection connection, SiatSoapCall call, Func<T> invoke, CancellationToken ct)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(connection);
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(connection.Token))
        {
            throw SiatGatewayErrors.MissingToken();
        }
        var occurredAt = clock.UtcNow;
        var watch = Stopwatch.StartNew();
        T? reply = default;
        string? error = null;
        try
        {
            reply = invoke();
            var (_, _, messages) = Summarize(reply);
            if (messages.Any(m => m.Code == SiatCodes.InvalidToken))
            {
                throw SiatGatewayErrors.TokenRejected($"código {SiatCodes.InvalidToken} en {call.Operation.Name}");
            }
            return reply;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            throw;
        }
        finally
        {
            watch.Stop();
            await LogAsync(connection, call, reply, occurredAt, (int)watch.ElapsedMilliseconds, error);
        }
    }

    private async Task LogAsync(SiatConnection connection, SiatSoapCall call, object? reply, DateTimeOffset occurredAt, int durationMs, string? error)
    {
        try
        {
            var ns = SiatSoapContract.Namespace(connection.Endpoints);
            string? response = null;
            int? code = null;
            var succeeded = false;
            if (reply is not null)
            {
                (succeeded, code, _) = Summarize(reply);
                response = SiatSoapEnvelope.Serialize(SiatSoapEnvelope.Response(ns, call.Operation.Name, SiatSimulatorSoapWriter.Payload(call.Operation, reply)));
            }
            await callLog.RecordAsync(new SiatCallRecord(connection.Environment, call.Resource, call.Operation.Name, call.Place, occurredAt, durationMs,
                null, code, succeeded && error is null, SiatSoapGateway.Scrub(SiatSoapEnvelope.ForLog(call.Envelope(ns)), connection.Token),
                SiatSoapGateway.Scrub(response, connection.Token), SiatSoapGateway.Scrub(error, connection.Token)), CancellationToken.None);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("M-INV · la bitácora del SIN (simulador) falló en {0}: {1}", call.Operation.Name, ex.Message);
        }
    }

    /// <summary>Transacción, código que resume la respuesta y mensajes, para cualquier respuesta del puerto.</summary>
    private static (bool Transaction, int? Code, IReadOnlyList<SiatMessage> Messages) Summarize(object reply) => reply switch
    {
        SiatReply r => (r.Transaction, r.StatusCode ?? r.Messages.FirstOrDefault()?.Code, r.Messages),
        SiatCuisReply r => (r.Transaction, r.Messages.FirstOrDefault()?.Code, r.Messages),
        SiatCufdReply r => (r.Transaction, r.Messages.FirstOrDefault()?.Code, r.Messages),
        SiatNitReply r => (r.Transaction, r.Code, r.Messages),
        SiatCatalogReply r => (r.Transaction, r.Messages.FirstOrDefault()?.Code, r.Messages),
        SiatClockReply r => (r.Transaction, r.Messages.FirstOrDefault()?.Code, r.Messages),
        SiatPointOfSaleReply r => (r.Transaction, r.Messages.FirstOrDefault()?.Code, r.Messages),
        SiatPointOfSaleListReply r => (r.Transaction, r.Messages.FirstOrDefault()?.Code, r.Messages),
        SiatEventReply r => (r.Transaction, r.Messages.FirstOrDefault()?.Code, r.Messages),
        _ => throw new ArgumentException($"Respuesta no reconocida: {reply.GetType().Name}.", nameof(reply)),
    };

    private static SiatSimulatorDocumentCall Call(SiatConnection connection, SiatPlace place, SiatCodesForCall codes, SiatDocumentRef document,
        int emission)
    {
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(document);
        return new(SiatSimulatorCaller.From(connection), place, document.Resource, document.DocumentSector, document.DocumentType, emission, codes.Cuis,
            codes.Cufd);
    }

    /// <summary>Las fechas viajan con milisegundos (yyyy-MM-ddTHH:mm:ss.fff): se truncan igual que en el cable.</summary>
    private static DateTime Milliseconds(DateTime value) => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMillisecond));
}
