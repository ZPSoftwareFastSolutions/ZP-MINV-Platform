namespace MINV.Domain.Iam;

/// <summary>Roles del sistema. Los cuatro primeros vienen de la V2.1 (02_USUARIOS); CAJERO y GERENCIA son de la V3.</summary>
public static class RoleCodes
{
    public const string Admin = "ADMIN";
    public const string Warehouse = "BODEGA";
    public const string Sales = "VENTAS";
    public const string ReadOnly = "CONSULTA";
    public const string Cashier = "CAJERO";
    public const string Management = "GERENCIA";

    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        (Admin, "Administrador"), (Warehouse, "Bodega"), (Sales, "Ventas"), (ReadOnly, "Consulta"),
        (Cashier, "Cajero"), (Management, "Gerencia"),
    ];
}

/// <summary>Permisos granulares (código estable en minúsculas con puntos).</summary>
public static class PermissionCodes
{
    public const string CatalogManage = "catalog.manage";
    public const string UsersManage = "iam.users.manage";
    public const string MovementsRegisterWarehouse = "inventory.movements.register.warehouse";
    public const string MovementsRegisterSales = "inventory.movements.register.sales";
    public const string StockView = "inventory.stock.view";
    public const string PhysicalCountRecord = "inventory.counts.record";
    public const string PhysicalCountPost = "inventory.counts.post";
    public const string PurchasingManage = "purchasing.manage";
    public const string PosOperate = "sales.pos.operate";
    public const string AuditView = "iam.audit.view";
    public const string AccountingManage = "accounting.manage";
    public const string ReportsView = "reports.view";
    public const string CustomersManage = "sales.customers.manage";
    public const string SalesView = "sales.view";

    public static readonly IReadOnlyList<(string Code, string Description)> All =
    [
        (CatalogManage, "Crear y modificar productos, categorías, unidades y proveedores"),
        (UsersManage, "Administrar usuarios, roles y permisos"),
        (MovementsRegisterWarehouse, "Registrar entradas, saldo inicial y ajustes (en la V2.1: 10A_ENTRADAS)"),
        (MovementsRegisterSales, "Registrar salidas (en la V2.1: 10B_SALIDAS)"),
        (StockView, "Consultar stock, alertas y pedido sugerido"),
        (PhysicalCountRecord, "Registrar conteos de la toma física"),
        (PhysicalCountPost, "Generar los ajustes de la toma física"),
        (PurchasingManage, "Órdenes de compra, recepciones y devoluciones"),
        (PosOperate, "Abrir y cerrar caja, vender y cobrar"),
        (AuditView, "Consultar la auditoría y la actividad"),
        (AccountingManage, "Asientos, períodos y costos"),
        (ReportsView, "Consultar reportes de ventas, compras, inventario y rentabilidad"),
        (CustomersManage, "Crear y modificar clientes"),
        (SalesView, "Consultar el historial de ventas y facturas"),
    ];

    /// <summary>Matriz rol → permisos (RBAC por defecto de un tenant nuevo). V3.1: cada rol suma las funciones de su
    /// puesto (ventas y caja venden y atienden clientes, bodega compra y ve reportes, gerencia aprueba y contabiliza).</summary>
    public static IReadOnlyList<string> ForRole(string roleCode) => roleCode switch
    {
        RoleCodes.Admin => All.Select(p => p.Code).ToList(),
        RoleCodes.Warehouse => [MovementsRegisterWarehouse, StockView, PhysicalCountRecord, PhysicalCountPost, PurchasingManage, ReportsView],
        RoleCodes.Sales => [MovementsRegisterSales, StockView, PosOperate, CustomersManage, SalesView, ReportsView],
        RoleCodes.Cashier => [PosOperate, MovementsRegisterSales, StockView, CustomersManage, SalesView],
        RoleCodes.Management => [StockView, AuditView, AccountingManage, ReportsView, SalesView, PurchasingManage],
        RoleCodes.ReadOnly => [StockView, ReportsView],
        _ => [],
    };
}
