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

    /// <summary>V6 · Usuario técnico de la tienda web (principal de las rutas públicas <c>/storefront/v1</c>, regla S-02).</summary>
    public const string Storefront = "TIENDA_WEB";

    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        (Admin, "Administrador"), (Warehouse, "Bodega"), (Sales, "Ventas"), (ReadOnly, "Consulta"),
        (Cashier, "Cajero"), (Management, "Gerencia"), (Storefront, "Tienda web"),
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

    // V4.2 · edición Tecnología (fichas técnicas, series e IMEI, garantías y RMA, armador de PC)
    public const string SpecsManage = "catalog.specs.manage";
    public const string SerialsView = "inventory.serials.view";
    public const string SerialsManage = "inventory.serials.manage";
    public const string ServiceOpen = "service.rma.open";
    public const string ServiceManage = "service.rma.manage";
    public const string PcBuildManage = "sales.pcbuild.manage";

    // V6 · Tienda web conectada (catálogo público y reservas de armados)
    public const string StorefrontRead = "storefront.read";
    public const string StorefrontReserve = "storefront.reserve";

    public static readonly IReadOnlyList<(string Code, string Description)> All =
    [
        (CatalogManage, "Crear y modificar productos, categorías, unidades y proveedores"),
        (UsersManage, "Administrar usuarios, roles y permisos"),
        (MovementsRegisterWarehouse, "Registrar entradas, saldo inicial y ajustes"),
        (MovementsRegisterSales, "Registrar salidas"),
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
        (SpecsManage, "Fichas técnicas: especificaciones por categoría, valores de cada producto, garantía y control por serie o IMEI"),
        (SerialsView, "Consultar series e IMEI, su trazabilidad, la garantía de una unidad y los casos RMA"),
        (SerialsManage, "Registrar series e IMEI de unidades en stock (inventario inicial) y dar de baja unidades serializadas"),
        (ServiceOpen, "Abrir casos de garantía (RMA) al recibir un equipo del cliente"),
        (ServiceManage, "Garantías y RMA: diagnosticar, enviar al proveedor, reponer con otra unidad y entregar equipos"),
        (PcBuildManage, "Armador de PC: armar, cotizar y anular armados (cotizaciones con precio congelado)"),
        (StorefrontRead, "Tienda web: leer el catálogo público (productos, precios, disponibilidad, imágenes y armados sugeridos)"),
        (StorefrontReserve, "Tienda web: reservar armados con reserva de stock y consultar o cancelar una reserva con su teléfono"),
    ];

    /// <summary>Matriz rol → permisos (RBAC por defecto de un tenant nuevo). V3.1: cada rol suma las funciones de su
    /// puesto (ventas y caja venden y atienden clientes, bodega compra y ve reportes, gerencia aprueba y contabiliza).
    /// V4.2: bodega lleva series, fichas técnicas y RMA; ventas y caja arman PC, consultan series y abren RMA; gerencia
    /// todo lo de la edición Tecnología; consulta solo lee series y casos. V6: administración y gerencia también leen y
    /// reservan por la tienda web; TIENDA_WEB (usuario técnico) solo eso más la consulta de stock.</summary>
    public static IReadOnlyList<string> ForRole(string roleCode) => roleCode switch
    {
        RoleCodes.Admin => All.Select(p => p.Code).ToList(),
        RoleCodes.Warehouse => [MovementsRegisterWarehouse, StockView, PhysicalCountRecord, PhysicalCountPost, PurchasingManage, ReportsView,
            TransfersManage, SpecsManage, SerialsView, SerialsManage, ServiceOpen, ServiceManage],
        RoleCodes.Sales => [MovementsRegisterSales, StockView, PosOperate, CustomersManage, SalesView, ReportsView, BillingView, BillingIssue,
            SerialsView, ServiceOpen, PcBuildManage],
        RoleCodes.Cashier => [PosOperate, MovementsRegisterSales, StockView, CustomersManage, SalesView, BillingView, BillingIssue, SerialsView,
            ServiceOpen, PcBuildManage],
        RoleCodes.Management => [StockView, AuditView, AccountingManage, ReportsView, SalesView, PurchasingManage, BranchesAll,
            TransfersManage, BillingView, BillingVoid, BillingContingency, SpecsManage, SerialsView, SerialsManage, ServiceOpen, ServiceManage,
            PcBuildManage, StorefrontRead, StorefrontReserve],
        RoleCodes.ReadOnly => [StockView, ReportsView, BillingView, SerialsView],
        RoleCodes.Storefront => [StorefrontRead, StorefrontReserve, StockView],
        _ => [],
    };
}
