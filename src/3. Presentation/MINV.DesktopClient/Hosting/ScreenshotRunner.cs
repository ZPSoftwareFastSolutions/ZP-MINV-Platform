using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MINV.DesktopClient.Services;
using MINV.DesktopClient.ViewModels;
using MINV.DesktopClient.Views;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Infrastructure.Demo;
using MINV.Infrastructure.Persistence;

namespace MINV.DesktopClient.Hosting;

/// <summary>
/// <c>M-INV.exe --capturas carpeta [--tema claro|oscuro]</c>: recorre la aplicación con la demostración y guarda una
/// imagen de cada pantalla, en el tema base (claro si no se indica) y un grupo de pantallas en el otro tema (sus archivos
/// llevan el nombre de ese tema: «20-oscuro-inicio.png» con base clara, «20-claro-inicio.png» con base oscura). Sirve para
/// la documentación y para revisar el diseño sin intervención (las ventanas se dibujan fuera de la pantalla visible y no
/// se toca el perfil del usuario). V4.2: la demostración es Tech Zone Gaming (sucursales CM, CB y SC; CAJA01 y CAJA02
/// tienen el turno abierto de su cajero): el administrador abre la caja libre que la pantalla preselecciona y lo que se
/// registra o se cobra es de productos sin serie o lleva sus series. Con la base local la caja es la del cajero de prueba
/// (su turno del día está abierto) y no se abre, registra ni cobra nada.
/// </summary>
public sealed class ScreenshotRunner(ClientHost host, ClientSettings settings, ThemeService theme)
{
    private string _folder = ".";
    private readonly List<string> _saved = [];
    private ThemeMode _base = ThemeMode.Light;
    private ThemeMode _other = ThemeMode.Dark;

    public async Task<int> RunAsync(string folder, ThemeMode baseTheme = ThemeMode.Light)
    {
        _folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(_folder);
        _base = baseTheme == ThemeMode.Dark ? ThemeMode.Dark : ThemeMode.Light;
        _other = _base == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
        var log = Path.Combine(_folder, "capturas.log");
        try
        {
            theme.Apply(_base, save: false);
            await CaptureStartupAsync();
            var demo = await host.PrepareDemoAsync();
            await CaptureLoginAsync(demo);
            await CaptureMainAsync(demo, DemoWorkspace.AdminEmail, admin: true);
            var seller = demo.Users.FirstOrDefault(u => u.RoleCode == RoleCodes.Sales);
            if (seller is not null)
            {
                await CaptureMainAsync(demo, seller.Email, admin: false);
            }
            // Pantallas de negocio (catálogo con imágenes, punto de venta, ventas, compras, reportes, contabilidad, usuarios):
            // con la base LOCAL de datos de prueba si está disponible (MINV_CAPTURAS_USUARIOS = archivo de usuarios de prueba);
            // si no, con la demostración.
            if (LocalUsers() is { } local && (await host.ProbeAsync()).IsReady)
            {
                using var admin = await host.SignInAsync(local.Tenant, local.Admin.Email, local.Admin.Password);
                using var cashier = local.Cashier is { } c ? await host.SignInAsync(local.Tenant, c.Email, c.Password) : null;
                await CaptureBusinessAsync(admin, cashier);
                await CaptureBillingAsync(admin, cashier);
                await CaptureTechAsync(admin, cashier);
                await CaptureV7Async(admin);
            }
            else
            {
                using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
                await CaptureBusinessAsync(admin, null);
                await CaptureBillingAsync(admin, null);
                await CaptureTechAsync(admin, null);
                await CaptureV7Async(admin);
            }
            File.WriteAllLines(log, _saved.Prepend(
                $"✔ {_saved.Count} capturas · M-INV {App.Version} · tema base {ThemeService.Name(_base)} · {DateTime.Now:dd/MM/yyyy HH:mm}"));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(log, "✖ " + ex);
            return 1;
        }
    }

    // -------------------------------------------------------------------------------------------- arranque
    private async Task CaptureStartupAsync()
    {
        var splash = new SplashViewModel();
        splash.Complete(0);
        splash.Complete(1, text: $"Preferencias cargadas · tema {ThemeService.Name(_base)}");
        splash.Complete(2, warning: true, text: "Sin base de datos: puede usar la demostración");
        splash.Begin(3, "Listo");
        splash.Progress = 90;
        var window = new SplashWindow(splash);
        await ShowAndCaptureAsync(window, "01-pantalla-de-carga.png", 900);
        window.Close();
    }

    private async Task CaptureLoginAsync(DemoSession demo)
    {
        var vm = new LoginViewModel(host, settings, new DatabaseStatus(false, false, "localhost:5432", "minv", "minv_app", null,
            "Sin conexión con localhost:5432: no respondió a tiempo"))
        {
            TenantCode = "TECHZONE",
            Email = "admin@techzone.example",
        };
        var window = new LoginWindow(vm);
        await ShowAndCaptureAsync(window, "02-inicio-de-sesion.png", 600);
        // V4 · Modo nube: el escritorio solo conoce la dirección del servidor M-INV (sin credenciales de la base)
        vm.ShowCloud("https://minv.techzone.example", new ServerStatus(true, "minv.techzone.example", App.Version, "Servidor M-INV disponible"));
        vm.TenantCode = "TECHZONE";
        vm.Email = "admin@techzone.example";
        await SettleAsync(500);
        await CaptureAsync(window, "04-inicio-de-sesion-nube.png");
        await vm.OpenDemo.ExecuteAsync();
        await CaptureAsync(window, "03-demostracion-elegir-rol.png");
        window.Close();
        _ = demo;
    }

