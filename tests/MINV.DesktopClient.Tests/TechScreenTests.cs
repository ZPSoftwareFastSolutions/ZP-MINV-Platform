using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Billing;
using MINV.Application.Sales;
using MINV.Application.Tech;
using MINV.DesktopClient.Services;
using MINV.DesktopClient.ViewModels;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Service;
using MINV.Infrastructure.Demo;

namespace MINV.DesktopClient.Tests;

/// <summary>
/// V4.2 · Pantallas de la edición Tecnología con la demostración de Tech Zone Gaming (series, RMA y armados de verdad) y un
/// catálogo técnico mínimo y controlado creado con los casos de uso (<see cref="TechSeed"/>): las páginas nuevas cargan con
/// los datos de la demostración, la caja vende una unidad con IMEI y la factura válida la lleva con la garantía, el armador
/// cotiza piezas compatibles y la cotización se cobra en la caja, y un caso RMA se abre desde una serie vendida, avanza y se
/// repone con otra unidad. El administrador abre la caja libre que la pantalla preselecciona (las demás tienen el turno de
/// su cajero).
/// </summary>
public sealed class TechScreenTests
{
    [Fact]
    public void V42_las_paginas_nuevas_se_construyen_y_cargan_con_la_demostracion() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        var tech = shell.Sections.Single(s => s.Title == "Tecnología");
        Assert.Equal(["armador", "series", "garantias"], tech.Pages.Select(p => p.Key));
        foreach (var key in new[] { "armador", "series", "garantias", "inicio", "catalogo", "pos" })
        {
            shell.Navigate(key);
            await shell.Current.LoadAsync(force: true);
            Assert.True(shell.Current.HasLoaded, key);
            Assert.False(shell.Current.HasError, $"{key}: {shell.Current.ErrorMessage}");
        }
        // Tech Zone Gaming trae armados cotizados y vendidos, series e IMEI en stock y casos RMA en varios estados
        var builder = (PcBuilderViewModel)shell.AllPages.First(p => p.Key == "armador");
        Assert.Equal(8, builder.Slots.Count);
        Assert.False(builder.NoBuilds);
        Assert.Contains(builder.Builds.Cast<PcBuildItem>(), b => b.Row.Status == PcBuildStatus.Sold);
        var serials = (SerialsViewModel)shell.AllPages.First(p => p.Key == "series");
        Assert.False(serials.IsEmpty);
        Assert.Contains(serials.Rows.Cast<SerialItem>(), i => i.Status == SerialNumberStatus.Sold);
        var claims = (WarrantyClaimsViewModel)shell.AllPages.First(p => p.Key == "garantias");
        Assert.False(claims.IsEmpty);
        Assert.Contains(claims.Rows.Cast<WarrantyClaimItem>(), c => c.IsOpen);
        var dashboard = (DashboardViewModel)shell.AllPages.First(p => p.Key == "inicio");
        Assert.False(dashboard.Tech.IsLoaded);   // V7: el tablero Tecnología es una sección plegable que se lee al abrirla
        await dashboard.Tech.OpenAsync();
        Assert.True(dashboard.HasTech);
        Assert.NotEqual("0", dashboard.SerialsKpi.Value);
    });

    [Fact]
    public void V42_el_menu_Tecnologia_muestra_a_cada_rol_solo_las_paginas_que_puede_leer_y_cargan() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        // Una página del menú exige el permiso de su lista: el armador lee las cotizaciones (ventas) y el catálogo (stock);
        // series y garantías, las series (matriz de roles de la V4.2, AccessCatalog.ForRole)
        var expected = new Dictionary<string, string[]>
        {
            [RoleCodes.Sales] = ["armador", "series", "garantias"],
            [RoleCodes.Cashier] = ["armador", "series", "garantias"],
            [RoleCodes.Management] = ["armador", "series", "garantias"],
            [RoleCodes.Warehouse] = ["series", "garantias"],
            [RoleCodes.ReadOnly] = ["series", "garantias"],
        };
        var roles = demo.Users.Where(u => expected.ContainsKey(u.RoleCode)).GroupBy(u => u.RoleCode).Select(g => g.First()).ToList();
        Assert.Contains(roles, u => u.RoleCode == RoleCodes.Sales);
        Assert.Contains(roles, u => u.RoleCode == RoleCodes.Warehouse);
        foreach (var user in roles)
        {
            using var session = await host.SignInDemoAsync(demo, user.Email);
            var shell = session.Services.GetRequiredService<ShellViewModel>();
            await shell.StartAsync();
            var tech = shell.Sections.SingleOrDefault(s => s.Title == "Tecnología")?.Pages.Select(p => p.Key).ToArray() ?? [];
            Assert.Equal(expected[user.RoleCode], tech);
            foreach (var key in tech)
            {
                shell.Navigate(key);
                await shell.Current.LoadAsync(force: true);
                Assert.False(shell.Current.HasError, $"{user.RoleCode} · {key}: {shell.Current.ErrorMessage}");
            }
            if (user.RoleCode == RoleCodes.Cashier && demo.Seed.To.DayOfWeek != DayOfWeek.Sunday)
            {
                // El cajero de la casa matriz tiene su turno abierto: la caja preseleccionada es la suya (no la primera). Los
                // domingos la tienda no abre (la carga no registra turnos ese día): no hay turno que comprobar.
                shell.Navigate("pos");
                var pos = (PosViewModel)shell.Current;
                await pos.LoadAsync(force: true);
                Assert.True(pos.IsOpen);
                Assert.Equal(pos.Session!.RegisterCode, pos.Register?.Code);
            }
        }
    });

    [Fact]
    public void V42_caja_vende_una_unidad_con_IMEI_y_la_factura_valida_lleva_el_IMEI_y_la_garantia() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        await TechSeed.CreateAsync(shell.App);

        shell.Navigate("pos");
        var pos = (PosViewModel)shell.Current;
        await pos.LoadAsync(force: true);
        Assert.True(pos.IsClosed);
        // La caja libre: CAJA01 y CAJA02 tienen el turno de su cajero (los domingos la tienda no abre y todas están libres)
        var freeRegister = demo.Seed.To.DayOfWeek == DayOfWeek.Sunday ? "CAJA01" : "CAJA03";
        Assert.Equal(freeRegister, pos.Register?.Code);
        await pos.OpenSession.ExecuteAsync();
        Assert.True(pos.IsOpen);
        Assert.Equal(freeRegister, pos.Session!.RegisterCode);
        Assert.True(pos.IsBilling);
        Assert.Contains(pos.PlatformChips, c => c.Label == "PS5");   // chips de las opciones de la especificación «plataforma»
        Assert.True(pos.HasManyCategories);   // muchas categorías: los chips se pliegan a dos filas y se despliegan a pedido
        Assert.True(pos.CollapseCategories);
        pos.ToggleCategories.Execute(null);
        Assert.False(pos.CollapseCategories);
        Assert.Equal("Ver menos categorías", pos.MoreCategoriesText);
        var phone = pos.Products.Cast<PosProduct>().Single(p => p.Sku == TechSeed.Phone.Sku);
        Assert.True(phone.IsSerialized);
        Assert.Equal("IMEI", phone.SerialBadge);

        // Agregar un producto serializado pide la unidad: el escáner la marca en el formulario
        pos.Add.Execute(phone);
        await Wpf.UntilAsync(() => shell.Dialogs.Form is SerialsDialog);
        var dialog = (SerialsDialog)shell.Dialogs.Form!;
        Assert.False(dialog.Confirm.CanExecute(null));
        shell.OnScanned("35209900000000");   // IMEI que no está en la sucursal: guía, no se marca
        Assert.True(dialog.Lines[0].MessageIsError);
        var imei = TechSeed.Phone.Serials[1];
        shell.OnScanned(imei);
        Assert.Equal([imei], dialog.Lines[0].Serials);
        await dialog.Confirm.ExecuteAsync();
        await Wpf.UntilAsync(() => pos.Cart.Count == 1);
        var line = pos.Cart[0];
        Assert.Equal(1m, line.Quantity);
        Assert.Equal([imei], line.Serials);
        Assert.False(line.MissingSerials);

        pos.Buyer!.DocumentNumber = "5115889";
        pos.Buyer.Name = "Juan Pérez";
        await pos.Checkout.ExecuteAsync();
        Assert.NotNull(pos.FiscalResult);
        var result = pos.FiscalResult!;
        Assert.Equal(FiscalDocumentStatus.Valid, result.Row.Status);
        Assert.Contains("IMEI: " + imei, result.TicketText, StringComparison.Ordinal);
        var until = shell.App.Session.Workspace.Today.AddMonths(12);   // fecha de la venta + 12 meses del producto (T-04)
        Assert.Contains(TechPrint.Warranty(until), result.TicketText, StringComparison.Ordinal);
        Assert.Equal(until, result.Sale.Lines.Single(l => l.SerialsText == "IMEI: " + imei).WarrantyUntil);
        var detail = await shell.App.SendAsync(new GetFiscalDocumentQuery(result.Row.Id));
        Assert.Contains($"<numeroImei>{imei}</numeroImei>", detail.Xml, StringComparison.Ordinal);
        Assert.Contains(detail.Lines, l => l.SerialsText == "IMEI: " + imei);   // el detalle de «Documentos fiscales» también la muestra
        var model = await shell.App.SendAsync(new GetFiscalPrintModelQuery(result.Row.Id));
        Assert.Equal(until, model.Lines.Single(l => l.SerialsText == "IMEI: " + imei).WarrantyUntil);   // rollo fiscal y PDF

        // La serie quedó vendida con la MISMA garantía derivada que imprimen el ticket y la factura (T-04)
        var status = await shell.App.SendAsync(new GetWarrantyStatusQuery(imei));
        Assert.Equal(SerialNumberStatus.Sold, status.Status);
        Assert.True(status.InWarranty);
        Assert.Equal(12, status.WarrantyMonths);
        Assert.Equal(until, status.WarrantyUntil);

        // Devolución por falla: se marcan las series de ESA venta (no una cantidad) y la unidad no vuelve al stock vendible
        var returning = SalesReturnDialog.OpenAsync(shell.App, result.Sale.InvoiceNumber);
        await Wpf.UntilAsync(() => shell.Dialogs.Form is SalesReturnDialog);
        var devolution = (SalesReturnDialog)shell.Dialogs.Form!;
        Assert.True(devolution.HasSerials);
        var returned = devolution.Lines.Single(l => l.Line.Sku == TechSeed.Phone.Sku);
        Assert.Equal([imei], returned.SerialOptions.Select(o => o.Serial));
        devolution.Reason = "Producto con falla";
        Assert.False(devolution.Confirm.CanExecute(null));   // sin series marcadas no devuelve nada
        returned.SerialOptions[0].IsChecked = true;
        Assert.Equal(1m, returned.Parsed);
        devolution.Defective = true;
        await devolution.Confirm.ExecuteAsync();
        Assert.Null(devolution.Error);
        Assert.NotNull(await returning);
        var back = await shell.App.SendAsync(new GetSerialTraceQuery(imei));
        Assert.Contains(back.Serial.Status, new[] { SerialNumberStatus.Returned, SerialNumberStatus.InRma });
        Assert.DoesNotContain(await shell.App.SendAsync(new GetAvailableSerialsQuery(TechSeed.Phone.Sku)), r => r.Serial == imei);
    });

    [Fact]
    public void V42_armador_con_piezas_compatibles_se_cotiza_y_se_vende_en_la_caja() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        await TechSeed.CreateAsync(shell.App);
        shell.Navigate("pos");
        var pos = (PosViewModel)shell.Current;
        await pos.LoadAsync(force: true);
        if (pos.IsClosed)
        {
            await pos.OpenSession.ExecuteAsync();
        }

        shell.Navigate("armador");
        var builder = (PcBuilderViewModel)shell.Current;
        await builder.LoadAsync(force: true);
        async Task PickAsync(PcSlot slot, string sku)
        {
            var target = builder.AllSlots.First(s => s.Slot == slot);
            builder.SelectSlot.Execute(target);
            await Wpf.UntilAsync(() => builder.SelectedSlot == target && !builder.IsLoadingCandidates && builder.Candidates.Any(c => c.Sku == sku));
            var candidate = builder.Candidates.First(c => c.Sku == sku);
            Assert.True(candidate.IsCompatible, $"{sku}: {candidate.Reason}");
            builder.AddPart.Execute(candidate);
            await Wpf.UntilAsync(() => target.Parts.Any(p => p.Sku == sku) && builder.Check?.Items.Any(i => i.Sku == sku) == true);
        }
        await PickAsync(PcSlot.Cpu, TechSeed.Cpu.Sku);
        await PickAsync(PcSlot.Motherboard, TechSeed.Board.Sku);

        // Con la placa AM5 elegida, el procesador Intel aparece atenuado con el motivo (lo decide el dominio)
        builder.SelectSlot.Execute(builder.Slots[0]);
        await Wpf.UntilAsync(() => !builder.IsLoadingCandidates && builder.Candidates.Any(c => c.Sku == TechSeed.CpuIntel.Sku));
        var intel = builder.Candidates.First(c => c.Sku == TechSeed.CpuIntel.Sku);
        Assert.True(intel.IsIncompatible);
        Assert.Contains("socket", intel.Reason, StringComparison.OrdinalIgnoreCase);

        await PickAsync(PcSlot.Ram, TechSeed.Ram.Sku);
        await PickAsync(PcSlot.Storage, TechSeed.Ssd.Sku);
        await PickAsync(PcSlot.Psu, TechSeed.Psu.Sku);
        await PickAsync(PcSlot.Case, TechSeed.Case.Sku);
        await PickAsync(PcSlot.Gpu, TechSeed.Gpu.Sku);

        // Extras por categoría: los candidatos muestran su plataforma (opciones de la especificación, T-07)
        builder.SelectSlot.Execute(builder.Extras.First(s => s.Slot == PcSlot.Peripheral));
        builder.ExtraCategory = builder.Categories.First(c => c.Value == "TZCON");
        await Wpf.UntilAsync(() => !builder.IsLoadingCandidates && builder.Candidates.Any(c => c.Sku == TechSeed.Console.Sku));
        Assert.Equal(["PS5"], builder.Candidates.First(c => c.Sku == TechSeed.Console.Sku).Platforms);
        // Cada ranura extra recuerda su categoría: la de periféricos no se arrastra al monitor
        builder.SelectSlot.Execute(builder.Extras.First(s => s.Slot == PcSlot.Monitor));
        await Wpf.UntilAsync(() => builder.SelectedSlot.Slot == PcSlot.Monitor && !builder.IsLoadingCandidates);
        Assert.NotEqual("TZCON", builder.ExtraCategory?.Value);
        builder.SelectSlot.Execute(builder.Extras.First(s => s.Slot == PcSlot.Peripheral));
        await Wpf.UntilAsync(() => builder.SelectedSlot.Slot == PcSlot.Peripheral && !builder.IsLoadingCandidates);
        Assert.Equal("TZCON", builder.ExtraCategory?.Value);
        await Wpf.UntilAsync(() => builder.Check is { Items.Count: 7 });
        Assert.Equal(0, builder.ErrorCount);
        Assert.True(builder.IsCompatible);
        Assert.Equal(65 + 115 + 75, builder.Check!.EstimatedDrawW);
        Assert.Equal(750, builder.Check.PsuW);
        var total = builder.Total;
        Assert.True(total > 0);

        builder.Name = "PC gamer de prueba";
        await builder.Quote.ExecuteAsync();
        Assert.NotNull(builder.Current);
        Assert.Equal(PcBuildStatus.Quoted, builder.Current!.Status);
        Assert.False(builder.CanEdit);
        Assert.Equal(total, builder.Current.Total);

        // Proforma: ticket de texto (con la garantía de cada pieza) y PDF
        var detail = await shell.App.SendAsync(new GetPcBuildQuery(builder.Current.Number));
        var proforma = await PcBuilderViewModel.BuildProformaAsync(shell.App, detail);
        var text = PcBuildProforma.Text(proforma);
        Assert.Contains(builder.Current.Number, text, StringComparison.Ordinal);
        Assert.Contains("Garantía 36 meses", text, StringComparison.Ordinal);
        Assert.Contains(PcBuildProforma.NotInvoice, text.Replace("\r\n", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal),
            StringComparison.Ordinal);
        var pdf = PcBuildProforma.Pdf(proforma);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.True(PcBuildProforma.Roll(proforma, 48).Length > 200);

        // «Vender en caja»: la caja carga la cotización y pide las series de las piezas serializadas
        builder.SellInPos.Execute(null);
        await Wpf.UntilAsync(() => shell.Dialogs.Form is SerialsDialog);
        var serials = (SerialsDialog)shell.Dialogs.Form!;
        Assert.Equal(4, serials.Lines.Count);   // procesador, placa, SSD y GPU llevan serie
        foreach (var serialLine in serials.Lines)
        {
            serialLine.Options.Cast<SerialPickOption>().First().IsChecked = true;
        }
        await serials.Confirm.ExecuteAsync();
        Assert.Same(pos, shell.Current);
        Assert.True(pos.IsBuildMode);
        Assert.Equal(7, pos.Cart.Count);
        Assert.Equal(total, pos.Total);
        Assert.All(pos.Cart.Where(l => l.IsSerialized), l => Assert.False(l.MissingSerials));
        pos.Buyer!.Prefill(SiatCodes.DocumentNit, "1020703023", null, "Estudio Pixel Andino S.R.L.", null);
        await pos.Checkout.ExecuteAsync();
        Assert.NotNull(pos.FiscalResult);
        Assert.Equal(FiscalDocumentStatus.Valid, pos.FiscalResult!.Row.Status);
        Assert.Equal(total, pos.FiscalResult.Row.Total);
        Assert.False(pos.IsBuildMode);
        var sold = (await shell.App.SendAsync(new GetPcBuildsQuery(PcBuildStatus.Sold))).Single(b => b.Number == builder.Current.Number);   // la demostración ya trae 2 vendidos
        Assert.Equal(pos.FiscalResult.Sale.InvoiceNumber, sold.InvoiceNumber);
    });

    [Fact]
    public void V42_rma_desde_una_serie_vendida_avanza_y_se_repone_con_otra_unidad() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        await TechSeed.CreateAsync(shell.App);
        shell.Navigate("pos");
        var pos = (PosViewModel)shell.Current;
        await pos.LoadAsync(force: true);
        if (pos.IsClosed)
        {
            await pos.OpenSession.ExecuteAsync();
        }
        var sold = TechSeed.Console.Serials[0];
        await shell.App.SendAsync(new CheckoutCommand("CF", "EFECTIVO", [new SaleLineInput(TechSeed.Console.Sku, 1, 0, [sold])], null, null,
            new FiscalBuyerInput(SiatCodes.DocumentCi, "5115889", null, "Juan Pérez", null, false)));

        // Series e IMEI: la serie vendida, su trazabilidad y «Abrir RMA»
        shell.Navigate("series", sold);
        var serials = (SerialsViewModel)shell.Current;
        await serials.EnsureLoadedAsync();
        await Wpf.UntilAsync(() => serials.Selected?.Serial == sold && serials.Trace is not null && !serials.IsLoadingTrace);
        Assert.Equal(SerialNumberStatus.Sold, serials.Selected!.Status);
        Assert.Contains(serials.Events, e => e.Title == "Vendida");
        Assert.Contains(serials.Events, e => e.Title == "Ingresó al stock");
        Assert.StartsWith("En garantía hasta", serials.WarrantyTitle, StringComparison.Ordinal);
        Assert.True(serials.OpenClaim.CanExecute(null));
        serials.OpenClaim.Execute(null);
        await Wpf.UntilAsync(() => shell.Dialogs.Form is OpenClaimDialog { HasStatus: true });
        var open = (OpenClaimDialog)shell.Dialogs.Form!;
        Assert.False(open.OutOfWarranty);
        open.Issue = "No enciende después de actualizar";
        await open.Confirm.ExecuteAsync();
        var claims = (WarrantyClaimsViewModel)shell.Current;
        await Wpf.UntilAsync(() => claims.Selected is not null && claims.Detail is not null);
        Assert.Equal(WarrantyClaimStatus.Received, claims.Selected!.Status);

        // Diagnóstico → reemplazo con otra unidad → entrega
        async Task RunAsync(WarrantyClaimStatus next, Func<FormDialog, Task> fill)
        {
            await Wpf.UntilAsync(() => claims.Actions.Any(a => a.Next == next));
            claims.RunAction.Execute(claims.Actions.First(a => a.Next == next));
            await Wpf.UntilAsync(() => shell.Dialogs.Form is not null);
            var form = shell.Dialogs.Form!;
            await fill(form);
            await form.Confirm.ExecuteAsync();
            Assert.Null(form.Error);
            await Wpf.UntilAsync(() => shell.Dialogs.Form is null && claims.Detail?.Claim.Status == next);
        }
        await RunAsync(WarrantyClaimStatus.Diagnosing, _ => Task.CompletedTask);
        var replacement = TechSeed.Console.Serials[1];
        await RunAsync(WarrantyClaimStatus.Replaced, form =>
        {
            var replace = Assert.IsType<ReplaceClaimDialog>(form);
            Assert.True(replace.OnScanned(replacement));
            return Task.CompletedTask;
        });
        Assert.Equal(replacement, claims.Detail!.Claim.ReplacementSerial);
        await RunAsync(WarrantyClaimStatus.Delivered, _ => Task.CompletedTask);
        Assert.False(claims.HasActions);

        var unit = await shell.App.SendAsync(new GetWarrantyStatusQuery(replacement));
        Assert.Equal(SerialNumberStatus.Sold, unit.Status);   // la unidad nueva quedó vendida al cliente del caso
        var original = await shell.App.SendAsync(new GetSerialTraceQuery(sold));
        Assert.Equal(SerialNumberStatus.InRma, original.Serial.Status);   // la defectuosa sigue en garantía (al proveedor o baja)
        Assert.Single(original.Claims);
    });

    [Fact]
    public void V42_recibir_una_compra_de_un_producto_con_IMEI_pide_las_series_con_lista_pegada_y_guia_en_vivo() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        await TechSeed.CreateAsync(shell.App);
        var supplier = (await shell.App.SendAsync(new MINV.Application.Catalog.GetCatalogOptionsQuery())).Suppliers[0].Code;
        var order = await shell.App.SendAsync(new MINV.Application.Purchasing.CreatePurchaseOrderCommand(supplier, null, "Celulares con IMEI",
            [new MINV.Application.Purchasing.PurchaseLineInput(TechSeed.Phone.Sku, 2, 1100m)]));
        await shell.App.SendAsync(new MINV.Application.Purchasing.ApprovePurchaseOrderCommand(order.Id));

        shell.Navigate("compras");
        var purchases = (PurchaseOrdersViewModel)shell.Current;
        await purchases.LoadAsync(force: true);
        purchases.Selected = purchases.Rows.Cast<PurchaseOrderItem>().Single(o => o.Row.Id == order.Id);
        var run = purchases.Receive.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Current is { HasInput: true });
        shell.Dialogs.Current!.InputText = "FAC-IMEI-1";
        shell.Dialogs.Current.Confirm.Execute(null);
        await Wpf.UntilAsync(() => shell.Dialogs.Form is SerialsDialog);
        var capture = (SerialsDialog)shell.Dialogs.Form!;
        var line = capture.Lines.Single();
        Assert.True(line.IsEntry);
        Assert.Equal(SerialKind.Imei, line.Kind);
        Assert.Equal("0 de 2", line.CountText);

        // Lista pegada: un IMEI válido y otro con el dígito de control equivocado (guía en vivo; el dominio vuelve a validar)
        var good = TechSeed.Imei("35209900176151");
        var wrong = "35209900176152" + ((int.Parse(TechSeed.Imei("35209900176152")[^1..], System.Globalization.CultureInfo.InvariantCulture) + 1) % 10);
        line.Paste = good + Environment.NewLine + wrong;
        line.AddPasted.Execute(null);
        Assert.Equal("2 de 2", line.CountText);
        Assert.True(line.HasProblems);
        Assert.False(capture.Confirm.CanExecute(null));
        line.RemoveEntry.Execute(line.Entries.Single(e => e.HasProblem));
        line.Input = good;   // repetida: no se agrega
        line.AddInput.Execute(null);
        Assert.Equal("1 de 2", line.CountText);
        var other = TechSeed.Imei("35209900176153");
        Assert.True(capture.OnScanned(other));   // el escáner escribe en la línea incompleta
        Assert.True(line.IsComplete);
        await capture.Confirm.ExecuteAsync();
        await run;
        Assert.Null(shell.Dialogs.Form);
        Assert.Equal(MINV.Domain.Purchasing.PurchaseOrderStatus.Received, purchases.Selected!.Status);
        foreach (var imei in new[] { good, other })
        {
            Assert.Equal(SerialNumberStatus.InStock, (await shell.App.SendAsync(new GetSerialTraceQuery(imei))).Serial.Status);
        }
    });

    [Fact]
    public void V42_transferencia_de_un_producto_serializado_viaja_con_sus_series_y_el_faltante_dice_cual_no_llego() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        await TechSeed.CreateAsync(shell.App);
        await shell.App.SendAsync(new MINV.Application.Corporate.CreateBranchCommand("SB", "Sucursal B", "ALMSB", "Almacén B"));

        // Solicitar: el producto serializado pide las unidades que viajan (disponibles del almacén de origen o escaneadas)
        shell.Navigate("transferencias");
        var transfers = (TransfersViewModel)shell.Current;
        await transfers.LoadAsync(force: true);
        await transfers.New.ExecuteAsync();
        var editor = transfers.Editor!;
        editor.To = editor.Destinations.First(d => d.Code == "ALMSB");
        editor.Product = editor.Products.First(p => p.Sku == TechSeed.Gpu.Sku);
        editor.AddLine.Execute(null);
        editor.Lines[0].Quantity = "2";
        var saving = editor.Save.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Form is SerialsDialog);
        var pick = (SerialsDialog)shell.Dialogs.Form!;
        Assert.True(pick.Lines[0].IsPick);
        Assert.Equal(TechSeed.Gpu.Serials.Order(), pick.Lines[0].Options.Cast<SerialPickOption>().Select(o => o.Serial).Order());
        Assert.False(pick.Confirm.CanExecute(null));
        foreach (var serial in TechSeed.Gpu.Serials)
        {
            shell.OnScanned(serial);   // el escáner marca cada unidad en el formulario
        }
        Assert.Equal("2 de 2", pick.Lines[0].CountText);
        await pick.Confirm.ExecuteAsync();
        await saving;
        Assert.Null(shell.Dialogs.Form);
        Assert.Null(transfers.Editor);
        var created = transfers.Selected!;
        await Wpf.UntilAsync(() => transfers.Detail is not null);
        Assert.Equal(TechSeed.Gpu.Serials.Order(), transfers.Detail!.Lines.Single().Serials!.Order());

        // Despachar y recibir con faltante: se indica QUÉ serie no llegó; la otra entra al stock del destino
        await shell.App.SendAsync(new MINV.Application.Inventory.Transfers.DispatchTransferCommand(created.Row.Id));
        await transfers.LoadAsync(force: true);
        await Wpf.UntilAsync(() => transfers.Detail?.Header.Status == MINV.Domain.Inventory.TransferStatus.Dispatched && transfers.Receive.CanExecute(null));
        transfers.Receive.Execute(null);
        var receipt = transfers.Receipt!;
        Assert.True(receipt.Lines[0].HasSerials);
        receipt.Lines[0].Received = "1";
        receipt.Lines[0].Reason = "Una caja no llegó";
        var receiving = receipt.Save.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Form is SerialsDialog);
        var lost = (SerialsDialog)shell.Dialogs.Form!;
        Assert.Equal(1, lost.Lines[0].Expected);
        Assert.True(lost.Lines[0].Accept(TechSeed.Gpu.Serials[1]));
        await lost.Confirm.ExecuteAsync();
        await receiving;
        Assert.Null(shell.Dialogs.Form);
        Assert.Null(transfers.Receipt);
        var arrived = await shell.App.SendAsync(new GetSerialTraceQuery(TechSeed.Gpu.Serials[0]));
        Assert.Equal(SerialNumberStatus.InStock, arrived.Serial.Status);
        Assert.Equal("SB", arrived.Serial.Branch);   // en la sucursal de destino
        var missing = await shell.App.SendAsync(new GetSerialTraceQuery(TechSeed.Gpu.Serials[1]));
        Assert.NotEqual(SerialNumberStatus.InStock, missing.Serial.Status);
    });

    [Fact]
    public void V42_catalogo_tecnico_ficha_insignias_facetas_y_series_en_movimientos() => Wpf.Run(async () =>
    {
        using var host = Wpf.NewHost();
        var demo = await host.PrepareDemoAsync();
        using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        await shell.StartAsync();
        await TechSeed.CreateAsync(shell.App);

        shell.Navigate("catalogo");
        var catalog = (CatalogViewModel)shell.Current;
        await catalog.LoadAsync(force: true);
        var phone = catalog.Rows.Cast<CatalogProduct>().Single(p => p.Sku == TechSeed.Phone.Sku);
        Assert.Equal("IMEI", phone.SerialBadge);
        Assert.Equal("Garantía 1 año", phone.WarrantyBadge);
        Assert.Contains(catalog.PlatformChips, c => c.Label == "PS5");
        catalog.SelectPlatform.Execute(catalog.PlatformChips.First(c => c.Label == "PS5"));
        var ps5 = catalog.Rows.Cast<CatalogProduct>().ToList();
        Assert.Contains(ps5, p => p.Sku == TechSeed.Console.Sku);   // la de prueba y las de la demostración: todas de PS5
        Assert.All(ps5, p => Assert.Contains("PS5", p.Platforms));
        Assert.DoesNotContain(ps5, p => p.Sku == TechSeed.Phone.Sku);
        catalog.ClearTechFilters.Execute(null);

        // Categoría con sus subcategorías y facetas por especificación
        catalog.Category = catalog.Categories.First(c => c.Value == TechSeed.Root);
        await Wpf.UntilAsync(() => catalog.HasFacets && catalog.Rows.Cast<CatalogProduct>().Count() == 10);   // la raíz incluye sus subcategorías
        Assert.Equal([TechSpecCodes.Condition], catalog.Facets.Select(f => f.Facet.Code));   // facetas propias y heredadas de la categoría
        catalog.Category = catalog.Categories.First(c => c.Value == "TZCPU");
        await Wpf.UntilAsync(() => catalog.Facets.Any(f => f.Facet.Code == "socket") && catalog.Rows.Cast<CatalogProduct>().Count() == 2);
        var socket = catalog.Facets.First(f => f.Facet.Code == "socket");
        Assert.Contains(socket.Choices, c => c.Label == "AM5 (1)");
        socket.Selected = socket.Choices.First(c => c.Value == "AM5");
        await Wpf.UntilAsync(() => catalog.Rows.Cast<CatalogProduct>().Count() == 1);
        Assert.Equal(TechSeed.Cpu.Sku, catalog.Rows.Cast<CatalogProduct>().Single().Sku);

        // Editor: pestaña Ficha técnica con el control de cada tipo; guardar cambia la garantía
        catalog.ClearTechFilters.Execute(null);
        catalog.Category = catalog.Categories[0];
        catalog.Edit.Execute(catalog.Rows.Cast<CatalogProduct>().First(p => p.Sku == TechSeed.Board.Sku));
        await Wpf.UntilAsync(() => catalog.Editor is { TechSheet: not null, IsLoadingTech: false });
        var editor = catalog.Editor!;
        editor.IsTechTab = true;
        var sheet = editor.TechSheet!;
        Assert.True(sheet.TrackSerials);
        Assert.Contains(sheet.Fields, f => f.Code == "socket" && f.IsOptionSingle && f.Option == "AM5");
        Assert.Contains(sheet.Fields, f => f.Code == "ram_max" && f.IsNumber && f.Text == "128");
        Assert.Contains(sheet.Fields, f => f.Code == TechSpecCodes.Condition && f.Definition.IsInherited);
        sheet.WarrantyMonths = "24";
        await editor.Save.ExecuteAsync();
        Assert.Null(editor.Error);
        Assert.Equal(24, (await shell.App.SendAsync(new GetProductTechQuery(TechSeed.Board.Sku))).WarrantyMonths);

        // Administración de especificaciones: lista las propias y heredadas, y crea una nueva
        var specs = new SpecsAdminDialog(shell.App, (await shell.App.SendAsync(new MINV.Application.Catalog.GetCatalogOptionsQuery())).Categories, "TZGPU");
        await specs.LoadAsync();
        Assert.Contains(specs.Specs, s => s.Code == "largo" && !s.IsInherited);
        Assert.Contains(specs.Specs, s => s.Code == TechSpecCodes.Condition && s.IsInherited);
        specs.NewSpec.Execute(null);
        specs.Code = "vram";
        specs.Name = "Memoria de video";
        specs.Unit = "GB";
        specs.Type = specs.Types.First(t => t.Value == SpecDataType.Number);
        await specs.SaveSpec.ExecuteAsync();
        Assert.Null(specs.FormError);
        Assert.Contains(specs.Specs, s => s.Code == "vram");

        // Registro de movimientos: un ajuste positivo de un producto serializado pide las series nuevas
        shell.Navigate("registro", new MovementPrefill(TechSeed.Ssd.Sku, MovementTypeCodes.AdjustmentIn));
        var movement = (MovementViewModel)shell.Current;
        await movement.EnsureLoadedAsync();
        await Wpf.UntilAsync(() => movement.HasProduct && !movement.IsLoadingProduct && movement.HasSerialHint);
        movement.QuantityText = "1";
        movement.Notes = "Unidad encontrada en el depósito";
        var run = movement.Register.ExecuteAsync();
        await Wpf.UntilAsync(() => shell.Dialogs.Form is SerialsDialog);
        var capture = (SerialsDialog)shell.Dialogs.Form!;
        capture.Lines[0].Input = "ssd1t00001";   // repetida en la empresa: el dominio la rechaza dentro del formulario
        capture.Lines[0].AddInput.Execute(null);
        await capture.Confirm.ExecuteAsync();
        Assert.NotNull(capture.Error);
        Assert.Same(capture, shell.Dialogs.Form);
        capture.Lines[0].ClearAll.Execute(null);
        capture.Lines[0].Input = "SSD1T00099";
        capture.Lines[0].AddInput.Execute(null);
        Assert.Equal("1 de 1", capture.Lines[0].CountText);
        await capture.Confirm.ExecuteAsync();
        await run;
        Assert.Null(shell.Dialogs.Form);
        Assert.Equal(SerialNumberStatus.InStock, (await shell.App.SendAsync(new GetSerialTraceQuery("SSD1T00099"))).Serial.Status);
    });
}
