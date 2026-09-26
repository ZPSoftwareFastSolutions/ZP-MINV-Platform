using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Application.Partners;
using MINV.Application.Purchasing;
using MINV.Application.Sales;
using MINV.Domain.Accounting;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Purchasing;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>
/// V4.1 · Libros del RCV (investigación 07 §5): ventas (24 columnas, V/A/C, código de control 0; sin las notas), compras
/// (23 columnas) con las facturas de proveedores registradas sobre sus recepciones (crédito fiscal y su asiento), la
/// exportación CSV/Excel con los encabezados exactos de las plantillas del SIN y el resumen IVA/IT del mes.
/// </summary>
public sealed class D_FiscalBookTests
{
    private const string Cuf = "44AAEC00DBD34C53C3E2CCE1A3FA7AF1E2A08606A667A75AC82F24C74";

    [Fact]
    public async Task Factura_de_proveedor_sobre_su_recepcion_con_el_asiento_del_credito_fiscal()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        var receipt = await ReceiveAsync(admin, 20, 56.5m);   // 20 × 56.50 = 1 130.00

        Assert.Contains(await admin.Send(new GetReceiptsWithoutInvoiceQuery()), r => r.ReceiptNumber == receipt && r.Total == 1130m);
        var message = await admin.Send(new RegisterSupplierInvoiceCommand(receipt, "4521", Cuf, new DateOnly(2026, 9, 24), 1130m));
        Assert.Contains("crédito fiscal Bs 146.90", message, StringComparison.Ordinal);

        var invoice = await admin.Db.Set<SupplierInvoice>().AsNoTracking().Include(i => i.Lines).SingleAsync();
        Assert.Equal(SupplierInvoiceStatus.Posted, invoice.Status);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal((20m, 56.5m), (line.Quantity, line.UnitCost));
        // Arco exclusivo ck_supplier_invoice_lines_origen: la línea sale de la recepción (el producto está en ella) y NO
        // repite la descripción (PostgreSQL rechaza las dos a la vez)
        Assert.NotNull(line.GoodsReceiptLineId);
        Assert.Null(line.Description);
        var fiscal = await admin.Db.Set<SupplierInvoiceFiscal>().AsNoTracking().SingleAsync();
        Assert.Equal((1130m, 146.90m, Cuf), (fiscal.TaxBase, fiscal.TaxCredit, fiscal.AuthorizationCode));

