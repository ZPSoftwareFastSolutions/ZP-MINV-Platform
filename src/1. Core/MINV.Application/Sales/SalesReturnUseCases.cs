using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Catalog;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Application.Tech;
using MINV.Domain.Accounting;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Events;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;

namespace MINV.Application.Sales;

// =====================================================================================================================
// V4.1 · Devoluciones de clientes (parciales o totales): stock de vuelta, reembolso, asiento contable y, si la venta
// tiene factura VÁLIDA en el SIN, nota crédito-débito (sector 24) en la misma transacción (el envío es después).
// =====================================================================================================================

public sealed class CreateSalesReturnValidator : AbstractValidator<CreateSalesReturnCommand>
{
    public CreateSalesReturnValidator()
    {
        RuleFor(x => x.InvoiceNumber).NotEmpty().WithMessage("Indique la venta (número de factura de M-INV).");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Indique el motivo de la devolución.").MaximumLength(200);
        RuleFor(x => x.RefundPaymentMethodCode).NotEmpty().WithMessage("Elija el medio con el que se reembolsa.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Indique qué productos se devuelven.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Sku).NotEmpty().WithMessage("Cada línea necesita el SKU.");
            l.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Cada cantidad devuelta debe ser mayor que 0.");
        });
    }
}

/// <summary>
/// Devolución de una venta de M-INV: por SKU, hasta lo vendido menos lo ya devuelto; el stock vuelve con DEVOLUCIÓN DE
/// CLIENTE a la misma posición de la salida original; reembolso = Σ importe de las líneas devueltas (precio y descuento de
/// la venta); asiento (Debe 4.1.01 Ventas neto + 2.1.02 IVA débito / Haber Caja o Banco; Debe 1.1.05 Inventario / Haber
/// 5.1.01 Costo de ventas al costo promedio); salida de caja si el medio abre la caja y el usuario tiene turno; número
/// «DV-&lt;sucursal&gt;-000001»; evento <c>sale.returned</c>. Nota crédito-débito si la factura del SIN es válida y el punto
/// está en línea (si no, la emite después el trabajo automático). Plazo de la nota: 18 meses.
/// </summary>
public sealed class CreateSalesReturnHandler(IMinvDbContext db, ICurrentUser user, IClock clock, IFiscalDocumentSerializer? serializer = null)
    : IRequestHandler<CreateSalesReturnCommand, SalesReturnResult>
{
    public async Task<SalesReturnResult> Handle(CreateSalesReturnCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para registrar la devolución.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await ReturnAsync(request, userId, ct);
                await db.SaveChangesAsync(ct);
                return result;
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    private async Task<SalesReturnResult> ReturnAsync(CreateSalesReturnCommand request, Guid userId, CancellationToken ct)
    {
        var number = request.InvoiceNumber.Trim().ToUpperInvariant();
        var invoice = await db.Set<Invoice>().FirstOrDefaultAsync(i => i.Number == number, ct) ?? throw new NotFoundException($"La venta {number} no existe.");
        Guard.That(invoice.Status == InvoiceStatus.Issued, "return.sale_voided", $"La venta {number} está anulada: no admite devoluciones.");
        var order = await db.Set<SalesOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == invoice.SalesOrderId, ct);
        var methodCode = request.RefundPaymentMethodCode.Trim().ToUpperInvariant();
        var method = await db.Set<PaymentMethod>().FirstOrDefaultAsync(m => m.Code == methodCode && m.IsActive, ct)
                     ?? throw new NotFoundException($"El medio de pago {methodCode} no existe.");

        // Documento fiscal vigente de la venta: una factura anulada no admite nota; la nota vence a los 18 meses
        var fiscal = await db.Set<FiscalDocument>().Include(d => d.Lines)
            .Where(d => d.InvoiceId == invoice.Id && d.Kind == FiscalDocumentKind.Invoice
                                                  && (d.Status == FiscalDocumentStatus.Pending || d.Status == FiscalDocumentStatus.Valid
                                                      || d.Status == FiscalDocumentStatus.Offline || d.Status == FiscalDocumentStatus.InPackage
                                                      || d.Status == FiscalDocumentStatus.Voided))
            .OrderByDescending(d => d.CreatedAt).FirstOrDefaultAsync(ct);
        if (fiscal is not null)
        {
            Guard.That(fiscal.Status != FiscalDocumentStatus.Voided, "return.invoice_voided",
                $"La factura N° {fiscal.Number} del SIN de esta venta está anulada: no admite devoluciones con nota crédito-débito. Emita primero un documento nuevo o anule la venta.");
            var billing = new BillingLookups(db, null, clock);
            if (await billing.SettingsAsync(ct) is { } settings)
            {
                var fiscalNow = billing.FiscalNow(settings, await billing.ZoneAsync(ct));
                Guard.That(fiscalNow <= FiscalRules.CreditNoteDeadline(fiscal.IssuedAt), "return.note_deadline",
                    $"Pasaron más de 18 meses desde la factura N° {fiscal.Number}: ya no se puede emitir la nota crédito-débito de la devolución.");
            }
        }

        var lookups = new InventoryLookups(db);
        var config = await lookups.ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var now = clock.UtcNow;
        var warehouseId = await SaleReverser.WarehouseOfAsync(db, order, ct);
        var lineIds = order.Lines.Select(l => l.Id).ToList();
        var returned = (await db.Set<SalesReturnLine>().Where(l => lineIds.Contains(l.SalesOrderLineId))
                .Select(l => new { l.SalesOrderLineId, l.Quantity }).ToListAsync(ct))
            .GroupBy(l => l.SalesOrderLineId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        PosSession? session = null;
        if (method.OpensCashDrawer)
        {
            session = await db.Set<PosSession>().Where(s => s.Status == PosSessionStatus.Open && s.OpenedByUserId == userId && s.BranchId == invoice.BranchId)
                .OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct);
        }
        var returnNumber = await Documents.NextForBranchAsync<SalesReturn>(db, r => r.Number, "DV", invoice.BranchId, ct);
        var salesReturn = new SalesReturn(invoice.TenantId, invoice.BranchId, returnNumber, invoice.Id, order.CustomerId, request.Reason.Trim(), method.Id,
            session?.Id, userId, now);
        var returnType = await lookups.MovementTypeAsync(MovementTypeCodes.SaleReturn, ct);
        var refund = 0m;
        var cost = 0m;
        var eventLines = new List<SaleEventLine>();
        var ledger = new SerialLedger(db);
        var detail = $"Devolución {returnNumber} de la venta {invoice.Number}{(request.Defective ? " (por falla)" : string.Empty)}: {request.Reason.Trim()}";
        detail = detail.Length > 250 ? detail[..250] : detail;
        var serialContext = new SerialContext(invoice.BranchId, userId, now, returnNumber, detail);
        foreach (var input in request.Lines.GroupBy(l => l.Sku.Trim().ToUpperInvariant())
                     .Select(g => (Sku: g.Key, Quantity: g.Sum(l => l.Quantity), Serials: g.Any(l => l.Serials is not null)
                         ? g.SelectMany(l => l.Serials ?? []).ToList()
                         : null)))
        {
            var item = await lookups.VariantBySkuAsync(input.Sku, ct);
            Quantities.EnsureAllowed(input.Quantity, item.Unit.AllowsDecimals, item.Unit.UnitCode);
            var sold = order.Lines.Where(l => l.VariantId == item.Variant.Id).ToList();
            Guard.That(sold.Count > 0, "return.not_sold", $"{item.Variant.Sku} no está en la venta {invoice.Number}.");
            var available = sold.Sum(l => l.Quantity - returned.GetValueOrDefault(l.Id));
            Guard.That(input.Quantity <= available, "return.exceeds",
                $"No se puede devolver más de lo vendido: {item.Variant.Sku} vendido {Quantities.Format(sold.Sum(l => l.Quantity))}, " +
                $"ya devuelto {Quantities.Format(sold.Sum(l => returned.GetValueOrDefault(l.Id)))}.");

            // V4.2 · Un producto serializado se devuelve por sus series: cada una es de ESTA venta y sigue vendida; la línea
            // de la devolución es la de la venta de cada serie
            var serials = await ledger.ExpectAsync(item, input.Quantity, input.Serials, ct);
            var takes = new List<(SalesOrderLine OrderLine, decimal Quantity, IReadOnlyList<SerialNumber> Units)>();
            if (serials.Count > 0)
            {
                var soldLineIds = sold.Select(l => l.Id).ToList();
                var ofSale = (await (from x in db.Set<SalesOrderLineSerial>()
                                     join s in db.Set<SerialNumber>() on x.SerialNumberId equals s.Id
                                     where soldLineIds.Contains(x.SalesOrderLineId) && serials.Contains(s.Serial)
                                     select new { x.SalesOrderLineId, Unit = s }).ToListAsync(ct))
                    .ToDictionary(x => x.Unit.Serial, StringComparer.Ordinal);
                foreach (var serial in serials)
                {
                    Guard.That(ofSale.ContainsKey(serial), SerialErrorCodes.NotInDocument, $"La serie {serial} de {item.Variant.Sku} no se vendió en {invoice.Number}.");
                    var unit = ofSale[serial].Unit;
                    Guard.That(unit.Status == SerialNumberStatus.Sold, SerialErrorCodes.NotAvailable,
                        $"La serie {serial} de {item.Variant.Sku} no se puede devolver: está {SerialNumber.Describe(unit.Status)}.");
                }
                foreach (var group in serials.GroupBy(s => ofSale[s].SalesOrderLineId))
                {
                    takes.Add((sold.First(l => l.Id == group.Key), group.Count(), group.Select(s => ofSale[s].Unit).ToList()));
                }
            }
            else
            {
                var pending = input.Quantity;
                foreach (var orderLine in sold)
                {
                    var take = Math.Min(pending, orderLine.Quantity - returned.GetValueOrDefault(orderLine.Id));
                    if (take > 0)
                    {
                        takes.Add((orderLine, take, []));
                        pending -= take;
                    }
                    if (pending <= 0)
                    {
                        break;
                    }
                }
            }

            foreach (var (orderLine, take, units) in takes)
            {
                var line = salesReturn.AddLine(orderLine, take, returned.GetValueOrDefault(orderLine.Id));
                refund += SalesReturn.RefundOf(orderLine, line.Quantity);
                eventLines.Add(new SaleEventLine(item.Variant.Sku, line.Quantity, orderLine.UnitPrice, orderLine.DiscountPercent));
                foreach (var unit in units)
                {
                    db.Set<SalesReturnLineSerial>().Add(new SalesReturnLineSerial(invoice.TenantId, invoice.BranchId, line.Id, unit.Id));
                }
                if (request.Defective)
                {
                    // Por falla: sin entrada de stock; la unidad queda devuelta y en garantía (no es stock vendible)
                    foreach (var unit in units)
                    {
                        unit.Return(null, null, serialContext);
                        unit.SendToRma(serialContext);
                    }
                    continue;
                }
                // A la misma posición y lote de la salida original (como la anulación de la venta)
                var original = await db.Set<StockMovement>().FirstAsync(m => m.Id == orderLine.StockMovementId, ct);
                var level = await db.Set<StockLevel>().FirstAsync(l => l.Id == original.StockLevelId, ct);
                var context = new MovementContext(userId, today, now, returnNumber, detail);
                var movement = level.Register(returnType, line.Quantity, item.Unit, context);
                db.Set<StockMovement>().Add(movement);
                line.LinkMovement(movement.Id);
                cost += line.Quantity * await AverageCosts.CurrentAsync(db, item.Variant.Id, warehouseId, ct);
                if (units.Count > 0)
                {
                    var batch = await ledger.BatchAsync(level.BatchId, ct);
                    foreach (var unit in units)
                    {
                        unit.Return(level, batch, serialContext);
                    }
                }
            }
        }
        refund = JournalPoster.Money(refund);
        var tax = Pricing.IncludedTax(refund, await Pricing.TaxRateAsync(db, today, ct), await Pricing.VatOnInvoicedAmountAsync(db, ct));
        db.Set<SalesReturn>().Add(salesReturn);
        await JournalPoster.PostAsync(db, invoice.TenantId, invoice.BranchId, userId, today,
            $"Devolución {returnNumber} de la venta {invoice.Number}: {request.Reason.Trim()}",
        [
            new JournalLineSpec(AccountCodes.Sales, refund - tax, 0),
            new JournalLineSpec(AccountCodes.VatDebit, tax, 0),
            new JournalLineSpec(method.OpensCashDrawer ? AccountCodes.Cash : AccountCodes.Bank, 0, refund),
            new JournalLineSpec(AccountCodes.Inventory, JournalPoster.Money(cost), 0),
            new JournalLineSpec(AccountCodes.CostOfSales, 0, JournalPoster.Money(cost)),
        ], now, salesReturn.Id, ct);
        if (session is not null && refund > 0)
        {
            db.Set<CashMovement>().Add(session.RegisterCash(CashDirection.Out, refund, $"Devolución {returnNumber} de la venta {invoice.Number}", userId, now));
        }
        db.Publish(new SaleReturnedEvent(returnNumber, invoice.Number, invoice.BranchId, refund, eventLines, now));

        // Nota crédito-débito (sector 24): solo en línea y sobre una factura válida; si no, queda para el trabajo automático
        var message = $"✔ Devolución {returnNumber} registrada: reembolso Bs {refund:N2} ({method.Name}); " +
                      (request.Defective ? "por falla: la mercadería quedó en garantía (no vuelve al stock vendible)." : "el stock volvió al almacén.");
        FiscalDocument? note = null;
        if (fiscal is not null)
        {
            var xml = serializer ?? throw new DomainException("fiscal.no_serializer",
                "Este equipo no tiene el generador del XML del SIN: no puede emitir la nota crédito-débito.");
            var issue = await FiscalIssuer.IssueCreditNoteAsync(new FiscalIssueServices(db, clock, xml, userId, BillingLookups.UserCode(user)), salesReturn,
                fiscal, order.Lines.ToList(), ct);
            note = issue.Note;
            message += " " + issue.Message;
        }
        return new SalesReturnResult(returnNumber, refund, note?.Id, note?.Number, note?.Status, message);
    }
}
