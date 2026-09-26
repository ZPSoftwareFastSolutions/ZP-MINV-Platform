using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Hardware.EscPos;

namespace MINV.Hardware.Tests;

/// <summary>Representación gráfica fiscal en rollo (ESC/POS): contenido, QR nativo con la URL del SIN (t=1) y corte.</summary>
public sealed class FiscalRollRendererTests
{
    private const string Cuf = "9C1A83F996B702B8497F0555BFF4C27CB7E1783A67A84B0CA3176D74";
    private const string QrUrl = "https://pilotosiat.impuestos.gob.bo/consulta/QR?nit=123451020&cuf=" + Cuf + "&numero=2377&t=2";

    private static readonly Encoding Pc858 = CreatePc858();

    private static readonly string[] Legends =
    [
        "ESTA FACTURA CONTRIBUYE AL DESARROLLO DEL PAÍS, EL USO ILÍCITO SERÁ SANCIONADO PENALMENTE DE ACUERDO A LEY",
        "Ley N° 453: El proveedor deberá entregar el producto en las modalidades y términos ofertados o convenidos.",
        "“Este documento es la Representación Gráfica de un Documento Fiscal Digital emitido en una modalidad de facturación en línea”",
    ];

    internal static FiscalPrintModel Invoice(bool isTest = false, bool isVoided = false, bool isOffline = false) => new(
        "FACTURA", "(Con Derecho a Crédito Fiscal)", "Metales Totai S.R.L.", 123451020, "SUCURSAL N. 5", 0, "Av. Juan XXIII", "2824512",
        "Yacuiba", 2377, Cuf, new DateTime(2022, 5, 6, 9, 19, 42, 957), "DAVID ZELADA", "987654", "1864",
        [
            new("GA-CL-013", "GANCHO P/ CALAMINA M6*J 60", "PIEZAS", 200m, 0.63m, 7.52m, 118.48m),
            new("GA-CL-015", "GANCHO P/ CALAMINA M6*J 70", "PIEZAS", 100m, 0.72m, 4.11m, 67.89m),
            new("RP-FR-01", "REPOSICION DE FORMULARIO", "UNIDAD (BIENES)", 1m, 0.98m, 0m, 0.98m),
            new("HI-CO-004", "HI. CORRUGADO 5/16\" (8,00MM) CA-50", "TUBOS", 3m, 40.72m, 0m, 122.16m),
        ],
        309.51m, 0m, 309.51m, 0m, 309.51m, 309.51m, "Trescientos nueve 51/100 Bolivianos", "EFECTIVO", "JPEREZ", Legends, QrUrl,
        isTest, isVoided, isOffline);

    internal static FiscalPrintModel CreditNote() => new(
        "NOTA CRÉDITO - DÉBITO", string.Empty, "ENTEL", 1003579028, "CASA MATRIZ", 0, "AV. JORGE LOPEZ #123", "2846005", "La Paz", 1,
        "44AAEC00DBD34C53C3E5B135433591A5FA086F86A867A75AC82F24C74", new DateTime(2021, 10, 6, 16, 3, 49, 570), "Juan Valdez", "5115889",
        "51158891",
        [
            new("123456", "Amortiguadores", "OTRO", 1m, 775m, 0m, 775m, 1),
            new("123457", "Tornillos", "OTRO", 1m, 75m, 0m, 75m, 2),
        ],
        75m, 0m, 75m, 0m, 75m, 75m, "Setenta y cinco 00/100 Bolivianos", null, null, Legends, QrUrl, false, false, false,
        new FiscalPrintOriginal(1, "44AAEC00DBD34C53C3E2CCE1A3FA7AF1E2A08606A667A75AC82F24C74", new DateTime(2021, 10, 6, 16, 3, 48, 675)),
        ReturnedTotal: 75m, CreditDebitAmount: 9.75m);

    [Fact]
    public void El_rollo_lleva_el_QR_nativo_con_la_URL_del_SIN_para_rollo()
    {
        var bytes = new FiscalRollRenderer().RenderRoll(Invoice());
        Assert.True(EscPosTests.IndexOf(bytes, [0x1D, (byte)'(', (byte)'k']) > 0);                    // GS ( k
        Assert.True(EscPosTests.IndexOf(bytes, [0x1D, (byte)'(', (byte)'k', 3, 0, 49, 81, 48]) > 0);   // imprimir el QR
        var url = QrUrl.Replace("&t=2", "&t=1", StringComparison.Ordinal);
        Assert.True(EscPosTests.IndexOf(bytes, Encoding.UTF8.GetBytes(url)) > 0);
        Assert.Equal(-1, EscPosTests.IndexOf(bytes, Encoding.UTF8.GetBytes("t=2")));
        Assert.Equal(new byte[] { 0x1D, (byte)'V', 66, 3 }, bytes[^4..]);                             // corte al final
    }

