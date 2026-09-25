using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;
using MINV.Infrastructure.Persistence;

namespace MINV.Infrastructure.Provisioning;

/// <summary>Datos para crear una empresa nueva (tenant) lista para operar.</summary>
public sealed record ProvisionTenantRequest(
    string Code,
    string LegalName,
    string? TaxId,
    string AdminEmail,
    string AdminName,
    string AdminPassword,
    string CurrencyCode = "BOB",
    string TimeZoneId = "America/La_Paz",
    string CountryIso = "BO",
    string CountryName = "Bolivia",
    string StateCode = "LP",
    string StateName = "La Paz",
    string CityName = "La Paz",
    string BranchName = "Casa matriz",
    string WarehouseName = "Almacén principal",
    decimal AlertMargin = 0.20m,
    int DaysWithoutRotation = 60,
    DateOnly? MinBusinessDate = null,
    IReadOnlyList<string>? LicensedModules = null);

/// <summary>Lo que se creó (identificadores útiles para importar datos o para las pruebas).</summary>
public sealed record ProvisionedTenant(
    Guid TenantId, Guid AdminUserId, Guid BranchId, Guid WarehouseId, string WarehouseCode, Guid ZoneId,
    Guid PickingLocationTypeId, Guid DefaultBinId, Guid CurrencyId, IReadOnlyDictionary<string, Guid> Roles,
    IReadOnlyDictionary<string, Guid> Units);

/// <summary>
/// Aprovisiona una empresa: configuración, moneda, geografía, sucursal y almacén con su topología mínima, RBAC
/// (roles, permisos y administrador), catálogos base (unidades, tipos de movimiento, estados del semáforo, motivos,
/// simbologías, medios de pago, impuesto), lista de precios, cliente «consumidor final», período contable y los módulos
/// licenciados. Los catálogos base son los de la V2.1 (unidades, tipos, estados y roles) más los propios de la V3.
/// </summary>
public sealed class TenantProvisioner(MinvWriteDbContext db, ITenantContext tenant, IPasswordHasher hasher, IClock clock)
{
    public const string WarehouseCode = "ALM01";
    public const string DefaultBinSuffix = "GENERAL";

    /// <summary>Unidades de la V2.1 (tools/demo_data.py: UNIDADES).</summary>
    public static readonly IReadOnlyList<(string Code, string Name, bool Decimals, string Description)> DefaultUnits =
    [
        ("UND", "Unidad", false, "Pieza individual"),
        ("CAJA", "Caja", false, "Caja cerrada del proveedor"),
        ("PAQ", "Paquete", false, "Paquete o bolsa cerrada"),
        ("PAR", "Par", false, "Par (guantes, botas)"),
        ("ROLLO", "Rollo", false, "Rollo completo"),
        ("KG", "Kilogramo", true, "Peso en kilogramos"),
        ("GL", "Galón", true, "Volumen en galones"),
        ("LT", "Litro", true, "Volumen en litros"),
        ("MT", "Metro", true, "Longitud en metros"),
    ];

    public async Task<ProvisionedTenant> ProvisionAsync(ProvisionTenantRequest r, CancellationToken ct = default)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        Guard.That(!await db.Tenants.AnyAsync(t => t.Code == code, ct), "tenant.duplicate", $"Ya existe la empresa {code}.");
        var company = new Tenant(code, r.LegalName, r.TaxId);
        db.Tenants.Add(company);
        tenant.Set(company.Id);
        await db.SyncTenantSessionAsync(ct);
        var id = company.Id;
        var now = clock.UtcNow;

        // Moneda y geografía
        var (currencyName, symbol) = r.CurrencyCode.ToUpperInvariant() switch
        {
            "BOB" => ("Boliviano", "Bs"),
            "COP" => ("Peso colombiano", "$"),
            "USD" => ("Dólar estadounidense", "US$"),
            _ => (r.CurrencyCode.ToUpperInvariant(), r.CurrencyCode.ToUpperInvariant()),
        };
        var currency = new Currency(id, r.CurrencyCode, currencyName, symbol, 2);
        var country = new Country(id, r.CountryIso, r.CountryName);
        var state = new State(id, country.Id, r.StateCode, r.StateName);
        var city = new City(id, state.Id, r.CityName);
        var postal = new PostalCode(id, city.Id, "S/N");
        var address = new Address(id, postal.Id, "Dirección principal", null);
        db.AddRange(currency, country, state, city, postal, address);

        // Sucursal, almacén y topología mínima (zona / pasillo / estantería / nivel / posición GENERAL)
        var branch = new Branch(id, "CM", r.BranchName, address.Id);
        var warehouse = new Warehouse(id, branch.Id, WarehouseCode, r.WarehouseName);
        var locationTypes = new[]
        {
            new LocationType(id, "PICKING", "Picking", true, false, false),
            new LocationType(id, "RESERVA", "Reserva", false, false, false),
            new LocationType(id, "RECEPCION", "Recepción", false, true, false),
            new LocationType(id, "DESPACHO", "Despacho", false, false, false),
            new LocationType(id, "POS", "Góndola del punto de venta", true, false, true),
        };
        var zone = new Zone(id, branch.Id, warehouse.Id, "GEN", "General", locationTypes[0].Id);
        var aisle = new Aisle(id, branch.Id, zone.Id, "00");
        var rack = new Rack(id, branch.Id, aisle.Id, "00");
        var shelf = new Shelf(id, branch.Id, rack.Id, "00");
        var bin = new Bin(id, branch.Id, shelf.Id, $"{WarehouseCode}-{DefaultBinSuffix}", locationTypes[0].Id, 0);
        db.AddRange(branch, warehouse);
        db.AddRange(locationTypes);
        db.AddRange(zone, aisle, rack, shelf, bin);
        db.Add(new TenantConfig(id, currency.Id, warehouse.Id, r.AlertMargin, r.DaysWithoutRotation,
            r.MinBusinessDate ?? new DateOnly(2020, 1, 1), r.TimeZoneId));

