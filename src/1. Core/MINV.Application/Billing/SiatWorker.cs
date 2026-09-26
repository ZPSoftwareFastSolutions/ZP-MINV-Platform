using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Application.Billing;

/// <summary>V4.1 · Resultado de la recuperación de un punto de venta fuera de línea (o en contingencia terminada).</summary>
public sealed record PointRecoveryResult(bool Recovered, bool Communicated, int EventsRegistered, int DocumentsPackaged, string Message);

/// <summary>
/// V4.1 · Trabajo de la facturación SIAT de UNA empresa (regla F-09): envía los documentos pendientes y los re-emite fuera
/// de línea cuando no hay comunicación (flujo C-07); recupera los puntos fuera de línea (verificar comunicación → CUFD
/// nuevo → cerrar y registrar el evento → verificar los documentos sin respuesta y anular duplicados → paquetes ≤ 500 →
/// validación); emite las notas crédito-débito pendientes y entrega los correos. Lo usan la caja
/// (<see cref="DispatchFiscalDocumentsCommand"/>), el botón «Procesar ahora» (<see cref="RunSiatWorkCommand"/>), los
/// comandos de contingencia y el servicio en segundo plano. Guarda después de cada respuesta del SIN: lo que el SIN ya
/// sabe no se pierde por un error posterior.
/// </summary>
public sealed class SiatWorker(IMinvDbContext db, ISiatGateway gateway, IFiscalDocumentSerializer serializer, ICurrentUser user, IClock clock,
    ISecretProtector? protector = null, IMailSender? mailSender = null, IFiscalDocumentRenderer? renderer = null) : ISiatWorker
{
    /// <summary>Códigos del SIN de NIT del comprador inválido (1037) o inexistente (994): se re-emite con excepción 1.</summary>
    private static readonly int[] InvalidBuyerNitCodes = [1037, SiatCodes.NitNotFound];

    /// <summary>CUF ya registrado en el SIN (el envío anterior llegó aunque se perdiera la respuesta).</summary>
    private const int CufAlreadyRegistered = 952;

    private const string TokenRejected = "siat.token_rejected";

    /// <summary>Intentos de entrega por correo de un documento antes de dejar de insistir.</summary>
    private const int MaxEmailAttempts = 3;

    private BillingLookups NewLookups() => new(db, protector, clock);

    private FiscalIssueServices Services(string? userCode = null) =>
        new(db, clock, serializer, user.UserId, userCode ?? BillingLookups.UserCode(user));

    // ================================================================================================ envío
    public async Task<DispatchResult> DispatchAsync(Guid? documentId, int max, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var lookups = NewLookups();
        if (!await lookups.IsBillingEnabledAsync(ct))
        {
            return new DispatchResult(0, 0, 0, 0, [], []);
        }
        FiscalContext context;
        try
        {
            context = await lookups.ContextAsync(ct);
        }
        catch (DomainException ex)
        {
            return new DispatchResult(0, 0, 0, 0, [], [ex.Message]);
        }
        var queue = new Queue<Guid>();
        if (documentId is { } id)
        {
            queue.Enqueue(id);
        }
        else
        {
            foreach (var pending in await db.Set<FiscalDocument>()
                         .Where(d => d.Status == FiscalDocumentStatus.Pending && d.Environment == context.Environment)
                         .OrderBy(d => d.CreatedAt).Select(d => d.Id).Take(Math.Clamp(max, 1, 500)).ToListAsync(ct))
            {
                queue.Enqueue(pending);
            }
        }
        var tally = new DispatchTally();
        var processed = new List<Guid>();
        while (queue.TryDequeue(out var next))
        {
            if (processed.Contains(next))
            {
                continue;
            }
            processed.Add(next);
            try
            {
                var outcome = await DispatchOneAsync(context, next, ct);
                tally.Add(outcome);
                foreach (var followUp in outcome.FollowUps)
                {
                    queue.Enqueue(followUp);
                }
            }
            catch (DomainException ex) when (ex.Code == TokenRejected)
            {
                tally.Messages.Add(ex.Message);
                break;
            }
            catch (DomainException ex)
            {
                db.ClearTracking();
                tally.Messages.Add(ex.Message);
            }
        }
        var finals = new List<Guid>();
        foreach (var docId in processed)
        {
            finals.Add(await FinalReplacementAsync(docId, ct));
        }
        var rows = await IssuedDocumentRows.ForAsync(db, clock, finals.Distinct().ToList(), ct);
        return new DispatchResult(tally.Sent, tally.Valid, tally.Rejected, tally.WentOffline, rows, tally.Messages);
    }

    /// <summary>Qué pasó con un documento en el envío (para los contadores) y qué documentos quedan por procesar.</summary>
    private sealed record DispatchOutcome(bool Sent, bool Valid, bool Rejected, bool WentOffline, IReadOnlyList<Guid> FollowUps, string? Message)
    {
        public static readonly DispatchOutcome Nothing = new(false, false, false, false, [], null);
    }

    private sealed class DispatchTally
    {
        public int Sent { get; private set; }

        public int Valid { get; private set; }

        public int Rejected { get; private set; }

        public int WentOffline { get; private set; }

        public List<string> Messages { get; } = [];

        public void Add(DispatchOutcome outcome)
        {
            Sent += outcome.Sent ? 1 : 0;
            Valid += outcome.Valid ? 1 : 0;
            Rejected += outcome.Rejected ? 1 : 0;
            WentOffline += outcome.WentOffline ? 1 : 0;
            if (outcome.Message is { Length: > 0 } message && !Messages.Contains(message))
            {
                Messages.Add(message);
            }
        }
    }

    private async Task<DispatchOutcome> DispatchOneAsync(FiscalContext context, Guid documentId, CancellationToken ct)
    {
        var document = await LoadDocumentAsync(documentId, ct);
        if (document is null || document.Status != FiscalDocumentStatus.Pending)
        {
            return DispatchOutcome.Nothing;
        }
        var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == document.PointOfSaleId, ct);
        var now = clock.UtcNow;
        var cufd = await db.Set<SiatCufd>().FirstAsync(c => c.Id == document.CufdId, ct);
        if (!point.IsOnline)
        {
            // El punto ya está fuera de línea: el documento no se envía y la venta se re-emite fuera de línea (C-07)
            return await PersistAsync(() => ReissueUnsentAsync(documentId,
                "No se envió: el punto de venta está fuera de línea. La venta se re-emite fuera de línea.", ct), ct);
        }
        if (!cufd.IsValidAt(now))
        {
            // El CUFD del documento venció antes de enviarlo (el SIN respondería 953): se descarta y se re-emite con el vigente
            return await PersistAsync(() => DiscardExpiredAsync(documentId, ct), ct);
        }
        var file = await db.Set<FiscalDocumentFile>().FirstAsync(f => f.DocumentId == document.Id, ct);
        var cuis = await db.Set<SiatCuis>().FirstAsync(c => c.Id == document.CuisId, ct);
        var gzip = serializer.Gzip(file.Xml);
        var sha = serializer.Sha256Hex(gzip);
        var place = BillingLookups.PlaceOf(document);
        var reference = new SiatDocumentRef(document.DocumentSector, document.DocumentType);
        var codes = new SiatCodesForCall(cuis.Code, cufd.Code);   // el CUFD de la solicitud es el del XML
        SiatReply? reply = null;
        SiatUnavailableException? failure = null;
        for (var attempt = 1; attempt <= 2 && reply is null; attempt++)
        {
            try
            {
                reply = await gateway.SendDocumentAsync(context.Connection, place, codes, reference, gzip, sha,
                    FiscalIssuer.Milliseconds(NewLookups().FiscalNow(context)), ct);
            }
            catch (SiatUnavailableException ex)
            {
                failure = ex;   // un reintento; si vuelve a fallar, fuera de línea
            }
        }
        if (reply is not null && !reply.Has(SiatCodes.ReceptionValidated) && reply.Has(CufAlreadyRegistered))
        {
            // El SIN ya tiene ese CUF (se perdió una respuesta anterior): se verifica su estado
            try
            {
                var status = await gateway.CheckDocumentStatusAsync(context.Connection, place, codes, reference, document.Cuf, ct);
                if (status.Transaction && status.StatusCode == SiatCodes.ReceptionValidated)
                {
                    reply = status;
                }
            }
            catch (SiatUnavailableException)
            {
                // se deja el rechazo: la verificación quedará para la próxima pasada
            }
        }
        return await PersistAsync(() => ApplySendAsync(documentId, reply, failure, ct), ct);
    }

    /// <summary>Aplica la respuesta del envío (o la falta de ella) al documento recién leído.</summary>
    private async Task<DispatchOutcome> ApplySendAsync(Guid documentId, SiatReply? reply, SiatUnavailableException? failure, CancellationToken ct)
    {
        var document = await LoadDocumentAsync(documentId, ct);
        if (document is null || document.Status != FiscalDocumentStatus.Pending)
        {
            return DispatchOutcome.Nothing;   // otro proceso ya lo resolvió
        }
        var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == document.PointOfSaleId, ct);
        var now = clock.UtcNow;
        if (reply is not null)
        {
            if (point.ConsecutiveFailures > 0 || point.LastContactAt is null || now - point.LastContactAt > TimeSpan.FromMinutes(5))
            {
                point.RecordContact(now);   // se actualiza de vez en cuando: evita choques de concurrencia entre cajas
            }
            if (reply.Has(SiatCodes.ReceptionValidated))
            {
                document.Accept(reply.ReceptionCode, SiatCodes.ReceptionValidated, now);
                db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Accepted, now, user.UserId, reply));
                return new DispatchOutcome(true, true, false, false, [], null);
            }
            var errors = reply.Messages.Where(m => !m.IsWarning).ToList();
            var code = reply.StatusCode ?? errors.FirstOrDefault()?.Code ?? SiatCodes.ReceptionRejected;
            document.Reject(code);
            db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Rejected, now, user.UserId, reply));
            if (document.Kind == FiscalDocumentKind.Invoice && document.BuyerDocumentType == SiatCodes.DocumentNit && document.ExceptionCode == 0
                && errors.Count > 0 && errors.All(m => InvalidBuyerNitCodes.Contains(m.Code)))
            {
                // Único error: el NIT del comprador. Se re-emite con código de excepción 1 (documento nuevo que reemplaza)
                await FlushAsync(ct);
                var replacement = await FiscalIssuer.ReissueInvoiceAsync(Services(document.UserCode), document, FiscalIssuer.BuyerOf(document), true, null,
                    ct);
                db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Reissued, now, user.UserId,
                    description: $"NIT del comprador observado por el SIN: re-emitido con código de excepción 1 (N° {replacement.Number})."));
                return new DispatchOutcome(true, false, true, false, [replacement.Id], null);
            }
            return new DispatchOutcome(true, false, true, false, [],
                $"{Title(document)} N° {document.Number} rechazada por el SIN: {reply.Describe()}. Corrija los datos y vuelva a emitirla.");
        }

        // Sin comunicación después del reintento: sin respuesta, punto fuera de línea y re-emisión fuera de línea (C-07)
        var error = failure?.Message ?? "El SIN no respondió.";
        document.MarkNoResponse();
        db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.NoResponse, now, user.UserId,
            description: "Sin respuesta del SIN al enviar (dos intentos): se verificará su estado al recuperar la comunicación. " + error));
        point.RecordFailure(error, now);
        var evt = await FiscalIssuer.OpenOfflineEventAsync(db, point.Id, ct)
                  ?? await OpenEventAsync(point, document.CufdId, IsNetworkFailure(failure), now, ct);
        point.GoOffline(now);
        var others = await db.Set<FiscalDocument>()
            .Where(d => d.PointOfSaleId == point.Id && d.Status == FiscalDocumentStatus.Pending && d.Id != document.Id)
            .Select(d => d.Id).ToListAsync(ct);
        if (document.Kind != FiscalDocumentKind.Invoice)
        {
            return new DispatchOutcome(true, false, false, false, others,
                $"Nota crédito-débito N° {document.Number} sin respuesta del SIN: se verificará al recuperar la comunicación (las notas no se emiten fuera de línea).");
        }
        await FlushAsync(ct);
        var offline = await FiscalIssuer.ReissueInvoiceAsync(Services(document.UserCode), document, FiscalIssuer.BuyerOf(document),
            document.ExceptionCode == 1, evt, ct);
        db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Reissued, now, user.UserId,
            description: $"Re-emitida fuera de línea como N° {offline.Number} (evento {evt.Description}): ese es el documento del comprador."));
        return new DispatchOutcome(true, false, false, true, others,
            $"Sin comunicación con el SIN: el punto de venta {point.Code} pasó a fuera de línea y las ventas se emiten fuera de línea.");
    }

    /// <summary>Pendiente de un punto que ya está fuera de línea: no se envía; queda sin respuesta (se verificará) y la venta
    /// se re-emite fuera de línea. Las notas esperan a la comunicación.</summary>
    private async Task<DispatchOutcome> ReissueUnsentAsync(Guid documentId, string reason, CancellationToken ct)
    {
        var document = await LoadDocumentAsync(documentId, ct);
        if (document is null || document.Status != FiscalDocumentStatus.Pending)
        {
            return DispatchOutcome.Nothing;
        }
        if (document.Kind != FiscalDocumentKind.Invoice)
        {
            return new DispatchOutcome(false, false, false, false, [],
                $"Nota crédito-débito N° {document.Number}: se enviará cuando el punto de venta vuelva a estar en línea.");
        }
        var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == document.PointOfSaleId, ct);
        var now = clock.UtcNow;
        document.MarkNoResponse();
        db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.NoResponse, now, user.UserId, description: reason));
        var evt = await FiscalIssuer.OpenOfflineEventAsync(db, point.Id, ct) ?? await OpenEventAsync(point, document.CufdId, false, now, ct);
        await FlushAsync(ct);
        var offline = await FiscalIssuer.ReissueInvoiceAsync(Services(document.UserCode), document, FiscalIssuer.BuyerOf(document),
            document.ExceptionCode == 1, evt, ct);
        db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Reissued, now, user.UserId,
            description: $"Re-emitida fuera de línea como N° {offline.Number} (evento {evt.Description})."));
        return new DispatchOutcome(false, false, false, true, [], null);
    }

    /// <summary>El CUFD del documento venció antes de enviarlo: nunca llegó al SIN, se descarta y la venta recibe un
    /// documento nuevo con el CUFD vigente (las notas las vuelve a emitir el trabajo automático).</summary>
    private async Task<DispatchOutcome> DiscardExpiredAsync(Guid documentId, CancellationToken ct)
    {
        var document = await LoadDocumentAsync(documentId, ct);
        if (document is null || document.Status != FiscalDocumentStatus.Pending)
        {
            return DispatchOutcome.Nothing;
        }
        var now = clock.UtcNow;
        document.MarkNoResponse();
        document.ResolveNoResponse(false, true, null, now);
        db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Discarded, now, user.UserId,
            description: "El CUFD del documento venció antes de enviarlo al SIN: se descarta y se emite uno nuevo con el CUFD vigente."));
        if (document.Kind != FiscalDocumentKind.Invoice)
        {
            return new DispatchOutcome(false, false, false, false, [], null);
        }
        await FlushAsync(ct);
        var replacement = await FiscalIssuer.ReissueInvoiceAsync(Services(document.UserCode), document, FiscalIssuer.BuyerOf(document),
            document.ExceptionCode == 1, null, ct);
        return new DispatchOutcome(false, false, false, replacement.EmissionType == SiatCodes.EmissionOffline,
            replacement.Status == FiscalDocumentStatus.Pending ? [replacement.Id] : [], null);
    }

    /// <summary>Evento automático por la falla: corte de internet (red o tiempo agotado) o inaccesibilidad al servicio del
    /// SIN (respondió con error), con el CUFD del documento como CUFD del evento (o el último usable).</summary>
    private async Task<SignificantEvent> OpenEventAsync(SiatPointOfSale point, Guid cufdId, bool network, DateTimeOffset now, CancellationToken ct)
    {
        var lookups = NewLookups();
        var cufd = await db.Set<SiatCufd>().FirstAsync(c => c.Id == cufdId, ct);
        if (!cufd.IsUsableOfflineAt(now))
        {
            cufd = await lookups.LatestCufdAsync(point.Id, ct) is { } latest && latest.IsUsableOfflineAt(now)
                ? latest
                : throw new DomainException("siat.no_cufd",
                    "No hay comunicación con el SIN y el punto de venta no tiene un CUFD de las últimas 72 horas: no se puede emitir fuera de línea.");
        }
        var settings = await lookups.RequireSettingsAsync(ct);
        var fiscalNow = lookups.FiscalNow(settings, await lookups.ZoneAsync(ct));
        return network
            ? await FiscalIssuer.OpenAutomaticEventAsync(db, point, cufd, FiscalIssuer.InternetEvent, FiscalIssuer.DefaultInternetEventCode, fiscalNow, now,
                user.UserId, ct)
            : await FiscalIssuer.OpenAutomaticEventAsync(db, point, cufd, FiscalIssuer.WebServiceEvent, FiscalIssuer.DefaultWebServiceEventCode, fiscalNow,
                now, user.UserId, ct);
    }

    /// <summary>¿La falla fue de red o de tiempo (corte de internet) y no una respuesta de error del SIN?</summary>
    private static bool IsNetworkFailure(Exception? failure)
    {
        for (var inner = failure?.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is HttpRequestException { StatusCode: null } or OperationCanceledException or TimeoutException or SocketException or IOException)
            {
                return true;
            }
        }
        return false;
    }

    // ================================================================================================ mantenimiento
    public async Task<SiatMaintenanceResult> MaintainAsync(bool force, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var lookups = NewLookups();
        if (!await lookups.IsBillingEnabledAsync(ct))
        {
            return new SiatMaintenanceResult(0, 0, 0, 0, 0, []);
        }
        FiscalContext context;
        try
        {
            context = await lookups.ContextAsync(ct);
        }
        catch (DomainException ex)
        {
            return new SiatMaintenanceResult(0, 0, 0, 0, 0, [ex.Message]);
        }
        var tally = new MaintenanceTally();
        try
        {
            await DailyAsync(lookups, context, force, tally, ct);
            await RecoverPointsAsync(context, force, tally, ct);
            await ProcessOnlineEventsAsync(context, tally, ct);
            await ValidatePackagesAsync(context, tally, ct);
            await ResolvePendingAsync(context, tally, ct);
            await ReconcileEventsAsync(context, tally, ct);
            await IssuePendingCreditNotesAsync(context, tally, ct);
            await SendPendingEmailsAsync(context, tally, ct);
        }
        catch (DomainException ex) when (ex.Code == TokenRejected)
        {
            tally.Messages.Add(ex.Message);
        }
        return tally.ToResult();
    }

    private sealed class MaintenanceTally
    {
        public int CuisRequested { get; set; }

        public int CufdRequested { get; set; }

        public int Recovered { get; set; }

        public int PackagesValidated { get; set; }

        public int DocumentsSent { get; set; }

        public List<string> Messages { get; } = [];

        public void Note(string? message)
        {
            if (message is { Length: > 0 } && !Messages.Contains(message))
            {
                Messages.Add(message);
            }
        }

        public SiatMaintenanceResult ToResult() => new(CuisRequested, CufdRequested, Recovered, PackagesValidated, DocumentsSent, Messages);
    }

    /// <summary>Ejecuta un paso por elemento: la falla de uno se registra y no detiene a los demás.</summary>
    private async Task EachAsync<T>(IEnumerable<T> items, Func<T, Task> action, Func<T, string> label, MaintenanceTally tally)
    {
        foreach (var item in items)
        {
            try
            {
                await action(item);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not DomainException { Code: TokenRejected })
            {
                db.ClearTracking();
                tally.Note($"{label(item)}: {ex.Message}");
            }
        }
    }

    /// <summary>Mantenimiento diario de los códigos (CUIS, CUFD, hora del SIN y catálogos).</summary>
    private async Task DailyAsync(BillingLookups lookups, FiscalContext context, bool force, MaintenanceTally tally, CancellationToken ct)
    {
        try
        {
            var daily = await SiatDailyMaintenance.RunAsync(lookups, new SiatCodeManager(lookups, gateway), context, force, user.UserId, ct);
            await db.SaveChangesAsync(ct);
            tally.CuisRequested += daily.CuisRequested;
            tally.CufdRequested += daily.CufdRequested;
            tally.Recovered += daily.Recovered;
            tally.PackagesValidated += daily.PackagesValidated;
            tally.DocumentsSent += daily.DocumentsSent;
            foreach (var message in daily.Messages)
            {
                tally.Note(message);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not DomainException { Code: TokenRejected })
        {
            db.ClearTracking();
            tally.Note("Mantenimiento diario de códigos: " + ex.Message);
        }
    }

    /// <summary>Puntos fuera de línea con el reintento vencido (o forzados) y los que están recuperando.</summary>
    private async Task RecoverPointsAsync(FiscalContext context, bool force, MaintenanceTally tally, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var points = await db.Set<SiatPointOfSale>()
            .Where(p => p.Environment == context.Environment && p.ClosedAt == null
                                                             && (p.Mode == SiatConnectionMode.Offline || p.Mode == SiatConnectionMode.Recovering))
            .Select(p => new { p.Id, p.Code, p.Mode, p.RetryAt }).ToListAsync(ct);
        var due = points.Where(p => force || p.Mode == SiatConnectionMode.Recovering || p.RetryAt is null || p.RetryAt <= now).ToList();
        await EachAsync(due, async p =>
        {
            var result = await RecoverAsync(context, p.Id, ct);
            tally.Recovered += result.Recovered ? 1 : 0;
            tally.DocumentsSent += result.DocumentsPackaged;
            tally.Note(result.Message);
        }, p => $"Punto de venta {p.Code}", tally);
    }

    /// <summary>
    /// Recupera ahora un punto de venta (comandos «Recuperar» y «Fin de la contingencia»): verifica la comunicación y, si
    /// responde, ejecuta la recuperación completa. Sin comunicación deja el punto como está (el trabajo automático reintenta).
    /// </summary>
    public async Task<PointRecoveryResult> RecoverPointAsync(Guid pointOfSaleId, CancellationToken cancellationToken = default)
    {
        var context = await NewLookups().ContextAsync(cancellationToken);
        try
        {
            return await RecoverAsync(context, pointOfSaleId, cancellationToken);
        }
        catch (SiatUnavailableException ex)
        {
            db.ClearTracking();
            return new PointRecoveryResult(false, false, 0, 0, $"Se perdió la comunicación con el SIN durante la recuperación: {ex.Message}");
        }
    }

    /// <summary>Recuperación (03 §16.1): comunicación → CUFD nuevo → cierre y registro de eventos → verificación de los
    /// documentos sin respuesta → paquetes → en línea. La validación de los paquetes la hace el mantenimiento.</summary>
    private async Task<PointRecoveryResult> RecoverAsync(FiscalContext context, Guid pointOfSaleId, CancellationToken ct)
    {
        var lookups = NewLookups();
        var codes = new SiatCodeManager(lookups, gateway);
        var now = clock.UtcNow;
        var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == pointOfSaleId, ct);
        var label = $"Punto de venta {point.Code} ({point.Name})";
        if (point.Mode == SiatConnectionMode.ManualContingency)
        {
            return new PointRecoveryResult(false, false, 0, 0, $"{label}: en contingencia manual; declare el fin de la contingencia para recuperarlo.");
        }
        if (point.Mode == SiatConnectionMode.Online)
        {
            return new PointRecoveryResult(false, true, 0, 0, $"{label}: ya está en línea.");
        }
        try
        {
            var check = await gateway.CheckCommunicationAsync(context.Connection, SiatResource.PurchaseSale, ct);
            if (!check.Transaction && !check.Has(SiatCodes.CommunicationOk))
            {
                return new PointRecoveryResult(false, true, 0, 0, $"{label}: el SIN no confirmó la comunicación ({check.Describe()}).");
            }
        }
        catch (SiatUnavailableException ex)
        {
            point.RecordFailure(ex.Message, now);
            await db.SaveChangesAsync(ct);
            var retry = point.RetryAt is { } at ? $" (próximo intento {TimeZoneInfo.ConvertTime(at, context.Zone):HH:mm})" : string.Empty;
            return new PointRecoveryResult(false, false, 0, 0, $"{label}: sigue sin comunicación con el SIN{retry}.");
        }
        point.StartRecovery(now);
        point.RecordContact(now);
        await db.SaveChangesAsync(ct);

        // CUFD NUEVO antes de registrar el evento y enviar los paquetes (C-18); CUIS vigente (se renueva si hace falta)
        var branch = await lookups.BranchMappingAsync(point.BranchId, ct);
        var cuis = await codes.EnsureCuisAsync(context, branch, point, false, ct);
        var cufd = await codes.EnsureCufdAsync(context, branch, point, true, ct);
        await db.SaveChangesAsync(ct);

        // Cierre de los eventos abiertos: fin = hora fiscal actual (nunca antes de la última factura fuera de línea)
        var fiscalNow = FiscalIssuer.Milliseconds(lookups.FiscalNow(context));
        foreach (var evt in await db.Set<SignificantEvent>().Where(e => e.PointOfSaleId == point.Id && e.Status == SignificantEventStatus.Open)
                     .ToListAsync(ct))
        {
            var last = await db.Set<FiscalDocument>().Where(d => d.SignificantEventId == evt.Id).MaxAsync(d => (DateTime?)d.IssuedAt, ct);
            var end = new[] { fiscalNow, last ?? fiscalNow, evt.StartedAt.AddMilliseconds(1) }.Max();
            evt.Close(end);
        }
        await db.SaveChangesAsync(ct);

        var place = BillingLookups.Place(branch, point);
        var messages = new List<string>();
        var registered = await RegisterEventsAsync(context, point, place, cuis, cufd, messages, ct);
        await ResolveDocumentsAsync(context, point.Id, cuis, cufd, messages, ct);
        var packaged = await SendPackagesAsync(context, point, place, cuis, cufd, messages, ct);
        point.BackOnline(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        var detail = messages.Count > 0 ? " " + string.Join(" ", messages) : string.Empty;
        return new PointRecoveryResult(true, true, registered, packaged,
            $"{label}: comunicación recuperada; {registered} evento(s) registrado(s) y {packaged} documento(s) enviado(s) en paquetes.{detail}");
    }

    /// <summary>Registra en el SIN los eventos cerrados del punto (cufd = el NUEVO, cufdEvento = el del evento).</summary>
    private async Task<int> RegisterEventsAsync(FiscalContext context, SiatPointOfSale point, SiatPlace place, SiatCuis cuis, SiatCufd cufd,
        List<string> messages, CancellationToken ct)
    {
        var registered = 0;
        var fiscalNow = NewLookups().FiscalNow(context);
        var closed = await db.Set<SignificantEvent>().Where(e => e.PointOfSaleId == point.Id && e.Status == SignificantEventStatus.Closed)
            .OrderBy(e => e.StartedAt).ToListAsync(ct);
        foreach (var evt in closed)
        {
            if (evt.RegistrationDeadline is { } deadline && fiscalNow > deadline)
            {
                messages.Add($"El evento «{evt.Description}» del {evt.StartedAt:dd/MM/yyyy HH:mm} superó las 48 h para registrarse: regístrelo con el SIN.");
                continue;
            }
            var eventCufd = await db.Set<SiatCufd>().FirstAsync(c => c.Id == evt.EventCufdId, ct);
            var reply = await gateway.RegisterEventAsync(context.Connection, place, cuis.Code, cufd.Code, evt.EventCode, evt.Description, evt.StartedAt,
                evt.EndedAt!.Value, eventCufd.Code, ct);
            if (reply.Transaction && reply.ReceptionCode is { Length: > 0 } receptionCode)
            {
                evt.Register(cufd.Id, receptionCode, clock.UtcNow);
                await db.SaveChangesAsync(ct);
                registered++;
            }
            else
            {
                messages.Add($"El SIN no registró el evento «{evt.Description}»: {SiatCodeManager.Describe(reply.Messages)}.");
            }
        }
        return registered;
    }

    /// <summary>
    /// Documentos sin respuesta del punto (o de todos los puntos en línea): verificación por CUF — registrado y re-emitido →
    /// duplicado que se anula con el motivo «FACTURA MAL EMITIDA»; registrado y no re-emitido → válido; no registrado →
    /// descartado (si nadie lo reemplazó, la venta recibe uno nuevo). También reintenta anular los duplicados pendientes.
    /// </summary>
    private async Task ResolveDocumentsAsync(FiscalContext context, Guid pointOfSaleId, SiatCuis cuis, SiatCufd cufd, List<string> messages,
        CancellationToken ct)
    {
        var ids = await db.Set<FiscalDocument>()
            .Where(d => d.PointOfSaleId == pointOfSaleId
                        && (d.Status == FiscalDocumentStatus.NoResponse || d.Status == FiscalDocumentStatus.DuplicateToVoid))
            .OrderBy(d => d.CreatedAt).Select(d => d.Id).ToListAsync(ct);
        var codes = new SiatCodesForCall(cuis.Code, cufd.Code);
        foreach (var id in ids)
        {
            var document = await LoadDocumentAsync(id, ct);
            if (document is null)
            {
                continue;
            }
            var now = clock.UtcNow;
            var place = BillingLookups.PlaceOf(document);
            var reference = new SiatDocumentRef(document.DocumentSector, document.DocumentType);
            if (document.Status == FiscalDocumentStatus.NoResponse)
            {
                var status = await gateway.CheckDocumentStatusAsync(context.Connection, place, codes, reference, document.Cuf, ct);
                var registered = status.Transaction && status.StatusCode is SiatCodes.ReceptionValidated or SiatCodes.VoidConfirmed;
                var missing = !status.Transaction && (status.Has(924) || status.Has(946));
                if (!registered && !missing)
                {
                    messages.Add($"{Title(document)} N° {document.Number}: el SIN no confirmó su estado ({status.Describe()}); se reintentará.");
                    continue;
                }
                var reissued = await db.Set<FiscalDocument>().AnyAsync(d => d.ReplacesDocumentId == document.Id, ct);
                document.ResolveNoResponse(registered, reissued, status.StatusCode ?? status.Messages.FirstOrDefault()?.Code, now);
                db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.StatusChecked, now, user.UserId, status,
                    registered
                        ? reissued
                            ? "Estaba registrado en el SIN y se re-emitió fuera de línea: se anula el duplicado (C-07)."
                            : "Estaba registrado en el SIN: queda válido."
                        : "No llegó al SIN: queda descartado."));
                if (!registered && !reissued && document.Kind == FiscalDocumentKind.Invoice)
                {
                    var replacement = await FiscalIssuer.ReissueInvoiceAsync(Services(document.UserCode), document, FiscalIssuer.BuyerOf(document),
                        document.ExceptionCode == 1, null, ct);
                    messages.Add($"La venta de la factura {document.Number} recibió el documento N° {replacement.Number}.");
                }
                if (document.Status == FiscalDocumentStatus.DuplicateToVoid && status.StatusCode == SiatCodes.VoidConfirmed)
                {
                    document.Void(await VoidReasonAsync(document, ct), SiatCodes.VoidConfirmed, now);
                    db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Voided, now, user.UserId,
                        description: "El duplicado ya figuraba anulado en el SIN."));
                }
                await db.SaveChangesAsync(ct);
            }
            if (document.Status == FiscalDocumentStatus.DuplicateToVoid)
            {
                var reason = await VoidReasonAsync(document, ct);
                var reply = await gateway.VoidDocumentAsync(context.Connection, place, codes, reference, document.Cuf, reason, ct);
                if (reply.Has(SiatCodes.VoidConfirmed) || reply.Has(SiatCodes.AlreadyVoided))
                {
                    document.Void(reason, reply.Has(SiatCodes.VoidConfirmed) ? SiatCodes.VoidConfirmed : SiatCodes.AlreadyVoided, clock.UtcNow);
                    db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Voided, clock.UtcNow, user.UserId, reply,
                        "Duplicado anulado: la venta conserva el documento re-emitido fuera de línea (C-07)."));
                }
                else
                {
                    db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.VoidFailed, clock.UtcNow, user.UserId, reply));
                    messages.Add($"No se pudo anular el duplicado N° {document.Number}: {reply.Describe()}.");
                }
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task<int> VoidReasonAsync(FiscalDocument document, CancellationToken ct) =>
        await FiscalIssuer.VoidReasonFromCatalogAsync(db,
            document.Kind == FiscalDocumentKind.Invoice ? FiscalIssuer.VoidReasonInvoice : FiscalIssuer.VoidReasonNote, ct)
        ?? await db.Set<SiatCatalogItem>().Where(i => i.Catalog == SiatCatalogNames.VoidReasons && i.IsCurrent).OrderBy(i => i.Code)
            .Select(i => (int?)i.Code).FirstOrDefaultAsync(ct)
        ?? 1;

    /// <summary>Paquetes (GZIP de TAR, ≤ 500, mismo sector, tipo y CAFC) de los documentos fuera de línea de los eventos
    /// registrados del punto, con el código de recepción del evento y el CUFD nuevo.</summary>
    private async Task<int> SendPackagesAsync(FiscalContext context, SiatPointOfSale point, SiatPlace place, SiatCuis cuis, SiatCufd cufd,
        List<string> messages, CancellationToken ct)
    {
        var packaged = 0;
        var events = await db.Set<SignificantEvent>()
            .Where(e => e.PointOfSaleId == point.Id && (e.Status == SignificantEventStatus.Registered || e.Status == SignificantEventStatus.PackagesSent))
            .OrderBy(e => e.StartedAt).ToListAsync(ct);
        foreach (var evt in events)
        {
            var documents = await db.Set<FiscalDocument>().Where(d => d.SignificantEventId == evt.Id && d.Status == FiscalDocumentStatus.Offline)
                .OrderBy(d => d.Number).ThenBy(d => d.IssuedAt).ToListAsync(ct);
            foreach (var group in documents.GroupBy(d => (d.DocumentSector, d.DocumentType, d.Cafc)))
            {
                foreach (var chunk in group.Chunk(SiatCodes.MaxDocumentsPerPackage))
                {
                    var ids = chunk.Select(d => d.Id).ToList();
                    var files = await db.Set<FiscalDocumentFile>().Where(f => ids.Contains(f.DocumentId)).ToDictionaryAsync(f => f.DocumentId, f => f.Xml, ct);
                    var entries = chunk.Select((d, i) => ((i + 1).ToString(CultureInfo.InvariantCulture) + ".xml", files[d.Id])).ToList();
                    var tarGz = serializer.Package(entries);
                    var sha = serializer.Sha256Hex(tarGz);
                    var (sector, type, cafc) = group.Key;
                    var reply = await gateway.SendPackageAsync(context.Connection, place, new SiatCodesForCall(cuis.Code, cufd.Code),
                        new SiatDocumentRef(sector, type), tarGz, sha, FiscalIssuer.Milliseconds(NewLookups().FiscalNow(context)), chunk.Length,
                        evt.ReceptionCode!, cafc, ct);
                    if (reply.Transaction && reply.ReceptionCode is { Length: > 0 } receptionCode)
                    {
                        var now = clock.UtcNow;
                        var package = new FiscalPackage(point.TenantId, point.BranchId, evt.Id, point.Id, cufd.Id, sector, type, cafc, sha, now);
                        package.Accepted(receptionCode, reply.StatusCode ?? SiatCodes.ReceptionPending);
                        db.Set<FiscalPackage>().Add(package);
                        for (var i = 0; i < chunk.Length; i++)
                        {
                            chunk[i].AddToPackage(package.Id, i + 1);
                            db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(chunk[i], FiscalDocumentAction.Packaged, now, user.UserId, reply,
                                $"Enviado en el paquete {receptionCode} (archivo {i + 1} de {chunk.Length})."));
                        }
                        evt.MarkPackagesSent();
                        await db.SaveChangesAsync(ct);
                        packaged += chunk.Length;
                    }
                    else
                    {
                        messages.Add($"El SIN rechazó un paquete de {chunk.Length} documento(s) del evento «{evt.Description}»: {reply.Describe()}.");
                    }
                }
            }
        }
        return packaged;
    }

    /// <summary>Puntos EN LÍNEA con eventos pendientes (registro que falló antes, o documentos fuera de línea sin paquete: p. ej.
    /// facturas CAFC transcritas después del fin, o documentos devueltos a la cola por un paquete rechazado).</summary>
    private async Task ProcessOnlineEventsAsync(FiscalContext context, MaintenanceTally tally, CancellationToken ct)
    {
        var offlineDocuments = db.Set<FiscalDocument>().Where(d => d.Status == FiscalDocumentStatus.Offline);
        var pointIds = await (from e in db.Set<SignificantEvent>()
                              join p in db.Set<SiatPointOfSale>() on e.PointOfSaleId equals p.Id
                              where p.Environment == context.Environment && p.ClosedAt == null && p.Mode == SiatConnectionMode.Online
                                    && (e.Status == SignificantEventStatus.Closed
                                        || ((e.Status == SignificantEventStatus.Registered || e.Status == SignificantEventStatus.PackagesSent)
                                            && offlineDocuments.Any(d => d.SignificantEventId == e.Id)))
                              select p.Id).Distinct().ToListAsync(ct);
        await EachAsync(pointIds, async pointId =>
        {
            var lookups = NewLookups();
            var codes = new SiatCodeManager(lookups, gateway);
            var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == pointId, ct);
            var branch = await lookups.BranchMappingAsync(point.BranchId, ct);
            var cuis = await codes.EnsureCuisAsync(context, branch, point, false, ct);
            var cufd = await codes.EnsureCufdAsync(context, branch, point, false, ct);
            await db.SaveChangesAsync(ct);
            var messages = new List<string>();
            var place = BillingLookups.Place(branch, point);
            await RegisterEventsAsync(context, point, place, cuis, cufd, messages, ct);
            tally.DocumentsSent += await SendPackagesAsync(context, point, place, cuis, cufd, messages, ct);
            messages.ForEach(tally.Note);
        }, _ => "Eventos pendientes", tally);
    }

    /// <summary>Valida los paquetes enviados: 908 → todos válidos; 904 → observados los que tienen mensajes con su número de
    /// archivo; 902 → los de contenido erróneo observados y el resto vuelve a la cola (paquete nuevo); 901 → se espera.</summary>
    private async Task ValidatePackagesAsync(FiscalContext context, MaintenanceTally tally, CancellationToken ct)
    {
        var packageIds = await (from k in db.Set<FiscalPackage>()
                                join p in db.Set<SiatPointOfSale>() on k.PointOfSaleId equals p.Id
                                where k.Status == FiscalPackageStatus.Sent && p.Environment == context.Environment
                                orderby k.SentAt
                                select k.Id).ToListAsync(ct);
        await EachAsync(packageIds, async packageId =>
        {
            var lookups = NewLookups();
            var codes = new SiatCodeManager(lookups, gateway);
            var package = await db.Set<FiscalPackage>().FirstAsync(k => k.Id == packageId, ct);
            var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == package.PointOfSaleId, ct);
            if (!point.IsOnline && point.Mode != SiatConnectionMode.Recovering)
            {
                return;   // sin comunicación: se valida cuando vuelva
            }
            var branch = await lookups.BranchMappingAsync(point.BranchId, ct);
            var cuis = await codes.EnsureCuisAsync(context, branch, point, false, ct);
            var cufd = await codes.EnsureCufdAsync(context, branch, point, false, ct);
            await db.SaveChangesAsync(ct);
            var reply = await gateway.ValidatePackageAsync(context.Connection, BillingLookups.Place(branch, point), new SiatCodesForCall(cuis.Code, cufd.Code),
                new SiatDocumentRef(package.DocumentSector, package.DocumentType), package.ReceptionCode!, ct);
            var now = clock.UtcNow;
            var messages = reply.Messages.Count > 0 ? JsonSerializer.Serialize(reply.Messages, BillingLookups.JsonOptions) : null;
            var documents = await db.Set<FiscalDocument>().Where(d => d.PackageId == package.Id && d.Status == FiscalDocumentStatus.InPackage)
                .ToListAsync(ct);
            switch (reply.StatusCode)
            {
                case SiatCodes.ReceptionValidated:
                    package.Validated(SiatCodes.ReceptionValidated, now, messages);
                    foreach (var document in documents)
                    {
                        document.ConfirmInPackage(SiatCodes.ReceptionValidated, now);
                        db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.PackageValidated, now, user.UserId, reply));
                    }
                    break;
                case SiatCodes.ReceptionObserved:
                case SiatCodes.ReceptionRejected:
                {
                    var observed = reply.StatusCode == SiatCodes.ReceptionObserved;
                    if (observed)
                    {
                        package.Observed(SiatCodes.ReceptionObserved, now, messages);
                    }
                    else
                    {
                        package.Rejected(SiatCodes.ReceptionRejected, now, messages);
                    }
                    foreach (var document in documents)
                    {
                        var own = reply.Messages.Where(m => m.FileNumber == document.PackagePosition && !m.IsWarning).ToList();
                        if (own.Count > 0)
                        {
                            document.RejectInPackage(own[0].Code);
                            db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.PackageRejected, now, user.UserId,
                                reply with { Messages = own }));
                        }
                        else if (observed)
                        {
                            document.ConfirmInPackage(SiatCodes.ReceptionValidated, now);
                            db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.PackageValidated, now, user.UserId,
                                description: "Validado dentro del paquete observado (sin errores propios)."));
                        }
                        else
                        {
                            document.ReturnToOfflineQueue();
                            db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.PackageRejected, now, user.UserId, reply,
                                "El paquete se rechazó en la cabecera: el documento se enviará en un paquete nuevo."));
                        }
                    }
                    tally.Note($"El paquete {package.ReceptionCode} tuvo observaciones: {reply.Describe()}.");
                    break;
                }
                case SiatCodes.ReceptionPending:
                    return;   // el SIN todavía lo procesa
                default:
                    tally.Note($"No se pudo validar el paquete {package.ReceptionCode}: {reply.Describe()}.");
                    return;
            }
            await db.SaveChangesAsync(ct);
            tally.PackagesValidated++;
        }, _ => "Validación de paquetes", tally);
    }

    /// <summary>Documentos sin respuesta y duplicados por anular de los puntos EN LÍNEA (verificación por CUF).</summary>
    private async Task ResolvePendingAsync(FiscalContext context, MaintenanceTally tally, CancellationToken ct)
    {
        var pointIds = await (from d in db.Set<FiscalDocument>()
                              join p in db.Set<SiatPointOfSale>() on d.PointOfSaleId equals p.Id
                              where p.Environment == context.Environment && p.Mode == SiatConnectionMode.Online && p.ClosedAt == null
                                    && (d.Status == FiscalDocumentStatus.NoResponse || d.Status == FiscalDocumentStatus.DuplicateToVoid)
                              select p.Id).Distinct().ToListAsync(ct);
        await EachAsync(pointIds, async pointId =>
        {
            var lookups = NewLookups();
            var codes = new SiatCodeManager(lookups, gateway);
            var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.Id == pointId, ct);
            var branch = await lookups.BranchMappingAsync(point.BranchId, ct);
            var cuis = await codes.EnsureCuisAsync(context, branch, point, false, ct);
            var cufd = await codes.EnsureCufdAsync(context, branch, point, false, ct);
            await db.SaveChangesAsync(ct);
            var messages = new List<string>();
            await ResolveDocumentsAsync(context, pointId, cuis, cufd, messages, ct);
            messages.ForEach(tally.Note);
        }, _ => "Documentos sin respuesta", tally);
    }

    /// <summary>Eventos con todos sus documentos resueltos: conciliados (o con observaciones). Los de contingencia manual
    /// esperan el fin del plazo de transcripción (72 h).</summary>
    private async Task ReconcileEventsAsync(FiscalContext context, MaintenanceTally tally, CancellationToken ct)
    {
        var fiscalNow = NewLookups().FiscalNow(context);
        var ids = await db.Set<SignificantEvent>()
            .Where(e => e.Environment == context.Environment
                        && (e.Status == SignificantEventStatus.Registered || e.Status == SignificantEventStatus.PackagesSent))
            .Select(e => e.Id).ToListAsync(ct);
        await EachAsync(ids, async id =>
        {
            var evt = await db.Set<SignificantEvent>().FirstAsync(e => e.Id == id, ct);
            if (evt.Kind == SignificantEventKind.ManualCafc && evt.TranscriptionDeadline is { } deadline && fiscalNow <= deadline)
            {
                return;
            }
            var pending = await db.Set<FiscalDocument>().AnyAsync(d => d.SignificantEventId == id
                                                                       && (d.Status == FiscalDocumentStatus.Offline
                                                                           || d.Status == FiscalDocumentStatus.InPackage), ct)
                          || await db.Set<FiscalPackage>().AnyAsync(k => k.SignificantEventId == id && k.Status == FiscalPackageStatus.Sent, ct);
            if (pending)
            {
                return;
            }
            var observed = await db.Set<FiscalPackage>().AnyAsync(k => k.SignificantEventId == id && k.Status != FiscalPackageStatus.Validated, ct)
                           || await db.Set<FiscalDocument>().AnyAsync(d => d.SignificantEventId == id && d.Status == FiscalDocumentStatus.PackageRejected,
                               ct);
            evt.Reconcile(observed);
            await db.SaveChangesAsync(ct);
        }, _ => "Conciliación de eventos", tally);
    }

    /// <summary>Notas crédito-débito pendientes: devoluciones de ventas con factura VÁLIDA que todavía no tienen nota (se
    /// registraron fuera de línea o antes de que la factura se validara).</summary>
    private async Task IssuePendingCreditNotesAsync(FiscalContext context, MaintenanceTally tally, CancellationToken ct)
    {
        var notes = db.Set<FiscalDocument>().Where(n => n.Kind == FiscalDocumentKind.CreditDebitNote && n.Status != FiscalDocumentStatus.Discarded);
        var returnIds = await (from r in db.Set<SalesReturn>()
                               join d in db.Set<FiscalDocument>() on r.InvoiceId equals d.InvoiceId
                               where d.Kind == FiscalDocumentKind.Invoice && d.Status == FiscalDocumentStatus.Valid && d.Environment == context.Environment
                                     && !notes.Any(n => n.SalesReturnId == r.Id)
                               orderby r.ReturnedAt
                               select r.Id).Distinct().Take(50).ToListAsync(ct);
        var issued = 0;
        await EachAsync(returnIds, async returnId =>
        {
            var salesReturn = await db.Set<SalesReturn>().Include(r => r.Lines).FirstAsync(r => r.Id == returnId, ct);
            var invoiceDocument = await db.Set<FiscalDocument>().Include(d => d.Lines)
                .Where(d => d.InvoiceId == salesReturn.InvoiceId && d.Kind == FiscalDocumentKind.Invoice && d.Status == FiscalDocumentStatus.Valid)
                .OrderByDescending(d => d.CreatedAt).FirstAsync(ct);
            var orderLines = await (from i in db.Set<Invoice>()
                                    join l in db.Set<SalesOrderLine>() on i.SalesOrderId equals l.SalesOrderId
                                    where i.Id == salesReturn.InvoiceId
                                    select l).ToListAsync(ct);
            var issue = await FiscalIssuer.IssueCreditNoteAsync(Services(), salesReturn, invoiceDocument, orderLines, ct);
            if (issue.Note is not null)
            {
                await db.SaveChangesAsync(ct);
                issued++;
            }
        }, _ => "Notas crédito-débito pendientes", tally);
        if (issued > 0)
        {
            tally.Note($"{issued} nota(s) crédito-débito pendiente(s) emitida(s): se envían con el próximo despacho.");
        }
    }

    /// <summary>Entrega por correo (XML + PDF) de los documentos válidos con correo del comprador y sin entrega registrada.</summary>
    private async Task SendPendingEmailsAsync(FiscalContext context, MaintenanceTally tally, CancellationToken ct)
    {
        if (mailSender is null || renderer is null)
        {
            return;
        }
        MailServer? server;
        try
        {
            server = await BuyerMail.ServerAsync(db, protector, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            tally.Note("Correo: " + ex.Message);
            return;
        }
        if (server is null)
        {
            return;
        }
        var since = clock.UtcNow.AddDays(-30);
        var deliveries = db.Set<FiscalDelivery>().Where(x => x.Channel == FiscalDeliveryChannel.Email);
        var ids = await db.Set<FiscalDocument>()
            .Where(d => d.Status == FiscalDocumentStatus.Valid && d.BuyerEmail != null && d.CreatedAt >= since && d.Environment == context.Environment
                        && !deliveries.Any(x => x.DocumentId == d.Id && x.Succeeded)
                        && deliveries.Count(x => x.DocumentId == d.Id) < MaxEmailAttempts)
            .OrderBy(d => d.CreatedAt).Select(d => d.Id).Take(20).ToListAsync(ct);
        var sent = 0;
        await EachAsync(ids, async id =>
        {
            var document = await LoadDocumentAsync(id, ct);
            if (document?.BuyerEmail is not { } email)
            {
                return;
            }
            var (ok, error) = await BuyerMail.SendDocumentAsync(db, mailSender, server, renderer, document, email, ct);
            var now = clock.UtcNow;
            db.Set<FiscalDelivery>().Add(new FiscalDelivery(document.TenantId, document.BranchId, document.Id, FiscalDeliveryChannel.Email, email, ok,
                error, now, user.UserId));
            if (ok)
            {
                db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Delivered, now, user.UserId,
                    description: $"XML y representación gráfica enviados a {email}."));
                sent++;
            }
            await db.SaveChangesAsync(ct);
        }, _ => "Correo al comprador", tally);
        if (sent > 0)
        {
            tally.Note($"{sent} documento(s) enviados por correo al comprador.");
        }
    }

    // ================================================================================================ auxiliares
    private Task<FiscalDocument?> LoadDocumentAsync(Guid id, CancellationToken ct) =>
        db.Set<FiscalDocument>().Include(d => d.Lines).Include(d => d.NoteReference).FirstOrDefaultAsync(d => d.Id == id, ct);

    /// <summary>Último documento de la cadena de reemplazos (el que recibe el comprador).</summary>
    private async Task<Guid> FinalReplacementAsync(Guid documentId, CancellationToken ct)
    {
        var current = documentId;
        for (var depth = 0; depth < 10; depth++)
        {
            var next = await db.Set<FiscalDocument>().Where(d => d.ReplacesDocumentId == current).OrderByDescending(d => d.CreatedAt)
                .Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
            if (next is null)
            {
                break;
            }
            current = next.Value;
        }
        return current;
    }

    /// <summary>Aplica el resultado y guarda con reintento optimista (máximo 3), en UNA transacción explícita: ante un
    /// conflicto se deshace, se relee y se vuelve a aplicar (lo que respondió el SIN no se pierde).</summary>
    private async Task<DispatchOutcome> PersistAsync(Func<Task<DispatchOutcome>> apply, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await db.BeginTransactionAsync(ct);
                var outcome = await apply();
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return outcome;
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    /// <summary>
    /// Guarda el cambio de estado del documento ANTES de insertar su reemplazo (dentro de la transacción de
    /// <see cref="PersistAsync"/>): el índice único parcial «un documento vigente por venta» de PostgreSQL se comprueba por
    /// sentencia, así que el original debe dejar de estar pendiente antes de que entre el nuevo.
    /// </summary>
    private Task FlushAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    internal static string Title(FiscalDocument document) => document.Kind == FiscalDocumentKind.Invoice ? "Factura" : "Nota crédito-débito";
}
