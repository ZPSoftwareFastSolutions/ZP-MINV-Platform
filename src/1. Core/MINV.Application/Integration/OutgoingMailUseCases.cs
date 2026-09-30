using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Integration;

// =====================================================================================================================
// V7 · Cola de correos para el personal (regla P-06): ver qué se pidió enviar, en qué quedó cada correo y reenviar la
// confirmación de una reserva. Ninguno de estos casos de uso envía nada: el envío lo hace el despachador después del COMMIT.
// =====================================================================================================================

/// <summary>
/// V7 · Fila de la cola de correos: reserva, tipo, destinatario, estado, intentos, último error y fechas. El destinatario sale
/// completo solo para quien gestiona las reservas (<c>sales.pcbuild.manage</c>, regla S-06); a los demás, enmascarado. En la
/// auditoría va siempre enmascarado. <see cref="NextAttemptAt"/> solo mientras está pendiente.
/// </summary>
public sealed record OutgoingMailRow(Guid Id, string Reservation, PcBuildKind ReservationKind, string BranchCode, OutgoingMailKind Kind, string KindText,
    string Recipient, OutgoingMailStatus Status, string StatusText, int Attempts, int MaxAttempts, string? LastError, DateTimeOffset RequestedAt,
    DateTimeOffset? NextAttemptAt, DateTimeOffset? LastAttemptAt, DateTimeOffset? CompletedAt) : IAuditableResponse
{
    object IAuditableResponse.AuditResult => this with { Recipient = ReservationMail.MaskRecipient(Recipient) };
}

/// <summary>V7 · Cola de correos de las sucursales visibles (los más recientes primero), por estado y por número de reserva.</summary>
[RequiresPermission(PermissionCodes.PcBuildManage)]
public sealed record GetOutgoingMailsQuery(OutgoingMailStatus? Status = null, string? Number = null, int Take = 200)
    : IRequest<IReadOnlyList<OutgoingMailRow>>;

public sealed class GetOutgoingMailsValidator : AbstractValidator<GetOutgoingMailsQuery>
{
    public GetOutgoingMailsValidator()
    {
        RuleFor(x => x.Status).IsInEnum().WithMessage("El estado del correo no existe.");
        RuleFor(x => x.Number).MaximumLength(40).WithMessage("El número de la reserva supera 40 caracteres.");
        RuleFor(x => x.Take).InclusiveBetween(1, OutgoingMailViews.MaxRows).WithMessage($"Se muestran de 1 a {OutgoingMailViews.MaxRows} correos.");
    }
}

public sealed class GetOutgoingMailsHandler(IMinvDbContext db, ICurrentUser user) : IRequestHandler<GetOutgoingMailsQuery, IReadOnlyList<OutgoingMailRow>>
{
    public async Task<IReadOnlyList<OutgoingMailRow>> Handle(GetOutgoingMailsQuery request, CancellationToken ct)
    {
        var query = OutgoingMailViews.Query(db);
        if (request.Status is { } status)
        {
            query = query.Where(x => x.Dispatch.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(request.Number))
        {
            var number = request.Number.Trim().ToUpperInvariant();
            query = query.Where(x => x.Number == number);
        }
        return await OutgoingMailViews.RowsAsync(db, user, query.OrderByDescending(x => x.Mail.RequestedAt).Take(request.Take), ct);
    }
}

/// <summary>
/// V7 · Reenvía la confirmación de una reserva VIGENTE: encola un correo nuevo para <see cref="Email"/> o, sin él, para el
/// correo de contacto de la reserva. Los correos de esa reserva que seguían pendientes se cancelan (el reenvío los reemplaza:
/// el cliente no recibe dos). Respeta los topes de 24 horas (<c>mail.recipient_limit</c>, <c>mail.company_limit</c>). No envía
/// nada: el despachador lo envía después del COMMIT (regla B-08). B6: si el despachador ya está enviando un pendiente de esa
/// reserva (arrendamiento vigente), se rechaza con <c>mail.sending</c> en vez de cancelarlo.
/// </summary>
[RequiresPermission(PermissionCodes.PcBuildManage)]
public sealed record ResendReservationMailCommand(string Number, string? Email = null) : IRequest<OutgoingMailRow>, IAuditableRequest
{
    // El correo del cliente no va completo a la auditoría (regla S-06)
    public object AuditDetails => new { Number, Email = Email is null ? null : ReservationMail.MaskRecipient(Email) };

