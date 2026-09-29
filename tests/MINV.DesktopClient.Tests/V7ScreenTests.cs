using System.IO;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Integration;
using MINV.Application.Inventory.Queries;
using MINV.Application.Sales;
using MINV.Application.Tech;
using MINV.DesktopClient.Services;
using MINV.DesktopClient.ViewModels;
using MINV.Domain.Billing;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Sales;
using MINV.Infrastructure.Demo;

namespace MINV.DesktopClient.Tests;

/// <summary>
/// V7 · Escritorio (paquete D1): el inicio simplificado (botones por rol y secciones plegables que no leen nada hasta abrirse),
/// la pantalla Reservas (lista, filtros, detalle, liberar, reenviar el correo, nueva reserva en mostrador y venta en la caja con
/// los datos de factura de la reserva), la cola de correos, los filtros de Usuarios y la exportación CSV compartida.
/// </summary>
public sealed class V7ScreenTests
{
    private static readonly string[] Shortcuts = ["nueva-reserva", "registrar-entrada", "registrar-salida"];

    [Fact]
    public void V7_el_inicio_muestra_los_botones_del_rol_y_las_secciones_cerradas_no_leen_datos() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        var home = Assert.IsType<DashboardViewModel>(shell.Current);
        Assert.True(home.HasLoaded);

        // «¿Qué querés hacer?»: un botón por pantalla del menú (sin el inicio) y los atajos del rol, agrupados por sección
        Assert.Equal(shell.AllPages.Where(p => p.Key != "inicio").Select(p => p.Key).Order(StringComparer.Ordinal),
            home.Actions.Select(a => a.Key).Where(k => !Shortcuts.Contains(k)).Order(StringComparer.Ordinal));
        Assert.Equal([.. shell.Sections.Where(s => s.Pages.Any(p => p.Key != "inicio")).Select(s => s.Title), "Ayuda y preferencias"],
            home.Groups.Select(g => g.Title));
        Assert.NotNull(home.Action("nueva-reserva"));
        Assert.NotNull(home.Action("registrar-entrada"));
        Assert.Contains(home.Groups.Single(g => g.Title == "Ventas").Actions, a => a.Key == "reservas");

        // Las secciones empiezan CERRADAS y sin datos (no se leyó nada del inventario)
        Assert.All(home.Sections, s => Assert.False(s.IsOpen || s.IsLoaded || s.IsLoading, s.Key));
        Assert.Equal("—", home.InventoryValue);
        Assert.Empty(home.Recent);
        Assert.Empty(home.Activity);
        Assert.Empty(home.StatusSegments);
        Assert.Equal("Ver", home.Inventory.ToggleText);

        // Abrir una sección lee SOLO esa sección
        await home.Inventory.Toggle.ExecuteAsync();
        Assert.True(home.Inventory.IsOpen && home.Inventory.IsLoaded);
        Assert.Equal("Ocultar", home.Inventory.ToggleText);
        Assert.NotEqual("—", home.InventoryValue);
        Assert.False(home.Charts.IsLoaded);
        Assert.False(home.Lists.IsLoaded);
        Assert.Empty(home.Recent);
        await home.Inventory.Toggle.ExecuteAsync();   // se cierra y conserva lo leído
        Assert.False(home.Inventory.IsOpen);
        Assert.True(home.Inventory.IsLoaded);

        // Un botón navega a su pantalla; el atajo «Nueva reserva en mostrador» abre el formulario en Reservas
        home.Action("reservas")!.Run.Execute(null);
        Assert.IsType<ReservationsViewModel>(shell.Current);

