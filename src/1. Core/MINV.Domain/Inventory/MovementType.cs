using MINV.Domain.Common;

namespace MINV.Domain.Inventory;

/// <summary>Tipo de movimiento: su <see cref="StockFactor"/> (+1/−1) es lo único que decide el signo del stock.</summary>
/// <remarks>Origen en la V2.1: 01_CONFIG (tblTiposMov): Tipo, FactorStock, Dominio, Descripción.</remarks>
public sealed class MovementType : Entity
{
    private MovementType()
    {
    }

    public MovementType(Guid tenantId, string code, string name, string? description, short stockFactor,
        MovementDomain domain, bool requiresNotes, bool isInitialBalance, bool isSystem)
        : base(tenantId)
    {
        Code = Guard.Code(code, "El código del tipo", 30);
        Name = Guard.Text(name, "El nombre del tipo", 80);
        Description = Guard.OptionalText(description, "La descripción", 200);
        Guard.That(stockFactor is 1 or -1, "movement_type.factor",
            "El factor de stock debe ser +1 o −1: no existe un tipo de movimiento con factor 0.");
        Guard.That(!isInitialBalance || stockFactor == 1, "movement_type.initial_balance",
            "El saldo inicial siempre suma al stock.");
        StockFactor = stockFactor;
        Domain = Guard.Defined(domain, "El dominio");
        RequiresNotes = requiresNotes;
        IsInitialBalance = isInitialBalance;
        IsSystem = isSystem;
    }

    public string Code { get; private set; } = string.Empty;

    /// <summary>Nombre visible (los de la V2.1 se conservan: «SALDO INICIAL», «AJUSTE (+)»…).</summary>
    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public short StockFactor { get; private set; }

    public MovementDomain Domain { get; private set; }

    /// <summary>Exige observación (motivo), como los AJUSTE de la V2.1.</summary>
    public bool RequiresNotes { get; private set; }

    /// <summary>SALDO INICIAL: solo se admite en una existencia sin movimientos.</summary>
    public bool IsInitialBalance { get; private set; }

    public bool IsSystem { get; private set; }

    public bool Increases => StockFactor > 0;

    /// <summary>Salida por venta (cuenta en «Salidas 30 días», cobertura y ranking).</summary>
    public bool IsSale => Domain == MovementDomain.Sales && StockFactor < 0;

    public decimal Signed(decimal quantity) => quantity * StockFactor;

    /// <summary>Tipos que se siembran en cada empresa: los 5 de la V2.1 con sus nombres originales y los de la V3.</summary>
    public static IReadOnlyList<MovementType> CreateDefaults(Guid tenantId) =>
    [
        new(tenantId, MovementTypeCodes.InitialBalance, "SALDO INICIAL",
            "Carga del inventario existente al implementar (una vez por existencia).", 1, MovementDomain.Warehouse, false, true, true),
        new(tenantId, MovementTypeCodes.Receipt, "ENTRADA",
            "Compra, recepción de mercancía o devolución de un cliente.", 1, MovementDomain.Warehouse, false, false, true),
        new(tenantId, MovementTypeCodes.AdjustmentIn, "AJUSTE (+)",
            "Sobrante encontrado en conteo físico. Exige observación.", 1, MovementDomain.Warehouse, true, false, true),
        new(tenantId, MovementTypeCodes.AdjustmentOut, "AJUSTE (-)",
            "Merma, daño, pérdida o faltante. Exige observación.", -1, MovementDomain.Warehouse, true, false, true),
        new(tenantId, MovementTypeCodes.Issue, "SALIDA",
            "Venta, despacho a cliente o consumo interno.", -1, MovementDomain.Sales, false, false, true),
        new(tenantId, MovementTypeCodes.Sale, "VENTA POS",
            "Venta en el punto de venta.", -1, MovementDomain.Sales, false, false, true),
        new(tenantId, MovementTypeCodes.SaleReturn, "DEVOLUCIÓN DE CLIENTE",
            "Reingreso por devolución de una venta. Exige observación.", 1, MovementDomain.Sales, true, false, true),
        new(tenantId, MovementTypeCodes.PurchaseReceipt, "RECEPCIÓN DE COMPRA",
            "Ingreso por una recepción de mercancía de una orden de compra.", 1, MovementDomain.Warehouse, false, false, true),
        new(tenantId, MovementTypeCodes.PurchaseReturn, "DEVOLUCIÓN A PROVEEDOR",
            "Salida por devolución a un proveedor. Exige observación.", -1, MovementDomain.Warehouse, true, false, true),
        new(tenantId, MovementTypeCodes.TransferOut, "TRASLADO (SALIDA)",
            "Salida hacia otro almacén.", -1, MovementDomain.Warehouse, false, false, true),
        new(tenantId, MovementTypeCodes.TransferIn, "TRASLADO (ENTRADA)",
            "Entrada desde otro almacén.", 1, MovementDomain.Warehouse, false, false, true),
    ];
}

/// <summary>Códigos estables de los tipos de movimiento y su correspondencia con la V2.1.</summary>
public static class MovementTypeCodes
{
    public const string InitialBalance = "SALDO_INICIAL";
    public const string Receipt = "ENTRADA";
    public const string AdjustmentIn = "AJUSTE_POS";
    public const string AdjustmentOut = "AJUSTE_NEG";
    public const string Issue = "SALIDA";
    public const string Sale = "VENTA_POS";
    public const string SaleReturn = "DEVOLUCION_CLIENTE";
    public const string PurchaseReceipt = "RECEPCION_COMPRA";
    public const string PurchaseReturn = "DEVOLUCION_PROVEEDOR";
    public const string TransferOut = "TRASLADO_SALIDA";
    public const string TransferIn = "TRASLADO_ENTRADA";

    /// <summary>Código de la V3 para el «Tipo» de un movimiento de la V2.1 (tblEntradas / tblSalidas).</summary>
    public static string FromV21(string tipo) => tipo.Trim().ToUpperInvariant() switch
    {
        "SALDO INICIAL" => InitialBalance,
        "ENTRADA" => Receipt,
        "AJUSTE (+)" => AdjustmentIn,
        "AJUSTE (-)" => AdjustmentOut,
        "SALIDA" => Issue,
        _ => throw new DomainException("movement_type.v21_unknown", $"Tipo de movimiento de la V2.1 desconocido: {tipo}."),
    };
}
