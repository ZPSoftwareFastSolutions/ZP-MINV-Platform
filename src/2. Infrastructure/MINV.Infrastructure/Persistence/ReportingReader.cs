using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Corporate;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence;

/// <summary>
/// V4 · Modelo de lectura: en PostgreSQL consulta las vistas <c>reporting.v_*</c> con <see cref="MinvReadDbContext"/>
/// (que puede apuntar a una réplica de lectura); en la demostración en memoria calcula lo mismo sobre el contexto de
/// escritura (no hay vistas materializadas).
/// </summary>
public sealed class ReportingReader(MinvWriteDbContext write, IServiceProvider services) : IReportingReader
{
    private MinvReadDbContext? Read => write.Database.IsRelational()
        ? (MinvReadDbContext?)services.GetService(typeof(MinvReadDbContext))
        : null;

    public async Task<IReadOnlyList<BranchSalesDay>> DailySalesAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (Read is { } read)
        {
            return await read.BranchDailySales.Where(x => x.Day >= from && x.Day <= to)
                .Select(x => new BranchSalesDay(x.BranchId, x.Day, x.Tickets, x.Revenue, x.Tax)).ToListAsync(ct);
        }
        var zone = TimeZoneInfo.FindSystemTimeZoneById((await write.TenantConfigs.FirstAsync(ct)).TimeZoneId);
        var invoices = await (from i in write.Invoices
                              where i.Status == InvoiceStatus.Issued && i.IssuedAt != null
                              select new
                              {
                                  i.BranchId,
                                  IssuedAt = i.IssuedAt!.Value,
                                  Amount = write.Payments.Where(p => p.InvoiceId == i.Id).Sum(p => p.Amount),
                                  Tax = write.InvoiceLines.Where(l => l.InvoiceId == i.Id).Sum(l => l.TaxAmount),
                              }).ToListAsync(ct);
        return invoices.Select(i => new { i.BranchId, Day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(i.IssuedAt, zone).DateTime), i.Amount, i.Tax })
            .Where(i => i.Day >= from && i.Day <= to)
            .GroupBy(i => new { i.BranchId, i.Day })
            .Select(g => new BranchSalesDay(g.Key.BranchId, g.Key.Day, g.Count(), g.Sum(x => x.Amount), g.Sum(x => x.Tax))).ToList();
    }

    public async Task<IReadOnlyList<BranchStockTotal>> StockAsync(CancellationToken ct = default)
    {
        if (Read is { } read)
        {
            return await read.BranchStock.GroupBy(x => x.BranchId)
                .Select(g => new BranchStockTotal(g.Key, g.Sum(x => x.OnHand), g.Sum(x => x.Value))).ToListAsync(ct);
        }
        var stock = await new ConsolidatedStockHandler(write).Handle(new ConsolidatedStockQuery(), ct);
        return stock.Branches.Select((b, i) => new BranchStockTotal(b.Id, stock.Rows.Sum(r => r.ByBranch[i]), stock.ValueByBranch[i])).ToList();
    }

    public async Task<DateTimeOffset?> RefreshedAtAsync(CancellationToken ct = default) =>
        Read is { } read ? await read.BranchStock.Select(x => (DateTimeOffset?)x.RefreshedAt).MaxAsync(ct) : null;
}