    public override string ToString() => $"ResendReservationMailCommand {{ Number = {Number} }}";
}

public sealed class ResendReservationMailValidator : AbstractValidator<ResendReservationMailCommand>
{
    public ResendReservationMailValidator()
    {
        RuleFor(x => x.Number).NotEmpty().WithMessage("Indique la reserva.").MaximumLength(40);
        RuleFor(x => x.Email).MaximumLength(OutgoingMail.MaxRecipientLength).WithMessage("El correo supera 254 caracteres.")
            .Must(e => string.IsNullOrWhiteSpace(e) || OutgoingMail.IsValidRecipient(e)).WithMessage("El correo no es una dirección válida.");
    }
}

public sealed class ResendReservationMailHandler(IMinvDbContext db, ICurrentUser user, IClock clock)
    : IRequestHandler<ResendReservationMailCommand, OutgoingMailRow>
{
    public async Task<OutgoingMailRow> Handle(ResendReservationMailCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión.");
        var number = request.Number.Trim().ToUpperInvariant();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var build = await db.Set<PcBuild>().AsNoTracking().FirstOrDefaultAsync(b => b.Number == number, ct)
                            ?? throw new NotFoundException($"La reserva {number} no existe o es de otra sucursal.");
                var now = clock.UtcNow;
                Guard.That(build.Status == PcBuildStatus.Reserved, "pcbuild.state",
                    $"La reserva {build.Number} está {PcBuild.Describe(build.Status)}: solo se reenvía la confirmación de una reserva vigente.");
                Guard.That(!build.IsReservationExpired(now), "pcbuild.state",
                    $"La reserva {build.Number} ya venció: solo se reenvía la confirmación de una reserva vigente.");
                var pending = await (from d in db.Set<OutgoingMailDispatch>()
                                     join m in db.Set<OutgoingMail>() on d.OutgoingMailId equals m.Id
                                     where m.PcBuildId == build.Id && d.Status == OutgoingMailStatus.Pending
                                     select d).ToListAsync(ct);
                // B6 · Un pendiente que el despachador ya tomó está SALIENDO: cancelarlo no lo detiene (el cliente recibiría dos
                // y la cola diría «cancelado» de un correo que sí salió). Se espera a que termine
                Guard.That(!pending.Any(d => d.IsLeasedAt(now)), "mail.sending",
                    $"La confirmación de la reserva {build.Number} se está enviando en este momento: espere un par de minutos y vuelva a intentarlo " +
                    "(si llega, no hace falta reenviarla).");
                var result = await ReservationMail.EnqueueAsync(db, build, userId, now, ct, request.Email);
                if (result.Mail is not { } mail)
                {
                    throw Rejection(result.Skipped, build.Number);
                }
                foreach (var dispatch in pending)
                {
                    dispatch.Cancel(ReservationMail.ReplacedReason, now);
                }
                await db.SaveChangesAsync(ct);
                return (await OutgoingMailViews.RowsAsync(db, user, OutgoingMailViews.Query(db).Where(x => x.Mail.Id == mail.Id), ct))[0];
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                // El despachador tomó o cerró un pendiente de esta reserva mientras tanto: se vuelve a leer la cola
                db.ClearTracking();
            }
        }
    }

    /// <summary>Por qué no se puede reenviar (código estable y mensaje para el personal).</summary>
    private static DomainException Rejection(ReservationMailSkip skip, string number) => skip switch
    {
        ReservationMailSkip.NoRecipient => new DomainException("mail.no_recipient",
            $"La reserva {number} no tiene correo de contacto: indique a qué correo enviar la confirmación."),
        ReservationMailSkip.InvalidRecipient => new DomainException("mail.recipient", "El correo del destinatario no es una dirección válida."),
        ReservationMailSkip.RecipientLimit => new DomainException("mail.recipient_limit",
            $"Esa dirección ya recibió {ReservationMail.MaxPerRecipient} correos en las últimas 24 horas: espere o use otra dirección."),
        ReservationMailSkip.CompanyLimit or ReservationMailSkip.AnonymousLimit => new DomainException("mail.company_limit",
            $"La empresa llegó al tope de {ReservationMail.MaxPerCompany} correos en 24 horas: vuelva a intentarlo más tarde."),
        _ => new DomainException("pcbuild.state", $"La reserva {number} ya no está vigente: no se reenvía la confirmación."),
    };
}

