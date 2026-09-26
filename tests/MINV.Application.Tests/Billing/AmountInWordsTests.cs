using System.Globalization;
using MINV.Application.Billing;

namespace MINV.Application.Tests.Billing;

/// <summary>Monto literal de la representación gráfica («Son: …»): español correcto, centavos /100 y formato oración.</summary>
public sealed class AmountInWordsTests
{
    [Theory]
    [InlineData("0", "Cero 00/100 Bolivianos")]
    [InlineData("1", "Uno 00/100 Bolivianos")]
    [InlineData("21", "Veintiuno 00/100 Bolivianos")]
    [InlineData("100", "Cien 00/100 Bolivianos")]
    [InlineData("101", "Ciento uno 00/100 Bolivianos")]
    [InlineData("1000", "Mil 00/100 Bolivianos")]
    [InlineData("1001", "Mil uno 00/100 Bolivianos")]
    [InlineData("21000", "Veintiún mil 00/100 Bolivianos")]
    [InlineData("1000000", "Un millón 00/100 Bolivianos")]
    [InlineData("2500000.50", "Dos millones quinientos mil 50/100 Bolivianos")]
    [InlineData("120.5", "Ciento veinte 50/100 Bolivianos")]
    [InlineData("99.99", "Noventa y nueve 99/100 Bolivianos")]
    public void Casos_de_la_especificacion(string amount, string expected)
    {
        Assert.Equal(expected, AmountInWords.Bolivianos(Parse(amount)));
    }

    [Theory]
    [InlineData("309.51", "Trescientos nueve 51/100 Bolivianos")]            // factura COMPRA VENTA.pdf
    [InlineData("3480.00", "Tres mil cuatrocientos ochenta 00/100 Bolivianos")] // factura TasaCero.pdf
    [InlineData("70.59", "Setenta 59/100 Bolivianos")]                      // Nota CreditoDebitoDescuento.pdf
    [InlineData("775.00", "Setecientos setenta y cinco 00/100 Bolivianos")] // Nota CreditoDebito.pdf (en formato oración)
    public void Literales_de_los_PDF_oficiales(string amount, string expected)
    {
        Assert.Equal(expected, AmountInWords.Bolivianos(Parse(amount)));
    }

    [Theory]
    [InlineData("0.5", "Cero 50/100 Bolivianos")]
    [InlineData("0.05", "Cero 05/100 Bolivianos")]
    [InlineData("11", "Once 00/100 Bolivianos")]
    [InlineData("15", "Quince 00/100 Bolivianos")]
    [InlineData("16", "Dieciséis 00/100 Bolivianos")]
    [InlineData("22", "Veintidós 00/100 Bolivianos")]
    [InlineData("26", "Veintiséis 00/100 Bolivianos")]
    [InlineData("30", "Treinta 00/100 Bolivianos")]
    [InlineData("31", "Treinta y uno 00/100 Bolivianos")]
    [InlineData("115", "Ciento quince 00/100 Bolivianos")]
    [InlineData("200", "Doscientos 00/100 Bolivianos")]
    [InlineData("500", "Quinientos 00/100 Bolivianos")]
    [InlineData("555", "Quinientos cincuenta y cinco 00/100 Bolivianos")]
    [InlineData("700", "Setecientos 00/100 Bolivianos")]
    [InlineData("900", "Novecientos 00/100 Bolivianos")]
    [InlineData("999", "Novecientos noventa y nueve 00/100 Bolivianos")]
    [InlineData("2000", "Dos mil 00/100 Bolivianos")]
    [InlineData("31000", "Treinta y un mil 00/100 Bolivianos")]
    [InlineData("100000", "Cien mil 00/100 Bolivianos")]
    [InlineData("101000", "Ciento un mil 00/100 Bolivianos")]
    [InlineData("201001", "Doscientos un mil uno 00/100 Bolivianos")]
    [InlineData("1000001", "Un millón uno 00/100 Bolivianos")]
    [InlineData("1001000", "Un millón mil 00/100 Bolivianos")]
    [InlineData("21000000", "Veintiún millones 00/100 Bolivianos")]
    [InlineData("1000000000", "Mil millones 00/100 Bolivianos")]
    [InlineData("21000000000", "Veintiún mil millones 00/100 Bolivianos")]
    [InlineData("999999999999.99", "Novecientos noventa y nueve mil novecientos noventa y nueve millones novecientos noventa y nueve mil novecientos noventa y nueve 99/100 Bolivianos")]
    public void Reglas_del_espanol(string amount, string expected)
    {
        Assert.Equal(expected, AmountInWords.Bolivianos(Parse(amount)));
    }

    [Theory]
    [InlineData("1.005", "Uno 01/100 Bolivianos")]      // HALF-UP antes de separar los centavos
    [InlineData("0.994", "Cero 99/100 Bolivianos")]
    [InlineData("99.995", "Cien 00/100 Bolivianos")]
    public void Redondea_a_2_decimales_antes_de_escribir(string amount, string expected)
    {
        Assert.Equal(expected, AmountInWords.Bolivianos(Parse(amount)));
    }

    [Fact]
    public void La_moneda_es_configurable_y_puede_omitirse()
    {
        Assert.Equal("Diez 00/100 Dólares", AmountInWords.Bolivianos(10m, "Dólares"));
        Assert.Equal("Diez 00/100", AmountInWords.Bolivianos(10m, string.Empty));
    }

    [Fact]
    public void No_depende_de_la_cultura_del_equipo()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");   // «i» con punto: la trampa clásica
            Assert.Equal("Ciento veinte 50/100 Bolivianos", AmountInWords.Bolivianos(120.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Montos_negativos_o_fuera_de_rango_se_rechazan()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AmountInWords.Bolivianos(-0.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() => AmountInWords.Bolivianos(AmountInWords.MaxAmount + 0.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() => AmountInWords.Words(-1));
    }

    [Fact]
    public void Palabras_con_y_sin_apocope()
    {
        Assert.Equal("veintiuno", AmountInWords.Words(21));
        Assert.Equal("veintiún", AmountInWords.Words(21, apocopate: true));
        Assert.Equal("uno", AmountInWords.Words(1));
        Assert.Equal("un", AmountInWords.Words(1, apocopate: true));
        Assert.Equal("cuarenta y un", AmountInWords.Words(41, apocopate: true));
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
