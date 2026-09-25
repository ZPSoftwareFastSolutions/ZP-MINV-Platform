using MINV.Domain.Common;

namespace MINV.Domain.Warehousing;

/// <summary>Posición física donde vive el stock (ruteo de picking por PickSequence).</summary>
/// <remarks>Origen en la V2.1: tblProductos: Ubicación (A-01-01).</remarks>
public sealed class Bin : Entity
{
    private Bin()
    {
    }

    public Bin(Guid tenantId, Guid shelfId, string code, Guid locationTypeId, int pickSequence)
        : base(tenantId)
    {
        ShelfId = Guard.NotEmpty(shelfId, nameof(shelfId));
        Code = Guard.Code(code, "El código de la posición", 40);
        LocationTypeId = Guard.NotEmpty(locationTypeId, nameof(locationTypeId));
        PickSequence = Guard.NonNegative(pickSequence, "La secuencia de picking");
        IsActive = true;
    }

    public Guid ShelfId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public Guid LocationTypeId { get; private set; }

    public int PickSequence { get; private set; }

    public bool IsActive { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
