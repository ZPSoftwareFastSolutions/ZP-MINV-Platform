using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Storefront;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Integration;

/// <summary>V7 · Por qué no se encoló el correo de una reserva (<see cref="None"/> = se encoló).</summary>
public enum ReservationMailSkip
{
    None,

    /// <summary>La reserva no tiene correo de contacto (y no se indicó otro).</summary>
    NoRecipient,

    /// <summary>El correo no es una dirección a la que se pueda enviar.</summary>
    InvalidRecipient,

    /// <summary>La reserva ya no está reservada (vendida, liberada o vencida).</summary>
    NotReserved,

    /// <summary>Esa dirección ya recibió <see cref="ReservationMail.MaxPerRecipient"/> correos en las últimas 24 horas.</summary>
    RecipientLimit,

    /// <summary>La empresa ya pidió <see cref="ReservationMail.MaxPerCompany"/> correos en las últimas 24 horas.</summary>
    CompanyLimit,
}

/// <summary>V7 · Resultado de encolar: el correo encolado o por qué no se encoló.</summary>
public sealed record ReservationMailResult(OutgoingMail? Mail, ReservationMailSkip Skipped)
{
    public bool Queued => Mail is not null;
}

/// <summary>V7 · Lo que el despachador necesita para enviar un correo: el contenido ya armado o, si ya no corresponde
/// enviarlo, el motivo con el que se cancela su cola (<see cref="OutgoingMailDispatch.Cancel"/>).</summary>
public sealed record ReservationMailDraft(ReservationMailContent? Content, string? CancelReason)
{
    public bool ShouldSend => Content is not null;
}

/// <summary>
/// V7 · Correo de confirmación de una reserva (regla P-06). Punto ÚNICO de encolado: lo llaman todos los caminos que reservan
/// (la tienda, la cuenta de cliente, el carrito de mostrador y la reserva de una cotización del escritorio) ANTES de su
/// <c>SaveChanges</c>, de modo que el correo se guarda en la MISMA transacción que la reserva y nunca dentro de ella se envía
/// nada (regla B-08): lo envía después el despachador de la cola. Un correo por reserva; la repetición idempotente no llega
/// aquí. Topes contra el abuso del envío: <see cref="MaxPerRecipient"/> por destinatario y <see cref="MaxPerCompany"/> por
/// empresa cada 24 horas, contando <c>integration.outgoing_mails</c>.
/// </summary>
public static class ReservationMail
{
    /// <summary>Correos por destinatario en 24 horas.</summary>
    public const int MaxPerRecipient = 3;

    /// <summary>Correos por empresa en 24 horas (por debajo del límite diario de envío de una cuenta de Gmail).</summary>
    public const int MaxPerCompany = 300;

    /// <summary>Ventana de los topes.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public const string SoldReason = "La reserva ya se vendió: no se envía la confirmación.";
    public const string ClosedReason = "La reserva ya no está reservada (liberada o vencida): no se envía la confirmación.";
    public const string ExpiredReason = "La reserva ya venció: no se envía la confirmación.";
    public const string MissingReason = "La reserva ya no existe: no se envía la confirmación.";
    public const string ReplacedReason = "Reemplazado por un reenvío de la confirmación.";

    /// <summary>
    /// Encola la confirmación de <paramref name="build"/> (ya reservada) para <paramref name="recipient"/> o, sin él, para su
    /// correo de contacto. NO guarda: agrega el correo y su cola al contexto para que entren en el <c>SaveChanges</c> de quien
    /// llama. Sin correo, con uno que no sirve, fuera de los topes o si la reserva ya no está vigente, NO encola y dice por
    /// qué: la reserva sigue adelante igual (el correo es un aviso, no una condición).
    /// </summary>
    public static async Task<ReservationMailResult> EnqueueAsync(IMinvDbContext db, PcBuild build, Guid userId, DateTimeOffset now,
        CancellationToken ct, string? recipient = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        var address = string.IsNullOrWhiteSpace(recipient) ? build.ContactEmail : recipient;
        if (string.IsNullOrWhiteSpace(address))
        {
            return new ReservationMailResult(null, ReservationMailSkip.NoRecipient);
        }
        if (!OutgoingMail.IsValidRecipient(address))
        {
            return new ReservationMailResult(null, ReservationMailSkip.InvalidRecipient);
        }
        if (build.Status != PcBuildStatus.Reserved || build.IsReservationExpired(now))
        {
            return new ReservationMailResult(null, ReservationMailSkip.NotReserved);
        }
        var normalized = OutgoingMail.NormalizeRecipient(address);
        var since = (now - Window).ToUniversalTime();
        var recent = db.Set<OutgoingMail>().Where(m => m.RequestedAt > since);
        if (await recent.CountAsync(m => m.Recipient == normalized, ct) >= MaxPerRecipient)
        {
            return new ReservationMailResult(null, ReservationMailSkip.RecipientLimit);
        }
        if (await recent.CountAsync(ct) >= MaxPerCompany)
        {
            return new ReservationMailResult(null, ReservationMailSkip.CompanyLimit);
        }
        var mail = new OutgoingMail(build.TenantId, build.BranchId, OutgoingMailKind.ReservationConfirmed, build.Id, normalized, userId, now);
        db.Set<OutgoingMail>().Add(mail);
        db.Set<OutgoingMailDispatch>().Add(new OutgoingMailDispatch(build.TenantId, mail.Id, now));
        return new ReservationMailResult(mail, ReservationMailSkip.None);
    }

