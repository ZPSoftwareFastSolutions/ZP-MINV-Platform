using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Accounting;
using MINV.Application.Catalog;
using MINV.Application.Common;
using MINV.Application.Corporate;
using MINV.Application.Iam;
using MINV.Application.Integration;
using MINV.Application.Inventory.Movements;
using MINV.Application.Inventory.PhysicalCounts;
using MINV.Application.Inventory.Queries;
using MINV.Application.Inventory.Transfers;
using MINV.Application.Partners;
using MINV.Application.Purchasing;
using MINV.Application.Sales;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;
using MINV.Infrastructure.Integration;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Provisioning;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Seeding;

public sealed record SeedOptions(string TenantCode = "MINV", string CompanyName = "Ferretería El Constructor S.R.L.", string TaxId = "1029384756",
    string Domain = "elconstructor.example", int Days = 60, int Seed = 2026, string TimeZoneId = "America/La_Paz");

public sealed record SeedUser(string RoleCode, string RoleName, string Name, string Email, string Password, string Branches = "");

public sealed record SeedResult(string TenantCode, string CompanyName, IReadOnlyList<SeedUser> Users, int Products, int Suppliers, int Customers,
    int Tickets, int PurchaseOrders, int Movements, int JournalEntries, DateOnly From, DateOnly To, IReadOnlyList<string> Branches, int Transfers,
    int ExternalOrders, string ApiKeyName, string ApiKeyToken, string? WebhookSecret);

/// <summary>
/// Datos de prueba aleatorios (reproducibles con la semilla) para la base LOCAL o la de la NUBE: empresa, usuarios de cada
/// rol con contraseña y sucursales asignadas, categorías, posiciones, proveedores, clientes, catálogo con imágenes y
/// precios, y N días de operación simulada. V4: tres sucursales (casa matriz, El Alto y Santa Cruz) con su caja; la casa
/// matriz compra y repone a las otras con transferencias semanales (despacho → mercadería en tránsito → recepción, con
/// faltantes ocasionales), cada sucursal vende en su caja, el e-commerce registra pedidos por la API (API Key) y quedan
/// transferencias pendientes y en tránsito para explorar. TODO pasa por los mismos casos de uso de la aplicación
/// (validación, permisos, alcance por sucursal, poka-yoke, auditoría y contabilidad): coherente por construcción.
/// </summary>
public sealed class LocalDataSeeder(IServiceProvider services, DemoClock clock)
{
    private static readonly string[] FirstNames =
    [
        "María", "José", "Ana", "Luis", "Carla", "Jorge", "Lucía", "Diego", "Valeria", "Miguel", "Sofía", "Andrés", "Camila", "Rodrigo",
        "Paola", "Fernando", "Daniela", "Marcelo", "Gabriela", "Ricardo", "Natalia", "Sergio", "Mariana", "Álvaro",
    ];

    private static readonly string[] LastNames =
    [
        "Quispe", "Mamani", "Fernández", "Rojas", "Vargas", "Gutiérrez", "Flores", "Choque", "Mendoza", "Castro", "Ortiz", "Salazar",
        "Paredes", "Rivera", "Torrez", "Morales", "Aguilar", "Céspedes", "Villarroel", "Suárez",
    ];

    private static readonly string[] PasswordWords =
    [
        "Condor", "Illimani", "Titicaca", "Sajama", "Uyuni", "Llama", "Vicuna", "Quinua", "Mirador", "Tunari", "Andes", "Salar", "Amboro",
        "Madidi", "Tiwanaku", "Samaipata",
    ];

    private static readonly (string Code, string Name, string Zone)[] Categories =
    [
        ("FER", "Ferretería", "A"), ("ELE", "Eléctricos", "B"), ("PLO", "Plomería", "C"), ("PIN", "Pinturas", "D"),
        ("SEG", "Seguridad industrial", "E"), ("ASE", "Aseo y limpieza", "F"), ("CON", "Construcción", "G"), ("HEL", "Herramientas eléctricas", "H"),
    ];

    private static readonly (string Name, string Contact, int LeadTime, string[] Categories)[] Suppliers =
    [
        ("Distribuidora Andina de Ferretería S.A.", "Rubén Céspedes", 3, ["FER"]),
        ("Electro Sur S.R.L.", "Paula Rincón", 2, ["ELE"]),
        ("Hidrotubos Bolivia Ltda.", "Andrés Mejía", 4, ["PLO"]),
        ("Pinturas del Altiplano S.A.", "Marcela Ortiz", 5, ["PIN"]),
        ("Protección Industrial Illimani S.R.L.", "Jaime Salinas", 6, ["SEG"]),
        ("Químicos Limpios S.A.", "Rosa Aliaga", 3, ["ASE"]),
        ("Materiales de Construcción El Alto", "Víctor Huanca", 2, ["CON"]),
        ("Power Tools Import S.R.L.", "Iván Arce", 7, ["HEL"]),
    ];

