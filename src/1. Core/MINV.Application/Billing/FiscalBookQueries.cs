using System.Globalization;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Purchasing;
using MINV.Domain.Warehousing;

namespace MINV.Application.Billing;

// =====================================================================================================================
// V4.1 · Libros del Registro de Compras y Ventas (RCV, investigación 07 §5) y resumen IVA/IT del mes.
//   • Libro de VENTAS estándar (24 columnas, ESPECIFICACIÓN 2): las facturas del mes por fecha fiscal, estado V (válida),
//     A (anulada: importes en cero) o C (transcrita de contingencia manual, CAFC); código de control «0» (en línea no hay).
//     Las facturas en línea ya las registra el SIN: el libro es interno, para conciliar y para el contador.
//   • Las NOTAS CRÉDITO-DÉBITO no van en el libro de ventas: su monto efectivo (13 % de lo devuelto) es un crédito fiscal
//     del emisor que se refleja en el resumen del mes (07 §5.7 y §7.24: «débito fiscal de ventas, menos el efectivo de las
//     notas emitidas»).
//   • Libro de COMPRAS estándar (23 columnas, ESPECIFICACIÓN 1): facturas de proveedores contabilizadas del mes.
//   • Exportación con el orden y los títulos EXACTOS de las plantillas del SIN (Pventasestandar / PComprasEstandar):
//     CSV (separador coma, punto decimal, sin separador de miles, fechas dd/mm/aaaa, UTF-8) o Excel (.xlsx).
// =====================================================================================================================

/// <summary>V4.1 · Cálculo de los libros (lo comparten la consulta, la exportación y el resumen).</summary>
internal static class FiscalBooks
{
    /// <summary>Encabezados de la plantilla «Registro de Ventas Estándar» del SIN (24 columnas, fila 1).</summary>
    public static readonly IReadOnlyList<string> SalesHeaders =
    [
        "Nº", "ESPECIFICACION", "FECHA DE LA FACTURA", "N° DE LA FACTURA", "CODIGO DE AUTORIZACION", "NIT / CI CLIENTE", "COMPLEMENTO",
        "NOMBRE O RAZON SOCIAL", "IMPORTE TOTAL DE LA VENTA", "IMPORTE ICE", "IMPORTE IEHD", "IMPORTE IPJ", "TASAS", "OTROS NO SUJETOS AL IVA",
        "EXPORTACIONES Y OPERACIONES EXENTAS", "VENTAS GRAVADAS A TASA CERO", "SUBTOTAL", "DESCUENTOS, BONIFICACIONES Y REBAJAS SUJETAS AL IVA",
        "IMPORTE GIFT CARD", "IMPORTE BASE PARA DEBITO FISCAL", "DEBITO FISCAL", "ESTADO", "CODIGO DE CONTROL", "TIPO DE VENTA",
    ];

    /// <summary>Encabezados de la plantilla «Registro de Compras Estándar» del SIN (23 columnas, fila 1).</summary>
    public static readonly IReadOnlyList<string> PurchasesHeaders =
    [
        "Nº", "ESPECIFICACION", "NIT PROVEEDOR", "RAZON SOCIAL PROVEEDOR", "CODIGO DE AUTORIZACION", "NUMERO FACTURA", "NUMERO DUI/DIM",
        "FECHA DE FACTURA/DUI/DIM", "IMPORTE TOTAL COMPRA", "IMPORTE ICE", "IMPORTE IEHD", "IMPORTE IPJ", "TASAS", "OTRO NO SUJETO A CREDITO FISCAL",
        "IMPORTES EXENTOS", "IMPORTE COMPRAS GRAVADAS A TASA CERO", "SUBTOTAL", "DESCUENTOS/BONIFICACIONES /REBAJAS SUJETAS AL IVA", "IMPORTE GIFT CARD",
        "IMPORTE BASE CF", "CREDITO FISCAL", "TIPO COMPRA", "CODIGO DE CONTROL",
    ];

