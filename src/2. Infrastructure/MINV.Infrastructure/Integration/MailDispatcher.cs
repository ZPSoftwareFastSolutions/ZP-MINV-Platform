using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Integration;
using MINV.Domain.Common;
using MINV.Domain.Integration;
using MINV.Infrastructure.Persistence;

namespace MINV.Infrastructure.Integration;

/// <summary>Resultado de una pasada del despachador de correos: cuántos tomó y qué pasó con ellos. <see cref="Halted"/> = la
/// pasada se cortó porque el servidor de correo falló (conexión, credenciales o configuración): el que la hospeda espera antes
/// de la siguiente.</summary>
public sealed record MailDispatchSummary(int Mails, int Sent, int Failed, int Postponed, int Cancelled, bool Halted);

/// <summary>
/// V7 · Despachador de la cola de correos (regla P-06; lo hospeda el API Gateway con <c>MailDispatcherService</c>). El correo se
/// encoló en la MISMA transacción que la reserva y aquí sale DESPUÉS del COMMIT (regla B-08). Cada pasada toma los pendientes
/// vencidos (en PostgreSQL con <c>integration.claim_outgoing_mails</c>: FOR UPDATE SKIP LOCKED y un arrendamiento de 120 s; en
/// memoria, con <see cref="OutgoingMailDispatch.Claim"/>) y, por cada uno, en un scope propio con SU empresa. B6: el arrendamiento
/// del reclamo es la marca de dueño: antes de nada lo RENUEVA (<see cref="OutgoingMailDispatch.Renew"/>, con la concurrencia
/// optimista de la cola) solo si sigue siendo el suyo. Si un lote tardó más que el arrendamiento y otra réplica ya lo reclamó, o
/// el correo ya se cerró, no lo toca: dos réplicas nunca envían el mismo correo, y mientras lo envía un reenvío no lo cancela.
/// <list type="number">
/// <item>si la reserva ya no está reservada (vendida, liberada, vencida), cancela la cola sin enviar;</item>
/// <item>resuelve el servidor SMTP: el de la empresa (<c>billing.mail_settings</c> activo) o el del servidor (<see cref="MailOptions"/>);
/// sin ninguno, lo pospone SIN gastar un intento;</item>
/// <item>arma el mensaje con la plantilla (<see cref="ReservationMail.DraftAsync"/>) y lo envía con un tiempo máximo propio;</item>
/// <item>registra el intento en <c>outgoing_mail_attempts</c> (append-only) y el resultado en la cola: enviado, reprogramado con la
/// espera de <see cref="OutgoingMailAttempt.BackoffBefore"/> o agotado (rechazo definitivo del destinatario o quinto intento).</item>
/// </list>
/// Una falla DEL SERVIDOR de correo (no se pudo conectar, no respondió, rechazó las credenciales) no es culpa del mensaje: ese
/// correo se pospone sin gastar un intento y la pasada se corta, para no quemar los intentos de toda la cola. Lo que se guarda
/// como error nunca lleva credenciales ni datos del servidor.
/// </summary>
public sealed class MailDispatcher(IServiceScopeFactory scopes, MailOptions options, IClock clock)
{
    /// <summary>Arrendamiento del reclamo: un correo tomado no vuelve a la cola hasta pasados estos segundos.</summary>
    public const int LeaseSeconds = 120;

    /// <summary>Tiempo máximo de UN envío (por encima del de <c>SmtpMailSender</c>: frena también a un emisor que no respeta la
    /// cancelación).</summary>
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Espera de un correo sin servidor de correo configurado.</summary>
    public static readonly TimeSpan NoServerDelay = TimeSpan.FromMinutes(15);

    /// <summary>Espera de un correo cuando el servidor de correo falló (conexión, credenciales, configuración).</summary>
    public static readonly TimeSpan ServerFailureDelay = TimeSpan.FromMinutes(5);

    public const string NoServerReason =
        "No hay servidor de correo configurado (ni el de la empresa ni el del servidor): el correo se reintentará más tarde.";

