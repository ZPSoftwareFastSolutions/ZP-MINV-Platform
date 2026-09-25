using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Hardware.Printers;

namespace MINV.Hardware;

/// <summary>Configuración de periféricos del punto de venta (appsettings: sección «Hardware»).</summary>
public sealed record HardwareOptions(string? PrinterKind = null, string? PrinterTarget = null, int BaudRate = 9600);

public static class DependencyInjection
{
    /// <summary>Registra la impresora de comprobantes según la configuración: <c>serial</c> (COMx), <c>network</c>
    /// (host:9100) o <c>windows</c> (nombre de la impresora instalada, USB).</summary>
    public static IServiceCollection AddMinvHardware(this IServiceCollection services, HardwareOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PrinterTarget is not { Length: > 0 } target)
        {
            return services;
        }
        IReceiptPrinter printer = options.PrinterKind?.ToLowerInvariant() switch
        {
            "serial" => new SerialReceiptPrinter(target, options.BaudRate),
            "network" => target.Split(':') is [var host, var port] ? new NetworkReceiptPrinter(host, int.Parse(port, System.Globalization.CultureInfo.InvariantCulture)) : new NetworkReceiptPrinter(target),
            "windows" when OperatingSystem.IsWindows() => new WindowsRawReceiptPrinter(target),
            _ => throw new InvalidOperationException($"Tipo de impresora no soportado: {options.PrinterKind}."),
        };
        services.AddSingleton(printer);
        return services;
    }
}
