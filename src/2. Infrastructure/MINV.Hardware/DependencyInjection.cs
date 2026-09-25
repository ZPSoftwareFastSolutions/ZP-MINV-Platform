using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Hardware.Printers;

namespace MINV.Hardware;

/// <summary>Configuración de periféricos del punto de venta (appsettings: sección «Hardware»).</summary>
public sealed record HardwareOptions(string? PrinterKind = null, string? PrinterTarget = null, int BaudRate = 9600);

public static class DependencyInjection
{
    /// <summary>Registra la impresora de comprobantes según la configuración (ver <see cref="ReceiptPrinters"/>).</summary>
    public static IServiceCollection AddMinvHardware(this IServiceCollection services, HardwareOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (ReceiptPrinters.Create(options) is { } printer)
        {
            services.AddSingleton(printer);
        }
        return services;
    }
}

/// <summary>Fábrica de impresoras de comprobantes a partir de la configuración.</summary>
public static class ReceiptPrinters
{
    /// <summary>Tipos admitidos (valor de <see cref="HardwareOptions.PrinterKind"/>).</summary>
    public static readonly IReadOnlyList<(string Kind, string Name, string Example)> Kinds =
    [
        ("serial", "Puerto serie (COM / RS-232)", "COM3"),
        ("network", "Red (Ethernet / Wi-Fi)", "192.168.1.50:9100"),
        ("windows", "Impresora de Windows (USB)", "EPSON TM-T20III"),
    ];

    /// <summary><c>serial</c> (COMx), <c>network</c> (host:9100) o <c>windows</c> (nombre de la impresora instalada, USB).
    /// Devuelve null si no hay impresora configurada.</summary>
    public static IReceiptPrinter? Create(HardwareOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PrinterTarget is not { Length: > 0 } target)
        {
            return null;
        }
        return options.PrinterKind?.ToLowerInvariant() switch
        {
            "serial" => new SerialReceiptPrinter(target, options.BaudRate),
            "network" => target.Split(':') is [var host, var port]
                ? new NetworkReceiptPrinter(host, int.Parse(port, System.Globalization.CultureInfo.InvariantCulture))
                : new NetworkReceiptPrinter(target),
            "windows" when OperatingSystem.IsWindows() => new WindowsRawReceiptPrinter(target),
            _ => throw new InvalidOperationException($"Tipo de impresora no soportado: {options.PrinterKind}."),
        };
    }
}
