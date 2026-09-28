using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Billing;
using MINV.Application.Inventory.Queries;
using MINV.Application.Tech;
using MINV.DesktopClient.Services;
using MINV.DesktopClient.ViewModels;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Sales;
using MINV.Infrastructure.Demo;

namespace MINV.DesktopClient.Tests;

/// <summary>
/// V6 · El escritorio y la tienda web conectada (regla S-08) con la demostración de Tech Zone Gaming, que trae armados
/// publicados y reservas web de ejemplo (una vigente y una vencida): el armador lista la reserva web con su canal, contacto y
/// vencimiento; una cotización propia se reserva, se publica y se libera desde el ViewModel; la caja vende un armado reservado
/// consumiendo la reserva; y stock, catálogo y caja muestran lo reservado (disponible = existencias − reservado, regla S-03).
/// </summary>
public sealed class StorefrontScreenTests
{
    [Fact]
    public void V6_el_armador_lista_la_reserva_web_de_la_demostracion_y_el_tablero_la_cuenta() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();

        shell.Navigate("armador");
        var builder = (PcBuilderViewModel)shell.Current;
        await builder.LoadAsync(force: true);
        Assert.Contains(builder.Statuses, s => s.Value == PcBuildStatus.Reserved);
        var all = builder.Builds.Cast<PcBuildItem>().ToList();
        var web = all.Where(b => b.IsWeb).ToList();
        Assert.NotEmpty(web);
        var active = Assert.Single(web, b => b.IsReservationActive);   // la demostración trae UNA reserva web vigente (regla S-09)
        Assert.Contains(web, b => b.Row.Status == PcBuildStatus.Cancelled && b.Row.CancelReason == PcBuild.ExpiredReason);   // y una vencida, ya cerrada
        Assert.Equal("Web", active.ChannelText);
        Assert.StartsWith(PcBuild.WebNumberPrefix, active.Number, StringComparison.Ordinal);
        Assert.True(active.HasContact);
        Assert.True(active.HasPhone);   // el administrador gestiona armados: ve el teléfono (regla S-06)
        Assert.NotEqual("—", active.ReservedUntilText);
        Assert.False(active.IsReservationExpired);
        Assert.True(active.CanSell);
        Assert.Equal("1", builder.WebKpi.Value);
        Assert.Contains(Fmt.Money(active.Total), builder.WebKpi.Detail, StringComparison.Ordinal);

        // Filtro rápido «Reservas web»: solo las de la tienda; con «Reservados» queda la vigente
        builder.OnlyWebReservations = true;
        Assert.All(builder.Builds.Cast<PcBuildItem>(), b => Assert.True(b.IsWeb));
        Assert.False(builder.NoBuildsMatch);
        builder.StatusFilter = builder.Statuses.First(s => s.Value == PcBuildStatus.Reserved);
        Assert.Equal([active.Number], builder.Builds.Cast<PcBuildItem>().Select(b => b.Number));
        builder.StatusFilter = builder.Statuses[0];
        builder.OnlyWebReservations = false;
        Assert.Equal(all.Count, builder.Builds.Cast<PcBuildItem>().Count());

        // Detalle de la reserva: contacto con teléfono para copiar, notas y las piezas con su disponibilidad; acciones según el estado
        await builder.OpenBuildAsync(active.Number);
        Assert.Equal(active.Number, builder.Current!.Number);
        Assert.True(builder.IsWebReservation);
        Assert.True(builder.HasContact);
        Assert.True(builder.HasContactPhone);
        Assert.True(builder.CopyPhone.CanExecute(null));
        Assert.True(builder.HasContactNotes);   // los contactos de prueba dejan una nota
        Assert.True(builder.HasReservationLines);
        Assert.Equal(active.Row.Items, builder.ReservationLines.Count);
        Assert.All(builder.ReservationLines, l => Assert.StartsWith("Reservada", l.AvailabilityText, StringComparison.Ordinal));
        Assert.StartsWith("Reservado hasta", builder.ReservationText, StringComparison.Ordinal);
        Assert.False(builder.CanEdit);   // los precios están congelados
        Assert.True(builder.CanRelease);
        Assert.False(builder.CanReserve);
        Assert.False(builder.CanPublish);   // una reserva web no se publica (es del cliente)
        Assert.False(builder.CanCancel);    // se libera, no se anula
        Assert.True(builder.CanSellCurrent);
        Assert.Contains("consume la reserva", builder.CurrentDetail, StringComparison.Ordinal);

