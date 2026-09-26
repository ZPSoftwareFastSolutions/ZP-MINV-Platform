using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Billing;

// =====================================================================================================================
// V4.1 · Envío de los documentos fiscales al SIN (después del COMMIT de la venta, regla F-03) y trabajo automático.
// La lógica vive en SiatWorker (la comparten la caja, el botón «Procesar ahora» y el servicio en segundo plano).
// =====================================================================================================================

/// <summary>Envía al SIN el documento indicado (la caja, al terminar de cobrar) o los pendientes más antiguos.</summary>
public sealed class DispatchFiscalDocumentsHandler(ISiatWorker worker) : IRequestHandler<DispatchFiscalDocumentsCommand, DispatchResult>
{
    public Task<DispatchResult> Handle(DispatchFiscalDocumentsCommand request, CancellationToken ct) =>
        worker.DispatchAsync(request.DocumentId, request.Max, ct);
}

/// <summary>
/// Ejecuta ahora el trabajo automático de la empresa de la sesión: envía los pendientes y, con
/// <see cref="RunSiatWorkCommand.Maintain"/>, el mantenimiento (recuperación fuera de línea, paquetes, validaciones, notas
/// pendientes, correos) y un segundo envío de lo que el mantenimiento dejó pendiente (p. ej. notas crédito-débito).
/// </summary>
public sealed class RunSiatWorkHandler(ISiatWorker worker) : IRequestHandler<RunSiatWorkCommand, SiatWorkResult>
{
    public async Task<SiatWorkResult> Handle(RunSiatWorkCommand request, CancellationToken ct)
    {
        var first = await worker.DispatchAsync(null, 50, ct);
        if (!request.Maintain)
        {
            return new SiatWorkResult(first, null);
        }
        var maintenance = await worker.MaintainAsync(false, ct);
        var second = await worker.DispatchAsync(null, 50, ct);
        return new SiatWorkResult(Merge(first, second), maintenance);
    }

    /// <summary>Une dos envíos (la fila más reciente de cada documento gana).</summary>
    internal static DispatchResult Merge(DispatchResult a, DispatchResult b)
    {
        var rows = a.Documents.Where(r => b.Documents.All(x => x.Id != r.Id)).Concat(b.Documents).ToList();
        return new DispatchResult(a.Sent + b.Sent, a.Valid + b.Valid, a.Rejected + b.Rejected, a.WentOffline + b.WentOffline, rows,
            a.Messages.Concat(b.Messages).Distinct().ToList());
    }
}

/// <summary>
/// V4.1 · Filas de documentos fiscales para los resultados de los comandos de esta área (despacho, re-emisión,
/// transcripción): estado, totales derivados de las líneas, plazos y qué acciones admite cada documento hoy (hora fiscal).
/// </summary>
public static class IssuedDocumentRows
{
    public static async Task<IReadOnlyList<FiscalDocumentRow>> ForAsync(IMinvDbContext db, IClock clock, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }
        var wanted = ids.Distinct().ToList();
        var documents = await db.Set<FiscalDocument>().AsNoTracking().Include(d => d.Lines).Include(d => d.NoteReference)
            .Where(d => wanted.Contains(d.Id)).ToListAsync(ct);
        var branchIds = documents.Select(d => d.BranchId).Distinct().ToList();
        var branches = await db.Set<Branch>().Where(b => branchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var pointIds = documents.Select(d => d.PointOfSaleId).Distinct().ToList();
        var points = await db.Set<SiatPointOfSale>().Where(p => pointIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        var returnIds = documents.Where(d => d.SalesReturnId is not null).Select(d => d.SalesReturnId!.Value).ToList();
        var returns = await db.Set<SalesReturn>().Where(r => returnIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.InvoiceId, ct);
        var invoiceIds = documents.Where(d => d.InvoiceId is not null).Select(d => d.InvoiceId!.Value).Concat(returns.Values).Distinct().ToList();
        var invoices = await db.Set<Invoice>().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
        var fiscalNow = await FiscalNowAsync(db, clock, ct);
        var rows = new List<FiscalDocumentRow>();
        foreach (var id in wanted)
        {
            if (documents.FirstOrDefault(d => d.Id == id) is not { } d)
            {
                continue;
            }
            var saleInvoice = d.InvoiceId ?? (d.SalesReturnId is { } r && returns.TryGetValue(r, out var inv) ? inv : null);
            var voidDeadline = FiscalRules.VoidDeadline(d.IssuedAt);
            rows.Add(new FiscalDocumentRow(d.Id, d.Kind, d.Number, d.Cuf, d.IssuedAt, d.BranchId, branches.GetValueOrDefault(d.BranchId, string.Empty),
                points.GetValueOrDefault(d.PointOfSaleId), d.BuyerName ?? d.CustomerCode,
                d.BuyerComplement is null ? d.BuyerDocumentNumber : $"{d.BuyerDocumentNumber}-{d.BuyerComplement}", d.TotalAmount, d.Status, d.IsReverted,
                d.EmissionType, saleInvoice is { } sale ? invoices.GetValueOrDefault(sale) : null, d.LastSiatCode, d.CanVoidAt(fiscalNow),
                d.Status == FiscalDocumentStatus.Voided && !d.IsReverted && fiscalNow <= voidDeadline,
                d.Kind == FiscalDocumentKind.Invoice && d.Status == FiscalDocumentStatus.Valid && fiscalNow <= FiscalRules.CreditNoteDeadline(d.IssuedAt),
                voidDeadline));
        }
        return rows;
    }

    /// <summary>Hora fiscal de la empresa (o la de Bolivia si todavía no hay configuración).</summary>
    private static async Task<DateTime> FiscalNowAsync(IMinvDbContext db, IClock clock, CancellationToken ct)
    {
        var lookups = new BillingLookups(db, null, clock);
        var settings = await lookups.SettingsAsync(ct);
        return settings is null
            ? DateTime.SpecifyKind(clock.UtcNow.ToOffset(TimeSpan.FromHours(-4)).DateTime, DateTimeKind.Unspecified)
            : lookups.FiscalNow(settings, await lookups.ZoneAsync(ct));
    }
}