    public const string UnreadableServerReason =
        "No se pudo leer la contraseña del correo de la empresa (falta la clave maestra o cambió): el correo se reintentará más tarde.";

    /// <summary>Un correo reclamado: su empresa y el arrendamiento con que lo tomó ESTA pasada (la marca de dueño).</summary>
    private sealed record Claimed(Guid TenantId, Guid OutgoingMailId, DateTimeOffset LeasedUntil);

    private enum Outcome
    {
        Skipped,
        Sent,
        Failed,
        Postponed,
        Cancelled,
        Halted,
    }

    /// <summary>Una pasada: toma hasta <paramref name="batch"/> correos vencidos y los procesa de a uno.</summary>
    public async Task<MailDispatchSummary> RunOnceAsync(int batch, CancellationToken ct)
    {
        IReadOnlyList<Claimed> claimed;
        using (var scope = scopes.CreateScope())
        {
            claimed = await ClaimAsync(scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>(), Math.Clamp(batch, 1, 500), ct);
        }
        int sent = 0, failed = 0, postponed = 0, cancelled = 0;
        var halted = false;
        for (var i = 0; i < claimed.Count; i++)
        {
            var item = claimed[i];
            using var scope = TenantScope(item.TenantId);
            switch (await DispatchAsync(scope.ServiceProvider, item, ct))
            {
                case Outcome.Sent:
                    sent++;
                    break;
                case Outcome.Failed:
                    failed++;
                    break;
                case Outcome.Postponed:
                    postponed++;
                    break;
                case Outcome.Cancelled:
                    cancelled++;
                    break;
                case Outcome.Halted:
                    postponed++;
                    halted = true;
                    break;
            }
            if (halted)
            {
                // B6 · Los que quedaron sin procesar vuelven a la cola para la pasada siguiente (sin gastar intentos), en vez de
                // esperar a que venza su arrendamiento
                await ReleaseAsync(claimed.Skip(i + 1), ct);
                break;
            }
        }
        return new MailDispatchSummary(claimed.Count, sent, failed, postponed, cancelled, halted);
    }

    /// <summary>Scope propio con la empresa del correo (proceso de plataforma: todas las sucursales de ESA empresa).</summary>
    private IServiceScope TenantScope(Guid tenantId)
    {
        var scope = scopes.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(tenantId);
        tenant.SetBranches(BranchScope.Unrestricted);
        return scope;
    }

