using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Accounting;
using MINV.Domain.Common;
using MINV.Domain.Inventory;
using MINV.Domain.Iam;

namespace MINV.Application.Common;

/// <summary>Numeración de documentos «PREFIJO-000001» (única por empresa; si dos cajas numeran a la vez, la base
/// rechaza el duplicado y el caso de uso reintenta). V4: cada sucursal numera con su código («F-CM-000001»,
/// «F-EA-000001»): una sucursal solo ve sus documentos (filtro y RLS) y así nunca choca con la numeración de otra.</summary>
public static class Documents
{
    /// <summary>Prefijo de una sucursal: «F» + código de la sucursal → «F-CM».</summary>
    public static async Task<string> BranchPrefixAsync(IMinvDbContext db, string prefix, Guid branchId, CancellationToken ct)
    {
        var code = db.Set<Domain.Warehousing.Branch>().Local.FirstOrDefault(b => b.Id == branchId)?.Code
                   ?? await db.Set<Domain.Warehousing.Branch>().Where(b => b.Id == branchId).Select(b => b.Code).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException("La sucursal no existe.");
        return prefix + "-" + code;
    }

    /// <summary>Número siguiente de un documento de la sucursal.</summary>
    public static async Task<string> NextForBranchAsync<T>(IMinvDbContext db, Expression<Func<T, string>> number, string prefix, Guid branchId,
        CancellationToken ct) where T : class =>
        await NextNumberAsync(db.Set<T>(), number, await BranchPrefixAsync(db, prefix, branchId, ct), ct);