    /// <summary>ESPECIFICACIÓN de las plantillas (columna 2, solo al importar): 1 compras estándar, 2 ventas estándar.</summary>
    public const int PurchasesSpecification = 1;
    public const int SalesSpecification = 2;

    /// <summary>Primer instante del mes y del siguiente (hora fiscal, sin zona).</summary>
    public static (DateTime Start, DateTime End) Month(int year, int month)
    {
        Guard.That(year is >= 2000 and <= 2100 && month is >= 1 and <= 12, "book.period", "El período es un mes válido (año de 4 dígitos y mes de 1 a 12).");
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return (start, start.AddMonths(1));
    }

    public static async Task<SalesBookView> SalesAsync(IMinvDbContext db, int year, int month, CancellationToken ct)
    {
        var (start, end) = Month(year, month);
        var invoices = await db.Set<FiscalDocument>().AsNoTracking().Include(d => d.Lines)
            .Where(d => d.Kind == FiscalDocumentKind.Invoice && d.IssuedAt >= start && d.IssuedAt < end
                        && (d.Status == FiscalDocumentStatus.Valid || d.Status == FiscalDocumentStatus.Pending || d.Status == FiscalDocumentStatus.Offline
                            || d.Status == FiscalDocumentStatus.InPackage || d.Status == FiscalDocumentStatus.Voided))
            .OrderBy(d => d.IssuedAt).ThenBy(d => d.Number).ToListAsync(ct);
        var branches = await db.Set<Branch>().AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var rows = invoices.Select((d, i) =>
        {
            var voided = d.Status == FiscalDocumentStatus.Voided;
            // Col 9 = lo que figura en la factura (Σ subTotal, sin deducir descuentos ni gift card); col 18 = descuento adicional
            var total = voided ? 0m : d.LinesSubtotal;
            var discounts = voided ? 0m : d.AdditionalDiscount;
            var giftCard = voided ? 0m : d.GiftCardAmount;
            var taxBase = total - discounts - giftCard;
            return new SalesBookRow(i + 1, d.IssuedAt, d.Number, d.Cuf, d.BuyerDocumentNumber, d.BuyerComplement,
                string.IsNullOrWhiteSpace(d.BuyerName) ? "S/N" : d.BuyerName, total, 0m, 0m, 0m, 0m, 0m, 0m, 0m, total, discounts, giftCard, taxBase,
                FiscalRules.Vat(taxBase), voided ? "A" : d.Cafc is not null ? "C" : "V", "0", d.DocumentSector, branches.GetValueOrDefault(d.BranchId, "?"));
        }).ToList();
        var counted = rows.Where(r => r.Status != "A").ToList();
        return new SalesBookView(year, month, rows, counted.Sum(r => r.Total), counted.Sum(r => r.TaxBase), counted.Sum(r => r.TaxDebit), counted.Count,
            rows.Count - counted.Count);
    }

    public static async Task<PurchasesBookView> PurchasesAsync(IMinvDbContext db, int year, int month, CancellationToken ct)
    {
        _ = Month(year, month);
        var first = new DateOnly(year, month, 1);
        var next = first.AddMonths(1);
        // Solo las contabilizadas: una factura de proveedor anulada no da crédito fiscal (sale del libro)
        var data = await (from i in db.Set<SupplierInvoice>().AsNoTracking()
                          join f in db.Set<SupplierInvoiceFiscal>().AsNoTracking() on i.Id equals f.SupplierInvoiceId
                          join s in db.Set<Supplier>().AsNoTracking() on i.SupplierId equals s.Id
                          where i.Status == SupplierInvoiceStatus.Posted && i.InvoiceDate >= first && i.InvoiceDate < next
                          orderby i.InvoiceDate, s.LegalName, i.Number
                          select new { Invoice = i, Fiscal = f, s.TaxId, s.LegalName }).ToListAsync(ct);
        var branches = await db.Set<Branch>().AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var rows = data.Select((x, i) =>
        {
            var f = x.Fiscal;
            var subtotal = f.TotalAmount - f.NotSubjectToVat;
            return new PurchasesBookRow(i + 1, string.IsNullOrWhiteSpace(x.TaxId) ? "0" : x.TaxId.Trim(), x.LegalName, f.AuthorizationCode, x.Invoice.Number,
                x.Invoice.InvoiceDate, f.TotalAmount, 0m, 0m, 0m, 0m, f.NotSubjectToVat, 0m, 0m, subtotal, f.Discounts, 0m, f.TaxBase, f.TaxCredit,
                f.PurchaseType.ToString(CultureInfo.InvariantCulture), string.IsNullOrWhiteSpace(f.ControlCode) ? "0" : f.ControlCode,
                branches.GetValueOrDefault(x.Invoice.BranchId, "?"));
        }).ToList();
        return new PurchasesBookView(year, month, rows, rows.Sum(r => r.Total), rows.Sum(r => r.TaxBase), rows.Sum(r => r.TaxCredit));
    }

