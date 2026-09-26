using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using MINV.Application.Tech;
using MINV.DesktopClient.Hosting;
using MINV.DesktopClient.Services;
using MINV.DesktopClient.ViewModels;

namespace MINV.DesktopClient.Tests;

/// <summary>
/// Ejecuta una prueba en un hilo STA con despachador de WPF (como la aplicación real): los temporizadores, las vistas
/// de colección y las continuaciones de <c>await</c> vuelven al mismo hilo.
/// </summary>
internal static class Wpf
{
    public static void Run(Func<Task> test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            _ = RunAsync();
            Dispatcher.PushFrame(frame);
            dispatcher.InvokeShutdown();

            async Task RunAsync()
            {
                try
                {
                    await test();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    frame.Continue = false;
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(3)))
        {
            throw new TimeoutException("La prueba de la interfaz no terminó en 3 minutos.");
        }
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    /// <summary>Espera a que la pantalla termine lo que está haciendo (cargas asíncronas, temporizadores).</summary>
    public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 15000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("La condición no se cumplió a tiempo.");
            }
            await Task.Delay(25);
        }
    }

    /// <summary>Cliente con preferencias en memoria (no toca el perfil del usuario que ejecuta las pruebas).</summary>
    public static ClientHost NewHost()
    {
        var settings = new ClientSettings { IsReadOnly = true };
        return new ClientHost([], settings, new ThemeService(settings));
    }
}

/// <summary>V4.2 · Datos de la demostración de Tech Zone Gaming que usan las pruebas de las pantallas.</summary>
internal static class DemoData
{
    /// <summary>SKU de los productos que llevan serie o IMEI (regla T-02: se venden, reciben y cuentan con sus series).</summary>
    public static async Task<IReadOnlySet<string>> SerializedAsync(AppServices app) =>
        (await app.SendAsync(new SearchTechProductsQuery(Max: 1000))).Where(p => p.TrackSerials).Select(p => p.Sku).ToHashSet(StringComparer.Ordinal);
}