    // -------------------------------------------------------------------------------------------- ventana principal
    private async Task CaptureMainAsync(DemoSession demo, string email, bool admin)
    {
        using var session = await host.SignInDemoAsync(demo, email);
        var shell = session.Services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow(shell) { Width = 1440, Height = 900 };
        Place(window);
        window.Show();
        await WaitAsync(() => shell.Current.HasLoaded, 20000);
        await SettleAsync(700);
        if (!admin)
        {
            await CaptureAsync(window, "30-rol-ventas-inicio.png");
            await GoAsync(shell, "registro");
            await CaptureAsync(window, "31-rol-ventas-registro.png");
            window.Close();
            return;
        }

        await CaptureAsync(window, "04-inicio.png");
        await GoAsync(shell, "stock");
        await CaptureAsync(window, "05-stock.png");

        // Registrar movimiento: formulario lleno, resultado y poka-yoke, con el producto SIN serie más vendido (una entrada de un
        // serializado pide sus series: esa captura es la de la edición Tecnología)
        var stock = (await shell.App.Data.ProjectionAsync()).Result.Stock;
        var serialized = await SerializedAsync(shell.App);
        var product = stock.Where(r => r.IsActive && r.Stock > 0 && !serialized.Contains(r.Sku)).OrderByDescending(r => r.Sales30Days).First();
        shell.Navigate("registro", new MovementPrefill(product.Sku, MovementTypeCodes.Receipt));
        var movement = (MovementViewModel)shell.Current;
        await movement.EnsureLoadedAsync();
        await WaitAsync(() => movement.HasProduct && !movement.IsLoadingProduct, 10000);
        movement.QuantityText = "24";
        movement.Document = "FAC-001582";
        movement.Notes = "Recepción del pedido semanal";
        await SettleAsync(500);
        await CaptureAsync(window, "06-registrar-movimiento.png");
        await movement.Register.ExecuteAsync();
        await WaitAsync(() => !movement.IsLoadingProduct, 10000);
        await SettleAsync(600);
        await CaptureAsync(window, "07-movimiento-registrado.png");
        var issue = movement.Types.First(t => t.Code == MovementTypeCodes.Issue);
        movement.SelectType.Execute(issue);
        movement.QuantityText = ((int)(movement.Product!.OnHand + 40)).ToString(Fmt.Culture);
        await SettleAsync(500);
        await CaptureAsync(window, "08-poka-yoke-salida-bloqueada.png");
        movement.Clear.Execute(null);

        await GoAsync(shell, "conteo");
        await CaptureAsync(window, "09-toma-fisica.png");
        if (shell.Current is PhysicalCountViewModel { HasOpenCount: true } count && count.Post.CanExecute(null))
        {
            count.Post.Execute(null);
            await WaitAsync(() => shell.Dialogs.Current is not null, 5000);
            await SettleAsync(300);
            await CaptureAsync(window, "10-confirmar-ajustes.png");
            shell.Dialogs.Current?.Cancel.Execute(null);
            await SettleAsync(200);
        }

        await GoAsync(shell, "alertas");
        await CaptureAsync(window, "11-alertas.png");
        await GoAsync(shell, "pedido");
        await CaptureAsync(window, "12-pedido-sugerido.png");
        await GoAsync(shell, "actividad");
        await CaptureAsync(window, "13-actividad.png");
        await GoAsync(shell, "configuracion");
        await CaptureAsync(window, "14-configuracion.png");
        await GoAsync(shell, "ayuda");
        await CaptureAsync(window, "15-ayuda.png");

        await GoAsync(shell, "stock");
        shell.OpenProduct(product.Sku);
        await WaitAsync(() => shell.ProductDetail is { IsLoading: false }, 10000);
        await SettleAsync(600);
        await CaptureAsync(window, "16-ficha-del-producto.png");
        shell.CloseProduct.Execute(null);

        // Tema oscuro
        theme.Apply(_other, save: false);
        await GoAsync(shell, "inicio");
        await CaptureAsync(window, Other("20-oscuro-inicio.png"));
        await GoAsync(shell, "stock");
        await CaptureAsync(window, Other("21-oscuro-stock.png"));
        shell.Navigate("registro", new MovementPrefill(product.Sku, MovementTypeCodes.Receipt));
        await WaitAsync(() => movement.HasProduct && !movement.IsLoadingProduct, 10000);
        movement.QuantityText = "6";
        await SettleAsync(500);
        await CaptureAsync(window, Other("22-oscuro-registro.png"));
        await GoAsync(shell, "alertas");
        await CaptureAsync(window, Other("23-oscuro-alertas.png"));
        shell.OpenProduct(product.Sku);
        await WaitAsync(() => shell.ProductDetail is { IsLoading: false }, 10000);
        await SettleAsync(600);
        await CaptureAsync(window, Other("24-oscuro-ficha.png"));
        shell.CloseProduct.Execute(null);
        theme.Apply(_base, save: false);

        var dialog = new ChangePasswordWindow(new ChangePasswordViewModel(shell.App, mandatory: false));
        await ShowAndCaptureAsync(dialog, "25-cambiar-contrasena.png", 400);
        dialog.Close();
        window.Close();
    }

