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

    // V4 · multi-sucursal e integraciones
    public const string BranchesAll = "corporate.branches.all";
    public const string BranchesManage = "corporate.branches.manage";
    public const string TransfersManage = "inventory.transfers.manage";
    public const string IntegrationManage = "integration.manage";

    // V4.1 · facturación SIAT
    public const string BillingView = "billing.view";
    public const string BillingIssue = "billing.issue";
    public const string BillingVoid = "billing.void";
    public const string BillingContingency = "billing.contingency";
    public const string BillingConfigure = "billing.configure";

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
        (BranchesAll, "Ver y operar todas las sucursales (gerencia global)"),
        (BranchesManage, "Crear y modificar sucursales y asignar usuarios a sucursales"),
        (TransfersManage, "Crear, despachar y recibir transferencias entre sucursales"),
        (IntegrationManage, "Administrar API Keys y webhooks de integración B2B"),
        (BillingView, "Consultar documentos fiscales, estado del SIAT y libros de ventas y compras"),
        (BillingIssue, "Emitir facturas (al vender) y reenviar documentos fiscales"),
        (BillingVoid, "Anular y revertir documentos fiscales y emitir notas crédito-débito"),
        (BillingContingency, "Gestionar eventos significativos, paquetes de contingencia y CAFC"),
        (BillingConfigure, "Configurar la facturación SIAT: NIT, token, sucursales, puntos de venta, CUIS, CUFD, catálogos y homologación"),
    ];

    /// <summary>Matriz rol → permisos (RBAC por defecto de un tenant nuevo). V3.1: cada rol suma las funciones de su
    /// puesto (ventas y caja venden y atienden clientes, bodega compra y ve reportes, gerencia aprueba y contabiliza).</summary>
    public static IReadOnlyList<string> ForRole(string roleCode) => roleCode switch
    {
        RoleCodes.Admin => All.Select(p => p.Code).ToList(),
        RoleCodes.Warehouse => [MovementsRegisterWarehouse, StockView, PhysicalCountRecord, PhysicalCountPost, PurchasingManage, ReportsView,
            TransfersManage],
        RoleCodes.Sales => [MovementsRegisterSales, StockView, PosOperate, CustomersManage, SalesView, ReportsView, BillingView, BillingIssue],
        RoleCodes.Cashier => [PosOperate, MovementsRegisterSales, StockView, CustomersManage, SalesView, BillingView, BillingIssue],
        RoleCodes.Management => [StockView, AuditView, AccountingManage, ReportsView, SalesView, PurchasingManage, BranchesAll,
            TransfersManage, BillingView, BillingVoid, BillingContingency],
        RoleCodes.ReadOnly => [StockView, ReportsView, BillingView],
        _ => [],
    };
}
