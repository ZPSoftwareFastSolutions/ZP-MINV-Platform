using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Sales;
using MINV.Domain.Accounting;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>V4.1 · Devoluciones con nota crédito-débito (agente E): transacción 1 = TODAS las líneas de la factura, 2 = las
/// devueltas; el caso armado del ejemplo oficial (775 original, 75 devuelto → 9,75 de crédito fiscal).</summary>
[Collection(E_BillingCollection.Name)]
public sealed class E_SalesReturnTests
{
    [Fact]
    public async Task Devolucion_parcial_emite_la_nota_con_tx1_y_tx2_y_se_valida_en_el_SIN()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        using var admin = await host.AdminAsync();
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("AMO-001", 1), new SaleLineInput("TOR-001", 1));
        Assert.Equal(775m, sale.Total);
        Assert.Equal(1, (await cashier.Send(new DispatchFiscalDocumentsCommand(sale.FiscalDocumentId))).Valid);
        var stockBefore = await host.OnHandAsync("TOR-001");

        var result = await admin.Send(new CreateSalesReturnCommand(sale.InvoiceNumber, "El cliente devolvió los tornillos", "EFECTIVO",
            [new ReturnLineInput("TOR-001", 1)]));
        Assert.Equal("DV-CM-000001", result.Number);
        Assert.Equal(75m, result.Refund);
        Assert.Equal(FiscalDocumentStatus.Pending, result.CreditNoteStatus);
        Assert.Equal(host.CashPointId, (await host.DocumentAsync(result.CreditNoteId!.Value)).PointOfSaleId);
        Assert.Equal(stockBefore + 1, await host.OnHandAsync("TOR-001"));

        var note = await host.DocumentAsync(result.CreditNoteId!.Value);
        Assert.Equal(FiscalDocumentKind.CreditDebitNote, note.Kind);
        Assert.Equal(SiatCodes.SectorCreditDebitNote, note.DocumentSector);
        Assert.Equal(1, note.Number);   // serie propia de las notas en el punto de venta
        Assert.Equal(2, note.Lines.Count(l => l.TransactionCode == 1));
        var returned = Assert.Single(note.Lines, l => l.TransactionCode == 2);
        Assert.Equal("TOR-001", returned.ProductCode);
        Assert.Equal(1m, returned.Quantity);
        Assert.Equal(775m, note.OriginalTotal);
        Assert.Equal(75m, note.ReturnedTotal);
        Assert.Equal(9.75m, note.VatAmount);
        var invoice = await host.DocumentAsync(sale.FiscalDocumentId!.Value);
        Assert.Equal(invoice.Id, note.NoteReference!.OriginalDocumentId);
        Assert.Equal(invoice.Cuf, note.NoteReference.OriginalCuf);
        Assert.Equal(invoice.Number, note.NoteReference.OriginalNumber);
        Assert.Equal(invoice.BuyerDocumentNumber, note.BuyerDocumentNumber);
        var xml = await host.DbAsync(db => db.Set<FiscalDocumentFile>().Where(f => f.DocumentId == note.Id).Select(f => f.Xml).FirstAsync());
        host.Services.GetRequiredService<IFiscalDocumentSerializer>().Validate(xml, SiatCodes.SectorCreditDebitNote);
        Assert.Contains("<montoEfectivoCreditoDebito>9.75</montoEfectivoCreditoDebito>", xml, StringComparison.Ordinal);

        // Asiento de la devolución: Debe ventas + IVA débito / Haber caja (reembolso) y costo de vuelta al inventario
        var entry = await host.DbAsync(db => db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines)
            .FirstAsync(e => e.Description.StartsWith("Devolución " + result.Number)));
        Assert.Equal(entry.TotalDebit, entry.TotalCredit);
        var accounts = await host.DbAsync(db => db.Set<Account>().ToDictionaryAsync(a => a.Id, a => a.Code));
        Assert.Equal(75m, entry.Lines.Where(l => accounts[l.AccountId] == AccountCodes.Cash).Sum(l => l.Credit));
        Assert.Equal(75m, entry.Lines.Where(l => accounts[l.AccountId] is AccountCodes.Sales or AccountCodes.VatDebit).Sum(l => l.Debit));
        Assert.Equal(40m, entry.Lines.Where(l => accounts[l.AccountId] == AccountCodes.Inventory).Sum(l => l.Debit));

        // La caja la despacha: el SIN la valida
        var dispatch = await admin.Send(new DispatchFiscalDocumentsCommand(note.Id));
        Assert.Equal(1, dispatch.Valid);
        Assert.Equal(FiscalDocumentStatus.Valid, (await host.DocumentAsync(note.Id)).Status);

        // Una segunda devolución que excede lo vendido se rechaza (nada queda registrado)
        var exceeds = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new CreateSalesReturnCommand(sale.InvoiceNumber, "Otra vez",
            "EFECTIVO", [new ReturnLineInput("TOR-001", 1)])));
        Assert.Equal("return.exceeds", exceeds.Code);
        Assert.Equal(stockBefore + 1, await host.OnHandAsync("TOR-001"));
        Assert.Equal(1, await host.DbAsync(db => db.Set<SalesReturn>().CountAsync()));

        // La venta con devoluciones no se anula entera con devolución de mercadería
        var reason = await host.DbAsync(db => db.Set<SiatCatalogItem>().Where(i => i.Catalog == SiatCatalogNames.VoidReasons).Select(i => i.Code)
            .FirstAsync());
        var withReturns = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new VoidFiscalDocumentCommand(invoice.Id, reason, true)));
        Assert.Equal("sale.has_returns", withReturns.Code);
    }

    [Fact]
    public async Task Devolucion_con_la_factura_fuera_de_linea_deja_la_nota_para_el_trabajo_automatico()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        using var admin = await host.AdminAsync();
        host.Simulator.Available = false;
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("FER-001", 3));
        await cashier.Send(new DispatchFiscalDocumentsCommand(sale.FiscalDocumentId));   // sin comunicación: re-emitida fuera de línea

        var result = await admin.Send(new CreateSalesReturnCommand(sale.InvoiceNumber, "Producto fallado", "EFECTIVO", [new ReturnLineInput("FER-001", 1)]));
        Assert.Null(result.CreditNoteId);
        Assert.Contains("automáticamente", result.Message, StringComparison.Ordinal);
        Assert.Equal(48.50m, result.Refund);

        // Vuelve la comunicación: recuperación (paquete validado) y en la misma pasada la nota pendiente
        host.Simulator.Available = true;
        host.MoveClockTo(host.Clock.UtcNow.AddMinutes(15));
        var maintenance = await host.WorkerAsync(w => w.MaintainAsync(true));
        Assert.Equal(1, maintenance.Recovered);
        var note = await host.DbAsync(db => db.Set<FiscalDocument>().AsNoTracking().Include(d => d.Lines)
            .SingleAsync(d => d.Kind == FiscalDocumentKind.CreditDebitNote));
        Assert.Equal(FiscalDocumentStatus.Pending, note.Status);
        Assert.Equal(48.50m, note.ReturnedTotal);
        var dispatch = await host.WorkerAsync(w => w.DispatchAsync(null, 50));
        Assert.Equal(1, dispatch.Valid);
        Assert.Equal(FiscalDocumentStatus.Valid, (await host.DocumentAsync(note.Id)).Status);
    }

    [Fact]
    public async Task Devolucion_de_una_venta_con_factura_anulada_se_rechaza()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        using var admin = await host.AdminAsync();
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("FER-002", 2));
        await cashier.Send(new DispatchFiscalDocumentsCommand(sale.FiscalDocumentId));
        var reason = await host.DbAsync(db => db.Set<SiatCatalogItem>().Where(i => i.Catalog == SiatCatalogNames.VoidReasons).Select(i => i.Code)
            .FirstAsync());
        await admin.Send(new VoidFiscalDocumentCommand(sale.FiscalDocumentId!.Value, reason, false));
        var error = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new CreateSalesReturnCommand(sale.InvoiceNumber, "Cambio", "EFECTIVO",
            [new ReturnLineInput("FER-002", 1)])));
        Assert.Equal("return.invoice_voided", error.Code);
    }
}