    // -------------------------------------------------------------------------------------------- pantallas de negocio
    private async Task CaptureBusinessAsync(SessionHandle admin, SessionHandle? cashier)
    {
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow(shell) { Width = 1440, Height = 900 };
        Place(window);
        window.Show();
        await WaitAsync(() => shell.Current.HasLoaded, 30000);
        await SettleAsync(700);
        await CaptureAsync(window, "40-inicio-administrador.png");

        await GoAsync(shell, "catalogo");
        await WaitImagesAsync(shell);
        await CaptureAsync(window, "41-catalogo-galeria.png");
        var catalog = (CatalogViewModel)shell.Current;
        if (catalog.Rows.Cast<CatalogProduct>().FirstOrDefault(p => p.HasImage) is { } product)
        {
            catalog.Edit.Execute(product);
            await WaitAsync(() => catalog.IsEditing, 10000);
            await SettleAsync(600);
            await CaptureAsync(window, "42-catalogo-editor.png");
            catalog.CloseEditor();
        }
        catalog.IsList = true;
        await SettleAsync(500);
        await CaptureAsync(window, "43-catalogo-lista.png");
        catalog.IsGallery = true;

        await GoAsync(shell, "ventas");
        var sales = (SalesViewModel)shell.Current;
        sales.Selected = sales.Rows.Cast<SaleItem>().FirstOrDefault();
        await SettleAsync(800);
        await CaptureAsync(window, "44-ventas.png");
        await GoAsync(shell, "clientes");
        await CaptureAsync(window, "45-clientes.png");
        await GoAsync(shell, "compras");
        var purchases = (PurchaseOrdersViewModel)shell.Current;
        purchases.Selected = purchases.Rows.Cast<PurchaseOrderItem>().FirstOrDefault(o => o.Status == MINV.Domain.Purchasing.PurchaseOrderStatus.Approved)
                             ?? purchases.Rows.Cast<PurchaseOrderItem>().FirstOrDefault();
        await SettleAsync(800);
        await CaptureAsync(window, "46-ordenes-de-compra.png");
        await GoAsync(shell, "proveedores");
        await CaptureAsync(window, "47-proveedores.png");

        await GoAsync(shell, "reportes");
        await SettleAsync(500);
        await CaptureAsync(window, "48-reportes-ventas.png");
        var reports = (ReportsViewModel)shell.Current;
        reports.Tab = "inventory";
        await SettleAsync(300);
        await WaitAsync(() => !reports.IsBusy, 15000);
        await SettleAsync(600);
        await CaptureAsync(window, "49-reportes-inventario.png");
        reports.Tab = "sales";
        await WaitAsync(() => !reports.IsBusy, 15000);

        await GoAsync(shell, "contabilidad");
        await CaptureAsync(window, "50-contabilidad-resultados.png");
        var accounting = (AccountingViewModel)shell.Current;
        accounting.Tab = "journal";
        await SettleAsync(700);
        await CaptureAsync(window, "51-libro-diario.png");
        accounting.Tab = "entry";
        accounting.Template = accounting.Templates[1];
        await SettleAsync(500);
        await CaptureAsync(window, "52-nuevo-asiento.png");
        accounting.Tab = "results";

        await GoAsync(shell, "usuarios");
        await CaptureAsync(window, "53-usuarios.png");
        var users = (UsersViewModel)shell.Current;
        users.Tab = "roles";
        await SettleAsync(500);
        await CaptureAsync(window, "54-roles-y-funciones.png");
        users.Tab = "users";

        // V4 · Sucursales, transferencias e integraciones (con la base de prueba multi-sucursal)
        await GoAsync(shell, "sucursales");
        await WaitAsync(() => !shell.Current.IsBusy, 20000);
        await SettleAsync(700);
        await CaptureAsync(window, "63-sucursales.png");
        await GoAsync(shell, "transferencias");
        var transfers = (TransfersViewModel)shell.Current;
        transfers.Selected = transfers.Rows.Cast<TransferItem>().FirstOrDefault(t => t.Status == MINV.Domain.Inventory.TransferStatus.Dispatched)
                             ?? transfers.Rows.Cast<TransferItem>().FirstOrDefault();
        await WaitAsync(() => transfers.Detail is not null || transfers.Selected is null, 10000);
        await SettleAsync(800);
        await CaptureAsync(window, "64-transferencias.png");
        if (transfers.Receive.CanExecute(null))
        {
            transfers.Receive.Execute(null);
            await SettleAsync(600);
            await CaptureAsync(window, "65-recibir-transferencia.png");
            transfers.CloseEditors();
        }
        await GoAsync(shell, "integraciones");
        await WaitAsync(() => !shell.Current.IsBusy, 15000);
        await SettleAsync(600);
        await CaptureAsync(window, "66-integraciones-api-keys.png");
        var integrations = (IntegrationsViewModel)shell.Current;
        integrations.Tab = 1;
        await SettleAsync(500);
        await CaptureAsync(window, "67-integraciones-webhooks.png");
        integrations.Tab = 0;
        if (shell.ShowBranchSelector)
        {
            shell.SelectedBranch = shell.BranchOptions.FirstOrDefault(b => b.Code == "CB") ?? shell.BranchOptions.Last();
            await WaitAsync(() => !shell.IsChangingBranch, 15000);
            await GoAsync(shell, "inicio");
            await SettleAsync(900);
            await CaptureAsync(window, "68-sucursal-cochabamba.png");
            shell.SelectedBranch = shell.BranchOptions.First(b => b.Code == "CM");
            await WaitAsync(() => !shell.IsChangingBranch, 15000);
        }

        await GoAsync(shell, "stock");
        await WaitImagesAsync(shell);
        await CaptureAsync(window, "57-stock-galeria.png");
        var top = (await shell.App.Data.ProjectionAsync()).Result.Stock.Where(r => r.IsActive && r.Stock > 0).OrderByDescending(r => r.Sales30Days).First();
        shell.OpenProduct(top.Sku);
        await WaitAsync(() => shell.ProductDetail is { IsLoading: false }, 10000);
        await SettleAsync(900);
        await CaptureAsync(window, "55-ficha-con-imagen.png");
        shell.CloseProduct.Execute(null);

        theme.Apply(_other, save: false);
        await GoAsync(shell, "catalogo");
        await CaptureAsync(window, Other("60-oscuro-catalogo.png"));
        await GoAsync(shell, "reportes");
        await CaptureAsync(window, Other("61-oscuro-reportes.png"));
        await GoAsync(shell, "contabilidad");
        await CaptureAsync(window, Other("62-oscuro-contabilidad.png"));
        theme.Apply(_base, save: false);
        window.Close();

        // Punto de venta: con el cajero de prueba que tiene la caja abierta (base local) o el administrador en la caja libre
        var (_, posWindow, pos) = await OpenPosAsync(cashier ?? admin);
        foreach (var item in pos.Products.Cast<PosProduct>().Where(p => !p.IsOut && !p.IsSerialized && p.Image is not null).Take(3).ToList())
        {
            pos.Add.Execute(item);
        }
        if (pos.Cart.Count > 1)
        {
            pos.Cart[0].Quantity = 2;
            pos.Cart[1].Discount = 10;
        }
        pos.CashReceived = "500";
        await SettleAsync(700);
        await CaptureAsync(posWindow, "56-punto-de-venta.png");
        theme.Apply(_other, save: false);
        await SettleAsync(500);
        await CaptureAsync(posWindow, Other("63-oscuro-punto-de-venta.png"));
        theme.Apply(_base, save: false);
        pos.Cart.Clear();
        posWindow.Close();
    }