    /// <summary>B6 · Devuelve a la cola los correos que esta pasada tomó y no llegó a intentar (solo los que siguen siendo suyos).</summary>
    private async Task ReleaseAsync(IEnumerable<Claimed> items, CancellationToken ct)
    {
        foreach (var item in items)
        {
            using var scope = TenantScope(item.TenantId);
            var db = scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
            try
            {
                var dispatch = await db.OutgoingMailDispatches.FirstOrDefaultAsync(d => d.OutgoingMailId == item.OutgoingMailId, ct);
                if (dispatch is not null && dispatch.Release(item.LeasedUntil, clock.UtcNow))
                {
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (ConcurrencyConflictException)
            {
                // Otra sesión lo cambió entre medio: ya no es de esta pasada
            }
        }
    }

    private async Task<IReadOnlyList<Claimed>> ClaimAsync(MinvWriteDbContext db, int batch, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            return await db.Database.SqlQuery<Claimed>(
                    $"SELECT tenant_id AS \"TenantId\", outgoing_mail_id AS \"OutgoingMailId\", leased_until AS \"LeasedUntil\" FROM integration.claim_outgoing_mails({batch}, {LeaseSeconds})")
                .ToListAsync(ct);
        }
        // En memoria no hay RLS ni función: el proceso de plataforma toma la cola de todas las empresas (regla A-04) con el mismo
        // arrendamiento que pondría la función
        var now = clock.UtcNow;
        var due = await db.OutgoingMailDispatches.IgnoreQueryFilters().Where(d => d.Status == OutgoingMailStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).Take(batch).ToListAsync(ct);
        foreach (var dispatch in due)
        {
            dispatch.Claim(now, now.AddSeconds(LeaseSeconds));
        }
        await db.SaveChangesAsync(ct);
        return due.Select(d => new Claimed(d.TenantId, d.OutgoingMailId, d.LeasedUntil!.Value)).ToList();
    }

    private async Task<Outcome> DispatchAsync(IServiceProvider sp, Claimed claimed, CancellationToken ct)
    {
        var db = sp.GetRequiredService<MinvWriteDbContext>();
        var mailId = claimed.OutgoingMailId;
        var mail = await db.OutgoingMails.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mailId, ct);
        var dispatch = await db.OutgoingMailDispatches.FirstOrDefaultAsync(d => d.OutgoingMailId == mailId, ct);
        // Ya resuelto por otro camino (un reenvío lo reemplazó, otra réplica lo envió) o ya no es de esta pasada: nada que hacer. El
        // reclamo ya adelantó su próximo intento (arrendamiento): NO se vuelve a mirar si «le toca».
        if (mail is null || dispatch is not { IsPending: true } || !await RenewAsync(db, dispatch, claimed.LeasedUntil, ct))
        {
            return Outcome.Skipped;
        }

        // 1. ¿Sigue reservada? El contenido sale de la reserva (precios congelados): nada se guardó del asunto ni del cuerpo
        var now = clock.UtcNow;
        var draft = await ReservationMail.DraftAsync(db, mail, now, options.PublicUrl, ct);
        if (!draft.ShouldSend)
        {
            await CommitAsync(db, mailId, d => d.Cancel(draft.CancelReason!, now), null, ct);
            return Outcome.Cancelled;
        }

        // 2. Servidor de correo: el de la empresa o el del servidor; sin ninguno (o sin emisor), se pospone sin gastar un intento
        MailServer? server;
        try
        {
            server = await MailServers.CompanyAsync(db, sp.GetService<ISecretProtector>(), ct) ?? options.Server();
        }
        catch (Exception ex) when (ex is DomainException or AccessDeniedException or CryptographicException or FormatException)
        {
            await CommitAsync(db, mailId, d => d.Postpone(UnreadableServerReason, now, ServerFailureDelay), null, ct);
            return Outcome.Postponed;
        }
        var sender = sp.GetService<IMailSender>();
        if (server is null || sender is null)
        {
            await CommitAsync(db, mailId, d => d.Postpone(NoServerReason, now, NoServerDelay), null, ct);
            return Outcome.Postponed;
        }

        // 3. Envío con tiempo máximo propio; el Message-ID es el mismo en cada reintento
        var content = draft.Content!;
        var message = new MailMessageSpec(server, mail.Recipient, content.Subject, content.HtmlBody, [], content.TextBody,
            mail.MessageId(server.FromAddress));
        var (failure, error, ms) = await SendAsync(sender, message, ct);
        now = clock.UtcNow;
        var safeError = error is null ? null : Scrub(server, error);

        // 4a. Falla del servidor de correo: no es del mensaje. Se pospone sin gastar un intento y se corta la pasada
        if (failure == MailFailureKind.Server)
        {
            await CommitAsync(db, mailId, d => d.Postpone(safeError!, now, ServerFailureDelay), null, ct);
            return Outcome.Halted;
        }

        // 4b. Se intentó entregar: el intento queda SIEMPRE en la bitácora, junto con el resultado en la cola
        await CommitAsync(db, mailId, d =>
        {
            if (failure is null)
            {
                d.MarkSent(now);
            }
            else
            {
                d.RecordFailure(safeError, now, permanent: failure == MailFailureKind.Permanent);
            }
        }, d => new OutgoingMailAttempt(mail.TenantId, mail.Id, d.NextAttempt, failure is null, safeError, now, ms), ct);
        return failure is null ? Outcome.Sent : Outcome.Failed;
    }

