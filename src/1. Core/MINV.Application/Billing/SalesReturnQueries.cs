using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Application.Billing;

// =====================================================================================================================
// V4.1 · Consultas de devoluciones de venta: historial con su nota crédito-débito y lo que todavía se puede devolver de
// una venta (vendido − devuelto por línea). La devolución misma (stock, reembolso, asiento y nota) es de la emisión.
// =====================================================================================================================

public sealed class GetSalesReturnsHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetSalesReturnsQuery, IReadOnlyList<SalesReturnRow>>
{
    public async Task<IReadOnlyList<SalesReturnRow>> Handle(GetSalesReturnsQuery r, CancellationToken ct)
    {
        Guard.That(r.To >= r.From, "range.invalid", "La fecha final no puede ser anterior a la inicial.");
        var zone = await new BillingLookups(db, null, clock).ZoneAsync(ct);
        // Límites del día en la zona de la empresa, pasados a UTC (timestamptz de PostgreSQL solo admite desfase 0)
        var start = SiatStatusBuilder.At(r.From.ToDateTime(TimeOnly.MinValue), zone).ToUniversalTime();
        var end = SiatStatusBuilder.At(r.To.AddDays(1).ToDateTime(TimeOnly.MinValue), zone).ToUniversalTime();
        var returns = await db.Set<SalesReturn>().AsNoTracking().Include(x => x.Lines).Where(x => x.ReturnedAt >= start && x.ReturnedAt < end)
            .OrderByDescending(x => x.ReturnedAt).ToListAsync(ct);
        if (returns.Count == 0)
        {
            return [];
        }
        var ids = returns.Select(x => x.Id).ToList();
        var invoiceIds = returns.Select(x => x.InvoiceId).Distinct().ToList();
        var customerIds = returns.Select(x => x.CustomerId).Distinct().ToList();
        var lineIds = returns.SelectMany(x => x.Lines).Select(l => l.SalesOrderLineId).Distinct().ToList();
        var invoices = await db.Set<Invoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
        var customers = await db.Set<Customer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var orderLines = await db.Set<SalesOrderLine>().AsNoTracking().Where(l => lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        // La nota vigente de cada devolución (la última emitida)
        var notes = (await db.Set<FiscalDocument>().AsNoTracking().Where(d => d.SalesReturnId != null && ids.Contains(d.SalesReturnId.Value)).ToListAsync(ct))
            .GroupBy(d => d.SalesReturnId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.CreatedAt).First());
        return returns.Select(x =>
        {
            var refund = x.Lines.Where(l => orderLines.ContainsKey(l.SalesOrderLineId))
                .Sum(l => SalesReturn.RefundOf(orderLines[l.SalesOrderLineId], l.Quantity));
            var note = notes.GetValueOrDefault(x.Id);
            return new SalesReturnRow(x.Number, invoices.GetValueOrDefault(x.InvoiceId, "?"), x.ReturnedAt, customers.GetValueOrDefault(x.CustomerId, "?"),
                x.Reason, refund, note is null ? null : $"Nota N° {note.Number}", note?.Status);
        }).ToList();
    }
}

public sealed class GetReturnableLinesHandler(IMinvDbContext db) : IRequestHandler<GetReturnableLinesQuery, IReadOnlyList<ReturnableLine>>
{
    public async Task<IReadOnlyList<ReturnableLine>> Handle(GetReturnableLinesQuery r, CancellationToken ct)
    {
        var number = (r.InvoiceNumber ?? string.Empty).Trim().ToUpperInvariant();
        var invoice = await db.Set<Invoice>().AsNoTracking().FirstOrDefaultAsync(i => i.Number == number, ct)
                      ?? throw new NotFoundException($"La venta {number} no existe (o es de una sucursal que no es suya).");
        Guard.That(invoice.Status != InvoiceStatus.Voided, "return.invoice_voided",
            $"La venta {number} está anulada (su mercadería ya volvió al stock): no admite devoluciones.");
        var lines = await (from l in db.Set<SalesOrderLine>().AsNoTracking()
                           join v in db.Set<ProductVariant>() on l.VariantId equals v.Id
                           join p in db.Set<Product>() on v.ProductId equals p.Id
                           join u in db.Set<UnitOfMeasure>() on l.UnitId equals u.Id
                           where l.SalesOrderId == invoice.SalesOrderId
                           orderby v.Sku
                           select new { l.Id, v.Sku, p.Name, Unit = u.Code, l.Quantity, l.UnitPrice, l.DiscountPercent }).ToListAsync(ct);
        var returned = await (from rl in db.Set<SalesReturnLine>()
                              join x in db.Set<SalesReturn>() on rl.SalesReturnId equals x.Id
                              where x.InvoiceId == invoice.Id
                              group rl.Quantity by rl.SalesOrderLineId into g
                              select new { g.Key, Total = g.Sum() }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        return lines.Select(l => new ReturnableLine(l.Sku, l.Name, l.Unit, l.Quantity, Quantities.Round6(returned.GetValueOrDefault(l.Id)), l.UnitPrice,
            l.DiscountPercent)).ToList();
    }
}