    // -------------------------------------------------------------------------------------------- V4.1 · facturación SIAT
    /// <summary>
    /// Pantallas de la facturación (69 en adelante). La demostración ya factura con el simulador del SIN en memoria: se
    /// cobran dos ventas (la primera con un producto serializado y su serie, que la factura lleva); con la base local se
    /// captura lo que haya (sin cobrar nada): las pantallas se ven bien aunque todavía no existan documentos.
    /// </summary>
    private async Task CaptureBillingAsync(SessionHandle admin, SessionHandle? cashier)
    {
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        if (!shell.Session.HasBillingModule)
        {
            _saved.Add("(sin capturas de facturación: la empresa no tiene el módulo FISCAL_SIAT)");
            return;
        }

        // Punto de venta con datos de facturación y resultado fiscal (con la serie de la unidad vendida)
        var (posShell, posWindow, pos) = await OpenPosAsync(cashier ?? admin);
        Guid? withSerials = null;   // la factura con series se muestra después en «Documentos fiscales»
        pos.Cart.Clear();
        if (pos.Buyer is { } buyer)
        {
            buyer.Prefill(MINV.Domain.Billing.SiatCodes.DocumentNit, "1020703023", null, "Estudio Pixel Andino S.R.L.", "compras@pixelandino.example");
        }
        if (pos.IsOpen && pos.Products.Cast<PosProduct>().FirstOrDefault(p => p.IsSerialized && !p.IsOut && p.Image is not null) is { } unit)
        {
            await AddWithSerialsAsync(posShell, pos, unit);
        }
        foreach (var item in pos.Products.Cast<PosProduct>().Where(p => !p.IsOut && !p.IsSerialized && p.Available >= 2 && p.Image is not null).Take(1).ToList())
        {
            pos.Add.Execute(item);
        }
        pos.QuickCash.Execute("exacto");
        posShell.App.Notify.Items.Clear();
        await SettleAsync(700);
        await CaptureAsync(posWindow, "75-punto-de-venta-datos-de-facturacion.png");
        if (posShell.Session.IsDemo && pos.IsBilling && pos.IsOpen && pos.Cart.Count > 0)
        {
            await pos.Checkout.ExecuteAsync();
            await WaitAsync(() => !pos.IsBusy, 20000);
            posShell.App.Notify.Items.Clear();
            await SettleAsync(900);
            await CaptureAsync(posWindow, "76-punto-de-venta-resultado-fiscal.png");
            withSerials = pos.FiscalResult?.Row.Id;
            pos.CloseFiscalResult.Execute(null);
            // Una segunda venta (ventas menores del día) para que la lista tenga más de un documento
            pos.Buyer?.UseSpecial.Execute(BuyerForm.SpecialNits[0]);
            if (pos.Products.Cast<PosProduct>().FirstOrDefault(p => !p.IsOut && !p.IsSerialized) is { } other)
            {
                pos.Add.Execute(other);
                await pos.Checkout.ExecuteAsync();
                await WaitAsync(() => !pos.IsBusy, 20000);
                pos.CloseFiscalResult.Execute(null);
            }
        }
        pos.Cart.Clear();
        pos.Buyer?.Clear();
        posShell.App.Notify.Items.Clear();   // los avisos de las ventas no tapan las capturas siguientes
        posWindow.Close();

        var window = new MainWindow(shell) { Width = 1440, Height = 900 };
        Place(window);
        window.Show();
        await WaitAsync(() => shell.Current.HasLoaded, 30000);
        shell.App.Notify.Items.Clear();

        await GoAsync(shell, "estado-siat");
        await WaitAsync(() => !shell.Current.IsBusy, 20000);
        await SettleAsync(800);
        await CaptureAsync(window, "69-estado-siat.png");

        await GoAsync(shell, "documentos-fiscales");
        var documents = (FiscalDocumentsViewModel)shell.Current;
        documents.Selected = documents.Rows.Cast<FiscalDocumentItem>().FirstOrDefault(d => d.Id == withSerials)
                             ?? documents.Rows.Cast<FiscalDocumentItem>().FirstOrDefault(d => d.Status == MINV.Domain.Billing.FiscalDocumentStatus.Valid)
                             ?? documents.Rows.Cast<FiscalDocumentItem>().FirstOrDefault();
        await WaitAsync(() => documents.Selected is null || documents.Detail is not null, 15000);
        await SettleAsync(900);
        await CaptureAsync(window, "70-documentos-fiscales-detalle.png");
        if (documents.Void.CanExecute(null))
        {
            var voiding = documents.Void.ExecuteAsync();
            await WaitAsync(() => shell.Dialogs.Form is not null || voiding.IsCompleted, 15000);
            if (shell.Dialogs.Form is VoidFiscalDialog dialog)
            {
                dialog.Note = "El cliente pidió la factura a nombre de su empresa";
                await SettleAsync(600);
                await CaptureAsync(window, "71-anular-documento-fiscal.png");
                dialog.Cancel.Execute(null);
            }
            await voiding;
        }

        // Ventas: la factura del SIN de cada venta (la venta facturada, seleccionada, con «Devolución» y «Anular»)
        await GoAsync(shell, "ventas");
        var sales = (SalesViewModel)shell.Current;
        await sales.LoadAsync(force: true);
        sales.Selected = sales.Rows.Cast<SaleItem>().FirstOrDefault(s => s.HasFiscal);
        await SettleAsync(700);
        await CaptureAsync(window, "80-ventas-con-factura-del-sin.png");
        sales.Selected = null;

        await GoAsync(shell, "homologacion");
        await SettleAsync(600);
        await CaptureAsync(window, "72-homologacion.png");

        await GoAsync(shell, "libros-fiscales");
        await WaitAsync(() => !shell.Current.IsBusy, 20000);
        await SettleAsync(600);
        await CaptureAsync(window, "73-libros-fiscales-ventas.png");
        var books = (FiscalBooksViewModel)shell.Current;
        books.Tab = 2;
        await SettleAsync(500);
        await CaptureAsync(window, "77-libros-fiscales-resumen-iva-it.png");
        books.Tab = 0;

        await GoAsync(shell, "facturacion-siat");
        await SettleAsync(700);
        await CaptureAsync(window, "74-configuracion-facturacion-siat.png");

        theme.Apply(_other, save: false);
        await GoAsync(shell, "documentos-fiscales");
        await SettleAsync(800);
        await CaptureAsync(window, Other("78-oscuro-documentos-fiscales.png"));
        await GoAsync(shell, "estado-siat");
        await SettleAsync(600);
        await CaptureAsync(window, Other("79-oscuro-estado-siat.png"));
        theme.Apply(_base, save: false);
        window.Close();
    }