        // Asiento: Debe 1.1.04 IVA crédito fiscal / Haber 1.1.05 Inventario por el 13 % de la base
        var entry = await admin.Db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.SourceCorrelationId == invoice.Id);
        var accounts = await admin.Db.Set<Account>().AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);
        Assert.Equal(146.90m, entry.Lines.Where(l => accounts[l.AccountId] == AccountCodes.VatCredit).Sum(l => l.Debit));
        Assert.Equal(146.90m, entry.Lines.Where(l => accounts[l.AccountId] == AccountCodes.Inventory).Sum(l => l.Credit));
        Assert.Equal(new DateOnly(2026, 9, 24), entry.EntryDate);

        // Una recepción no puede tener dos facturas; ya no está entre las pendientes
        Assert.Equal("supplier_invoice.receipt_invoiced", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new RegisterSupplierInvoiceCommand(receipt, "4522", Cuf, new DateOnly(2026, 9, 24), 1130m)))).Code);
        Assert.DoesNotContain(await admin.Send(new GetReceiptsWithoutInvoiceQuery()), r => r.ReceiptNumber == receipt);
        await Assert.ThrowsAsync<NotFoundException>(() => admin.Send(new RegisterSupplierInvoiceCommand("RC-XX-000009", "1", Cuf, new DateOnly(2026, 9, 24), 10m)));
        await Assert.ThrowsAsync<RequestValidationException>(() => admin.Send(new RegisterSupplierInvoiceCommand(receipt, "1", Cuf, new DateOnly(2026, 9, 24), 0m)));
        var another = await ReceiveAsync(admin, 1, 10m);
        Assert.Equal("supplier_invoice.future", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new RegisterSupplierInvoiceCommand(another, "9", Cuf, new DateOnly(2026, 12, 1), 10m)))).Code);

        var rows = await admin.Send(new GetSupplierInvoicesQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        var row = Assert.Single(rows);
        Assert.Equal(("4521", "Contabilizada", receipt, "CM"), (row.Number, row.Status, row.ReceiptNumber, row.BranchCode));
        Assert.Equal((1130m, 1130m, 146.90m, "1020304050"), (row.TotalAmount, row.TaxBase, row.TaxCredit, row.SupplierNit));
    }

    /// <summary>
    /// V4.2 · Recepción al costo NETO de IVA (así compran el catálogo y los datos de prueba): la factura del proveedor trae el
    /// IVA encima. Su asiento suma ese IVA a la deuda con el proveedor (2.1.01) y del inventario sale solo la diferencia con el
    /// crédito del SIN (13 % del importe con IVA). Con un descuento del proveedor, la deuda baja al importe menos el descuento.
    /// En todos los casos el inventario queda en el importe de la factura menos descuentos y crédito fiscal. Con la convención
    /// de la V4.2 (costo neto = 87 % del importe, en Bolivia el crédito fiscal es el 13 % de la factura) el importe que
    /// corresponde a la recepción es recepción / 0,87 (<see cref="FiscalRules.InvoiceForNetCost"/>): el crédito fiscal es
    /// exactamente el IVA que se suma a la deuda y el inventario no cambia (sigue en el valor del stock).
    /// </summary>
    [Fact]
    public async Task Factura_de_proveedor_sobre_una_recepcion_al_costo_neto_suma_el_IVA_a_la_deuda()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        var accounts = await admin.Db.Set<Account>().AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);
        async Task<(decimal Vat, decimal PayablesDebit, decimal PayablesCredit, decimal InventoryCredit)> EntryOfAsync(string number)
        {
            var invoice = await admin.Db.Set<SupplierInvoice>().AsNoTracking().SingleAsync(i => i.Number == number);
            var entry = await admin.Db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.SourceCorrelationId == invoice.Id);
            Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
            decimal Sum(string code, Func<JournalLine, decimal> side) => entry.Lines.Where(l => accounts[l.AccountId] == code).Sum(side);
            return (Sum(AccountCodes.VatCredit, l => l.Debit), Sum(AccountCodes.Payables, l => l.Debit), Sum(AccountCodes.Payables, l => l.Credit),
                Sum(AccountCodes.Inventory, l => l.Credit));
        }

        // 10 × 100.00 al costo neto = 1 000.00; la factura es de 1 130.00 con IVA: crédito 146.90, la deuda sube 130.00 y el
        // inventario baja 16.90 (queda en 983.10 = 1 130.00 − 146.90)
        var net = await ReceiveAsync(admin, 10, 100m);
        await admin.Send(new RegisterSupplierInvoiceCommand(net, "5001", Cuf, new DateOnly(2026, 9, 24), 1130m));
        Assert.Equal((146.90m, 0m, 130m, 16.90m), await EntryOfAsync("5001"));

        // 10 × 11.30 con IVA = 113.00 y 3.00 de descuento: crédito 14.30 (13 % de 110), la deuda baja 3.00 y el inventario
        // 17.30 (queda en 95.70 = 110.00 − 14.30)
        var gross = await ReceiveAsync(admin, 10, 11.3m);
        await admin.Send(new RegisterSupplierInvoiceCommand(gross, "5002", Cuf, new DateOnly(2026, 9, 24), 113m, Discounts: 3m));
        Assert.Equal((14.30m, 3m, 0m, 17.30m), await EntryOfAsync("5002"));

        // Convención de la V4.2: 10 × 100.00 al costo neto = 1 000.00 → factura de 1 000.00 / 0,87 = 1 149.43; crédito 149.43
        // (13 % de 1 149.43 = 149.4259), la deuda sube 149.43 y el inventario NO cambia (el asiento no toca 1.1.05)
        var exact = await ReceiveAsync(admin, 10, 100m);
        Assert.Equal(1149.43m, FiscalRules.InvoiceForNetCost(1000m));
        await admin.Send(new RegisterSupplierInvoiceCommand(exact, "5003", Cuf, new DateOnly(2026, 9, 24), FiscalRules.InvoiceForNetCost(1000m)));
        Assert.Equal((149.43m, 0m, 149.43m, 0m), await EntryOfAsync("5003"));
    }

    /// <summary>
    /// V4.2 · El IVA de una venta es el débito fiscal de su factura en el libro de ventas: el 13 % del TOTAL redondeado una vez
    /// (<c>FiscalRules.Vat</c>), repartido entre las líneas. Redondeando línea por línea, 729,50 + 15,50 daba 94,84 + 2,02 =
    /// 96,86 y el asiento (2.1.02) quedaba un centavo por encima del libro (96,85).
    /// </summary>
    [Fact]
    public async Task El_IVA_de_la_venta_es_el_debito_fiscal_del_total_de_la_factura()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.CreateProductAsync(admin, "FER-101", "Taladro percutor 750 W", price: 729.50m);
        await D_BillingTestHost.CreateProductAsync(admin, "FER-102", "Broca para concreto 8 mm", price: 15.50m);
        var sale = await D_BillingTestHost.SellAsync(admin, new SaleLineInput("FER-101", 1), new SaleLineInput("FER-102", 1));
        Assert.Equal((745m, 96.85m), (sale.Total, sale.Tax));
        Assert.Equal(FiscalRules.Vat(sale.Total), sale.Tax);

        var invoice = await admin.Db.Set<MINV.Domain.Sales.Invoice>().AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.Number == sale.InvoiceNumber);
        Assert.Equal([2.01m, 94.84m], invoice.Lines.Select(l => l.TaxAmount).Order());
        var accounts = await admin.Db.Set<Account>().AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);
        var entry = await admin.Db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.SourceCorrelationId == invoice.SalesOrderId);
        Assert.Equal((96.85m, 648.15m), (entry.Lines.Where(l => accounts[l.AccountId] == AccountCodes.VatDebit).Sum(l => l.Credit),
            entry.Lines.Where(l => accounts[l.AccountId] == AccountCodes.Sales).Sum(l => l.Credit)));
    }

    [Fact]
    public async Task Libros_del_mes_exportables_y_resumen_IVA_IT()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.ConfigureAsync(admin, enable: false);
        var cm = await D_Documents.PlaceAsync(admin);

        // Ventas: dos válidas (una con descuento adicional), una anulada, una rechazada (no cuenta), una transcrita (CAFC) y una nota
        var a = await D_Documents.InvoiceAsync(admin, cm, 1, [D_Documents.Line("FER-001", "Destornilladores", 2, 50m)]);
        var b = await D_Documents.InvoiceAsync(admin, cm, 2, [D_Documents.Line("FER-002", "Tornillos", 10, 10m, discount: 5m)], additionalDiscount: 5m,
            buyerName: "Constructora Andina S.A.", buyerDocument: "1020703023", documentType: SiatCodes.DocumentNit);
        var voided = await D_Documents.InvoiceAsync(admin, cm, 3, [D_Documents.Line("FER-001", "Destornilladores", 1, 50m)]);
        await D_Documents.ChangeAsync(admin, voided.Id, d => d.Void(1, SiatCodes.VoidConfirmed, host.Clock.UtcNow));
        var rejected = await D_Documents.InvoiceAsync(admin, cm, 4, [D_Documents.Line("FER-001", "Destornilladores", 1, 50m)], accept: false);
        await D_Documents.ChangeAsync(admin, rejected.Id, d => d.Reject(SiatCodes.ReceptionRejected));
        var manual = await D_Documents.InvoiceAsync(admin, cm, 5, [D_Documents.Line("FER-001", "Destornilladores", 1, 20m)],
            emission: SiatCodes.EmissionOffline, cafc: "CAFC-01");
        await D_Documents.CreditNoteAsync(admin, cm, 1, a,
        [
            D_Documents.Line("FER-001", "Destornilladores", 2, 50m, tx: 1),
            D_Documents.Line("FER-001", "Destornilladores", 1, 50m, tx: 2),
        ]);
        // Documento de otro mes: no entra
        await D_Documents.InvoiceAsync(admin, cm, 6, [D_Documents.Line("FER-001", "Destornilladores", 1, 50m)], issuedAt: new DateTime(2026, 8, 31, 23, 59, 59, 999));

        var book = await admin.Send(new GetSalesBookQuery(2026, 9));
        Assert.Equal([1L, 2L, 3L, 5L], book.Rows.Select(r => r.Number));   // sin la rechazada ni la nota; en orden de emisión
        Assert.Equal([1, 2, 3, 4], book.Rows.Select(r => r.Row));
        var first = book.Rows[0];
        Assert.Equal((100m, 100m, 0m, 0m, 100m, 13m, "V", "0", a.Cuf), (first.Total, first.Subtotal, first.Discounts, first.GiftCard, first.TaxBase,
            first.TaxDebit, first.Status, first.ControlCode, first.Cuf));
        Assert.Equal(("5115889", "Juan Pérez", 1, "CM"), (first.BuyerDocument, first.BuyerName, first.DocumentSector, first.BranchCode));
        var second = book.Rows[1];
        Assert.Equal((95m, 5m, 90m, 11.70m), (second.Total, second.Discounts, second.TaxBase, second.TaxDebit));   // 100 − 5 por línea; − 5 adicional
        var annulled = book.Rows[2];
        Assert.Equal(("A", 0m, 0m), (annulled.Status, annulled.Total, annulled.TaxDebit));
        Assert.Equal(("C", 20m), (book.Rows[3].Status, book.Rows[3].Total));
        Assert.Equal((215m, 210m, 27.30m, 3, 1), (book.Total, book.TaxBase, book.TaxDebit, book.Valid, book.Voided));

        // Compras del mes
        var receipt = await ReceiveAsync(admin, 10, 11.3m);
        await admin.Send(new RegisterSupplierInvoiceCommand(receipt, "777", Cuf, new DateOnly(2026, 9, 20), 113m, Discounts: 3m, NotSubjectToVat: 10m));
        var purchases = await admin.Send(new GetPurchasesBookQuery(2026, 9));
        var p = Assert.Single(purchases.Rows);
        Assert.Equal(("1020304050", "Aceros del Sur S.R.L.", Cuf, "777", new DateOnly(2026, 9, 20)), (p.SupplierNit, p.SupplierName, p.AuthorizationCode,
            p.InvoiceNumber, p.Date));
        Assert.Equal((113m, 10m, 103m, 3m, 100m, 13m, "1", "0"), (p.Total, p.OtherNotSubject, p.Subtotal, p.Discounts, p.TaxBase, p.TaxCredit, p.PurchaseType,
            p.ControlCode));
        Assert.Equal((113m, 100m, 13m), (purchases.Total, purchases.TaxBase, purchases.TaxCredit));
        Assert.Empty((await admin.Send(new GetPurchasesBookQuery(2026, 8))).Rows);

        // Resumen: débito 27.30 − crédito de compras 13 − crédito de la nota 6.50 = 7.80 a pagar; IT 3 % de las ventas netas
        // (montos cobrados 100 + 90 + 20 = 210, menos lo devuelto 50)
        var summary = await admin.Send(new GetTaxSummaryQuery(2026, 9));
        Assert.Equal((210m, 50m, 27.30m, 13m, 6.50m), (summary.GrossSales, summary.CreditNotes, summary.TaxDebit, summary.TaxCreditPurchases,
            summary.TaxCreditNotes));
        Assert.Equal((7.80m, 0m, 160m, 4.80m), (summary.VatPayable, summary.VatCarryForward, summary.TransactionTaxBase, summary.TransactionTax));
        Assert.Equal((3, 1, 1), (summary.Invoices, summary.VoidedInvoices, summary.Notes));
        var august = await admin.Send(new GetTaxSummaryQuery(2026, 8));
        Assert.Equal((6.50m, 0m), (august.TaxDebit, august.VatCarryForward));
        Assert.Equal("book.period", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new GetSalesBookQuery(2026, 13)))).Code);

        // CSV con los encabezados EXACTOS de la plantilla del SIN (ESPECIFICACIÓN 2, fecha dd/mm/aaaa, punto decimal)
        var csv = await admin.Send(new ExportFiscalBookQuery(2026, 9, false));
        Assert.Equal(("LibroVentas-202609.csv", "text/csv; charset=utf-8"), (csv.FileName, csv.ContentType));
        using var reader = new StreamReader(new MemoryStream(csv.Content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = (await reader.ReadToEndAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        Assert.Equal("Nº,ESPECIFICACION,FECHA DE LA FACTURA,N° DE LA FACTURA,CODIGO DE AUTORIZACION,NIT / CI CLIENTE,COMPLEMENTO,NOMBRE O RAZON SOCIAL," +
                     "IMPORTE TOTAL DE LA VENTA,IMPORTE ICE,IMPORTE IEHD,IMPORTE IPJ,TASAS,OTROS NO SUJETOS AL IVA,EXPORTACIONES Y OPERACIONES EXENTAS," +
                     "VENTAS GRAVADAS A TASA CERO,SUBTOTAL,\"DESCUENTOS, BONIFICACIONES Y REBAJAS SUJETAS AL IVA\",IMPORTE GIFT CARD," +
                     "IMPORTE BASE PARA DEBITO FISCAL,DEBITO FISCAL,ESTADO,CODIGO DE CONTROL,TIPO DE VENTA", lines[0]);
        Assert.Equal($"1,2,25/09/2026,1,{a.Cuf},5115889,,Juan Pérez,100.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,100.00,0.00,0.00,100.00,13.00,V,0,0", lines[1]);
        Assert.Contains(",Constructora Andina S.A.,95.00,", lines[2], StringComparison.Ordinal);
        var purchasesCsv = await admin.Send(new ExportFiscalBookQuery(2026, 9, true, "CSV"));
        Assert.Equal("LibroCompras-202609.csv", purchasesCsv.FileName);
        var purchaseLines = Encoding.UTF8.GetString(purchasesCsv.Content).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Nº,ESPECIFICACION,NIT PROVEEDOR,RAZON SOCIAL PROVEEDOR,CODIGO DE AUTORIZACION,NUMERO FACTURA,NUMERO DUI/DIM,", purchaseLines[0],
            StringComparison.Ordinal);
        Assert.Equal($"1,1,1020304050,Aceros del Sur S.R.L.,{Cuf},777,0,20/09/2026,113.00,0.00,0.00,0.00,0.00,10.00,0.00,0.00,103.00,3.00,0.00,100.00,13.00,1,0",
            purchaseLines[1]);

        // Excel: un zip con xl/workbook.xml, la hoja con los encabezados como texto y los importes como NÚMERO
        var xlsx = await admin.Send(new ExportFiscalBookQuery(2026, 9, false, "xlsx"));
        Assert.Equal(("LibroVentas-202609.xlsx", ExportFiscalBookHandler.XlsxContentType), (xlsx.FileName, xlsx.ContentType));
        using (var zip = new ZipArchive(new MemoryStream(xlsx.Content), ZipArchiveMode.Read))
        {
            Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
            Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
            Assert.NotNull(zip.GetEntry("xl/_rels/workbook.xml.rels"));
            using var sheetStream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var sheet = XDocument.Load(sheetStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var rows = sheet.Descendants(ns + "row").ToList();
            Assert.Equal(5, rows.Count);
            var header = rows[0].Elements(ns + "c").Select(c => c.Value).ToList();
            Assert.Equal(24, header.Count);
            Assert.Equal("IMPORTE TOTAL DE LA VENTA", header[8]);
            var total = rows[1].Elements(ns + "c").Single(c => (string?)c.Attribute("r") == "I2");
            Assert.Null(total.Attribute("t"));   // número, no texto
            Assert.Equal(100m, decimal.Parse(total.Value, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal("inlineStr", (string?)rows[1].Elements(ns + "c").Single(c => (string?)c.Attribute("r") == "C2").Attribute("t"));
            Assert.Equal("25/09/2026", rows[1].Elements(ns + "c").Single(c => (string?)c.Attribute("r") == "C2").Value);
        }
        Assert.Equal("book.format", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new ExportFiscalBookQuery(2026, 9, false, "pdf")))).Code);
        Assert.Equal("AA7", SimpleXlsxWriter.Reference(26, 7));
    }

    /// <summary>Compra recibida (orden aprobada y recepción contabilizada) del proveedor «Aceros del Sur»; devuelve el número de la recepción.</summary>
    private static async Task<string> ReceiveAsync(D_Session admin, decimal quantity, decimal cost)
    {
        var suppliers = await admin.Send(new GetSuppliersQuery());
        var supplier = suppliers.FirstOrDefault(s => s.Name == "Aceros del Sur S.R.L.")?.Code
                       ?? await admin.Send(new SaveSupplierCommand(null, "Aceros del Sur S.R.L.", "1020304050", 3, "Rubén Céspedes", "70000000",
                           "ventas@aceros.example", true));
        var products = await admin.Db.Set<MINV.Domain.Catalog.ProductVariant>().AnyAsync(v => v.Sku == "FER-010");
        if (!products)
        {
            await D_BillingTestHost.CreateProductAsync(admin, "FER-010", "Perno hexagonal 1/2\" (caja x50)", stock: 0);
        }
        var order = await admin.Send(new CreatePurchaseOrderCommand(supplier, null, null, [new PurchaseLineInput("FER-010", quantity, cost)]));
        await admin.Send(new ApprovePurchaseOrderCommand(order.Id));
        return (await admin.Send(new ReceivePurchaseOrderCommand(order.Id, "FAC-PROV"))).ReceiptNumber;
    }
}
