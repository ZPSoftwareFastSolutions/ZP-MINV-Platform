using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>
/// V4.1 · Administración SIAT contra el simulador en proceso: configuración de la empresa (NIT, conexión con token cifrado,
/// sucursales del Padrón, correo), «Preparar SIAT» (punto 0, CUIS, CUFD, hora y los 18 catálogos), registro y cierre de
/// puntos de venta, CUIS/CUFD forzados, sincronización, comunicación y verificación de NIT.
/// </summary>
public sealed class D_SiatAdminTests
{
    [Fact]
    public async Task Configura_una_empresa_de_prueba_completa_y_prepara_el_SIAT()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();

        // Sin configurar: el módulo está licenciado pero nada más
        var empty = await admin.Send(new GetSiatSettingsQuery());
        Assert.False(empty.Configured);
        Assert.True(empty.ModuleActive);
        Assert.Contains(empty.Branches, b => b.BranchCode == "CM" && b.SiatCode is null);

        // Activar exige token y casa matriz
        var noToken = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveSiatSettingsCommand(D_BillingTestHost.Nit,
            D_BillingTestHost.CompanyName, D_BillingTestHost.SystemCode, SiatCodes.EnvironmentTest, null, null, true)));
        Assert.Equal("siat.no_token", noToken.Code);

        // Conexión: el token se cifra y nunca se devuelve
        var saved = await admin.Send(new SaveSiatProfileCommand(SiatCodes.EnvironmentTest, SiatEndpointSet.ForBaseUrl("http://127.0.0.1:5095"),
            D_BillingTestHost.QrBase, 15, D_BillingTestHost.Token, new DateOnly(2027, 6, 30)));
        Assert.DoesNotContain(D_BillingTestHost.Token, saved, StringComparison.Ordinal);
        var profile = await admin.Db.Set<SiatEnvironmentProfile>().AsNoTracking().SingleAsync();
        Assert.True(profile.HasToken);
        Assert.DoesNotContain(D_BillingTestHost.Token, profile.TokenCiphertext!, StringComparison.Ordinal);
        Assert.Equal(D_BillingTestHost.Token, admin.Services.GetRequiredService<ISecretProtector>().Unprotect(profile.TokenCiphertext!, profile.TokenKeyId!));
        // Sin token nuevo se conserva el guardado
        await admin.Send(new SaveSiatProfileCommand(SiatCodes.EnvironmentTest, SiatEndpointSet.ForBaseUrl("http://127.0.0.1:5095"),
            D_BillingTestHost.QrBase, 20, null, new DateOnly(2027, 6, 30)));
        Assert.Equal(profile.TokenCiphertext, (await admin.Db.Set<SiatEnvironmentProfile>().AsNoTracking().SingleAsync()).TokenCiphertext);

        var noHeadOffice = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveSiatSettingsCommand(D_BillingTestHost.Nit,
            D_BillingTestHost.CompanyName, D_BillingTestHost.SystemCode, SiatCodes.EnvironmentTest, null, null, true)));
        Assert.Equal("siat.no_head_office", noHeadOffice.Code);

        // Sucursales del Padrón: el código es único
        await admin.Send(new SaveSiatBranchCommand("CM", 0, "La Paz", "2800000"));
        await admin.Send(new MINV.Application.Corporate.CreateBranchCommand("EA", "Sucursal El Alto", "ALMEA", "Almacén El Alto"));
        var taken = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveSiatBranchCommand("EA", 0, "El Alto", null)));
        Assert.Equal("siat.branch_code_taken", taken.Code);
        await admin.Send(new SaveSiatBranchCommand("EA", 1, "El Alto", null));

        var enabled = await admin.Send(new SaveSiatSettingsCommand(D_BillingTestHost.Nit, D_BillingTestHost.CompanyName, D_BillingTestHost.SystemCode,
            SiatCodes.EnvironmentTest, null, "Documento emitido fuera de línea: verifíquelo en www.impuestos.gob.bo", true));
        Assert.Contains("ACTIVADA", enabled, StringComparison.Ordinal);

        // Correo con contraseña cifrada
        await admin.Send(new SaveMailSettingsCommand("smtp.tornillo.example", 587, true, "facturas", "clave-smtp-123", "facturas@tornillo.example",
            "Facturación El Tornillo", true));
        var mail = await admin.Db.Set<MailSettings>().AsNoTracking().SingleAsync();
        Assert.DoesNotContain("clave-smtp-123", mail.PasswordCiphertext!, StringComparison.Ordinal);

        var view = await admin.Send(new GetSiatSettingsQuery());
        Assert.True(view.Configured && view.IsEnabled && view.ModuleActive);
        Assert.Equal(SiatSettings.DefaultOnlineLegend, view.OnlineLegend);   // null → leyenda por defecto
        Assert.StartsWith("Documento emitido fuera de línea", view.OfflineLegend, StringComparison.Ordinal);
        Assert.True(Assert.Single(view.Profiles).HasToken);
        Assert.Equal(20, view.Profiles[0].TimeoutSeconds);
        Assert.Equal([0, 1], view.Branches.Where(b => b.SiatCode is not null).Select(b => b.SiatCode!.Value).Order());
        Assert.True(view.Mail is { HasPassword: true, IsEnabled: true });

        // Preparar SIAT: punto 0 en cada sucursal, CUIS, CUFD, hora y catálogos
        var prepared = await admin.Send(new PrepareSiatCommand());
        Assert.Equal(2, prepared.CuisRequested);
        Assert.Equal(2, prepared.CufdRequested);
        var points = await admin.Db.Set<SiatPointOfSale>().AsNoTracking().ToListAsync();
        Assert.Equal(2, points.Count);
        Assert.All(points, p => Assert.Equal(0, p.Code));
        Assert.Equal(2, await admin.Db.Set<SiatCuis>().CountAsync());
        Assert.Equal(2, await admin.Db.Set<SiatCufd>().CountAsync());
        var settings = await admin.Db.Set<SiatSettings>().AsNoTracking().SingleAsync();
        Assert.NotNull(settings.ClockSyncedAt);
        Assert.Equal(5, await admin.Db.Set<SiatActivity>().CountAsync());   // V4.2: 3 de tecnología + 2 de ferretería (simulador)
        Assert.True(await admin.Db.Set<SiatProduct>().CountAsync() > 200);
        Assert.True(await admin.Db.Set<SiatLegend>().CountAsync() >= 12);
        Assert.Equal(10, await admin.Db.Set<SiatActivitySector>().CountAsync());   // factura (1) y nota (24) por actividad
        Assert.True(await admin.Db.Set<SiatCatalogItem>().CountAsync(i => i.Catalog == SiatCatalogNames.PaymentMethods) >= 10);
        Assert.Equal(17, await admin.Db.Set<SiatSyncRun>().Where(r => r.Error == null && r.Catalog != SiatCatalogNames.DateTime).Select(r => r.Catalog)
            .Distinct().CountAsync());

        // Punto de venta 1 de la casa matriz, vinculado a la caja 1: el SIN asigna el código, con su CUIS y su CUFD
        var status = await admin.Send(new RegisterSiatPointOfSaleCommand("CM", "Caja 1", "Caja principal del mostrador", "CAJA01"));
        Assert.Equal(1, status.Code);
        Assert.Equal("CAJA01", status.RegisterCode);
        Assert.Equal(0, status.SiatBranchCode);
        Assert.NotNull(status.CuisValidUntil);
        Assert.True(status.CufdValidUntil > host.Clock.UtcNow);
        Assert.Equal(SiatConnectionMode.Online, status.Mode);

        // CUFD forzado: uno nuevo; CUIS forzado: el SIN mantiene el vigente (faltan más de 5 días)
        var cufds = await admin.Db.Set<SiatCufd>().CountAsync(c => c.PointOfSaleId == status.Id);
        Assert.StartsWith("✔ CUFD nuevo", await admin.Send(new RequestCufdCommand(status.Id)), StringComparison.Ordinal);
        Assert.Equal(cufds + 1, await admin.Db.Set<SiatCufd>().CountAsync(c => c.PointOfSaleId == status.Id));
        Assert.Contains("mantuvo", await admin.Send(new RequestCuisCommand(status.Id)), StringComparison.Ordinal);

        // Otra caja con el mismo registro: la caja 1 pasa al punto nuevo (una caja, un punto por ambiente)
        var second = await admin.Send(new RegisterSiatPointOfSaleCommand("CM", "Caja 2", null, "CAJA01"));
        Assert.Equal(2, second.Code);
        Assert.Null((await admin.Db.Set<SiatPointOfSale>().AsNoTracking().SingleAsync(p => p.Id == status.Id)).PosRegisterId);
        Assert.Contains("CAJA01", await admin.Send(new LinkPointOfSaleRegisterCommand(status.Id, "caja01")), StringComparison.Ordinal);
        Assert.Null((await admin.Db.Set<SiatPointOfSale>().AsNoTracking().SingleAsync(p => p.Id == second.Id)).PosRegisterId);

        // Cierre definitivo (el punto 0 no se cierra)
        Assert.Contains("DEFINITIVAMENTE", await admin.Send(new CloseSiatPointOfSaleCommand(second.Id)), StringComparison.Ordinal);
        Assert.True((await admin.Db.Set<SiatPointOfSale>().AsNoTracking().SingleAsync(p => p.Id == second.Id)).IsClosed);
        var zero = points.First(p => p.BranchId == status.BranchId);
        Assert.Equal("siat.pos_zero", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new CloseSiatPointOfSaleCommand(zero.Id)))).Code);
        Assert.Equal("siat.pos_closed", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new RequestCufdCommand(second.Id)))).Code);

        // Todo quedó en la auditoría, sin el token
        var audit = await admin.Db.Set<MINV.Domain.Iam.AuditLog>().AsNoTracking().Select(a => a.Details ?? string.Empty).ToListAsync();
        Assert.Contains(audit, d => d.Contains("TokenChanged", StringComparison.Ordinal));
        Assert.DoesNotContain(audit, d => d.Contains(D_BillingTestHost.Token, StringComparison.Ordinal));
        Assert.DoesNotContain(audit, d => d.Contains("clave-smtp-123", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Comunicacion_y_sincronizacion_de_un_catalogo_y_sin_comunicacion_registra_el_fallo()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.ConfigureAsync(admin);

        var ok = await admin.Send(new CheckSiatCommunicationCommand());
        Assert.Contains("926", ok, StringComparison.Ordinal);
        Assert.All(await admin.Db.Set<SiatPointOfSale>().AsNoTracking().ToListAsync(), p => Assert.NotNull(p.LastContactAt));

        var one = await admin.Send(new SyncSiatCatalogsCommand("tipo_metodo_pago"));
        Assert.Equal(1, one.Catalogs);
        Assert.True(one.Items >= 10);
        Assert.Equal("siat.catalog_unknown",
            (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SyncSiatCatalogsCommand("NO_EXISTE")))).Code);

        host.Simulator.Available = false;
        var down = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new CheckSiatCommunicationCommand()));
        Assert.Equal("siat.unavailable", down.Code);
        Assert.StartsWith("No hay comunicación con el SIN", down.Message, StringComparison.Ordinal);
        Assert.All(await admin.Db.Set<SiatPointOfSale>().AsNoTracking().ToListAsync(), p =>
        {
            Assert.Equal(1, p.ConsecutiveFailures);
            Assert.NotNull(p.LastError);
        });
        var point = await admin.Db.Set<SiatPointOfSale>().AsNoTracking().FirstAsync();
        Assert.Equal("siat.unavailable", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new RequestCufdCommand(point.Id)))).Code);
        Assert.Equal("siat.unavailable", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SyncSiatCatalogsCommand()))).Code);
    }

    [Fact]
    public async Task VerifyNit_valido_inactivo_987_y_sin_comunicacion_con_excepcion()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.ConfigureAsync(admin);

        var valid = await admin.Send(new VerifyNitCommand(1020703023, "cf"));
        Assert.True(valid is { Checked: true, IsValid: true, SiatCode: SiatCodes.NitActive });
        Assert.Equal("NIT Activo", valid.Description);
        var customer = await admin.Db.Set<Customer>().AsNoTracking().SingleAsync(c => c.Code == "CF");
        var check = await admin.Db.Set<CustomerNitCheck>().AsNoTracking().SingleAsync(c => c.Nit == 1020703023);
        Assert.Equal(customer.Id, check.CustomerId);
        Assert.Equal(admin.Login.UserId, check.UserId);

        var inactive = await admin.Send(new VerifyNitCommand(1234567999));
        Assert.True(inactive is { Checked: true, IsValid: false, SiatCode: SiatCodes.NitInactive });
        Assert.Null((await admin.Db.Set<CustomerNitCheck>().AsNoTracking().SingleAsync(c => c.Nit == 1234567999)).CustomerId);

        await Assert.ThrowsAsync<NotFoundException>(() => admin.Send(new VerifyNitCommand(1020703023, "NO-EXISTE")));

        host.Simulator.Available = false;
        var offline = await admin.Send(new VerifyNitCommand(5555555));
        Assert.False(offline.Checked);
        Assert.False(offline.IsValid);
        Assert.Null(offline.SiatCode);
        Assert.Equal("No se pudo verificar: el comprobante saldrá con código de excepción", offline.Description);
        Assert.False(await admin.Db.Set<CustomerNitCheck>().AnyAsync(c => c.Nit == 5555555));
    }

    [Fact]
    public async Task El_mantenimiento_diario_no_repite_lo_del_dia_y_sigue_cuando_un_punto_no_tiene_comunicacion()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.ConfigureAsync(admin);
        var (lookups, codes) = admin.Billing();
        admin.Db.ClearTracking();

        // Segunda corrida del mismo día: nada que pedir (CUIS y CUFD vigentes, hora y catálogos de hoy)
        var context = await lookups.ContextAsync(CancellationToken.None);
        var calm = await SiatDailyMaintenance.RunAsync(lookups, codes, context, false, admin.Login.UserId, CancellationToken.None);
        Assert.Equal(0, calm.CuisRequested);
        Assert.Equal(0, calm.CufdRequested);
        Assert.DoesNotContain(calm.Messages, m => m.StartsWith("Catálogos", StringComparison.Ordinal));
        await admin.Db.SaveChangesAsync();

        // Una sucursal nueva en el Padrón: se crea su punto 0 con CUIS y CUFD
        await admin.Send(new MINV.Application.Corporate.CreateBranchCommand("SC", "Sucursal Santa Cruz", "ALMSC", "Almacén Santa Cruz"));
        await admin.Send(new SaveSiatBranchCommand("SC", 2, "Santa Cruz", null));
        admin.Db.ClearTracking();
        context = await lookups.ContextAsync(CancellationToken.None);
        var grown = await SiatDailyMaintenance.RunAsync(lookups, codes, context, false, null, CancellationToken.None);
        Assert.Equal(1, grown.CuisRequested);
        Assert.Equal(1, grown.CufdRequested);
        Assert.Contains(grown.Messages, m => m.StartsWith("SC: se creó el punto 0", StringComparison.Ordinal));
        await admin.Db.SaveChangesAsync();
        Assert.Equal(3, await admin.Db.Set<SiatPointOfSale>().CountAsync());

        // Sin comunicación: registra el fallo en cada punto y NO lanza
        host.Simulator.Available = false;
        admin.Db.ClearTracking();
        context = await lookups.ContextAsync(CancellationToken.None);
        var down = await SiatDailyMaintenance.RunAsync(lookups, codes, context, true, null, CancellationToken.None);
        Assert.Equal(3, down.Messages.Count(m => m.Contains("sin comunicación con el SIN", StringComparison.Ordinal)));
        await admin.Db.SaveChangesAsync();
        Assert.All(await admin.Db.Set<SiatPointOfSale>().AsNoTracking().ToListAsync(), p => Assert.Equal(1, p.ConsecutiveFailures));
    }

    [Fact]
    public async Task La_bitacora_tecnica_muestra_las_llamadas_sin_el_token()
    {
        await using var host = await D_BillingTestHost.CreateAsync();
        using var admin = await host.SignInAsync();
        await D_BillingTestHost.ConfigureAsync(admin, elAlto: false);
        var today = DateOnly.FromDateTime(D_Documents.FiscalNow(admin));
        var calls = await admin.Send(new GetSiatServiceCallsQuery(today, today));
        Assert.True(calls.Count >= 20, $"Solo {calls.Count} llamadas");
        Assert.All(calls, c => Assert.DoesNotContain(D_BillingTestHost.Token, (c.RequestBody ?? string.Empty) + (c.ResponseBody ?? string.Empty),
            StringComparison.Ordinal));
        var cufd = await admin.Send(new GetSiatServiceCallsQuery(today, today, "cufd", 5));
        Assert.NotEmpty(cufd);
        Assert.All(cufd, c => Assert.Contains("cufd", c.Operation, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await admin.Send(new GetSiatServiceCallsQuery(today.AddDays(-3), today.AddDays(-2))));
    }
}
