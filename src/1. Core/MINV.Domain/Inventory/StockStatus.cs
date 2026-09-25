using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Estados del semáforo de stock con su prioridad y acción sugerida.</summary>
/// <remarks>Origen en la V2.1: 01_CONFIG (tblEstados).</remarks>
public sealed class StockStatus : Entity
{
    private StockStatus()
    {
    }

    public StockStatus(Guid tenantId, string code, string name, int priority, string suggestedAction, bool requiresAction)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código", 20);
        Name = Guard.Text(name, "El nombre", 40);
        Priority = priority;
        SuggestedAction = Guard.Text(suggestedAction, "La acción sugerida", 200);
        RequiresAction = requiresAction;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int Priority { get; private set; }

    public string SuggestedAction { get; private set; } = string.Empty;

    public bool RequiresAction { get; private set; }
}
