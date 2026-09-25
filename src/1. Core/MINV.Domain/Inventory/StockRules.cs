using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Estados del semáforo de stock. El valor numérico es la prioridad de la V2.1 (tblEstados).</summary>
public enum StockStatusCode
{
    Inconsistent = 1,
    OutOfStock = 2,
    Critical = 3,
    Low = 4,
    Optimal = 5,
    Overstock = 6,
    Inactive = 7,
}

/// <summary>
/// Reglas del semáforo, alertas y reposición. Son las mismas de la V2.1 (tblEstados, 16_ALERTAS, 18_PEDIDO), que ya
/// existían en Python (generador) y TypeScript (RecalcularStock.ts): la V3 es la tercera implementación y las pruebas
/// de paridad verifican que produce los mismos resultados con los datos de la V2.1.
/// </summary>
public static class StockRules
{
    /// <summary>Orden de prioridad de las alertas (16_ALERTAS).</summary>
    public static readonly IReadOnlyList<StockStatusCode> AlertOrder =
        [StockStatusCode.Inconsistent, StockStatusCode.OutOfStock, StockStatusCode.Critical, StockStatusCode.Low, StockStatusCode.Overstock];

    /// <summary>Estados que entran al pedido sugerido (REPONER).</summary>
    public static readonly IReadOnlyList<StockStatusCode> ReorderOrder =
        [StockStatusCode.OutOfStock, StockStatusCode.Critical, StockStatusCode.Low];

    public static StockStatusCode Evaluate(decimal stock, decimal minimum, decimal maximum, bool isActive, decimal alertMargin)
    {
        if (!isActive)
        {
            return StockStatusCode.Inactive;
        }
        if (stock < 0)
        {
            return StockStatusCode.Inconsistent;
        }
        if (stock == 0)
        {
            return StockStatusCode.OutOfStock;
        }
        if (stock <= minimum)
        {
            return StockStatusCode.Critical;
        }
        if (stock <= minimum * (1 + alertMargin))
        {
            return StockStatusCode.Low;
        }
        return maximum > 0 && stock > maximum ? StockStatusCode.Overstock : StockStatusCode.Optimal;
    }

    public static bool IsAlert(StockStatusCode status) => AlertOrder.Contains(status);

    public static bool NeedsReorder(StockStatusCode status) => ReorderOrder.Contains(status);

    /// <summary>Estados que requieren acción (en la V2.1: ACCIONABLES, cuentan en «requieren acción»).</summary>
    public static bool RequiresAction(StockStatusCode status) =>
        status is StockStatusCode.Inconsistent or StockStatusCode.OutOfStock or StockStatusCode.Critical or StockStatusCode.Low;

    /// <summary>Tope de reposición: el máximo o, si no hay máximo, 2 × mínimo.</summary>
    public static decimal ReorderTarget(decimal minimum, decimal maximum) => maximum > 0 ? maximum : 2 * minimum;

    /// <summary>Cantidad sugerida de 16_ALERTAS (0 en sobrestock e inconsistente).</summary>
    public static decimal SuggestedQuantity(StockStatusCode status, decimal stock, decimal minimum, decimal maximum) =>
        status is StockStatusCode.Overstock or StockStatusCode.Inconsistent
            ? 0
            : Quantities.Round6(Math.Max(0, ReorderTarget(minimum, maximum) - Math.Max(0, stock)));

    /// <summary>Días que alcanza el stock al ritmo de salidas de 30 días (null si no hubo salidas).</summary>
    public static int? CoverageDays(decimal stock, decimal sales30Days) =>
        sales30Days > 0 ? (int)decimal.Floor(Math.Max(0, stock) / (sales30Days / 30m)) : null;

    /// <summary>Código ASCII (tabla StockStatuses).</summary>
    public static string Code(StockStatusCode status) => status switch
    {
        StockStatusCode.Inconsistent => "INCONSISTENTE",
        StockStatusCode.OutOfStock => "AGOTADO",
        StockStatusCode.Critical => "CRITICO",
        StockStatusCode.Low => "BAJO",
        StockStatusCode.Optimal => "OPTIMO",
        StockStatusCode.Overstock => "SOBRESTOCK",
        _ => "INACTIVO",
    };

    /// <summary>Texto visible, idéntico al de la V2.1.</summary>
    public static string Label(StockStatusCode status) => status switch
    {
        StockStatusCode.Inconsistent => "INCONSISTENTE",
        StockStatusCode.OutOfStock => "AGOTADO",
        StockStatusCode.Critical => "CRÍTICO",
        StockStatusCode.Low => "BAJO",
        StockStatusCode.Optimal => "ÓPTIMO",
        StockStatusCode.Overstock => "SOBRESTOCK",
        _ => "INACTIVO",
    };

    public static StockStatusCode FromLabel(string label) => label.Trim().ToUpperInvariant() switch
    {
        "INCONSISTENTE" => StockStatusCode.Inconsistent,
        "AGOTADO" => StockStatusCode.OutOfStock,
        "CRÍTICO" or "CRITICO" => StockStatusCode.Critical,
        "BAJO" => StockStatusCode.Low,
        "ÓPTIMO" or "OPTIMO" => StockStatusCode.Optimal,
        "SOBRESTOCK" => StockStatusCode.Overstock,
        "INACTIVO" => StockStatusCode.Inactive,
        _ => throw new DomainException("stock_status.unknown", $"Estado de stock desconocido: {label}."),
    };

    /// <summary>Estados que se siembran en cada empresa con la acción sugerida de la V2.1.</summary>
    public static IReadOnlyList<(StockStatusCode Status, string Action, bool RequiresAction)> Defaults =>
    [
        (StockStatusCode.Inconsistent, "Auditar la bitácora y registrar un AJUSTE", true),
        (StockStatusCode.OutOfStock, "Reabastecer de inmediato", true),
        (StockStatusCode.Critical, "Emitir orden de compra", true),
        (StockStatusCode.Low, "Programar reposición", true),
        (StockStatusCode.Optimal, "Sin acción", false),
        (StockStatusCode.Overstock, "Frenar compras / rotar inventario", true),
        (StockStatusCode.Inactive, "Sin acción", false),
    ];
}
