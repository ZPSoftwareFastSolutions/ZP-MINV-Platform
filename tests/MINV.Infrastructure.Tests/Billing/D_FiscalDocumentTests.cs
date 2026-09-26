using System.Text;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Application.Sales;
using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>
/// V4.1 · Consulta de documentos fiscales (búsqueda, acciones permitidas por la hora fiscal, detalle con bitácora, XML y
/// entregas) y representación gráfica fiel a la emisión (emisor tomado del XML, leyendas, QR, monto literal) en PDF.
/// </summary>
public sealed class D_FiscalDocumentTests
{
    [Fact]
    public async Task Busca_documentos_y_arma_el_detalle_con_bitacora_XML_y_entregas()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.ConfigureAsync(admin, enable: false);
        await D_BillingTestHost.CreateProductAsync(admin, "FER-001", "Juego de destornilladores (6 piezas)");
        var sale = await D_BillingTestHost.SellAsync(admin, new SaleLineInput("FER-001", 2));
        var saleInvoiceId = await admin.Db.Set<MINV.Domain.Sales.Invoice>().Where(i => i.Number == sale.InvoiceNumber).Select(i => i.Id).SingleAsync();
        var cm = await D_Documents.PlaceAsync(admin);
        var ea = await D_Documents.PlaceAsync(admin, "EA");

        var valid = await D_Documents.InvoiceAsync(admin, cm, 1, [D_Documents.Line("FER-001", "Juego de destornilladores (6 piezas)", 2, 45m)], saleInvoiceId);
        var voided = await D_Documents.InvoiceAsync(admin, cm, 2, [D_Documents.Line("FER-001", "Juego de destornilladores", 1, 45m)],
            buyerName: "Constructora Andina S.A.", buyerDocument: "1020703023", documentType: SiatCodes.DocumentNit);
        await D_Documents.ChangeAsync(admin, voided.Id, d => d.Void(1, SiatCodes.VoidConfirmed, host.Clock.UtcNow));
        var pending = await D_Documents.InvoiceAsync(admin, ea, 1, [D_Documents.Line("FER-001", "Juego de destornilladores", 1, 50m)], accept: false);
        var replacement = await D_Documents.InvoiceAsync(admin, cm, 3, [D_Documents.Line("FER-001", "Juego de destornilladores", 1, 45m)],
            replaces: voided.Id);
        var note = await D_Documents.CreditNoteAsync(admin, cm, 1, valid,
        [
            D_Documents.Line("FER-001", "Juego de destornilladores (6 piezas)", 2, 45m, tx: 1),
            D_Documents.Line("FER-001", "Juego de destornilladores (6 piezas)", 1, 45m, tx: 2),
        ]);

        var today = DateOnly.FromDateTime(D_Documents.FiscalNow(admin));
        var all = await admin.Send(new GetFiscalDocumentsQuery(today, today));
        Assert.Equal(5, all.Count);
        var row = all.Single(r => r.Id == valid.Id);
        Assert.Equal((sale.InvoiceNumber, "CM", 0, 90m), (row.SaleNumber, row.BranchCode, row.PointOfSaleCode, row.Total));
        Assert.True(row is { CanVoid: true, CanRevert: false, CanCreditNote: true, Status: FiscalDocumentStatus.Valid });
        Assert.Equal(new DateTime(2026, 10, 9, 23, 59, 59, 999).AddTicks(9999), row.VoidDeadline);
        var voidedRow = all.Single(r => r.Id == voided.Id);
        Assert.True(voidedRow is { CanVoid: false, CanRevert: true, CanCreditNote: false, Status: FiscalDocumentStatus.Voided });
        Assert.Equal("1020703023", voidedRow.BuyerDocument);
        Assert.True(all.Single(r => r.Id == pending.Id) is { CanVoid: false, BranchCode: "EA", Status: FiscalDocumentStatus.Pending });
        var noteRow = all.Single(r => r.Id == note.Id);
        Assert.Equal((FiscalDocumentKind.CreditDebitNote, 45m, sale.InvoiceNumber), (noteRow.Kind, noteRow.Total, noteRow.SaleNumber));
        Assert.False(noteRow.CanCreditNote);

