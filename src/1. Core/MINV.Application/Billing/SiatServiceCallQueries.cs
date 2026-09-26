using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Application.Billing;

/// <summary>
/// V4.1 · Bitácora técnica de las llamadas al SIN (recurso, operación, duración, HTTP, código SIAT y cuerpos SOAP SIN el
/// token, regla F-12): evidencia para la inspección (Fase II) y para soporte. Del día más reciente al más antiguo.
/// </summary>
public sealed class GetSiatServiceCallsHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetSiatServiceCallsQuery, IReadOnlyList<SiatServiceCallRow>>
{
    public async Task<IReadOnlyList<SiatServiceCallRow>> Handle(GetSiatServiceCallsQuery r, CancellationToken ct)
    {
        Guard.That(r.To >= r.From, "range.invalid", "La fecha final no puede ser anterior a la inicial.");
        var zone = await new BillingLookups(db, null, clock).ZoneAsync(ct);
        // Límites del día en la zona de la empresa, pasados a UTC (timestamptz de PostgreSQL solo admite desfase 0)
        var start = SiatStatusBuilder.At(r.From.ToDateTime(TimeOnly.MinValue), zone).ToUniversalTime();
        var end = SiatStatusBuilder.At(r.To.AddDays(1).ToDateTime(TimeOnly.MinValue), zone).ToUniversalTime();
        var max = Math.Clamp(r.Max, 1, 5000);
        var query = db.Set<SiatServiceCall>().AsNoTracking().Where(c => c.OccurredAt >= start && c.OccurredAt < end);
        if (!string.IsNullOrWhiteSpace(r.Operation))
        {
            var operation = r.Operation.Trim().ToUpperInvariant();
            query = query.Where(c => c.Operation.ToUpper().Contains(operation));
        }
        return await query.OrderByDescending(c => c.OccurredAt).Take(max)
            .Select(c => new SiatServiceCallRow(c.OccurredAt, c.Resource, c.Operation, c.PointOfSaleCode, c.DurationMs, c.HttpStatus, c.SiatCode,
                c.Succeeded, c.Error, c.RequestBody, c.ResponseBody))
            .ToListAsync(ct);
    }
}