    /// <summary>Envía con el tiempo máximo del despachador y clasifica la falla (null = enviado).</summary>
    private static async Task<(MailFailureKind? Failure, string? Error, int Ms)> SendAsync(IMailSender sender, MailMessageSpec message, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(SendTimeout);
        try
        {
            await sender.SendAsync(message, limit.Token).WaitAsync(limit.Token);
            return (null, null, Elapsed(watch));
        }
        catch (MailDeliveryException ex)
        {
            return (ex.Kind, ex.Message, Elapsed(watch));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (MailFailureKind.Server, $"El servidor de correo no respondió en {SendTimeout.TotalSeconds:0} s.", Elapsed(watch));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un emisor que no clasifica su falla: se trata como pasajera y NO se guarda su mensaje (podría traer datos del servidor)
            return (MailFailureKind.Transient, $"El envío falló por un error inesperado ({ex.GetType().Name}).", Elapsed(watch));
        }
    }

    /// <summary>
    /// B6 · Renueva el arrendamiento SOLO si sigue siendo el de este reclamo (<paramref name="held"/>) y lo guarda con la
    /// concurrencia optimista de la cola: el correo queda de esta pasada durante <see cref="LeaseSeconds"/> (más que
    /// <see cref="SendTimeout"/>, lo que dura como máximo UN envío). Falso si otra réplica lo reclamó al vencer el arrendamiento
    /// (su marca es otra) o si otra sesión lo cambió entre medio: entonces esta pasada no lo envía.
    /// </summary>
    private async Task<bool> RenewAsync(MinvWriteDbContext db, OutgoingMailDispatch dispatch, DateTimeOffset held, CancellationToken ct)
    {
        try
        {
            dispatch.Renew(held, clock.UtcNow.AddSeconds(LeaseSeconds));
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is ConcurrencyConflictException || ex is DomainException { Code: "mail.lease" })
        {
            db.ClearTracking();
            return false;
        }
    }

    /// <summary>
    /// Aplica el resultado a la cola y agrega el intento (si lo hubo) en UN guardado. Si otra sesión cambió la cola entre medio,
    /// relee y reintenta (máximo 3). B6: el arrendamiento renovado impide que un reenvío la cancele o que otra réplica la cierre
    /// mientras se envía; si aun así la cola ya estaba cerrada (una réplica detenida más que su arrendamiento), el intento, que
    /// OCURRIÓ, queda en la bitácora Y en el contador (<see cref="OutgoingMailDispatch.CountLateAttempt"/>): <c>attempts</c>
    /// siempre es igual a las filas de <c>outgoing_mail_attempts</c> (comprobación D08).
    /// </summary>
    private static async Task CommitAsync(MinvWriteDbContext db, Guid mailId, Action<OutgoingMailDispatch> apply,
        Func<OutgoingMailDispatch, OutgoingMailAttempt>? attempt, CancellationToken ct)
    {
        for (var round = 1; ; round++)
        {
            var dispatch = await db.OutgoingMailDispatches.FirstAsync(d => d.OutgoingMailId == mailId, ct);
            if (dispatch.IsPending)
            {
                if (attempt is not null)
                {
                    db.OutgoingMailAttempts.Add(attempt(dispatch));
                }
                apply(dispatch);
            }
            else if (attempt is not null && dispatch.Attempts < OutgoingMailAttempt.MaxAttempts)
            {
                db.OutgoingMailAttempts.Add(attempt(dispatch));
                dispatch.CountLateAttempt();
            }
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (ConcurrencyConflictException) when (round < 3)
            {
                db.ClearTracking();
            }
        }
    }

    private static int Elapsed(Stopwatch watch) => (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds);

    /// <summary>El error que se guarda: sin la contraseña ni el usuario (<see cref="MailServer.Redact"/>), sin el nombre del
    /// servidor ni la dirección del remitente (por si el emisor los repitió), en una línea y de 500 caracteres como máximo.</summary>
    internal static string Scrub(MailServer server, string error)
    {
        var text = server.Redact(error);
        foreach (var value in new[] { server.Host, server.FromAddress })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                text = text.Replace(value, "***", StringComparison.OrdinalIgnoreCase);
            }
        }
        return OutgoingMailDispatch.Clean(text) ?? "El envío falló sin detalle.";
    }
}
