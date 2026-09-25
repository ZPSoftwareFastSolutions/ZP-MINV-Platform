using MINV.Application.Abstractions;
using MINV.Hardware.EscPos;
using MINV.Hardware.Scanners;

namespace MINV.Hardware.Tests;

public sealed class EscPosTests
{
    internal static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }
        return -1;
    }

    [Fact]
    public void Inicializa_y_selecciona_la_pagina_de_codigos_PC858()
    {
        var bytes = new EscPosDocument().ToArray();
        Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x74, 19 }, bytes);
    }

    [Fact]
    public void Los_acentos_y_la_enie_se_codifican_en_PC858()
    {
        var bytes = new EscPosDocument().Text("Ñandú ó").ToArray()[5..];
        Assert.Equal(new byte[] { 0xA5, (byte)'a', (byte)'n', (byte)'d', 0xA3, (byte)' ', 0xA2 }, bytes);
    }

    [Fact]
    public void Negrita_tamano_corte_y_cajon()
    {
        var bytes = new EscPosDocument().Bold(true).Size(2, 2).OpenCashDrawer().Cut().ToArray();
        Assert.True(IndexOf(bytes, [0x1B, (byte)'E', 1]) > 0);
        Assert.True(IndexOf(bytes, [0x1D, (byte)'!', 0x11]) > 0);
        Assert.True(IndexOf(bytes, [0x1B, (byte)'p', 0, 25, 250]) > 0);
        Assert.Equal(new byte[] { 0x1D, (byte)'V', 66, 3 }, bytes[^4..]);
    }

    [Fact]
    public void Codigo_de_barras_CODE128_y_QR()
    {
        var bytes = new EscPosDocument().Code128("FV-0001").Qr("https://minv.example/v/1").ToArray();
        Assert.True(IndexOf(bytes, [0x1D, (byte)'k', 73, 9, (byte)'{', (byte)'B', (byte)'F']) > 0);
        Assert.True(IndexOf(bytes, [0x1D, (byte)'(', (byte)'k', 3, 0, 49, 81, 48]) > 0);   // imprimir el QR
    }

    [Fact]
    public void Dos_columnas_llenan_exactamente_el_ancho()
    {
        var doc = new EscPosDocument(32).Columns2("Tornillo drywall 6x1\" caja x100 muy largo", "1.234,50");
        var text = System.Text.Encoding.Latin1.GetString(doc.ToArray()[5..^3]);
        Assert.Equal(32, text.Length);
        Assert.EndsWith("1.234,50", text);
    }

    [Fact]
    public void El_comprobante_incluye_encabezado_lineas_total_y_corte()
    {
        var receipt = new Receipt("Distribuidora Demo", "900123456", "Casa matriz", "FV-0001", new DateTimeOffset(2026, 9, 25, 16, 0, 0, TimeSpan.Zero),
            "Carlos Ruiz", [new ReceiptLine("FER-001 Tornillo drywall", 2, 9_800m)], 19_600m, "Bs", "EFECTIVO", "FV-0001");
        var bytes = ReceiptRenderer.Render(receipt, 48, openDrawer: true);
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.Contains("Distribuidora Demo", text);
        Assert.Contains("TOTAL Bs", text);
        Assert.Contains("19.600,00", text);
        Assert.True(IndexOf(bytes, [0x1B, (byte)'p', 0, 25, 250]) > 0);
        Assert.Equal(new byte[] { 0x1D, (byte)'V', 66, 3 }, bytes[^4..]);
    }
}

public sealed class ScannerTests
{
    [Fact]
    public void El_lector_serie_emite_una_lectura_por_linea()
    {
        var scanner = new SerialBarcodeScanner("COM9");
        var codes = new List<string>();
        scanner.Scanned += (_, e) => codes.Add(e.Code);
        scanner.Feed("75010313");
        scanner.Feed("11309\r\nFER-001\r");
        Assert.Equal(["7501031311309", "FER-001"], codes);
    }

    [Fact]
    public void El_modo_teclado_distingue_el_escaner_del_tecleo_humano()
    {
        var detector = new KeyboardWedgeDetector();
        var codes = new List<string>();
        detector.Scanned += (_, e) => codes.Add(e.Code);
        var t = DateTimeOffset.UtcNow;
        foreach (var ch in "7501031311309")
        {
            detector.OnKey(ch, t = t.AddMilliseconds(8));
        }
        Assert.True(detector.OnKey('\r', t.AddMilliseconds(8)));
        foreach (var ch in "ab")
        {
            detector.OnKey(ch, t = t.AddMilliseconds(400));   // tecleo humano: lento y corto
        }
        Assert.False(detector.OnKey('\r', t.AddMilliseconds(400)));
        Assert.Equal(["7501031311309"], codes);
    }
}

/// <summary>Página de prueba y fábrica de impresoras (pantalla Configuración del cliente).</summary>
public sealed class PrinterTests
{
    [Fact]
    public void La_pagina_de_prueba_incluye_acentos_codigo_de_barras_QR_y_corte()
    {
        var bytes = ReceiptRenderer.RenderTestPage("Distribuidora Demo", "COM3", new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        Assert.True(EscPosTests.IndexOf(bytes, [0xA4]) > 0);                          // ñ en PC858
        Assert.True(EscPosTests.IndexOf(bytes, [0x1D, (byte)'k', 73]) > 0);           // CODE128
        Assert.True(EscPosTests.IndexOf(bytes, [0x1D, (byte)'(', (byte)'k']) > 0);     // QR
        Assert.Equal(new byte[] { 0x1D, (byte)'V', 66, 3 }, bytes[^4..]);  // corte
    }

    [Fact]
    public void Sin_destino_no_hay_impresora_y_un_tipo_desconocido_se_rechaza()
    {
        Assert.Null(ReceiptPrinters.Create(new HardwareOptions()));
        Assert.IsType<MINV.Hardware.Printers.NetworkReceiptPrinter>(ReceiptPrinters.Create(new HardwareOptions("network", "10.0.0.5:9100")));
        Assert.Throws<InvalidOperationException>(() => ReceiptPrinters.Create(new HardwareOptions("usb-magico", "X")));
    }
}
