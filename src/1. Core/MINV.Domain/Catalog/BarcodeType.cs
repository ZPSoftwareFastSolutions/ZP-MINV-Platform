using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Simbología de código de barras (EAN-13, UPC-A…).</summary>
public sealed class BarcodeType : Entity
{
    private BarcodeType()
    {
    }

    public BarcodeType(Guid tenantId, string code, string name, int? length, bool hasCheckDigit)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código", 20);
        Name = Guard.Text(name, "El nombre", 60);
        Guard.That(length is null || length >= 0, "guard.non_negative", "La longitud no puede ser negativo.");
        Length = length;
        HasCheckDigit = hasCheckDigit;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int? Length { get; private set; }

    public bool HasCheckDigit { get; private set; }
}