        // RBAC: permisos, roles, matriz rol-permiso y administrador
        var permissions = PermissionCodes.All.ToDictionary(p => p.Code, p => new Permission(id, p.Code, p.Description));
        db.AddRange(permissions.Values);
        var roles = RoleCodes.All.ToDictionary(x => x.Code, x => new Role(id, x.Code, x.Name, isSystem: true));
        db.AddRange(roles.Values);
        foreach (var role in roles.Values)
        {
            db.AddRange(PermissionCodes.ForRole(role.Code).Select(p => new RolePermission(id, role.Id, permissions[p].Id)));
        }
        var admin = new User(id, r.AdminEmail, r.AdminName);
        db.Add(admin);
        db.Add(new UserCredential(id, admin.Id, hasher.Hash(r.AdminPassword), hasher.Algorithm, hasher.Iterations, now, mustChangePassword: false));
        db.Add(new UserRole(id, admin.Id, roles[RoleCodes.Admin].Id));
        db.Add(new BranchUser(id, branch.Id, admin.Id));

        // Catálogos base
        var units = DefaultUnits.ToDictionary(u => u.Code, u => new UnitOfMeasure(id, u.Code, u.Name, u.Decimals, u.Description));
        db.AddRange(units.Values);
        db.AddRange(MovementType.CreateDefaults(id));
        db.AddRange(StockRules.Defaults.Select(s => new StockStatus(id, StockRules.Code(s.Status), StockRules.Label(s.Status),
            (int)s.Status, s.Action, s.RequiresAction)));
        db.AddRange(new AdjustmentReason(id, "MERMA", "Merma"), new AdjustmentReason(id, "DANO", "Daño"),
            new AdjustmentReason(id, "PERDIDA", "Pérdida o robo"), new AdjustmentReason(id, "CONTEO", "Diferencia de conteo físico"),
            new AdjustmentReason(id, "CORRECCION", "Corrección de un registro"));
        db.AddRange(new BarcodeType(id, "EAN13", "EAN-13", 13, true), new BarcodeType(id, "EAN8", "EAN-8", 8, true),
            new BarcodeType(id, "UPCA", "UPC-A", 12, true), new BarcodeType(id, "CODE128", "Code 128", null, false),
            new BarcodeType(id, "INTERNO", "Código interno", null, false));
        db.AddRange(new PaymentMethod(id, "EFECTIVO", "Efectivo", false, true), new PaymentMethod(id, "TARJETA", "Tarjeta", true, false),
            new PaymentMethod(id, "QR", "QR / billetera", true, false), new PaymentMethod(id, "TRANSFERENCIA", "Transferencia", true, false));
        var iva = new Tax(id, "IVA", "Impuesto al valor agregado");
        db.Add(iva);
        db.Add(new TaxRate(id, iva.Id, r.CountryIso.Equals("BO", StringComparison.OrdinalIgnoreCase) ? 13m : 19m,
            new DateOnly(2020, 1, 1), null));

        // Ventas: lista de precios, categoría y cliente «consumidor final»
        var today = clock.TodayIn(r.TimeZoneId);
        var priceList = new PriceList(id, "GENERAL", currency.Id, new DateOnly(today.Year, 1, 1), null, isDefault: true);
        var customerCategory = new CustomerCategory(id, "GENERAL", "General", priceList.Id);
        db.AddRange(priceList, customerCategory, new Customer(id, "CF", "Consumidor final", null, null, null, customerCategory.Id));
        db.Add(new PosRegister(id, branch.Id, warehouse.Id, "CAJA01", "Caja 1", null));

        // Contabilidad mínima
        db.Add(new FiscalPeriod(id, (short)today.Year, (short)today.Month));
        db.Add(new CostCenter(id, "CM", r.BranchName, branch.Id));
        db.AddRange(ChartOfAccounts.CreateDefaults(id));

        // Módulos licenciados (por defecto, todos). En PostgreSQL el catálogo de módulos viene de la migración (HasData);
        // la base en memoria de la demostración no ejecuta migraciones, así que se completa aquí la primera vez.
        var knownModules = await db.Modules.Select(m => m.Id).ToListAsync(ct);
        db.AddRange(LicenseModule.Catalog().Where(m => !knownModules.Contains(m.Id)));
        var licensed = r.LicensedModules ?? LicenseModule.Catalog().Select(m => m.Code).ToList();
        foreach (var module in LicenseModule.Catalog().Where(m => licensed.Contains(m.Code)))
        {
            db.Add(new TenantModule(id, module.Id, now, null));
        }

        await db.SaveChangesAsync(ct);
        return new ProvisionedTenant(id, admin.Id, branch.Id, warehouse.Id, warehouse.Code, zone.Id, locationTypes[0].Id, bin.Id,
            currency.Id, roles.ToDictionary(k => k.Key, v => v.Value.Id), units.ToDictionary(k => k.Key, v => v.Value.Id));
    }
}
