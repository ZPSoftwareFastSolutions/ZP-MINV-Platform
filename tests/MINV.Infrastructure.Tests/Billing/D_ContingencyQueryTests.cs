using Microsoft.EntityFrameworkCore;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>V4.1 · Talonarios CAFC (alta y uso), eventos significativos y paquetes de contingencia con sus documentos.</summary>
public sealed class D_ContingencyQueryTests
{
    [Fact]
    public async Task CAFC_eventos_y_paquetes_con_sus_documentos()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.ConfigureAsync(admin);

        Assert.Contains("CAFC-0001", await admin.Send(new RegisterContingencyCodeCommand("cm", SiatCodes.SectorPurchaseSale, "CAFC-0001", 1, 50,
            new DateOnly(2026, 12, 31))), StringComparison.Ordinal);
        var overlap = await admin.Send(new RegisterContingencyCodeCommand("CM", SiatCodes.SectorPurchaseSale, "CAFC-0002", 40, 90, null));
        Assert.Contains("se superpone", overlap, StringComparison.Ordinal);
        await admin.Send(new RegisterContingencyCodeCommand("EA", SiatCodes.SectorPurchaseSale, "CAFC-0001", 1, 50, null));   // otra sucursal: vale
        Assert.Equal("siat.cafc_duplicate", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new RegisterContingencyCodeCommand("CM", SiatCodes.SectorPurchaseSale, "CAFC-0001", 100, 150, null)))).Code);
        Assert.Equal("siat.cafc_sector", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new RegisterContingencyCodeCommand("CM", SiatCodes.SectorCreditDebitNote, "CAFC-0009", 1, 5, null)))).Code);
        Assert.Equal("siat.cafc_expired", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new RegisterContingencyCodeCommand("CM", SiatCodes.SectorPurchaseSale, "CAFC-0010", 1, 5, new DateOnly(2026, 1, 31))))).Code);
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            admin.Send(new RegisterContingencyCodeCommand("CM", SiatCodes.SectorPurchaseSale, "CAFC-0011", 10, 5, null)));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            admin.Send(new RegisterContingencyCodeCommand("ZZ", SiatCodes.SectorPurchaseSale, "CAFC-0012", 1, 5, null)));

        // Contingencia manual en la casa matriz con el CAFC-0001: dos facturas transcritas y un paquete
        var cm = await D_Documents.PlaceAsync(admin);
        var cafc = await admin.Db.Set<ContingencyCode>().SingleAsync(c => c.Code == "CAFC-0001" && c.BranchId == cm.Point.BranchId);
        var started = D_Documents.FiscalNow(admin, -7200);
        var manual = new SignificantEvent(cm.Point.TenantId, cm.Point.BranchId, cm.Point.Id, SiatCodes.EnvironmentTest, SignificantEventKind.ManualCafc, 5,
            "CORTE DE SUMINISTRO DE ENERGIA ELECTRICA", started, cm.Cufd.Id, cafc.Id, host.Clock.UtcNow, admin.Login.UserId);
        manual.Close(D_Documents.FiscalNow(admin, -600));
        admin.Db.Set<SignificantEvent>().Add(manual);
        await admin.Db.SaveChangesAsync();
        var first = await D_Documents.InvoiceAsync(admin, cm, 1, [D_Documents.Line("FER-001", "Destornilladores", 1, 50m)], emission: SiatCodes.EmissionOffline,
            cafc: "CAFC-0001", eventId: manual.Id, issuedAt: D_Documents.FiscalNow(admin, -5000));
        await D_Documents.InvoiceAsync(admin, cm, 2, [D_Documents.Line("FER-001", "Destornilladores", 2, 50m)], emission: SiatCodes.EmissionOffline,
            cafc: "CAFC-0001", eventId: manual.Id, issuedAt: D_Documents.FiscalNow(admin, -4000));
        var package = new FiscalPackage(cm.Point.TenantId, cm.Point.BranchId, manual.Id, cm.Point.Id, cm.Cufd.Id, SiatCodes.SectorPurchaseSale,
            SiatCodes.InvoiceWithTaxCredit, "CAFC-0001", new string('b', 64), host.Clock.UtcNow);
        admin.Db.Set<FiscalPackage>().Add(package);
        await admin.Db.SaveChangesAsync();
        await D_Documents.ChangeAsync(admin, first.Id, d => d.AddToPackage(package.Id, 1));

        var codes = await admin.Send(new GetContingencyCodesQuery());
        Assert.Equal(3, codes.Count);
        var used = codes.Single(c => c.BranchCode == "CM" && c.Code == "CAFC-0001");
        Assert.Equal((2, 1L, 50L, true, new DateOnly(2026, 12, 31)), (used.Used, used.NumberFrom, used.NumberTo, used.IsActive, used.ValidUntil!.Value));
        Assert.Equal(0, codes.Single(c => c.BranchCode == "EA").Used);

        var today = DateOnly.FromDateTime(D_Documents.FiscalNow(admin));
        var events = await admin.Send(new GetSignificantEventsQuery(today, today));
        var row = Assert.Single(events);
        Assert.Equal((SignificantEventKind.ManualCafc, 5, "CM", 0, 2, "CAFC-0001", SignificantEventStatus.Closed),
            (row.Kind, row.EventCode, row.BranchCode, row.PointOfSaleCode, row.Documents, row.Cafc, row.Status));
        Assert.Equal(manual.EndedAt!.Value.AddHours(48), row.RegistrationDeadline);
        Assert.Equal(manual.EndedAt!.Value.AddHours(72), row.TranscriptionDeadline);
        Assert.Empty(await admin.Send(new GetSignificantEventsQuery(today.AddDays(-10), today.AddDays(-5))));

        var packages = await admin.Send(new GetFiscalPackagesQuery(manual.Id));
        var p = Assert.Single(packages);
        Assert.Equal((1, "CAFC-0001", FiscalPackageStatus.Sent, "CM", 0), (p.Documents, p.Cafc, p.Status, p.BranchCode, p.PointOfSaleCode));
        Assert.Single(await admin.Send(new GetFiscalPackagesQuery()));
        Assert.Empty(await admin.Send(new GetFiscalPackagesQuery(Guid.NewGuid())));
    }
}
