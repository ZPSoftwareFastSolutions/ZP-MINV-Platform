using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.Application.Inventory.PhysicalCounts;

// ------------------------------------------------------------------------------------------------ abrir
/// <summary>Abre una toma física en un almacén (una abierta por almacén).</summary>
[RequiresPermission(PermissionCodes.PhysicalCountRecord)]
public sealed record OpenPhysicalCountCommand(string WarehouseCode, DateOnly? CountDate = null, string? Notes = null)
    : IRequest<OpenPhysicalCountResult>, IAuditableRequest
{
    public object AuditDetails => new { WarehouseCode, CountDate };
}

public sealed record OpenPhysicalCountResult(Guid PhysicalCountId, string Number);

public sealed class OpenPhysicalCountHandler(IMinvDbContext db, IClock clock)
    : IRequestHandler<OpenPhysicalCountCommand, OpenPhysicalCountResult>
{
    public async Task<OpenPhysicalCountResult> Handle(OpenPhysicalCountCommand request, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var warehouse = await lookups.WarehouseByCodeAsync(request.WarehouseCode, ct);
        var config = await lookups.ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var date = request.CountDate ?? today;
        Guard.That(date <= today && date >= config.MinBusinessDate, "count.date",
            "La fecha del conteo no es válida (futura o anterior a la mínima).");
        Guard.That(!await db.Set<PhysicalCount>().AnyAsync(c => c.WarehouseId == warehouse.Id && c.Status == PhysicalCountStatus.Open, ct),
            "count.already_open", $"Ya hay una toma física abierta en {warehouse.Code}.");
        var prefix = PhysicalCount.NumberFor(date, 1);
        var sameDay = await db.Set<PhysicalCount>().CountAsync(c => c.Number.StartsWith(prefix), ct);
        var count = PhysicalCount.Open(warehouse.TenantId, warehouse.BranchId, warehouse.Id, date, sameDay + 1, request.Notes);
        db.Set<PhysicalCount>().Add(count);
        await db.SaveChangesAsync(ct);
        return new OpenPhysicalCountResult(count.Id, count.Number);
    }
}

// ------------------------------------------------------------------------------------------------ contar
/// <summary>Registra lo contado de un producto en una posición (varias personas cuentan a la vez).</summary>
[RequiresPermission(PermissionCodes.PhysicalCountRecord)]
public sealed record RecordCountCommand(Guid PhysicalCountId, string Sku, string BinCode, decimal CountedQuantity, string? LotNumber = null)
    : IRequest<Guid>;

public sealed class RecordCountValidator : AbstractValidator<RecordCountCommand>
{
    public RecordCountValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(40);
        RuleFor(x => x.BinCode).NotEmpty().MaximumLength(40);
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0).WithMessage("El conteo debe ser un número mayor o igual a 0.");
    }
}

public sealed class RecordCountHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<RecordCountCommand, Guid>
{
    public async Task<Guid> Handle(RecordCountCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para contar.");
        var lookups = new InventoryLookups(db);
        var count = await db.Set<PhysicalCount>().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == request.PhysicalCountId, ct)
                    ?? throw new NotFoundException("La toma física no existe.");
        var item = await lookups.VariantBySkuAsync(request.Sku, ct);
        var bin = await lookups.BinByCodeAsync(request.BinCode, ct);
        Guard.That(await lookups.WarehouseOfBinAsync(bin.Id, ct) == count.WarehouseId, "count.other_warehouse",
            $"La posición {bin.Code} no pertenece al almacén de la toma {count.Number}.");
        Quantities.EnsureAllowed(request.CountedQuantity, item.Unit.AllowsDecimals, item.Unit.UnitCode);
        var batch = await lookups.BatchAsync(item.Variant, request.LotNumber, ct);
        var (level, _) = await lookups.StockLevelAsync(count.TenantId, bin.Id, batch.Id, ct);
        var line = count.RecordCount(level.Id, request.CountedQuantity, userId, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return line.Id;
    }
}

// ------------------------------------------------------------------------------------------------ contabilizar
/// <summary>
/// Genera los ajustes de la toma física (sucesor de GenerarAjustesConteo.ts): exige confirmación explícita, valida todo
/// y registra AJUSTE (±) o SALDO INICIAL contra el stock exacto en UNA transacción.
/// </summary>
[RequiresPermission(PermissionCodes.PhysicalCountPost)]
public sealed record PostPhysicalCountCommand(Guid PhysicalCountId, bool Confirmed) : IRequest<PostPhysicalCountResult>, IAuditableRequest
{
    public object AuditDetails => new { PhysicalCountId, Confirmed };
}

public sealed record PostPhysicalCountResult(string Number, int Movements, int Surpluses, int Shortages, int InitialBalances, int Matching, string Message);

