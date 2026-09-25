using System.Text.Json;
using System.Text.RegularExpressions;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Importing.V21;

/// <summary>Datos del tenant de destino (ya aprovisionado) que necesita el plan de migración.</summary>
public sealed record V21Seeds(
    Guid TenantId,
    Guid AdminUserId,
    Guid BranchId,
    Guid WarehouseId,
    string WarehouseCode,
    Guid PickingLocationTypeId,
    Guid DefaultBinId,
    IReadOnlyDictionary<string, UnitOfMeasure> Units,
    IReadOnlyDictionary<string, Guid> Roles,
    IReadOnlyDictionary<string, MovementType> MovementTypes,
    IReadOnlyDictionary<string, User> ExistingUsers);

public sealed record V21ImportOptions(string TimeZoneId = "America/La_Paz", bool ImportOpenCount = true);

public sealed record V21ImportReport(
    int Users, int Categories, int Units, int Suppliers, int Products, int Bins, int Movements, int RejectedMovements,
    int ActivityRows, int CountLines, IReadOnlyList<string> Warnings);

/// <summary>Resultado del plan: entidades listas para guardar (en memoria) y lo necesario para verificar la paridad.</summary>
public sealed class V21ImportPlan
{
    public List<object> Entities { get; } = [];

    public Dictionary<string, (ProductVariant Variant, StockLevel Level, UnitRule Unit)> BySku { get; } = new(StringComparer.Ordinal);

    public List<(V21Movement Source, StockMovement Movement, MovementType Type, Guid VariantId)> Movements { get; } = [];

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public V21ImportReport? Report { get; internal set; }
}

/// <summary>
/// Convierte el libro colaborativo de la V2.1 en entidades de la V3 (sin tocar la base de datos, así se puede probar):
/// categorías, unidades, proveedores y contactos, usuarios y roles, productos (variante y lote por defecto, política de
/// mínimo/máximo, proveedor preferido, costo inicial), topología de posiciones desde «Ubicación», los movimientos
/// consolidados reproducidos en orden a través del dominio (poka-yoke incluido), los rechazados como auditoría, la
/// actividad (14_ACTIVIDAD) y la toma física en curso.
/// </summary>
public static partial class V21ImportPlanner
{
    public static V21ImportPlan Build(V21Workbook wb, V21Seeds seeds, V21ImportOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(wb);
        ArgumentNullException.ThrowIfNull(seeds);
        var plan = new V21ImportPlan();
        var t = seeds.TenantId;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);