    /// <summary>Categoría, nombre, unidad, costo (Bs), mínimo, máximo y popularidad (1 a 5).</summary>
    private static readonly (string Cat, string Name, string Unit, decimal Cost, int Min, int Max, int Pop)[] Products =
    [
        ("FER", "Tornillo drywall 6x1\" (caja x100)", "CAJA", 18.5m, 10, 60, 5), ("FER", "Tornillo autorroscante 8x3/4\" (caja x100)", "CAJA", 22m, 8, 50, 4),
        ("FER", "Clavo de acero 2\" (kilo)", "KG", 14m, 15, 80, 4), ("FER", "Martillo carpintero 16 oz", "UND", 48m, 4, 20, 3),
        ("FER", "Candado de seguridad 40 mm", "UND", 35m, 5, 25, 3), ("FER", "Cinta métrica 5 m", "UND", 22m, 6, 30, 4),
        ("FER", "Serrucho de 20\"", "UND", 55m, 3, 12, 2), ("FER", "Llave inglesa 10\"", "UND", 62m, 3, 15, 2),
        ("FER", "Juego de destornilladores (6 piezas)", "UND", 45m, 4, 18, 3), ("FER", "Tarugo plástico 1/4\" (bolsa x100)", "PAQ", 9m, 10, 60, 4),
        ("FER", "Nivel de burbuja 24\"", "UND", 38m, 3, 12, 2),
        ("ELE", "Cable THHN 12 AWG (rollo 100 m)", "ROLLO", 420m, 3, 15, 3), ("ELE", "Cable dúplex 2x14 AWG (metro)", "MT", 4.2m, 100, 600, 5),
        ("ELE", "Foco LED 9 W luz blanca", "UND", 11m, 20, 120, 5), ("ELE", "Foco LED 12 W luz cálida", "UND", 14m, 15, 90, 4),
        ("ELE", "Tomacorriente doble", "UND", 12m, 15, 80, 4), ("ELE", "Interruptor simple", "UND", 9.5m, 15, 80, 4),
        ("ELE", "Cinta aislante negra 3/4\"", "UND", 5m, 20, 100, 5), ("ELE", "Breaker enchufable 1x20 A", "UND", 38m, 6, 30, 3),
        ("ELE", "Extensión eléctrica 5 m", "UND", 42m, 5, 25, 3), ("ELE", "Reflector LED 50 W", "UND", 95m, 3, 15, 2),
        ("PLO", "Tubo PVC 1/2\" (6 m)", "UND", 24m, 15, 80, 4), ("PLO", "Codo PVC 1/2\" x 90°", "UND", 2.5m, 40, 250, 5),
        ("PLO", "Llave de paso 1/2\" de bronce", "UND", 38m, 6, 30, 3), ("PLO", "Grifo de cocina cromado", "UND", 120m, 2, 10, 2),
        ("PLO", "Cinta teflón 3/4\"", "UND", 3m, 30, 150, 5), ("PLO", "Pegamento PVC 250 ml", "UND", 26m, 8, 40, 3),
        ("PLO", "Manguera de jardín 15 m", "UND", 85m, 3, 12, 2),
        ("PIN", "Pintura látex blanca (galón)", "GL", 68m, 10, 50, 5), ("PIN", "Esmalte sintético negro (galón)", "GL", 92m, 5, 25, 3),
        ("PIN", "Pintura anticorrosiva roja (galón)", "GL", 98m, 4, 20, 2), ("PIN", "Brocha de 2\"", "UND", 12m, 10, 60, 4),
        ("PIN", "Brocha de 4\"", "UND", 21m, 8, 40, 3), ("PIN", "Rodillo de lana 9\"", "UND", 28m, 6, 30, 3),
        ("PIN", "Thinner acrílico (litro)", "LT", 16m, 10, 60, 4), ("PIN", "Lija al agua #120", "UND", 3.5m, 30, 150, 4),
        ("SEG", "Casco de seguridad blanco", "UND", 45m, 5, 25, 3), ("SEG", "Guantes de nitrilo (par)", "PAR", 12m, 20, 100, 5),
        ("SEG", "Guantes de cuero (par)", "PAR", 28m, 8, 40, 3), ("SEG", "Botas de seguridad punta de acero (par)", "PAR", 210m, 3, 15, 2),
        ("SEG", "Gafas de protección", "UND", 18m, 8, 40, 3), ("SEG", "Tapabocas N95 (caja x20)", "CAJA", 95m, 4, 20, 3),
        ("SEG", "Chaleco reflectivo", "UND", 32m, 6, 30, 2), ("SEG", "Extintor PQS 6 kg", "UND", 280m, 2, 8, 1),
        ("ASE", "Hipoclorito de sodio 5% (galón)", "GL", 22m, 15, 80, 5), ("ASE", "Detergente en polvo (kilo)", "KG", 17m, 15, 80, 4),
        ("ASE", "Bolsa de basura negra 90x110 (paquete x10)", "PAQ", 11m, 20, 120, 5), ("ASE", "Escoba plástica", "UND", 19m, 6, 30, 3),
        ("ASE", "Trapeador de algodón", "UND", 24m, 6, 30, 3), ("ASE", "Desinfectante multiuso (litro)", "LT", 14m, 12, 60, 4),
        ("ASE", "Jabón líquido para manos (litro)", "LT", 18m, 10, 50, 3),
        ("CON", "Cemento Portland (bolsa 50 kg)", "UND", 58m, 40, 200, 5), ("CON", "Yeso (bolsa 25 kg)", "UND", 32m, 15, 80, 3),
        ("CON", "Escalera de aluminio 6 peldaños", "UND", 380m, 2, 8, 1), ("CON", "Carretilla 90 L", "UND", 320m, 2, 8, 1),
        ("CON", "Pala punta redonda", "UND", 55m, 4, 20, 2),
        ("HEL", "Taladro percutor 650 W", "UND", 360m, 2, 10, 2), ("HEL", "Amoladora angular 4-1/2\"", "UND", 310m, 2, 10, 2),
        ("HEL", "Disco de corte metal 4-1/2\"", "UND", 9m, 30, 150, 5), ("HEL", "Juego de brocas (10 piezas)", "UND", 65m, 4, 20, 3),
        ("HEL", "Sierra caladora 500 W", "UND", 420m, 1, 6, 1),
    ];

