using Microsoft.EntityFrameworkCore;
using MINV.Application.Billing;
using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>
/// V4.1 · Tablero «Estado SIAT»: puntos de venta con sus vigencias y documentos, y las ALERTAS con severidad y cuenta
/// regresiva (configuración, token, CUIS, CUFD, fuera de línea, eventos a registrar en 48 h, contingencia manual y 72 h de
/// transcripción, paquetes, documentos sin respuesta y rechazados, sincronización del día, homologación, ambiente de pruebas).
/// </summary>
public sealed class D_SiatStatusTests
{
    [Fact]
    public async Task Sin_configurar_el_tablero_pide_la_configuracion()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        var status = await admin.Send(new GetSiatStatusQuery());
        Assert.False(status.Configured);
        Assert.Empty(status.Points);
        Assert.Equal(SiatAlertSeverity.Danger, status.Alerts[0].Severity);
        Assert.Contains(status.Alerts, a => a.Title == "Falta configurar la facturación SIAT");
        Assert.Contains(status.Alerts, a => a.Title.StartsWith("Falta la conexión del ambiente 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task El_tablero_muestra_los_puntos_y_todas_las_alertas_con_sus_plazos()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        var today = DateOnly.FromDateTime(D_BillingTestHost.Start.ToOffset(TimeSpan.FromHours(-4)).DateTime);
        await D_BillingTestHost.ConfigureAsync(admin, tokenValidUntil: today.AddDays(3));

        // Recién preparado: todo en orden salvo el token por vencer y el ambiente de pruebas
        var fresh = await admin.Send(new GetSiatStatusQuery());
        Assert.True(fresh is { Configured: true, Enabled: true, HasToken: true, Environment: SiatCodes.EnvironmentTest, Nit: D_BillingTestHost.Nit });
        Assert.NotNull(fresh.ClockSyncedAt);
        Assert.NotNull(fresh.LastCatalogSync);
        Assert.Equal(2, fresh.Points.Count);
        Assert.All(fresh.Points, p => Assert.True(p.CuisValidUntil > host.Clock.UtcNow.AddDays(300) && p.CufdValidUntil > host.Clock.UtcNow));
        Assert.Equal(["CM", "EA"], fresh.Points.Select(p => p.BranchCode));
        Assert.Equal([0, 1], fresh.Points.Select(p => p.SiatBranchCode));
        var token = Assert.Single(fresh.Alerts, a => a.Title.StartsWith("El token delegado vence", StringComparison.Ordinal));
        Assert.Equal(SiatAlertSeverity.Warning, token.Severity);
        Assert.Contains("3 día(s)", token.Title, StringComparison.Ordinal);
        Assert.NotNull(token.Deadline);
        Assert.Contains(fresh.Alerts, a => a is { Severity: SiatAlertSeverity.Info, Title: "Ambiente de pruebas (piloto)" });
        Assert.DoesNotContain(fresh.Alerts, a => a.Severity == SiatAlertSeverity.Danger);
        Assert.DoesNotContain(fresh.Alerts, a => a.Title.Contains("sincroniz", StringComparison.Ordinal));

        // Documentos: dos válidos hoy, uno sin respuesta, uno rechazado, uno fuera de línea
        var cm = await D_Documents.PlaceAsync(admin);
        var ea = await D_Documents.PlaceAsync(admin, "EA");
        await D_Documents.InvoiceAsync(admin, cm, 1, [D_Documents.Line("FER-001", "Destornilladores", 2, 50m)]);
        await D_Documents.InvoiceAsync(admin, cm, 2, [D_Documents.Line("FER-001", "Destornilladores", 1, 30m)]);
        var lost = await D_Documents.InvoiceAsync(admin, cm, 3, [D_Documents.Line("FER-001", "Destornilladores", 1, 10m)], accept: false);
        await D_Documents.ChangeAsync(admin, lost.Id, d => d.MarkNoResponse());
        var rejected = await D_Documents.InvoiceAsync(admin, cm, 4, [D_Documents.Line("FER-001", "Destornilladores", 1, 10m)], accept: false);
        await D_Documents.ChangeAsync(admin, rejected.Id, d => d.Reject(SiatCodes.ReceptionRejected));

        // El Alto fuera de línea con su evento cerrado (48 h para registrarlo) y un documento emitido durante el evento
        var start = D_Documents.FiscalNow(admin, -3600);
        var eaPoint = await admin.Db.Set<SiatPointOfSale>().SingleAsync(p => p.Id == ea.Point.Id);
        eaPoint.GoOffline(host.Clock.UtcNow.AddHours(-1));
        var offlineEvent = new SignificantEvent(ea.Point.TenantId, ea.Point.BranchId, ea.Point.Id, SiatCodes.EnvironmentTest, SignificantEventKind.Offline, 1,
            "CORTE DEL SERVICIO DE INTERNET", start, ea.Cufd.Id, null, host.Clock.UtcNow, admin.Login.UserId);
        offlineEvent.Close(D_Documents.FiscalNow(admin, -60));
        admin.Db.Set<SignificantEvent>().Add(offlineEvent);
        await admin.Db.SaveChangesAsync();
        await D_Documents.InvoiceAsync(admin, ea, 1, [D_Documents.Line("FER-001", "Destornilladores", 1, 40m)], emission: SiatCodes.EmissionOffline,
            eventId: offlineEvent.Id, issuedAt: D_Documents.FiscalNow(admin, -1800));

        // Casa matriz: una contingencia manual (CAFC) abierta y un paquete enviado sin validar
        await admin.Send(new RegisterContingencyCodeCommand("CM", SiatCodes.SectorPurchaseSale, "CAFC-0001", 1, 100, null));
        var cafc = await admin.Db.Set<ContingencyCode>().SingleAsync();
        var cmPoint = await admin.Db.Set<SiatPointOfSale>().SingleAsync(p => p.Id == cm.Point.Id);
        cmPoint.StartManualContingency(host.Clock.UtcNow);
        admin.Db.Set<SignificantEvent>().Add(new SignificantEvent(cm.Point.TenantId, cm.Point.BranchId, cm.Point.Id, SiatCodes.EnvironmentTest,
            SignificantEventKind.ManualCafc, 5, "CORTE DE SUMINISTRO DE ENERGIA ELECTRICA", D_Documents.FiscalNow(admin), cm.Cufd.Id, cafc.Id, host.Clock.UtcNow,
            admin.Login.UserId));
        await admin.Db.SaveChangesAsync();
        admin.Db.Set<FiscalPackage>().Add(new FiscalPackage(ea.Point.TenantId, ea.Point.BranchId, offlineEvent.Id, ea.Point.Id, ea.Cufd.Id,
            SiatCodes.SectorPurchaseSale, SiatCodes.InvoiceWithTaxCredit, null, new string('a', 64), host.Clock.UtcNow));
        await admin.Db.SaveChangesAsync();

        // Productos activos sin homologar
        await D_BillingTestHost.CreateProductAsync(admin, "FER-001", "Juego de destornilladores", stock: 0);

        var status = await admin.Send(new GetSiatStatusQuery());
        Assert.Equal(3, status.DocumentsToday);           // las dos válidas y la emitida fuera de línea (la rechazada y la sin respuesta no cuentan)
        Assert.Equal(170m, status.BilledToday);
        var eaRow = status.Points.Single(p => p.BranchCode == "EA");
        Assert.Equal(SiatConnectionMode.Offline, eaRow.Mode);
        Assert.Equal(1, eaRow.OfflineDocuments);
        Assert.Equal(SignificantEventStatus.Closed, eaRow.OpenEvent!.Status);
        Assert.Equal(1, eaRow.OpenEvent.Documents);
        Assert.NotNull(eaRow.OpenEvent.RegistrationDeadline);
        var cmRow = status.Points.Single(p => p.BranchCode == "CM");
        Assert.Equal(SiatConnectionMode.ManualContingency, cmRow.Mode);
        Assert.Equal(1, cmRow.PendingDocuments);          // el que quedó sin respuesta
        Assert.Equal("CAFC-0001", cmRow.OpenEvent!.Cafc);
        Assert.Equal(1, status.OpenEvents);
        Assert.Equal(2, status.PendingDocuments + status.OfflineDocuments);

        string[] expected =
        [
            "EA · punto 0: fuera de línea desde", "EA · punto 0: Evento fuera de línea sin registrar en el SIN", "CM · punto 0: en contingencia manual desde",
            "CM · punto 0: Contingencia manual abierta desde", "1 paquete(s) de contingencia sin validar", "1 documento(s) sin respuesta del SIN",
            "1 documento(s) rechazado(s) en los últimos 7 días", "1 producto(s) activo(s) sin homologar", "El token delegado vence en 3 día(s)",
            "Ambiente de pruebas (piloto)",
        ];
        Assert.All(expected, title => Assert.Contains(status.Alerts, a => a.Title.StartsWith(title, StringComparison.Ordinal)));
        var registration = status.Alerts.Single(a => a.Title.Contains("sin registrar", StringComparison.Ordinal));
        Assert.Contains("48 h", registration.Detail, StringComparison.Ordinal);
        Assert.Equal(new DateTimeOffset(offlineEvent.RegistrationDeadline!.Value, TimeSpan.FromHours(-4)), registration.Deadline);
        Assert.True(status.Alerts.Select(a => SiatAlertSeverityRank(a.Severity)).SequenceEqual(status.Alerts.Select(a => SiatAlertSeverityRank(a.Severity))
            .Order()));

        // Un día después: CUFD vencidos, hora y catálogos sin sincronizar hoy, evento cerca del plazo
        host.Clock.StartAt(host.Clock.UtcNow.AddHours(26));
        var tomorrow = await admin.Send(new GetSiatStatusQuery());
        Assert.Contains(tomorrow.Alerts, a => a.Title == "La hora no se sincronizó hoy con el SIN");
        Assert.Contains(tomorrow.Alerts, a => a.Title == "Los catálogos no se sincronizaron hoy");
        Assert.Contains(tomorrow.Alerts, a => a.Title.StartsWith("CM · punto 0: CUFD vencido", StringComparison.Ordinal));
        Assert.Equal(0, tomorrow.DocumentsToday);
        Assert.Equal(0m, tomorrow.BilledToday);
        host.Clock.StartAt(host.Clock.UtcNow.AddDays(3));
        var expiredToken = await admin.Send(new GetSiatStatusQuery());
        Assert.Contains(expiredToken.Alerts, a => a is { Severity: SiatAlertSeverity.Danger, Title: "El token delegado VENCIÓ" });
    }

    [Fact]
    public async Task El_total_facturado_del_dia_suma_las_facturas_emitidas()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.ConfigureAsync(admin, elAlto: false);
        var cm = await D_Documents.PlaceAsync(admin);
        await D_Documents.InvoiceAsync(admin, cm, 1, [D_Documents.Line("FER-001", "Destornilladores", 2, 50m)]);
        await D_Documents.InvoiceAsync(admin, cm, 2, [D_Documents.Line("FER-001", "Destornilladores", 1, 30m)], accept: false);
        var voided = await D_Documents.InvoiceAsync(admin, cm, 3, [D_Documents.Line("FER-001", "Destornilladores", 1, 70m)]);
        await D_Documents.ChangeAsync(admin, voided.Id, d => d.Void(1, SiatCodes.VoidConfirmed, host.Clock.UtcNow));
        var status = await admin.Send(new GetSiatStatusQuery());
        Assert.Equal(2, status.DocumentsToday);
        Assert.Equal(130m, status.BilledToday);
        Assert.Equal(1, status.PendingDocuments);
    }

    private static int SiatAlertSeverityRank(string severity) => severity switch
    {
        SiatAlertSeverity.Danger => 0,
        SiatAlertSeverity.Warning => 1,
        _ => 2,
    };
}