    // -------------------------------------------------------------------------------------------- V4.2 · edición Tecnología
    /// <summary>
    /// V4.2 · Armador de PC, series e IMEI, garantías y RMA, catálogo técnico, caja con series y sección Tecnología del
    /// tablero (81 en adelante). Tolerante a datos vacíos: con una empresa sin productos serializados, cotizaciones o casos
    /// RMA se capturan las pantallas con sus estados vacíos (no se inventan datos); con la empresa de prueba de la edición
    /// (o una base local con ella) cada pantalla se muestra con un ejemplo real elegido de sus datos. V6 (99 a 102): las
    /// reservas de la tienda web en el armador (lista filtrada y detalle) y el stock con unidades reservadas.
    /// </summary>
    private async Task CaptureTechAsync(SessionHandle admin, SessionHandle? cashier)
    {
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        if (!shell.AllPages.Any(p => p.Key == "armador"))
        {
            _saved.Add("(sin capturas de la edición Tecnología: el rol no tiene acceso)");
            return;
        }
        var window = new MainWindow(shell) { Width = 1440, Height = 900 };
        Place(window);
        window.Show();
        await TryWaitAsync(() => shell.Current.HasLoaded, 30000);
        shell.App.Notify.Items.Clear();

        // Tablero: la sección Tecnología (V7: plegable; se abre y se desplaza hasta ella)
        await GoAsync(shell, "inicio");
        if (shell.Current is DashboardViewModel home)
        {
            await home.Tech.OpenAsync();
        }
        await SettleAsync(600);
        await ScrollToAsync<Views.Pages.DashboardView>(window, "Ver tablero Tecnología");
        await SettleAsync(300);
        await CaptureAsync(window, "81-inicio-tecnologia.png");

        // Catálogo: insignias Serie/IMEI, garantía y plataformas; ficha técnica y especificaciones por categoría
        await GoAsync(shell, "catalogo");
        await WaitImagesAsync(shell);
        var catalog = (CatalogViewModel)shell.Current;
        var serialized = catalog.Rows.Cast<CatalogProduct>().Where(p => p.HasSerialBadge).ToList();
        if (await ShowcaseCategoryAsync(shell.App, catalog) is { } category)
        {
            catalog.Category = category;
            await TryWaitAsync(() => !catalog.IsBusy, 10000);
            await SettleAsync(900);
        }
        await CaptureAsync(window, "82-catalogo-insignias-y-plataformas.png");
        // La ficha es de un producto de la categoría que se ve en la galería (serializado y, si lo hay, con stock); si no, el
        // primero serializado del catálogo completo
        var shown = catalog.Rows.Cast<CatalogProduct>().Where(p => p.HasSerialBadge).ToList();
        var sample = shown.FirstOrDefault(p => p.Stock > 0) ?? shown.FirstOrDefault() ?? serialized.FirstOrDefault()
                     ?? catalog.Rows.Cast<CatalogProduct>().FirstOrDefault();
        if (sample is not null && catalog.CanEdit)
        {
            catalog.Edit.Execute(sample);
            await TryWaitAsync(() => catalog.Editor is { IsLoadingTech: false }, 10000);
            if (catalog.Editor is { } editor)
            {
                editor.IsTechTab = true;
                await SettleAsync(700);
                await CaptureAsync(window, "83-catalogo-ficha-tecnica.png");
                theme.Apply(_other, save: false);
                await SettleAsync(500);
                await CaptureAsync(window, Other("98-oscuro-catalogo-ficha-tecnica.png"));
                theme.Apply(_base, save: false);
                catalog.CloseEditor();
            }
        }
        if (catalog.ManageSpecs.CanExecute(null))
        {
            var specs = catalog.ManageSpecs.ExecuteAsync();
            await TryWaitAsync(() => shell.Dialogs.Form is SpecsAdminDialog || specs.IsCompleted, 15000);
            if (shell.Dialogs.Form is SpecsAdminDialog dialog)
            {
                await TryWaitAsync(() => !dialog.NoSpecs || dialog.Category is null, 3000);
                dialog.Selected = dialog.Specs.FirstOrDefault(x => !x.IsInherited && x.View.DataType == MINV.Domain.Catalog.SpecDataType.Option)
                                  ?? dialog.Specs.FirstOrDefault();
                await SettleAsync(600);
                await CaptureAsync(window, "84-catalogo-especificaciones.png");
                dialog.Cancel.Execute(null);
            }
            await specs;
        }
        catalog.Category = catalog.Categories[0];

        // Caja (la del cajero de prueba con la base local; la caja libre del administrador en la demostración): elegir la
        // unidad (serie o IMEI) de un producto serializado y el carrito con sus series
        var (posShell, posWindow, pos) = await OpenPosAsync(cashier ?? admin);
        pos.Cart.Clear();
        if (pos.IsOpen && pos.Products.Cast<PosProduct>().FirstOrDefault(p => p.IsSerialized && !p.IsOut && p.Image is not null) is { } unit)
        {
            await AddWithSerialsAsync(posShell, pos, unit, () => CaptureAsync(posWindow, "85-caja-elegir-serie.png"));
            if (pos.Products.Cast<PosProduct>().FirstOrDefault(p => !p.IsSerialized && !p.IsOut && p.Image is not null) is { } other)
            {
                pos.Add.Execute(other);
            }
            pos.IsBuyerExpanded = false;   // datos de facturación plegados: se ve el carrito con las series de cada línea
            posShell.App.Notify.Items.Clear();
            await SettleAsync(700);
            await CaptureAsync(posWindow, "86-caja-con-series.png");
            theme.Apply(_other, save: false);
            await SettleAsync(500);
            await CaptureAsync(posWindow, Other("97-oscuro-caja-con-series.png"));
            theme.Apply(_base, save: false);
            pos.Cart.Clear();
            pos.IsBuyerExpanded = true;
        }
        posWindow.Hide();

        // Armador de PC: una cotización vigente de la sucursal de la caja (o cualquiera) o, si no hay, las primeras piezas
        // compatibles de cada ranura
        await GoAsync(shell, "armador");
        var builder = (PcBuilderViewModel)shell.Current;
        var branch = posShell.Session.Access.Active?.Code;
        var quote = builder.Builds.Cast<PcBuildItem>().FirstOrDefault(b => b.CanSell && b.Row.IsCompatible && b.Row.BranchCode == branch)
                    ?? builder.Builds.Cast<PcBuildItem>().FirstOrDefault(b => b.CanSell && b.Row.BranchCode == branch)
                    ?? builder.Builds.Cast<PcBuildItem>().FirstOrDefault(b => b.CanSell)
                    ?? builder.Builds.Cast<PcBuildItem>().FirstOrDefault();
        if (quote is not null)
        {
            await builder.OpenBuildAsync(quote.Number);
        }
        else
        {
            foreach (var slot in builder.Slots.Where(s => s.IsRequired || s.Slot == MINV.Domain.Catalog.PcSlot.Gpu).ToList())
            {
                builder.SelectSlot.Execute(slot);
                await TryWaitAsync(() => builder.SelectedSlot == slot && !builder.IsLoadingCandidates, 10000);
                await SettleAsync(200);
                if (builder.Candidates.FirstOrDefault(c => c.IsCompatible && !c.IsOut) is { } candidate && builder.CanEdit)
                {
                    builder.AddPart.Execute(candidate);
                    await TryWaitAsync(() => slot.HasParts, 10000);
                }
            }
            builder.SelectSlot.Execute(builder.Slots.FirstOrDefault(s => s.Slot == MINV.Domain.Catalog.PcSlot.Gpu) ?? builder.Slots[0]);
        }
        await TryWaitAsync(() => !builder.IsLoadingCandidates, 10000);
        await SettleAsync(900);
        await CaptureAsync(window, "87-armador-de-pc.png");
        theme.Apply(_other, save: false);
        await SettleAsync(500);
        await CaptureAsync(window, Other("94-oscuro-armador-de-pc.png"));
        theme.Apply(_base, save: false);
        builder.IsQuotesTab = true;
        await SettleAsync(600);
        await CaptureAsync(window, "88-armador-cotizaciones.png");
        builder.IsBuildTab = true;
        if (builder.Proforma.CanExecute(null))
        {
            var proforma = builder.Proforma.ExecuteAsync();
            await TryWaitAsync(() => shell.Dialogs.Form is ProformaDialog || proforma.IsCompleted, 10000);
            await SettleAsync(600);
            await CaptureAsync(window, "89-armador-proforma.png");
            shell.Dialogs.Form?.Cancel.Execute(null);
            await proforma;
        }

        // V6 · Reservas web: la lista con el filtro «Reservas web» (canal, contacto, reservado hasta) y el detalle de una reserva
        // (contacto con «Copiar teléfono», notas del cliente, piezas con su disponibilidad). Con una empresa sin reservas web la lista
        // muestra su estado vacío y no hay detalle.
        builder.IsQuotesTab = true;
        builder.StatusFilter = builder.Statuses[0];
        builder.OnlyWebReservations = true;
        await SettleAsync(600);
        await CaptureAsync(window, "99-armador-reservas-web.png");
        var webReservation = builder.Builds.Cast<PcBuildItem>().FirstOrDefault(b => b.IsWeb && b.IsReservationActive)
                             ?? builder.Builds.Cast<PcBuildItem>().FirstOrDefault(b => b.IsWeb);
        builder.OnlyWebReservations = false;
        if (webReservation is not null)
        {
            await builder.OpenBuildAsync(webReservation.Number);
            await TryWaitAsync(() => !builder.IsLoadingCandidates, 10000);
            shell.App.Notify.Items.Clear();
            await SettleAsync(900);
            await CaptureAsync(window, "100-armador-reserva-web-detalle.png");
            theme.Apply(_other, save: false);
            await SettleAsync(500);
            await CaptureAsync(window, Other("102-oscuro-armador-reserva-web-detalle.png"));
            theme.Apply(_base, save: false);
        }
        builder.IsBuildTab = true;

        // Caja: una cotización vigente de su sucursal cargada con «Desde armado» (precios cotizados y series de cada pieza)
        if (quote is { CanSell: true } && quote.Row.BranchCode == branch && pos.IsOpen)
        {
            posWindow.Show();
            pos.Cart.Clear();
            posShell.Navigate("pos", new PcBuildToSell(quote.Number));
            // La caja carga la cotización y pide las series de sus piezas serializadas (el formulario aparece después)
            await TryWaitAsync(() => posShell.Dialogs.Form is SerialsDialog, 15000);
            if (posShell.Dialogs.Form is SerialsDialog serials)
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
            await TryWaitAsync(() => posShell.Dialogs.Form is null && pos.IsBuildMode, 10000);
            pos.IsBuyerExpanded = false;
            posShell.App.Notify.Items.Clear();
            await SettleAsync(800);
            await CaptureAsync(posWindow, "90-caja-desde-armado.png");
            pos.ClearBuild.Execute(null);
            pos.IsBuyerExpanded = true;
        }
        posWindow.Close();

        // V6 · Stock con unidades reservadas: el chip «Con reservas» (si hay) y la insignia «Reservado: n» de cada producto
        // (disponible = existencias − reservado, regla S-03)
        await GoAsync(shell, "stock");
        var stockPage = (StockViewModel)shell.Current;
        stockPage.IsGallery = true;
        if (stockPage.Filters.FirstOrDefault(f => ReferenceEquals(f.Value, StockViewModel.ReservedFilter)) is { } reservedChip)
        {
            stockPage.SelectFilter.Execute(reservedChip);
        }
        await WaitImagesAsync(shell);
        await CaptureAsync(window, "101-stock-con-reservado.png");
        stockPage.SelectFilter.Execute(stockPage.Filters[0]);

        // Series e IMEI: las unidades vendidas (con su venta y su garantía en la lista) y la primera con su trazabilidad
        await GoAsync(shell, "series");
        var serialsPage = (SerialsViewModel)shell.Current;
        if (serialsPage.Statuses.FirstOrDefault(s => s.Value == MINV.Domain.Inventory.SerialNumberStatus.Sold) is { } soldFilter)
        {
            serialsPage.Status = soldFilter;
            await SettleAsync(300);
            await TryWaitAsync(() => !serialsPage.IsBusy, 15000);
        }
        serialsPage.Selected = serialsPage.Rows.Cast<SerialItem>().FirstOrDefault(i => i.Status == MINV.Domain.Inventory.SerialNumberStatus.Sold)
                               ?? serialsPage.Rows.Cast<SerialItem>().FirstOrDefault();
        await TryWaitAsync(() => !serialsPage.IsLoadingTrace, 10000);
        await SettleAsync(900);
        await CaptureAsync(window, "91-series-e-imei.png");
        theme.Apply(_other, save: false);
        await SettleAsync(500);
        await CaptureAsync(window, Other("95-oscuro-series-e-imei.png"));
        theme.Apply(_base, save: false);

        // Garantías y RMA: un caso abierto (o el primero) con su bitácora y el formulario para abrir uno
        await GoAsync(shell, "garantias");
        var claims = (WarrantyClaimsViewModel)shell.Current;
        claims.Selected = claims.Rows.Cast<WarrantyClaimItem>().FirstOrDefault(c => c.IsOpen) ?? claims.Rows.Cast<WarrantyClaimItem>().FirstOrDefault();
        await SettleAsync(900);
        await CaptureAsync(window, "92-garantias-rma.png");
        theme.Apply(_other, save: false);
        await SettleAsync(500);
        await CaptureAsync(window, Other("96-oscuro-garantias-rma.png"));
        theme.Apply(_base, save: false);
        if (claims.OpenNew.CanExecute(null))
        {
            var opening = claims.OpenNew.ExecuteAsync();
            await TryWaitAsync(() => shell.Dialogs.Form is OpenClaimDialog || opening.IsCompleted, 10000);
            if (shell.Dialogs.Form is OpenClaimDialog open)
            {
                if (serialsPage.Rows.Cast<SerialItem>().FirstOrDefault(i => i.Status == MINV.Domain.Inventory.SerialNumberStatus.Sold) is { } sold)
                {
                    open.Serial = sold.Serial;
                    await open.LookupAsync();
                    open.Issue = "No enciende después de una actualización";
                }
                await SettleAsync(600);
                await CaptureAsync(window, "93-abrir-caso-rma.png");
                open.Cancel.Execute(null);
            }
            await opening;
        }
        shell.App.Notify.Items.Clear();
        window.Close();
    }

