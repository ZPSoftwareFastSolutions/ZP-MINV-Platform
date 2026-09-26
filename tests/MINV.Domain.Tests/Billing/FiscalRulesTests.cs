using System.Globalization;
using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Domain.Tests.Billing;

public sealed class FiscalRulesTests
{
    [Theory]
    [InlineData("3.14159", "3.14")]   // ejemplos de la página «Algoritmo de redondeo»
    [InlineData("3.14559", "3.15")]
    [InlineData("0.005", "0.01")]
    [InlineData("2.345", "2.35")]    // Math.Round sin modo (al par) daría 2.34
    [InlineData("1.005", "1.01")]    // en double daría 1.00
    [InlineData("2.344999", "2.34")]
    [InlineData("9.1767", "9.18")]
    [InlineData("100", "100")]
    public void Round2_es_HALF_UP_a_2_decimales(string value, string expected)
    {
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), FiscalRules.Round2(decimal.Parse(value, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Subtotal_de_linea_y_13_por_ciento_con_los_numeros_de_los_ejemplos_oficiales()
    {
        Assert.Equal(118.48m, FiscalRules.LineSubtotal(200m, 0.63m, 7.52m));
        Assert.Equal(67.89m, FiscalRules.LineSubtotal(100m, 0.72m, 4.11m));
        Assert.Equal(122.16m, FiscalRules.LineSubtotal(3m, 40.72m, null));
        Assert.Equal(9.75m, FiscalRules.Vat(75m));
        Assert.Equal(100.75m, FiscalRules.Vat(775m));
        Assert.Equal(9.18m, FiscalRules.Vat(70.59m));
    }

    [Theory]
    [InlineData("2026-09-25 10:00", "2026-10-09")]   // ejemplos de la investigación 03 §11.5
    [InlineData("2026-10-01 00:00", "2026-11-09")]
    [InlineData("2026-12-31 23:59", "2027-01-09")]
    [InlineData("2027-01-31 08:30", "2027-02-09")]
    public void Plazo_de_anulacion_hasta_el_fin_del_dia_9_del_mes_siguiente(string issued, string lastDay)
    {
        var deadline = FiscalRules.VoidDeadline(DateTime.ParseExact(issued, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        var day = DateTime.ParseExact(lastDay, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal(day.Date, deadline.Date);
        Assert.Equal(day.AddDays(1), deadline.AddTicks(1));   // inclusivo hasta el último instante del día 9
    }

    [Fact]
    public void Plazo_de_la_nota_credito_debito_es_de_18_meses()
    {
        Assert.Equal(new DateTime(2027, 7, 15, 9, 0, 0), FiscalRules.CreditNoteDeadline(new DateTime(2026, 1, 15, 9, 0, 0)));
    }

    [Theory]
    [InlineData("4797123412347896", "4797000000007896")]
    [InlineData("4797 1234 1234 7896", "4797000000007896")]
    [InlineData("4797-1234-1234-7896", "4797000000007896")]
    [InlineData("4797********7896", "4797000000007896")]
    [InlineData("4797XXXXXXXX7896", "4797000000007896")]
    [InlineData("379712345677896", "379700000007896")]
    public void La_tarjeta_se_enmascara_con_4_primeros_ceros_y_4_ultimos(string card, string masked)
    {
        Assert.Equal(masked, FiscalRules.MaskCard(card));
    }

    [Theory]
    [InlineData("12345", "card.length")]
    [InlineData("47971234123478961234", "card.length")]
    [InlineData("****123412347896", "card.digits")]
    public void Una_tarjeta_invalida_se_rechaza(string card, string code)
    {
        Assert.Equal(code, Assert.Throws<DomainException>(() => FiscalRules.MaskCard(card)).Code);
    }

    [Fact]
    public void Documento_del_comprador_CI_y_NIT_numericos_y_complemento_solo_con_CI()
    {
        FiscalRules.EnsureBuyerDocument(SiatCodes.DocumentCi, "5115889", "1A");
        FiscalRules.EnsureBuyerDocument(SiatCodes.DocumentNit, "1003579028", null);
        FiscalRules.EnsureBuyerDocument(SiatCodes.DocumentCex, "E-12345", null);
        FiscalRules.EnsureBuyerDocument(SiatCodes.DocumentPassport, "AB123456", " ");
        Assert.Equal("buyer.doc_numeric", Assert.Throws<DomainException>(() => FiscalRules.EnsureBuyerDocument(SiatCodes.DocumentCi, "51158A9", null)).Code);
        Assert.Equal("buyer.doc_numeric", Assert.Throws<DomainException>(() => FiscalRules.EnsureBuyerDocument(SiatCodes.DocumentNit, "1003-579", null)).Code);
        Assert.Equal("buyer.complement", Assert.Throws<DomainException>(() => FiscalRules.EnsureBuyerDocument(SiatCodes.DocumentNit, "1003579028", "1A")).Code);
        Assert.Equal("buyer.complement", Assert.Throws<DomainException>(() => FiscalRules.EnsureBuyerDocument(SiatCodes.DocumentCi, "5115889", "123456")).Code);
        Assert.Equal("buyer.doc_number", Assert.Throws<DomainException>(() => FiscalRules.EnsureBuyerDocument(SiatCodes.DocumentCi, " ", null)).Code);
        Assert.Equal("buyer.doc_number", Assert.Throws<DomainException>(() => FiscalRules.EnsureBuyerDocument(SiatCodes.DocumentOther, new string('9', 21), null)).Code);
        Assert.Equal("buyer.doc_type", Assert.Throws<DomainException>(() => FiscalRules.EnsureBuyerDocument(6, "5115889", null)).Code);
        Assert.Equal("buyer.doc_type", Assert.Throws<DomainException>(() => FiscalRules.EnsureBuyerDocument(0, "5115889", null)).Code);
    }

    [Theory]
    [InlineData("1.23", 2, true)]
    [InlineData("1.230", 2, true)]
    [InlineData("100", 2, true)]
    [InlineData("1.234", 2, false)]
    [InlineData("0.005", 2, false)]
    [InlineData("0.3333333333", 10, true)]
    [InlineData("0.33333333333", 10, false)]
    public void Cuenta_los_decimales_significativos(string value, int decimals, bool expected)
    {
        Assert.Equal(expected, FiscalRules.HasAtMostDecimals(decimal.Parse(value, CultureInfo.InvariantCulture), decimals));
    }

    [Fact]
    public void Formato_de_fecha_y_hora_fiscal_con_milisegundos_y_sin_zona()
    {
        Assert.Equal("2021-10-06T16:03:48.675", FiscalRules.FormatDateTime(new DateTime(2021, 10, 6, 16, 3, 48, 675)));
        var laPaz = TimeZoneInfo.CreateCustomTimeZone("La Paz", TimeSpan.FromHours(-4), "La Paz", "BOT");
        var fiscal = FiscalRules.ToFiscalTime(new DateTimeOffset(2026, 9, 25, 20, 0, 0, 123, TimeSpan.Zero), laPaz);
        Assert.Equal(new DateTime(2026, 9, 25, 16, 0, 0, 123), fiscal);
        Assert.Equal(DateTimeKind.Unspecified, fiscal.Kind);
    }

    [Fact]
    public void Plazos_de_contingencia()
    {
        Assert.Equal(TimeSpan.FromHours(48), FiscalRules.EventRegistrationWindow);
        Assert.Equal(TimeSpan.FromHours(72), FiscalRules.CafcTranscriptionWindow);
        Assert.Equal(TimeSpan.FromHours(72), FiscalRules.CufdExtendedValidity);
        Assert.Equal(TimeSpan.FromHours(2), FiscalRules.MaxOfflineRetryInterval);
    }

    /// <summary>
    /// V4.2 · Costo neto y factura del proveedor (Bolivia: el crédito fiscal es el 13 % del importe facturado). Para cualquier
    /// recepción al costo neto, la factura recepción / 0,87 (redondeada) deja el crédito fiscal (13 % redondeado) igual a la
    /// diferencia entre la factura y la recepción: el asiento de la factura no toca el inventario (1.1.05), hasta en el redondeo
    /// (el 13 % de un error de medio centavo no llega a mover el crédito). Con recepción × 1,13 (la convención anterior) el
    /// inventario bajaba el 1,69 % de la recepción.
    /// </summary>
    [Fact]
    public void La_factura_de_una_compra_al_costo_neto_deja_el_credito_fiscal_igual_al_IVA_sumado_a_la_deuda()
    {
        Assert.Equal(1480.74m, FiscalRules.NetCost(1702m));
        Assert.Equal(1702m, FiscalRules.InvoiceForNetCost(1480.74m));
        Assert.Equal(1149.43m, FiscalRules.InvoiceForNetCost(1000m));
        var random = new Random(42);
        for (var i = 0; i < 20_000; i++)
        {
            var net = random.Next(1, 50_000_000) / 100m;
            var invoice = FiscalRules.InvoiceForNetCost(net);
            Assert.Equal(invoice - net, FiscalRules.Vat(invoice));
        }
        // Con la convención anterior (factura = recepción × 1,13) el crédito fiscal superaba al IVA sumado a la deuda
        Assert.Equal(146.90m, FiscalRules.Vat(1130m));
        Assert.Equal(16.90m, FiscalRules.Vat(1130m) - (1130m - 1000m));
    }

    /// <summary>V4.2 · IVA incluido en el precio: en Bolivia el 13 % del importe (el débito fiscal del libro de ventas) y el neto
    /// el 87 %; en los demás países (tasa del 19 %), importe × tasa / (100 + tasa).</summary>
    [Fact]
    public void El_IVA_incluido_sigue_la_convencion_del_pais()
    {
        Assert.True(MINV.Domain.Accounting.VatRules.OnInvoicedAmount("BO"));
        Assert.True(MINV.Domain.Accounting.VatRules.OnInvoicedAmount(null));
        Assert.False(MINV.Domain.Accounting.VatRules.OnInvoicedAmount("CO"));
        Assert.Equal(13m, MINV.Domain.Accounting.VatRules.IncludedTax(100m, 13m, onInvoicedAmount: true));
        Assert.Equal(FiscalRules.Vat(2049m), MINV.Domain.Accounting.VatRules.IncludedTax(2049m, 13m, onInvoicedAmount: true));
        Assert.Equal(11.50m, MINV.Domain.Accounting.VatRules.IncludedTax(100m, 13m, onInvoicedAmount: false));
        Assert.Equal(19m, MINV.Domain.Accounting.VatRules.IncludedTax(119m, 19m, onInvoicedAmount: false));
        Assert.Equal(1782.63m, MINV.Domain.Accounting.VatRules.NetOf(2049m, 13m, onInvoicedAmount: true));
        Assert.Equal(100m, MINV.Domain.Accounting.VatRules.NetOf(119m, 19m, onInvoicedAmount: false));
        Assert.Equal(100m, MINV.Domain.Accounting.VatRules.GrossOf(87m, 13m, onInvoicedAmount: true));
        Assert.Equal(119m, MINV.Domain.Accounting.VatRules.GrossOf(100m, 19m, onInvoicedAmount: false));
        Assert.Equal(0m, MINV.Domain.Accounting.VatRules.IncludedTax(100m, 0m, onInvoicedAmount: true));
        // Con el costo y el precio netos al 87 %, el margen es (precio − costo con IVA) / precio: el «margen_pct» del catálogo
        var (price, cost) = (2049m, FiscalRules.NetCost(1702m));
        var net = MINV.Domain.Accounting.VatRules.NetOf(price, 13m, onInvoicedAmount: true);
        Assert.InRange((net - cost) / net - (price - 1702m) / price, -0.0001m, 0.0001m);
    }

    /// <summary>
    /// V4.2 · El IVA de una venta es el del total (el débito fiscal de la factura en el libro de ventas) repartido entre sus
    /// líneas: redondear línea por línea daba 94,84 + 2,02 = 96,86 para 729,50 + 15,50, y el libro dice 96,85 (reparto: 94,84 +
    /// 2,01; el centavo va a la línea de mayor fracción descartada y, en empate, a la de mayor importe). El reparto suma
    /// siempre el IVA del total, ninguna línea queda negativa y, si el redondeo por línea ya cuadra, coincide con él.
    /// </summary>
    [Fact]
    public void El_IVA_de_la_venta_es_el_del_total_repartido_entre_las_lineas()
    {
        Assert.Equal([94.84m, 2.01m], MINV.Domain.Accounting.VatRules.Allocate([729.50m, 15.50m], 13m, onInvoicedAmount: true));
        Assert.Equal(FiscalRules.Vat(745m), MINV.Domain.Accounting.VatRules.Allocate([729.50m, 15.50m], 13m, onInvoicedAmount: true).Sum());
        Assert.Equal([0.01m, 0.01m, 0m, 0m], MINV.Domain.Accounting.VatRules.Allocate([0.04m, 0.04m, 0.04m, 0.04m], 13m, onInvoicedAmount: true));
        Assert.Equal([13m, 6.50m], MINV.Domain.Accounting.VatRules.Allocate([100m, 50m], 13m, onInvoicedAmount: true));
        Assert.Empty(MINV.Domain.Accounting.VatRules.Allocate([], 13m, onInvoicedAmount: true));
        Assert.Equal([0m, 0m], MINV.Domain.Accounting.VatRules.Allocate([100m, 50m], 0m, onInvoicedAmount: true));
        var random = new Random(7);
        foreach (var onAmount in new[] { true, false })
        {
            var rate = onAmount ? 13m : 19m;
            for (var i = 0; i < 5_000; i++)
            {
                var amounts = Enumerable.Range(0, random.Next(1, 8)).Select(_ => random.Next(1, 2_000_000) / 100m).ToArray();
                var taxes = MINV.Domain.Accounting.VatRules.Allocate(amounts, rate, onAmount);
                Assert.Equal(MINV.Domain.Accounting.VatRules.IncludedTax(amounts.Sum(), rate, onAmount), taxes.Sum());
                Assert.All(taxes, t => Assert.True(t >= 0 && t == decimal.Round(t, 2)));
                var perLine = amounts.Select(a => MINV.Domain.Accounting.VatRules.IncludedTax(a, rate, onAmount)).ToArray();
                if (perLine.Sum() == taxes.Sum())
                {
                    Assert.Equal(perLine, taxes);
                }
                else
                {
                    Assert.All(taxes.Zip(perLine), p => Assert.InRange(p.First - p.Second, -0.01m, 0.01m));
                }
            }
        }
    }
}
