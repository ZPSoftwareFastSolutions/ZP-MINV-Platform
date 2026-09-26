using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;
using MINV.Domain.Sales;
using MINV.Domain.Service;
using MINV.Domain.Warehousing;

namespace MINV.Application.Tech;

// =====================================================================================================================
// V4.2 · Garantías y RMA (regla T-05): abrir un caso por una unidad vendida (en garantía o reparación con cargo), avanzar
// según la tabla de transiciones del agregado, reponer con otra unidad (salida real con su asiento 5.1.10) y consultar.
// Numeración RMA-<sucursal>-000001 con reintento.
// =====================================================================================================================

/// <summary>V4.2 · Filas de los casos RMA (serie, producto, cliente, proveedor, reemplazo y garantía derivada).</summary>
internal sealed class ClaimViews(IMinvDbContext db, IClock clock)
{
    public async Task<IReadOnlyList<WarrantyClaimRow>> RowsAsync(IReadOnlyList<WarrantyClaim> claims, CancellationToken ct)
    {
        if (claims.Count == 0)
        {
            return [];
        }
        var serialIds = claims.Select(c => c.SerialNumberId).Concat(claims.Where(c => c.ReplacementSerialId is not null).Select(c => c.ReplacementSerialId!.Value))
            .Distinct().ToList();
        var units = await db.Set<SerialNumber>().AsNoTracking().Where(s => serialIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var sales = await new SerialViews(db, clock).SalesAsync(claims.Select(c => units[c.SerialNumberId]).DistinctBy(u => u.Id).ToList(), ct);
        var variantIds = units.Values.Select(u => u.VariantId).Distinct().ToList();
        var items = await (from v in db.Set<ProductVariant>()
                           join p in db.Set<Product>() on v.ProductId equals p.Id
                           where variantIds.Contains(v.Id)
                           select new { v.Id, v.Sku, p.Name }).ToDictionaryAsync(x => x.Id, ct);
        var customerIds = claims.Select(c => c.CustomerId).Distinct().ToList();
        var customers = await db.Set<Customer>().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var supplierIds = claims.Where(c => c.SupplierId is not null).Select(c => c.SupplierId!.Value).Distinct().ToList();
        var suppliers = await db.Set<Supplier>().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.LegalName, ct);
        var branches = await db.Set<Branch>().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var now = clock.UtcNow;
        return claims.Select(c =>
        {
            var unit = units[c.SerialNumberId];
            var item = items[unit.VariantId];
            return new WarrantyClaimRow(c.Id, c.Number, branches.GetValueOrDefault(c.BranchId, "?"), unit.Serial, item.Sku, item.Name,
                customers.GetValueOrDefault(c.CustomerId, "?"), c.Issue, c.Status, c.IsInWarranty, sales[unit.Id].WarrantyUntil, c.ReceivedAt, c.ClosedAt,
                c.SupplierId is { } s ? suppliers.GetValueOrDefault(s) : null, c.Resolution,
                c.ReplacementSerialId is { } r ? units[r].Serial : null, (int)((c.ClosedAt ?? now) - c.ReceivedAt).TotalDays);
        }).ToList();
    }

    public async Task<WarrantyClaimRow> RowAsync(WarrantyClaim claim, CancellationToken ct) => (await RowsAsync([claim], ct))[0];
}

/// <summary>Efectos de un cambio de estado del caso en su serie (la bitácora de la serie deja el mismo documento).</summary>
internal static class ClaimSerials
{
    public static void Apply(SerialNumber unit, WarrantyClaimStatus previous, WarrantyClaimStatus next, SerialContext context)
    {
        switch (next)
        {
            case WarrantyClaimStatus.SentToSupplier:
                unit.SendToSupplier(context);
                break;
            case WarrantyClaimStatus.Repaired:
                unit.MarkRepaired(context);
                break;
            case WarrantyClaimStatus.Replaced when previous == WarrantyClaimStatus.SentToSupplier:
                // El proveedor se queda con la unidad defectuosa (la reemplazó): sale del RMA como devuelta al proveedor
                unit.ReturnToSupplier(null, context);
                break;
            case WarrantyClaimStatus.Replaced:
                unit.MarkReplaced(context);
                break;
            case WarrantyClaimStatus.Delivered when previous is WarrantyClaimStatus.Repaired or WarrantyClaimStatus.Rejected:
                unit.ReturnFromRma(context);
                break;
        }
    }
}

