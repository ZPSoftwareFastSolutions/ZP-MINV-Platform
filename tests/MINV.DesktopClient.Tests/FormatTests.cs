using System.IO;
using MINV.Application.Iam;
using MINV.DesktopClient.Services;
using MINV.DesktopClient.ViewModels;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.Tests;

/// <summary>Formatos en español, lectura de cantidades, exportación a Excel y resumen de la auditoría.</summary>
public sealed class FormatTests
{
    [Theory]
    [InlineData("2,5", 2.5)]
    [InlineData("2.5", 2.5)]
    [InlineData(" 7 ", 7)]
    [InlineData("1.234,5", 1234.5)]
    [InlineData("1,234.5", 1234.5)]
    [InlineData("0,125", 0.125)]
    public void Lee_cantidades_con_coma_o_punto_decimal(string text, double expected)
    {
        Assert.True(Fmt.TryParseQuantity(text, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("2,5,x")]
    public void Rechaza_lo_que_no_es_un_numero(string text) => Assert.False(Fmt.TryParseQuantity(text, out _));

    [Fact]
    public void Textos_para_personas()
    {
        Assert.Equal("Devolución de cliente", Fmt.SentenceCase("DEVOLUCIÓN DE CLIENTE"));
        Assert.Equal("Venta POS", Fmt.SentenceCase("VENTA POS"));
        Assert.Equal("Ajuste (+)", Fmt.SentenceCase("AJUSTE (+)"));
        Assert.Equal("AG", Fmt.Initials("Ana Gómez"));
        Assert.Equal("AD", Fmt.Initials("admin"));
        Assert.Equal("Registró un movimiento", Fmt.Action("RegisterMovement"));
        Assert.Equal("Registrar entrada", Fmt.Action("RegistrarEntrada"));
        Assert.Equal("Recalcular stock", Fmt.Action("RecalcularStock.ts"));
        var now = new DateTimeOffset(2026, 9, 25, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal("hace un momento", Fmt.Relative(now.AddSeconds(-20), now));
        Assert.Equal("hace 5 min", Fmt.Relative(now.AddMinutes(-5), now));
        Assert.Equal("—", Fmt.Coverage(null));
        Assert.Equal("1 día", Fmt.Coverage(1));
        Assert.Equal("12 días", Fmt.Coverage(12));
    }

    [Fact]
    public void El_resumen_de_la_auditoria_se_lee_en_lenguaje_natural()
    {
        var now = DateTimeOffset.UtcNow;
        var v3 = new ActivityItem(new ActivityRow(now, "ana@demo.example", "Ana Gómez", "RegisterMovement", AuditOutcome.Succeeded,
            """{"request":{"Sku":"FER-001","BinCode":"ALM01-A-01-01","Quantity":5,"BusinessDate":null},"result":{"Message":"✔ Registrado · ENTRADA 5 UND"},"error":null}"""), now);
        Assert.Equal("SKU: FER-001 · Posición: ALM01-A-01-01 · Cantidad: 5 · Registrado · ENTRADA 5 UND", v3.Details);
        Assert.Equal("Registró un movimiento", v3.ActionText);

        var rejected = new ActivityItem(new ActivityRow(now, null, null, "RegisterMovement", AuditOutcome.Rejected,
            """{"request":{"Sku":"FER-001"},"result":null,"error":"Stock insuficiente: disponible 2, solicitado 9."}"""), now);
        Assert.Equal("Stock insuficiente: disponible 2, solicitado 9.", rejected.Details);
        Assert.Equal("Sistema", rejected.UserName);

        var v21 = new ActivityItem(new ActivityRow(now, "ana@demo.example", "Ana Gómez", "RegistrarEntrada", AuditOutcome.Succeeded,
            """{"resultado":"✔","detalle":"Registrado E-20260925-101500-AB12 · ENTRADA 20 UND"}"""), now);
        Assert.Equal("Registrado E-20260925-101500-AB12 · ENTRADA 20 UND", v21.Details);
    }

    [Fact]
    public void El_CSV_abre_en_Excel_con_acentos_y_neutraliza_formulas()
    {
        var path = Path.Combine(Path.GetTempPath(), $"minv-prueba-{Guid.NewGuid():N}.csv");
        try
        {
            Csv.Write(path, ["Producto", "Cantidad", "Nota"],
            [
                ["Tornillo «drywall»; caja", 2.5m, "=HYPERLINK(\"http://x\")"],
                ["Cinta métrica", -5m, "+SUMA(A1)"],
                ["Martillo \"pro\"", 10, "-texto"],
            ]);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);   // UTF-8 con BOM: Excel respeta los acentos
            var lines = File.ReadAllLines(path);
            var sep = lines[0][8].ToString();
            Assert.Equal("Producto" + sep + "Cantidad" + sep + "Nota", lines[0]);
            Assert.Contains("'=HYPERLINK", lines[1], StringComparison.Ordinal);
            Assert.Contains("'+SUMA", lines[2], StringComparison.Ordinal);
            Assert.Contains("'-texto", lines[3], StringComparison.Ordinal);
            Assert.Contains(sep + "-5" + sep, lines[2], StringComparison.Ordinal);   // un número negativo no se altera
            Assert.Contains("\"Martillo \"\"pro\"\"\"", lines[3], StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
