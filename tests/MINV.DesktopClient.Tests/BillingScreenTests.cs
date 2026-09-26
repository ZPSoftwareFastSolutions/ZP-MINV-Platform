using System.IO;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.DesktopClient.Hosting;
using MINV.DesktopClient.Services;
using MINV.DesktopClient.ViewModels;
using MINV.Domain.Billing;
using MINV.Infrastructure.Demo;

namespace MINV.DesktopClient.Tests;

/// <summary>
/// V4.1 · Pantallas de la facturación SIAT con la demostración y el simulador del SIN en memoria: la caja factura (datos
/// del comprador, envío al SIN y resultado fiscal), el documento se consulta y se anula ante el SIN, el historial de
/// ventas muestra la factura, y el trabajo automático corre sin molestar.
/// </summary>
public sealed class BillingScreenTests
{
    [Fact]
    public void La_caja_factura_y_el_documento_se_anula_ante_el_SIN() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        var message = await DesktopDemoBilling.ConfigureAsync(shell.App);
        Assert.Contains("activa", message, StringComparison.Ordinal);   // la demostración ya factura desde el inicio (DemoBillingSetup)
        Assert.True(shell.Session.IsBillingEnabled);

        // Caja: banda fiscal, datos del comprador y cobro con envío inmediato al SIN
        shell.Navigate("pos");
        var pos = (PosViewModel)shell.Current;
        await pos.LoadAsync(force: true);
        Assert.True(pos.IsBilling);
        Assert.NotNull(pos.Buyer);
        if (pos.IsClosed)
        {
            await pos.OpenSession.ExecuteAsync();
        }
        Assert.StartsWith("Facturación en línea", pos.FiscalTitle, StringComparison.Ordinal);
        Assert.Equal("Success", pos.FiscalBrush);
        pos.Buyer!.DocumentNumber = "5115889";
        pos.Buyer.Name = "Juan Pérez";
        Assert.True(pos.Buyer.IsCi);
        foreach (var product in pos.Products.Cast<PosProduct>().Where(p => p.Available >= 2).Take(2).ToList())
        {
            pos.Add.Execute(product);
        }
        var total = pos.Total;
        await pos.Checkout.ExecuteAsync();
        Assert.NotNull(pos.FiscalResult);
        var result = pos.FiscalResult!;
        Assert.Equal(FiscalDocumentStatus.Valid, result.Row.Status);
        Assert.Equal(total, result.Row.Total);
        Assert.Contains("SIN VALOR LEGAL", result.TicketText, StringComparison.Ordinal);
        Assert.Contains(result.Row.Cuf, result.TicketText.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(result.QrUrl));
        Assert.True(pos.Buyer.IsEmpty);   // el comprador se limpia para la siguiente venta
        pos.CloseFiscalResult.Execute(null);
        Assert.False(pos.IsFiscalResultOpen);