    /// <summary>
    /// V7 (103 a 110): el inicio simplificado (botones del rol y una sección plegable abierta), Reservas (lista, detalle y el
    /// formulario de una reserva en mostrador, sin guardarla), la cola de correos y Usuarios con los clientes de la tienda web.
    /// </summary>
    private async Task CaptureV7Async(SessionHandle admin)
    {
        var shell = admin.Services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow(shell) { Width = 1440, Height = 900 };
        Place(window);
        window.Show();
        await WaitAsync(() => shell.Current.HasLoaded, 30000);
        shell.App.Notify.Items.Clear();
        await GoAsync(shell, "inicio");
        await SettleAsync(700);
        await CaptureAsync(window, "103-inicio-simplificado.png");
        if (shell.Current is DashboardViewModel home)
        {
            await home.Inventory.OpenAsync();
            await home.Charts.OpenAsync();
            await SettleAsync(700);
            await ScrollToAsync<Views.Pages.DashboardView>(window, "Ver indicadores del inventario");
            await SettleAsync(400);
            await CaptureAsync(window, "104-inicio-seccion-abierta.png");
            await home.Inventory.CloseAsync();
            await home.Charts.CloseAsync();
        }

        if (shell.AllPages.Any(p => p.Key == "reservas"))
        {
            await GoAsync(shell, "reservas");
            var reservations = (ReservationsViewModel)shell.Current;
            await SettleAsync(700);
            await CaptureAsync(window, "105-reservas.png");
            var rows = reservations.Rows.Cast<ReservationItem>().ToList();
            reservations.Selected = rows.FirstOrDefault(r => r.IsActive && r.IsWeb && r.IsCart && r.HasEmail)
                                    ?? rows.FirstOrDefault(r => r.IsActive) ?? rows.FirstOrDefault();
            if (reservations.Selected is not null)
            {
                await TryWaitAsync(() => !reservations.IsLoadingDetail && reservations.Detail is not null, 10000);
                await SettleAsync(800);
                await CaptureAsync(window, "106-reserva-detalle.png");
                theme.Apply(_other, save: false);
                await SettleAsync(500);
                await CaptureAsync(window, Other("110-oscuro-reserva-detalle.png"));
                theme.Apply(_base, save: false);
                reservations.Selected = null;
            }
            if (reservations.NewReservation.CanExecute(null) && shell.Session.Access.Active is not null)
            {
                var opening = reservations.NewReservation.ExecuteAsync();
                await TryWaitAsync(() => shell.Dialogs.Form is CounterReservationDialog || opening.IsCompleted, 10000);
                if (shell.Dialogs.Form is CounterReservationDialog form)
                {
                    form.ContactName = "Mariana Rojas";
                    form.ContactPhone = "70012345";
                    form.ContactEmail = "mariana@cliente.example";
                    form.Search = "monitor";
                    if (form.Suggestions.FirstOrDefault(s => !s.IsOut) is { } option)
                    {
                        form.AddProduct.Execute(option);
                    }
                    form.Search = "mouse";   // el buscador con sugerencias a la vista
                    await SettleAsync(700);
                    await CaptureAsync(window, "107-nueva-reserva-mostrador.png");
                    form.Cancel.Execute(null);
                }
                await opening;
            }
        }
        if (shell.AllPages.Any(p => p.Key == "correos"))
        {
            await GoAsync(shell, "correos");
            await SettleAsync(700);
            await CaptureAsync(window, "108-correos.png");
        }
        if (shell.AllPages.Any(p => p.Key == "usuarios"))
        {
            await GoAsync(shell, "usuarios");
            var users = (UsersViewModel)shell.Current;
            users.Kind = users.Kinds.FirstOrDefault(k => k.Value == UserKind.Customer) ?? users.Kinds[0];
            await SettleAsync(700);
            await CaptureAsync(window, "109-usuarios-clientes-web.png");
            users.ClearFilters.Execute(null);
        }
        shell.App.Notify.Items.Clear();
        window.Close();
    }

