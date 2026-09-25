using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Unidad de medida.</summary>
/// <remarks>Origen en la V2.1: 06_UNIDADES (tblUnidades): Código, Unidad, Decimales, Descripción.</remarks>
public sealed class UnitOfMeasure : Entity
{
    private UnitOfMeasure()
    {
    }

    public UnitOfMeasure(Guid tenantId, string code, string name, bool allowsDecimals, string? description)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código de la unidad", 10);
        Name = Guard.Text(name, "El nombre", 40);
        AllowsDecimals = allowsDecimals;
        Description = Guard.OptionalText(description, "La descripción", 200);
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool AllowsDecimals { get; private set; }

    public string? Description { get; private set; }
}
