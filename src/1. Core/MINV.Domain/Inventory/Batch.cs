using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>
/// Lote de una variante. Toda variante tiene un lote por defecto («SIN-LOTE»): así la existencia se identifica
/// siempre por (posición, lote) y la variante se deriva del lote, sin dependencias transitivas (5FN).
/// </summary>
public sealed class Batch : Entity
{
    public const string DefaultLotNumber = "SIN-LOTE";

    private Batch()
    {
    }

    public Batch(Guid tenantId, Guid variantId, string lotNumber, DateOnly? manufacturedOn, DateOnly? expiresOn)
        : this(tenantId, variantId, lotNumber, manufacturedOn, expiresOn, isDefault: false)
    {
    }

    private Batch(Guid tenantId, Guid variantId, string lotNumber, DateOnly? manufacturedOn, DateOnly? expiresOn, bool isDefault)
        : base(tenantId)
    {
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        LotNumber = Guard.Text(lotNumber, "El número de lote", 40).ToUpperInvariant();
        Guard.That(expiresOn is null || manufacturedOn is null || expiresOn >= manufacturedOn, "batch.expiry",
            "La caducidad no puede ser anterior a la fabricación.");
        Guard.That(isDefault || LotNumber != DefaultLotNumber, "batch.reserved_lot",
            $"El lote {DefaultLotNumber} está reservado para el lote por defecto.");
        ManufacturedOn = manufacturedOn;
        ExpiresOn = expiresOn;
        IsDefault = isDefault;
    }

    public Guid VariantId { get; private set; }

    public string LotNumber { get; private set; } = string.Empty;

    public DateOnly? ManufacturedOn { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    public bool IsDefault { get; private set; }

    public static Batch CreateDefault(Guid tenantId, Guid variantId) =>
        new(tenantId, variantId, DefaultLotNumber, null, null, isDefault: true);

    public bool IsExpired(DateOnly today) => ExpiresOn is { } d && d < today;
}