    /// <summary>
    /// Para el despachador: arma el correo de <paramref name="mail"/> desde su reserva (productos, precios congelados, total,
    /// vencimiento, sucursal y empresa) con <see cref="ReservationMailTemplate"/>, o dice por qué ya no corresponde enviarlo
    /// (la reserva se vendió, se liberó o venció). <paramref name="publicUrl"/> es la dirección pública de la tienda (la fija
    /// el servidor, <c>Minv:Mail:PublicUrl</c>); sin ella el correo sale sin el enlace «Ver mi reserva».
    /// </summary>
    public static async Task<ReservationMailDraft> DraftAsync(IMinvDbContext db, OutgoingMail mail, DateTimeOffset now, string? publicUrl,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mail);
        var build = await db.Set<PcBuild>().AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == mail.PcBuildId, ct);
        if (build is null)
        {
            return new ReservationMailDraft(null, MissingReason);
        }
        if (build.Status != PcBuildStatus.Reserved)
        {
            return new ReservationMailDraft(null, build.Status == PcBuildStatus.Sold ? SoldReason : ClosedReason);
        }
        if (build.IsReservationExpired(now))
        {
            return new ReservationMailDraft(null, ExpiredReason);
        }
        return new ReservationMailDraft(ReservationMailTemplate.Render(await ModelAsync(db, build, publicUrl, ct)), null);
    }

    /// <summary>Modelo del correo de una reserva: solo lo que la plantilla puede decir (nunca notas, texto libre del cliente,
    /// correo ni teléfono completo).</summary>
    public static async Task<ReservationMailModel> ModelAsync(IMinvDbContext db, PcBuild build, string? publicUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(build);
        var names = await PcBuildStock.NamesAsync(db, build.Lines.Select(l => l.VariantId).Distinct().ToList(), ct);
        var company = await db.Set<Tenant>().AsNoTracking().Where(t => t.Id == build.TenantId).Select(t => t.LegalName).FirstOrDefaultAsync(ct);
        var branch = await db.Set<Branch>().AsNoTracking().Where(b => b.Id == build.BranchId).Select(b => new { b.Name, b.AddressId }).FirstOrDefaultAsync(ct);
        var street = branch?.AddressId is { } addressId
            ? await db.Set<Address>().AsNoTracking().Where(a => a.Id == addressId).Select(a => a.Street).FirstOrDefaultAsync(ct)
            : null;
        var lines = build.Lines.OrderBy(l => l.Slot).ThenBy(l => l.Id).Select(l =>
        {
            var (sku, name) = names.GetValueOrDefault(l.VariantId, ("?", "?"));
            return new ReservationMailLine(sku, name, l.Quantity, l.QuotedUnitPrice, l.Subtotal);
        }).ToList();
        return new ReservationMailModel(build.Number, build.Kind, company ?? string.Empty, build.ContactName, lines, build.Total,
            build.ReservedUntil ?? build.CreatedAt, branch?.Name ?? string.Empty, street, publicUrl, build.ContactPhone);
    }

    /// <summary>Dirección enmascarada para quien no gestiona las reservas y para la auditoría: la primera letra y el dominio
    /// («a***@correo.example»).</summary>
    public static string MaskRecipient(string? recipient)
    {
        var text = recipient?.Trim() ?? string.Empty;
        var at = text.LastIndexOf('@');
        return at <= 0 ? "***" : $"{text[0]}***{text[at..]}";
    }
}
