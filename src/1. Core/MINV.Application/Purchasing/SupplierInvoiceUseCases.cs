using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;
using MINV.Domain.Warehousing;

namespace MINV.Application.Purchasing;

// =====================================================================================================================
// V4.1 · Facturas de proveedores (libro de compras, investigación 07 §5.3): se registran sobre una recepción ya
// contabilizada con los datos fiscales del documento del proveedor; el 13 % de la base se reclasifica del inventario al
// IVA crédito fiscal (Debe 1.1.04 / Haber 1.1.05). El costo promedio histórico no se recalcula (limitación documentada).
// =====================================================================================================================

public sealed class RegisterSupplierInvoiceValidator : AbstractValidator<RegisterSupplierInvoiceCommand>
{
    public RegisterSupplierInvoiceValidator()
    {
        RuleFor(x => x.ReceiptNumber).NotEmpty().WithMessage("Elija la recepción de mercadería que factura el proveedor.");
        RuleFor(x => x.InvoiceNumber).NotEmpty().WithMessage("Indique el número de la factura del proveedor.").MaximumLength(40);
        RuleFor(x => x.AuthorizationCode).NotEmpty().WithMessage("Indique el CUF (o el código de autorización) de la factura del proveedor.")
            .MaximumLength(100);
        RuleFor(x => x.TotalAmount).GreaterThan(0).WithMessage("El importe total de la compra debe ser mayor que 0.");
        RuleFor(x => x.Discounts).GreaterThanOrEqualTo(0).WithMessage("Los descuentos no pueden ser negativos.");
        RuleFor(x => x.NotSubjectToVat).GreaterThanOrEqualTo(0).WithMessage("El importe no sujeto a crédito fiscal no puede ser negativo.");
        RuleFor(x => x.PurchaseType).InclusiveBetween(1, 5).WithMessage("El tipo de compra va de 1 a 5 (1 = mercado interno, actividades gravadas).");
        RuleFor(x => x.ControlCode).MaximumLength(17);
    }
}

public sealed class RegisterSupplierInvoiceHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<RegisterSupplierInvoiceCommand, string>
{
    public async Task<string> Handle(RegisterSupplierInvoiceCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para registrar facturas de proveedores.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RegisterAsync(r, userId, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();   // otra sesión numeró el asiento a la vez: se reintenta con el estado real
            }
        }
    }

    private async Task<string> RegisterAsync(RegisterSupplierInvoiceCommand r, Guid userId, CancellationToken ct)
    {
        var receiptNumber = r.ReceiptNumber.Trim().ToUpperInvariant();
        var receipt = await db.Set<GoodsReceipt>().Include(g => g.Lines).FirstOrDefaultAsync(g => g.Number == receiptNumber, ct)
                      ?? throw new NotFoundException($"La recepción {receiptNumber} no existe (o es de una sucursal que no es suya).");
        Guard.That(receipt.Status == GoodsReceiptStatus.Posted, "supplier_invoice.receipt_state",
            $"La recepción {receiptNumber} no está contabilizada: solo se factura mercadería recibida.");
        var existing = await SupplierInvoices.InvoiceOfReceiptAsync(db, receipt, ct);
        Guard.That(existing is null, "supplier_invoice.receipt_invoiced",
            $"La recepción {receiptNumber} ya tiene registrada la factura {existing} del proveedor: una recepción no puede tener dos facturas.");
        var supplierId = receipt.SupplierId
                         ?? await db.Set<PurchaseOrder>().Where(o => o.Id == receipt.PurchaseOrderId).Select(o => (Guid?)o.SupplierId).FirstOrDefaultAsync(ct)
                         ?? throw new DomainException("supplier_invoice.no_supplier", $"La recepción {receiptNumber} no tiene proveedor.");
        var supplier = await db.Set<Supplier>().FirstAsync(s => s.Id == supplierId, ct);
        var number = r.InvoiceNumber.Trim();
        Guard.That(!await db.Set<SupplierInvoice>().AnyAsync(i => i.SupplierId == supplier.Id && i.Number == number, ct), "supplier_invoice.duplicate",
            $"La factura {number} de {supplier.LegalName} ya está registrada.");
        var config = await db.Set<TenantConfig>().FirstAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        Guard.That(r.InvoiceDate <= today, "supplier_invoice.future", "La fecha de la factura del proveedor no puede ser posterior a hoy.");
        var now = clock.UtcNow;

        // La factura del proveedor copia las líneas de la recepción (cantidad y costo) y queda contabilizada. Arco exclusivo
        // ck_supplier_invoice_lines_origen: la línea de una recepción NO lleva descripción propia (el producto sale de la
        // recepción; guardarlo otra vez sería redundante y PostgreSQL lo rechaza)
        var invoice = new SupplierInvoice(receipt.TenantId, receipt.BranchId, supplier.Id, number, r.InvoiceDate, null, config.DefaultCurrencyId);
        var taxRateId = await (from x in db.Set<TaxRate>()
                               join t in db.Set<Tax>() on x.TaxId equals t.Id
                               where t.Code == "IVA" && x.ValidFrom <= r.InvoiceDate && (x.ValidTo == null || x.ValidTo >= r.InvoiceDate)
                               orderby x.ValidFrom descending
                               select (Guid?)x.Id).FirstOrDefaultAsync(ct);
        foreach (var line in receipt.Lines)
        {
            invoice.AddLine(line.Id, null, line.Quantity, line.UnitCost, taxRateId);
        }
        invoice.Post();
        var fiscal = new SupplierInvoiceFiscal(invoice.TenantId, invoice.BranchId, invoice.Id, r.AuthorizationCode.Trim(),
            string.IsNullOrWhiteSpace(r.ControlCode) ? null : r.ControlCode.Trim(), r.TotalAmount, r.Discounts, r.NotSubjectToVat, r.PurchaseType);
        db.Set<SupplierInvoice>().Add(invoice);
        db.Set<SupplierInvoiceFiscal>().Add(fiscal);

        // Crédito fiscal: Debe 1.1.04 IVA crédito fiscal / Haber 1.1.05 Inventario (el inventario entró al costo con IVA incluido).
        // En el mes de la factura; si ese período ya se cerró, en el de hoy.
        string? entryNumber = null;
        if (fiscal.TaxCredit > 0)
        {
            var closed = await db.Set<FiscalPeriod>().AnyAsync(p => p.Year == r.InvoiceDate.Year && p.Month == r.InvoiceDate.Month
                                                                    && p.Status == FiscalPeriodStatus.Closed, ct);
            var entry = await JournalPoster.PostAsync(db, invoice.TenantId, invoice.BranchId, userId, closed ? today : r.InvoiceDate,
                $"Crédito fiscal · factura {number} de {supplier.LegalName} · recepción {receipt.Number}",
                [new JournalLineSpec(AccountCodes.VatCredit, fiscal.TaxCredit, 0), new JournalLineSpec(AccountCodes.Inventory, 0, fiscal.TaxCredit)],
                now, invoice.Id, ct);
            entryNumber = entry.Number;
        }
        await db.SaveChangesAsync(ct);
        return $"✔ Factura {number} de {supplier.LegalName} registrada (recepción {receipt.Number}): base Bs " +
               $"{fiscal.TaxBase.ToString("0.00", CultureInfo.InvariantCulture)}, crédito fiscal Bs {fiscal.TaxCredit.ToString("0.00", CultureInfo.InvariantCulture)}" +
               (entryNumber is null ? "." : $" · asiento {entryNumber}.");
    }
}

internal static class SupplierInvoices
{
    /// <summary>Número de la factura (no anulada) que ya factura alguna línea de la recepción, o null.</summary>
    public static async Task<string?> InvoiceOfReceiptAsync(IMinvDbContext db, GoodsReceipt receipt, CancellationToken ct)
    {
        var lineIds = receipt.Lines.Select(l => l.Id).ToList();
        return await (from l in db.Set<SupplierInvoiceLine>()
                      join i in db.Set<SupplierInvoice>() on l.SupplierInvoiceId equals i.Id
                      where l.GoodsReceiptLineId != null && lineIds.Contains(l.GoodsReceiptLineId.Value) && i.Status != SupplierInvoiceStatus.Cancelled
                      select i.Number).FirstOrDefaultAsync(ct);
    }

    public static string StatusText(SupplierInvoiceStatus status) => status switch
    {
        SupplierInvoiceStatus.Posted => "Contabilizada",
        SupplierInvoiceStatus.Cancelled => "Anulada",
        _ => "Borrador",
    };
}

