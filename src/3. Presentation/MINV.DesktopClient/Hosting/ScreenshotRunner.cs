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
/// <c>M-INV.exe --capturas carpeta</c>: recorre la aplicación con la demostración (datos de la V2.1) y guarda una imagen
/// de cada pantalla, en tema claro y oscuro. Sirve para la documentación y para revisar el diseño sin intervención
/// (las ventanas se dibujan fuera de la pantalla visible y no se toca el perfil del usuario).
/// </summary>
public sealed class ScreenshotRunner(ClientHost host, ClientSettings settings, ThemeService theme)
{
    private string _folder = ".";
    private readonly List<string> _saved = [];

    public async Task<int> RunAsync(string folder)
    {
        _folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(_folder);
        var log = Path.Combine(_folder, "capturas.log");
        try
        {
            theme.Apply(ThemeMode.Light, save: false);
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
                await CaptureBusinessAsync(admin, local.Cashier is { } c ? await host.SignInAsync(local.Tenant, c.Email, c.Password) : null);
            }
            else
            {
                using var admin = await host.SignInDemoAsync(demo, DemoWorkspace.AdminEmail);
                await CaptureBusinessAsync(admin, null);
            }
            File.WriteAllLines(log, _saved.Prepend($"✔ {_saved.Count} capturas · M-INV {App.Version} · {DateTime.Now:dd/MM/yyyy HH:mm}"));
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
        splash.Complete(1, text: "Preferencias cargadas · tema claro");
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
            TenantCode = "DEMO",
            Email = "admin@distribuidorademo.example",
        };
        var window = new LoginWindow(vm);
        await ShowAndCaptureAsync(window, "02-inicio-de-sesion.png", 600);
        // V4 · Modo nube: el escritorio solo conoce la dirección del servidor M-INV (sin credenciales de la base)
        vm.ShowCloud("https://minv.elconstructor.example", new ServerStatus(true, "minv.elconstructor.example", "4.0.0-alpha.1", "Servidor M-INV disponible"));
        vm.TenantCode = "MINV";
        vm.Email = "admin@elconstructor.example";
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

        // Registrar movimiento: formulario lleno, resultado y poka-yoke
        var stock = (await shell.App.Data.ProjectionAsync()).Result.Stock;
        var product = stock.Where(r => r.IsActive && r.Stock > 0).OrderByDescending(r => r.Sales30Days).First();
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
        theme.Apply(ThemeMode.Dark, save: false);
        await GoAsync(shell, "inicio");
        await CaptureAsync(window, "20-oscuro-inicio.png");
        await GoAsync(shell, "stock");
        await CaptureAsync(window, "21-oscuro-stock.png");
        shell.Navigate("registro", new MovementPrefill(product.Sku, MovementTypeCodes.Receipt));
        await WaitAsync(() => movement.HasProduct && !movement.IsLoadingProduct, 10000);
        movement.QuantityText = "6";
        await SettleAsync(500);
        await CaptureAsync(window, "22-oscuro-registro.png");
        await GoAsync(shell, "alertas");
        await CaptureAsync(window, "23-oscuro-alertas.png");
        shell.OpenProduct(product.Sku);
        await WaitAsync(() => shell.ProductDetail is { IsLoading: false }, 10000);
        await SettleAsync(600);
        await CaptureAsync(window, "24-oscuro-ficha.png");
        shell.CloseProduct.Execute(null);
        theme.Apply(ThemeMode.Light, save: false);

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
            shell.SelectedBranch = shell.BranchOptions.FirstOrDefault(b => b.Code == "EA") ?? shell.BranchOptions.Last();
            await WaitAsync(() => !shell.IsChangingBranch, 15000);
            await GoAsync(shell, "inicio");
            await SettleAsync(900);
            await CaptureAsync(window, "68-sucursal-el-alto.png");
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

        theme.Apply(ThemeMode.Dark, save: false);
        await GoAsync(shell, "catalogo");
        await CaptureAsync(window, "60-oscuro-catalogo.png");
        await GoAsync(shell, "reportes");
        await CaptureAsync(window, "61-oscuro-reportes.png");
        await GoAsync(shell, "contabilidad");
        await CaptureAsync(window, "62-oscuro-contabilidad.png");
        theme.Apply(ThemeMode.Light, save: false);
        window.Close();

        // Punto de venta: con un cajero que tiene la caja abierta (o el administrador)
        var posShell = cashier is null ? shell : cashier.Services.GetRequiredService<ShellViewModel>();
        var posWindow = new MainWindow(posShell) { Width = 1440, Height = 900 };
        Place(posWindow);
        posWindow.Show();
        await WaitAsync(() => posShell.Current.HasLoaded, 30000);
        await GoAsync(posShell, "pos");
        await WaitImagesAsync(posShell);
        var pos = (PosViewModel)posShell.Current;
        foreach (var item in pos.Products.Cast<PosProduct>().Where(p => !p.IsOut && p.Image is not null).Take(3).ToList())
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
        theme.Apply(ThemeMode.Dark, save: false);
        await SettleAsync(500);
        await CaptureAsync(posWindow, "63-oscuro-punto-de-venta.png");
        theme.Apply(ThemeMode.Light, save: false);
        pos.Cart.Clear();
        posWindow.Close();
        cashier?.Dispose();
    }

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
        (string, string)? Find(string role) => lines.Select(l => System.Text.RegularExpressions.Regex.Split(l.Trim(), @"\s{2,}"))
            .Where(c => c.Length >= 4 && c[0] == role).Select(c => ((string, string)?)(c[2], c[3])).FirstOrDefault();
        return tenant is not null && Find("Administrador") is { } admin ? (tenant, admin, Find("Cajero")) : null;
    }

    // -------------------------------------------------------------------------------------------- utilidades
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