        // Categorías (raíz: V2.1 no tenía jerarquía)
        var categories = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in wb.Categories())
        {
            var category = new Category(t, SafeCode(c.Code, 20), c.Name);
            categories[c.Name] = category;
            plan.Entities.Add(category);
            plan.Entities.AddRange(CategoryTree.ForNewCategory(category, []));
        }

        // Unidades (las de la V2.1 que no vengan sembradas)
        var units = new Dictionary<string, UnitOfMeasure>(seeds.Units, StringComparer.OrdinalIgnoreCase);
        var newUnits = 0;
        foreach (var u in wb.Units().Where(u => !units.ContainsKey(u.Code)))
        {
            var unit = new UnitOfMeasure(t, SafeCode(u.Code, 10), u.Name, u.AllowsDecimals, u.Description);
            units[u.Code] = unit;
            plan.Entities.Add(unit);
            newUnits++;
        }

        // Proveedores y su contacto principal
        var suppliers = new Dictionary<string, Supplier>(StringComparer.Ordinal);
        var n = 0;
        foreach (var s in wb.Suppliers())
        {
            var supplier = new Supplier(t, $"P{++n:000}", s.Name, s.TaxId, s.LeadTimeDays ?? 0);
            suppliers.TryAdd(s.Name, supplier);
            plan.Entities.Add(supplier);
            if (s.Contact is not null || s.Phone is not null || s.Email is not null)
            {
                plan.Entities.Add(new SupplierContact(t, supplier.Id, s.Contact ?? s.Name, s.Phone, ValidEmailOrNull(s.Email), isPrimary: true));
            }
        }

        // Usuarios y roles (las credenciales no existen en la V2.1: las asigna el administrador)
        var users = new Dictionary<string, User>(seeds.ExistingUsers, StringComparer.OrdinalIgnoreCase);
        var newUsers = 0;
        foreach (var u in wb.Users())
        {
            if (!users.ContainsKey(u.Email))
            {
                var user = new User(t, u.Email, u.Name.Length >= 2 ? u.Name : u.Email);
                if (!u.Active)
                {
                    user.Deactivate();
                }
                users[u.Email] = user;
                plan.Entities.Add(user);
                plan.Entities.Add(new BranchUser(t, seeds.BranchId, user.Id));
                newUsers++;
                if (seeds.Roles.TryGetValue(u.Role, out var roleId))
                {
                    plan.Entities.Add(new UserRole(t, user.Id, roleId));
                }
                else
                {
                    plan.Warnings.Add($"{u.Email}: rol «{u.Role}» desconocido (queda sin rol).");
                }
            }
        }
        if (newUsers > 0)
        {
            plan.Warnings.Add($"{newUsers} usuario(s) migrados sin contraseña: asígnela con «minv user password».");
        }

        // Topología de posiciones a partir de «Ubicación» (p. ej. A-01-01 → zona A, pasillo 01, estantería 01)
        var topology = new Topology(t, seeds);
        foreach (var p in wb.Products())
        {
            if (!categories.TryGetValue(p.Category, out var category))
            {
                category = new Category(t, SafeCode(p.Category, 20), p.Category);
                categories[p.Category] = category;
                plan.Entities.Add(category);
                plan.Entities.AddRange(CategoryTree.ForNewCategory(category, []));
            }
            if (!units.TryGetValue(p.Unit, out var unit))
            {
                plan.Errors.Add($"{p.Sku}: unidad {p.Unit} desconocida.");
                continue;
            }
            var product = Product.Create(t, p.Sku, p.Name, category.Id, unit.Id);
            if (!p.Active)
            {
                product.Deactivate();
            }
            var variant = product.DefaultVariant;
            var batch = Batch.CreateDefault(t, variant.Id);
            var bin = topology.BinFor(p.Location, plan);
            var level = StockLevel.Open(t, seeds.BranchId, bin, batch.Id);
            plan.Entities.AddRange([product, batch, level,
                new ProductStockPolicy(t, seeds.BranchId, variant.Id, seeds.WarehouseId, p.Minimum, p.Maximum),
                new BinAssignment(t, seeds.BranchId, bin, variant.Id, isPrimaryPick: true),
                new AverageCostHistory(t, seeds.BranchId, variant.Id, seeds.WarehouseId, 1, now, Math.Max(0, p.UnitCost), null)]);
            if (p.Supplier.Length > 0 && suppliers.TryGetValue(p.Supplier, out var supplier))
            {
                plan.Entities.Add(new ProductSupplier(t, product.Id, supplier.Id, null, null, isPreferred: true));
            }
            else if (p.Supplier.Length > 0)
            {
                plan.Warnings.Add($"{p.Sku}: el proveedor «{p.Supplier}» no está en 04_PROVEEDORES.");
            }
            plan.BySku[p.Sku] = (variant, level, new UnitRule(unit.Code, unit.AllowsDecimals));
        }

        // Movimientos: consolidados → StockMovements por el dominio (en orden); rechazados → auditoría
        var levelsWithMovements = new HashSet<Guid>();
        var rejected = 0;
        foreach (var m in wb.Movements())
        {
            var when = ToUtc(m.TimestampSerial, zone);
            var userId = users.TryGetValue(m.UserEmail, out var who) ? who.Id : (Guid?)null;
            var script = m.IsSalesFragment ? "RegistrarSalida" : "RegistrarEntrada";
            if (!m.IsConsolidated)
            {
                rejected++;
                plan.Entities.Add(new AuditLog(t, userId, when, script, AuditOutcome.Rejected, "StockMovement", null,
                    JsonSerializer.Serialize(new { v21 = m.Id, tipo = m.Type, sku = m.Sku, cantidad = m.Quantity, estado = m.Status }),
                    UuidV7.NewGuid(when), m.Id));
                continue;
            }
            if (!plan.BySku.TryGetValue(m.Sku, out var target))
            {
                plan.Errors.Add($"{m.Id}: el SKU {m.Sku} no existe en el catálogo.");
                continue;
            }
            try
            {
                var type = seeds.MovementTypes[MovementTypeCodes.FromV21(m.Type)];
                StockLevel.EnsureInitialBalanceAllowed(type, levelsWithMovements.Contains(target.Level.Id));
                var context = new MovementContext(userId ?? seeds.AdminUserId, m.BusinessDate, when, m.Document, m.Notes,
                    LegacyReference: m.Id);
                var movement = target.Level.Register(type, m.Quantity, target.Unit, context);
                levelsWithMovements.Add(target.Level.Id);
                plan.Entities.Add(movement);
                plan.Movements.Add((m, movement, type, target.Variant.Id));
            }
            catch (DomainException ex)
            {
                plan.Errors.Add($"{m.Id} ({m.Type} {m.Quantity} {m.Sku}): {ex.Message}");
            }
        }

        // Actividad (14_ACTIVIDAD) → auditoría inmutable
        var activity = 0;
        foreach (var a in wb.Activity())
        {
            var when = ToUtc(a.TimestampSerial, zone);
            var outcome = a.Result.StartsWith('✔') ? AuditOutcome.Succeeded : a.Result.StartsWith('✖') ? AuditOutcome.Rejected : AuditOutcome.Failed;
            plan.Entities.Add(new AuditLog(t, users.TryGetValue(a.UserEmail, out var who) ? who.Id : null, when,
                Truncate(a.Script.Length > 0 ? a.Script : "V2.1", 100), outcome, null, null,
                JsonSerializer.Serialize(new { resultado = a.Result, detalle = a.Detail }), UuidV7.NewGuid(when), a.Id));
            activity++;
        }

        // Toma física en curso (13_CONTEO) → toma abierta con sus conteos
        var countLines = 0;
        var counts = wb.Counts();
        if (options.ImportOpenCount && counts.Count > 0)
        {
            var date = wb.SnapshotSerial is { } s ? DateOnly.FromDateTime(XlsxTableReader.FromSerial(Math.Floor(s))) : DateOnly.FromDateTime(now.UtcDateTime);
            var count = PhysicalCount.Open(t, seeds.BranchId, seeds.WarehouseId, date, 1, "Toma física en curso migrada de la V2.1");
            foreach (var c in counts)
            {
                if (!plan.BySku.TryGetValue(c.Sku, out var target))
                {
                    plan.Warnings.Add($"Conteo de {c.Sku}: el SKU no existe (se omite).");
                    continue;
                }
                if (!target.Unit.AllowsDecimals && !Quantities.IsWhole(c.Counted))
                {
                    plan.Warnings.Add($"Conteo de {c.Sku}: {c.Counted} no es válido en {target.Unit.UnitCode} (se omite).");
                    continue;
                }
                count.RecordCount(target.Level.Id, c.Counted, seeds.AdminUserId, now);
                countLines++;
            }
            if (countLines > 0)
            {
                plan.Entities.Add(count);
            }
        }

        plan.Report = new V21ImportReport(newUsers, categories.Count, newUnits, suppliers.Count, plan.BySku.Count, topology.Bins,
            plan.Movements.Count, rejected, activity, countLines, plan.Warnings);
        return plan;
    }

    /// <summary>Serial local de Excel (Timestamp de la V2.1) → instante UTC con la zona horaria de la empresa.</summary>
    public static DateTimeOffset ToUtc(double serial, TimeZoneInfo zone)
    {
        var local = XlsxTableReader.FromSerial(serial);
        local = new DateTime(local.Ticks - local.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static string SafeCode(string value, int max)
    {
        var code = InvalidCodeChars().Replace(value.Trim().ToUpperInvariant(), "_").Trim('_', '-');
        if (code.Length == 0)
        {
            code = "X";
        }
        return code.Length <= max ? code : code[..max];
    }

    private static string? ValidEmailOrNull(string? email) =>
        email is not null && EmailLike().IsMatch(email.Trim()) ? email.Trim().ToLowerInvariant() : null;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    [GeneratedRegex("[^A-Z0-9_-]+")]
    private static partial Regex InvalidCodeChars();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailLike();

    /// <summary>Crea zonas, pasillos, estanterías, niveles y posiciones bajo demanda (sin duplicados).</summary>
    private sealed class Topology(Guid tenantId, V21Seeds seeds)
    {
        private readonly Dictionary<string, Guid> _zones = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Guid> _aisles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Guid> _racks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Guid> _shelves = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Guid> _bins = new(StringComparer.Ordinal);

        public int Bins => _bins.Count;

        public Guid BinFor(string location, V21ImportPlan plan)
        {
            var loc = SafeCode(location, 30);
            if (location.Trim().Length == 0)
            {
                return seeds.DefaultBinId;
            }
            if (_bins.TryGetValue(loc, out var existing))
            {
                return existing;
            }
            var parts = loc.Split('-', StringSplitOptions.RemoveEmptyEntries);
            string Part(int i) => i < parts.Length ? SafeCode(parts[i], 20) : "00";
            var zoneKey = Part(0);
            if (!_zones.TryGetValue(zoneKey, out var zoneId))
            {
                var z = new Zone(tenantId, seeds.BranchId, seeds.WarehouseId, zoneKey, $"Zona {zoneKey}", seeds.PickingLocationTypeId);
                plan.Entities.Add(z);
                _zones[zoneKey] = zoneId = z.Id;
            }
            var aisleKey = zoneKey + "/" + Part(1);
            if (!_aisles.TryGetValue(aisleKey, out var aisleId))
            {
                var a = new Aisle(tenantId, seeds.BranchId, zoneId, Part(1));
                plan.Entities.Add(a);
                _aisles[aisleKey] = aisleId = a.Id;
            }
            var rackKey = aisleKey + "/" + Part(2);
            if (!_racks.TryGetValue(rackKey, out var rackId))
            {
                var r = new Rack(tenantId, seeds.BranchId, aisleId, Part(2));
                plan.Entities.Add(r);
                _racks[rackKey] = rackId = r.Id;
            }
            var shelfKey = rackKey + "/" + Part(3);
            if (!_shelves.TryGetValue(shelfKey, out var shelfId))
            {
                var s = new Shelf(tenantId, seeds.BranchId, rackId, Part(3));
                plan.Entities.Add(s);
                _shelves[shelfKey] = shelfId = s.Id;
            }
            var bin = new Bin(tenantId, seeds.BranchId, shelfId, SafeCode($"{seeds.WarehouseCode}-{loc}", 40), seeds.PickingLocationTypeId, _bins.Count + 1);
            plan.Entities.Add(bin);
            _bins[loc] = bin.Id;
            return bin.Id;
        }
    }
}
