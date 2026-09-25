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