    public static async Task<string> NextNumberAsync<T>(DbSet<T> set, Expression<Func<T, string>> number, string prefix, CancellationToken ct)
        where T : class
    {
        var start = prefix + "-";
        var fromDb = await set.Select(number).Where(n => n.StartsWith(start)).OrderByDescending(n => n).FirstOrDefaultAsync(ct);
        var compiled = number.Compile();
        var fromLocal = set.Local.Select(compiled).Where(n => n.StartsWith(start, StringComparison.Ordinal))
            .OrderByDescending(n => n, StringComparer.Ordinal).FirstOrDefault();
        var last = new[] { fromDb, fromLocal }.Where(n => n is not null).Max(StringComparer.Ordinal);
        var sequence = last is not null && int.TryParse(last[start.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n + 1 : 1;
        return start + sequence.ToString("000000", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// V4 · Sucursal de un documento que no nace de un almacén (asiento manual, gasto): la indicada (si el alcance de la
/// sesión la permite), la activa, la única del alcance o la del almacén por defecto de la empresa.
/// </summary>
public static class BranchContext
{
    public static async Task<Guid> ResolveAsync(IMinvDbContext db, Guid? requested, CancellationToken ct)
    {
        var scope = db.Branches;
        if (requested is Guid id)
        {
            if (!scope.Allows(id))
            {
                throw new AccessDeniedException("No tiene acceso a esa sucursal.");
            }
            _ = await db.Set<Domain.Warehousing.Branch>().Where(b => b.Id == id && b.IsActive).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("La sucursal no existe o está inactiva.");
            return id;
        }
        if (scope.ActiveBranchId is Guid active)
        {
            return active;
        }
        if (!scope.AllBranches && scope.BranchIds.Count == 1)
        {
            return scope.BranchIds.First();
        }
        var config = await db.Set<TenantConfig>().FirstAsync(ct);
        var branches = db.Set<Domain.Warehousing.Warehouse>();
        return await branches.Where(w => w.Id == config.DefaultWarehouseId).Select(w => (Guid?)w.BranchId).FirstOrDefaultAsync(ct)
               ?? await db.Set<Domain.Warehousing.Branch>().Where(b => b.IsActive).OrderBy(b => b.Code).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct)
               ?? throw new NotFoundException("La empresa no tiene sucursales.");
    }
}

/// <summary>Línea de un asiento: cuenta (código), debe, haber y detalle.</summary>
public sealed record JournalLineSpec(string AccountCode, decimal Debit, decimal Credit, string? Memo = null);

/// <summary>
/// Contabilidad automática: arma y contabiliza un asiento de partida doble (crea el período del mes si no existe y
/// rechaza los cerrados). Lo usan las ventas, las compras y los asientos manuales.
/// </summary>
public static class JournalPoster
{
    public static async Task<JournalEntry> PostAsync(IMinvDbContext db, Guid tenantId, Guid branchId, Guid userId, DateOnly date, string description,
        IReadOnlyList<JournalLineSpec> lines, DateTimeOffset now, Guid? correlationId, CancellationToken ct)
    {
        var effective = lines.Select(l => l with { Debit = Money(l.Debit), Credit = Money(l.Credit) })
            .Where(l => l.Debit > 0 || l.Credit > 0).ToList();
        var codes = effective.Select(l => l.AccountCode).Distinct().ToList();
        var accounts = await db.Set<Account>().Where(a => codes.Contains(a.Code)).ToDictionaryAsync(a => a.Code, ct);
        foreach (var code in codes)
        {
            if (!accounts.TryGetValue(code, out var account))
            {
                throw new NotFoundException($"La cuenta contable {code} no existe.");
            }
            Guard.That(account.IsPostable, "account.not_postable", $"La cuenta {code} · {account.Name} es un grupo: use una de sus subcuentas.");
        }
        var year = (short)date.Year;
        var month = (short)date.Month;
        var period = db.Set<FiscalPeriod>().Local.FirstOrDefault(p => p.Year == year && p.Month == month)
                     ?? await db.Set<FiscalPeriod>().FirstOrDefaultAsync(p => p.Year == year && p.Month == month, ct);
        if (period is null)
        {
            period = new FiscalPeriod(tenantId, year, month);
            db.Set<FiscalPeriod>().Add(period);
        }
        Guard.That(period.Status == FiscalPeriodStatus.Open, "period.closed", $"El período {month:00}/{year} está cerrado.");
        var config = await db.Set<TenantConfig>().FirstAsync(ct);
        var number = await Documents.NextForBranchAsync<JournalEntry>(db, e => e.Number, "AS", branchId, ct);
        var entry = new JournalEntry(tenantId, branchId, number, period.Id, date, description.Length > 250 ? description[..250] : description,
            config.DefaultCurrencyId, correlationId);
        foreach (var line in effective)
        {
            var accountId = accounts[line.AccountCode].Id;
            if (line.Debit > 0)
            {
                entry.Debit(accountId, line.Debit, memo: line.Memo);
            }
            if (line.Credit > 0)
            {
                entry.Credit(accountId, line.Credit, memo: line.Memo);
            }
        }
        entry.Post(userId, now);
        db.Set<JournalEntry>().Add(entry);
        return entry;
    }

    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Costo promedio ponderado por variante y almacén (AverageCostHistory es append-only: cada cambio es una fila). V4: las
/// filas llevan una secuencia por (variante, almacén) con índice único; el costo vigente es el de mayor secuencia. Si dos
/// recepciones concurrentes (una compra y una transferencia) calculan sobre el mismo promedio, la segunda choca con el
/// índice, se traduce a conflicto de concurrencia y el caso de uso reintenta con el promedio que dejó la primera.
/// </summary>
public static class AverageCosts
{
    public static async Task<decimal> CurrentAsync(IMinvDbContext db, Guid variantId, Guid warehouseId, CancellationToken ct) =>
        (await LatestAsync(db, variantId, warehouseId, ct))?.AverageCost ?? 0m;

    /// <summary>Registra un nuevo costo promedio con la secuencia siguiente.</summary>
    public static async Task<AverageCostHistory> RecordAsync(IMinvDbContext db, Guid tenantId, Guid branchId, Guid variantId, Guid warehouseId,
        DateTimeOffset effectiveAt, decimal averageCost, Guid? stockMovementId, CancellationToken ct)
    {
        var sequence = ((await LatestAsync(db, variantId, warehouseId, ct))?.Sequence ?? 0) + 1;
        var row = new AverageCostHistory(tenantId, branchId, variantId, warehouseId, sequence, effectiveAt, averageCost, stockMovementId);
        db.Set<AverageCostHistory>().Add(row);
        return row;
    }

    private static async Task<AverageCostHistory?> LatestAsync(IMinvDbContext db, Guid variantId, Guid warehouseId, CancellationToken ct)
    {
        var local = db.Set<AverageCostHistory>().Local.Where(x => x.VariantId == variantId && x.WarehouseId == warehouseId)
            .OrderByDescending(x => x.Sequence).FirstOrDefault();
        var stored = await db.Set<AverageCostHistory>().AsNoTracking().Where(x => x.VariantId == variantId && x.WarehouseId == warehouseId)
            .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
        return local is null ? stored : stored is null || local.Sequence >= stored.Sequence ? local : stored;
    }

    /// <summary>Nuevo promedio después de recibir <paramref name="quantity"/> a <paramref name="unitCost"/>.</summary>
    public static decimal Weighted(decimal onHandBefore, decimal averageBefore, decimal quantity, decimal unitCost)
    {
        var before = Math.Max(0, onHandBefore);
        var total = before + quantity;
        return total <= 0 ? unitCost : decimal.Round((before * averageBefore + quantity * unitCost) / total, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>Existencia total de una variante en un almacén (todas sus posiciones y lotes).</summary>
    public static async Task<decimal> OnHandAsync(IMinvDbContext db, Guid variantId, IQueryable<Guid> warehouseBins, CancellationToken ct) =>
        await (from l in db.Set<StockLevel>()
               join b in db.Set<Batch>() on l.BatchId equals b.Id
               where b.VariantId == variantId && warehouseBins.Contains(l.BinId)
               select (decimal?)l.QuantityOnHand).SumAsync(ct) ?? 0m;
}
