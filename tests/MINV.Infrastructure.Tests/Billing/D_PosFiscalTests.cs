using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Application.Partners;
using MINV.Application.Sales;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>
/// V4.1 · Lo que ve la caja: estado fiscal del punto de venta del usuario (módulo, activación, turno, punto, CUIS, CUFD y
/// modo), comprador por documento con su última verificación de NIT, devoluciones con su nota y lo que falta devolver.
/// </summary>
public sealed class D_PosFiscalTests
{
    [Fact]
    public async Task Estado_fiscal_de_la_caja_paso_a_paso()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();

        var none = await admin.Send(new GetPosFiscalStateQuery());
        Assert.False(none.BillingEnabled || none.Ready);
        Assert.Contains("no está configurada", none.Message, StringComparison.Ordinal);

        await D_BillingTestHost.ConfigureAsync(admin, enable: false, elAlto: false);
        Assert.Contains("desactivada", (await admin.Send(new GetPosFiscalStateQuery())).Message, StringComparison.Ordinal);
        await admin.Send(new SaveSiatSettingsCommand(D_BillingTestHost.Nit, D_BillingTestHost.CompanyName, D_BillingTestHost.SystemCode,
            SiatCodes.EnvironmentTest, null, null, true));
        var noSession = await admin.Send(new GetPosFiscalStateQuery());
        Assert.True(noSession.BillingEnabled);
        Assert.False(noSession.Ready);
        Assert.Equal("Abra un turno de caja para facturar.", noSession.Message);
        Assert.Equal(5, noSession.DocumentTypes.Count);

        // Con turno: la caja sin punto propio factura con el punto 0 de la sucursal
        await admin.Send(new OpenPosSessionCommand("CAJA01", 100m));
        var zero = await admin.Send(new GetPosFiscalStateQuery());
        Assert.True(zero is { Ready: true, Mode: SiatConnectionMode.Online, PointOfSaleCode: 0 });
        Assert.StartsWith("✔ Facturación en línea · punto de venta 0", zero.Message, StringComparison.Ordinal);

        // Punto de venta propio de la caja
        var point = await admin.Send(new RegisterSiatPointOfSaleCommand("CM", "Caja 1", null, "CAJA01"));
        var linked = await admin.Send(new GetPosFiscalStateQuery());
        Assert.Equal(1, linked.PointOfSaleCode);

        // Productos sin homologar: se avisa
        await D_BillingTestHost.CreateProductAsync(admin, "FER-001", "Juego de destornilladores", stock: 0);
        var pending = await admin.Send(new GetPosFiscalStateQuery());
        Assert.Equal(1, pending.PendingHomologation);
        Assert.Contains("1 producto(s) sin homologar", pending.Message, StringComparison.Ordinal);

        // Fuera de línea: sigue facturando (tipo de emisión 2)
        await ChangePointAsync(admin, point.Id, p => p.GoOffline(host.Clock.UtcNow));
        var offline = await admin.Send(new GetPosFiscalStateQuery());
        Assert.True(offline is { Ready: true, Mode: SiatConnectionMode.Offline });
        Assert.StartsWith("⚠ Fuera de línea", offline.Message, StringComparison.Ordinal);

        // Contingencia manual: la caja no emite (facturas del talonario CAFC)
        await ChangePointAsync(admin, point.Id, p => p.BackOnline(host.Clock.UtcNow));
        await ChangePointAsync(admin, point.Id, p => p.StartManualContingency(host.Clock.UtcNow));
        var manual = await admin.Send(new GetPosFiscalStateQuery());
        Assert.True(manual is { Ready: false, Mode: SiatConnectionMode.ManualContingency });
        Assert.Contains("CAFC", manual.Message, StringComparison.Ordinal);

