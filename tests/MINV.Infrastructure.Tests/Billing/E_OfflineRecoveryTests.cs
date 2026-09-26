using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Sales;
using MINV.Domain.Billing;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>V4.1 · Fuera de línea y recuperación (agente E): sin comunicación el documento queda sin respuesta, la venta se
/// re-emite fuera de línea (C-07) y el punto pasa a fuera de línea con un evento abierto; al volver la comunicación: CUFD
/// nuevo → evento registrado → verificación de los sin respuesta → paquete → validación → en línea.</summary>
[Collection(E_BillingCollection.Name)]
public sealed class E_OfflineRecoveryTests
{
    [Fact]
    public async Task Sin_comunicacion_la_venta_se_reemite_fuera_de_linea_y_al_recuperar_todo_queda_valido()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("FER-001", 2));
        var originalId = sale.FiscalDocumentId!.Value;

        // (c) SIN apagado: dos intentos fallidos → sin respuesta + re-emisión fuera de línea + punto fuera de línea + evento
        host.Simulator.Available = false;
        var dispatch = await cashier.Send(new DispatchFiscalDocumentsCommand(originalId));
        Assert.Equal(1, dispatch.WentOffline);
        Assert.Equal(0, dispatch.Valid);
        var row = Assert.Single(dispatch.Documents);
        Assert.NotEqual(originalId, row.Id);
        Assert.Equal(FiscalDocumentStatus.Offline, row.Status);
        Assert.Equal(SiatCodes.EmissionOffline, row.EmissionType);
        var original = await host.DocumentAsync(originalId);
        Assert.Equal(FiscalDocumentStatus.NoResponse, original.Status);
        var reissued = await host.DocumentAsync(row.Id);
        Assert.Equal(originalId, reissued.ReplacesDocumentId);
        Assert.NotEqual(original.Number, reissued.Number);
        Assert.NotEqual(original.Cuf, reissued.Cuf);
        Assert.Equal(original.InvoiceId, reissued.InvoiceId);
        Assert.Equal(original.TotalAmount, reissued.TotalAmount);
        Assert.Equal(original.BuyerDocumentNumber, reissued.BuyerDocumentNumber);
        Assert.Equal(original.CufdId, reissued.CufdId);   // el CUFD del evento es el del documento
        var point = await host.PointAsync(host.CashPointId);
        Assert.Equal(SiatConnectionMode.Offline, point.Mode);
        var evt = await host.DbAsync(db => db.Set<SignificantEvent>().AsNoTracking().SingleAsync(e => e.PointOfSaleId == host.CashPointId));
        Assert.Equal(SignificantEventStatus.Open, evt.Status);
        Assert.Equal(SignificantEventKind.Offline, evt.Kind);
        Assert.Equal(original.CufdId, evt.EventCufdId);
        Assert.Equal(evt.Id, reissued.SignificantEventId);
        Assert.Contains("INACCESIBILIDAD", FiscalIssuer.Plain(evt.Description), StringComparison.Ordinal);

        // Los cobros siguientes salen fuera de línea con el CUFD del evento; con NIT, código de excepción 1
        var offlineSale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.NitBuyer(), new SaleLineInput("FER-002", 1));
        Assert.Equal(FiscalDocumentStatus.Offline, offlineSale.FiscalStatus);
        var offline = await host.DocumentAsync(offlineSale.FiscalDocumentId!.Value);
        Assert.Equal(SiatCodes.EmissionOffline, offline.EmissionType);
        Assert.Equal(1, offline.ExceptionCode);
        Assert.Equal(evt.Id, offline.SignificantEventId);
        Assert.Equal(evt.EventCufdId, offline.CufdId);
        var eventCufd = await host.DbAsync(db => db.Set<SiatCufd>().AsNoTracking().FirstAsync(c => c.Id == evt.EventCufdId));
        Assert.EndsWith(eventCufd.ControlCode, offline.Cuf, StringComparison.Ordinal);
        var untouched = await cashier.Send(new DispatchFiscalDocumentsCommand(offline.Id));
        Assert.Equal(0, untouched.Sent);   // los fuera de línea van en paquete, no uno por uno
        Assert.Equal(FiscalDocumentStatus.Offline, Assert.Single(untouched.Documents).Status);

        // Mientras siga sin comunicación, el mantenimiento solo reintenta más tarde
        var stillDown = await host.WorkerAsync(w => w.MaintainAsync(true));
        Assert.Equal(0, stillDown.Recovered);
        Assert.Equal(SiatConnectionMode.Offline, (await host.PointAsync(host.CashPointId)).Mode);

        // (d) SIN encendido: recuperación completa en el mantenimiento
        host.Simulator.Available = true;
        host.MoveClockTo(host.Clock.UtcNow.AddMinutes(20));
        var recovered = await host.WorkerAsync(w => w.MaintainAsync(true));
        Assert.Equal(1, recovered.Recovered);
        Assert.Equal(2, recovered.DocumentsSent);
        Assert.Equal(1, recovered.PackagesValidated);
        Assert.Equal(SiatConnectionMode.Online, (await host.PointAsync(host.CashPointId)).Mode);
        var finalEvent = await host.DbAsync(db => db.Set<SignificantEvent>().AsNoTracking().SingleAsync(e => e.Id == evt.Id));
        Assert.Equal(SignificantEventStatus.Reconciled, finalEvent.Status);
        Assert.NotNull(finalEvent.ReceptionCode);
        Assert.NotNull(finalEvent.EndedAt);
        Assert.NotEqual(finalEvent.EventCufdId, finalEvent.SendCufdId);   // CUFD NUEVO para registrar el evento (C-18)
        Assert.True(finalEvent.EndedAt >= offline.IssuedAt);
        Assert.Equal(FiscalDocumentStatus.Valid, (await host.DocumentAsync(reissued.Id)).Status);
        Assert.Equal(FiscalDocumentStatus.Valid, (await host.DocumentAsync(offline.Id)).Status);
        Assert.Equal(FiscalDocumentStatus.Discarded, (await host.DocumentAsync(originalId)).Status);   // nunca llegó al SIN
        var package = await host.DbAsync(db => db.Set<FiscalPackage>().AsNoTracking().SingleAsync());
        Assert.Equal(FiscalPackageStatus.Validated, package.Status);
        Assert.Null(package.Cafc);
        Assert.Equal(new[] { 1, 2 }, await host.DbAsync(db => db.Set<FiscalDocument>().Where(d => d.PackageId == package.Id)
            .OrderBy(d => d.PackagePosition).Select(d => d.PackagePosition!.Value).ToListAsync()));

        // De nuevo en línea: el próximo cobro sale en línea con el CUFD nuevo
        var online = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("FER-001", 1));
        Assert.Equal(FiscalDocumentStatus.Pending, online.FiscalStatus);
        Assert.Equal(finalEvent.SendCufdId, (await host.DocumentAsync(online.FiscalDocumentId!.Value)).CufdId);
    }

    [Fact]
    public async Task El_original_que_si_llego_al_SIN_queda_como_duplicado_y_se_anula_al_recuperar()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("TOR-001", 1));
        var originalId = sale.FiscalDocumentId!.Value;

        // El envío llegó al SIN pero la respuesta se perdió: se simula enviándolo directo por el puerto sin registrar la respuesta
        using (var scope = host.PlatformScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<IMinvDbContext>();
            var serializer = sp.GetRequiredService<IFiscalDocumentSerializer>();
            var lookups = new BillingLookups(db, sp.GetRequiredService<ISecretProtector>(), sp.GetRequiredService<IClock>());
            var context = await lookups.ContextAsync(default);
            var document = await db.Set<FiscalDocument>().FirstAsync(d => d.Id == originalId);
            var xml = await db.Set<FiscalDocumentFile>().Where(f => f.DocumentId == originalId).Select(f => f.Xml).FirstAsync();
            var cuis = await db.Set<SiatCuis>().FirstAsync(c => c.Id == document.CuisId);
            var cufd = await db.Set<SiatCufd>().FirstAsync(c => c.Id == document.CufdId);
            var gzip = serializer.Gzip(xml);
            var reply = await sp.GetRequiredService<ISiatGateway>().SendDocumentAsync(context.Connection, BillingLookups.PlaceOf(document),
                new SiatCodesForCall(cuis.Code, cufd.Code), SiatDocumentRef.PurchaseSale, gzip, serializer.Sha256Hex(gzip), host.FiscalNow);
            Assert.True(reply.Has(SiatCodes.ReceptionValidated));
        }

        host.Simulator.Available = false;
        var dispatch = await cashier.Send(new DispatchFiscalDocumentsCommand(originalId));
        Assert.Equal(1, dispatch.WentOffline);
        var reissuedId = Assert.Single(dispatch.Documents).Id;

        host.Simulator.Available = true;
        host.MoveClockTo(host.Clock.UtcNow.AddMinutes(10));
        var recovered = await host.WorkerAsync(w => w.MaintainAsync(true));
        Assert.Equal(1, recovered.Recovered);
        var original = await host.DocumentAsync(originalId);
        Assert.Equal(FiscalDocumentStatus.Voided, original.Status);   // estaba registrado y re-emitido: duplicado anulado (C-07)
        Assert.NotNull(original.VoidReasonCode);
        var reasons = await host.DbAsync(db => db.Set<SiatCatalogItem>().Where(i => i.Catalog == SiatCatalogNames.VoidReasons)
            .ToDictionaryAsync(i => i.Code, i => i.Description));
        Assert.Equal("FACTURA MAL EMITIDA", reasons[original.VoidReasonCode!.Value]);
        var actions = await host.DbAsync(db => db.Set<FiscalDocumentEvent>().Where(e => e.DocumentId == originalId).Select(e => e.Action).ToListAsync());
        Assert.Contains(FiscalDocumentAction.StatusChecked, actions);
        Assert.Contains(FiscalDocumentAction.Voided, actions);
        Assert.Equal(FiscalDocumentStatus.Valid, (await host.DocumentAsync(reissuedId)).Status);
    }

    [Fact]
    public async Task Sin_CUFD_vigente_la_caja_abre_un_evento_automatico_y_emite_con_el_ultimo_CUFD()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        // El CUFD del día venció (25 h después) pero el último sigue usable (≤ 72 h): fuera de línea automático
        host.MoveClockTo(host.Clock.UtcNow.AddHours(25));
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("FER-001", 1));
        Assert.Equal(FiscalDocumentStatus.Offline, sale.FiscalStatus);
        Assert.Equal(SiatConnectionMode.Offline, (await host.PointAsync(host.CashPointId)).Mode);
        var evt = await host.DbAsync(db => db.Set<SignificantEvent>().AsNoTracking().SingleAsync(e => e.PointOfSaleId == host.CashPointId));
        Assert.Equal(SignificantEventStatus.Open, evt.Status);

        // Más de 72 h sin CUFD usable: no se puede facturar
        host.MoveClockTo(host.Clock.UtcNow.AddHours(50));
        var noCufd = await Assert.ThrowsAsync<Domain.Common.DomainException>(() =>
            E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("FER-001", 1)));
        Assert.Equal("siat.no_cufd", noCufd.Code);
    }
}
