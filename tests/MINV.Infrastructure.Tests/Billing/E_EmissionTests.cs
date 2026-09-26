using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Integration;
using MINV.Application.Sales;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>V4.1 · Emisión al vender (agente E): factura en la MISMA transacción de la venta (sin llamar al SIN), envío
/// después del COMMIT, comprador por documento, homologación y decimales.</summary>
[Collection(E_BillingCollection.Name)]
public sealed class E_EmissionTests
{
    [Fact]
    public async Task Cobro_en_linea_queda_pendiente_y_el_despacho_lo_valida_con_el_total_cobrado()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        var inSiat = host.Simulator.Status().Documents;   // el simulador es compartido: se cuenta la diferencia
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(email: "juan@cliente.example"),
            new SaleLineInput("FER-001", 2), new SaleLineInput("FER-002", 3, 10), new SaleLineInput("ELE-001", 12.5m));

        // La venta nace con su documento PENDIENTE (en línea) y el SIN todavía no sabe nada (regla F-03)
        Assert.NotNull(sale.FiscalDocumentId);
        Assert.Equal(FiscalDocumentStatus.Pending, sale.FiscalStatus);
        Assert.Equal(1, sale.FiscalNumber);
        Assert.Equal(inSiat, host.Simulator.Status().Documents);
        var pending = await host.DocumentAsync(sale.FiscalDocumentId!.Value);
        Assert.Equal(SiatCodes.EmissionOnline, pending.EmissionType);
        Assert.Equal(host.CashPointId, pending.PointOfSaleId);
        Assert.Equal(sale.Total, pending.TotalAmount);   // F-06: total fiscal = total cobrado (con el descuento del 10 %)
        Assert.Equal(3, pending.Lines.Count);
        var discounted = pending.Lines.Single(l => l.ProductCode == "FER-002");
        Assert.Equal(FiscalRules.Round2(3 * 18.90m) - FiscalRules.Round2(3 * 18.90m * 0.9m), discounted.Discount);
        Assert.Equal(sale.Cuf, pending.Cuf);

        // El XML guardado valida contra el XSD oficial y su huella es la del GZIP
        var (xml, sha) = await host.DbAsync(async db => await db.Set<FiscalDocumentFile>().Where(f => f.DocumentId == pending.Id)
            .Select(f => new ValueTuple<string, string>(f.Xml, f.GzipSha256)).FirstAsync());
        var serializer = host.Services.GetRequiredService<IFiscalDocumentSerializer>();
        serializer.Validate(xml, SiatCodes.SectorPurchaseSale);
        Assert.Equal(serializer.Sha256Hex(serializer.Gzip(xml)), sha);
        Assert.Contains("<cuf>" + pending.Cuf + "</cuf>", xml, StringComparison.Ordinal);

        // La caja despacha su documento: 908 → válido
        var dispatch = await cashier.Send(new DispatchFiscalDocumentsCommand(pending.Id));
        Assert.Equal(1, dispatch.Sent);
        Assert.Equal(1, dispatch.Valid);
        var row = Assert.Single(dispatch.Documents);
        Assert.Equal(FiscalDocumentStatus.Valid, row.Status);
        Assert.Equal(sale.Total, row.Total);
        Assert.Equal(sale.InvoiceNumber, row.SaleNumber);
        Assert.True(row.CanVoid);
        Assert.True(row.CanCreditNote);
        var valid = await host.DocumentAsync(pending.Id);
        Assert.Equal(FiscalDocumentStatus.Valid, valid.Status);
        Assert.Equal(SiatCodes.ReceptionValidated, valid.LastSiatCode);
        Assert.NotNull(valid.ReceptionCode);
        Assert.Equal(inSiat + 1, host.Simulator.Status().Documents);
        var actions = await host.DbAsync(db => db.Set<FiscalDocumentEvent>().Where(e => e.DocumentId == pending.Id).Select(e => e.Action).ToListAsync());
        Assert.Contains(FiscalDocumentAction.Issued, actions);
        Assert.Contains(FiscalDocumentAction.Accepted, actions);

        // Un segundo envío no hace nada (ya no está pendiente) y el siguiente cobro numera 2
        var again = await cashier.Send(new DispatchFiscalDocumentsCommand(pending.Id));
        Assert.Equal(0, again.Sent);
        var second = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("FER-001", 1));
        Assert.Equal(2, second.FiscalNumber);
    }

    [Fact]
    public async Task Comprador_nuevo_se_registra_y_la_homologacion_y_los_decimales_se_controlan()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        var customersBefore = await host.DbAsync(db => db.Set<Customer>().CountAsync());

        // Comprador con NIT que no es cliente: se crea el cliente con su identidad fiscal
        var first = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.NitBuyer(), new SaleLineInput("FER-001", 1));
        var created = await host.DbAsync(db => db.Set<Customer>().AsNoTracking().FirstAsync(c => c.Code == "NIT-1234567019"));
        Assert.Equal(SiatCodes.DocumentNit, created.DocumentType);
        Assert.Equal("1234567019", created.TaxId);
        Assert.Equal("CONSTRUCTORA ANDINA S.R.L.", created.Name);
        var document = await host.DocumentAsync(first.FiscalDocumentId!.Value);
        Assert.Equal(created.Id, document.CustomerId);
        Assert.Equal("NIT-1234567019", document.CustomerCode);
        Assert.Equal(SiatCodes.DocumentNit, document.BuyerDocumentType);
        Assert.Equal(0, document.ExceptionCode);   // en línea el NIT se valida (sin excepción)

        // El mismo documento otra vez: usa ese cliente (no crea otro)
        await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.NitBuyer(name: null), new SaleLineInput("FER-001", 1));
        Assert.Equal(customersBefore + 1, await host.DbAsync(db => db.Set<Customer>().CountAsync()));

        // Consumidor final sin documento: la factura es nominativa (II-1)
        var noBuyer = await Assert.ThrowsAsync<DomainException>(() => E_BillingTestHost.SellAsync(cashier, null, new SaleLineInput("FER-001", 1)));
        Assert.Equal("fiscal.buyer_required", noBuyer.Code);

        // Producto sin homologar: rechazo con la lista de lo que falta (y la venta no se registra)
        await host.DbAsync(async db =>
        {
            var variant = await db.Set<Domain.Catalog.ProductVariant>().FirstAsync(v => v.Sku == "FER-002");
            db.Remove(await db.Set<ProductSiatCode>().FirstAsync(p => p.ProductId == variant.ProductId));
            return await db.SaveChangesAsync();
        });
        var stockBefore = await host.OnHandAsync("FER-002");
        var notHomologated = await Assert.ThrowsAsync<DomainException>(() =>
            E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("FER-001", 1), new SaleLineInput("FER-002", 1)));
        Assert.Equal("fiscal.not_homologated", notHomologated.Code);
        Assert.Contains("FER-002", notHomologated.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("FER-001", notHomologated.Message, StringComparison.Ordinal);
        Assert.Equal(stockBefore, await host.OnHandAsync("FER-002"));

        // Cantidad con 3 decimales: la factura del sector 1 admite 2
        var decimals = await Assert.ThrowsAsync<DomainException>(() =>
            E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("ELE-001", 1.255m)));
        Assert.Equal("fiscal.quantity_decimals", decimals.Code);
        Assert.Contains("2 decimales", decimals.Message, StringComparison.Ordinal);

        // NIT especial de ventas menores: tipo NIT y excepción 1
        var minor = await E_BillingTestHost.SellAsync(cashier, new FiscalBuyerInput(SiatCodes.DocumentNit, SiatCodes.SpecialMinorSales, null, null, null),
            new SaleLineInput("FER-001", 1));
        Assert.Equal(1, (await host.DocumentAsync(minor.FiscalDocumentId!.Value)).ExceptionCode);
    }

    [Fact]
    public async Task Pedido_externo_con_comprador_factura_en_el_punto_cero_y_la_repeticion_devuelve_el_mismo_CUF()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var admin = await host.AdminAsync();
        var order = new CreateExternalOrderCommand("WEB-0001", "CF", "QR", [new SaleLineInput("FER-001", 1), new SaleLineInput("TOR-001", 2)],
            "QR-778899", null, E_BillingTestHost.Ci("7788990", "Ana Rojas", "ana@cliente.example"));
        var result = await admin.Send(order);
        Assert.False(result.Replayed);
        Assert.NotNull(result.Cuf);
        Assert.Equal(1, result.FiscalNumber);
        var document = await host.DbAsync(db => db.Set<FiscalDocument>().AsNoTracking().FirstAsync(d => d.Cuf == result.Cuf));
        Assert.Equal(host.PointZeroId, document.PointOfSaleId);   // la API factura con el punto 0 de la sucursal
        Assert.Equal("ana@cliente.example", document.BuyerEmail);
        var replay = await admin.Send(order);
        Assert.True(replay.Replayed);
        Assert.Equal(result.Cuf, replay.Cuf);
        Assert.Equal(result.FiscalNumber, replay.FiscalNumber);
        // El mismo externalId con otro comprador es OTRO contenido (el hash incluye al comprador si viene)
        await Assert.ThrowsAsync<Application.Common.IdempotencyConflictException>(() =>
            admin.Send(order with { Buyer = E_BillingTestHost.Ci("1111111") }));
        // Sin comprador el hash es el de la V4 (compatibilidad)
        Assert.NotEqual(CreateExternalOrderHandler.RequestHash(order), CreateExternalOrderHandler.RequestHash(order with { Buyer = null }));
    }

    [Fact]
    public async Task Anular_la_venta_con_factura_activa_se_rechaza_y_se_indica_la_anulacion_fiscal()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("FER-001", 1));
        var error = await Assert.ThrowsAsync<DomainException>(() => cashier.Send(new VoidSaleCommand(sale.InvoiceNumber, "Error de cobro")));
        Assert.Equal("sale.has_fiscal_document", error.Code);
        Assert.Contains("Documentos fiscales", error.Message, StringComparison.Ordinal);
    }
}
