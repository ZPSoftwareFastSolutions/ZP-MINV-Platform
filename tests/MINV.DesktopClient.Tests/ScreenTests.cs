using Microsoft.Extensions.DependencyInjection;
using MINV.DesktopClient.Services;
using MINV.DesktopClient.ViewModels;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Infrastructure.Demo;

namespace MINV.DesktopClient.Tests;

/// <summary>
/// Pantallas del cliente con la demostración (libro real de la V2.1 migrado a memoria) y los casos de uso reales: lo
/// que ve y hace una persona, sin ventanas (las vistas se revisan con <c>M-INV.exe --capturas</c>).
/// </summary>
public sealed class ScreenTests
{
    [Fact]
    public void El_menu_y_los_tipos_de_movimiento_se_adaptan_al_rol() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        Assert.Equal(["General", "Inventario", "Reposición", "Control"], shell.Sections.Select(s => s.Title));
        Assert.Equal(["inicio", "stock", "registro", "conteo", "alertas", "pedido", "actividad", "configuracion", "ayuda"],
            shell.AllPages.Select(p => p.Key));
        Assert.Equal("Ctrl+1", shell.AllPages[0].Shortcut);

        var seller = demo.Users.First(u => u.RoleCode == RoleCodes.Sales);
        using var sales = await host.SignInDemoAsync(demo, seller.Email);
        var salesShell = sales.Services.GetRequiredService<ShellViewModel>();
        Assert.DoesNotContain(salesShell.AllPages, p => p.Key is "conteo" or "actividad");
        salesShell.Navigate("registro");
        var movement = (MovementViewModel)salesShell.Current;
        await movement.EnsureLoadedAsync();
        Assert.NotEmpty(movement.Types);
        Assert.All(movement.Types, t => Assert.Contains(t.Code,
            new[] { MovementTypeCodes.Issue, MovementTypeCodes.Sale, MovementTypeCodes.SaleReturn }));
        salesShell.Navigate("actividad");   // sin permiso: no cambia de pantalla y avisa
        Assert.Same(movement, salesShell.Current);
        Assert.Contains(salesShell.Notifications.Items, t => t.Kind == ToastKind.Warning);
    });

    [Fact]
    public void Tablero_stock_alertas_y_pedido_muestran_la_misma_proyeccion() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var session = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = session.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        var view = await shell.App.Data.ProjectionAsync();
        var result = view.Result;

        var dashboard = (DashboardViewModel)shell.Current;
        Assert.True(dashboard.HasLoaded);
        Assert.Equal(result.Alerts.Count, dashboard.AlertCount);
        Assert.Equal(result.Stock.Count, (int)dashboard.StatusSegments.Sum(s => s.Value));
        Assert.Equal(14, dashboard.Trend.Count);
        Assert.NotEmpty(dashboard.TopSellers);
        Assert.NotEmpty(dashboard.Recent);
        Assert.NotEmpty(dashboard.Activity);
        Assert.StartsWith("Hay ", dashboard.NextStep, StringComparison.Ordinal);
        Assert.Equal(result.Alerts.Count.ToString(Fmt.Culture), shell.Alerts.Badge);

        shell.Navigate("stock");
        var stock = (StockViewModel)shell.Current;
        await stock.EnsureLoadedAsync();
        Assert.Equal(result.Stock.Count, stock.VisibleCount);
        var outOfStock = stock.Filters.First(f => Equals(f.Value, StockStatusCode.OutOfStock));
        stock.SelectFilter.Execute(outOfStock);
        Assert.Equal(outOfStock.Count, stock.VisibleCount);
        Assert.All(stock.Rows.Cast<StockItem>(), r => Assert.Equal(StockStatusCode.OutOfStock, r.Status));
        stock.SelectFilter.Execute(stock.Filters[0]);
        stock.Search = "cable";
        await Wpf.UntilAsync(() => stock.VisibleCount < result.Stock.Count);
        Assert.All(stock.Rows.Cast<StockItem>(), r => Assert.Contains("cable", r.Name, StringComparison.OrdinalIgnoreCase));

        shell.Navigate("alertas");
        var alerts = (AlertsViewModel)shell.Current;
        await alerts.EnsureLoadedAsync();
        Assert.Equal(result.Alerts.Count, alerts.Items.Count);
        Assert.Equal(result.Alerts.Select(a => a.Sku), alerts.Items.Select(a => a.Sku));   // mismo orden que 16_ALERTAS

        shell.Navigate("pedido");
        var order = (OrderViewModel)shell.Current;
        await order.EnsureLoadedAsync();
        Assert.Equal(result.Order.Count, order.Groups.Sum(g => g.Lines.Count));
        Assert.Equal(Fmt.Money(result.Order.Sum(o => o.Subtotal)), order.TotalText);

        shell.Navigate("actividad");
        var activity = (ActivityViewModel)shell.Current;
        await activity.EnsureLoadedAsync();
        Assert.True(activity.VisibleCount > 400);
        var rejected = activity.Filters.First(f => Equals(f.Value, AuditOutcome.Rejected));
        activity.SelectFilter.Execute(rejected);
        Assert.Equal(rejected.Count, activity.VisibleCount);
    });

    [Fact]
    public void Registrar_desde_la_pantalla_actualiza_los_datos_y_el_poka_yoke_bloquea() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var session = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = session.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        var product = (await shell.App.Data.ProjectionAsync()).Result.Stock.First(r => r.IsActive && r.Stock > 0 && r.Unit == "UND");

        shell.Navigate("registro", new MovementPrefill(product.Sku, MovementTypeCodes.Receipt));
        var movement = (MovementViewModel)shell.Current;
        await movement.EnsureLoadedAsync();
        await Wpf.UntilAsync(() => movement.HasProduct && !movement.IsLoadingProduct);
        Assert.Equal(MovementTypeCodes.Receipt, movement.SelectedType!.Code);
        Assert.False(string.IsNullOrEmpty(movement.BinCode));
        var before = movement.Product!.OnHand;
        var version = shell.App.Data.Version;

        movement.QuantityText = "3";
        Assert.True(movement.CanRegister);
        Assert.Equal(movement.BinAvailable + 3, movement.Projected);
        await movement.Register.ExecuteAsync();
        await Wpf.UntilAsync(() => !movement.IsLoadingProduct && movement.Product?.OnHand == before + 3);
        Assert.Single(movement.SessionLog);
        Assert.True(shell.App.Data.Version > version);
        Assert.Contains(shell.Notifications.Items, t => t.Kind == ToastKind.Success && t.Title == "Entrada registrada");
        Assert.Equal(string.Empty, movement.QuantityText);

        // Poka-yoke visual: una salida mayor que lo disponible en la posición no se puede registrar
        movement.SelectType.Execute(movement.Types.First(t => t.Code == MovementTypeCodes.Issue));
        movement.QuantityText = Fmt.Qty(movement.BinAvailable + 1);
        Assert.True(movement.WouldBeNegative);
        Assert.False(movement.CanRegister);

        // Los ajustes exigen observaciones; una unidad entera no admite decimales
        movement.SelectType.Execute(movement.Types.First(t => t.Code == MovementTypeCodes.AdjustmentIn));
        movement.QuantityText = "1,5";
        Assert.NotNull(movement.QuantityError);
        movement.QuantityText = "1";
        Assert.True(movement.NotesMissing);
        Assert.False(movement.CanRegister);
        movement.Notes = "Sobrante encontrado";
        Assert.True(movement.CanRegister);

        // El tablero se recarga solo al volver (los datos cambiaron)
        shell.Navigate("inicio");
        await Wpf.UntilAsync(() => !shell.Current.IsBusy);
        Assert.Contains(((DashboardViewModel)shell.Current).Recent, m => m.Sku == product.Sku);
    });

    [Fact]
    public void Toma_fisica_contar_quitar_y_contabilizar_con_confirmacion() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var session = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = session.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        shell.Navigate("conteo");
        var count = (PhysicalCountViewModel)shell.Current;
        await count.EnsureLoadedAsync();
        Assert.True(count.HasOpenCount);          // la toma en curso de la V2.1 se migró
        Assert.Equal(2, count.LineCount);

        var sku = (await shell.App.Data.LookupAsync()).First(p => count.Lines.All(l => l.Sku != p.Sku)).Sku;
        Assert.True(count.Picker.TrySelectCode(sku));
        await Wpf.UntilAsync(() => count.HasProduct);
        count.CountedText = "7";
        Assert.True(count.CanRecord);
        await count.Record.ExecuteAsync();
        Assert.Equal(3, count.LineCount);
        Assert.False(count.HasProduct);

        count.RemoveLine.Execute(count.Lines.First(l => l.Sku == sku));
        await Wpf.UntilAsync(() => count.LineCount == 2);

        var posting = count.Post.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is not null);
        Assert.Contains(shell.Dialogs.Current!.Details, d => d.Contains("sobrante", StringComparison.Ordinal));
        shell.Dialogs.Current.Confirm.Execute(null);
        await posting;
        Assert.False(count.HasOpenCount);
        Assert.Contains(shell.Notifications.Items, t => t.Title == "Toma física contabilizada");
    });

    [Fact]
    public void El_buscador_encuentra_por_SKU_nombre_y_categoria_sin_tildes() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var session = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = session.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        var search = shell.Search;
        Assert.True(search.CatalogSize > 30);

        search.Query = "fer-001";
        Assert.Equal("FER-001", search.Suggestions[0].Sku);
        search.Query = "electricos";                 // «ELÉCTRICOS» sin tilde
        Assert.NotEmpty(search.Suggestions);
        Assert.All(search.Suggestions, s => Assert.Equal("ELÉCTRICOS", s.Category));
        search.Query = "zzz-no-existe";
        Assert.True(search.HasNoResults);

        search.Query = "FER-001";
        Assert.True(search.Commit());                // Enter: abre la ficha
        await Wpf.UntilAsync(() => shell.ProductDetail is { IsLoading: false });
        Assert.Equal("FER-001", shell.ProductDetail!.Sku);
        Assert.Equal(shell.ProductDetail.Card!.TotalMovements, shell.ProductDetail.Kardex.Count);
        Assert.Equal((double)shell.ProductDetail.Card.OnHand, shell.ProductDetail.Balances[^1]);
        Assert.Equal(string.Empty, search.Query);
    });
}