    /// <summary>
    /// Categoría para mostrar las insignias de la galería: la que tiene más productos con serie o IMEI (con sus
    /// subcategorías) sin pasar de 10 productos (una pantalla); si ninguna entra, la de más serializados.
    /// </summary>
    private static async Task<Choice<string?>?> ShowcaseCategoryAsync(AppServices app, CatalogViewModel catalog)
    {
        var candidates = new List<(Choice<string?> Category, int Serialized, int Total)>();
        foreach (var category in catalog.Categories.Where(c => c.Value is not null))
        {
            try
            {
                var rows = await app.SendAsync(new MINV.Application.Tech.SearchTechProductsQuery(CategoryCode: category.Value, Max: 500));
                var count = rows.Count(r => r.TrackSerials);
                if (count > 0)
                {
                    candidates.Add((category, count, rows.Count));
                }
            }
            catch (Exception ex) when (AppServices.IsExpected(ex))
            {
                return null;
            }
        }
        return candidates.Where(c => c.Total <= 10).OrderByDescending(c => c.Serialized).ThenBy(c => c.Total).Select(c => c.Category).FirstOrDefault()
               ?? candidates.OrderByDescending(c => c.Serialized).Select(c => c.Category).FirstOrDefault();
    }

    /// <summary>
    /// Desplaza la página <typeparamref name="TPage"/> hasta su primer texto <paramref name="text"/> (p. ej. una sección del
    /// tablero) y lo deja arriba. Se busca dentro de la página: el menú lateral también tiene una sección «Tecnología».
    /// </summary>
    private static async Task ScrollToAsync<TPage>(Window window, string text) where TPage : FrameworkElement
    {
        FrameworkElement? target = null;
        await TryWaitAsync(() =>
        {
            window.UpdateLayout();
            target = Find(window, e => e is TPage) is { } page ? Find(page, e => e is System.Windows.Controls.TextBlock t && t.Text == text) : null;
            return target is not null;
        }, 10000);
        if (target is null)
        {
            return;
        }
        DependencyObject? parent = target;
        while (parent is not null and not System.Windows.Controls.ScrollViewer)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }
        if (parent is System.Windows.Controls.ScrollViewer viewer)
        {
            var top = target.TransformToAncestor(viewer).Transform(new Point(0, 0)).Y;
            viewer.ScrollToVerticalOffset(Math.Max(0, viewer.VerticalOffset + top - 16));
        }
        else
        {
            target.BringIntoView();
        }
        window.UpdateLayout();
    }

    private static FrameworkElement? Find(DependencyObject root, Func<DependencyObject, bool> predicate)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (predicate(child) && child is FrameworkElement { IsVisible: true } element)
            {
                return element;
            }
            if (Find(child, predicate) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Ventana con la caja de <paramref name="session"/>. En la demostración, si el usuario no tiene turno, abre la caja que la
    /// pantalla preselecciona (la libre: CAJA01 y CAJA02 tienen el turno de su cajero); con la base local no abre nada (se usa
    /// el cajero de prueba, cuyo turno del día está abierto).
    /// </summary>
    private static async Task<(ShellViewModel Shell, MainWindow Window, PosViewModel Pos)> OpenPosAsync(SessionHandle session)
    {
        var shell = session.Services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow(shell) { Width = 1440, Height = 900 };
        Place(window);
        window.Show();
        await WaitAsync(() => shell.Current.HasLoaded, 30000);
        await GoAsync(shell, "pos");
        await WaitImagesAsync(shell);
        var pos = (PosViewModel)shell.Current;
        await pos.LoadAsync(force: true);
        if (pos.IsClosed && shell.Session.IsDemo && pos.OpenSession.CanExecute(null))
        {
            await pos.OpenSession.ExecuteAsync();
            await SettleAsync(400);
        }
        shell.App.Notify.Items.Clear();
        return (shell, window, pos);
    }

    /// <summary>Agrega a la caja un producto serializado eligiendo su primera unidad disponible en el formulario de series
    /// (antes de confirmar puede tomarse la captura del formulario).</summary>
    private static async Task AddWithSerialsAsync(ShellViewModel shell, PosViewModel pos, PosProduct unit, Func<Task>? capture = null)
    {
        pos.Add.Execute(unit);
        await TryWaitAsync(() => shell.Dialogs.Form is SerialsDialog, 10000);
        if (shell.Dialogs.Form is not SerialsDialog pick)
        {
            return;
        }
        if (pick.Lines[0].Options.Cast<SerialPickOption>().FirstOrDefault() is { } first)
        {
            first.IsChecked = true;
        }
        await SettleAsync(600);
        if (capture is not null)
        {
            await capture();
        }
        await pick.Confirm.ExecuteAsync();
        await TryWaitAsync(() => shell.Dialogs.Form is null, 5000);
    }

    /// <summary>SKU de los productos con serie o IMEI (se registran, venden y reciben con sus series, regla T-02).</summary>
    private static async Task<IReadOnlySet<string>> SerializedAsync(AppServices app) =>
        (await app.SendAsync(new MINV.Application.Tech.SearchTechProductsQuery(Max: 1000))).Where(p => p.TrackSerials).Select(p => p.Sku)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static async Task WaitImagesAsync(ShellViewModel shell)
    {
        await shell.App.Images.AllAsync();
        await WaitAsync(() => !shell.Current.IsBusy, 15000);
        await SettleAsync(900);
    }

    /// <summary>Empresa y usuarios de la base local de prueba (archivo que escribe <c>minv datos-prueba</c>).</summary>
    private static (string Tenant, (string Email, string Password) Admin, (string Email, string Password)? Cashier)? LocalUsers()
    {
        var file = Environment.GetEnvironmentVariable("MINV_CAPTURAS_USUARIOS");
        if (file is null || !File.Exists(file))
        {
            return null;
        }
        var lines = File.ReadAllLines(file);
        var tenant = lines.Select(l => System.Text.RegularExpressions.Regex.Match(l, @"código de empresa: (\S+)")).FirstOrDefault(m => m.Success)?.Groups[1].Value;
        // Fila «Rol  Nombre  Correo  Contraseña  Sucursales»: el correo es la primera palabra con @ y la contraseña, la siguiente
        // (un nombre largo puede quedar pegado al correo con un solo espacio)
        (string, string)? Find(string role) => lines
            .Select(l => System.Text.RegularExpressions.Regex.Match(l, "^" + role + @"\s.*?\s(?<correo>[^\s@]+@[^\s@]+)\s+(?<clave>\S+)"))
            .Where(m => m.Success).Select(m => ((string, string)?)(m.Groups["correo"].Value, m.Groups["clave"].Value)).FirstOrDefault();
        return tenant is not null && Find("Administrador") is { } admin ? (tenant, admin, Find("Cajero")) : null;
    }

    // -------------------------------------------------------------------------------------------- utilidades
    /// <summary>Nombre de una captura del tema alterno: lleva el nombre del tema en que se tomó.</summary>
    private string Other(string file) =>
        _other == ThemeMode.Dark ? file : file.Replace("-oscuro-", "-" + ThemeService.Name(_other) + "-", StringComparison.Ordinal);

    private static async Task GoAsync(ShellViewModel shell, string key)
    {
        shell.Navigate(key);
        await shell.Current.EnsureLoadedAsync();
        await WaitAsync(() => !shell.Current.IsBusy, 15000);
        await SettleAsync(500);
    }

    private async Task ShowAndCaptureAsync(Window window, string file, int settle)
    {
        Place(window);
        window.Show();
        await SettleAsync(settle);
        await CaptureAsync(window, file);
    }

    private static void Place(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
    }

    private static async Task SettleAsync(int milliseconds)
    {
        await Task.Delay(milliseconds);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static async Task WaitAsync(Func<bool> condition, int timeoutMs)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("La pantalla no terminó de cargar a tiempo.");
            }
            await Task.Delay(50);
        }
    }

    /// <summary>Como <see cref="WaitAsync"/> pero sin fallar: las capturas de la V4.2 toleran datos vacíos.</summary>
    private static async Task<bool> TryWaitAsync(Func<bool> condition, int timeoutMs)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                return false;
            }
            await Task.Delay(50);
        }
        return true;
    }

    private async Task CaptureAsync(Window window, string file)
    {
        await SettleAsync(150);
        window.UpdateLayout();
        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        var shot = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        shot.Render(window);
        // Las ventanas con transparencia (sombra propia) se componen sobre un fondo neutro.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)), null, new Rect(0, 0, width, height));
            dc.DrawImage(shot, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(_folder, file);
        await using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }
        _saved.Add(file);
    }
}
