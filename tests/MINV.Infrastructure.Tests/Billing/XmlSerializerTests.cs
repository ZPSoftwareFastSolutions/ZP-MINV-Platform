using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Infrastructure.Billing;
using MINV.Infrastructure.Billing.Rendering;
using MINV.Infrastructure.Billing.Xml;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>
/// XML de la factura (sector 1) y de la nota crédito-débito (sector 24) contra los XSD oficiales del SIN (regla F-05,
/// investigación 05): orden exacto del XSD, todos los elementos, <c>xsi:nil</c>, UTF-8 sin BOM, números con punto; GZIP,
/// paquetes GZIP(TAR) y SHA-256.
/// </summary>
public sealed class XmlSerializerTests
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";

    private readonly SiatXmlSerializer _serializer = new();

    private static string XsdFolder => Path.Combine(V21MigrationTests.RepoRoot(), "src", "2. Infrastructure", "MINV.Infrastructure", "Billing", "Xsd");

    private static string OfficialXml(int sector) => File.ReadAllText(Path.Combine(XsdFolder,
        sector == SiatCodes.SectorPurchaseSale ? "facturaComputarizadaCompraVenta.xml" : "notaComputarizadaCreditoDebito.xml"));

    // ------------------------------------------------------------------------------------------------ XSD oficiales
    [Theory]
    [InlineData(SiatCodes.SectorPurchaseSale)]
    [InlineData(SiatCodes.SectorCreditDebitNote)]
    public void El_XML_oficial_del_SIN_valida_contra_su_XSD(int sector)
    {
        _serializer.Validate(OfficialXml(sector), sector);
        Assert.Empty(SiatSchemas.Validate(OfficialXml(sector), sector));
    }

    [Fact]
    public void El_XML_de_un_sector_no_valida_con_el_XSD_del_otro()
    {
        Assert.Equal("fiscal.xsd", Assert.Throws<DomainException>(() =>
            _serializer.Validate(OfficialXml(SiatCodes.SectorPurchaseSale), SiatCodes.SectorCreditDebitNote)).Code);
        Assert.Throws<NotSupportedException>(() => _serializer.Validate(OfficialXml(SiatCodes.SectorPurchaseSale), 8));
    }

    // ------------------------------------------------------------------------------------------------ factura (sector 1)
    [Fact]
    public void Regenera_el_XML_oficial_de_compra_venta_con_el_mismo_CUF_y_los_mismos_valores()
    {
        var doc = XmlSampleDocuments.OfficialInvoice();
        Assert.Equal(XmlSampleDocuments.OfficialInvoiceCuf, doc.Cuf);
        var xml = _serializer.BuildXml(doc, XmlSampleDocuments.Context());
        var ours = XDocument.Parse(xml);
        var official = XDocument.Parse(OfficialXml(SiatCodes.SectorPurchaseSale));
        AssertSameValues(official, ours, new Dictionary<string, (string? Official, string? Ours)>
        {
            // C-24: M-INV envía SIEMPRE el código numérico del punto de venta (el ejemplo usa nil; ambos pasan el XSD).
            ["codigoPuntoVenta"] = (null, "0"),
            // R4: por defecto 0 (el ejemplo usa nil).
            ["codigoExcepcion"] = (null, "0"),
            // R5: «Si no aplica deberá ser nulo» (el ejemplo envía 0).
            ["montoDescuento"] = ("0", null),
        });
        Assert.Equal("99.00", Value(ours, "montoTotal"));
        Assert.Equal("99.00", Value(ours, "montoTotalSujetoIva"));
        Assert.Equal("99.00", Value(ours, "montoTotalMoneda"));
        Assert.Equal("1.00", Value(ours, "descuentoAdicional"));
        Assert.Equal("100.00", Value(ours, "subTotal"));
        Assert.Equal("2021-10-06T16:03:48.675", Value(ours, "fechaEmision"));
    }

    [Fact]
    public void La_factura_sigue_el_orden_exacto_del_XSD_30_y_11_elementos()
    {
        var xml = XDocument.Parse(_serializer.BuildXml(XmlSampleDocuments.FullInvoice(), XmlSampleDocuments.Context()));
        var (header, detail) = XsdOrder("facturaComputarizadaCompraVenta.xsd");
        Assert.Equal(30, header.Count);
        Assert.Equal(11, detail.Count);
        Assert.Equal("facturaComputarizadaCompraVenta", xml.Root!.Name.LocalName);
        Assert.Equal(header, xml.Root.Element("cabecera")!.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(3, xml.Root.Elements("detalle").Count());
        Assert.All(xml.Root.Elements("detalle"), d => Assert.Equal(detail, d.Elements().Select(e => e.Name.LocalName)));
        Assert.Equal(["cabecera", "detalle", "detalle", "detalle"], xml.Root.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void Declaracion_raiz_sin_namespace_y_ubicacion_del_esquema()
    {
        var xml = _serializer.BuildXml(XmlSampleDocuments.MinimalInvoice(), XmlSampleDocuments.Context());
        Assert.StartsWith("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" + "\n<facturaComputarizadaCompraVenta ", xml);
        Assert.NotEqual('﻿', xml[0]);
        var root = XDocument.Parse(xml).Root!;
        Assert.Equal(XNamespace.None, root.Name.Namespace);
        Assert.Equal("facturaComputarizadaCompraVenta.xsd", root.Attribute(Xsi + "noNamespaceSchemaLocation")!.Value);
        Assert.All(root.Descendants(), e => Assert.Equal(XNamespace.None, e.Name.Namespace));
        Assert.DoesNotContain("\r", xml);
    }

    [Fact]
    public void Los_vacios_van_con_xsi_nil_y_nunca_se_omiten_ni_quedan_vacios()
    {
        var xml = XDocument.Parse(_serializer.BuildXml(XmlSampleDocuments.MinimalInvoice(), XmlSampleDocuments.Context(phone: null)));
        string[] nil =
        [
            "telefono", "nombreRazonSocial", "complemento", "numeroTarjeta", "montoGiftCard", "descuentoAdicional", "cafc", "montoDescuento",
            "numeroSerie", "numeroImei",
        ];
        foreach (var name in nil)
        {
            var element = Assert.Single(xml.Descendants(name));
            Assert.Equal("true", element.Attribute(Xsi + "nil")?.Value);
            Assert.True(element.IsEmpty, name);
        }
        var nils = xml.Descendants().Where(e => e.Attribute(Xsi + "nil") is not null).Select(e => e.Name.LocalName).Order();
        Assert.Equal(nil.Order(), nils);
        Assert.All(xml.Descendants().Where(e => !e.HasElements && e.Attribute(Xsi + "nil") is null),
            e => Assert.False(string.IsNullOrWhiteSpace(e.Value), e.Name.LocalName));
        Assert.Equal("0", Value(xml, "codigoPuntoVenta"));
        Assert.Equal("0", Value(xml, "codigoExcepcion"));
    }

    [Fact]
    public void Con_todos_los_datos_no_queda_ningun_nil_salvo_el_cafc()
    {
        var doc = XmlSampleDocuments.FullInvoice();
        var text = _serializer.BuildXml(doc, XmlSampleDocuments.Context(pointOfSaleCode: 3));
        var xml = XDocument.Parse(text);
        Assert.Equal(["cafc"], xml.Descendants().Where(e => e.Attribute(Xsi + "nil") is not null).Select(e => e.Name.LocalName));
        Assert.Equal("4797000000007896", Value(xml, "numeroTarjeta"));
        Assert.Equal("2", Value(xml, "codigoMetodoPago"));
        Assert.Equal("1A", Value(xml, "complemento"));
        Assert.Equal("3", Value(xml, "codigoPuntoVenta"));
        Assert.Equal("9999999999", Value(xml, "numeroFactura"));
        Assert.Equal("7500.03", Sum(xml, "subTotal").ToString("0.00", CultureInfo.InvariantCulture));
        Assert.Equal("7490.03", Value(xml, "montoTotal"));
        Assert.Equal("7450.03", Value(xml, "montoTotalSujetoIva"));
        Assert.Equal("7490.03", Value(xml, "montoTotalMoneda"));
        Assert.Equal("40.00", Value(xml, "montoGiftCard"));
        Assert.Equal("10.00", Value(xml, "descuentoAdicional"));
        Assert.Equal("1.00", Value(xml, "tipoCambio"));
        Assert.Equal("0.99", xml.Descendants("montoDescuento").First().Value);
        Assert.Equal("2500.01", xml.Descendants("subTotal").First().Value);
        Assert.Equal("Ferretería Ñandú & Cía. <Sucursal «Norte»>", Value(xml, "nombreRazonSocial"));
        Assert.Contains("&amp; Cía. &lt;Sucursal «Norte»&gt;", text);   // escapado, con los acentos en UTF-8
        Assert.Equal(doc.Cuf, Value(xml, "cuf"));
    }

    [Fact]
    public void Los_numeros_usan_punto_aunque_la_cultura_del_equipo_use_coma()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-BO");
            var xml = _serializer.BuildXml(XmlSampleDocuments.FullInvoice(), XmlSampleDocuments.Context());
            Assert.Contains("<montoTotal>7490.03</montoTotal>", xml);
            Assert.Contains("<precioUnitario>1250.50</precioUnitario>", xml);
            Assert.DoesNotContain("7490,03", xml);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void La_leyenda_va_en_una_sola_linea_sin_espacios_agregados()
    {
        var doc = FiscalDocument.IssueInvoice(Guid.NewGuid(), XmlSampleDocuments.Emission(new DateTime(2026, 9, 25, 16, 0, 0),
                legend: "  Ley N° 453: Tienes derecho a recibir información\n            sobre los servicios que utilices.\n  "),
            FiscalBuyer.Create(null, "C-1", SiatCodes.DocumentCi, "1234567", null, null, null), null,
            [new FiscalLineInput(null, "477300", 62151, "X-1", "Producto", 1m, 57, 10m, null)], 1, null, 0m, 0m, false, XmlSampleDocuments.Now);
        var xml = XDocument.Parse(_serializer.BuildXml(doc, XmlSampleDocuments.Context()));
        Assert.Equal("Ley N° 453: Tienes derecho a recibir información sobre los servicios que utilices.", Value(xml, "leyenda"));
    }

    [Fact]
    public void Una_factura_fuera_de_linea_con_CAFC_lleva_el_codigo_y_la_excepcion_con_NIT()
    {
        var doc = FiscalDocument.IssueInvoice(Guid.NewGuid(),
            XmlSampleDocuments.Emission(new DateTime(2026, 9, 25, 16, 0, 0), emissionType: SiatCodes.EmissionOffline) with { Cafc = "1011917833B0D" },
            FiscalBuyer.Create(null, "C-1", SiatCodes.DocumentNit, "1020703023", null, "Constructora Andina S.A.", null), null,
            [new FiscalLineInput(null, "477300", 62151, "X-1", "Producto", 1m, 57, 10m, null)], 1, null, 0m, 0m, false, XmlSampleDocuments.Now);
        var xml = XDocument.Parse(_serializer.BuildXml(doc, XmlSampleDocuments.Context()));
        Assert.Equal("1011917833B0D", Value(xml, "cafc"));
        Assert.Equal("1", Value(xml, "codigoExcepcion"));
        Assert.Equal("5", Value(xml, "codigoTipoDocumentoIdentidad"));
        var parts = Cuf.DecodeFull(Value(xml, "cuf"), XmlSampleDocuments.ControlCode);
        Assert.Equal(SiatCodes.EmissionOffline, parts!.EmissionType);
    }

    // ------------------------------------------------------------------------------------------------ nota (sector 24)
    [Fact]
    public void Regenera_el_XML_oficial_de_la_nota_credito_debito()
    {
        var doc = XmlSampleDocuments.OfficialNote();
        Assert.Equal(XmlSampleDocuments.OfficialNoteCuf, doc.Cuf);
        var xml = _serializer.BuildXml(doc, XmlSampleDocuments.Context(businessName: "ENTEL", phone: "2846005"));
        var ours = XDocument.Parse(xml);
        AssertSameValues(XDocument.Parse(OfficialXml(SiatCodes.SectorCreditDebitNote)), ours, new Dictionary<string, (string? Official, string? Ours)>
        {
            ["codigoExcepcion"] = (null, "0"),
            // El ejemplo trae una fecha ficticia (año 3919): el dominio exige una factura anterior y dentro de 18 meses.
            ["fechaEmisionFactura"] = ("3919-02-01T10:14:36.000", "2021-09-01T10:14:36.000"),
        });
        Assert.Equal("775.00", Value(ours, "montoTotalOriginal"));
        Assert.Equal("75.00", Value(ours, "montoTotalDevuelto"));
        Assert.Equal("9.75", Value(ours, "montoEfectivoCreditoDebito"));
        Assert.Equal(["1", "2"], ours.Descendants("codigoDetalleTransaccion").Select(e => e.Value));
    }

    [Fact]
    public void La_nota_sigue_el_orden_exacto_del_XSD_27_y_10_elementos_con_10_decimales_en_el_detalle()
    {
        var text = _serializer.BuildXml(XmlSampleDocuments.DecimalNote(), XmlSampleDocuments.Context());
        var xml = XDocument.Parse(text);
        var (header, detail) = XsdOrder("notaComputarizadaCreditoDebito.xsd");
        Assert.Equal(27, header.Count);
        Assert.Equal(10, detail.Count);
        Assert.Equal("notaFiscalComputarizadaCreditoDebito", xml.Root!.Name.LocalName);
        Assert.Equal("notaComputarizadaCreditoDebito.xsd", xml.Root.Attribute(Xsi + "noNamespaceSchemaLocation")!.Value);
        Assert.Equal(header, xml.Root.Element("cabecera")!.Elements().Select(e => e.Name.LocalName));
        Assert.All(xml.Root.Elements("detalle"), d => Assert.Equal(detail, d.Elements().Select(e => e.Name.LocalName)));
        var returned = xml.Root.Elements("detalle").Last();
        Assert.Equal("12.3456789012", returned.Element("cantidad")!.Value);
        Assert.Equal("5.50", returned.Element("precioUnitario")!.Value);
        Assert.Equal("67.90", returned.Element("subTotal")!.Value);
        Assert.Equal("true", returned.Element("montoDescuento")!.Attribute(Xsi + "nil")?.Value);
        Assert.Equal("25.00", xml.Root.Elements("detalle").First().Element("montoDescuento")!.Value);
        Assert.Equal("800.00", Value(xml, "montoTotalOriginal"));
        Assert.Equal("4.41", Value(xml, "montoDescuentoCreditoDebito"));
        Assert.Equal("63.49", Value(xml, "montoTotalDevuelto"));
        Assert.Equal("8.25", Value(xml, "montoEfectivoCreditoDebito"));
        Assert.Equal("2026-09-01T09:30:00.250", Value(xml, "fechaEmisionFactura"));
        Assert.Equal(XmlSampleDocuments.OfficialInvoiceCuf, Value(xml, "numeroAutorizacionCuf"));
        Assert.DoesNotContain("<cafc", text);
        Assert.DoesNotContain("<montoTotal>", text);
    }

    // ------------------------------------------------------------------------------------------------ validación
    [Fact]
    public void Un_XML_roto_lanza_fiscal_xsd_con_los_primeros_errores()
    {
        var official = OfficialXml(SiatCodes.SectorPurchaseSale);
        var reordered = XDocument.Parse(official);
        var giftCard = reordered.Descendants("montoGiftCard").Single();
        giftCard.Remove();
        reordered.Descendants("montoTotal").Single().AddBeforeSelf(giftCard);
        var withoutPhone = official.Replace("<telefono>78595684</telefono>", string.Empty, StringComparison.Ordinal);
        var emptyPhone = official.Replace("<telefono>78595684</telefono>", "<telefono></telefono>", StringComparison.Ordinal);
        var comma = official.Replace("<montoTotal>99</montoTotal>", "<montoTotal>99,50</montoTotal>", StringComparison.Ordinal);
        var threeDecimals = official.Replace("<montoTotal>99</montoTotal>", "<montoTotal>99.123</montoTotal>", StringComparison.Ordinal);
        var withNamespace = official.Replace("<facturaComputarizadaCompraVenta ", "<facturaComputarizadaCompraVenta xmlns=\"http://x\" ",
            StringComparison.Ordinal);
        foreach (var broken in new[]
                 {
                     reordered.ToString(), withoutPhone, emptyPhone, comma, threeDecimals, withNamespace, "<facturaComputarizadaCompraVenta/>",
                     "<facturaComputarizadaCompraVenta><cabecera>", string.Empty,
                 })
        {
            var ex = Assert.Throws<DomainException>(() => _serializer.Validate(broken, SiatCodes.SectorPurchaseSale));
            Assert.Equal("fiscal.xsd", ex.Code);
            Assert.StartsWith("El XML no cumple el esquema del SIN: ", ex.Message);
        }
    }

    [Fact]
    public void BuildXml_no_devuelve_un_XML_que_no_valida()
    {
        // municipio admite hasta 25 caracteres en el XSD: el serializador no recorta en silencio, rechaza.
        var ex = Assert.Throws<DomainException>(() => _serializer.BuildXml(XmlSampleDocuments.MinimalInvoice(),
            XmlSampleDocuments.Context(municipality: "Municipio con un nombre demasiado largo")));
        Assert.Equal("fiscal.xsd", ex.Code);
        Assert.Contains("municipio", ex.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ GZIP, TAR y SHA-256
    [Fact]
    public void Gzip_ida_y_vuelta_UTF8_sin_BOM_y_determinista()
    {
        var xml = _serializer.BuildXml(XmlSampleDocuments.FullInvoice(), XmlSampleDocuments.Context());
        var gz = _serializer.Gzip(xml);
        Assert.Equal(new byte[] { 0x1F, 0x8B, 0x08 }, gz[..3]);   // RFC 1952: GZIP con deflate
        var raw = Gunzip(gz);
        Assert.Equal(Encoding.UTF8.GetBytes(xml), raw);
        Assert.Equal((byte)'<', raw[0]);                         // sin BOM (EF BB BF)
        Assert.Equal(xml, Encoding.UTF8.GetString(raw));
        Assert.Equal(gz, _serializer.Gzip(xml));
        Assert.Equal(_serializer.Sha256Hex(gz), _serializer.Sha256Hex(_serializer.Gzip(xml)));
    }

    [Fact]
    public void Paquete_GZIP_de_un_TAR_con_los_N_archivos_en_orden()
    {
        var xmls = Enumerable.Range(1, 5)
            .Select(i => _serializer.BuildXml(FiscalDocument.IssueInvoice(Guid.NewGuid(),
                XmlSampleDocuments.Emission(new DateTime(2026, 9, 25, 16, 0, i), number: i, emissionType: SiatCodes.EmissionOffline),
                FiscalBuyer.Create(null, "C-1", SiatCodes.DocumentCi, "1234567", null, "Señora Núñez", null), null,
                [new FiscalLineInput(null, "477300", 62151, $"X-{i}", "Producto", i, 57, 10m, null)], 1, null, 0m, 0m, false,
                XmlSampleDocuments.Now), XmlSampleDocuments.Context()))
            .ToList();
        var package = _serializer.Package(xmls.Select(x => (string.Empty, x)).ToList());
        Assert.Equal(new byte[] { 0x1F, 0x8B }, package[..2]);
        var entries = Untar(Gunzip(package));
        Assert.Equal(["1.xml", "2.xml", "3.xml", "4.xml", "5.xml"], entries.Select(e => e.Name));
        Assert.Equal(xmls, entries.Select(e => Encoding.UTF8.GetString(e.Data)));
        Assert.All(entries, e => Assert.Equal((byte)'<', e.Data[0]));
        Assert.Equal(package, _serializer.Package(xmls.Select(x => (string.Empty, x)).ToList()));   // determinista

        var named = Untar(Gunzip(_serializer.Package([("factura-000123.xml", xmls[0]), ("factura-000124.xml", xmls[1])])));
        Assert.Equal(["factura-000123.xml", "factura-000124.xml"], named.Select(e => e.Name));
    }

    [Fact]
    public void El_paquete_valida_cantidad_y_nombres()
    {
        Assert.Equal("fiscal.package_size", Assert.Throws<DomainException>(() => _serializer.Package([])).Code);
        var tooMany = Enumerable.Range(0, SiatCodes.MaxDocumentsPerPackage + 1).Select(_ => (string.Empty, "<a/>")).ToList();
        Assert.Equal("fiscal.package_size", Assert.Throws<DomainException>(() => _serializer.Package(tooMany)).Code);
        Assert.Equal("fiscal.package_name", Assert.Throws<DomainException>(() => _serializer.Package([("a.xml", "<a/>"), ("A.xml", "<a/>")])).Code);
        Assert.Equal("fiscal.package_name", Assert.Throws<DomainException>(() => _serializer.Package([("dir/a.xml", "<a/>")])).Code);
        Assert.Equal("fiscal.package_name", Assert.Throws<DomainException>(() => _serializer.Package([("año.xml", "<a/>")])).Code);
        var max = Enumerable.Range(0, SiatCodes.MaxDocumentsPerPackage).Select(_ => (string.Empty, "<a/>")).ToList();
        Assert.Equal(SiatCodes.MaxDocumentsPerPackage, Untar(Gunzip(_serializer.Package(max))).Count);
    }

    [Theory]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void Sha256_en_hexadecimal_minuscula_de_64_caracteres(string text, string expected)
    {
        var hash = _serializer.Sha256Hex(Encoding.ASCII.GetBytes(text));
        Assert.Equal(expected, hash);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
    }

    [Fact]
    public void Se_registran_el_serializador_y_el_PDF()
    {
        using var provider = new ServiceCollection().AddMinvFiscalDocuments().AddMinvFiscalDocuments().BuildServiceProvider();
        Assert.IsType<SiatXmlSerializer>(provider.GetRequiredService<IFiscalDocumentSerializer>());
        Assert.IsType<FiscalPdfRenderer>(provider.GetRequiredService<IFiscalDocumentRenderer>());
        Assert.Single(new ServiceCollection().AddMinvFiscalDocuments().AddMinvFiscalDocuments(), d => d.ServiceType == typeof(IFiscalDocumentSerializer));
    }

    // ------------------------------------------------------------------------------------------------ utilidades
    /// <summary>Orden de los elementos de cabecera y detalle leído del XSD oficial embebido.</summary>
    private static (List<string> Header, List<string> Detail) XsdOrder(string xsdFile)
    {
        var xsd = XDocument.Load(Path.Combine(XsdFolder, xsdFile));
        List<string> Children(string name) => xsd.Descendants(Xs + "element").Single(e => (string?)e.Attribute("name") == name)
            .Element(Xs + "complexType")!.Element(Xs + "sequence")!.Elements(Xs + "element").Select(e => (string)e.Attribute("name")!).ToList();
        return (Children("cabecera"), Children("detalle"));
    }

    private static string Value(XDocument xml, string name) => xml.Descendants(name).First().Value;

    private static decimal Sum(XDocument xml, string name) =>
        xml.Descendants(name).Sum(e => decimal.Parse(e.Value, CultureInfo.InvariantCulture));

    /// <summary>Compara elemento por elemento (mismo orden, mismos nombres y mismos valores: números por valor, textos sin
    /// los espacios de indentación del ejemplo). Las diferencias esperadas se declaran (null = <c>xsi:nil</c>).</summary>
    private static void AssertSameValues(XDocument official, XDocument ours, IReadOnlyDictionary<string, (string? Official, string? Ours)> expectedDifferences)
    {
        var a = Leaves(official);
        var b = Leaves(ours);
        Assert.Equal(a.Select(x => x.Path), b.Select(x => x.Path));
        for (var i = 0; i < a.Count; i++)
        {
            var name = a[i].Path[(a[i].Path.LastIndexOf('/') + 1)..];
            if (expectedDifferences.TryGetValue(name, out var diff))
            {
                Assert.Equal(diff.Official, a[i].Value);
                Assert.Equal(diff.Ours, b[i].Value);
                continue;
            }
            if (a[i].Value is { } x && b[i].Value is { } y && decimal.TryParse(x, NumberStyles.Number, CultureInfo.InvariantCulture, out var dx)
                && decimal.TryParse(y, NumberStyles.Number, CultureInfo.InvariantCulture, out var dy))
            {
                Assert.True(dx == dy, $"{a[i].Path}: {x} ≠ {y}");
                continue;
            }
            Assert.True(a[i].Value == b[i].Value, $"{a[i].Path}: «{a[i].Value}» ≠ «{b[i].Value}»");
        }
    }

    private static List<(string Path, string? Value)> Leaves(XDocument xml)
    {
        var leaves = new List<(string Path, string? Value)>();
        var index = 0;
        foreach (var block in xml.Root!.Elements())
        {
            foreach (var e in block.Elements())
            {
                var value = e.Attribute(Xsi + "nil")?.Value == "true"
                    ? null
                    : string.Join(' ', e.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                leaves.Add(($"{block.Name.LocalName}[{index}]/{e.Name.LocalName}", value));
            }
            index++;
        }
        return leaves;
    }

    private static byte[] Gunzip(byte[] data)
    {
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static List<(string Name, byte[] Data)> Untar(byte[] tar)
    {
        var entries = new List<(string, byte[])>();
        using var reader = new TarReader(new MemoryStream(tar));
        while (reader.GetNextEntry() is { } entry)
        {
            Assert.Equal(TarEntryType.RegularFile, entry.EntryType);
            Assert.Equal(TarEntryFormat.Ustar, entry.Format);
            using var data = new MemoryStream();
            entry.DataStream?.CopyTo(data);
            entries.Add((entry.Name, data.ToArray()));
        }
        return entries;
    }
}