    public static string Date(DateTime value) => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateOnly value) => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Filas de la plantilla de ventas (con ESPECIFICACIÓN = 2 en la segunda columna y TIPO DE VENTA 0).</summary>
    public static IEnumerable<IReadOnlyList<object?>> SalesCells(SalesBookView book) =>
        book.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Row, SalesSpecification, Date(r.Date), r.Number, r.Cuf, r.BuyerDocument, r.Complement ?? string.Empty, r.BuyerName, r.Total, r.Ice, r.Iehd,
            r.Ipj, r.Fees, r.OtherNotSubject, r.Exports, r.ZeroRate, r.Subtotal, r.Discounts, r.GiftCard, r.TaxBase, r.TaxDebit, r.Status, r.ControlCode, 0,
        });

    /// <summary>Filas de la plantilla de compras (ESPECIFICACIÓN = 1; N° DUI/DIM = 0 porque son facturas).</summary>
    public static IEnumerable<IReadOnlyList<object?>> PurchasesCells(PurchasesBookView book) =>
        book.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Row, PurchasesSpecification, r.SupplierNit, r.SupplierName, r.AuthorizationCode, r.InvoiceNumber, "0", Date(r.Date), r.Total, r.Ice, r.Iehd,
            r.Ipj, r.Fees, r.OtherNotSubject, r.Exempt, r.ZeroRate, r.Subtotal, r.Discounts, r.GiftCard, r.TaxBase, r.TaxCredit,
            int.TryParse(r.PurchaseType, NumberStyles.None, CultureInfo.InvariantCulture, out var type) ? (object)type : r.PurchaseType, r.ControlCode,
        });

    /// <summary>CSV (RFC 4180): coma entre campos, comillas cuando hace falta, punto decimal, sin separador de miles, UTF-8 con
    /// BOM (Excel lo abre con los acentos correctos).</summary>
    public static byte[] Csv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", headers.Select(Field))).Append("\r\n");
        foreach (var row in rows)
        {
            sb.Append(string.Join(",", row.Select(cell => Field(cell switch
            {
                null => string.Empty,
                decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                var other => other.ToString() ?? string.Empty,
            })))).Append("\r\n");
        }
        return [.. new UTF8Encoding(true).GetPreamble(), .. new UTF8Encoding(false).GetBytes(sb.ToString())];
    }

    private static string Field(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}

public sealed class GetSalesBookHandler(IMinvDbContext db) : IRequestHandler<GetSalesBookQuery, SalesBookView>
{
    public Task<SalesBookView> Handle(GetSalesBookQuery r, CancellationToken ct) => FiscalBooks.SalesAsync(db, r.Year, r.Month, ct);
}

public sealed class GetPurchasesBookHandler(IMinvDbContext db) : IRequestHandler<GetPurchasesBookQuery, PurchasesBookView>
{
    public Task<PurchasesBookView> Handle(GetPurchasesBookQuery r, CancellationToken ct) => FiscalBooks.PurchasesAsync(db, r.Year, r.Month, ct);
}

