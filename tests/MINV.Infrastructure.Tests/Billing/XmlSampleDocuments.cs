using MINV.Application.Abstractions;
using MINV.Domain.Billing;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>
/// Documentos fiscales de prueba. Los «oficiales» reproducen con el dominio de M-INV los XML de ejemplo del SIN
/// (<c>facturaComputarizadaCompraVenta.xml</c> y <c>notaComputarizadaCreditoDebito.xml</c>): mismo NIT, fecha con
/// milisegundos, número, código de control y montos, así que el CUF que genera el dominio es el MISMO del ejemplo.
/// </summary>
internal static class XmlSampleDocuments
{
    public const long Nit = 1003579028;
    public const string ControlCode = "67A75AC82F24C74";
    public const string Cufd = "BQUE+QytqQUDBKVUFOSVRPQkxVRFZNVFVJBMDAwMDAwM";
    public const string OfficialInvoiceCuf = "44AAEC00DBD34C53C3E2CCE1A3FA7AF1E2A08606A667A75AC82F24C74";
    public const string OfficialNoteCuf = "44AAEC00DBD34C53C3E5B135433591A5FA086F86A867A75AC82F24C74";

    public const string InvoiceLegend =
        "Ley N° 453: Tienes derecho a recibir información sobre las características y contenidos de los servicios que utilices.";

    public const string NoteLegend = "Ley N° 453: Los servicios deben suministrarse en condiciones de inocuidad, calidad y seguridad.";

    public static readonly DateTimeOffset Now = new(2026, 9, 25, 20, 0, 0, TimeSpan.Zero);

    public static FiscalXmlContext Context(string businessName = "Carlos Loza", string? phone = "78595684", int pointOfSaleCode = 0,
        string municipality = "La Paz") =>
        new(Nit, businessName, municipality, phone, 0, pointOfSaleCode, Cufd, "AV. JORGE LOPEZ #123");

    public static FiscalEmission Emission(DateTime issuedAt, long number = 1, string legend = InvoiceLegend, string user = "pperez",
        int emissionType = SiatCodes.EmissionOnline, int pointOfSaleCode = 0) =>
        new(Guid.NewGuid(), SiatCodes.EnvironmentTest, Nit, Guid.NewGuid(), 0, pointOfSaleCode, Guid.NewGuid(), Guid.NewGuid(), ControlCode,
            emissionType, issuedAt, number, legend, user, emissionType == SiatCodes.EmissionOffline ? Guid.NewGuid() : null);

    /// <summary>La factura del XML oficial: 1 × 100 con descuento adicional 1 → montoTotal 99.</summary>
    public static FiscalDocument OfficialInvoice() =>
        FiscalDocument.IssueInvoice(Guid.NewGuid(), Emission(new DateTime(2021, 10, 6, 16, 3, 48, 675)),
            FiscalBuyer.Create(null, "51158891", SiatCodes.DocumentCi, "5115889", null, "Mi razon social", null), null,
            [new FiscalLineInput(null, "451010", 49111, "JN-131231", "JUGO DE NARANJA EN VASO", 1m, 1, 100m, 0m, SerialNumber: "124548", Imei: "545454")],
            paymentMethodCode: 1, cardNumber: null, additionalDiscount: 1m, giftCardAmount: 0m, exceptionRequested: false, Now);

    /// <summary>La nota del XML oficial: original 775.00 (transacción 1), devuelto 75.00 (transacción 2) → efectivo 9.75.</summary>
    public static FiscalDocument OfficialNote() =>
        FiscalDocument.IssueCreditNote(Guid.NewGuid(), Emission(new DateTime(2021, 10, 6, 16, 3, 49, 570), legend: NoteLegend, user: "vjcm"),
            FiscalBuyer.Create(null, "51158891", SiatCodes.DocumentCi, "5115889", null, "Juan Valdez", null), null,
            new FiscalOriginalInvoice(null, 1, "dsa564dsa54d6as5", new DateTime(2021, 9, 1, 10, 14, 36), null),
            [
                new FiscalLineInput(null, "451010", 49111, "123456", "Amortiguadores", 1m, 1, 775m, null, 1),
                new FiscalLineInput(null, "451010", 49111, "123456", "Tornillos", 1m, 1, 75m, null, 2),
            ],
            Now);

    /// <summary>Factura mínima: sin teléfono, nombre, complemento, tarjeta, gift card, descuentos, serie ni IMEI.</summary>
    public static FiscalDocument MinimalInvoice(int documentType = SiatCodes.DocumentCi, string documentNumber = "5115889") =>
        FiscalDocument.IssueInvoice(Guid.NewGuid(), Emission(new DateTime(2026, 9, 25, 16, 0, 0, 5), number: 42),
            FiscalBuyer.Create(null, "C-0001", documentType, documentNumber, null, null, null), null,
            [new FiscalLineInput(null, "477300", 62151, "FER-001", "Tornillo drywall 6x1\"", 12m, 57, 0.35m, null)],
            paymentMethodCode: 1, cardNumber: null, additionalDiscount: 0m, giftCardAmount: 0m, exceptionRequested: false, Now);

    /// <summary>Factura completa: tarjeta, gift card, descuento adicional y por línea, complemento, serie e IMEI, varias líneas.</summary>
    public static FiscalDocument FullInvoice(int lines = 3) =>
        FiscalDocument.IssueInvoice(Guid.NewGuid(), Emission(new DateTime(2026, 9, 25, 16, 0, 0, 999), number: 9_999_999_999, pointOfSaleCode: 3),
            FiscalBuyer.Create(Guid.NewGuid(), "C-0002", SiatCodes.DocumentCi, "5115889", "1A", "Ferretería Ñandú & Cía. <Sucursal «Norte»>",
                "compras@example.com"), Guid.NewGuid(),
            Enumerable.Range(1, lines).Select(i => new FiscalLineInput(Guid.NewGuid(), "477300", 62151, $"CEL-{i:000}",
                $"Celular línea negra modelo {i}", 2m, 57, 1250.50m, 0.99m, SerialNumber: $"SN-{i}", Imei: $"35{i:0000000000000}")).ToList(),
            paymentMethodCode: 2, cardNumber: "4797 1234 1234 7896", additionalDiscount: 10m, giftCardAmount: 40m, exceptionRequested: false, Now);

    /// <summary>Nota con cantidades de hasta 10 decimales en el detalle y descuento prorrateado.</summary>
    public static FiscalDocument DecimalNote() =>
        FiscalDocument.IssueCreditNote(Guid.NewGuid(), Emission(new DateTime(2026, 9, 25, 16, 0, 0, 0), number: 7, legend: NoteLegend),
            FiscalBuyer.Create(null, "C-0003", SiatCodes.DocumentNit, "1020703023", null, "Constructora Andina S.A.", null), null,
            new FiscalOriginalInvoice(null, 120, OfficialInvoiceCuf, new DateTime(2026, 9, 1, 9, 30, 0, 250), 4.41m),
            [
                new FiscalLineInput(null, "477300", 62151, "CAB-001", "Cable THW 12 AWG (metro)", 150m, 57, 5.50m, 25m, 1),
                new FiscalLineInput(null, "477300", 62151, "CAB-001", "Cable THW 12 AWG (metro)", 12.3456789012m, 57, 5.50m, null, 2),
            ],
            Now);
}
