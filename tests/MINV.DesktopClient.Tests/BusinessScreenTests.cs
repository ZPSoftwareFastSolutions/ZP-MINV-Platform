using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Accounting;
using MINV.Domain.Accounting;
using MINV.DesktopClient.Services;
using MINV.DesktopClient.ViewModels;
using MINV.Domain.Purchasing;
using MINV.Infrastructure.Demo;

namespace MINV.DesktopClient.Tests;

/// <summary>
/// Pantallas de negocio con la demostración: catálogo con imágenes, punto de venta (abrir caja, carrito, cobro,
/// anulación), órdenes de compra (pedido sugerido → aprobar → recibir), reportes, contabilidad y usuarios. V4.2: la
/// demostración es Tech Zone Gaming (con ventas, compras y cajas abiertas de sus cajeros): el administrador abre la caja
/// libre (CAJA03) y cobra o recibe productos sin serie (los serializados se venden y se reciben con sus series).
/// </summary>
public sealed class BusinessScreenTests
{
    [Fact]
    public void Punto_de_venta_cobra_y_la_venta_se_puede_anular() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        // V4.1 · La demostración factura con el simulador del SIN; esta prueba cubre la venta SIN facturación (anular
        // desde Ventas devuelve el stock). La venta facturada y su anulación ante el SIN están en BillingScreenTests.
        var siat = await shell.App.SendAsync(new MINV.Application.Billing.GetSiatSettingsQuery());
        if (siat is { Configured: true, IsEnabled: true })
        {
            await shell.App.SendAsync(new MINV.Application.Billing.SaveSiatSettingsCommand(siat.Nit!.Value, siat.BusinessName!, siat.SystemCode!,
                siat.Environment, siat.OnlineLegend, siat.OfflineLegend, Enabled: false));
            await shell.App.RefreshBillingAsync();
        }

        shell.Navigate("pos");
        var pos = (PosViewModel)shell.Current;
        await pos.EnsureLoadedAsync();
        Assert.True(pos.IsClosed);
        Assert.NotEmpty(pos.Products.Cast<PosProduct>());
        Assert.All(pos.Products.Cast<PosProduct>(), p => Assert.NotNull(p.Image));
        pos.Register = pos.Registers.First(r => r.Code == "CAJA03");   // la caja libre (las demás tienen el turno de su cajero)
        await pos.OpenSession.ExecuteAsync();
        Assert.True(pos.IsOpen);

        var serialized = await DemoData.SerializedAsync(shell.App);
        var products = pos.Products.Cast<PosProduct>().Where(p => p.Available >= 3 && !serialized.Contains(p.Sku)).Take(2).ToList();
        foreach (var product in products)
        {
            pos.Add.Execute(product);
        }
        pos.Increase.Execute(pos.Cart[0]);
        pos.Cart[1].Discount = 10;
        Assert.Equal(2m, pos.Cart[0].Quantity);
        var total = pos.Total;
        Assert.True(total > 0);
        pos.CashReceived = Numbers.Plain(decimal.Ceiling(total) + 1000);
        Assert.StartsWith("Vuelto", pos.ChangeText, StringComparison.Ordinal);
        await pos.Checkout.ExecuteAsync();
        Assert.Empty(pos.Cart);
        Assert.NotNull(pos.LastSale);
        Assert.Equal(total, pos.LastSale!.Total);
        Assert.NotNull(pos.ReceiptText);
        Assert.Contains(pos.LastSale.InvoiceNumber, pos.ReceiptText!, StringComparison.Ordinal);

        shell.Navigate("ventas");
        var sales = (SalesViewModel)shell.Current;
        await sales.EnsureLoadedAsync();
        var sale = sales.Rows.Cast<SaleItem>().First(s => s.Invoice == pos.LastSale.InvoiceNumber);
        sales.Selected = sale;
        await Wpf.UntilAsync(() => sales.Lines.Count == 2);
        Assert.Equal(sales.Rows.Cast<SaleItem>().Count(s => !s.IsVoided).ToString("N0", Fmt.Culture), sales.TicketsKpi.Value);
        var voided = sales.Rows.Cast<SaleItem>().Count(s => s.IsVoided);

