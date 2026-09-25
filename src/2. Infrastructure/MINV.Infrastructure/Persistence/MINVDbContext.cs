using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence;

/// <summary>
/// Contexto EF Core de M-INV V3 (PostgreSQL, Code-First). 96 tablas en 7 contextos delimitados (un esquema por
/// contexto), normalizadas hasta 5FN. Reglas que aplica a todo el modelo (ver <see cref="ModelConventions"/>):
/// <list type="bullet">
/// <item>Multi-tenant: toda entidad (salvo Tenant y LicenseModule) tiene TenantId con filtro global de consulta y cada
/// clave foránea incluye el TenantId: una fila solo puede referenciar filas de su misma empresa.</item>
/// <item>Concurrencia optimista: <c>RowVersion</c> → columna de sistema <c>xmin</c> de PostgreSQL.</item>
/// <item>Append-only: StockMovements, AuditLogs, AccessLogs, CashMovements, Payments, ExchangeRates y
/// AverageCostHistory no admiten UPDATE ni DELETE (EF Core + triggers).</item>
/// </list>
/// </summary>
public sealed class MINVDbContext : DbContext, IMinvDbContext
{
    private readonly ITenantContext _tenant;

    public MINVDbContext(DbContextOptions<MINVDbContext> options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    /// <summary>Tenant de la sesión; lo usan los filtros globales (EF Core lo parametriza en cada consulta).</summary>
    public Guid CurrentTenantId => _tenant.IsSet ? _tenant.TenantId : Guid.Empty;

    // ---- IAM y tenants · esquema iam (14 tablas)
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<LicenseModule> Modules => Set<LicenseModule>();
    public DbSet<TenantModule> TenantModules => Set<TenantModule>();
    public DbSet<TenantConfig> TenantConfigs => Set<TenantConfig>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<HardwareToken> HardwareTokens => Set<HardwareToken>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // ---- Catálogo y datos maestros · esquema catalog (18 tablas)
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryHierarchy> CategoryHierarchies => Set<CategoryHierarchy>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<BrandModel> Models => Set<BrandModel>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<UnitConversion> UnitConversions => Set<UnitConversion>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductUnitConversion> ProductUnitConversions => Set<ProductUnitConversion>();
    public DbSet<CatalogAttribute> Attributes => Set<CatalogAttribute>();
    public DbSet<CatalogAttributeValue> AttributeValues => Set<CatalogAttributeValue>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductVariantAttribute> ProductVariantAttributes => Set<ProductVariantAttribute>();
    public DbSet<BarcodeType> BarcodeTypes => Set<BarcodeType>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<Tax> Taxes => Set<Tax>();
    public DbSet<ProductTax> ProductTaxes => Set<ProductTax>();
    public DbSet<ProductSupplier> ProductSuppliers => Set<ProductSupplier>();
    public DbSet<ProductStockPolicy> ProductStockPolicies => Set<ProductStockPolicy>();

    // ---- Topología de almacén · esquema warehouse (10 tablas)
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<LocationType> LocationTypes => Set<LocationType>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Aisle> Aisles => Set<Aisle>();
    public DbSet<Rack> Racks => Set<Rack>();
    public DbSet<Shelf> Shelves => Set<Shelf>();
    public DbSet<Bin> Bins => Set<Bin>();
    public DbSet<BranchUser> BranchUsers => Set<BranchUser>();
    public DbSet<BinAssignment> BinAssignments => Set<BinAssignment>();

    // ---- Motor transaccional de stock · esquema inventory (14 tablas)
    public DbSet<MovementType> MovementTypes => Set<MovementType>();
    public DbSet<StockStatus> StockStatuses => Set<StockStatus>();
    public DbSet<AdjustmentReason> AdjustmentReasons => Set<AdjustmentReason>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<SerialNumber> SerialNumbers => Set<SerialNumber>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockAdjustmentLine> StockAdjustmentLines => Set<StockAdjustmentLine>();
    public DbSet<PhysicalCount> PhysicalCounts => Set<PhysicalCount>();
    public DbSet<PhysicalCountLine> PhysicalCountLines => Set<PhysicalCountLine>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();

    // ---- Compras y proveedores · esquema purchasing (11 tablas)
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierContact> SupplierContacts => Set<SupplierContact>();
    public DbSet<SupplierAddress> SupplierAddresses => Set<SupplierAddress>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SupplierInvoiceLine> SupplierInvoiceLines => Set<SupplierInvoiceLine>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnLine> PurchaseReturnLines => Set<PurchaseReturnLine>();

    // ---- Ventas y POS · esquema sales (19 tablas)
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<State> States => Set<State>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<PostalCode> PostalCodes => Set<PostalCode>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<CustomerCategory> CustomerCategories => Set<CustomerCategory>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();
    public DbSet<PosRegister> POSRegisters => Set<PosRegister>();
    public DbSet<PosSession> POSSessions => Set<PosSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Payment> Payments => Set<Payment>();

    // ---- Costos y contabilidad · esquema accounting (10 tablas)
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<AverageCostHistory> AverageCostHistory => Set<AverageCostHistory>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<TaxRule> TaxRules => Set<TaxRule>();

    public void ClearTracking() => ChangeTracker.Clear();

    /// <summary>Si la conexión ya está abierta (p. ej. dentro de una transacción), vuelve a fijar <c>minv.tenant_id</c>
    /// para que las políticas de Row Level Security vean el tenant actual (la base en memoria del modo demostración no
    /// tiene conexión ni RLS).</summary>
    public async Task SyncTenantSessionAsync(CancellationToken cancellationToken = default)
    {
        if (Database.IsRelational() && Database.GetDbConnection().State == System.Data.ConnectionState.Open)
        {
            var value = CurrentTenantId == Guid.Empty ? string.Empty : CurrentTenantId.ToString();
            await Database.ExecuteSqlRawAsync("SELECT set_config('minv.tenant_id', {0}, false)", [value], cancellationToken);
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(
                "Otra sesión modificó los mismos datos mientras usted trabajaba. Se reintentará con los valores actuales.", ex);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
        {
            throw new ConcurrencyConflictException(
                "Otra sesión registró el mismo dato al mismo tiempo (" + PostgresErrors.ConstraintName(ex) + ").", ex);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MINVDbContext).Assembly);
        ModelConventions.Apply(modelBuilder, this);
    }
}
