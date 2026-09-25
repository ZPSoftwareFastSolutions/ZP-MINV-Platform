using System.ComponentModel;
using System.IO.Ports;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MINV.Application.Abstractions;

namespace MINV.Hardware.Printers;

/// <summary>Impresora ESC/POS conectada por puerto serie (RS-232 o USB-CDC que Windows expone como COMx).</summary>
public sealed class SerialReceiptPrinter(string portName, int baudRate = 9600) : IReceiptPrinter
{
    public string Name => $"ESC/POS {portName} ({baudRate} bps)";

    public async Task PrintAsync(ReadOnlyMemory<byte> document, CancellationToken cancellationToken = default)
    {
        using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.RequestToSend,
            WriteTimeout = 5000,
        };
        port.Open();
        await port.BaseStream.WriteAsync(document, cancellationToken);
        await port.BaseStream.FlushAsync(cancellationToken);
    }
}

/// <summary>Impresora de red (puerto 9100, «raw TCP»).</summary>
public sealed class NetworkReceiptPrinter(string host, int port = 9100) : IReceiptPrinter
{
    public string Name => $"ESC/POS {host}:{port}";

    public async Task PrintAsync(ReadOnlyMemory<byte> document, CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken);
        await using var stream = client.GetStream();
        await stream.WriteAsync(document, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}

/// <summary>Impresora USB instalada en Windows: envía los bytes ESC/POS en crudo a la cola de impresión (RAW).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRawReceiptPrinter(string printerName) : IReceiptPrinter
{
    public string Name => printerName;

    public Task PrintAsync(ReadOnlyMemory<byte> document, CancellationToken cancellationToken = default) =>
        Task.Run(() => Send(document.ToArray()), cancellationToken);

    private void Send(byte[] bytes)
    {
        if (!NativeMethods.OpenPrinter(printerName, out var handle, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"No se pudo abrir la impresora «{printerName}».");
        }
        try
        {
            var info = new NativeMethods.DocInfo { DocName = "M-INV comprobante", DataType = "RAW" };
            if (!NativeMethods.StartDocPrinter(handle, 1, info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            try
            {
                NativeMethods.StartPagePrinter(handle);
                var unmanaged = Marshal.AllocCoTaskMem(bytes.Length);
                try
                {
                    Marshal.Copy(bytes, 0, unmanaged, bytes.Length);
                    if (!NativeMethods.WritePrinter(handle, unmanaged, bytes.Length, out var written) || written != bytes.Length)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "La impresora no recibió todo el documento.");
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(unmanaged);
                }
                NativeMethods.EndPagePrinter(handle);
            }
            finally
            {
                NativeMethods.EndDocPrinter(handle);
            }
        }
        finally
        {
            NativeMethods.ClosePrinter(handle);
        }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public sealed class DocInfo
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string DocName = string.Empty;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? OutputFile;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string DataType = "RAW";
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool OpenPrinter(string printerName, out IntPtr handle, IntPtr defaults);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr handle);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool StartDocPrinter(IntPtr handle, int level, [In] DocInfo info);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndDocPrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool StartPagePrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndPagePrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool WritePrinter(IntPtr handle, IntPtr bytes, int count, out int written);
    }
}