internal static class Claims
{
    public static async Task<WarrantyClaim> LoadAsync(IMinvDbContext db, string number, CancellationToken ct)
    {
        var n = number.Trim().ToUpperInvariant();
        return await db.Set<WarrantyClaim>().FirstOrDefaultAsync(c => c.Number == n, ct)
               ?? throw new NotFoundException($"El caso {n} no existe o es de otra sucursal.");
    }
}

public sealed class GetWarrantyClaimsHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetWarrantyClaimsQuery, IReadOnlyList<WarrantyClaimRow>>
{
    public async Task<IReadOnlyList<WarrantyClaimRow>> Handle(GetWarrantyClaimsQuery request, CancellationToken ct)
    {
        var query = db.Set<WarrantyClaim>().AsNoTracking();
        if (request.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }
        if (request.OnlyOpen)
        {
            query = query.Where(c => c.Status != WarrantyClaimStatus.Delivered);
        }
        return await new ClaimViews(db, clock).RowsAsync(await query.OrderByDescending(c => c.ReceivedAt).Take(2000).ToListAsync(ct), ct);
    }
}

public sealed class GetWarrantyClaimHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetWarrantyClaimQuery, WarrantyClaimDetail>
{
    public async Task<WarrantyClaimDetail> Handle(GetWarrantyClaimQuery request, CancellationToken ct)
    {
        var number = request.Number.Trim().ToUpperInvariant();
        var claim = await db.Set<WarrantyClaim>().AsNoTracking().Include(c => c.History).FirstOrDefaultAsync(c => c.Number == number, ct)
                    ?? throw new NotFoundException($"El caso {number} no existe o es de otra sucursal.");
        var unit = await db.Set<SerialNumber>().AsNoTracking().FirstAsync(s => s.Id == claim.SerialNumberId, ct);
        var userIds = claim.History.Select(h => h.UserId).Distinct().ToList();
        var users = await db.Set<User>().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var invoice = claim.InvoiceId is { } id ? await db.Set<Invoice>().Where(i => i.Id == id).Select(i => i.Number).FirstOrDefaultAsync(ct) : null;
        var warranty = await new SerialViews(db, clock).WarrantyAsync(unit, ct);
        return new WarrantyClaimDetail(await new ClaimViews(db, clock).RowAsync(claim, ct), warranty,
            claim.History.OrderBy(h => h.OccurredAt).Select(h => new WarrantyClaimEventView(h.OccurredAt, h.Action, h.Status, h.Note,
                users.GetValueOrDefault(h.UserId, "?"))).ToList(),
            invoice ?? warranty.InvoiceNumber, WarrantyClaim.Transitions[claim.Status]);
    }
}

public sealed class OpenWarrantyClaimValidator : AbstractValidator<OpenWarrantyClaimCommand>
{
    public OpenWarrantyClaimValidator()
    {
        RuleFor(x => x.Serial).NotEmpty().WithMessage("Indique la serie o el IMEI del equipo.");
        RuleFor(x => x.Issue).NotEmpty().WithMessage("Describa la falla reportada.").MinimumLength(5).MaximumLength(500);
    }
}