public sealed class ExportFiscalBookHandler(IMinvDbContext db) : IRequestHandler<ExportFiscalBookQuery, FiscalFile>
{
    public const string CsvContentType = "text/csv; charset=utf-8";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<FiscalFile> Handle(ExportFiscalBookQuery r, CancellationToken ct)
    {
        var format = (r.Format ?? "csv").Trim().TrimStart('.').ToLowerInvariant();
        Guard.That(format is "csv" or "xlsx", "book.format", "El libro se exporta en CSV o en Excel (xlsx).");
        IReadOnlyList<string> headers;
        IReadOnlyList<IReadOnlyList<object?>> rows;
        if (r.Purchases)
        {
            headers = FiscalBooks.PurchasesHeaders;
            rows = FiscalBooks.PurchasesCells(await FiscalBooks.PurchasesAsync(db, r.Year, r.Month, ct)).ToList();
        }
        else
        {
            headers = FiscalBooks.SalesHeaders;
            rows = FiscalBooks.SalesCells(await FiscalBooks.SalesAsync(db, r.Year, r.Month, ct)).ToList();
        }
        var name = $"{(r.Purchases ? "LibroCompras" : "LibroVentas")}-{r.Year:0000}{r.Month:00}.{format}";
        return format == "csv"
            ? new FiscalFile(name, CsvContentType, FiscalBooks.Csv(headers, rows))
            : new FiscalFile(name, XlsxContentType, SimpleXlsxWriter.Write("Hoja1", headers, rows));
    }
}

/// <summary>
/// V4.1 · Resumen IVA/IT del mes para el contador (el traslado a los Form. 200 y 400 es suyo: el mapeo a casillas no está
/// documentado por el SIN). IVA: débito fiscal (13 % de la base de las facturas válidas) menos el crédito de las notas
/// crédito-débito emitidas y el crédito fiscal de las compras → IVA a pagar o saldo a favor. IT: 3 % de las ventas netas
/// de devoluciones.
/// </summary>
public sealed class GetTaxSummaryHandler(IMinvDbContext db) : IRequestHandler<GetTaxSummaryQuery, TaxSummaryView>
{
    public async Task<TaxSummaryView> Handle(GetTaxSummaryQuery r, CancellationToken ct)
    {
        var sales = await FiscalBooks.SalesAsync(db, r.Year, r.Month, ct);
        var purchases = await FiscalBooks.PurchasesAsync(db, r.Year, r.Month, ct);
        var (start, end) = FiscalBooks.Month(r.Year, r.Month);
        var invoices = await db.Set<FiscalDocument>().AsNoTracking().Include(d => d.Lines)
            .Where(d => d.Kind == FiscalDocumentKind.Invoice && d.IssuedAt >= start && d.IssuedAt < end
                        && (d.Status == FiscalDocumentStatus.Valid || d.Status == FiscalDocumentStatus.Pending || d.Status == FiscalDocumentStatus.Offline
                            || d.Status == FiscalDocumentStatus.InPackage))
            .ToListAsync(ct);
        var notes = await db.Set<FiscalDocument>().AsNoTracking().Include(d => d.Lines).Include(d => d.NoteReference)
            .Where(d => d.Kind == FiscalDocumentKind.CreditDebitNote && d.IssuedAt >= start && d.IssuedAt < end
                        && (d.Status == FiscalDocumentStatus.Valid || d.Status == FiscalDocumentStatus.Pending))
            .ToListAsync(ct);
        var grossSales = invoices.Sum(d => d.TotalAmount);
        var creditNotes = notes.Sum(d => d.ReturnedTotal);
        var taxCreditNotes = notes.Sum(d => d.VatAmount);
        var credits = purchases.TaxCredit + taxCreditNotes;
        var itBase = Math.Max(0m, grossSales - creditNotes);
        return new TaxSummaryView(r.Year, r.Month, grossSales, creditNotes, sales.TaxDebit, purchases.TaxCredit, taxCreditNotes,
            Math.Max(0m, sales.TaxDebit - credits), Math.Max(0m, credits - sales.TaxDebit), itBase,
            FiscalRules.Round2(itBase * SiatCodes.TransactionTaxRate), sales.Valid, sales.Voided, notes.Count);
    }
}
