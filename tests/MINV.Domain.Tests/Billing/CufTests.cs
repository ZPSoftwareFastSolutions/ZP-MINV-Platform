using System.Globalization;
using System.Numerics;
using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Domain.Tests.Billing;

/// <summary>
/// CUF con los vectores de la investigación 06 §1.3–1.4 y 00 §2.4: el ejemplo oficial del SIN (modalidad 1), el mismo con
/// modalidad 2 y los CUF de la URL del QR, del XML computarizado, de la nota y de los PDF de ejemplo (sucursal 5 y tasa
/// cero), todos verificados carácter por carácter.
/// </summary>
public sealed class CufTests
{
    /// <summary>Partes «NIT|fecha|sucursal|modalidad|emisión|tipo|sector|número|PV», código de control y CUF esperado.</summary>
    public static TheoryData<string, string, string> Vectors => new()
    {
        // Ejemplo oficial de la página «Generación del CUF».
        { "123456789|20190113163721231|0|1|1|1|1|1|0", "A19E23EF34124CD", "8727F63A15F8976591FDDE5B387C5D015A29E06A1A19E23EF34124CD" },
        // El mismo con modalidad 2 (computarizada en línea, la de M-INV).
        { "123456789|20190113163721231|0|2|1|1|1|1|0", "A19E23EF34124CD", "8727F63A15F8976591FDDE5B4128CF31A2C8606A3A19E23EF34124CD" },
        // URL de ejemplo del QR (número 137).
        { "1003579028|20200824162245482|0|2|1|1|1|137|0", "67A75AC82F24C74", "44AAEC00DBCBA35880091FEE05E92DC65368558BA467A75AC82F24C74" },
        // XML oficial facturaComputarizadaCompraVenta.xml.
        { "1003579028|20211006160348675|0|2|1|1|1|1|0", "67A75AC82F24C74", "44AAEC00DBD34C53C3E2CCE1A3FA7AF1E2A08606A667A75AC82F24C74" },
        // PDF «Nota CreditoDebito.pdf» (tipo 3, sector 24).
        { "1003579028|20211006160349570|0|2|1|3|24|1|0", "67A75AC82F24C74", "44AAEC00DBD34C53C3E5B135433591A5FA086F86A867A75AC82F24C74" },
        // PDF «factura COMPRA VENTA.pdf» (sucursal 5, número 2377).
        { "142591020|20220506091942957|5|1|1|1|1|2377|0", "7A84B0CA3176D74", "9C1A83F996B702B8497F0555BFF4C27CB7E1783A67A84B0CA3176D74" },
        // PDF «factura TasaCero.pdf» (modalidad 3, tipo 2, sector 8).
        { "374803027|20220330152721578|0|3|1|2|8|24|0", "159318EB7846D74", "19A522E68ED20E82F3FADFE11D8545827010889F04159318EB7846D74" },
    };