/// <summary>Abre el caso en la sucursal activa: la serie pasa a «en RMA» (sin entrada de stock); garantía derivada de la
/// venta; fuera de garantía solo como reparación con cargo explícita.</summary>
public sealed class OpenWarrantyClaimHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<OpenWarrantyClaimCommand, WarrantyClaimRow>
{
    public async Task<WarrantyClaimRow> Handle(OpenWarrantyClaimCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para abrir un caso.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var claim = await OpenAsync(request, userId, ct);
                await db.SaveChangesAsync(ct);
                return await new ClaimViews(db, clock).RowAsync(claim, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    private async Task<WarrantyClaim> OpenAsync(OpenWarrantyClaimCommand request, Guid userId, CancellationToken ct)
    {
        var unit = await SerialLedger.ResolveAsync(db, request.Serial, request.Sku, ct);
        Guard.That(unit.Status is SerialNumberStatus.Sold or SerialNumberStatus.Returned, SerialErrorCodes.NotAvailable,
            $"Solo se abre un caso de garantía de una unidad vendida: la serie {unit.Serial} está {SerialNumber.Describe(unit.Status)}.");
        var open = await db.Set<WarrantyClaim>().Where(c => c.SerialNumberId == unit.Id && c.Status != WarrantyClaimStatus.Delivered)
            .Select(c => c.Number).FirstOrDefaultAsync(ct);
        Guard.That(open is null, "rma.open_exists", $"La serie {unit.Serial} ya tiene el caso {open} abierto.");
        var branchId = await BranchContext.ResolveAsync(db, null, ct);
        var sale = (await new SerialViews(db, clock).SalesAsync([unit], ct))[unit.Id];
        Customer? customer = sale.Customer;
        if (!string.IsNullOrWhiteSpace(request.CustomerCode))
        {
            var code = request.CustomerCode.Trim().ToUpperInvariant();
            customer = await db.Set<Customer>().FirstOrDefaultAsync(c => c.Code == code, ct) ?? throw new NotFoundException($"El cliente {code} no existe.");
        }
        Guard.That(customer is not null, "rma.customer_required",
            $"La serie {unit.Serial} se vendió en otra sucursal (o sin venta registrada): indique el cliente del caso.");
        Guard.That(sale.InWarranty || request.ChargeableRepair, "rma.out_of_warranty",
            sale.WarrantyUntil is { } until
                ? $"La garantía de {unit.Serial} venció el {until:dd/MM/yyyy}: ábralo como reparación con cargo."
                : $"La serie {unit.Serial} no tiene una venta con garantía: ábralo como reparación con cargo.");
        var productId = await db.Set<ProductVariant>().Where(v => v.Id == unit.VariantId).Select(v => v.ProductId).FirstAsync(ct);
        var supplierId = await db.Set<ProductSupplier>().Where(p => p.ProductId == productId && p.IsPreferred).Select(p => (Guid?)p.SupplierId)
            .FirstOrDefaultAsync(ct);
        var now = clock.UtcNow;
        var number = await Documents.NextForBranchAsync<WarrantyClaim>(db, c => c.Number, "RMA", branchId, ct);
        var claim = new WarrantyClaim(unit.TenantId, branchId, number, unit.Id, customer!.Id, sale.InvoiceBranchId == branchId ? sale.InvoiceId : null,
            request.Issue.Trim(), sale.InWarranty, supplierId, userId, now);
        unit.SendToRma(new SerialContext(branchId, userId, now, number, (sale.InWarranty ? "Garantía: " : "Reparación con cargo: ") + request.Issue.Trim()));
        db.Set<WarrantyClaim>().Add(claim);
        return claim;
    }
}

public sealed class MoveWarrantyClaimValidator : AbstractValidator<MoveWarrantyClaimCommand>
{
    public MoveWarrantyClaimValidator()
    {
        RuleFor(x => x.Number).NotEmpty().WithMessage("Indique el caso.");
        RuleFor(x => x.Resolution).MaximumLength(500);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class MoveWarrantyClaimHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<MoveWarrantyClaimCommand, WarrantyClaimRow>
{
    public async Task<WarrantyClaimRow> Handle(MoveWarrantyClaimCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var claim = await Claims.LoadAsync(db, request.Number, ct);
                var unit = await db.Set<SerialNumber>().FirstAsync(s => s.Id == claim.SerialNumberId, ct);
                Guid? supplierId = null;
                if (!string.IsNullOrWhiteSpace(request.SupplierCode))
                {
                    var code = request.SupplierCode.Trim().ToUpperInvariant();
                    supplierId = await db.Set<Supplier>().Where(s => s.Code == code).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct)
                                 ?? throw new NotFoundException($"El proveedor {code} no existe.");
                }
                var now = clock.UtcNow;
                var previous = claim.Status;
                claim.MoveTo(request.Next, request.Resolution?.Trim(), supplierId, userId, now, string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim());
                ClaimSerials.Apply(unit, previous, request.Next,
                    new SerialContext(claim.BranchId, userId, now, claim.Number, $"{WarrantyClaim.Describe(request.Next)}{(request.Note is null ? string.Empty : " · " + request.Note.Trim())}"));
                await db.SaveChangesAsync(ct);
                return await new ClaimViews(db, clock).RowAsync(claim, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }
}

public sealed class AddWarrantyClaimNoteValidator : AbstractValidator<AddWarrantyClaimNoteCommand>
{
    public AddWarrantyClaimNoteValidator() => RuleFor(x => x.Note).NotEmpty().WithMessage("Escriba la nota.").MaximumLength(500);
}

public sealed class AddWarrantyClaimNoteHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<AddWarrantyClaimNoteCommand, WarrantyClaimRow>
{
    public async Task<WarrantyClaimRow> Handle(AddWarrantyClaimNoteCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión.");
        var claim = await Claims.LoadAsync(db, request.Number, ct);
        claim.AddNote(request.Note.Trim(), userId, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return await new ClaimViews(db, clock).RowAsync(claim, ct);
    }
}

public sealed class IssueWarrantyReplacementValidator : AbstractValidator<IssueWarrantyReplacementCommand>
{
    public IssueWarrantyReplacementValidator()
    {
        RuleFor(x => x.Number).NotEmpty().WithMessage("Indique el caso.");
        RuleFor(x => x.ReplacementSerial).NotEmpty().WithMessage("Indique la serie de la unidad de reemplazo.");
        RuleFor(x => x.Resolution).MaximumLength(500);
    }
}

/// <summary>
/// Reposición por garantía (regla T-05): otra unidad del MISMO producto, en stock en la sucursal del caso, sale con el
/// movimiento REPOSICIÓN POR GARANTÍA y el asiento Debe 5.1.10 Costo de garantías / Haber 1.1.05 Inventario al costo
/// promedio; queda vendida al cliente del caso. Reintento optimista ×3 (existencias y numeración de asientos).
/// </summary>
public sealed class IssueWarrantyReplacementHandler(IMinvDbContext db, ICurrentUser user, IClock clock)
    : IRequestHandler<IssueWarrantyReplacementCommand, WarrantyClaimRow>
{
    public async Task<WarrantyClaimRow> Handle(IssueWarrantyReplacementCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var claim = await ReplaceAsync(request, userId, ct);
                await db.SaveChangesAsync(ct);
                return await new ClaimViews(db, clock).RowAsync(claim, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    private async Task<WarrantyClaim> ReplaceAsync(IssueWarrantyReplacementCommand request, Guid userId, CancellationToken ct)
    {
        var claim = await Claims.LoadAsync(db, request.Number, ct);
        var original = await db.Set<SerialNumber>().FirstAsync(s => s.Id == claim.SerialNumberId, ct);
        var lookups = new InventoryLookups(db);
        var item = await (from v in db.Set<ProductVariant>()
                          join p in db.Set<Product>() on v.ProductId equals p.Id
                          join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                          where v.Id == original.VariantId
                          select new { v, p, u.Code, u.AllowsDecimals }).FirstAsync(ct);
        var info = new VariantInfo(item.v, item.p, new UnitRule(item.Code, item.AllowsDecimals));
        var ledger = new SerialLedger(db);
        var serials = await ledger.ExpectAsync(info, 1, [request.ReplacementSerial], ct);
        Guard.That(serials[0] != original.Serial, "rma.replacement_same", "El reemplazo debe ser otra unidad.");
        var group = (await ledger.OnHandAsync(info, serials, claim.BranchId, null, ct))[0];
        var replacement = group.Units[0];
        var now = clock.UtcNow;
        var config = await lookups.ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var detail = $"Reposición por garantía {claim.Number} de la serie {original.Serial}";
        // El caso primero: valida el estado (diagnóstico o proveedor) y que sea el primer reemplazo
        claim.SetReplacement(replacement.Id, userId, now, $"Unidad de reemplazo {replacement.Serial} entregada");
        var type = await lookups.MovementTypeAsync(MovementTypeCodes.WarrantyReplacement, ct);
        db.Set<StockMovement>().Add(group.Level.Register(type, 1, info.Unit, new MovementContext(userId, today, now, claim.Number, detail)));
        replacement.IssueAsReplacement(group.Level, new SerialContext(claim.BranchId, userId, now, claim.Number, detail));
        var warehouseId = await lookups.WarehouseOfBinAsync(group.Level.BinId, ct);
        var cost = JournalPoster.Money(await AverageCosts.CurrentAsync(db, info.Variant.Id, warehouseId, ct));
        if (cost > 0)
        {
            await JournalPoster.PostAsync(db, claim.TenantId, claim.BranchId, userId, today, $"{detail} ({info.Variant.Sku}, serie {replacement.Serial})",
                [new JournalLineSpec(AccountCodes.WarrantyCost, cost, 0), new JournalLineSpec(AccountCodes.Inventory, 0, cost)], now, claim.Id, ct);
        }
        if (!string.IsNullOrWhiteSpace(request.Resolution))
        {
            var previous = claim.Status;
            claim.MoveTo(WarrantyClaimStatus.Replaced, request.Resolution.Trim(), null, userId, now);
            ClaimSerials.Apply(original, previous, WarrantyClaimStatus.Replaced,
                new SerialContext(claim.BranchId, userId, now, claim.Number, $"Reemplazada por {replacement.Serial}"));
        }
        return claim;
    }
}