        // Filtros y búsqueda por número, CUF, documento o nombre (sin distinguir mayúsculas)
        Assert.Equal(4, (await admin.Send(new GetFiscalDocumentsQuery(today, today, Kind: FiscalDocumentKind.Invoice))).Count);
        Assert.Equal(voided.Id, Assert.Single(await admin.Send(new GetFiscalDocumentsQuery(today, today, FiscalDocumentStatus.Voided))).Id);
        Assert.Contains(await admin.Send(new GetFiscalDocumentsQuery(today, today, Search: valid.Cuf[..20].ToLowerInvariant())), r => r.Id == valid.Id);
        Assert.Equal(voided.Id, Assert.Single(await admin.Send(new GetFiscalDocumentsQuery(today, today, Search: "andina"))).Id);
        Assert.Equal(voided.Id, Assert.Single(await admin.Send(new GetFiscalDocumentsQuery(today, today, Search: "10207"))).Id);
        Assert.Equal(3, (await admin.Send(new GetFiscalDocumentsQuery(today, today, Search: "1"))).Count(r => r.Number == 1));
        Assert.Empty(await admin.Send(new GetFiscalDocumentsQuery(today.AddDays(1), today.AddDays(2))));
        await Assert.ThrowsAsync<DomainException>(() => admin.Send(new GetFiscalDocumentsQuery(today, today.AddDays(-1))));

        // Detalle: unidad SIN descriptiva, bitácora con el usuario, XML, medio de pago y motivo de anulación del catálogo
        var detail = await admin.Send(new GetFiscalDocumentQuery(valid.Id));
        Assert.Equal("UNIDAD (BIENES)", Assert.Single(detail.Lines).Unit);
        Assert.Equal((90m, 11.70m), (detail.Lines[0].Subtotal, detail.TaxAmount));
        Assert.Contains(detail.Events, e => e.Action == FiscalDocumentAction.Issued && e.User == D_BillingTestHost.AdminName);
        Assert.StartsWith("<?xml", detail.Xml, StringComparison.Ordinal);
        Assert.Contains($"<cuf>{valid.Cuf}</cuf>", detail.Xml, StringComparison.Ordinal);
        Assert.Equal("EFECTIVO", detail.PaymentMethod);
        Assert.Equal(D_Documents.Legend, detail.Legend);
        Assert.Equal("REC-1", detail.ReceptionCode);
        var voidedDetail = await admin.Send(new GetFiscalDocumentQuery(voided.Id));
        Assert.Equal("FACTURA MAL EMITIDA", voidedDetail.VoidReason);
        Assert.Equal(replacement.Id, voidedDetail.ReplacedByDocumentId);
        Assert.Equal(voided.Id, (await admin.Send(new GetFiscalDocumentQuery(replacement.Id))).ReplacesDocumentId);
        var noteDetail = await admin.Send(new GetFiscalDocumentQuery(note.Id));
        Assert.Equal((valid.Number, valid.Cuf), (noteDetail.Original!.Number, noteDetail.Original.Cuf));
        Assert.Equal(5.85m, noteDetail.TaxAmount);   // 13 % de lo devuelto (45)
        await Assert.ThrowsAsync<NotFoundException>(() => admin.Send(new GetFiscalDocumentQuery(Guid.NewGuid())));

