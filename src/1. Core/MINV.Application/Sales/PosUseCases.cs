using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Application.Sales;

/// <summary>Abre un turno de caja (requiere el módulo POS y hardware licenciado).</summary>
[RequiresModule(LicenseModuleCodes.PosHardware)]
[RequiresPermission(PermissionCodes.PosOperate)]
public sealed record OpenPosSessionCommand(string RegisterCode, decimal OpeningCash) : IRequest<Guid>, IAuditableRequest
{
    public object AuditDetails => new { RegisterCode, OpeningCash };
}

public sealed class OpenPosSessionValidator : AbstractValidator<OpenPosSessionCommand>
{
    public OpenPosSessionValidator()
    {
        RuleFor(x => x.RegisterCode).NotEmpty().WithMessage("Indique la caja.");
        RuleFor(x => x.OpeningCash).GreaterThanOrEqualTo(0).WithMessage("El fondo inicial no puede ser negativo.");
    }
}

public sealed class OpenPosSessionHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<OpenPosSessionCommand, Guid>
{
    public async Task<Guid> Handle(OpenPosSessionCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para abrir caja.");
        var code = request.RegisterCode.Trim().ToUpperInvariant();
        var register = await db.Set<PosRegister>().FirstOrDefaultAsync(r => r.Code == code && r.IsActive, ct)
                       ?? throw new NotFoundException($"La caja {code} no existe o está inactiva.");
        Guard.That(!await db.Set<PosSession>().AnyAsync(s => s.PosRegisterId == register.Id && s.Status == PosSessionStatus.Open, ct),
            "pos.already_open", $"La caja {code} ya tiene un turno abierto.");
        var session = PosSession.Open(register.TenantId, register.Id, userId, request.OpeningCash, clock.UtcNow);
        db.Set<PosSession>().Add(session);
        await db.SaveChangesAsync(ct);   // el índice único parcial impide dos turnos abiertos aunque dos cajeros abran a la vez
        return session.Id;
    }
}

/// <summary>Cierra el turno con el arqueo de efectivo.</summary>
[RequiresModule(LicenseModuleCodes.PosHardware)]
[RequiresPermission(PermissionCodes.PosOperate)]
public sealed record ClosePosSessionCommand(Guid SessionId, decimal CountedCash) : IRequest<decimal>, IAuditableRequest
{
    public object AuditDetails => new { SessionId, CountedCash };
}

public sealed class ClosePosSessionHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<ClosePosSessionCommand, decimal>
{
    public async Task<decimal> Handle(ClosePosSessionCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para cerrar caja.");
        var session = await db.Set<PosSession>().FirstOrDefaultAsync(s => s.Id == request.SessionId, ct)
                      ?? throw new NotFoundException("El turno de caja no existe.");
        var cash = await db.Set<CashMovement>().Where(m => m.PosSessionId == session.Id)
            .GroupBy(m => m.Direction).Select(g => new { g.Key, Total = g.Sum(m => m.Amount) }).ToListAsync(ct);
        var cashPayments = await (from p in db.Set<Payment>()
                                  join pm in db.Set<PaymentMethod>() on p.PaymentMethodId equals pm.Id
                                  where p.PosSessionId == session.Id && pm.OpensCashDrawer
                                  select (decimal?)p.Amount).SumAsync(ct) ?? 0m;
        var expected = session.ExpectedCash(cashPayments,
            cash.Where(c => c.Key == CashDirection.In).Sum(c => c.Total),
            cash.Where(c => c.Key == CashDirection.Out).Sum(c => c.Total));
        session.Close(userId, request.CountedCash, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return session.CashDifference(expected) ?? 0m;
    }
}
