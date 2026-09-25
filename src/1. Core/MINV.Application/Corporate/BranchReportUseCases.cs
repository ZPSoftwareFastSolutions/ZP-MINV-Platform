using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Warehousing;

namespace MINV.Application.Corporate;

public sealed record BranchReportRow(string Code, string Name, int Tickets, decimal Revenue, decimal Tax, decimal AverageTicket, decimal StockValue,
    decimal SharePercent);

public sealed record BranchReportDay(DateOnly Day, IReadOnlyList<decimal> RevenueByBranch);

public sealed record BranchReport(DateOnly From, DateOnly To, IReadOnlyList<BranchReportRow> Branches, IReadOnlyList<BranchReportDay> Days,
    decimal TotalRevenue, decimal TotalStockValue, decimal InTransitValue, DateTimeOffset? RefreshedAt);

/// <summary>
/// V4 · Tablero gerencial por sucursal (Auditoría global): ventas, ticket promedio, participación y valor del stock de
/// cada sucursal visible, más lo que está en tránsito. Sale del MODELO DE LECTURA (vistas materializadas): no carga la
/// base transaccional de las cajas.
/// </summary>
[RequiresModule(LicenseModuleCodes.GlobalAudit)]
[RequiresPermission(PermissionCodes.ReportsView)]
public sealed record GetBranchReportQuery(DateOnly From, DateOnly To) : IRequest<BranchReport>;

public sealed class GetBranchReportHandler(IMinvDbContext db, IReportingReader reader) : IRequestHandler<GetBranchReportQuery, BranchReport>
{
    public async Task<BranchReport> Handle(GetBranchReportQuery request, CancellationToken ct)
    {
        var scope = db.Branches;
        var branches = (await db.Set<Branch>().OrderBy(b => b.Code).Select(b => new { b.Id, b.Code, b.Name }).ToListAsync(ct))
            .Where(b => scope.Allows(b.Id)).ToList();
        var sales = await reader.DailySalesAsync(request.From, request.To, ct);
        var stock = await reader.StockAsync(ct);
        var inTransit = await (from l in db.Set<StockTransferLine>()
                               join t in db.Set<StockTransfer>() on l.StockTransferId equals t.Id
                               where t.Status == TransferStatus.Dispatched
                               select l.Quantity * (l.UnitCost ?? 0)).SumAsync(ct);
        var total = sales.Sum(s => s.Revenue);
        var rows = branches.Select(b =>
        {
            var mine = sales.Where(s => s.BranchId == b.Id).ToList();
            var revenue = mine.Sum(s => s.Revenue);
            var tickets = mine.Sum(s => s.Tickets);
            return new BranchReportRow(b.Code, b.Name, tickets, JournalPoster.Money(revenue), JournalPoster.Money(mine.Sum(s => s.Tax)),
                tickets == 0 ? 0 : JournalPoster.Money(revenue / tickets), JournalPoster.Money(stock.Where(s => s.BranchId == b.Id).Sum(s => s.Value)),
                total == 0 ? 0 : decimal.Round(revenue * 100 / total, 1));
        }).ToList();
        var days = new List<BranchReportDay>();
        for (var day = request.From; day <= request.To; day = day.AddDays(1))
        {
            days.Add(new BranchReportDay(day, branches.Select(b => sales.Where(s => s.BranchId == b.Id && s.Day == day).Sum(s => s.Revenue)).ToList()));
        }
        return new BranchReport(request.From, request.To, rows, days, JournalPoster.Money(total), rows.Sum(r => r.StockValue), JournalPoster.Money(inTransit),
            await reader.RefreshedAtAsync(ct));
    }
}