        // CUFD vencido: dentro de las 72 h desde su obtención todavía se puede facturar fuera de línea; después, no
        await ChangePointAsync(admin, point.Id, p => p.BackOnline(host.Clock.UtcNow));
        host.Clock.StartAt(host.Clock.UtcNow.AddHours(30));
        var expired = await admin.Send(new GetPosFiscalStateQuery());
        Assert.True(expired.Ready);
        Assert.StartsWith("⚠ El CUFD del punto de venta 1 venció", expired.Message, StringComparison.Ordinal);
        host.Clock.StartAt(host.Clock.UtcNow.AddHours(50));
        var tooOld = await admin.Send(new GetPosFiscalStateQuery());
        Assert.False(tooOld.Ready);
        Assert.Contains("no tiene CUFD del día", tooOld.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Comprador_por_documento_devoluciones_y_lineas_devolvibles()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.ConfigureAsync(admin, enable: false, elAlto: false);

        // Cliente anterior a la V4.1 (solo el número) y cliente con tipo de documento y complemento
        var company = await admin.Send(new SaveCustomerCommand(null, "Constructora Andina S.A.", "1020703023", "compras@andina.example", null, "GENERAL", true));
        var person = await admin.Send(new SaveCustomerCommand(null, "Juan Pérez", "5115889", null, null, "GENERAL", true));
        var entity = await admin.Db.Set<Customer>().FirstAsync(c => c.Code == person);
        entity.SetFiscalIdentity(SiatCodes.DocumentCi, "5115889", "1a");
        await admin.Db.SaveChangesAsync();

        var byNit = await admin.Send(new FindFiscalBuyerQuery(SiatCodes.DocumentNit, "1020703023"));
        Assert.True(byNit is { Found: true, Name: "Constructora Andina S.A.", Email: "compras@andina.example", NitValid: null });
        Assert.Equal(company, byNit.CustomerCode);
        await admin.Send(new VerifyNitCommand(1020703023, company));
        Assert.True((await admin.Send(new FindFiscalBuyerQuery(SiatCodes.DocumentNit, "1020703023"))).NitValid);
        var byCi = await admin.Send(new FindFiscalBuyerQuery(SiatCodes.DocumentCi, "5115889", "1A"));
        Assert.True(byCi is { Found: true, Name: "Juan Pérez", Complement: "1A" });
        Assert.False((await admin.Send(new FindFiscalBuyerQuery(SiatCodes.DocumentCi, "5115889"))).Found);   // sin complemento es otra persona
        Assert.False((await admin.Send(new FindFiscalBuyerQuery(SiatCodes.DocumentCi, "999999"))).Found);
        Assert.Equal("buyer.doc_numeric", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new FindFiscalBuyerQuery(SiatCodes.DocumentNit, "12AB")))).Code);

        // Venta de 3 unidades y una devolución de 1 con su nota crédito-débito
        await D_BillingTestHost.CreateProductAsync(admin, "FER-001", "Juego de destornilladores (6 piezas)");
        await D_BillingTestHost.CreateProductAsync(admin, "FER-002", "Tornillo drywall");
        var sale = await D_BillingTestHost.SellAsync(admin, new SaleLineInput("FER-001", 3, 10), new SaleLineInput("FER-002", 1));
        var returnable = await admin.Send(new GetReturnableLinesQuery(sale.InvoiceNumber.ToLowerInvariant()));
        Assert.Equal(2, returnable.Count);
        Assert.Equal((3m, 0m, 45m, 10m), (returnable[0].Sold, returnable[0].Returned, returnable[0].UnitPrice, returnable[0].DiscountPercent));

        var db = admin.Db;
        var invoice = await db.Set<Invoice>().SingleAsync(i => i.Number == sale.InvoiceNumber);
        var order = await db.Set<SalesOrder>().Include(o => o.Lines).SingleAsync(o => o.Id == invoice.SalesOrderId);
        var customer = await db.Set<Customer>().SingleAsync(c => c.Code == "CF");
        var cash = await db.Set<PaymentMethod>().SingleAsync(m => m.Code == "EFECTIVO");
        var variant = await db.Set<MINV.Domain.Catalog.ProductVariant>().SingleAsync(v => v.Sku == "FER-001");
        var now = admin.Services.GetRequiredService<IClock>().UtcNow;
        var salesReturn = new SalesReturn(invoice.TenantId, invoice.BranchId, "DV-CM-000001", invoice.Id, customer.Id, "Producto con falla", cash.Id, null,
            admin.Login.UserId, now);
        salesReturn.AddLine(order.Lines.Single(l => l.VariantId == variant.Id), 1, 0);
        db.Set<SalesReturn>().Add(salesReturn);
        await db.SaveChangesAsync();
        var cm = await D_Documents.PlaceAsync(admin);
        var original = await D_Documents.InvoiceAsync(admin, cm, 1, [D_Documents.Line("FER-001", "Juego de destornilladores (6 piezas)", 3, 40.50m)],
            invoice.Id);
        await D_Documents.CreditNoteAsync(admin, cm, 1, original,
        [
            D_Documents.Line("FER-001", "Juego de destornilladores (6 piezas)", 3, 40.50m, tx: 1),
            D_Documents.Line("FER-001", "Juego de destornilladores (6 piezas)", 1, 40.50m, tx: 2),
        ], salesReturn.Id);

        returnable = await admin.Send(new GetReturnableLinesQuery(sale.InvoiceNumber));
        Assert.Equal((3m, 1m), (returnable.Single(l => l.Sku == "FER-001").Sold, returnable.Single(l => l.Sku == "FER-001").Returned));
        Assert.Equal(0m, returnable.Single(l => l.Sku == "FER-002").Returned);
        var today = DateOnly.FromDateTime(D_Documents.FiscalNow(admin));
        var returns = await admin.Send(new GetSalesReturnsQuery(today, today));
        var row = Assert.Single(returns);
        Assert.Equal(("DV-CM-000001", sale.InvoiceNumber, "Consumidor final", "Producto con falla"), (row.Number, row.InvoiceNumber, row.Customer, row.Reason));
        Assert.Equal(40.50m, row.Refund);   // 1 × 45 × (1 − 10 %)
        Assert.Equal(("Nota N° 1", FiscalDocumentStatus.Valid), (row.CreditNote, row.CreditNoteStatus));
        Assert.Empty(await admin.Send(new GetSalesReturnsQuery(today.AddDays(-5), today.AddDays(-1))));
        await Assert.ThrowsAsync<NotFoundException>(() => admin.Send(new GetReturnableLinesQuery("F-CM-999999")));

        // Venta anulada en M-INV (su mercadería ya volvió): no admite devoluciones
        var other = await D_BillingTestHost.SellAsync(admin, new SaleLineInput("FER-002", 1));
        await admin.Send(new VoidSaleCommand(other.InvoiceNumber, "Error de caja"));
        Assert.Equal("return.invoice_voided", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new GetReturnableLinesQuery(other.InvoiceNumber)))).Code);
    }

    private static async Task ChangePointAsync(D_Session admin, Guid pointId, Action<SiatPointOfSale> change)
    {
        admin.Db.ClearTracking();
        var point = await admin.Db.Set<SiatPointOfSale>().SingleAsync(p => p.Id == pointId);
        change(point);
        await admin.Db.SaveChangesAsync();
        admin.Db.ClearTracking();
    }
}