    /// <summary>V4 · Sucursales de la empresa de prueba (la casa matriz la crea el aprovisionamiento).</summary>
    public const string BranchMain = "CM";
    public const string BranchElAlto = "EA";
    public const string BranchSantaCruz = "SC";

    private Random _rng = new(2026);

    public async Task<SeedResult> SeedAsync(SeedOptions o, Action<string> log, CancellationToken ct = default)
    {
        _rng = new Random(o.Seed);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(o.TimeZoneId);
        var realNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var today = DateOnly.FromDateTime(realNow.DateTime);
        var start = today.AddDays(-o.Days);
        void At(DateOnly day, int hour, int minute) =>
            clock.StartAt(new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), zone.GetUtcOffset(day.ToDateTime(new TimeOnly(hour, minute)))));

        // ------------------------------------------------------------------------------------ empresa y administrador
        At(start.AddDays(-1), 8, 0);
        var users = new List<SeedUser>();
        var adminPassword = NewPassword();
        users.Add(new SeedUser(RoleCodes.Admin, "Administrador", "Administrador General", $"admin@{o.Domain}", adminPassword, "Todas"));
        using (var scope = services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().ProvisionAsync(new ProvisionTenantRequest(o.TenantCode, o.CompanyName,
                o.TaxId, users[0].Email, users[0].Name, adminPassword, "BOB", o.TimeZoneId, "BO", "Bolivia", "LP", "La Paz", "La Paz",
                "Casa matriz · Av. 6 de Agosto", "Almacén central", 0.20m, 60, start.AddDays(-1)), ct);
        }
        log($"Empresa {o.TenantCode} · {o.CompanyName} creada (período {start:dd/MM/yyyy} a {today:dd/MM/yyyy}).");

        using var admin = await SignInAsync(o.TenantCode, users[0].Email, adminPassword, ct);

        // ------------------------------------------------------------------------------------ V4 · sucursales
        await admin.Send(new CreateBranchCommand(BranchElAlto, "Sucursal El Alto", "ALMEA", "Almacén El Alto"), ct);
        await admin.Send(new CreateBranchCommand(BranchSantaCruz, "Sucursal Santa Cruz", "ALMSC", "Almacén Santa Cruz"), ct);
        log("3 sucursales: CM · Casa matriz (almacén central), EA · El Alto y SC · Santa Cruz, cada una con su caja.");

        // ------------------------------------------------------------------------------------ usuarios por rol y sucursal
        var plan = new (string Role, string RoleName, string[] Branches)[]
        {
            (RoleCodes.Management, "Gerencia", [BranchMain]),
            (RoleCodes.Warehouse, "Bodega", [BranchMain]), (RoleCodes.Warehouse, "Bodega", [BranchElAlto]), (RoleCodes.Warehouse, "Bodega", [BranchSantaCruz]),
            (RoleCodes.Sales, "Ventas", [BranchMain]), (RoleCodes.Sales, "Ventas", [BranchSantaCruz]),
            (RoleCodes.Cashier, "Cajero", [BranchMain]), (RoleCodes.Cashier, "Cajero", [BranchMain]), (RoleCodes.Cashier, "Cajero", [BranchElAlto]),
            (RoleCodes.Cashier, "Cajero", [BranchSantaCruz]),
            (RoleCodes.ReadOnly, "Consulta", [BranchMain, BranchElAlto, BranchSantaCruz]),
        };
        var usedNames = new HashSet<string>();
        foreach (var (role, roleName, branches) in plan)
        {
            string name;
            do
            {
                name = $"{Pick(FirstNames)} {Pick(LastNames)}";
            }
            while (!usedNames.Add(name));
            var email = $"{Slug(name)}@{o.Domain}";
            var password = NewPassword();
            await admin.Send(new SaveUserCommand(null, email, name, role, true, password, branches), ct);
            await admin.Send(new ResetUserPasswordCommand(email, password, MustChange: false), ct);
            users.Add(new SeedUser(role, roleName, name, email, password,
                role == RoleCodes.Management ? "Todas (gerencia global)" : string.Join(", ", branches)));
        }
        log($"{users.Count} usuarios creados (uno o más por rol, asignados a sus sucursales).");

        // ------------------------------------------------------------------------------------ datos maestros
        var db = admin.Scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var tenantId = admin.Scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId;
        var warehouse = await db.Warehouses.FirstAsync(w => w.Code == TenantProvisioner.WarehouseCode, ct);
        var picking = await db.LocationTypes.FirstAsync(t => t.Code == "PICKING", ct);
        var priceList = await db.PriceLists.FirstAsync(p => p.IsDefault, ct);
        db.CustomerCategories.AddRange(new CustomerCategory(tenantId, "MAYORISTA", "Mayorista", priceList.Id),
            new CustomerCategory(tenantId, "EMPRESA", "Empresa / constructora", priceList.Id));
        db.POSRegisters.AddRange(new PosRegister(tenantId, warehouse.BranchId, warehouse.Id, "CAJA02", "Caja 2", null),
            new PosRegister(tenantId, warehouse.BranchId, warehouse.Id, "CAJA03", "Caja 3 (mostrador)", null));
        var binsByCategory = new Dictionary<string, List<string>>();
        foreach (var (code, name, zoneCode) in Categories)
        {
            var z = new Zone(tenantId, warehouse.BranchId, warehouse.Id, zoneCode, name, picking.Id);
            var aisle = new Aisle(tenantId, warehouse.BranchId, z.Id, "01");
            db.AddRange(z, aisle);
            var list = new List<string>();
            for (var r = 1; r <= 3; r++)
            {
                var rack = new Rack(tenantId, warehouse.BranchId, aisle.Id, r.ToString("00", CultureInfo.InvariantCulture));
                var shelf = new Shelf(tenantId, warehouse.BranchId, rack.Id, "01");
                var binCode = $"{warehouse.Code}-{zoneCode}-01-{r:00}";
                db.AddRange(rack, shelf, new Bin(tenantId, warehouse.BranchId, shelf.Id, binCode, picking.Id, list.Count + 1));
                list.Add(binCode);
            }
            binsByCategory[code] = list;
        }
        await db.SaveChangesAsync(ct);
        foreach (var (code, name, _) in Categories)
        {
            await admin.Send(new SaveCategoryCommand(code, name), ct);
        }
        var supplierByCategory = new Dictionary<string, string>();
        foreach (var s in Suppliers)
        {
            var phone = $"7{_rng.Next(1000000, 9999999)}";
            var code = await admin.Send(new SaveSupplierCommand(null, s.Name, $"{_rng.Next(100000000, 999999999)}01", s.LeadTime, s.Contact, phone,
                $"ventas@{Slug(s.Name.Split(' ')[0] + " " + s.Name.Split(' ')[1])}.example", true), ct);
            foreach (var c in s.Categories)
            {
                supplierByCategory[c] = code;
            }
        }
        var customers = new List<string> { "CF" };
        for (var i = 0; i < 28; i++)
        {
            var company = i % 3 == 0;
            var name = company
                ? $"{Pick(["Constructora", "Inmobiliaria", "Servicios", "Comercial", "Taller"])} {Pick(LastNames)} {Pick(["S.R.L.", "Ltda.", "S.A."])}"
                : $"{Pick(FirstNames)} {Pick(LastNames)} {Pick(LastNames)}";
            var category = company ? (i % 2 == 0 ? "EMPRESA" : "MAYORISTA") : "GENERAL";
            var email = company ? $"compras@{Slug(name.Split(' ')[1])}{i}.example" : $"{Slug(name.Split(' ')[0] + " " + name.Split(' ')[1])}{i}@correo.example";
            customers.Add(await admin.Send(new SaveCustomerCommand(null, name, company ? $"{_rng.Next(1000000, 9999999)}01{i}" : $"{_rng.Next(3000000, 9999999)}",
                email, $"6{_rng.Next(1000000, 9999999)}", category, true), ct));
        }
        log($"{Categories.Length} categorías, {Suppliers.Length} proveedores, {customers.Count} clientes y {binsByCategory.Values.Sum(b => b.Count)} posiciones.");

        // ------------------------------------------------------------------------------------ catálogo con imágenes y saldo inicial
        var catalog = new List<(string Sku, string Unit, int Pop, decimal Cost, int Min, int Max)>();
        var counters = new Dictionary<string, int>();
        var opening = 0m;
        foreach (var p in Products)
        {
            counters[p.Cat] = counters.GetValueOrDefault(p.Cat) + 1;
            var sku = $"{p.Cat}-{counters[p.Cat]:000}";
            // Precio de venta con IVA incluido: 45 % a 80 % sobre el costo (margen bruto neto de IVA de 22 % a 37 %)
            var price = decimal.Round(p.Cost * (1.45m + (decimal)_rng.NextDouble() * 0.35m), 1, MidpointRounding.AwayFromZero);
            var bins = binsByCategory[p.Cat];
            var barcode = Ean13("777" + _rng.Next(100000000, 999999999).ToString(CultureInfo.InvariantCulture));
            await admin.Send(new SaveProductCommand(null, sku, p.Name, $"{p.Name}. Producto de prueba generado para M-INV.", p.Cat, p.Unit,
                supplierByCategory[p.Cat], p.Min, p.Max, p.Cost, price, barcode, true, bins[_rng.Next(bins.Count)]), ct);
            await admin.Send(new SetProductImageCommand(sku, ProductImageLibrary.ForProduct(p.Name), "image/png", ProductImageLibrary.KindFor(p.Name) + ".png"), ct);
            catalog.Add((sku, p.Unit, p.Pop, p.Cost, p.Min, p.Max));
        }
        At(start, 7, 30);
        foreach (var (sku, unit, pop, cost, min, max) in catalog)
        {
            // Algunos productos arrancan bajos o agotados para que haya alertas y pedido sugerido
            var roll = _rng.NextDouble();
            // La casa matriz abastece también a las sucursales: arranca con más existencia
            var quantity = roll < 0.06 ? 0 : roll < 0.18 ? Math.Max(1, min * 0.7) : max * (0.9 + _rng.NextDouble() * 0.9);
            var q = Qty(unit, (decimal)quantity);
            if (q <= 0)
            {
                continue;
            }
            var bin = await BinOfAsync(db, sku, ct);
            await admin.Send(new RegisterMovementCommand(sku, bin, MovementTypeCodes.InitialBalance, q, start, "INV-INICIAL", "Inventario inicial"), ct);
            opening += q * cost;
        }
        await admin.Send(new CreateJournalEntryCommand(start, "Asiento de apertura: aporte de capital en inventario, banco y caja",
        [
            new JournalLineSpec(AccountCodes.Inventory, JournalPoster.Money(opening), 0),
            new JournalLineSpec(AccountCodes.Bank, 85000, 0),
            new JournalLineSpec(AccountCodes.Cash, 3000, 0),
            new JournalLineSpec(AccountCodes.Capital, 0, JournalPoster.Money(opening) + 88000),
        ]), ct);
        log($"{catalog.Count} productos con imagen, precio y saldo inicial (Bs {opening:N2} en inventario).");

        // V4 · API Key del e-commerce (el token se entrega una sola vez; queda en el archivo local de credenciales)
        var apiKey = await admin.Send(new CreateApiKeyCommand("Tienda en línea", [ApiScopes.CatalogRead, ApiScopes.StockRead, ApiScopes.OrdersWrite],
            BranchMain), ct);

        // ------------------------------------------------------------------------------------ operación diaria simulada
        var cashiers = users.Where(u => u.RoleCode == RoleCodes.Cashier).ToList();
        var keepers = users.Where(u => u.RoleCode == RoleCodes.Warehouse).ToList();
        var manager = users.First(u => u.RoleCode == RoleCodes.Management);
        using var s1 = await SignInAsync(o.TenantCode, cashiers[0].Email, cashiers[0].Password, ct);
        using var s2 = await SignInAsync(o.TenantCode, cashiers[1].Email, cashiers[1].Password, ct);
        using var sEa = await SignInAsync(o.TenantCode, cashiers[2].Email, cashiers[2].Password, ct);
        using var sSc = await SignInAsync(o.TenantCode, cashiers[3].Email, cashiers[3].Password, ct);
        using var bodega = await SignInAsync(o.TenantCode, keepers[0].Email, keepers[0].Password, ct);
        using var bodegaEa = await SignInAsync(o.TenantCode, keepers[1].Email, keepers[1].Password, ct);
        using var bodegaSc = await SignInAsync(o.TenantCode, keepers[2].Email, keepers[2].Password, ct);
        using var gerencia = await SignInAsync(o.TenantCode, manager.Email, manager.Password, ct);
        using var tienda = await ApiSignInAsync(apiKey.Token, ct);
        var registers = new[]
        {
            (Session: s1, Register: "CAJA01", Branch: BranchMain), (Session: s2, Register: "CAJA02", Branch: BranchMain),
            (Session: sEa, Register: $"{BranchElAlto}-CAJA1", Branch: BranchElAlto), (Session: sSc, Register: $"{BranchSantaCruz}-CAJA1", Branch: BranchSantaCruz),
        };
        var weighted = catalog.SelectMany(c => Enumerable.Repeat(c, c.Pop * c.Pop)).ToList();
        var tickets = 0;
        var transfers = 0;
        var webOrders = 0;
        var lastInvoice = (string?)null;

        // Reposición de una sucursal: la casa matriz arma la transferencia con lo que tiene disponible y la despacha;
        // la sucursal la recibe (a veces con un faltante que se registra como merma en tránsito)
        async Task<TransferRef?> ShipAsync(string warehouseCode, int items, double share)
        {
            var lines = new List<TransferLineInput>();
            foreach (var item in weighted.OrderBy(_ => _rng.Next()).DistinctBy(x => x.Sku).Take(items * 2))
            {
                var available = await AvailableAsync(db, item.Sku, warehouse.Id, ct);
                var qty = Qty(item.Unit, Math.Max(item.Unit is "KG" or "MT" or "LT" or "GL" ? 1 : 2, (decimal)(item.Max * share)));
                if (available >= qty * 2 && lines.Count < items)
                {
                    lines.Add(new TransferLineInput(item.Sku, qty));
                }
            }
            if (lines.Count == 0)
            {
                return null;
            }
            var created = await bodega.Send(new CreateTransferCommand(warehouseCode, lines, "Reposición semanal de la sucursal"), ct);
            await bodega.Send(new DispatchTransferCommand(created.Id), ct);
            transfers++;
            return created;
        }

        async Task ReceiveAsync(SignedIn receiver, Guid transferId)
        {
            var detail = await receiver.Send(new GetTransferQuery(transferId), ct);
            var receipt = new List<TransferReceiptInput>();
            if (_rng.NextDouble() < 0.25)
            {
                var line = detail.Lines[_rng.Next(detail.Lines.Count)];
                receipt.Add(new TransferReceiptInput(line.Sku, Math.Max(0, line.Quantity - 1),
                    Pick(["Caja dañada en el camión", "Faltante detectado al contar la recepción", "Producto roto en el traslado"])));
            }
            await receiver.Send(new ReceiveTransferCommand(transferId, receipt), ct);
        }

        // Primera distribución: la casa matriz abastece a El Alto y Santa Cruz el día de apertura
        At(start, 9, 0);
        var firstEa = await ShipAsync("ALMEA", 30, 0.35);
        var firstSc = await ShipAsync("ALMSC", 30, 0.35);
        At(start, 16, 30);
        if (firstEa is not null)
        {
            await ReceiveAsync(bodegaEa, firstEa.Id);
        }
        if (firstSc is not null)
        {
            await ReceiveAsync(bodegaSc, firstSc.Id);
        }
        var pendingReceipts = new List<(DateOnly Day, SignedIn Receiver, Guid Id)>();
        for (var day = start.AddDays(1); day <= today; day = day.AddDays(1))
        {
            if (day.DayOfWeek == DayOfWeek.Sunday)
            {
                continue;
            }
            var isToday = day == today;
            var lastHour = isToday ? Math.Clamp(realNow.Hour, 9, 19) : 19;

            // Recepciones pendientes (llegan en su fecha estimada)
            At(day, 8, 40);
            foreach (var order in await bodega.Send(new GetPurchaseOrdersQuery(PurchaseOrderStatus.Approved), ct))
            {
                if (order.ExpectedDate <= day)
                {
                    await bodega.Send(new ReceivePurchaseOrderCommand(order.Id, $"FAC-{_rng.Next(10000, 99999)}"), ct);
                }
            }

            // V4 · Recepciones de transferencias despachadas el día anterior (la mercadería estuvo en tránsito)
            At(day, 8, 50);
            foreach (var pending in pendingReceipts.Where(x => x.Day <= day).ToList())
            {
                await ReceiveAsync(pending.Receiver, pending.Id);
                pendingReceipts.Remove(pending);
            }
            // V4 · Reposición semanal: lunes a El Alto, miércoles a Santa Cruz (llegan al día siguiente)
            if (!isToday && day.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday)
            {
                At(day, 15, 0);
                var toEa = day.DayOfWeek == DayOfWeek.Monday;
                var shipped = await ShipAsync(toEa ? "ALMEA" : "ALMSC", _rng.Next(6, 11), 0.2);
                if (shipped is not null)
                {
                    pendingReceipts.Add((day.AddDays(1), toEa ? bodegaEa : bodegaSc, shipped.Id));
                }
            }
            // V4 · Pedidos del e-commerce por la API (martes y viernes), despachados desde la casa matriz
            if (day.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Friday)
            {
                for (var n = _rng.Next(1, 4); n > 0; n--)
                {
                    At(day, _rng.Next(10, 18), _rng.Next(0, 59));
                    var lines = Enumerable.Range(0, _rng.Next(1, 4)).Select(_ => weighted[_rng.Next(weighted.Count)]).DistinctBy(x => x.Sku)
                        .Select(x => new SaleLineInput(x.Sku, SaleQty(x.Unit, x.Pop))).ToList();
                    try
                    {
                        await tienda.Send(new CreateExternalOrderCommand($"WEB-{day:yyyyMMdd}-{n}", customers[_rng.Next(1, customers.Count)], "QR", lines,
                            $"QR-{_rng.Next(100000, 999999)}"), ct);
                        webOrders++;
                    }
                    catch (DomainException)
                    {
                        // sin stock: la tienda recibe el rechazo (poka-yoke) y el pedido no se registra
                    }
                }
            }

            // Pedido sugerido: bodega arma las órdenes los lunes y jueves; gerencia las aprueba
            if (day.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Thursday && !isToday)
            {
                At(day, 9, 10);
                try
                {
                    await bodega.Send(new CreateSuggestedPurchaseOrdersCommand(), ct);
                }
                catch (DomainException)
                {
                    // nada nuevo que pedir
                }
                At(day, 10, 5);
                foreach (var draft in await gerencia.Send(new GetPurchaseOrdersQuery(PurchaseOrderStatus.Draft), ct))
                {
                    await gerencia.Send(new ApprovePurchaseOrderCommand(draft.Id), ct);
                }
            }

            // Ventas en dos cajas, en orden cronológico (la numeración de facturas sigue la hora real de cada venta)
            At(day, 8, 30);
            var schedule = new List<(int Minute, SignedIn Session)>();
            foreach (var (session, register, branch) in registers)
            {
                await session.Send(new OpenPosSessionCommand(register, 500m), ct);
                var count = (branch == BranchMain ? _rng.Next(6, 12) : _rng.Next(3, 7)) + (day.DayOfWeek == DayOfWeek.Saturday ? 3 : 0);
                if (isToday)
                {
                    count = Math.Max(2, count * (lastHour - 8) / 11);
                }
                schedule.AddRange(Enumerable.Range(0, count).Select(_ => (_rng.Next(9 * 60, lastHour * 60 + 30), session)));
            }
            foreach (var (minuteOfDay, session) in schedule.OrderBy(x => x.Minute))
            {
                At(day, minuteOfDay / 60, minuteOfDay % 60);
                var lines = Enumerable.Range(0, _rng.Next(1, 5)).Select(_ => weighted[_rng.Next(weighted.Count)]).DistinctBy(x => x.Sku)
                    .Select(x => new SaleLineInput(x.Sku, SaleQty(x.Unit, x.Pop), _rng.NextDouble() < 0.08 ? 5 : 0)).ToList();
                var customer = _rng.NextDouble() < 0.62 ? "CF" : customers[_rng.Next(customers.Count)];
                var pay = _rng.NextDouble();
                var method = pay < 0.55 ? "EFECTIVO" : pay < 0.8 ? "QR" : pay < 0.95 ? "TARJETA" : "TRANSFERENCIA";
                var reference = method == "EFECTIVO" ? null : $"{method[..2]}-{_rng.Next(100000, 999999)}";
                try
                {
                    var result = await session.Send(new CheckoutCommand(customer, method, lines, null, reference), ct);
                    tickets++;
                    lastInvoice = result.InvoiceNumber;
                }
                catch (DomainException)
                {
                    // Sin stock suficiente: el poka-yoke rechaza la venta (queda en la auditoría como rechazada)
                }
            }
            if (!isToday)
            {
                At(day, 19, 45);
                foreach (var (session, _, _) in registers)
                {
                    var state = await session.Send(new GetPosStateQuery(), ct);
                    var counted = state.Session!.ExpectedCash + (_rng.NextDouble() < 0.15 ? _rng.Next(-8, 6) : 0);
                    await session.Send(new ClosePosSessionCommand(state.Session.Id, counted), ct);
                }
            }

            if (day.DayOfWeek == DayOfWeek.Saturday)
            {
                log($"… {day:dd/MM/yyyy}: {tickets} ventas acumuladas.");
            }

            // Anulaciones ocasionales, mermas y ajustes
            if (!isToday && lastInvoice is not null && _rng.NextDouble() < 0.07)
            {
                At(day, 19, 20);
                await s1.Send(new VoidSaleCommand(lastInvoice, "Error de cobro: el cliente cambió de producto"), ct);
                lastInvoice = null;
            }
            if (_rng.NextDouble() < 0.12)
            {
                At(day, 18, 10);
                var item = catalog[_rng.Next(catalog.Count)];
                var card = await bodega.Send(new GetProductCardQuery(item.Sku, 1), ct);
                var bin = card.Bins.OrderByDescending(b => b.Available).FirstOrDefault();
                if (bin is { Available: >= 2 })
                {
                    await bodega.Send(new RegisterMovementCommand(item.Sku, bin.BinCode, MovementTypeCodes.AdjustmentOut, 1, day, null,
                        Pick(["Merma: producto dañado en exhibición", "Merma: empaque roto", "Pérdida detectada en revisión"])), ct);
                }
            }

            // Tesorería: depósitos de efectivo los sábados; gastos y pagos a proveedores a fin de mes
            if (day.DayOfWeek == DayOfWeek.Saturday && !isToday)
            {
                At(day, 20, 0);
                var cash = (await gerencia.Send(new GetChartOfAccountsQuery(), ct)).First(a => a.Code == AccountCodes.Cash).Balance;
                if (cash > 4000)
                {
                    await gerencia.Send(new CreateJournalEntryCommand(day, "Depósito del efectivo de la semana en el banco",
                        [new JournalLineSpec(AccountCodes.Bank, JournalPoster.Money(cash - 3000), 0), new JournalLineSpec(AccountCodes.Cash, 0, JournalPoster.Money(cash - 3000))]), ct);
                }
            }
            if (day.AddDays(1).Month != day.Month || (isToday && day.Day >= 25))
            {
                At(day, 20, 30);
                // Gastos del mes proporcionales a los días operados (el primer mes del período es parcial)
                var monthStart = new DateOnly(day.Year, day.Month, 1);
                var operated = day.DayNumber - (monthStart < start ? start : monthStart).DayNumber + 1;
                var share = Math.Min(1m, operated / (decimal)DateTime.DaysInMonth(day.Year, day.Month));
                var salaries = JournalPoster.Money((11200m + _rng.Next(0, 600)) * share);
                var rent = JournalPoster.Money(3500m * share);
                var utilities = JournalPoster.Money((450m + _rng.Next(0, 200)) * share);
                var office = JournalPoster.Money((250m + _rng.Next(0, 200)) * share);
                await gerencia.Send(new CreateJournalEntryCommand(day, $"Gastos del mes {day:MM/yyyy}: sueldos, alquiler y servicios",
                [
                    new JournalLineSpec("6.1.01", salaries, 0, "Planilla de sueldos"), new JournalLineSpec("6.1.02", rent, 0, "Alquiler del local"),
                    new JournalLineSpec("6.1.03", utilities, 0, "Luz, agua e internet"), new JournalLineSpec("6.1.04", office, 0, "Útiles y varios"),
                    new JournalLineSpec(AccountCodes.Bank, 0, salaries + rent + utilities + office, "Pago por banco"),
                ]), ct);
                var chart = await gerencia.Send(new GetChartOfAccountsQuery(), ct);
                var payables = chart.First(a => a.Code == AccountCodes.Payables).Balance;
                if (payables > 1000)
                {
                    var pay = JournalPoster.Money(payables * 0.7m);
                    await gerencia.Send(new CreateJournalEntryCommand(day, "Pago a proveedores por transferencia bancaria",
                        [new JournalLineSpec(AccountCodes.Payables, pay, 0), new JournalLineSpec(AccountCodes.Bank, 0, pay)]), ct);
                }
            }
        }
        log($"{o.Days} días de operación simulados: {tickets} ventas facturadas.");

        // ------------------------------------------------------------------------------------ estado final para explorar la app
        At(today, Math.Clamp(realNow.Hour, 9, 20), Math.Min(realNow.Minute, 50));
        var open = await bodega.Send(new OpenPhysicalCountCommand(warehouse.Code, today, "Conteo cíclico de la zona A (Ferretería)"), ct);
        foreach (var item in catalog.Where(c => c.Sku.StartsWith("FER", StringComparison.Ordinal)).Take(5))
        {
            var card = await bodega.Send(new GetProductCardQuery(item.Sku, 1), ct);
            var bin = card.Bins.OrderByDescending(b => b.OnHand).FirstOrDefault();
            if (bin is not null)
            {
                var delta = _rng.Next(-2, 3);
                await bodega.Send(new RecordCountCommand(open.PhysicalCountId, item.Sku, bin.BinCode, Math.Max(0, bin.OnHand + delta)), ct);
            }
        }
        var extra = catalog.Where(c => c.Sku.StartsWith("HEL", StringComparison.Ordinal)).Take(2).ToList();
        await bodega.Send(new CreatePurchaseOrderCommand(supplierByCategory["HEL"], today.AddDays(7), "Reposición de herramientas eléctricas (borrador)",
            extra.Select(e => new PurchaseLineInput(e.Sku, 3, e.Cost)).ToList()), ct);

        // V4 · Para explorar: una transferencia a Santa Cruz EN TRÁNSITO (despachada hoy) y una a El Alto PENDIENTE
        foreach (var pending in pendingReceipts)
        {
            await ReceiveAsync(pending.Receiver, pending.Id);
        }
        await ShipAsync("ALMSC", 5, 0.15);
        var popular = weighted.DistinctBy(x => x.Sku).Take(4).ToList();
        await bodega.Send(new CreateTransferCommand("ALMEA", popular.Select(x => new TransferLineInput(x.Sku, Qty(x.Unit, Math.Max(2, x.Max * 0.1m)))).ToList(),
            "Pedido de El Alto para el fin de semana (pendiente de despacho)"), ct);
        transfers++;

        // V4 · Webhook de la tienda (solo si este equipo tiene la clave maestra de integraciones)
        string? webhookSecret = null;
        try
        {
            webhookSecret = (await admin.Send(new CreateWebhookCommand("https://tienda.elconstructor.example/webhooks/minv",
                [IntegrationEvents.SaleCompleted, IntegrationEvents.TransferDispatched, IntegrationEvents.TransferReceived], "Tienda en línea (prueba)"), ct)).Secret;
        }
        catch (AccessDeniedException)
        {
            log("Webhook de prueba omitido: falta la clave maestra de integraciones (MINV_INTEGRATION_KEYS) en este equipo.");
        }

        if (db.Database.IsRelational())
        {
            // Carga masiva recién hecha: estadísticas para el planificador (si no, las consultas de las pantallas son lentas
            // hasta que pasa el autovacuum)
            await db.Database.ExecuteSqlRawAsync(PostgresMaintenance.AnalyzeSql, ct);
        }
        var stats = new
        {
            Movements = await db.StockMovements.CountAsync(ct),
            Journal = await db.JournalEntries.CountAsync(ct),
            Orders = await db.PurchaseOrders.CountAsync(ct),
        };
        log($"Listo: {stats.Movements} movimientos, {stats.Orders} órdenes de compra, {transfers} transferencias, {webOrders} pedidos web y " +
            $"{stats.Journal} asientos contables.");
        return new SeedResult(o.TenantCode, o.CompanyName, users, catalog.Count, Suppliers.Length, customers.Count, tickets, stats.Orders,
            stats.Movements, stats.Journal, start, today, [BranchMain, BranchElAlto, BranchSantaCruz], transfers, webOrders, apiKey.Name, apiKey.Token,
            webhookSecret);
    }

    // ---------------------------------------------------------------------------------------------- utilidades
    private sealed class SignedIn(IServiceScope scope) : IDisposable
    {
        public IServiceScope Scope { get; } = scope;

        public IMediator Mediator => Scope.ServiceProvider.GetRequiredService<IMediator>();

        /// <summary>Como el cliente de escritorio: cada caso de uso parte con el rastreo limpio (lee el estado real).</summary>
        public Task<T> Send<T>(IRequest<T> request, CancellationToken ct)
        {
            Scope.ServiceProvider.GetRequiredService<IMinvDbContext>().ClearTracking();
            return Mediator.Send(request, ct);
        }

        public void Dispose() => Scope.Dispose();
    }

    private async Task<SignedIn> SignInAsync(string tenantCode, string email, string password, CancellationToken ct)
    {
        var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand(tenantCode, email, password, Environment.MachineName, "datos-prueba"), ct);
        return new SignedIn(scope);
    }

    /// <summary>Como el API Gateway: la API Key deja el contexto de la petición (empresa, dueño, alcances, canal api).</summary>
    private async Task<SignedIn> ApiSignInAsync(string token, CancellationToken ct)
    {
        var scope = services.CreateScope();
        _ = await scope.ServiceProvider.GetRequiredService<ApiKeyAuthenticator>().AuthenticateAsync(token, ct)
            ?? throw new InvalidOperationException("La API Key de prueba no se pudo autenticar.");
        return new SignedIn(scope);
    }

    /// <summary>Disponible de un producto en un almacén (todas sus posiciones y lotes).</summary>
    private static async Task<decimal> AvailableAsync(MinvWriteDbContext db, string sku, Guid warehouseId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var bins = new Application.Inventory.InventoryLookups(db).BinIdsOf(warehouseId);
        return await (from l in db.StockLevels
                      join b in db.Batches on l.BatchId equals b.Id
                      join v in db.ProductVariants on b.VariantId equals v.Id
                      where v.Sku == sku && bins.Contains(l.BinId)
                      select (decimal?)(l.QuantityOnHand - l.QuantityReserved)).SumAsync(ct) ?? 0m;
    }

    private static async Task<string> BinOfAsync(MinvWriteDbContext db, string sku, CancellationToken ct) =>
        await (from v in db.ProductVariants
               join a in db.BinAssignments on v.Id equals a.VariantId
               join b in db.Bins on a.BinId equals b.Id
               where v.Sku == sku
               select b.Code).FirstAsync(ct);

    private T Pick<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];

    private decimal SaleQty(string unit, int popularity)
    {
        var decimals = unit is "KG" or "MT" or "LT" or "GL";
        if (decimals)
        {
            return unit == "MT" ? _rng.Next(2, 26) : decimal.Round(0.5m + (decimal)_rng.NextDouble() * 3.5m, 1);
        }
        return _rng.Next(1, popularity >= 4 ? 5 : 3);
    }

    private static decimal Qty(string unit, decimal value) =>
        unit is "KG" or "MT" or "LT" or "GL" ? decimal.Round(value, 1) : decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    /// <summary>Contraseña aleatoria fácil de dictar: «Illimani-4827» (letras, guion y números; nunca se versiona).</summary>
    private string NewPassword() =>
        $"{PasswordWords[RandomNumberGenerator.GetInt32(PasswordWords.Length)]}-{RandomNumberGenerator.GetInt32(1000, 10000).ToString(CultureInfo.InvariantCulture)}";

    private static string Slug(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in decomposed)
        {
            if (ch == ' ')
            {
                sb.Append('.');
            }
            else if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }
        return sb.ToString().Trim('.');
    }

    private static string Ean13(string twelveDigits) => twelveDigits + BarcodeRules.ComputeGtinCheckDigit(twelveDigits);
}