        // Anular con motivo (combo editable del cuadro): el stock vuelve y la factura queda ANULADA
        var run = sales.Void.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is { HasInput: true });
        shell.Dialogs.Current!.InputText = shell.Dialogs.Current.InputOptions[1];
        shell.Dialogs.Current.Confirm.Execute(null);
        await run;
        Assert.Contains(sales.Rows.Cast<SaleItem>(), s => s.Invoice == pos.LastSale.InvoiceNumber && s.IsVoided);
        Assert.Equal((voided + 1).ToString("N0", Fmt.Culture), sales.VoidedKpi.Value);
    });

    [Fact]
    public void Compras_desde_el_pedido_sugerido_se_aprueban_y_se_reciben() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        shell.Navigate("compras");
        var purchases = (PurchaseOrdersViewModel)shell.Current;
        await purchases.EnsureLoadedAsync();
        Assert.False(purchases.IsEmpty);   // la demostración trae órdenes recibidas, aprobadas y un borrador
        var before = purchases.Rows.Cast<PurchaseOrderItem>().Select(o => o.Row.Id).ToHashSet();

        // Pedido sugerido → órdenes en borrador (el cuadro de confirmación se acepta)
        var run = purchases.FromSuggestion.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is not null);
        shell.Dialogs.Current!.Confirm.Execute(null);
        await run;
        var drafts = purchases.Rows.Cast<PurchaseOrderItem>().Where(o => o.Status == PurchaseOrderStatus.Draft && !before.Contains(o.Row.Id)).ToList();
        Assert.NotEmpty(drafts);

        purchases.Selected = drafts[0];
        run = purchases.Approve.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is not null);
        shell.Dialogs.Current!.Confirm.Execute(null);
        await run;
        Assert.Equal(PurchaseOrderStatus.Approved, purchases.Selected!.Status);

        // V4.2 · La recepción desde esta pantalla es la de una orden SIN productos serializados (un serializado entra con sus
        // series): se aprueba y se recibe un borrador de un producto sin serie de su proveedor
        var serialized = await DemoData.SerializedAsync(shell.App);
        var item = (await shell.App.SendAsync(new MINV.Application.Catalog.GetCatalogQuery()))
            .First(c => c.SupplierCode is not null && c.Unit != "SERV" && !serialized.Contains(c.Sku));
        var order = await shell.App.SendAsync(new MINV.Application.Purchasing.CreatePurchaseOrderCommand(item.SupplierCode!, null, "Prueba de la pantalla",
            [new MINV.Application.Purchasing.PurchaseLineInput(item.Sku, 2, item.UnitCost)]));
        await purchases.LoadAsync(force: true);
        purchases.Selected = purchases.Rows.Cast<PurchaseOrderItem>().First(o => o.Row.Id == order.Id);
        run = purchases.Approve.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is not null);
        shell.Dialogs.Current!.Confirm.Execute(null);
        await run;
        Assert.Equal(PurchaseOrderStatus.Approved, purchases.Selected!.Status);

        run = purchases.Receive.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is { HasInput: true });
        shell.Dialogs.Current!.InputText = "FAC-7788";
        shell.Dialogs.Current!.Confirm.Execute(null);
        await run;
        Assert.Equal(PurchaseOrderStatus.Received, purchases.Selected!.Status);
        Assert.Equal(1d, purchases.Selected.Received);
    });

    [Fact]
    public void Catalogo_reportes_contabilidad_y_usuarios_cargan_con_sus_datos() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();

        shell.Navigate("catalogo");
        var catalog = (CatalogViewModel)shell.Current;
        await catalog.EnsureLoadedAsync();
        Assert.True(catalog.CanEdit);
        var items = catalog.Rows.Cast<CatalogProduct>().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.True(i.HasImage, i.Sku));
        catalog.State = catalog.States.First(s => s.Value == "lowmargin");
        catalog.State = catalog.States[0];
        catalog.Edit.Execute(items[0]);
        await Wpf.UntilAsync(() => catalog.IsEditing);
        var editor = catalog.Editor!;
        editor.Cost = "10";
        editor.MarginPreset = editor.MarginPresets.First(m => m.Value == 0.30m);
        Assert.True(Numbers.TryParse(editor.Price, out var suggested) && suggested > 14.2m);   // 10 / (1 − 0,30) = 14,29 + IVA
        editor.Price = "16,10";
        await editor.Save.ExecuteAsync();
        Assert.False(catalog.IsEditing);
        Assert.Equal(16.10m, catalog.Rows.Cast<CatalogProduct>().First(p => p.Sku == items[0].Sku).Price);

        shell.Navigate("reportes");
        var reports = (ReportsViewModel)shell.Current;
        await reports.EnsureLoadedAsync();
        reports.Tab = "inventory";
        await Wpf.UntilAsync(() => !reports.IsBusy && reports.CategoryRows.Count > 0);
        reports.Tab = "movements";
        await Wpf.UntilAsync(() => !reports.IsBusy);

        shell.Navigate("contabilidad");
        var accounting = (AccountingViewModel)shell.Current;
        await accounting.EnsureLoadedAsync();
        Assert.Contains(accounting.Accounts, a => a.Code == AccountCodes.Cash);
        accounting.Template = accounting.Templates.First(t => t.Label.StartsWith("Aporte", StringComparison.Ordinal));
        accounting.EntryLines[0].Debit = "1000";
        Assert.False(accounting.IsBalanced);
        accounting.EntryLines[1].Credit = "1000";
        Assert.True(accounting.IsBalanced);
        await accounting.SaveEntry.ExecuteAsync();
        Assert.Null(accounting.EntryError);
        Assert.True(accounting.IsJournal);
        Assert.Contains(accounting.Journal, j => j.Description.StartsWith("Aporte", StringComparison.Ordinal));

        shell.Navigate("usuarios");
        var users = (UsersViewModel)shell.Current;
        await users.EnsureLoadedAsync();
        Assert.Equal(7, users.Roles.Count);   // V6: + Tienda web (usuario técnico)
        Assert.NotEmpty(users.Rows.Cast<UserItem>());
        Assert.NotNull(users.Company);
    });
}