        // Inicio › Tecnología: la tarjeta cuenta la misma reserva y abre el armador filtrado
        shell.Navigate("inicio");
        var dashboard = (DashboardViewModel)shell.Current;
        await dashboard.LoadAsync(force: true);
        Assert.True(dashboard.HasTech);
        Assert.Equal("1", dashboard.WebReservationsKpi.Value);
        Assert.Contains(Fmt.Money(active.Total), dashboard.WebReservationsKpi.Detail!, StringComparison.Ordinal);
        dashboard.GoWebReservations.Execute(null);
        Assert.Same(builder, shell.Current);
        Assert.True(builder.IsQuotesTab);
        Assert.True(builder.OnlyWebReservations);
        Assert.All(builder.Builds.Cast<PcBuildItem>(), b => Assert.True(b.IsWeb));
    });

    [Fact]
    public void V6_una_cotizacion_propia_se_reserva_se_publica_y_se_libera_desde_el_armador() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        await TechSeed.CreateAsync(shell.App);
        var number = await QuoteTechSeedBuildAsync(shell);
        var builder = (PcBuilderViewModel)shell.Current;
        Assert.True(builder.CanReserve);
        Assert.True(builder.CanPublish);
        Assert.True(builder.CanCancel);
        Assert.False(builder.CanRelease);
        var gpuBefore = await shell.App.SendAsync(new GetProductCardQuery(TechSeed.Gpu.Sku));
        Assert.Equal(0m, gpuBefore.Reserved);

        // Reservar: pide las horas (48 por defecto); se responde 24 en el cuadro de diálogo
        var reserving = builder.ReserveBuild.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is { HasInput: true });
        Assert.Equal("48", shell.Dialogs.Current!.InputText);
        shell.Dialogs.Current.InputText = "24";
        shell.Dialogs.Current.Confirm.Execute(null);
        await reserving;
        Assert.Equal(PcBuildStatus.Reserved, builder.Current!.Status);
        Assert.NotNull(builder.Current.ReservedUntil);
        Assert.InRange((builder.Current.ReservedUntil!.Value - shell.App.Now).TotalHours, 23.5, 24.5);
        Assert.Equal(7m, builder.Current.Reserved);   // una unidad por pieza: CPU, placa, RAM, SSD, fuente, gabinete y GPU
        Assert.True(builder.CanRelease);
        Assert.False(builder.CanReserve);
        Assert.False(builder.CanCancel);
        Assert.True(builder.CanSellCurrent);
        Assert.True(builder.ShowPublishBeside);   // publicar ocupa el lugar de «Anular»
        Assert.False(builder.HasContact);   // una cotización del escritorio no tiene contacto web
        var gpuReserved = await shell.App.SendAsync(new GetProductCardQuery(TechSeed.Gpu.Sku));
        Assert.Equal(1m, gpuReserved.Reserved);
        Assert.Equal(gpuBefore.OnHand - 1, gpuReserved.Available);   // disponible = existencias − reservado (regla S-03)
        var listed = builder.Builds.Cast<PcBuildItem>().Single(b => b.Number == number);
        Assert.True(listed.IsReserved);
        Assert.False(listed.IsWeb);
        Assert.Equal("Escritorio", listed.ChannelText);
        Assert.False(listed.HighlightReservation);   // 24 h: ni vencida ni por vencer
        Assert.Equal("1", builder.WebKpi.Value);   // la reserva es del escritorio: no suma a la reserva web vigente de la demostración
        Assert.True(int.Parse(builder.QuotedKpi.Value, Fmt.Culture) >= 1);   // las cotizaciones vigentes incluyen la reservada

        // Stock y caja ven lo reservado
        shell.Navigate("stock");
        var stock = (StockViewModel)shell.Current;
        await stock.LoadAsync(force: true);
        var gpuRow = stock.Rows.Cast<StockItem>().Single(i => i.Sku == TechSeed.Gpu.Sku);
        Assert.Equal(1m, gpuRow.Reserved);
        Assert.Equal("Reservado: 1", gpuRow.ReservedText);
        Assert.Equal(gpuBefore.OnHand - 1, gpuRow.Available);
        var chip = stock.Filters.Single(f => ReferenceEquals(f.Value, StockViewModel.ReservedFilter));
        stock.SelectFilter.Execute(chip);
        Assert.All(stock.Rows.Cast<StockItem>(), i => Assert.True(i.HasReserved));
        Assert.Contains(stock.Rows.Cast<StockItem>(), i => i.Sku == TechSeed.Gpu.Sku);
        shell.Navigate("catalogo");
        var catalog = (CatalogViewModel)shell.Current;
        await catalog.LoadAsync(force: true);
        var gpuCard = catalog.Rows.Cast<CatalogProduct>().Single(p => p.Sku == TechSeed.Gpu.Sku);
        Assert.Equal(1m, gpuCard.Reserved);
        Assert.Contains("reservado 1", gpuCard.StockText, StringComparison.Ordinal);
        shell.Navigate("pos");
        var pos = (PosViewModel)shell.Current;
        await pos.LoadAsync(force: true);
        var gpuTile = pos.Products.Cast<PosProduct>().Single(p => p.Sku == TechSeed.Gpu.Sku);
        Assert.Equal(1m, gpuTile.Reserved);
        Assert.Equal($"Disponible {Fmt.Qty(gpuBefore.OnHand - 1)} (reservado 1)", gpuTile.AvailableText);

        // Publicar y quitar de la web
        shell.Navigate("armador");
        await builder.TogglePublish.ExecuteAsync();
        Assert.True(builder.Current!.PublishedToWeb);
        Assert.Equal("Quitar de la web", builder.PublishText);
        Assert.True(builder.Builds.Cast<PcBuildItem>().Single(b => b.Number == number).IsPublished);
        await builder.TogglePublish.ExecuteAsync();
        Assert.False(builder.Current!.PublishedToWeb);
        Assert.Equal("Publicar en la web", builder.PublishText);

        // Liberar: pide el motivo; sin motivo avisa y no libera; con motivo el stock vuelve y el armado queda anulado
        var releasing = builder.ReleaseReservation.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is { HasInput: true });
        shell.Dialogs.Current!.InputText = string.Empty;
        shell.Dialogs.Current.Confirm.Execute(null);
        await releasing;
        Assert.Equal(PcBuildStatus.Reserved, builder.Current!.Status);
        Assert.Contains(shell.Notifications.Items, t => t.Kind == ToastKind.Warning && t.Title == "Falta el motivo");
        releasing = builder.ReleaseReservation.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is { HasInput: true });
        shell.Dialogs.Current!.InputText = "El cliente desistió";
        shell.Dialogs.Current.Confirm.Execute(null);
        await releasing;
        Assert.Equal(PcBuildStatus.Cancelled, builder.Current!.Status);
        Assert.Equal("El cliente desistió", builder.Current.CancelReason);
        Assert.False(builder.CanRelease);
        Assert.Equal(0m, (await shell.App.SendAsync(new GetProductCardQuery(TechSeed.Gpu.Sku))).Reserved);
        // Cada cambio dejó su fila en la bitácora del armado (regla S-04)
        var history = (await shell.App.SendAsync(new GetPcBuildQuery(number))).History!.Select(e => e.Action).ToList();
        Assert.Equal([PcBuildEventAction.Created, PcBuildEventAction.Quoted, PcBuildEventAction.Reserved, PcBuildEventAction.Published, PcBuildEventAction.Unpublished,
            PcBuildEventAction.Released], history);
    });

    [Fact]
    public void V6_la_caja_vende_la_reserva_web_de_la_demostracion_y_consume_la_reserva() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        shell.Navigate("pos");
        var pos = (PosViewModel)shell.Current;
        await pos.LoadAsync(force: true);
        if (pos.IsClosed)
        {
            await pos.OpenSession.ExecuteAsync();
        }
        Assert.True(pos.IsOpen);

        shell.Navigate("armador");
        var builder = (PcBuilderViewModel)shell.Current;
        await builder.LoadAsync(force: true);
        var reservation = builder.Builds.Cast<PcBuildItem>().Single(b => b.IsWeb && b.IsReservationActive);
        Assert.Equal(shell.App.Session.Access.Active?.Code, reservation.Row.BranchCode);   // la tienda reserva en la casa matriz, la sucursal activa del administrador
        var detail = await shell.App.SendAsync(new GetPcBuildQuery(reservation.Number));
        var firstSku = detail.QuotedItems[0].Sku;
        var firstQuantity = detail.QuotedItems.Where(i => i.Sku == firstSku).Sum(i => i.Quantity);
        var before = await shell.App.SendAsync(new GetProductCardQuery(firstSku));
        Assert.True(before.Reserved >= firstQuantity);

        // «Vender en caja» desde la reserva abierta: avisa que la venta consume la reserva y la caja la carga en modo armado
        await builder.OpenBuildAsync(reservation.Number);
        Assert.True(builder.SellInPos.CanExecute(null));
        builder.SellInPos.Execute(null);
        Assert.Contains(shell.Notifications.Items, t => t.Kind == ToastKind.Info && t.Title == "La venta consume la reserva");
        await Wpf.UntilAsync(() => pos.IsBuildMode || shell.Dialogs.Form is SerialsDialog);
        if (shell.Dialogs.Form is SerialsDialog serials)
        {
            foreach (var line in serials.Lines)
            {
                foreach (var option in line.Options.Cast<SerialPickOption>().Take(line.Expected))
                {
                    option.IsChecked = true;
                }
            }
            await serials.Confirm.ExecuteAsync();
        }
        await Wpf.UntilAsync(() => shell.Dialogs.Form is null && pos.IsBuildMode);
        Assert.Same(pos, shell.Current);
        Assert.True(pos.IsReservedBuild);
        Assert.Contains("consume la reserva", pos.BuildDetail, StringComparison.Ordinal);
        Assert.Equal(detail.QuotedItems.Count, pos.Cart.Count);
        Assert.All(pos.Cart, l => Assert.True(l.IsReserved));
        Assert.All(pos.Cart, l => Assert.False(l.ExceedsStock));   // las unidades reservadas ya están apartadas: no «superan lo disponible»
        Assert.Equal(reservation.Total, pos.Total);

        pos.Buyer!.DocumentNumber = "5115889";
        pos.Buyer.Name = "Juan Pérez";
        await pos.Checkout.ExecuteAsync();
        Assert.NotNull(pos.FiscalResult);
        Assert.Equal(FiscalDocumentStatus.Valid, pos.FiscalResult!.Row.Status);
        Assert.Equal(reservation.Total, pos.FiscalResult.Row.Total);
        Assert.False(pos.IsBuildMode);
        Assert.Contains(shell.Notifications.Items, t => t.Kind == ToastKind.Success && t.Message?.Contains("reserva consumida", StringComparison.Ordinal) == true);
        var sold = (await shell.App.SendAsync(new GetPcBuildsQuery(PcBuildStatus.Sold, PcBuildChannel.Web))).Single(b => b.Number == reservation.Number);
        Assert.Equal(pos.FiscalResult.Sale.InvoiceNumber, sold.InvoiceNumber);
        Assert.Equal(0m, sold.Reserved);
        // La reserva se consumió: el stock salió UNA sola vez (regla S-04)
        var after = await shell.App.SendAsync(new GetProductCardQuery(firstSku));
        Assert.Equal(before.Reserved - firstQuantity, after.Reserved);
        Assert.Equal(before.OnHand - firstQuantity, after.OnHand);
        Assert.Equal(before.Available, after.Available);
        var history = (await shell.App.SendAsync(new GetPcBuildQuery(reservation.Number))).History!;
        Assert.Equal(PcBuildEventAction.Sold, history[^1].Action);
        Assert.Contains("reserva consumida", history[^1].Detail, StringComparison.Ordinal);
        await builder.LoadAsync(force: true);
        Assert.Equal("0", builder.WebKpi.Value);
    });

    [Fact]
    public void V6_stock_catalogo_y_caja_muestran_lo_reservado_y_la_caja_no_deja_vender_mas_que_lo_disponible() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        var reserved = (await shell.App.SendAsync(new GetStockReservationsQuery())).ToDictionary(r => r.Sku, r => r.Reserved);
        Assert.NotEmpty(reserved);   // la reserva web vigente de la demostración aparta las piezas de su armado

        shell.Navigate("stock");
        var stock = (StockViewModel)shell.Current;
        await stock.LoadAsync(force: true);
        var withReservations = stock.Rows.Cast<StockItem>().Where(i => i.HasReserved).ToList();
        Assert.Equal(reserved.Count, withReservations.Count);
        Assert.All(withReservations, i => Assert.Equal(reserved[i.Sku], i.Reserved));
        Assert.All(withReservations, i => Assert.Equal(Math.Max(0, i.Stock - i.Reserved), i.Available));
        var chip = stock.Filters.Single(f => ReferenceEquals(f.Value, StockViewModel.ReservedFilter));
        Assert.Equal(withReservations.Count, chip.Count);
        stock.SelectFilter.Execute(chip);
        Assert.Equal(withReservations.Count, stock.VisibleCount);

        shell.Navigate("catalogo");
        var catalog = (CatalogViewModel)shell.Current;
        await catalog.LoadAsync(force: true);
        foreach (var (sku, quantity) in reserved)
        {
            var card = catalog.Rows.Cast<CatalogProduct>().Single(p => p.Sku == sku);
            Assert.Equal(quantity, card.Reserved);
            Assert.Contains($"reservado {Fmt.Qty(quantity)}", card.StockText, StringComparison.Ordinal);
        }

        shell.Navigate("pos");
        var pos = (PosViewModel)shell.Current;
        await pos.LoadAsync(force: true);
        if (pos.IsClosed)
        {
            await pos.OpenSession.ExecuteAsync();
        }
        var serialized = await DemoData.SerializedAsync(shell.App);
        var tiles = pos.Products.Cast<PosProduct>().Where(p => p.HasReserved).ToList();
        Assert.All(tiles, t => Assert.Contains($"reservado {Fmt.Qty(t.Reserved)}", t.AvailableText, StringComparison.Ordinal));
        // Un producto sin serie con reservas y algo disponible: la caja avisa y no cobra más que lo disponible
        var tile = tiles.First(t => !t.IsOut && !serialized.Contains(t.Sku));
        Assert.Equal($"Disponible {Fmt.Qty(tile.Available)} (reservado {Fmt.Qty(tile.Reserved)})", tile.AvailableText);
        pos.Add.Execute(tile);
        var line = Assert.Single(pos.Cart);
        line.Quantity = tile.Available + tile.Reserved;   // pide también lo reservado
        Assert.True(line.ExceedsStock);
        Assert.Contains("reservada", line.ExceedsStockText, StringComparison.Ordinal);
        pos.Buyer!.DocumentNumber = "5115889";
        pos.Buyer.Name = "Juan Pérez";
        await pos.Checkout.ExecuteAsync();
        Assert.Null(pos.FiscalResult);
        Assert.Single(pos.Cart);
        Assert.Contains(shell.Notifications.Items, t => t.Kind == ToastKind.Warning && t.Title == "Stock insuficiente");
        Assert.Equal(tile.Reserved, (await shell.App.SendAsync(new GetProductCardQuery(tile.Sku))).Reserved);   // nada salió
        // Con lo disponible sí se vende
        line.Quantity = tile.Available;
        Assert.False(line.ExceedsStock);
        await pos.Checkout.ExecuteAsync();
        Assert.NotNull(pos.FiscalResult);
        Assert.Equal(FiscalDocumentStatus.Valid, pos.FiscalResult!.Row.Status);
        var card2 = await shell.App.SendAsync(new GetProductCardQuery(tile.Sku));
        Assert.Equal(tile.Reserved, card2.Reserved);   // lo reservado sigue apartado
        Assert.Equal(0m, card2.Available);
    });

    /// <summary>Arma y cotiza en el armador la PC compatible de <see cref="TechSeed"/> (7 piezas) y deja la pantalla en el armador
    /// con esa cotización abierta; devuelve su número.</summary>
    private static async Task<string> QuoteTechSeedBuildAsync(ShellViewModel shell)
    {
        shell.Navigate("armador");
        var builder = (PcBuilderViewModel)shell.Current;
        await builder.LoadAsync(force: true);
        foreach (var (slot, sku) in new[]
                 {
                     (PcSlot.Cpu, TechSeed.Cpu.Sku), (PcSlot.Motherboard, TechSeed.Board.Sku), (PcSlot.Ram, TechSeed.Ram.Sku), (PcSlot.Storage, TechSeed.Ssd.Sku),
                     (PcSlot.Psu, TechSeed.Psu.Sku), (PcSlot.Case, TechSeed.Case.Sku), (PcSlot.Gpu, TechSeed.Gpu.Sku),
                 })
        {
            var target = builder.AllSlots.First(s => s.Slot == slot);
            builder.SelectSlot.Execute(target);
            await Wpf.UntilAsync(() => builder.SelectedSlot == target && !builder.IsLoadingCandidates && builder.Candidates.Any(c => c.Sku == sku));
            builder.AddPart.Execute(builder.Candidates.First(c => c.Sku == sku));
            await Wpf.UntilAsync(() => target.Parts.Any(p => p.Sku == sku) && builder.Check?.Items.Any(i => i.Sku == sku) == true);
        }
        await Wpf.UntilAsync(() => builder.Check is { Items.Count: 7 });
        Assert.True(builder.IsCompatible);
        builder.Name = "PC gamer para reservar";
        await builder.Quote.ExecuteAsync();
        Assert.Equal(PcBuildStatus.Quoted, builder.Current!.Status);
        return builder.Current.Number;
    }
}