/// <summary>V7 · Lectura de la cola de correos (la comparten la consulta y el reenvío).</summary>
internal static class OutgoingMailViews
{
    public const int MaxRows = 500;

    /// <summary>Correo, cola y reserva (propiedades con <c>init</c>: EF Core traduce los filtros y el orden sobre ellas).</summary>
    internal sealed class Item
    {
        public required OutgoingMail Mail { get; init; }

        public required OutgoingMailDispatch Dispatch { get; init; }

        public required string Number { get; init; }

        public required PcBuildKind ReservationKind { get; init; }
    }

    public static IQueryable<Item> Query(IMinvDbContext db) =>
        from m in db.Set<OutgoingMail>().AsNoTracking()
        join d in db.Set<OutgoingMailDispatch>().AsNoTracking() on m.Id equals d.OutgoingMailId
        join b in db.Set<PcBuild>().AsNoTracking() on m.PcBuildId equals b.Id
        select new Item { Mail = m, Dispatch = d, Number = b.Number, ReservationKind = b.Kind };

    public static async Task<IReadOnlyList<OutgoingMailRow>> RowsAsync(IMinvDbContext db, ICurrentUser user, IQueryable<Item> query, CancellationToken ct)
    {
        var items = await query.ToListAsync(ct);
        var ids = items.Select(i => i.Mail.Id).ToList();
        var lastAttempts = ids.Count == 0
            ? new Dictionary<Guid, DateTimeOffset>()
            : await db.Set<OutgoingMailAttempt>().AsNoTracking().Where(a => ids.Contains(a.OutgoingMailId))
                .GroupBy(a => a.OutgoingMailId).Select(g => new { g.Key, Last = g.Max(a => a.AttemptedAt) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        var branches = await db.Set<Branch>().AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        // El destinatario completo solo para quien gestiona las reservas (regla S-06)
        var showRecipient = user.HasPermission(PermissionCodes.PcBuildManage);
        return items.Select(i => new OutgoingMailRow(i.Mail.Id, i.Number, i.ReservationKind, branches.GetValueOrDefault(i.Mail.BranchId, "?"), i.Mail.Kind,
            KindText(i.Mail.Kind), showRecipient ? i.Mail.Recipient : ReservationMail.MaskRecipient(i.Mail.Recipient), i.Dispatch.Status,
            StatusText(i.Dispatch.Status), i.Dispatch.Attempts, OutgoingMailAttempt.MaxAttempts, i.Dispatch.LastError, i.Mail.RequestedAt,
            i.Dispatch.IsPending ? i.Dispatch.NextAttemptAt : null, lastAttempts.TryGetValue(i.Mail.Id, out var last) ? last : null,
            i.Dispatch.CompletedAt)).ToList();
    }

    public static string KindText(OutgoingMailKind kind) => kind switch
    {
        OutgoingMailKind.ReservationConfirmed => "Confirmación de reserva",
        _ => kind.ToString(),
    };

    public static string StatusText(OutgoingMailStatus status) => status switch
    {
        OutgoingMailStatus.Pending => "Pendiente",
        OutgoingMailStatus.Sent => "Enviado",
        OutgoingMailStatus.Exhausted => "Agotado",
        OutgoingMailStatus.Cancelled => "Cancelado",
        _ => status.ToString(),
    };
}