        // Documentos fiscales: detalle con la bitácora del SIN y anulación con el formulario
        shell.Navigate("documentos-fiscales", new FiscalDocumentFocus(result.Row.Id));
        var documents = (FiscalDocumentsViewModel)shell.Current;
        await documents.EnsureLoadedAsync();
        await Wpf.UntilAsync(() => documents.Detail?.Row.Id == result.Row.Id);
        Assert.Contains(documents.Events, e => e.Event.Action == FiscalDocumentAction.Accepted);
        Assert.True(documents.Void.CanExecute(null));
        var voiding = documents.Void.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Form is VoidFiscalDialog);
        var dialog = (VoidFiscalDialog)shell.Dialogs.Form!;
        Assert.NotNull(dialog.Reason);
        Assert.False(dialog.Confirm.CanExecute(null));   // exige la confirmación expresa
        dialog.Confirmed = true;
        await dialog.Confirm.ExecuteAsync();
        await voiding;
        Assert.Null(shell.Dialogs.Form);
        await Wpf.UntilAsync(() => documents.Detail?.Row.Status == FiscalDocumentStatus.Voided);
        Assert.True(documents.Revert.CanExecute(null));

        // Ventas: la factura del SIN de la venta aparece en el historial
        shell.Navigate("ventas");
        var sales = (SalesViewModel)shell.Current;
        await sales.LoadAsync(force: true);
        var sale = sales.Rows.Cast<SaleItem>().First(s => s.Invoice == result.Sale.InvoiceNumber);
        Assert.True(sale.HasFiscal);
        Assert.Equal(FiscalDocumentStatus.Voided, sale.Fiscal!.Status);

        // Estado SIAT, homologación y libros con datos
        shell.Navigate("estado-siat");
        var status = (SiatStatusViewModel)shell.Current;
        await status.LoadAsync(force: true);
        Assert.False(status.HasError, status.ErrorMessage);
        Assert.NotEmpty(status.Points);
        Assert.True(status.Points.All(p => p.IsOnline));
        shell.Navigate("homologacion");
        var homologation = (HomologationViewModel)shell.Current;
        await homologation.LoadAsync(force: true);
        Assert.Equal("0", homologation.PendingKpi.Value);
        shell.Navigate("libros-fiscales");
        var books = (FiscalBooksViewModel)shell.Current;
        await books.LoadAsync(force: true);
        Assert.False(books.HasError, books.ErrorMessage);

        // Trabajo automático (modo demostración): una ronda sin errores
        Assert.True(shell.BillingWork.Applies);
        await shell.BillingWork.RunOnceAsync();
        Assert.NotNull(shell.BillingWork.LastResult);
    });

    [Fact]
    public void Devolucion_de_una_venta_facturada_emite_la_nota_credito_debito() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        await DesktopDemoBilling.ConfigureAsync(shell.App);

        shell.Navigate("pos");
        var pos = (PosViewModel)shell.Current;
        await pos.LoadAsync(force: true);
        if (pos.IsClosed)
        {
            await pos.OpenSession.ExecuteAsync();
        }
        pos.Buyer!.UseSpecial.Execute(BuyerForm.SpecialNits[0]);   // 99003 ventas menores del día
        Assert.True(pos.Buyer.IsNit);
        var product = pos.Products.Cast<PosProduct>().First(p => p.Available >= 3);
        pos.Add.Execute(product);
        pos.Increase.Execute(pos.Cart[0]);
        await pos.Checkout.ExecuteAsync();
        var invoice = pos.FiscalResult!.Sale.InvoiceNumber;
        Assert.Equal(FiscalDocumentStatus.Valid, pos.FiscalResult.Row.Status);
        pos.CloseFiscalResult.Execute(null);

        shell.Navigate("ventas");
        var sales = (SalesViewModel)shell.Current;
        await sales.LoadAsync(force: true);
        sales.Selected = sales.Rows.Cast<SaleItem>().First(s => s.Invoice == invoice);
        Assert.True(sales.ReturnSale.CanExecute(null));
        var returning = sales.ReturnSale.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Form is SalesReturnDialog);
        var dialog = (SalesReturnDialog)shell.Dialogs.Form!;
        dialog.Lines[0].Quantity = "1";
        dialog.Reason = "Producto con falla";
        Assert.True(dialog.Confirm.CanExecute(null));
        await dialog.Confirm.ExecuteAsync();
        await returning;
        Assert.True(sales.IsReturnsTab);
        var row = Assert.Single(sales.Returns);
        Assert.Equal(invoice, row.Row.InvoiceNumber);
        Assert.True(row.HasNote);
    });

    [Fact]
    public void Formatos_y_ticket_fiscal_en_pantalla()
    {
        var now = new DateTimeOffset(2026, 9, 25, 14, 0, 0, TimeSpan.Zero);
        Assert.Equal("vence en 5 h 20 min", FiscalText.Countdown(now.AddHours(5).AddMinutes(20), now));
        Assert.Equal("venció hace 3 días", FiscalText.Countdown(now.AddDays(-3), now));
        Assert.Equal("8727F63A…7C5D01", FiscalText.ShortCuf("8727F63A15F8976591FDDE5B387C5D01"));
        Assert.Equal("Válida (anulación revertida)", FiscalText.Status(FiscalDocumentStatus.Valid, isReverted: true));
        Assert.Equal("el SIN va 1 h adelantado", FiscalText.ClockOffset(3_599_999));
        Assert.Equal("el SIN va 2 min 5 s atrasado", FiscalText.ClockOffset(-125_000));
        Assert.Equal("la hora coincide con la de este sistema", FiscalText.ClockOffset(300));
        Assert.Equal(5, DocumentTypeOption.From(null).Count);
        Assert.Equal(("5115889", "1A"), ReissueDialog.SplitDocument("5115889-1A"));
        Assert.Equal(5, ReissueDialog.DocumentTypeFromXml("<facturaComputarizadaCompraVenta><cabecera><codigoTipoDocumentoIdentidad>5</codigoTipoDocumentoIdentidad></cabecera></facturaComputarizadaCompraVenta>"));

        var model = new FiscalPrintModel("FACTURA", "(Con Derecho A Crédito Fiscal)", "Ferretería Demo S.R.L.", 1003579028, "Casa Matriz", 0, "Av. 6 de Agosto 100",
            "2800000", "La Paz", 12, "8727F63A15F8976591FDDE5B387C5D015A29E06A1A19E23EF34124CD", new DateTime(2026, 9, 25, 10, 30, 0), "Juan Pérez",
            "5115889", "CF", [new FiscalPrintLine("FER-001", "Martillo", "UNIDAD", 2, 45, 0, 90)], 90, 0, 90, 0, 90, 90, "NOVENTA 00/100 BOLIVIANOS",
            "EFECTIVO", "Cajero", ["Ley N° 453: prueba.", "Este documento es la representación gráfica…"], "https://pilotosiat.impuestos.gob.bo/consulta/QR?nit=1",
            IsTest: true, IsVoided: false, IsOffline: false);
        var text = FiscalTicketText.Render(model);
        Assert.Contains("FACTURA N°", text, StringComparison.Ordinal);
        Assert.Contains("SIN VALOR LEGAL", text, StringComparison.Ordinal);
        Assert.All(text.Split(Environment.NewLine), line => Assert.True(line.Length <= 42, line));
    }

    [Fact]
    public void La_clave_maestra_se_toma_del_archivo_de_claves_si_no_hay_variable()
    {
        const string variable = "MINV_INTEGRATION_KEYS";
        var original = Environment.GetEnvironmentVariable(variable);
        var file = Path.Combine(Path.GetTempPath(), $"claves-prueba-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllLines(file, ["# claves de integración (prueba)", $"{variable}=clave-de-prueba-no-real"]);
            Environment.SetEnvironmentVariable(variable, null);
            ClientHost.LoadIntegrationKeys(file);
            Assert.Equal("clave-de-prueba-no-real", Environment.GetEnvironmentVariable(variable));
            // Si la variable ya existe, manda la variable (el archivo no la pisa)
            Environment.SetEnvironmentVariable(variable, "de-la-variable");
            ClientHost.LoadIntegrationKeys(file);
            Assert.Equal("de-la-variable", Environment.GetEnvironmentVariable(variable));
            // Sin archivo no pasa nada
            Environment.SetEnvironmentVariable(variable, null);
            ClientHost.LoadIntegrationKeys(file + ".no-existe");
            Assert.Null(Environment.GetEnvironmentVariable(variable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
            File.Delete(file);
        }
    }
}
