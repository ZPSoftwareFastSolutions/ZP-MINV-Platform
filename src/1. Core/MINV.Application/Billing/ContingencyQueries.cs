using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Warehousing;

namespace MINV.Application.Billing;

// =====================================================================================================================
// V4.1 · Contingencia: consultas de eventos significativos, paquetes y CAFC, y alta de los talonarios CAFC. La
// declaración, el cierre y la recuperación de la contingencia son de la emisión (otro archivo).
// =====================================================================================================================

/// <summary>V4.1 · Filas de eventos significativos (tablero y consulta) con su sucursal, punto de venta, documentos y CAFC.</summary>
internal static class SiatEventRows
{
    public static async Task<IReadOnlyList<SignificantEventRow>> BuildAsync(IMinvDbContext db, IReadOnlyList<SignificantEvent> events, CancellationToken ct)
    {
        if (events.Count == 0)
        {
            return [];
        }
        var ids = events.Select(e => e.Id).ToList();
        var pointIds = events.Select(e => e.PointOfSaleId).Distinct().ToList();
        var cafcIds = events.Where(e => e.ContingencyCodeId is not null).Select(e => e.ContingencyCodeId!.Value).Distinct().ToList();
        var points = await db.Set<SiatPointOfSale>().AsNoTracking().Where(p => pointIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        var branches = await db.Set<Branch>().AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var cafcs = await db.Set<ContingencyCode>().AsNoTracking().Where(c => cafcIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
        var documents = await db.Set<FiscalDocument>().AsNoTracking().Where(d => d.SignificantEventId != null && ids.Contains(d.SignificantEventId.Value))
            .GroupBy(d => d.SignificantEventId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return events.Select(e => new SignificantEventRow(e.Id, e.BranchId, branches.GetValueOrDefault(e.BranchId, "?"), points.GetValueOrDefault(e.PointOfSaleId),
            e.Kind, e.EventCode, e.Description, e.StartedAt, e.EndedAt, e.Status, e.ReceptionCode, documents.GetValueOrDefault(e.Id),
            e.RegistrationDeadline, e.TranscriptionDeadline, e.ContingencyCodeId is { } c ? cafcs.GetValueOrDefault(c) : null)).ToList();
    }
}

public sealed class GetSignificantEventsHandler(IMinvDbContext db) : IRequestHandler<GetSignificantEventsQuery, IReadOnlyList<SignificantEventRow>>
{
    public async Task<IReadOnlyList<SignificantEventRow>> Handle(GetSignificantEventsQuery r, CancellationToken ct)
    {
        Guard.That(r.To >= r.From, "range.invalid", "La fecha final no puede ser anterior a la inicial.");
        var start = r.From.ToDateTime(TimeOnly.MinValue);
        var end = r.To.AddDays(1).ToDateTime(TimeOnly.MinValue);
        // Un evento que empezó antes del rango pero sigue abierto (o terminó dentro) también se muestra
        var events = await db.Set<SignificantEvent>().AsNoTracking()
            .Where(e => e.StartedAt < end && (e.EndedAt == null || e.EndedAt >= start))
            .OrderByDescending(e => e.StartedAt).ToListAsync(ct);
        return await SiatEventRows.BuildAsync(db, events, ct);
    }
}

public sealed class GetFiscalPackagesHandler(IMinvDbContext db) : IRequestHandler<GetFiscalPackagesQuery, IReadOnlyList<FiscalPackageRow>>
{
    public async Task<IReadOnlyList<FiscalPackageRow>> Handle(GetFiscalPackagesQuery r, CancellationToken ct)
    {
        var eventId = r.EventId;
        var packages = await db.Set<FiscalPackage>().AsNoTracking().Where(p => eventId == null || p.SignificantEventId == eventId)
            .OrderByDescending(p => p.SentAt).Take(1000).ToListAsync(ct);
        var ids = packages.Select(p => p.Id).ToList();
        var pointIds = packages.Select(p => p.PointOfSaleId).Distinct().ToList();
        var points = await db.Set<SiatPointOfSale>().AsNoTracking().Where(p => pointIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        var branches = await db.Set<Branch>().AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var documents = await db.Set<FiscalDocument>().AsNoTracking().Where(d => d.PackageId != null && ids.Contains(d.PackageId.Value))
            .GroupBy(d => d.PackageId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return packages.Select(p => new FiscalPackageRow(p.Id, p.SignificantEventId, branches.GetValueOrDefault(p.BranchId, "?"),
            points.GetValueOrDefault(p.PointOfSaleId), p.DocumentSector, p.Cafc, documents.GetValueOrDefault(p.Id), p.Status, p.ReceptionCode, p.SentAt,
            p.ValidatedAt, p.LastSiatCode, p.Messages)).ToList();
    }
}

public sealed class GetContingencyCodesHandler(IMinvDbContext db) : IRequestHandler<GetContingencyCodesQuery, IReadOnlyList<ContingencyCodeRow>>
{
    public async Task<IReadOnlyList<ContingencyCodeRow>> Handle(GetContingencyCodesQuery request, CancellationToken ct)
    {
        var codes = await db.Set<ContingencyCode>().AsNoTracking().ToListAsync(ct);
        var branches = await db.Set<Branch>().AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        // Usados = documentos transcritos con ese CAFC en esa sucursal (el CAFC viaja en el campo <cafc> de cada factura)
        var used = await db.Set<FiscalDocument>().AsNoTracking().Where(d => d.Cafc != null)
            .GroupBy(d => new { d.BranchId, d.Cafc }).Select(g => new { g.Key.BranchId, g.Key.Cafc, Count = g.Count() }).ToListAsync(ct);
        return codes.OrderBy(c => branches.GetValueOrDefault(c.BranchId)).ThenByDescending(c => c.IsActive).ThenBy(c => c.Code)
            .Select(c => new ContingencyCodeRow(c.Id, branches.GetValueOrDefault(c.BranchId, "?"), c.DocumentSector, c.Code, c.NumberFrom, c.NumberTo,
                c.ValidUntil, c.IsActive, used.Where(u => u.BranchId == c.BranchId && u.Cafc == c.Code).Sum(u => u.Count))).ToList();
    }
}

public sealed class RegisterContingencyCodeValidator : AbstractValidator<RegisterContingencyCodeCommand>
{
    public RegisterContingencyCodeValidator()
    {
        RuleFor(x => x.BranchCode).NotEmpty().WithMessage("Elija la sucursal del talonario.");
        RuleFor(x => x.Code).NotEmpty().WithMessage("Indique el CAFC que entregó el SIN.").MaximumLength(50);
        RuleFor(x => x.NumberFrom).GreaterThan(0).WithMessage("El primer número del talonario debe ser mayor que 0.");
        RuleFor(x => x.NumberTo).GreaterThanOrEqualTo(x => x.NumberFrom).WithMessage("El último número no puede ser menor que el primero.");
    }
}

public sealed class RegisterContingencyCodeHandler(IMinvDbContext db, ITenantContext tenant, IClock clock)
    : IRequestHandler<RegisterContingencyCodeCommand, string>
{
    public async Task<string> Handle(RegisterContingencyCodeCommand r, CancellationToken ct)
    {
        // Las notas crédito-débito no se emiten en contingencia (regla F-09): los talonarios CAFC son de facturas (sector 1)
        Guard.That(r.DocumentSector == SiatCodes.SectorPurchaseSale, "siat.cafc_sector",
            "Los talonarios CAFC son para facturas de compra-venta (sector 1): las notas crédito-débito no se emiten en contingencia.");
        var branch = await SiatAdminSupport.BranchAsync(db, r.BranchCode, ct);
        if (!db.Branches.Allows(branch.Id))
        {
            throw new AccessDeniedException("No puede registrar talonarios de una sucursal que no es suya.");
        }
        var code = r.Code.Trim();
        Guard.That(!await db.Set<ContingencyCode>().AnyAsync(c => c.BranchId == branch.Id && c.DocumentSector == r.DocumentSector && c.Code == code, ct),
            "siat.cafc_duplicate", $"El CAFC {code} ya está registrado en la sucursal {branch.Code}.");
        if (r.ValidUntil is { } until)
        {
            var zoneId = await db.Set<Domain.Iam.TenantConfig>().Select(c => c.TimeZoneId).FirstOrDefaultAsync(ct) ?? "America/La_Paz";
            Guard.That(until >= clock.TodayIn(zoneId), "siat.cafc_expired", $"El CAFC venció el {until:dd/MM/yyyy}.");
        }
        var overlapping = await db.Set<ContingencyCode>().AnyAsync(c => c.BranchId == branch.Id && c.DocumentSector == r.DocumentSector && c.IsActive
                                                                          && c.NumberFrom <= r.NumberTo && r.NumberFrom <= c.NumberTo, ct);
        db.Set<ContingencyCode>().Add(new ContingencyCode(tenant.TenantId, branch.Id, r.DocumentSector, code, r.NumberFrom, r.NumberTo, r.ValidUntil));
        await db.SaveChangesAsync(ct);
        return $"✔ Talonario CAFC {code} de la sucursal {branch.Code} registrado (números {r.NumberFrom} a {r.NumberTo}" +
               (r.ValidUntil is { } v ? $", vigente hasta {v:dd/MM/yyyy})" : ")") +
               (overlapping ? ". Atención: su rango se superpone con otro talonario activo de la sucursal." : ".");
    }
}
