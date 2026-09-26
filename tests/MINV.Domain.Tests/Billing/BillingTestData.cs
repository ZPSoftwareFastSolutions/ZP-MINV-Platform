using MINV.Domain.Billing;

namespace MINV.Domain.Tests.Billing;

/// <summary>Fábrica de documentos fiscales de prueba (empresa, sucursal, punto de venta, CUIS/CUFD ficticios).</summary>
internal sealed class BillingTestData
{
    public const long Nit = 1003579028;
    public const string ControlCode = "67A75AC82F24C74";

    public Guid Tenant { get; } = Guid.NewGuid();

    public Guid Branch { get; } = Guid.NewGuid();

    public Guid PointOfSale { get; } = Guid.NewGuid();

    public DateTimeOffset Now { get; } = new(2026, 9, 25, 20, 0, 0, TimeSpan.Zero);

    /// <summary>Hora fiscal de la emisión (con milisegundos).</summary>
    public DateTime IssuedAt { get; } = new(2026, 9, 25, 16, 3, 48, 675);

    public FiscalEmission Emission(long number = 1, int emissionType = SiatCodes.EmissionOnline, DateTime? issuedAt = null,
        int branchCode = 0, int pointOfSaleCode = 0, string? cafc = null) =>
        new(Branch, SiatCodes.EnvironmentTest, Nit, PointOfSale, branchCode, pointOfSaleCode, Guid.NewGuid(), Guid.NewGuid(), ControlCode,
            emissionType, issuedAt ?? IssuedAt, number, "Ley N° 453: Tienes derecho a recibir información sobre las características y contenidos de los servicios que utilices.",
            "JPEREZ", emissionType == SiatCodes.EmissionOffline ? Guid.NewGuid() : null, cafc);

    public static FiscalBuyer Buyer(int documentType = SiatCodes.DocumentCi, string number = "5115889", string? complement = null,
        string? name = "Juan Pérez") =>
        FiscalBuyer.Create(Guid.NewGuid(), "C-0001", documentType, number, complement, name, "juan@example.com");

    public static FiscalLineInput Line(decimal quantity, decimal price, decimal? discount = null, int? tx = null, string code = "FER-001",
        string description = "Tornillo drywall 6x1\"") =>
        new(Guid.NewGuid(), "451010", 49111, code, description, quantity, 57, price, discount, tx);

    public FiscalDocument Invoice(IReadOnlyList<FiscalLineInput>? lines = null, FiscalBuyer? buyer = null, decimal additionalDiscount = 0,
        decimal giftCard = 0, bool exceptionRequested = false, FiscalEmission? emission = null, int paymentMethod = 1, string? card = null) =>
        FiscalDocument.IssueInvoice(Tenant, emission ?? Emission(), buyer ?? Buyer(), Guid.NewGuid(),
            lines ?? [Line(2, 75.50m, 5m), Line(1, 104m, code: "FER-002", description: "Cemento IP-30 bolsa 50 kg")], paymentMethod, card,
            additionalDiscount, giftCard, exceptionRequested, Now);

    /// <summary>Ejemplo del XML oficial de la nota 24: original 775 (transacción 1), devuelto 75 (transacción 2).</summary>
    public FiscalDocument CreditNote(IReadOnlyList<FiscalLineInput>? lines = null, DateTime? issuedAt = null, DateTime? originalIssuedAt = null,
        decimal? discountShare = null) =>
        FiscalDocument.IssueCreditNote(Tenant, Emission(number: 1, issuedAt: issuedAt), Buyer(), Guid.NewGuid(),
            new FiscalOriginalInvoice(Guid.NewGuid(), 1, "44AAEC00DBD34C53C3E2CCE1A3FA7AF1E2A08606A667A75AC82F24C74",
                originalIssuedAt ?? new DateTime(2026, 9, 1, 10, 14, 36, 0), discountShare),
            lines ?? [Line(1, 775m, tx: 1, code: "123456", description: "Amortiguadores"), Line(1, 75m, tx: 2, code: "123457", description: "Tornillos")],
            Now);
}