        // Entregas: impresión y correo, con su constancia en la bitácora
        Assert.Contains("Impreso", await admin.Send(new RecordFiscalDeliveryCommand(valid.Id, FiscalDeliveryChannel.Print)), StringComparison.Ordinal);
        await admin.Send(new RecordFiscalDeliveryCommand(valid.Id, FiscalDeliveryChannel.Email, "juan@correo.example"));
        await Assert.ThrowsAsync<DomainException>(() => admin.Send(new RecordFiscalDeliveryCommand(valid.Id, FiscalDeliveryChannel.Email, "no-es-correo")));
        detail = await admin.Send(new GetFiscalDocumentQuery(valid.Id));
        Assert.Equal([FiscalDeliveryChannel.Print, FiscalDeliveryChannel.Email], detail.Deliveries.Select(d => d.Channel));
        Assert.Equal("juan@correo.example", detail.Deliveries[1].Recipient);
        Assert.Contains(detail.Events, e => e.Action == FiscalDocumentAction.Printed);
        Assert.Contains(detail.Events, e => e.Action == FiscalDocumentAction.Delivered);
    }

    [Fact]
    public async Task La_representacion_grafica_es_fiel_a_la_emision_y_se_renderiza_en_PDF()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.ConfigureAsync(admin, enable: false);
        await D_BillingTestHost.CreateProductAsync(admin, "FER-001", "Juego de destornilladores (6 piezas)");
        var sale = await D_BillingTestHost.SellAsync(admin, new SaleLineInput("FER-001", 2));
        var saleInvoiceId = await admin.Db.Set<MINV.Domain.Sales.Invoice>().Where(i => i.Number == sale.InvoiceNumber).Select(i => i.Id).SingleAsync();
        var cm = await D_Documents.PlaceAsync(admin);
        var ea = await D_Documents.PlaceAsync(admin, "EA");
        var invoice = await D_Documents.InvoiceAsync(admin, cm, 1,
            [D_Documents.Line("FER-001", "Juego de destornilladores (6 piezas)", 2, 45.25m, discount: 0.50m)], saleInvoiceId, additionalDiscount: 1m);

        // Después de emitir cambian la razón social y el teléfono: la representación gráfica NO cambia (sale del XML)
        await admin.Send(new SaveSiatSettingsCommand(D_BillingTestHost.Nit, "Otra Razón Social S.A.", D_BillingTestHost.SystemCode, SiatCodes.EnvironmentTest,
            null, null, false));
        await admin.Send(new SaveSiatBranchCommand("CM", 0, "Nuestra Señora de La Paz", "2999999"));

        var model = await admin.Send(new GetFiscalPrintModelQuery(invoice.Id));
        Assert.Equal(("FACTURA", "(Con Derecho a Crédito Fiscal)"), (model.Title, model.Subtitle));
        Assert.Equal((D_BillingTestHost.CompanyName, D_BillingTestHost.Nit, "CASA MATRIZ", 0), (model.IssuerName, model.IssuerNit, model.BranchLabel,
            model.PointOfSaleCode));
        Assert.Equal(("La Paz", "2800000", cm.Cufd.Address), (model.Municipality, model.Phone, model.Address));
        Assert.Equal((1L, invoice.Cuf, invoice.IssuedAt), (model.Number, model.Cuf, model.IssuedAt));
        Assert.Equal(("Juan Pérez", "5115889", "CF"), (model.BuyerName, model.BuyerDocument, model.CustomerCode));
        var line = Assert.Single(model.Lines);
        Assert.Equal(("FER-001", "UNIDAD (BIENES)", 2m, 45.25m, 0.50m, 90m), (line.ProductCode, line.Unit, line.Quantity, line.UnitPrice, line.Discount, line.Subtotal));
        Assert.Equal((90m, 1m, 89m, 0m, 89m, 89m), (model.Subtotal, model.Discount, model.Total, model.GiftCard, model.AmountToPay, model.TaxBase));
        Assert.Equal("Son: Ochenta y nueve 00/100 Bolivianos", model.AmountInWords);
        Assert.Equal([SiatSettings.FixedLegend, D_Documents.Legend, SiatSettings.DefaultOnlineLegend], model.Legends);
        Assert.Equal($"{D_BillingTestHost.QrBase}?nit={D_BillingTestHost.Nit}&cuf={invoice.Cuf}&numero=1&t=2", model.QrUrl);
        Assert.True(model is { IsTest: true, IsVoided: false, IsOffline: false, Original: null });
        Assert.Equal((D_BillingTestHost.AdminName, "EFECTIVO", sale.InvoiceNumber), (model.Cashier, model.PaymentMethod, model.SaleNumber));

        // Sucursal 1 del Padrón y fuera de línea: «SUCURSAL N° 1» y la leyenda de fuera de línea
        var cafc = await D_Documents.InvoiceAsync(admin, ea, 5, [D_Documents.Line("FER-001", "Juego", 1, 10m)], emission: SiatCodes.EmissionOffline);
        var offline = await admin.Send(new GetFiscalPrintModelQuery(cafc.Id));
        Assert.Equal(("SUCURSAL N° 1", "El Alto", null), (offline.BranchLabel, offline.Municipality, offline.Phone));
        Assert.True(offline.IsOffline);
        Assert.Equal(SiatSettings.DefaultOfflineLegend, offline.Legends[2]);

        // Nota crédito-débito: título, factura original y monto devuelto (literal del devuelto)
        var note = await D_Documents.CreditNoteAsync(admin, cm, 1, invoice,
        [
            D_Documents.Line("FER-001", "Juego de destornilladores (6 piezas)", 2, 45.25m, 0.50m, tx: 1),
            D_Documents.Line("FER-001", "Juego de destornilladores (6 piezas)", 1, 45.25m, tx: 2),
        ]);
        var noteModel = await admin.Send(new GetFiscalPrintModelQuery(note.Id));
        Assert.Equal(("NOTA CRÉDITO - DÉBITO", string.Empty), (noteModel.Title, noteModel.Subtitle));
        Assert.Equal((invoice.Number, invoice.Cuf, invoice.IssuedAt), (noteModel.Original!.Number, noteModel.Original.Cuf, noteModel.Original.IssuedAt));
        Assert.Equal((45.25m, 5.88m), (noteModel.ReturnedTotal!.Value, noteModel.CreditDebitAmount!.Value));
        Assert.Equal("Son: Cuarenta y cinco 25/100 Bolivianos", noteModel.AmountInWords);
        Assert.Equal(2, noteModel.Lines.Count);
        Assert.Equal(sale.InvoiceNumber, noteModel.SaleNumber);   // la venta de la factura original

        // PDF (hoja): nombre con el número y la sucursal; rollo sin impresora ESC/POS → aviso claro
        var pdf = await admin.Send(new RenderFiscalDocumentQuery(invoice.Id));
        Assert.Equal(("Factura-1-CM.pdf", "application/pdf"), (pdf.FileName, pdf.ContentType));
        var text = Encoding.Latin1.GetString(pdf.Content);
        Assert.StartsWith("%PDF-", text, StringComparison.Ordinal);
        Assert.Contains("(FACTURA)", text, StringComparison.Ordinal);
        Assert.Contains("SIN VALOR LEGAL", text, StringComparison.Ordinal);
        Assert.Equal("NotaCreditoDebito-1-CM.pdf", (await admin.Send(new RenderFiscalDocumentQuery(note.Id))).FileName);
        Assert.Equal("billing.no_roll_printer", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new RenderFiscalDocumentQuery(invoice.Id, FiscalDeliveryChannel.Print)))).Code);
        Assert.Equal("billing.render_format", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new RenderFiscalDocumentQuery(invoice.Id, FiscalDeliveryChannel.Email)))).Code);

        // Anulado: marca de agua
        await D_Documents.ChangeAsync(admin, invoice.Id, d => d.Void(1, SiatCodes.VoidConfirmed, host.Clock.UtcNow));
        Assert.True((await admin.Send(new GetFiscalPrintModelQuery(invoice.Id))).IsVoided);
        Assert.Equal("https://x.example/QR?nit=1&cuf=AB&numero=2&t=1",
            FiscalPrintModelBuilder.WithQrSize(FiscalPrintModelBuilder.QrUrl("https://x.example/QR", 1, "AB", 2, 2), 1));
    }
}
