using MINV.Domain.Catalog;
using MINV.Domain.Inventory;

namespace MINV.Domain.Tests;

/// <summary>Fábrica de datos de prueba (un tenant, tipos de movimiento de la V2.1 y existencias).</summary>
internal sealed class TestData
{
    public Guid Tenant { get; } = Guid.NewGuid();

    public Guid User { get; } = Guid.NewGuid();

    public DateTimeOffset Now { get; } = new(2026, 9, 25, 16, 0, 0, TimeSpan.Zero);

    public DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    public Dictionary<string, MovementType> Types { get; }

    public TestData()
    {
        Types = MovementType.CreateDefaults(Tenant).ToDictionary(t => t.Code);
    }

    /// <summary>V4 · Sucursal de las existencias y documentos de prueba.</summary>
    public Guid Branch { get; } = Guid.NewGuid();

    public MovementType Type(string code) => Types[code];

    public CountMovementTypes CountTypes =>
        new(Type(MovementTypeCodes.InitialBalance), Type(MovementTypeCodes.AdjustmentIn), Type(MovementTypeCodes.AdjustmentOut));

    public static readonly UnitRule Und = new("UND", false);

    public static readonly UnitRule Kg = new("KG", true);

    public StockLevel Level(decimal initial = 0)
    {
        var level = StockLevel.Open(Tenant, Branch, Guid.NewGuid(), Guid.NewGuid());
        if (initial > 0)
        {
            level.Register(Type(MovementTypeCodes.InitialBalance), initial, Kg, Context());
        }
        return level;
    }

    public MovementContext Context(string? notes = null, string? doc = null) => new(User, Today, Now, doc, notes);

    public Product Product(string code = "FER-001") =>
        Catalog.Product.Create(Tenant, code, "Tornillo drywall 6x1\" (caja x100)", Guid.NewGuid(), Guid.NewGuid());
}
