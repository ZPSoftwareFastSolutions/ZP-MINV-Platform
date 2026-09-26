using Microsoft.EntityFrameworkCore;
using MINV.Application.Billing;
using MINV.Application.Sales;
using MINV.Domain.Accounting;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>V4.1 · Anulación y reversión (agente E): plazo del día 9 del mes siguiente (hora fiscal), 905/907 del SIN,
/// «anular y devolver» (stock, asiento inverso y venta anulada), reversión una sola vez y re-emisión.</summary>
[Collection(E_BillingCollection.Name)]
public sealed class E_VoidRevertTests
{
    private static async Task<(CheckoutResult Sale, Guid DocumentId)> ValidSaleAsync(E_BillingTestHost host, E_Session cashier, string sku, decimal quantity)
    {
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(email: null), new SaleLineInput(sku, quantity));
        var dispatch = await cashier.Send(new DispatchFiscalDocumentsCommand(sale.FiscalDocumentId));
        Assert.Equal(1, dispatch.Valid);
        return (sale, sale.FiscalDocumentId!.Value);
    }

    [Fact]
    public async Task Anulacion_en_plazo_reversion_una_sola_vez_y_reemision()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        using var admin = await host.AdminAsync();
        var (sale, documentId) = await ValidSaleAsync(host, cashier, "FER-001", 1);
        var reason = await host.DbAsync(db => db.Set<SiatCatalogItem>()
            .Where(i => i.Catalog == SiatCatalogNames.VoidReasons && i.Description == "FACTURA MAL EMITIDA").Select(i => i.Code).FirstAsync());

        // Motivo que no está en el catálogo: rechazo local
        var badReason = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new VoidFiscalDocumentCommand(documentId, 77, false)));
        Assert.Equal("fiscal.void_reason", badReason.Code);

        // 905: anulada en el SIN; la venta sigue vigente (sin devolver mercadería)
        var stock = await host.OnHandAsync("FER-001");
        var voided = await admin.Send(new VoidFiscalDocumentCommand(documentId, reason, false, "Datos del comprador errados"));
        Assert.Contains("anulada en el SIN", voided, StringComparison.Ordinal);
        Assert.Contains("otro medio", voided, StringComparison.Ordinal);   // sin correo del comprador: notificar por otro medio
        var document = await host.DocumentAsync(documentId);
        Assert.Equal(FiscalDocumentStatus.Voided, document.Status);
        Assert.Equal(reason, document.VoidReasonCode);
        Assert.Equal(SiatCodes.VoidConfirmed, document.LastSiatCode);
        Assert.Equal(stock, await host.OnHandAsync("FER-001"));
        Assert.Equal(InvoiceStatus.Issued, await host.DbAsync(db => db.Set<Invoice>().Where(i => i.Number == sale.InvoiceNumber).Select(i => i.Status)
            .FirstAsync()));
        var status = await admin.Send(new CheckFiscalDocumentStatusCommand(documentId));
        Assert.Contains("ANULADO", status, StringComparison.Ordinal);

        // Reversión: 907 → válida otra vez y ya no se puede anular; la segunda reversión se rechaza
        var reverted = await admin.Send(new RevertFiscalVoidCommand(documentId));
        Assert.Contains("revirtió", reverted, StringComparison.Ordinal);
        document = await host.DocumentAsync(documentId);
        Assert.Equal(FiscalDocumentStatus.Valid, document.Status);
        Assert.True(document.IsReverted);
        var twice = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new RevertFiscalVoidCommand(documentId)));
        Assert.Equal("fiscal.revert_state", twice.Code);
        var voidAgain = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new VoidFiscalDocumentCommand(documentId, reason, false)));
        Assert.Equal("fiscal.void_reverted", voidAgain.Code);

        // Otra venta: anulación sin devolver y re-emisión con el comprador corregido (documento nuevo que reemplaza)
        var (_, secondId) = await ValidSaleAsync(host, cashier, "FER-002", 2);
        await admin.Send(new VoidFiscalDocumentCommand(secondId, reason, false));
        var corrected = new FiscalBuyerInput(SiatCodes.DocumentNit, "1020304050", null, "EMPRESA CORRECTA S.A.", null);
        var reissue = await cashier.Send(new ReissueFiscalDocumentCommand(secondId, corrected));
        Assert.Equal(FiscalDocumentStatus.Pending, reissue.Status);
        var replacement = await host.DocumentAsync(reissue.Id);
        Assert.Equal(secondId, replacement.ReplacesDocumentId);
        Assert.Equal("1020304050", replacement.BuyerDocumentNumber);
        Assert.Equal((await host.DocumentAsync(secondId)).TotalAmount, replacement.TotalAmount);
        var duplicate = await Assert.ThrowsAsync<DomainException>(() => cashier.Send(new ReissueFiscalDocumentCommand(secondId, null)));
        Assert.Equal("fiscal.reissue_active", duplicate.Code);
        Assert.Equal(1, (await cashier.Send(new DispatchFiscalDocumentsCommand(reissue.Id))).Valid);
        // Revertir la anulación del primero ya no es posible: la venta tiene otro documento vigente
        var replaced = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new RevertFiscalVoidCommand(secondId)));
        Assert.Equal("fiscal.revert_replaced", replaced.Code);
    }

    [Fact]
    public async Task Anular_y_devolver_repone_el_stock_con_asiento_inverso_y_no_admite_reversion()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        using var admin = await host.AdminAsync();
        var stockBefore = await host.OnHandAsync("AMO-001");
        var (sale, documentId) = await ValidSaleAsync(host, cashier, "AMO-001", 2);
        Assert.Equal(stockBefore - 2, await host.OnHandAsync("AMO-001"));
        var reason = await host.DbAsync(db => db.Set<SiatCatalogItem>()
            .Where(i => i.Catalog == SiatCatalogNames.VoidReasons).OrderBy(i => i.Code).Select(i => i.Code).FirstAsync());

        var result = await admin.Send(new VoidFiscalDocumentCommand(documentId, reason, true));
        Assert.Contains("volvió al stock", result, StringComparison.Ordinal);
        Assert.Equal(FiscalDocumentStatus.Voided, (await host.DocumentAsync(documentId)).Status);
        Assert.Equal(stockBefore, await host.OnHandAsync("AMO-001"));
        var invoice = await host.DbAsync(db => db.Set<Invoice>().AsNoTracking().FirstAsync(i => i.Number == sale.InvoiceNumber));
        Assert.Equal(InvoiceStatus.Voided, invoice.Status);
        var reversal = await host.DbAsync(db => db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines)
            .FirstAsync(e => e.Description.StartsWith("Anulación de la venta " + sale.InvoiceNumber)));
        Assert.Equal(reversal.TotalDebit, reversal.TotalCredit);
        var cash = await host.DbAsync(db => db.Set<Account>().Where(a => a.Code == AccountCodes.Cash).Select(a => a.Id).FirstAsync());
        Assert.Contains(reversal.Lines, l => l.AccountId == cash && l.Credit == sale.Total);   // Haber Caja: el efectivo cobrado

        var revert = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new RevertFiscalVoidCommand(documentId)));
        Assert.Equal("fiscal.revert_goods_returned", revert.Code);
        var reissue = await Assert.ThrowsAsync<DomainException>(() => cashier.Send(new ReissueFiscalDocumentCommand(documentId, null)));
        Assert.Equal("fiscal.reissue_goods_returned", reissue.Code);
    }

    [Fact]
    public async Task Fuera_de_plazo_la_anulacion_se_rechaza_localmente()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        using var admin = await host.AdminAsync();
        var (_, documentId) = await ValidSaleAsync(host, cashier, "FER-001", 1);
        var document = await host.DocumentAsync(documentId);
        var deadline = FiscalRules.VoidDeadline(document.IssuedAt);
        Assert.Equal(new DateTime(2026, 10, 9, 23, 59, 59), deadline.AddTicks(1).AddSeconds(-1));

        // Día 10 del mes siguiente (hora de Bolivia): el SIN respondería 934; M-INV lo rechaza sin llamar
        host.MoveClockTo(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(-4)));
        var calls = await host.DbAsync(db => db.Set<SiatServiceCall>().CountAsync());
        var late = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new VoidFiscalDocumentCommand(documentId, 1, false)));
        Assert.Equal("fiscal.void_deadline", late.Code);
        Assert.Contains("día 9", late.Message, StringComparison.Ordinal);
        Assert.Equal(calls, await host.DbAsync(db => db.Set<SiatServiceCall>().CountAsync()));
        Assert.Equal(FiscalDocumentStatus.Valid, (await host.DocumentAsync(documentId)).Status);
    }
}