    [Fact]
    public void El_rollo_lleva_los_datos_de_la_factura_con_acentos_en_PC858()
    {
        var text = Text(new FiscalRollRenderer().RenderRoll(Invoice()));
        Assert.Contains("Metales Totai S.R.L.", text);
        Assert.Contains("SUCURSAL N. 5", text);
        Assert.Contains("No. Punto de Venta 0", text);
        Assert.Contains("FACTURA", text);
        Assert.Contains("CON DERECHO A CRÉDITO FISCAL", text);
        Assert.Contains("FACTURA N°", text);
        Assert.Contains("2377", text);
        Assert.Contains("CÓD. AUTORIZACIÓN:", text);
        Assert.Contains(Cuf[..48], text);                 // el CUF partido en líneas de 48 columnas
        Assert.Contains(Cuf[48..], text);
        Assert.Contains("Nombre/Razón Social: DAVID ZELADA", text);
        Assert.Contains("NIT/CI/CEX: 987654", text);
        Assert.Contains("Cod. Cliente: 1864", text);
        Assert.Contains("200.00 PIEZAS x 0.63 - 7.52 =", text);
        Assert.Contains("118.48", text);
        Assert.Contains("MONTO A PAGAR Bs", text);
        Assert.Contains("IMPORTE BASE CRÉDITO FISCAL", text);
        Assert.Contains("309.51", text);
        Assert.Contains("Son: Trescientos nueve 51/100 Bolivianos", text);
        Assert.Contains("Ley N° 453:", text);
        Assert.Contains("\"Este documento es la Representación Gráfica", text);   // comillas tipográficas → rectas
        Assert.DoesNotContain("SIN VALOR LEGAL", text);
        Assert.DoesNotContain('?', text);   // ningún carácter quedó fuera de la página de códigos
    }

    [Fact]
    public void Marcas_de_prueba_anulado_y_fuera_de_linea()
    {
        var text = Text(new FiscalRollRenderer().RenderRoll(Invoice(isTest: true, isVoided: true, isOffline: true)));
        Assert.Contains("*** SIN VALOR LEGAL ***", text);
        Assert.Contains("*** ANULADO ***", text);
        Assert.Contains("FUERA DE LÍNEA", text);
    }

    [Fact]
    public void La_nota_lleva_la_factura_original_y_las_dos_secciones_de_detalle()
    {
        var text = Text(new FiscalRollRenderer().RenderRoll(CreditNote()));
        Assert.Contains("NOTA CRÉDITO - DÉBITO", text);
        Assert.Contains("NOTA N°", text);
        Assert.Contains("N° Factura: 1", text);
        Assert.Contains("N° Autorización/CUF:", text);
        Assert.Contains("DETALLE DOCUMENTO ORIGEN", text);
        Assert.Contains("MONTO TOTAL ORIGINAL Bs", text);
        Assert.Contains("775.00", text);
        Assert.Contains("DETALLE DE LA DEVOLUCIÓN O RESCISIÓN DE SERVICIO", text);
        Assert.Contains("MONTO TOTAL DEVUELTO Bs", text);
        Assert.Contains("MONTO EFECTIVO DEL CRÉDITO O DÉBITO (13%) Bs", text);
        Assert.Contains("9.75", text);
        Assert.DoesNotContain("MONTO A PAGAR", text);
    }

    [Fact]
    public void En_58_mm_ninguna_linea_supera_32_columnas()
    {
        var lines = Lines(new FiscalRollRenderer().RenderRoll(Invoice(), columns: 32));
        Assert.Contains(new string('-', 32), lines);
        Assert.DoesNotContain(new string('-', 48), lines);
        Assert.All(lines, l => Assert.True(l.Length <= 32, $"«{l}» mide {l.Length}"));
        var wide = Lines(new FiscalRollRenderer().RenderRoll(CreditNote(), columns: 48));
        Assert.All(wide, l => Assert.True(l.Length <= 48, $"«{l}» mide {l.Length}"));
    }

    [Fact]
    public void Se_registra_con_el_hardware()
    {
        using var provider = new ServiceCollection().AddMinvHardware(new HardwareOptions()).BuildServiceProvider();
        Assert.IsType<FiscalRollRenderer>(provider.GetRequiredService<IFiscalRollRenderer>());
    }

    [Fact]
    public void La_URL_del_QR_fija_el_tamano_una_sola_vez()
    {
        Assert.Equal("https://x/QR?nit=1&cuf=A&numero=2&t=1", FiscalRollRenderer.WithQrSize("https://x/QR?nit=1&cuf=A&numero=2&t=2", 1));
        Assert.Equal("https://x/QR?nit=1&t=1", FiscalRollRenderer.WithQrSize("https://x/QR?nit=1", 1));
        Assert.Equal("https://x/QR?t=1", FiscalRollRenderer.WithQrSize("https://x/QR", 1));
    }

    /// <summary>Texto imprimible del documento: quita los comandos ESC/GS y el QR, y separa las líneas.</summary>
    private static string Text(byte[] bytes) => string.Join('\n', Lines(bytes));

    private static List<string> Lines(byte[] bytes)
    {
        var lines = new List<string>();
        var current = new List<byte>();
        var i = 0;
        while (i < bytes.Length)
        {
            var b = bytes[i];
            if (b == EscPosDocument.Esc)
            {
                if (bytes[i + 1] == (byte)'d')
                {
                    lines.Add(Pc858.GetString(current.ToArray()));
                    current.Clear();
                }
                i += bytes[i + 1] == (byte)'@' ? 2 : 3;   // ESC @ · ESC t n · ESC a n · ESC E n · ESC d n
            }
            else if (b == EscPosDocument.Gs && bytes[i + 1] == (byte)'(' && bytes[i + 2] == (byte)'k')
            {
                i += 5 + (bytes[i + 3] | (bytes[i + 4] << 8));   // funciones del QR (modelo, tamaño, corrección, datos, imprimir)
            }
            else if (b == EscPosDocument.Gs)
            {
                i += bytes[i + 1] == (byte)'V' ? 4 : 3;   // GS V m n · GS ! n
            }
            else
            {
                current.Add(b);
                i++;
            }
        }
        if (current.Count > 0)
        {
            lines.Add(Pc858.GetString(current.ToArray()));
        }
        return lines;
    }

    private static Encoding CreatePc858()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(858);
    }
}
