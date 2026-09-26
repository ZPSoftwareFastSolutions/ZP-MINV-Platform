using Microsoft.EntityFrameworkCore;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Application.Sales;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>V4.1 · Contingencia (agente E): contingencia manual con talonario CAFC (inicio, transcripción con número y
/// fecha, fin con recuperación y paquete con el CAFC), paso manual a fuera de línea y el trabajo automático por la tubería
/// de MediatR con un usuario cajero.</summary>
[Collection(E_BillingCollection.Name)]
public sealed class E_ContingencyTests
{
    private const string CafcCode = "CAFC-E-0001";

    private static Task RegisterCafcAsync(E_BillingTestHost host) => host.DbAsync(async db =>
    {
        db.Add(new ContingencyCode(host.TenantId, host.BranchId, SiatCodes.SectorPurchaseSale, CafcCode, 1001, 1100, null));
        return await db.SaveChangesAsync();
    });

    [Fact]
    public async Task Contingencia_manual_CAFC_transcripcion_y_paquete_validado()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        await RegisterCafcAsync(host);
        using var admin = await host.AdminAsync();
        using var cashier = await host.CashierAsync();
        var energy = await host.EventCodeAsync("ENERGIA");
        var internet = await host.EventCodeAsync("CORTE DEL SERVICIO DE INTERNET");
        var start = host.FiscalNow.AddMinutes(-30);

        // Un evento automático (internet) no es contingencia manual; un CAFC que no existe tampoco sirve
        var notManual = await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new StartManualContingencyCommand(host.CashPointId, internet, null, start, CafcCode)));
        Assert.Equal("siat.event_manual", notManual.Code);
        var noCafc = await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new StartManualContingencyCommand(host.CashPointId, energy, null, start, "NO-EXISTE")));
        Assert.Equal("siat.cafc_not_found", noCafc.Code);

        // Inicio: corte de energía con el talonario CAFC; la caja deja de emitir
        var started = await admin.Send(new StartManualContingencyCommand(host.CashPointId, energy, null, start, CafcCode));
        Assert.Contains(CafcCode, started, StringComparison.Ordinal);
        Assert.Equal(SiatConnectionMode.ManualContingency, (await host.PointAsync(host.CashPointId)).Mode);
        var evt = await host.DbAsync(db => db.Set<SignificantEvent>().AsNoTracking().SingleAsync(e => e.PointOfSaleId == host.CashPointId));
        Assert.Equal(SignificantEventKind.ManualCafc, evt.Kind);
        Assert.Equal(energy, evt.EventCode);
        var blocked = await Assert.ThrowsAsync<DomainException>(() =>
            E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("FER-001", 1)));
        Assert.Equal("fiscal.manual_contingency", blocked.Code);

        // Transcripción de 2 facturas manuales con SU número y SU fecha (dentro del evento)
        var stock = await host.OnHandAsync("FER-001");
        var first = await admin.Send(new TranscribeManualInvoiceCommand(evt.Id, 1001, start.AddMinutes(5), E_BillingTestHost.Ci("5566778", "Rosa Quispe"),
            "EFECTIVO", [new SaleLineInput("FER-001", 2)]));
        var second = await admin.Send(new TranscribeManualInvoiceCommand(evt.Id, 1002, start.AddMinutes(12), E_BillingTestHost.NitBuyer(),
            "EFECTIVO", [new SaleLineInput("FER-002", 1), new SaleLineInput("TOR-001", 1)]));
        Assert.Equal(1001, first.Number);
        Assert.Equal(1002, second.Number);
        Assert.Equal(FiscalDocumentStatus.Offline, first.Status);
        Assert.Equal(SiatCodes.EmissionOffline, first.EmissionType);
        Assert.Equal(stock - 2, await host.OnHandAsync("FER-001"));   // la venta transcrita descarga el stock
        var transcribed = await host.DocumentAsync(first.Id);
        Assert.Equal(CafcCode, transcribed.Cafc);
        Assert.Equal(evt.Id, transcribed.SignificantEventId);
        Assert.Equal(evt.EventCufdId, transcribed.CufdId);
        Assert.Equal(FiscalIssuer.Milliseconds(start.AddMinutes(5)), transcribed.IssuedAt);
        Assert.Equal(1, (await host.DocumentAsync(second.Id)).ExceptionCode);   // NIT fuera de línea: excepción 1
        var sale = await host.DbAsync(db => (from i in db.Set<Invoice>()
                                             join o in db.Set<SalesOrder>() on i.SalesOrderId equals o.Id
                                             where i.Id == transcribed.InvoiceId
                                             select new { o.PosSessionId, o.WarehouseId }).FirstAsync());
        Assert.Null(sale.PosSessionId);   // sin turno de caja
        Assert.NotNull(sale.WarehouseId);

        // Controles del talonario: fuera del rango, número repetido y fecha fuera del evento
        var outOfRange = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new TranscribeManualInvoiceCommand(evt.Id, 2000, start.AddMinutes(6),
            E_BillingTestHost.Ci(), "EFECTIVO", [new SaleLineInput("FER-001", 1)])));
        Assert.Equal("fiscal.cafc_number", outOfRange.Code);
        var repeated = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new TranscribeManualInvoiceCommand(evt.Id, 1001, start.AddMinutes(6),
            E_BillingTestHost.Ci(), "EFECTIVO", [new SaleLineInput("FER-001", 1)])));
        Assert.Equal("fiscal.cafc_number_used", repeated.Code);
        var before = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new TranscribeManualInvoiceCommand(evt.Id, 1003, start.AddMinutes(-1),
            E_BillingTestHost.Ci(), "EFECTIVO", [new SaleLineInput("FER-001", 1)])));
        Assert.Equal("fiscal.cafc_date", before.Code);

        // Fin de la contingencia: CUFD nuevo → evento registrado → paquete con el CAFC; la validación la hace el trabajo automático
        var ended = await admin.Send(new EndContingencyCommand(host.CashPointId));
        Assert.StartsWith("✔", ended, StringComparison.Ordinal);
        Assert.Equal(SiatConnectionMode.Online, (await host.PointAsync(host.CashPointId)).Mode);
        var package = await host.DbAsync(db => db.Set<FiscalPackage>().AsNoTracking().SingleAsync());
        Assert.Equal(CafcCode, package.Cafc);
        Assert.Equal(FiscalPackageStatus.Sent, package.Status);
        var maintenance = await host.WorkerAsync(w => w.MaintainAsync(false));
        Assert.Equal(1, maintenance.PackagesValidated);
        Assert.Equal(FiscalDocumentStatus.Valid, (await host.DocumentAsync(first.Id)).Status);
        Assert.Equal(FiscalDocumentStatus.Valid, (await host.DocumentAsync(second.Id)).Status);
        var registered = await host.DbAsync(db => db.Set<SignificantEvent>().AsNoTracking().SingleAsync(e => e.Id == evt.Id));
        Assert.Equal(SignificantEventStatus.PackagesSent, registered.Status);   // hasta 72 h se pueden transcribir más facturas
        Assert.NotNull(registered.ReceptionCode);

        // Una factura manual más, transcrita después del fin (dentro de las 72 h): va en otro paquete del mismo evento
        var late = await admin.Send(new TranscribeManualInvoiceCommand(evt.Id, 1003, start.AddMinutes(20), E_BillingTestHost.Ci(), "EFECTIVO",
            [new SaleLineInput("ELE-001", 3)]));
        Assert.Equal(FiscalDocumentStatus.Offline, late.Status);
        await host.WorkerAsync(w => w.MaintainAsync(false));   // envía el paquete
        await host.WorkerAsync(w => w.MaintainAsync(false));   // lo valida
        Assert.Equal(FiscalDocumentStatus.Valid, (await host.DocumentAsync(late.Id)).Status);
        Assert.Equal(2, await host.DbAsync(db => db.Set<FiscalPackage>().CountAsync(k => k.Cafc == CafcCode)));

        // Pasadas las 72 h ya no se transcribe
        host.MoveClockTo(host.Clock.UtcNow.AddHours(73));
        var tooLate = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new TranscribeManualInvoiceCommand(evt.Id, 1004, start.AddMinutes(21),
            E_BillingTestHost.Ci(), "EFECTIVO", [new SaleLineInput("FER-001", 1)])));
        Assert.Equal("fiscal.cafc_deadline", tooLate.Code);
    }

    [Fact]
    public async Task Paso_manual_a_fuera_de_linea_y_recuperacion_forzada()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var admin = await host.AdminAsync();
        using var cashier = await host.CashierAsync();
        var offline = await admin.Send(new GoOfflineCommand(host.CashPointId));
        Assert.Contains("fuera de línea", offline, StringComparison.Ordinal);
        var evt = await host.DbAsync(db => db.Set<SignificantEvent>().AsNoTracking().SingleAsync(e => e.PointOfSaleId == host.CashPointId));
        Assert.Contains("INTERNET", FiscalIssuer.Plain(evt.Description), StringComparison.Ordinal);
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("FER-001", 1));
        Assert.Equal(FiscalDocumentStatus.Offline, sale.FiscalStatus);

        host.MoveClockTo(host.Clock.UtcNow.AddMinutes(5));
        var recovered = await admin.Send(new RecoverPointOfSaleCommand(host.CashPointId));
        Assert.StartsWith("✔", recovered, StringComparison.Ordinal);
        Assert.Equal(SiatConnectionMode.Online, (await host.PointAsync(host.CashPointId)).Mode);
        Assert.Equal(FiscalDocumentStatus.InPackage, (await host.DocumentAsync(sale.FiscalDocumentId!.Value)).Status);
    }

    [Fact]
    public async Task El_cajero_ejecuta_el_trabajo_automatico_por_la_tuberia_de_MediatR()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        using var cashier = await host.CashierAsync();
        var first = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("FER-001", 1));
        var second = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.NitBuyer(), new SaleLineInput("FER-002", 2));

        var work = await cashier.Send(new RunSiatWorkCommand());
        Assert.Equal(2, work.Dispatch.Valid);
        Assert.NotNull(work.Maintenance);
        Assert.All(work.Dispatch.Documents, d => Assert.Equal(FiscalDocumentStatus.Valid, d.Status));
        Assert.Equal(FiscalDocumentStatus.Valid, (await host.DocumentAsync(first.FiscalDocumentId!.Value)).Status);
        Assert.Equal(FiscalDocumentStatus.Valid, (await host.DocumentAsync(second.FiscalDocumentId!.Value)).Status);

        // La tubería audita el comando y exige el permiso de emitir (un usuario de consulta no puede)
        var audited = await host.DbAsync(db => db.Set<Domain.Iam.AuditLog>().CountAsync(a => a.Action == "RunSiatWork"));
        Assert.Equal(1, audited);
        await Assert.ThrowsAsync<AccessDeniedException>(() => cashier.Send(new VoidFiscalDocumentCommand(first.FiscalDocumentId!.Value, 1, false)));
    }
}