public sealed class GetSupplierInvoicesHandler(IMinvDbContext db) : IRequestHandler<GetSupplierInvoicesQuery, IReadOnlyList<SupplierInvoiceRow>>
{
    public async Task<IReadOnlyList<SupplierInvoiceRow>> Handle(GetSupplierInvoicesQuery r, CancellationToken ct)
    {
        Guard.That(r.To >= r.From, "range.invalid", "La fecha final no puede ser anterior a la inicial.");
        var from = r.From;
        var to = r.To;
        var invoices = await db.Set<SupplierInvoice>().AsNoTracking().Include(i => i.Lines)
            .Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to && i.Status != SupplierInvoiceStatus.Draft)
            .OrderByDescending(i => i.InvoiceDate).ThenBy(i => i.Number).ToListAsync(ct);
        var ids = invoices.Select(i => i.Id).ToList();
        var fiscal = await db.Set<SupplierInvoiceFiscal>().AsNoTracking().Where(f => ids.Contains(f.SupplierInvoiceId))
            .ToDictionaryAsync(f => f.SupplierInvoiceId, ct);
        var suppliers = await db.Set<Supplier>().AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var branches = await db.Set<Branch>().AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var receiptLineIds = invoices.SelectMany(i => i.Lines).Where(l => l.GoodsReceiptLineId != null).Select(l => l.GoodsReceiptLineId!.Value).Distinct().ToList();
        var receipts = await (from l in db.Set<GoodsReceiptLine>()
                              join g in db.Set<GoodsReceipt>() on l.GoodsReceiptId equals g.Id
                              where receiptLineIds.Contains(l.Id)
                              select new { l.Id, g.Number }).ToDictionaryAsync(x => x.Id, x => x.Number, ct);
        return invoices.Select(i =>
        {
            var s = suppliers[i.SupplierId];
            var f = fiscal.GetValueOrDefault(i.Id);
            var receipt = i.Lines.Where(l => l.GoodsReceiptLineId != null).Select(l => receipts.GetValueOrDefault(l.GoodsReceiptLineId!.Value))
                .FirstOrDefault(n => n is not null);
            var total = f?.TotalAmount ?? JournalPoster.Money(i.Lines.Sum(l => l.Quantity * l.UnitCost));
            return new SupplierInvoiceRow(i.Id, i.Number, s.Code, s.LegalName, s.TaxId, i.InvoiceDate, f?.AuthorizationCode ?? string.Empty, total,
                f?.TaxBase ?? 0m, f?.TaxCredit ?? 0m, SupplierInvoices.StatusText(i.Status), receipt, branches.GetValueOrDefault(i.BranchId, "?"));
        }).ToList();
    }
}

public sealed class GetReceiptsWithoutInvoiceHandler(IMinvDbContext db, IClock clock)
    : IRequestHandler<GetReceiptsWithoutInvoiceQuery, IReadOnlyList<PendingSupplierInvoiceRow>>
{
    public async Task<IReadOnlyList<PendingSupplierInvoiceRow>> Handle(GetReceiptsWithoutInvoiceQuery request, CancellationToken ct)
    {
        var receipts = await db.Set<GoodsReceipt>().AsNoTracking().Include(g => g.Lines).Where(g => g.Status == GoodsReceiptStatus.Posted)
            .OrderByDescending(g => g.ReceivedAt).ToListAsync(ct);
        var invoiced = (await (from l in db.Set<SupplierInvoiceLine>()
                               join i in db.Set<SupplierInvoice>() on l.SupplierInvoiceId equals i.Id
                               where l.GoodsReceiptLineId != null && i.Status != SupplierInvoiceStatus.Cancelled
                               select l.GoodsReceiptLineId!.Value).ToListAsync(ct)).ToHashSet();
        var orderIds = receipts.Where(g => g.SupplierId is null && g.PurchaseOrderId is not null).Select(g => g.PurchaseOrderId!.Value).Distinct().ToList();
        var orderSuppliers = await db.Set<PurchaseOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.SupplierId, ct);
        var suppliers = await db.Set<Supplier>().AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var zone = await new BillingLookups(db, null, clock).ZoneAsync(ct);
        return receipts.Where(g => !g.Lines.Any(l => invoiced.Contains(l.Id)))
            .Select(g =>
            {
                var supplierId = g.SupplierId ?? (g.PurchaseOrderId is { } o ? orderSuppliers.GetValueOrDefault(o) : Guid.Empty);
                var supplier = suppliers.GetValueOrDefault(supplierId);
                return new PendingSupplierInvoiceRow(g.Number, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(g.ReceivedAt, zone).DateTime),
                    supplier?.Code ?? "?", supplier?.LegalName ?? "?", g.Lines.Sum(l => JournalPoster.Money(l.Quantity * l.UnitCost)));
            }).ToList();
    }
}