public sealed class PostPhysicalCountHandler(IMinvDbContext db, ICurrentUser user, IClock clock)
    : IRequestHandler<PostPhysicalCountCommand, PostPhysicalCountResult>
{
    public async Task<PostPhysicalCountResult> Handle(PostPhysicalCountCommand request, CancellationToken ct)
    {
        Guard.That(request.Confirmed, "count.not_confirmed", "Confirme la generación de ajustes.");
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para contabilizar.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await PostAsync(request, userId, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();   // otra sesión movió una existencia o numeró un asiento a la vez: se reintenta
            }
        }
    }

    private async Task<PostPhysicalCountResult> PostAsync(PostPhysicalCountCommand request, Guid userId, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var count = await db.Set<PhysicalCount>().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == request.PhysicalCountId, ct)
                    ?? throw new NotFoundException("La toma física no existe.");
        var levelIds = count.Lines.Select(l => l.StockLevelId).ToList();
        var levels = await db.Set<StockLevel>().Where(l => levelIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var info = await (from l in db.Set<StockLevel>()
                          join b in db.Set<Batch>() on l.BatchId equals b.Id
                          join v in db.Set<Domain.Catalog.ProductVariant>() on b.VariantId equals v.Id
                          join p in db.Set<Domain.Catalog.Product>() on v.ProductId equals p.Id
                          join u in db.Set<Domain.Catalog.UnitOfMeasure>() on p.BaseUnitId equals u.Id
                          where levelIds.Contains(l.Id)
                          select new
                          {
                              l.Id, VariantId = v.Id, v.Sku, u.Code, u.AllowsDecimals,
                              HasMovements = db.Set<StockMovement>().Any(m => m.StockLevelId == l.Id),
                              Serialized = p.TrackingMode == Domain.Catalog.TrackingMode.Serial,
                          }).ToListAsync(ct);
        var items = info.ToDictionary(i => i.Id, i => new CountItemInfo(i.Sku, new UnitRule(i.Code, i.AllowsDecimals), i.HasMovements));
        // V4.2 · Un producto serializado no se ajusta por cantidad (regla T-02): su diferencia se registra con las series
        var serialized = info.Where(i => i.Serialized).Select(i => i.Id).ToHashSet();
        var differences = count.Lines.Where(l => serialized.Contains(l.StockLevelId) && levels.TryGetValue(l.StockLevelId, out var level)
                                                  && l.CountedQuantity != level.QuantityOnHand)
            .Select(l => items[l.StockLevelId].Sku).Distinct().Order(StringComparer.Ordinal).ToList();
        Guard.That(differences.Count == 0, "serial.count_adjustment",
            $"{string.Join(", ", differences)} lleva(n) serie: registre la diferencia con un ajuste que indique las series (Registrar movimiento).");
        var types = await lookups.CountTypesAsync(ct);
        var now = clock.UtcNow;
        var movements = count.Post(levels, items, types, userId, now);
        db.Set<StockMovement>().AddRange(movements);

        // V4.2 · Los sobrantes y faltantes se contabilizan al costo promedio del almacén (5.1.09 / 4.1.02 contra 1.1.05; ver
        // InventoryAdjustments); el SALDO INICIAL de una existencia sin movimientos es parte de la apertura (no se contabiliza)
        var variantOf = info.ToDictionary(i => i.Id, i => i.VariantId);
        var costs = new Dictionary<Guid, decimal>();
        var (surplus, shortage) = (0m, 0m);
        foreach (var movement in movements.Where(m => m.MovementTypeId == types.AdjustmentIn.Id || m.MovementTypeId == types.AdjustmentOut.Id))
        {
            var variantId = variantOf[movement.StockLevelId];
            if (!costs.TryGetValue(variantId, out var cost))
            {
                costs[variantId] = cost = await AverageCosts.CurrentAsync(db, variantId, count.WarehouseId, ct);
            }
            if (movement.MovementTypeId == types.AdjustmentIn.Id)
            {
                surplus += movement.Quantity * cost;
            }
            else
            {
                shortage += movement.Quantity * cost;
            }
        }
        var config = await lookups.ConfigAsync(ct);
        var entry = await InventoryAdjustments.PostAsync(db, count.TenantId, count.BranchId, userId, count.CountDate, clock.TodayIn(config.TimeZoneId),
            $"Toma física {count.Number}: sobrantes y faltantes al costo promedio", surplus, shortage, now, count.Id, ct);
        await db.SaveChangesAsync(ct);

        int Of(string code) => movements.Count(m => m.MovementTypeId == code switch
        {
            MovementTypeCodes.AdjustmentIn => types.AdjustmentIn.Id,
            MovementTypeCodes.AdjustmentOut => types.AdjustmentOut.Id,
            _ => types.InitialBalance.Id,
        });
        var result = new PostPhysicalCountResult(count.Number, movements.Count, Of(MovementTypeCodes.AdjustmentIn),
            Of(MovementTypeCodes.AdjustmentOut), Of(MovementTypeCodes.InitialBalance), count.Lines.Count - movements.Count, "");
        var message = movements.Count == 0
            ? $"✔ {count.Number}: los {count.Lines.Count} producto(s) contados cuadran; no se generaron ajustes"
            : $"✔ {count.Number}: {movements.Count} movimiento(s) ({result.Surpluses} sobrante(s), {result.Shortages} faltante(s), " +
              $"{result.InitialBalances} saldo(s) inicial(es)) · {result.Matching} cuadran" + (entry is null ? string.Empty : $" · asiento {entry.Number}");
        return result with { Message = message };
    }
}
