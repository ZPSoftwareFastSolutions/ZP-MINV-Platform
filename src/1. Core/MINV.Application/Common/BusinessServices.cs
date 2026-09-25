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
/// rechaza el duplicado y el caso de uso reintenta).</summary>
public static class Documents
{
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

/// <summary>Línea de un asiento: cuenta (código), debe, haber y detalle.</summary>
public sealed record JournalLineSpec(string AccountCode, decimal Debit, decimal Credit, string? Memo = null);

/// <summary>
/// Contabilidad automática: arma y contabiliza un asiento de partida doble (crea el período del mes si no existe y
/// rechaza los cerrados). Lo usan las ventas, las compras y los asientos manuales.
/// </summary>
public static class JournalPoster
{
    public static async Task<JournalEntry> PostAsync(IMinvDbContext db, Guid tenantId, Guid userId, DateOnly date, string description,
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
        var number = await Documents.NextNumberAsync(db.Set<JournalEntry>(), e => e.Number, "AS", ct);
        var entry = new JournalEntry(tenantId, number, period.Id, date, description.Length > 250 ? description[..250] : description,
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

/// <summary>Costo promedio ponderado por variante y almacén (AverageCostHistory es append-only: cada cambio es una fila).</summary>
public static class AverageCosts
{
    public static async Task<decimal> CurrentAsync(IMinvDbContext db, Guid variantId, Guid warehouseId, CancellationToken ct)
    {
        var local = db.Set<AverageCostHistory>().Local.Where(x => x.VariantId == variantId && x.WarehouseId == warehouseId)
            .OrderByDescending(x => x.EffectiveAt).FirstOrDefault();
        if (local is not null)
        {
            return local.AverageCost;
        }
        return await db.Set<AverageCostHistory>().Where(x => x.VariantId == variantId && x.WarehouseId == warehouseId)
            .OrderByDescending(x => x.EffectiveAt).Select(x => (decimal?)x.AverageCost).FirstOrDefaultAsync(ct) ?? 0m;
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