        // Otro rol ve solo sus funciones (Ventas: caja y reservas sí; usuarios y compras no)
        var seller = demo.Users.First(u => u.RoleCode == RoleCodes.Sales);
        using var sales = await host.SignInDemoAsync(demo, seller.Email);
        var salesShell = sales.Services.GetRequiredService<ShellViewModel>();
        await salesShell.StartAsync();
        var salesHome = Assert.IsType<DashboardViewModel>(salesShell.Current);
        Assert.Equal(salesShell.AllPages.Where(p => p.Key != "inicio").Select(p => p.Key).Order(StringComparer.Ordinal),
            salesHome.Actions.Select(a => a.Key).Where(k => !Shortcuts.Contains(k)).Order(StringComparer.Ordinal));
        Assert.NotNull(salesHome.Action("pos"));
        Assert.Null(salesHome.Action("usuarios"));
        Assert.Null(salesHome.Action("compras"));
        Assert.Null(salesHome.Action("registrar-entrada"));   // Ventas no registra entradas de bodega
        Assert.DoesNotContain(salesHome.Groups, g => g.Title == "Administración" && g.Actions.Any(a => a.Key == "usuarios"));
        Assert.All(salesHome.Sections, s => Assert.False(s.IsLoaded, s.Key));
    });

    [Fact]
    public void V7_reservas_lista_filtra_detalla_reenvia_el_correo_libera_y_exporta() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        shell.Navigate("reservas");
        var reservations = Assert.IsType<ReservationsViewModel>(shell.Current);
        await reservations.LoadAsync(force: true);
        var all = reservations.Rows.Cast<ReservationItem>().ToList();
        var builds = await shell.App.SendAsync(new GetPcBuildsQuery());
        // Carritos (web y mostrador) y armados que llegaron a reservarse
        Assert.Equal(builds.Count(b => b.Kind == PcBuildKind.Cart || b.ReservedUntil is not null), all.Count);
        Assert.Contains(all, r => r.IsCart && r.IsWeb);
        Assert.Contains(all, r => !r.IsCart && r.IsWeb);
        Assert.Equal(demo.Seed.Web!.CounterCarts, all.Count(r => r.IsCart && !r.IsWeb));
        // Primero las reservadas, la que vence antes arriba
        var reserved = all.TakeWhile(r => r.IsReserved).ToList();
        Assert.Equal(all.Count(r => r.IsReserved), reserved.Count);
        Assert.Equal(reserved.Select(r => r.ReservedUntilSort).Order(), reserved.Select(r => r.ReservedUntilSort));
        Assert.Equal(all.Count(r => r.IsActive).ToString("N0", Fmt.Culture), reservations.ActiveKpi.Value);

        // Filtros en listas desplegables: tipo, canal, estado y búsqueda (por teléfono, sin espacios)
        reservations.Kind = reservations.Kinds.First(k => k.Value == PcBuildKind.Cart);
        Assert.All(reservations.Rows.Cast<ReservationItem>(), r => Assert.Equal("Compra", r.KindText));
        reservations.Channel = reservations.Channels.First(c => c.Value == PcBuildChannel.Desktop);
        Assert.All(reservations.Rows.Cast<ReservationItem>(), r => Assert.Equal("Mostrador", r.ChannelText));
        Assert.True(reservations.HasFilters);
        reservations.ClearFilters.Execute(null);
        Assert.False(reservations.HasFilters);
        Assert.Equal(all.Count, reservations.VisibleCount);
        reservations.State = reservations.States.First(s => s.Value == ReservationState.Released);
        Assert.All(reservations.Rows.Cast<ReservationItem>(), r => Assert.Equal("Liberada", r.StatusText));
        reservations.ClearFilters.Execute(null);
        var target = all.First(r => r.IsActive && r.IsWeb && r.HasPhone && r.HasEmail);
        reservations.Search = target.Phone;
        await Wpf.UntilAsync(() => reservations.VisibleCount < all.Count);
        Assert.Contains(reservations.Rows.Cast<ReservationItem>(), r => r.Number == target.Number);
        reservations.ClearFilters.Execute(null);

        // Detalle: productos, bitácora y correos de la reserva
        reservations.Selected = reservations.Rows.Cast<ReservationItem>().Single(r => r.Number == target.Number);
        await Wpf.UntilAsync(() => reservations.Detail?.Build.Number == target.Number && !reservations.IsLoadingDetail);
        Assert.Equal(target.Row.Items, reservations.Lines.Count);
        Assert.NotEmpty(reservations.History);
        Assert.True(reservations.CopyPhone.CanExecute(null));
        Assert.True(reservations.Release.CanExecute(null));

        // Reenviar el correo a OTRO correo (el cliente escribió mal el suyo): queda en la cola para ese destinatario
        var resending = reservations.ResendMail.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is { HasInput: true });
        Assert.Equal(target.Email, shell.Dialogs.Current!.InputText);
        shell.Dialogs.Current.InputText = "otro.correo@cliente.example";
        shell.Dialogs.Current.Confirm.Execute(null);
        await resending;
        Assert.Contains(shell.Notifications.Items, t => t.Kind == ToastKind.Success && t.Title == "Correo en cola");
        var queued = await shell.App.SendAsync(new GetOutgoingMailsQuery(Number: target.Number));
        Assert.Contains(queued, m => m.Recipient == "otro.correo@cliente.example" && m.Status == OutgoingMailStatus.Pending);

        // Liberar: sin motivo avisa y no libera; con motivo el stock vuelve y la reserva queda «Liberada»
        reservations.Selected = reservations.Rows.Cast<ReservationItem>().Single(r => r.Number == target.Number);
        var detail = await shell.App.SendAsync(new GetPcBuildQuery(target.Number));
        var sku = detail.QuotedItems[0].Sku;
        var before = await shell.App.SendAsync(new GetProductCardQuery(sku));
        var releasing = reservations.Release.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is { HasInput: true });
        shell.Dialogs.Current!.InputText = string.Empty;
        shell.Dialogs.Current.Confirm.Execute(null);
        await releasing;
        Assert.Contains(shell.Notifications.Items, t => t.Kind == ToastKind.Warning && t.Title == "Falta el motivo");
        Assert.Equal(PcBuildStatus.Reserved, (await shell.App.SendAsync(new GetPcBuildQuery(target.Number))).Build.Status);
        releasing = reservations.Release.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is { HasInput: true });
        shell.Dialogs.Current!.InputText = "El cliente desistió";
        shell.Dialogs.Current.Confirm.Execute(null);
        await releasing;
        var released = reservations.Rows.Cast<ReservationItem>().Single(r => r.Number == target.Number);
        Assert.Equal(ReservationState.Released, released.State);
        Assert.False(released.CanSell);
        var after = await shell.App.SendAsync(new GetProductCardQuery(sku));
        Assert.Equal(before.Reserved - detail.QuotedItems.Where(i => i.Sku == sku).Sum(i => i.Quantity), after.Reserved);

        // Exportar CSV: las filas visibles, con «;», UTF-8 con BOM y los encabezados en español
        var path = Path.Combine(Path.GetTempPath(), $"minv-reservas-{Guid.NewGuid():N}.csv");
        shell.Dialogs.AskCsvPath = _ => path;
        try
        {
            reservations.State = reservations.States.First(s => s.Value == ReservationState.Active);
            reservations.Export.Execute(null);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            var lines = File.ReadAllLines(path);
            Assert.StartsWith("Número;Tipo;Canal;Sucursal;Cliente", lines[0], StringComparison.Ordinal);
            Assert.Equal(reservations.VisibleCount + 1, lines.Length);
            Assert.DoesNotContain(lines, l => l.StartsWith(target.Number + ";", StringComparison.Ordinal));   // ya no está reservada
            Assert.Contains(shell.Notifications.Items, t => t.Kind == ToastKind.Success && t.Title == "CSV guardado"
                                                             && t.Message?.StartsWith("Reservas: ", StringComparison.Ordinal) == true);
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public void V7_nueva_reserva_en_mostrador_y_la_caja_la_cobra_con_los_datos_de_factura() => Wpf.Run(async () =>
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

        shell.Navigate("reservas");
        var reservations = (ReservationsViewModel)shell.Current;
        await reservations.LoadAsync(force: true);
        var serialized = await DemoData.SerializedAsync(shell.App);
        var product = (await shell.App.SendAsync(new GetSellableProductsQuery()))
            .First(p => p.Available >= 2 && !serialized.Contains(p.Sku) && p.Price > 0);
        var before = await shell.App.SendAsync(new GetProductCardQuery(product.Sku));

        // El formulario: cliente, teléfono, correo, productos con buscador, días para recoger y datos de factura
        var creating = reservations.NewReservation.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Form is CounterReservationDialog);
        var form = (CounterReservationDialog)shell.Dialogs.Form!;
        Assert.False(form.Confirm.CanExecute(null));   // sin productos ni contacto no se puede reservar
        form.ContactName = "Mariana Rojas";
        form.ContactPhone = "70012345";
        form.ContactEmail = "mariana@cliente.example";
        form.Search = product.Sku;
        Assert.Contains(form.Suggestions, s => s.Sku == product.Sku);
        form.AddProduct.Execute(form.Suggestions.First(s => s.Sku == product.Sku));
        Assert.Equal(string.Empty, form.Search);
        form.Lines.Single().Increase.Execute(null);
        Assert.Equal(2, form.Lines.Single().Quantity);
        Assert.Equal(Fmt.Money(product.Price * 2), form.TotalText);
        form.HoldDays = form.HoldOptions.First(h => h.Value == 3);
        form.DocumentType = form.DocumentTypes.First(d => d.Value == SiatCodes.DocumentCi);
        form.DocumentNumber = "5115889";
        form.BuyerName = "Juan Pérez";
        await form.Confirm.ExecuteAsync();
        await creating;
        Assert.Null(shell.Dialogs.Form);

        // Queda elegida en la lista: mostrador, reservada, con los datos de factura; el stock quedó apartado
        await Wpf.UntilAsync(() => reservations.Selected is not null && !reservations.IsBusy);
        var created = reservations.Selected!;
        Assert.StartsWith("RES-", created.Number, StringComparison.Ordinal);
        Assert.Equal("Mostrador", created.ChannelText);
        Assert.Equal("Compra", created.KindText);
        Assert.True(created.IsActive);
        Assert.True(created.HasBuyer);
        Assert.Equal("Juan Pérez", created.BuyerNameText);
        Assert.InRange((created.Row.ReservedUntil!.Value - shell.App.Now).TotalDays, 2.9, 3.1);
        Assert.Contains(shell.Notifications.Items, t => t.Kind == ToastKind.Success && t.Title == $"Reserva {created.Number} creada");
        var reservedCard = await shell.App.SendAsync(new GetProductCardQuery(product.Sku));
        Assert.Equal(before.Reserved + 2, reservedCard.Reserved);

        // Vender en caja: la caja carga la reserva y precarga el comprador con los datos de factura de la reserva
        Assert.True(reservations.SellInPos.CanExecute(null));
        reservations.SellInPos.Execute(null);
        Assert.Contains(shell.Notifications.Items, t => t.Kind == ToastKind.Info && t.Title == "La venta consume la reserva");
        await Wpf.UntilAsync(() => pos.IsBuildMode);
        Assert.Same(pos, shell.Current);
        Assert.True(pos.IsReservedBuild);
        Assert.Equal("5115889", pos.Buyer!.DocumentNumber);
        Assert.Equal("Juan Pérez", pos.Buyer.Name);
        Assert.Contains("precargados", pos.BuildDetail, StringComparison.Ordinal);
        await pos.Checkout.ExecuteAsync();
        Assert.NotNull(pos.FiscalResult);
        Assert.Equal(FiscalDocumentStatus.Valid, pos.FiscalResult!.Row.Status);
        Assert.Contains("5115889", pos.FiscalResult.Row.BuyerDocument, StringComparison.Ordinal);
        Assert.Equal("Juan Pérez", pos.FiscalResult.Row.BuyerName);
        var sold = await shell.App.SendAsync(new GetPcBuildQuery(created.Number));
        Assert.Equal(PcBuildStatus.Sold, sold.Build.Status);
        var soldCard = await shell.App.SendAsync(new GetProductCardQuery(product.Sku));
        Assert.Equal(before.Reserved, soldCard.Reserved);   // la reserva se consumió
        Assert.Equal(before.OnHand - 2, soldCard.OnHand);   // y el stock salió UNA sola vez
    });

    [Fact]
    public void V7_la_cola_de_correos_filtra_abre_la_reserva_y_exporta() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        shell.Navigate("correos");
        var mails = Assert.IsType<MailQueueViewModel>(shell.Current);
        await mails.LoadAsync(force: true);
        Assert.False(mails.HasError, mails.ErrorMessage);
        var all = mails.Rows.Cast<MailQueueItem>().ToList();
        Assert.NotEmpty(all);
        Assert.Equal(all.Count(m => m.Status == OutgoingMailStatus.Pending).ToString("N0", Fmt.Culture), mails.PendingKpi.Value);

        mails.Status = mails.Statuses.First(s => s.Value == OutgoingMailStatus.Pending);
        Assert.All(mails.Rows.Cast<MailQueueItem>(), m => Assert.Equal(OutgoingMailStatus.Pending, m.Status));
        Assert.True(mails.HasFilters);
        mails.ClearFilters.Execute(null);
        Assert.Equal(all.Count, mails.VisibleCount);

        // Abrir la reserva del correo: Reservas la muestra elegida
        var mail = all.First();
        mails.Selected = mail;
        mails.OpenReservation.Execute(null);
        var reservations = Assert.IsType<ReservationsViewModel>(shell.Current);
        await Wpf.UntilAsync(() => reservations.HasLoaded && !reservations.IsBusy && reservations.Selected is not null);
        Assert.Equal(mail.Reservation, reservations.Selected!.Number);

        // Exportar: si se cancela el cuadro de guardar, no se escribe nada
        shell.Dialogs.AskCsvPath = _ => null;
        Assert.Null(shell.App.ExportCsv("correos.csv", "Cola de correos", mails.ExportTable()));
        var path = Path.Combine(Path.GetTempPath(), $"minv-correos-{Guid.NewGuid():N}.csv");
        shell.Dialogs.AskCsvPath = _ => path;
        try
        {
            Assert.Equal(path, shell.App.ExportCsv("correos.csv", "Cola de correos", mails.ExportTable()));
            Assert.Equal(all.Count + 1, File.ReadAllLines(path).Length);
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public void V7_usuarios_separa_el_personal_de_los_clientes_web_y_la_cuenta_tecnica() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        shell.Navigate("usuarios");
        var users = (UsersViewModel)shell.Current;
        await users.LoadAsync(force: true);

        // Por defecto, el personal (sin las cuentas de la tienda web)
        Assert.Equal("Personal", users.Kind.Label);
        Assert.False(users.HasFilters);
        var staff = users.Rows.Cast<UserItem>().ToList();
        Assert.NotEmpty(staff);
        Assert.All(staff, u => Assert.True(u.IsStaff, u.Email));
        Assert.All(staff, u => Assert.True(users.Edit.CanExecute(u)));

        users.Kind = users.Kinds.First(k => k.Value == UserKind.Customer);
        var customers = users.Rows.Cast<UserItem>().ToList();
        Assert.Equal(demo.Seed.Web!.Accounts, customers.Count);
        Assert.All(customers, u => Assert.Equal("Cliente web", u.KindText));
        Assert.All(customers, u => Assert.False(users.Edit.CanExecute(u)));   // la cuenta la maneja el cliente desde la tienda
        Assert.True(users.HasFilters);

        users.Kind = users.Kinds.First(k => k.Value == UserKind.Technical);
        var technical = Assert.Single(users.Rows.Cast<UserItem>());
        Assert.False(users.Edit.CanExecute(technical));
        Assert.False(users.ResetPassword.CanExecute(technical));   // la usa el servidor de la tienda: no se le cambia la clave

        users.ClearFilters.Execute(null);
        Assert.Equal("Personal", users.Kind.Label);
        Assert.Equal(staff.Count, users.Rows.Cast<UserItem>().Count());
        Assert.Equal(staff.Count, users.ExportTable().Count);

        // El alta ofrece solo roles del personal
        users.New.Execute(null);
        Assert.NotNull(users.Editor);
        Assert.DoesNotContain(users.Editor!.Roles, r => r.Value is RoleCodes.Customer or RoleCodes.Storefront);
    });

    [Fact]
    public void V7_filtros_y_exportacion_en_stock_transferencias_series_garantias_documentos_y_actividad() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();

        // Stock: proveedor en lista desplegable, «Limpiar filtros» y la exportación de lo visible
        shell.Navigate("stock");
        var stock = (StockViewModel)shell.Current;
        await stock.LoadAsync(force: true);
        var total = stock.VisibleCount;
        var supplier = stock.Suppliers.First(s => s.Value is not null);
        stock.Supplier = supplier;
        Assert.True(stock.VisibleCount < total);
        Assert.True(stock.HasFilters);
        Assert.Equal(stock.VisibleCount, stock.ExportTable().Count);
        stock.ClearFilters.Execute(null);
        Assert.False(stock.HasFilters);
        Assert.Equal(total, stock.VisibleCount);

        // Transferencias: origen y destino en listas; el panel de alta se abre (antes IsCreating no se avisaba)
        shell.Navigate("transferencias");
        var transfers = (TransfersViewModel)shell.Current;
        await transfers.LoadAsync(force: true);
        var allTransfers = transfers.Rows.Cast<TransferItem>().Count();
        var from = transfers.FromBranches.First(b => b.Value is not null);
        transfers.From = from;
        Assert.All(transfers.Rows.Cast<TransferItem>(), t => Assert.Equal(from.Value, t.Row.FromBranch));
        Assert.Equal(transfers.Rows.Cast<TransferItem>().Count(), transfers.ExportTable().Count);
        transfers.ClearFilters.Execute(null);
        Assert.Equal(allTransfers, transfers.Rows.Cast<TransferItem>().Count());
        var changed = new List<string?>();
        transfers.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        await transfers.New.ExecuteAsync();
        Assert.True(transfers.IsCreating);
        Assert.Contains(nameof(TransfersViewModel.IsCreating), changed);

        // Series: la garantía como filtro; «Limpiar filtros» vuelve a mostrar todo
        shell.Navigate("series");
        var serials = (SerialsViewModel)shell.Current;
        await serials.LoadAsync(force: true);
        var allSerials = serials.Rows.Cast<SerialItem>().Count();
        serials.Warranty = serials.Warranties.First(w => w.Value == "valid");
        Assert.All(serials.Rows.Cast<SerialItem>(), s => Assert.True(s.Row.WarrantyUntil >= shell.App.Session.Workspace.Today));
        Assert.True(serials.HasFilters);
        serials.ClearFilters.Execute(null);
        await Wpf.UntilAsync(() => !serials.IsBusy && serials.Rows.Cast<SerialItem>().Count() == allSerials);

        // Garantías: cobertura y búsqueda
        shell.Navigate("garantias");
        var claims = (WarrantyClaimsViewModel)shell.Current;
        await claims.LoadAsync(force: true);
        claims.ClearFilters.Execute(null);   // todos los casos (por defecto, los abiertos)
        var allClaims = claims.Rows.Cast<WarrantyClaimItem>().Count();
        claims.Coverage = claims.Coverages.First(c => c.Value == false);
        Assert.All(claims.Rows.Cast<WarrantyClaimItem>(), c => Assert.Equal("Con cargo", c.CoverageText));
        claims.Coverage = claims.Coverages[0];
        var first = claims.Rows.Cast<WarrantyClaimItem>().First();
        claims.Search = first.Number;
        Assert.Contains(claims.Rows.Cast<WarrantyClaimItem>(), c => c.Number == first.Number);
        Assert.True(claims.Rows.Cast<WarrantyClaimItem>().Count() <= allClaims);

        // Documentos fiscales: sucursal y emisión en listas, sobre lo que trae el período
        shell.Navigate("documentos-fiscales");
        var documents = (FiscalDocumentsViewModel)shell.Current;
        await documents.LoadAsync(force: true);
        var branch = documents.Branches.First(b => b.Value is not null);
        documents.Branch = branch;
        Assert.All(documents.Rows.Cast<FiscalDocumentItem>(), d => Assert.Equal(branch.Value, d.Row.BranchCode));
        documents.Emission = documents.Emissions.First(e => e.Value == SiatCodes.EmissionOnline);
        Assert.All(documents.Rows.Cast<FiscalDocumentItem>(), d => Assert.False(d.IsOffline));
        Assert.Equal(documents.Rows.Cast<FiscalDocumentItem>().Count(), documents.ExportTable().Count);
        Assert.True(documents.HasFilters);

        // Actividad: usuario y acción en listas; las acciones de las reservas tienen nombre en español
        shell.Navigate("actividad");
        var activity = (ActivityViewModel)shell.Current;
        await activity.LoadAsync(force: true);
        var user = activity.Users.First(u => u.Value is not null);
        activity.User = user;
        Assert.All(activity.Rows.Cast<ActivityItem>(), a => Assert.Equal(user.Value, a.UserName));
        activity.ClearFilters.Execute(null);
        Assert.False(activity.HasFilters);
        Assert.DoesNotContain(activity.ActionsFilter, a => a.Value is not null && a.Label.Contains("pc build", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Reservó un carrito", Fmt.Action("ReserveCart"));
        Assert.Equal("Liberó una reserva", Fmt.Action("ReleasePcBuildReservation"));
    });

    [Fact]
    public void V7_el_tablero_Tecnologia_cuenta_como_cotizaciones_solo_los_armados() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        var builds = await shell.App.SendAsync(new GetPcBuildsQuery());
        var dashboard = await shell.App.SendAsync(new GetTechDashboardQuery(30));
        // Los carritos reservados (RES-…) no son cotizaciones de armados: antes inflaban «Armados cotizados»
        Assert.Contains(builds, b => b.Kind == PcBuildKind.Cart && b.Status == PcBuildStatus.Reserved);
        var quotes = builds.Where(b => b.Kind == PcBuildKind.Build && b.Status is PcBuildStatus.Quoted or PcBuildStatus.Reserved && !b.IsExpired).ToList();
        Assert.Equal(quotes.Count, dashboard.QuotesOpen);
        Assert.Equal(quotes.Sum(b => b.Total), dashboard.QuotesValue);

        // Armador › Cotizaciones lista solo armados
        shell.Navigate("armador");
        var builder = (PcBuilderViewModel)shell.Current;
        await builder.LoadAsync(force: true);
        Assert.All(builder.Builds.Cast<PcBuildItem>(), b => Assert.Equal(PcBuildKind.Build, b.Row.Kind));
    });
}