    private static DateTime At(string stamp) => DateTime.ParseExact(stamp, "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

    private static Cuf.Parts Parse(string spec)
    {
        var p = spec.Split('|');
        int I(int i) => int.Parse(p[i], CultureInfo.InvariantCulture);
        return new Cuf.Parts(long.Parse(p[0], CultureInfo.InvariantCulture), At(p[1]), I(2), I(3), I(4), I(5), I(6),
            long.Parse(p[7], CultureInfo.InvariantCulture), I(8));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Genera_el_CUF_de_los_vectores_oficiales(string spec, string control, string expected)
    {
        Assert.Equal(expected, Cuf.Generate(Parse(spec), control));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Decodifica_ida_y_vuelta_con_y_sin_codigo_de_control(string spec, string control, string cuf)
    {
        var expected = Parse(spec);
        Assert.Equal(expected, Cuf.Decode(cuf[..^control.Length]));
        Assert.Equal(expected, Cuf.DecodeFull(cuf, control));
        Assert.Equal(expected, Cuf.DecodeFull(cuf));
    }

    [Fact]
    public void Cadena_de_53_digitos_y_modulo_11_del_ejemplo_oficial()
    {
        var parts = new Cuf.Parts(123456789, At("20190113163721231"), 0, 1, 1, 1, 1, 1, 0);
        var digits = Cuf.Digits(parts);
        Assert.Equal("00001234567892019011316372123100001110100000000010000", digits);
        Assert.Equal(53, digits.Length);
        Assert.Equal("1", Cuf.Mod11(digits));   // suma 472, resto 10 → «1»
        Assert.Equal("3", Cuf.Mod11(Cuf.Digits(parts with { Modality = 2 })));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1", "2")]
    [InlineData("12", "7")]
    [InlineData("19", "1")]
    public void Modulo_11_con_pesos_2_a_9_de_derecha_a_izquierda(string digits, string expected)
    {
        Assert.Equal(expected, Cuf.Mod11(digits));
    }

    [Fact]
    public void Modulo_11_rechaza_lo_que_no_son_digitos()
    {
        Assert.Equal("cuf.mod11", Assert.Throws<DomainException>(() => Cuf.Mod11("12A")).Code);
    }

    [Fact]
    public void Base16_sin_el_cero_inicial_de_NET()
    {
        const string digits = "000012345678920190113163721231000011101000000000100001";
        Assert.Equal("8727F63A15F8976591FDDE5B387C5D015A29E06A1", Cuf.Base16(digits));
        // La trampa documentada: BigInteger.ToString("X") antepone un 0 cuando el primer nibble es ≥ 8.
        Assert.StartsWith("0", BigInteger.Parse(digits, CultureInfo.InvariantCulture).ToString("X", CultureInfo.InvariantCulture));
        Assert.Equal("FF", Cuf.Base16("255"));
        Assert.Equal("0", Cuf.Base16("0"));
    }

    [Fact]
    public void Un_CUF_alterado_no_decodifica()
    {
        const string hex = "8727F63A15F8976591FDDE5B387C5D015A29E06A1";
        Assert.NotNull(Cuf.Decode(hex));
        Assert.Null(Cuf.Decode(hex[..^1] + "2"));                     // +1: cambia solo el dígito verificador
        Assert.Null(Cuf.Decode("8727F63A15F8976591FDDE5B387C5D015A29E06A0"));
        Assert.Null(Cuf.Decode("ZZZ"));
        Assert.Null(Cuf.Decode(string.Empty));
        Assert.Null(Cuf.DecodeFull("8727F63A15F8976591FDDE5B387C5D015A29E06A2A19E23EF34124CD", "A19E23EF34124CD"));
        Assert.Null(Cuf.DecodeFull(" "));
    }

    [Fact]
    public void Los_CUF_de_los_XML_oficiales_decodifican_con_la_fecha_de_emision_del_XML()
    {
        var invoice = Cuf.DecodeFull("44AAEC00DBD34C53C3E2CCE1A3FA7AF1E2A08606A667A75AC82F24C74");
        Assert.NotNull(invoice);
        Assert.Equal(1003579028, invoice.Nit);
        Assert.Equal(new DateTime(2021, 10, 6, 16, 3, 48, 675), invoice.IssuedAt);   // <fechaEmision>2021-10-06T16:03:48.675
        Assert.Equal((SiatCodes.ModalityComputerized, SiatCodes.EmissionOnline, SiatCodes.InvoiceWithTaxCredit, SiatCodes.SectorPurchaseSale),
            (invoice.Modality, invoice.EmissionType, invoice.DocumentType, invoice.DocumentSector));

        var note = Cuf.DecodeFull("44AAEC00DBD34C53C3E5B135433591A5FA086F86A867A75AC82F24C74");
        Assert.NotNull(note);
        Assert.Equal(new DateTime(2021, 10, 6, 16, 3, 49, 570), note.IssuedAt);      // <fechaEmision>2021-10-06T16:03:49.570
        Assert.Equal((SiatCodes.AdjustmentDocument, SiatCodes.SectorCreditDebitNote, 1L), (note.DocumentType, note.DocumentSector, note.Number));
    }

    [Fact]
    public void Valida_las_longitudes_de_cada_campo()
    {
        var ok = new Cuf.Parts(123456789, At("20190113163721231"), 0, 2, 1, 1, 1, 1, 0);
        Assert.Equal("cuf.nit", Assert.Throws<DomainException>(() => Cuf.Digits(ok with { Nit = 0 })).Code);
        Assert.Equal("cuf.nit", Assert.Throws<DomainException>(() => Cuf.Digits(ok with { Nit = 10_000_000_000_000 })).Code);
        Assert.Equal("cuf.branch", Assert.Throws<DomainException>(() => Cuf.Digits(ok with { BranchCode = 10_000 })).Code);
        Assert.Equal("cuf.pos", Assert.Throws<DomainException>(() => Cuf.Digits(ok with { PointOfSaleCode = -1 })).Code);
        Assert.Equal("cuf.number", Assert.Throws<DomainException>(() => Cuf.Digits(ok with { Number = 10_000_000_000 })).Code);
        Assert.Equal("cuf.codes", Assert.Throws<DomainException>(() => Cuf.Digits(ok with { Modality = 10 })).Code);
        Assert.Equal("cuf.sector", Assert.Throws<DomainException>(() => Cuf.Digits(ok with { DocumentSector = 100 })).Code);
        Assert.Equal("cuf.control", Assert.Throws<DomainException>(() => Cuf.Generate(ok, " ")).Code);
    }
}
