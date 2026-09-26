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
using MINV.Application.Inventory.Queries;
using MINV.Application.Inventory.Transfers;
using MINV.Application.Partners;
using MINV.Application.Purchasing;
using MINV.Application.Sales;
using MINV.Application.Tech;
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
using MINV.Infrastructure.Seeding.Tecnologia;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Seeding;

/// <summary>Opciones de la empresa de prueba. V4.1: <paramref name="TaxId"/> es también el NIT de simulación del SIAT;
/// <paramref name="Billing"/> = false la carga sin facturación; <paramref name="BillingDays"/> son los últimos días del
/// período que se facturan; <paramref name="SiatToken"/> (opcional) fija el token de simulación (si no, uno aleatorio) y
/// <paramref name="SiatSimulatorUrl"/> es la URL del simulador HTTP que queda en la configuración. V4.2: la empresa es
/// Tech Zone Gaming S.R.L. (catálogo de tecnología embebido); <paramref name="SharedPassword"/> (la demostración) da la
/// misma contraseña a todos los usuarios, <paramref name="Volume"/> escala las ventas diarias (1 = normal) y
/// <paramref name="ResetClock"/> = false (la demostración) deja el reloj simulado al final de la operación de hoy (lo que se
/// registre después queda siempre después de lo cargado) y <paramref name="BillingScenarios"/> = false factura todas las
/// ventas sin los escenarios de contingencia (corte de internet, CAFC, anulaciones, reversión, notas y NIT rechazado);
/// <paramref name="SuggestedOrders"/> = false (la demostración) arma las órdenes de compra de lo que está bajo el mínimo sin
/// calcular el pedido sugerido completo.</summary>
public sealed record SeedOptions(string TenantCode = "TECHZONE", string CompanyName = "Tech Zone Gaming S.R.L.", string TaxId = "1023456029",
    string Domain = "techzone.example", int Days = 60, int Seed = 2026, string TimeZoneId = "America/La_Paz", bool Billing = true,
    int BillingDays = 25, string? SiatToken = null, string SiatSimulatorUrl = SiatSeedSetup.LocalSimulatorUrl, string? SharedPassword = null,
    double Volume = 1.0, bool ResetClock = true, bool BillingScenarios = true, bool SuggestedOrders = true)
{
    // Sin la contraseña compartida (regla A-12: nunca en logs ni en ToString)
    public override string ToString() => $"SeedOptions {TenantCode} · {Days} días · semilla {Seed} · facturación {(Billing ? "sí" : "no")}";
}

public sealed record SeedUser(string RoleCode, string RoleName, string Name, string Email, string Password, string Branches = "")
{
    // La contraseña de prueba no se muestra al registrar el usuario en un log o en una aserción
    public override string ToString() => $"SeedUser {RoleCode} · {Email} · {Branches}";
}

/// <summary>V4.2 · Resumen de la edición Tecnología de la empresa de prueba: fichas técnicas, series e IMEI, casos RMA,
/// armados de PC y devoluciones.</summary>
public sealed record SeedTech(int Categories, int SpecDefinitions, int SpecValues, int Brands, int SerializedProducts, int Serials, int SerialsInStock,
    int WarrantyClaims, IReadOnlyList<string> ClaimStates, int PcBuilds, int PcBuildsSold, int PcBuildsIncompatible, int Returns);

/// <summary>Resultado de la carga. V4.1: <paramref name="Billing"/> resume la facturación (null si se cargó sin ella). V4.2:
/// <paramref name="Tech"/> resume series, RMA y armados.</summary>
public sealed record SeedResult(string TenantCode, string CompanyName, IReadOnlyList<SeedUser> Users, int Products, int Suppliers, int Customers,
    int Tickets, int PurchaseOrders, int Movements, int JournalEntries, DateOnly From, DateOnly To, IReadOnlyList<string> Branches, int Transfers,
    int ExternalOrders, string ApiKeyName, string ApiKeyToken, string? WebhookSecret, SeedBilling? Billing = null, SeedTech? Tech = null)
{
    // El token de la API Key y el secreto del webhook se muestran una sola vez (regla B-11): nunca en ToString
    public override string ToString() => $"SeedResult {TenantCode} · {Products} productos · {Tickets} ventas · {From:dd/MM/yyyy} a {To:dd/MM/yyyy}";
}

/// <summary>
/// Datos de prueba aleatorios (reproducibles con la semilla) para la base LOCAL, la de la NUBE y la DEMOSTRACIÓN en memoria.
/// V4.2 · La empresa es <b>Tech Zone Gaming S.R.L.</b> (regla T-09), una tienda de computadoras, componentes, periféricos,
/// consolas y videojuegos con tres sucursales (CM La Paz, CB Cochabamba y SC Santa Cruz): árbol de categorías,
/// especificaciones técnicas con claves de compatibilidad, 159 productos con ficha técnica, perfil técnico (serie o IMEI y
/// garantía), imagen, marca, proveedores y clientes del catálogo embebido (<see cref="TechSeedCatalog"/>). N días de
/// operación: compras recibidas con series, transferencias con sus series (algunas con faltantes), ventas de caja y
/// pedidos de la tienda en línea con las series escaneadas y el comprador, devoluciones (una por falla), casos RMA en
/// todos sus estados, armados de PC (cotizados, dos vendidos en la caja y los incompatibles marcados), tomas físicas,
/// contabilidad y (V4.1) facturación SIAT con el simulador del SIN en proceso (<see cref="BillingScenario"/>). TODO pasa
/// por los mismos casos de uso de la aplicación (regla A-13: validación, permisos, alcance por sucursal, poka-yoke,
/// series, auditoría y contabilidad): coherente por construcción.
/// </summary>
public sealed partial class LocalDataSeeder(IServiceProvider services, DemoClock clock)
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

    /// <summary>V4 · Sucursales de la empresa de prueba (la casa matriz la crea el aprovisionamiento). V4.2: CM La Paz,
    /// CB Cochabamba y SC Santa Cruz (las del catálogo de tecnología).</summary>
    public const string BranchMain = "CM";
    public const string BranchCochabamba = "CB";
    public const string BranchSantaCruz = "SC";

    /// <summary>Almacén de cada sucursal.</summary>
    public const string MainWarehouse = TenantProvisioner.WarehouseCode;
    public const string CochabambaWarehouse = "ALMCB";
    public const string SantaCruzWarehouse = "ALMSC";

    private Random _rng = new(2026);
    private TechSeedCatalog _catalog = null!;
    private SeedStock _stock = null!;
    private List<TechProduct> _weighted = [];
    private readonly HashSet<string> _serials = new(StringComparer.Ordinal);

    public async Task<SeedResult> SeedAsync(SeedOptions o, Action<string> log, CancellationToken ct = default)
    {
        _rng = new Random(o.Seed);
        _catalog = TechSeedCatalog.Current;
        _serials.Clear();
        var volume = Math.Clamp(o.Volume, 0.2, 3.0);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(o.TimeZoneId);
        var realNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var today = DateOnly.FromDateTime(realNow.DateTime);
        var start = today.AddDays(-Math.Max(2, o.Days));
        var latest = DateTimeOffset.MinValue;   // la hora simulada más tardía usada (lo último registrado)
        void At(DateOnly day, int hour, int minute)
        {
            var instant = new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), zone.GetUtcOffset(day.ToDateTime(new TimeOnly(hour, minute))));
            latest = instant > latest ? instant : latest;
            clock.StartAt(instant);
        }

        // ------------------------------------------------------------------------------------ empresa y administrador
        At(start.AddDays(-1), 8, 0);
        var users = new List<SeedUser>();
        var adminPassword = o.SharedPassword ?? NewPassword();
        users.Add(new SeedUser(RoleCodes.Admin, "Administrador", "Administrador General", $"admin@{o.Domain}", adminPassword, "Todas"));
        using (var scope = services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().ProvisionAsync(new ProvisionTenantRequest(o.TenantCode, o.CompanyName,
                o.TaxId, users[0].Email, users[0].Name, adminPassword, "BOB", o.TimeZoneId, "BO", "Bolivia", "LP", "La Paz", "La Paz",
                "Casa matriz La Paz · Av. 16 de Julio (El Prado)", "Almacén central La Paz", 0.20m, 60, start.AddDays(-1)), ct);
        }
        log($"Empresa {o.TenantCode} · {o.CompanyName} creada (período {start:dd/MM/yyyy} a {today:dd/MM/yyyy}).");

        using var admin = await SignInAsync(o.TenantCode, users[0].Email, adminPassword, ct);

        // ------------------------------------------------------------------------------------ V4 · sucursales
        await admin.Send(new CreateBranchCommand(BranchCochabamba, "Sucursal Cochabamba", CochabambaWarehouse, "Almacén Cochabamba"), ct);
        await admin.Send(new CreateBranchCommand(BranchSantaCruz, "Sucursal Santa Cruz", SantaCruzWarehouse, "Almacén Santa Cruz"), ct);
        log("3 sucursales: CM · Casa matriz La Paz (almacén central, 3 cajas), CB · Cochabamba y SC · Santa Cruz (una caja cada una).");

        // ------------------------------------------------------------------------------------ usuarios por rol y sucursal
        var plan = new (string Role, string RoleName, string[] Branches)[]
        {
            (RoleCodes.Management, "Gerencia", [BranchMain]),
            (RoleCodes.Warehouse, "Bodega", [BranchMain]), (RoleCodes.Warehouse, "Bodega", [BranchCochabamba]),
            (RoleCodes.Warehouse, "Bodega", [BranchSantaCruz]),
            (RoleCodes.Sales, "Ventas", [BranchMain]), (RoleCodes.Sales, "Ventas", [BranchSantaCruz]),
            (RoleCodes.Cashier, "Cajero", [BranchMain]), (RoleCodes.Cashier, "Cajero", [BranchMain]), (RoleCodes.Cashier, "Cajero", [BranchCochabamba]),
            (RoleCodes.Cashier, "Cajero", [BranchSantaCruz]),
            (RoleCodes.ReadOnly, "Consulta", [BranchMain, BranchCochabamba, BranchSantaCruz]),
        };
        var usedNames = new HashSet<string>();
        var planned = new List<(SeedUser User, string[] Branches)>();
        foreach (var (role, roleName, branches) in plan)
        {
            string name;
            do
            {
                name = $"{Pick(FirstNames)} {Pick(LastNames)}";
            }
            while (!usedNames.Add(name));
            var password = o.SharedPassword ?? NewPassword();
            planned.Add((new SeedUser(role, roleName, name, $"{Slug(name)}@{o.Domain}", password,
                role == RoleCodes.Management ? "Todas (gerencia global)" : string.Join(", ", branches)), branches));
        }
        // Lo que no depende de lo anterior (cada alta de usuario calcula el hash PBKDF2 de su contraseña; cada ficha de
        // producto es independiente) el administrador lo registra desde tres sesiones a la vez, con los mismos casos de uso
        var helpers = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => SignInAsync(o.TenantCode, users[0].Email, adminPassword, ct), ct)));
        SeededCatalog catalog;
        decimal opening;
        var db = admin.Scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        try
        {
            var creators = helpers.Prepend(admin).ToList();
            await InParallelAsync(creators, planned, async (session, x) =>
            {
                await session.Send(new SaveUserCommand(null, x.User.Email, x.User.Name, x.User.RoleCode, true, x.User.Password, x.Branches), ct);
                await session.Send(new ResetUserPasswordCommand(x.User.Email, x.User.Password, MustChange: false), ct);
            }, ct);
            users.AddRange(planned.Select(p => p.User));
            log($"{users.Count} usuarios creados (uno o más por rol, asignados a sus sucursales).");

            // -------------------------------------------------------------------------------- datos maestros
            var tenantId = admin.Scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId;
            _stock = new SeedStock(db, _catalog);
            catalog = await SeedCatalogAsync(admin, creators, db, tenantId, log, ct);

            // -------------------------------------------------------------------------------- saldo inicial de la casa matriz
            At(start, 7, 30);
            var buildParts = _catalog.Builds.SelectMany(b => b.Lines).Select(l => l.Sku).ToHashSet(StringComparer.Ordinal);
            var balances = new List<(TechProduct Product, int Quantity, IReadOnlyList<string>? Serials)>();
            foreach (var p in _catalog.Products)
            {
                // Algunos productos arrancan bajos o agotados para que haya alertas y pedido sugerido; los servicios tienen un
                // cupo (las piezas de los armados del catálogo arrancan con existencia: se cotizan y se venden al final del período)
                var roll = buildParts.Contains(p.Sku) ? 0.5 : _rng.NextDouble();
                // (la demostración, con pocos días y menos ventas, arranca más cerca del máximo: si no, más de la mitad del
                // catálogo quedaba en sobrestock en su tablero)
                var quantity = p.IsService ? 90
                    : roll < 0.05 ? 0
                    : roll < 0.15 ? Math.Max(1, (int)Math.Round(p.Minimum * 0.7))
                    : (int)Math.Round(p.Maximum * (volume < 1 ? 0.6 + _rng.NextDouble() * 0.45 : 0.9 + _rng.NextDouble() * 0.7));
                if (quantity > 0)
                {
                    balances.Add((p, quantity, p.TracksSerials ? NewSerials(p, quantity) : null));
                }
            }
            // Cada saldo inicial es de un producto distinto (su lote, su existencia y sus series): también a la vez
            await InParallelAsync(creators, balances, (session, x) => session.Send(new RegisterMovementCommand(x.Product.Sku, catalog.Bins[x.Product.Sku],
                MovementTypeCodes.InitialBalance, x.Quantity, start, "INV-INICIAL", x.Product.IsService ? "Cupo inicial de servicio técnico" : "Inventario inicial",
                Serials: x.Serials), ct), ct);
            opening = balances.Sum(x => x.Quantity * x.Product.Cost);
        }
        finally
        {
            foreach (var helper in helpers)
            {
                helper.Dispose();
            }
        }
        await admin.Send(new CreateJournalEntryCommand(start, "Asiento de apertura: aporte de capital en inventario, banco y caja",
        [
            new JournalLineSpec(AccountCodes.Inventory, JournalPoster.Money(opening), 0),
            new JournalLineSpec(AccountCodes.Bank, 350000, 0),
            new JournalLineSpec(AccountCodes.Cash, 5000, 0),
            new JournalLineSpec(AccountCodes.Capital, 0, JournalPoster.Money(opening) + 355000),
        ]), ct);
        log($"Saldo inicial de la casa matriz: {_catalog.Products.Count} productos (Bs {opening:N2} en inventario, {_serials.Count} series e IMEI).");

        // V4 · API Key del e-commerce (el token se entrega una sola vez; queda en el archivo local de credenciales)
        var apiKey = await admin.Send(new CreateApiKeyCommand("Tienda en línea", [ApiScopes.CatalogRead, ApiScopes.StockRead, ApiScopes.OrdersWrite],
            BranchMain), ct);

        // ------------------------------------------------------------------------------------ sesiones de la operación diaria
        var cashiers = users.Where(u => u.RoleCode == RoleCodes.Cashier).ToList();
        var keepers = users.Where(u => u.RoleCode == RoleCodes.Warehouse).ToList();
        var sellers = users.Where(u => u.RoleCode == RoleCodes.Sales).ToList();
        var manager = users.First(u => u.RoleCode == RoleCodes.Management);
        // Los ingresos son independientes (cada uno verifica su contraseña): en paralelo
        var signedIn = await Task.WhenAll(new[] { cashiers[0], cashiers[1], cashiers[2], cashiers[3], keepers[0], keepers[1], keepers[2], sellers[0], sellers[1], manager }
            .Select(u => Task.Run(() => SignInAsync(o.TenantCode, u.Email, u.Password, ct), ct)));
        using var s1 = signedIn[0];
        using var s2 = signedIn[1];
        using var sCb = signedIn[2];
        using var sSc = signedIn[3];
        using var bodega = signedIn[4];
        using var bodegaCb = signedIn[5];
        using var bodegaSc = signedIn[6];
        using var ventasCm = signedIn[7];
        using var ventasSc = signedIn[8];
        using var gerencia = signedIn[9];
        using var tienda = await ApiSignInAsync(apiKey.Token, ct);
        var registers = new[]
        {
            (Session: s1, Register: "CAJA01", Branch: BranchMain), (Session: s2, Register: "CAJA02", Branch: BranchMain),
            (Session: sCb, Register: $"{BranchCochabamba}-CAJA1", Branch: BranchCochabamba), (Session: sSc, Register: $"{BranchSantaCruz}-CAJA1", Branch: BranchSantaCruz),
        };
        var branchSessions = new Dictionary<string, BranchTeam>(StringComparer.Ordinal)
        {
            [BranchMain] = new(BranchMain, MainWarehouse, s1, bodega, ventasCm),
            [BranchCochabamba] = new(BranchCochabamba, CochabambaWarehouse, sCb, bodegaCb, sCb),
            [BranchSantaCruz] = new(BranchSantaCruz, SantaCruzWarehouse, sSc, bodegaSc, ventasSc),
        };
        _weighted = _catalog.Products.SelectMany(p => Enumerable.Repeat(p, p.IsService ? Math.Max(1, p.Popularity) : p.Popularity * p.Popularity)).ToList();
        var tickets = 0;
        var transfers = 0;
        var webOrders = 0;
        var lastSale = ((SignedIn Session, string Invoice)?)null;   // la última venta de caja (la anula la caja que la cobró)

        // V4.1 · Facturación SIAT en los últimos días del período (con el simulador del SIN en proceso y el mismo reloj)
        var billingFrom = today.AddDays(-Math.Clamp(o.BillingDays, 1, Math.Max(1, o.Days - 1)));
        var billing = BillingScenario.TryCreate(this, services, o, billingFrom, today, At, log, admin, gerencia, bodega, catalog.Customers, ct);

        // V4.2 · Agenda de la edición Tecnología: casos RMA, armados de PC, devolución por falla y tomas físicas
        var tech = new TechAgenda(this, start, today, At, log, admin, branchSessions, db, catalog, billing, ct);

        // Reposición de una sucursal: la casa matriz arma la transferencia con lo que la sucursal necesita y ella tiene de
        // sobra (las series salen del almacén central) y la despacha; la sucursal la recibe (a veces con un faltante)
        async Task<TransferRef?> ShipAsync(string warehouseCode, int items, double share, IReadOnlyList<(TechProduct Product, int Quantity)>? only = null,
            string? note = null)
        {
            var lines = new List<TransferLineInput>();
            var candidates = only ?? _weighted.OrderBy(_ => _rng.Next()).DistinctBy(x => x.Sku)
                .OrderBy(p => _stock.Available(warehouseCode, p.Sku) / Math.Max(1m, p.Maximum)).Select(p => (Product: p, Quantity: 0)).ToList();
            foreach (var (p, fixedQuantity) in candidates)
            {
                if (lines.Count >= items)
                {
                    break;
                }
                // Lo que falta para llegar a la cuota de la sucursal (o las piezas exactas de un armado ya cotizado)
                var wanted = Math.Max(p.IsService ? 10 : 1, (int)Math.Round(p.Maximum * share));
                var qty = only is not null ? fixedQuantity : wanted - (int)Math.Floor(_stock.Available(warehouseCode, p.Sku));
                // La casa matriz conserva su mínimo (salvo para las piezas de un armado ya cotizado: si no las tiene, las compra)
                var reserve = only is not null ? 0 : p.Minimum;
                if (only is not null && qty > 0 && _stock.Available(MainWarehouse, p.Sku) < qty && _catalog.SupplierOf(p) is { } supplier)
                {
                    await UrgentPurchaseAsync(p, qty - (int)Math.Floor(Math.Max(0, _stock.Available(MainWarehouse, p.Sku))), supplier, note);
                }
                if (qty <= 0 || _stock.Available(MainWarehouse, p.Sku) < qty + reserve || !_stock.TryTake(MainWarehouse, p, qty, _rng, out var serials))
                {
                    continue;
                }
                lines.Add(new TransferLineInput(p.Sku, qty, serials));
            }
            if (lines.Count == 0)
            {
                return null;
            }
            var created = await bodega.Send(new CreateTransferCommand(warehouseCode, lines, note ?? "Reposición de la sucursal"), ct);
            await bodega.Send(new DispatchTransferCommand(created.Id), ct);
            transfers++;
            return created;
        }

        // Compra urgente al proveedor (orden aprobada y recibida el mismo día con sus series) de lo que falta en la casa matriz
        async Task UrgentPurchaseAsync(TechProduct product, int quantity, TechSupplier supplier, string? reason)
        {
            var order = await bodega.Send(new CreatePurchaseOrderCommand(catalog.SupplierCodes[supplier.Code], null,
                $"Compra urgente: {reason ?? "reposición"}", [new PurchaseLineInput(product.Sku, quantity, product.Cost)]), ct);
            await gerencia.Send(new ApprovePurchaseOrderCommand(order.Id), ct);
            var serials = product.TracksSerials ? NewSerials(product, quantity) : null;
            await bodega.Send(new ReceivePurchaseOrderCommand(order.Id, $"FAC-{_rng.Next(10000, 99999)}",
                serials is null ? null : [new SkuSerials(product.Sku, serials)]), ct);
            _stock.GiveBack(MainWarehouse, product.Sku, quantity, serials);   // lo recibido ya está en el almacén central
            log($"… compra urgente de {quantity} {product.Sku} a {supplier.Name} ({reason}).");
        }

        // Demostración: bodega arma a mano la orden de lo que está bajo el mínimo en la casa matriz (los proveedores con más
        // faltantes, entrega en dos días), sin calcular la proyección completa del pedido sugerido
        async Task OrderWhatIsLowAsync(DateOnly day)
        {
            var low = _catalog.Products.Where(p => !p.IsService && _catalog.SupplierOf(p) is not null && _stock.Available(MainWarehouse, p.Sku) < p.Minimum)
                .GroupBy(p => _catalog.SupplierOf(p)!.Code).OrderByDescending(g => g.Count()).Take(2);
            foreach (var group in low)
            {
                await bodega.Send(new CreatePurchaseOrderCommand(catalog.SupplierCodes[group.Key], day.AddDays(2), "Reposición de lo que está bajo el mínimo",
                    group.Take(6).Select(p => new PurchaseLineInput(p.Sku, Math.Max(1, p.Maximum - (int)Math.Floor(Math.Max(0, _stock.Available(MainWarehouse, p.Sku)))),
                        p.Cost)).ToList()), ct);
            }
        }

        async Task ReceiveAsync(SignedIn receiver, Guid transferId, bool complete = false)
        {
            var detail = await receiver.Send(new GetTransferQuery(transferId), ct);
            var receipt = new List<TransferReceiptInput>();
            if (!complete && _rng.NextDouble() < 0.2)
            {
                var line = detail.Lines[_rng.Next(detail.Lines.Count)];
                var missing = line.Serials is { Count: > 0 } s ? new[] { s[_rng.Next(s.Count)] } : null;
                receipt.Add(new TransferReceiptInput(line.Sku, Math.Max(0, line.Quantity - 1),
                    Pick(["Caja dañada en el camión", "Faltante detectado al contar la recepción", "Equipo golpeado en el traslado"]), missing));
            }
            await receiver.Send(new ReceiveTransferCommand(transferId, receipt), ct);
        }

        // Primera distribución: la casa matriz abastece a Cochabamba y Santa Cruz el día de apertura (con cupos de servicio)
        await _stock.RefreshAsync(ct);
        At(start, 9, 0);
        // La demostración (menos volumen) abastece muchos productos con pocas unidades para que sus sucursales no queden casi
        // agotadas; en memoria cada línea cuesta unos 25 ms (90 por sucursal suman unos 2 s a la apertura)
        var (firstLines, firstShare) = volume < 1 ? (90, 0.1) : ((int)Math.Round(70 * volume * volume), 0.35);
        var firstCb = await ShipAsync(CochabambaWarehouse, firstLines, firstShare, note: "Primera distribución a la sucursal Cochabamba");
        var firstSc = await ShipAsync(SantaCruzWarehouse, firstLines, firstShare, note: "Primera distribución a la sucursal Santa Cruz");
        At(start, 16, 30);
        if (firstCb is not null)
        {
            await ReceiveAsync(bodegaCb, firstCb.Id);
        }
        if (firstSc is not null)
        {
            await ReceiveAsync(bodegaSc, firstSc.Id);
        }
        log("Primera distribución: la casa matriz abasteció a Cochabamba y Santa Cruz (transferencias con sus series, recibidas el mismo día).");
        if (db.Database.IsRelational())
        {
            // Carga inicial recién hecha (catálogo, series, saldo inicial y primera distribución): estadísticas para el
            // planificador antes de la operación diaria (si no, el pedido sugerido puede elegir un plan pésimo y agotar el
            // tiempo de espera antes de que pase el autovacuum)
            await db.Database.ExecuteSqlRawAsync(PostgresMaintenance.AnalyzeSql, ct);
        }
        var pendingReceipts = new List<(DateOnly Day, SignedIn Receiver, Guid Id, bool Complete)>();   // Complete: piezas de un armado (llegan todas)
        for (var day = start.AddDays(1); day <= today; day = day.AddDays(1))
        {
            if (day.DayOfWeek == DayOfWeek.Sunday)
            {
                continue;
            }
            var isToday = day == today;
            var lastHour = isToday ? Math.Clamp(realNow.Hour, 10, 19) : 19;
            // Minuto de lo que ocurre al final de la jornada (garantías, armados, mermas, gastos del mes): el de siempre y, HOY,
            // enseguida de la última venta (V4.2: nada de hoy queda horas después de la hora final de la carga; si no, lo que
            // la persona registre después aparecería antes, p. ej. recibir una transferencia antes de su despacho)
            int Evening(int usual, int todayOffset) => isToday ? lastHour * 60 + 40 + todayOffset : usual;

            // V4.1 · Facturación: el primer día facturado se configura (07:45) y cada día el trabajo automático pide el CUFD
            // del día, sincroniza la hora y los catálogos (08:15), como lo hace el servidor en la nube
            if (billing is not null)
            {
                await billing.StartDayAsync(day);
            }

            // Recepciones de compras (llegan en su fecha estimada) con las series de cada unidad serializada
            At(day, 8, 40);
            foreach (var order in await bodega.Send(new GetPurchaseOrdersQuery(PurchaseOrderStatus.Approved), ct))
            {
                if (order.ExpectedDate <= day)
                {
                    var detail = await bodega.Send(new GetPurchaseOrderQuery(order.Id), ct);
                    var serials = detail.Lines.Where(l => _catalog.Product(l.Sku).TracksSerials && l.Quantity > l.Received)
                        .Select(l => new SkuSerials(l.Sku, NewSerials(_catalog.Product(l.Sku), (int)(l.Quantity - l.Received)))).ToList();
                    await bodega.Send(new ReceivePurchaseOrderCommand(order.Id, $"FAC-{_rng.Next(10000, 99999)}", serials), ct);
                }
            }

            // V4 · Recepciones de transferencias despachadas el día anterior (la mercadería estuvo en tránsito)
            At(day, 8, 50);
            foreach (var pending in pendingReceipts.Where(x => x.Day <= day).ToList())
            {
                await ReceiveAsync(pending.Receiver, pending.Id, pending.Complete);
                pendingReceipts.Remove(pending);
            }
            await _stock.RefreshAsync(ct);

            // Pedido sugerido: bodega arma las órdenes los lunes y jueves; gerencia las aprueba
            if (day.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Thursday && !isToday)
            {
                At(day, 9, 10);
                if (o.SuggestedOrders)
                {
                    try
                    {
                        await bodega.Send(new CreateSuggestedPurchaseOrdersCommand(), ct);
                    }
                    catch (DomainException)
                    {
                        // nada nuevo que pedir
                    }
                }
                else
                {
                    await OrderWhatIsLowAsync(day);
                }
                At(day, 10, 5);
                foreach (var draft in await gerencia.Send(new GetPurchaseOrdersQuery(PurchaseOrderStatus.Draft), ct))
                {
                    await gerencia.Send(new ApprovePurchaseOrderCommand(draft.Id), ct);
                }
            }

            // V4 · Reposición: lunes y jueves a Cochabamba, martes y viernes a Santa Cruz (llegan al día siguiente)
            if (!isToday && day.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Thursday or DayOfWeek.Friday)
            {
                At(day, 15, 0);
                var toCb = day.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Thursday;
                var shipped = await ShipAsync(toCb ? CochabambaWarehouse : SantaCruzWarehouse, Math.Max(4, (int)Math.Round(_rng.Next(8, 15) * volume)), 0.3);
                if (shipped is not null)
                {
                    pendingReceipts.Add((day.AddDays(1), toCb ? bodegaCb : bodegaSc, shipped.Id, false));
                }
            }

            // V4 · Pedidos de la tienda en línea por la API (martes, jueves y sábados), despachados desde la casa matriz
            if (day.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday or DayOfWeek.Saturday)
            {
                // En orden cronológico: cada pedido se factura y se envía al SIN al registrarse (la hora fiscal no retrocede)
                var webCount = _rng.Next(1, volume < 1 ? 3 : 4);   // la demostración (menos volumen) recibe menos pedidos
                // (hoy, hasta la hora de la última venta: un pedido de las 17:00 no puede llegar antes de que corra la carga)
                var webTimes = Enumerable.Range(0, webCount).Select(_ => _rng.Next(10 * 60, isToday ? lastHour * 60 + 30 : 18 * 60)).Order().ToList();
                for (var n = 0; n < webCount; n++)
                {
                    At(day, webTimes[n] / 60, webTimes[n] % 60);
                    var lines = PickLines(MainWarehouse, 2, allowServices: false);
                    if (lines.Count == 0)
                    {
                        continue;
                    }
                    var webCustomer = Pick(catalog.Customers).Code;
                    try
                    {
                        // V4.1 · Si la empresa factura, el pedido lleva los datos de facturación del cliente de la tienda
                        var order = await tienda.Send(new CreateExternalOrderCommand($"WEB-{day:yyyyMMdd}-{n + 1}", webCustomer, "QR", lines,
                            $"QR-{_rng.Next(100000, 999999)}", Buyer: billing?.ForWebOrder(webCustomer)), ct);
                        billing?.NoteWebOrder(order);
                        tech.NoteSale(day, BranchMain, order.InvoiceNumber, webCustomer, lines);
                        webOrders++;
                    }
                    catch (DomainException)
                    {
                        _stock.GiveBack(MainWarehouse, lines);   // la tienda recibe el rechazo (poka-yoke) y el pedido no se registra
                    }
                    if (billing is not null)
                    {
                        await billing.DispatchPendingAsync();   // el trabajo automático del servidor lo envía en segundos
                    }
                }
            }

            // Ventas en las cuatro cajas, en orden cronológico (la numeración de facturas sigue la hora real de cada venta)
            At(day, 8, 30);
            var schedule = new List<(int Minute, SignedIn Session, string Branch, string? Tag)>();
            foreach (var (session, register, branch) in registers)
            {
                await session.Send(new OpenPosSessionCommand(register, 800m), ct);
                var count = (branch == BranchMain ? _rng.Next(4, 9) : _rng.Next(3, 7)) + (day.DayOfWeek == DayOfWeek.Saturday ? 2 : 0);
                count = Math.Max(1, (int)Math.Round(count * volume));
                if (isToday)
                {
                    count = Math.Max(2, count * (lastHour - 8) / 11);
                }
                schedule.AddRange(Enumerable.Range(0, count).Select(_ => (_rng.Next(9 * 60, lastHour * 60 + 30), session, branch, (string?)null)));
            }
            // V4.1 · Ventas de los escenarios de facturación (venta menor, NIT rechazado, facturas durante el corte de internet)
            foreach (var slot in billing?.ExtraSales(day, lastHour * 60 + 30) ?? [])
            {
                schedule.Add((slot.Minute, registers.First(r => r.Branch == slot.Branch).Session, slot.Branch, slot.Tag));
            }
            // V4.2 · Venta de armados cotizados en la caja de su sucursal
            foreach (var slot in tech.BuildSales(day))
            {
                schedule.Add((slot.Minute, registers.First(r => r.Branch == slot.Branch).Session, slot.Branch, slot.Tag));
            }
            foreach (var (minuteOfDay, session, branch, tag) in schedule.OrderBy(x => x.Minute))
            {
                if (billing is not null)
                {
                    await billing.AdvanceAsync(day, minuteOfDay);
                    if (billing.Skips(branch) && tag is null)
                    {
                        continue;   // corte de energía: esa caja factura a mano con el talonario CAFC (se transcribe después)
                    }
                }
                At(day, minuteOfDay / 60, minuteOfDay % 60);
                if (tag is not null && TechAgenda.IsBuildTag(tag))
                {
                    await tech.SellBuildAsync(session, branch, tag, day);
                    continue;
                }
                var warehouseCode = branchSessions[branch].Warehouse;
                var lines = PickLines(warehouseCode, 3, allowServices: branch == BranchMain);
                if (lines.Count == 0)
                {
                    continue;
                }
                var customer = _rng.NextDouble() < 0.5 || tag is not null ? "CF" : catalog.CustomerFor(branch, _rng);
                var pay = _rng.NextDouble();
                var method = pay < 0.4 ? "EFECTIVO" : pay < 0.65 ? "QR" : pay < 0.9 ? "TARJETA" : "TRANSFERENCIA";
                var reference = method == "EFECTIVO" ? null : $"{method[..2]}-{_rng.Next(100000, 999999)}";
                var (buyer, card) = billing?.ForSale(customer, method, tag) ?? (null, null);
                try
                {
                    var result = await session.Send(new CheckoutCommand(customer, method, lines, null, reference, buyer, card), ct);
                    tickets++;
                    lastSale = (session, result.InvoiceNumber);
                    tech.NoteSale(day, branch, result.InvoiceNumber, customer, lines);
                    if (billing is not null)
                    {
                        await billing.AfterSaleAsync(session, branch, result, lines, day, buyer, tag);
                    }
                }
                catch (DomainException ex)
                {
                    // Sin stock suficiente: el poka-yoke rechaza la venta (queda en la auditoría como rechazada)
                    _stock.GiveBack(warehouseCode, lines);
                    if (tag is not null)
                    {
                        log($"… {day:dd/MM/yyyy}: la venta del escenario «{tag}» no se registró: {ex.Message}");
                    }
                }
            }
            if (billing is not null)
            {
                await billing.AdvanceAsync(day, (isToday ? lastHour : 19) * 60 + 40);
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
            // V4.1 · Después del cierre de caja: anulaciones, reversión y devoluciones con nota crédito-débito del día
            if (billing is not null)
            {
                await billing.EndOfDayAsync(day, (isToday ? lastHour : 19) * 60 + 50);
            }

            // V4.2 · Garantías (RMA), armados de PC (cotizaciones y piezas para las sucursales), devolución por falla y tomas
            // físicas del día
            foreach (var parts in await tech.EndOfDayAsync(day, isToday, Evening(18 * 60, 1)))
            {
                var shipped = await ShipAsync(parts.Warehouse, parts.Parts.Count, 0, parts.Parts, parts.Note);
                if (shipped is not null)
                {
                    pendingReceipts.Add((day.AddDays(1), branchSessions[parts.Branch].Keeper, shipped.Id, true));
                }
            }

            if (day.DayOfWeek == DayOfWeek.Saturday)
            {
                log($"… {day:dd/MM/yyyy}: {tickets} ventas del día a día acumuladas.");
            }

            // Anulaciones ocasionales, mermas y ajustes (V4.1: una venta facturada se anula en el SIN, ver los escenarios)
            if (!isToday && lastSale is { } last && _rng.NextDouble() < 0.07 && billing is not { Active: true })
            {
                At(day, 19, 20);
                try
                {
                    await last.Session.Send(new VoidSaleCommand(last.Invoice, "Error de cobro: el cliente cambió de producto"), ct);
                }
                catch (DomainException)
                {
                    // la última venta ya tuvo una devolución o una garantía: no se anula
                }
                lastSale = null;
            }
            if (_rng.NextDouble() < 0.12)
            {
                // Merma de un accesorio sin serie (los serializados se dan de baja con su serie, desde un caso o una devolución)
                var mermaAt = Evening(18 * 60 + 10, 15);
                At(day, mermaAt / 60, mermaAt % 60);
                var item = Pick(_catalog.Products.Where(p => !p.TracksSerials && !p.IsService).ToList());
                var bin = (await _stock.BinsAsync(MainWarehouse, item.Sku, ct)).OrderByDescending(b => b.Available).FirstOrDefault();
                if (bin.Available >= 2)
                {
                    await bodega.Send(new RegisterMovementCommand(item.Sku, bin.BinCode, MovementTypeCodes.AdjustmentOut, 1, day, null,
                        Pick(["Merma: producto dañado en exhibición", "Merma: empaque abierto sin reposición", "Pérdida detectada en revisión"])), ct);
                }
            }

            // Tesorería: depósitos de efectivo los sábados; gastos y pagos a proveedores a fin de mes
            if (day.DayOfWeek == DayOfWeek.Saturday && !isToday)
            {
                At(day, 20, 0);
                var cash = (await gerencia.Send(new GetChartOfAccountsQuery(), ct)).First(a => a.Code == AccountCodes.Cash).Balance;
                if (cash > 8000)
                {
                    await gerencia.Send(new CreateJournalEntryCommand(day, "Depósito del efectivo de la semana en el banco",
                        [new JournalLineSpec(AccountCodes.Bank, JournalPoster.Money(cash - 5000), 0), new JournalLineSpec(AccountCodes.Cash, 0, JournalPoster.Money(cash - 5000))]), ct);
                }
            }
            if (day.AddDays(1).Month != day.Month || (isToday && day.Day >= 25))
            {
                var expensesAt = Evening(20 * 60 + 30, 18);
                At(day, expensesAt / 60, expensesAt % 60);
                // Gastos del mes proporcionales a los días operados (el primer mes del período es parcial)
                var monthStart = new DateOnly(day.Year, day.Month, 1);
                var operated = day.DayNumber - (monthStart < start ? start : monthStart).DayNumber + 1;
                var share = Math.Min(1m, operated / (decimal)DateTime.DaysInMonth(day.Year, day.Month));
                var salaries = JournalPoster.Money((42000m + _rng.Next(0, 3000)) * share);
                var rent = JournalPoster.Money(15500m * share);
                var utilities = JournalPoster.Money((2100m + _rng.Next(0, 600)) * share);
                var office = JournalPoster.Money((900m + _rng.Next(0, 500)) * share);
                await gerencia.Send(new CreateJournalEntryCommand(day, $"Gastos del mes {day:MM/yyyy}: sueldos, alquileres y servicios",
                [
                    new JournalLineSpec("6.1.01", salaries, 0, "Planilla de sueldos"), new JournalLineSpec("6.1.02", rent, 0, "Alquiler de los locales"),
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
        // Ventas de caja: los tickets del día a día más las facturas manuales CAFC transcritas (V4.1) y los armados de PC
        // cobrados en la caja (V4.2), que también son ventas de caja; el mismo total va al resumen final (SeedResult.Tickets)
        var manualSales = billing?.ManualSales ?? 0;
        var breakdown = new List<string> { $"{tickets} del día a día" };
        if (manualSales > 0)
        {
            breakdown.Add($"{manualSales} facturas manuales CAFC transcritas");
        }
        if (tech.BuildsSold > 0)
        {
            breakdown.Add($"{tech.BuildsSold} armados de PC");
        }
        tickets += manualSales + tech.BuildsSold;
        log($"{o.Days} días de operación simulados: {tickets} ventas en caja" +
            (breakdown.Count > 1 ? $" ({string.Join(", ", breakdown.SkipLast(1))} y {breakdown[^1]})" : string.Empty) + $" y {webOrders} pedidos web.");

        // ------------------------------------------------------------------------------------ estado final para explorar la app
        At(today, Math.Clamp(realNow.Hour, 10, 20), Math.Min(realNow.Minute, 50));
        if (clock.UtcNow <= latest)
        {
            // Lo de hoy (cierres, garantías, gastos del mes) pudo quedar más tarde: el estado final va después de todo lo
            // registrado (en la demostración lo que haga la persona aparece siempre como lo más reciente)
            clock.StartAt(latest.AddMinutes(1));
        }
        await _stock.RefreshAsync(ct);
        await tech.FinalCountAsync(bodega);
        var gpuSupplier = catalog.SupplierCodes[_catalog.SupplierOf(_catalog.Products.First(p => p.Category == "GPU"))!.Code];
        var extra = _catalog.Products.Where(p => p.Category == "GPU").OrderByDescending(p => p.Popularity).Take(2).ToList();
        await bodega.Send(new CreatePurchaseOrderCommand(gpuSupplier, today.AddDays(7), "Reposición de tarjetas de video (borrador)",
            extra.Select(e => new PurchaseLineInput(e.Sku, 3, e.Cost)).ToList()), ct);

        // V4 · Para explorar: una transferencia a Santa Cruz EN TRÁNSITO (despachada hoy) y una a Cochabamba PENDIENTE
        foreach (var pending in pendingReceipts)
        {
            await ReceiveAsync(pending.Receiver, pending.Id, pending.Complete);
        }
        await _stock.RefreshAsync(ct);
        await ShipAsync(SantaCruzWarehouse, 5, 0.4, note: "Reposición de fin de semana (en tránsito)");
        var popular = _weighted.DistinctBy(x => x.Sku).Where(p => !p.IsService && _stock.Available(MainWarehouse, p.Sku) >= p.Minimum + 2).Take(4).ToList();
        if (popular.Count > 0)
        {
            var lines = new List<TransferLineInput>();
            foreach (var p in popular)
            {
                if (_stock.TryTake(MainWarehouse, p, 2, _rng, out var serials))
                {
                    lines.Add(new TransferLineInput(p.Sku, 2, serials));
                }
            }
            await bodega.Send(new CreateTransferCommand(CochabambaWarehouse, lines, "Pedido de Cochabamba para el fin de semana (pendiente de despacho)"), ct);
            transfers++;
        }

        // V4 · Webhook de la tienda (solo si este equipo tiene la clave maestra de integraciones)
        string? webhookSecret = null;
        try
        {
            webhookSecret = (await admin.Send(new CreateWebhookCommand("https://tienda.techzone.example/webhooks/minv",
                [IntegrationEvents.SaleCompleted, IntegrationEvents.TransferDispatched, IntegrationEvents.TransferReceived], "Tienda en línea (prueba)"), ct)).Secret;
        }
        catch (AccessDeniedException)
        {
            log("Webhook de prueba omitido: falta la clave maestra de integraciones (MINV_INTEGRATION_KEYS) en este equipo.");
        }

        // V4.1 · Facturas de proveedores, puntos de venta en línea, CUFD vigentes para hoy y resumen de la facturación
        var billed = billing is null ? null : await billing.FinishAsync();

        if (db.Database.IsRelational())
        {
            // Carga masiva recién hecha: estadísticas para el planificador (si no, las consultas de las pantallas son lentas
            // hasta que pasa el autovacuum)
            await db.Database.ExecuteSqlRawAsync(PostgresMaintenance.AnalyzeSql, ct);
        }
        db.ChangeTracker.Clear();
        var stats = new
        {
            Movements = await db.StockMovements.CountAsync(ct),
            Journal = await db.JournalEntries.CountAsync(ct),
            Orders = await db.PurchaseOrders.CountAsync(ct),
            Customers = await db.Set<Customer>().CountAsync(ct),   // V4.1: más los compradores eventuales facturados
        };
        var techSummary = await tech.SummaryAsync();
        log($"Listo: {stats.Movements} movimientos, {stats.Orders} órdenes de compra, {transfers} transferencias, {webOrders} pedidos web y " +
            $"{stats.Journal} asientos contables · {techSummary.Serials} series e IMEI ({techSummary.SerialsInStock} en stock), " +
            $"{techSummary.WarrantyClaims} casos RMA, {techSummary.PcBuilds} armados ({techSummary.PcBuildsSold} vendidos).");
        // El día simulado de «hoy» empieza a las 10:00 aunque la carga corra de madrugada: si el reloj simulado quedó adelante
        // de la hora real, vuelve a la hora real (lo que se registre después en este proceso no queda «en el futuro»)
        if (o.ResetClock && clock.UtcNow > DateTimeOffset.UtcNow)
        {
            clock.StartAt(DateTimeOffset.UtcNow);
        }
        return new SeedResult(o.TenantCode, o.CompanyName, users, _catalog.Products.Count, _catalog.Suppliers.Count, stats.Customers, tickets,
            stats.Orders, stats.Movements, stats.Journal, start, today, [BranchMain, BranchCochabamba, BranchSantaCruz], transfers, webOrders, apiKey.Name,
            apiKey.Token, webhookSecret, billed, techSummary);
    }

    // ---------------------------------------------------------------------------------------------- catálogo del rubro
    /// <summary>Lo creado del catálogo: posición de cada producto, código de proveedor y de cliente de cada código del JSON.</summary>
    private sealed record SeededCatalog(IReadOnlyDictionary<string, string> Bins, IReadOnlyDictionary<string, string> SupplierCodes,
        IReadOnlyDictionary<string, string> CustomerCodes, IReadOnlyList<SeedCustomer> Customers, int SpecValues)
    {
        /// <summary>Cliente habitual de una sucursal (los que compran en ella), elegido al azar.</summary>
        public string CustomerFor(string branch, Random rng)
        {
            var mine = Customers.Where(c => c.Branch == branch).ToList();
            return (mine.Count > 0 ? mine : Customers)[rng.Next(mine.Count > 0 ? mine.Count : Customers.Count)].Code;
        }
    }

    /// <summary>
    /// Catálogo de Tech Zone Gaming con los casos de uso del catálogo (regla A-13): categorías con subcategorías,
    /// especificaciones con sus opciones y claves de compatibilidad, proveedores, clientes, productos con precio, costo,
    /// mínimo/máximo, posición, imagen, perfil técnico (serie o IMEI y garantía) y ficha técnica completa. Lo que no tiene
    /// caso de uso (topología del almacén, cajas extra, categorías de cliente, la unidad SERV y las marcas) se registra en el
    /// contexto de la sesión del administrador, como el aprovisionamiento.
    /// </summary>
    private async Task<SeededCatalog> SeedCatalogAsync(SignedIn admin, IReadOnlyList<SignedIn> admins, MinvWriteDbContext db, Guid tenantId,
        Action<string> log, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.FirstAsync(w => w.Code == MainWarehouse, ct);
        var picking = await db.LocationTypes.FirstAsync(t => t.Code == "PICKING", ct);
        var priceList = await db.PriceLists.FirstAsync(p => p.IsDefault, ct);
        foreach (var category in _catalog.CustomerCategories.Where(c => c.IsNew))
        {
            db.CustomerCategories.Add(new CustomerCategory(tenantId, category.Code, category.Name, priceList.Id));
        }
        db.POSRegisters.AddRange(new PosRegister(tenantId, warehouse.BranchId, warehouse.Id, "CAJA02", "Caja 2", null),
            new PosRegister(tenantId, warehouse.BranchId, warehouse.Id, "CAJA03", "Caja 3 (servicio técnico)", null));
        foreach (var unit in _catalog.Units.Where(u => u.IsNew))
        {
            db.Set<UnitOfMeasure>().Add(new UnitOfMeasure(tenantId, unit.Code, unit.Name, unit.AllowsDecimals, unit.Description));
        }
        var brands = _catalog.Brands.ToDictionary(b => b.Name, b => new Brand(tenantId, b.Name), StringComparer.Ordinal);
        db.Set<Brand>().AddRange(brands.Values);
        // Una zona por categoría raíz (A, B, C…) con tres estanterías en el almacén central
        var roots = _catalog.Categories.Where(c => c.Parent is null).ToList();
        var binsByRoot = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < roots.Count; i++)
        {
            var zoneCode = ((char)('A' + i)).ToString();
            var z = new Zone(tenantId, warehouse.BranchId, warehouse.Id, zoneCode, roots[i].Name, picking.Id);
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
            binsByRoot[roots[i].Code] = list;
        }
        await db.SaveChangesAsync(ct);

        // Árbol de categorías (las madres primero) y especificaciones técnicas con sus opciones (regla T-01)
        foreach (var category in _catalog.Categories)
        {
            await admin.Send(new SaveCategoryCommand(category.Code, category.Name, category.Parent), ct);
        }
        await InParallelAsync(admins, _catalog.Specs, (session, spec) => session.Send(new SaveSpecDefinitionCommand(spec.Category, spec.Code, spec.Name,
            spec.Unit, spec.DataType, spec.IsMultiValued, spec.IsFilterable, spec.IsRequired, spec.CompatibilityKey, spec.Order, spec.Options), ct), ct);

        // Proveedores y clientes (ficticios, con su documento fiscal: NIT las empresas e instituciones, CI las personas)
        var suppliers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in _catalog.Suppliers)
        {
            suppliers[s.Code] = await admin.Send(new SaveSupplierCommand(null, s.Name, s.TaxId, s.LeadTimeDays, s.Contact, s.Phone, s.Email, true), ct);
        }
        var customers = new Dictionary<string, string>(StringComparer.Ordinal);
        var seedCustomers = new List<SeedCustomer>();
        foreach (var c in _catalog.Customers)
        {
            var code = await admin.Send(new SaveCustomerCommand(null, c.Name, c.DocumentNumber, c.Email, c.Phone, c.Category, true), ct);
            customers[c.Code] = code;
            seedCustomers.Add(new SeedCustomer(code, c.Name, c.DocumentNumber, c.Email, c.IsCompany, c.Branch));
        }

        // Productos con precio, costo, política de stock, posición, imagen, perfil técnico y ficha técnica completa
        var bins = new Dictionary<string, string>(StringComparer.Ordinal);
        var products = new List<(TechProduct Product, string Bin, string? Barcode)>();
        foreach (var p in _catalog.Products)
        {
            var rootBins = binsByRoot[_catalog.Root(p.Category)];
            var bin = rootBins[_rng.Next(rootBins.Count)];
            bins[p.Sku] = bin;
            products.Add((p, bin, p.IsService ? null : Ean13("777" + _rng.Next(100000000, 999999999).ToString(CultureInfo.InvariantCulture))));
        }
        await InParallelAsync(admins, products, async (session, x) =>
        {
            var p = x.Product;
            var description = $"{p.Name}. Marca {p.Brand} · {string.Join(" › ", _catalog.Lineage(p.Category).Reverse().Select(c => _catalog.Category(c).Name))}" +
                              (p.TracksSerials ? $" · garantía {p.WarrantyMonths} meses, control por {(p.SerialKind == SerialKind.Imei ? "IMEI" : "número de serie")}." : ".");
            var supplier = _catalog.SupplierOf(p) is { } s ? suppliers[s.Code] : null;   // los servicios de la tienda no tienen proveedor
            await session.Send(new SaveProductCommand(null, p.Sku, p.Name, description, p.Category, p.Unit, supplier,
                p.Minimum, p.Maximum, p.Cost, p.Price, x.Barcode, true, x.Bin), ct);
            await session.Send(new SaveProductTechCommand(p.Sku, p.TracksSerials, p.SerialKind, p.WarrantyMonths,
                p.Specs.Select(s => new ProductSpecInput(s.Key, s.Value)).ToList()), ct);
            await session.Send(new SetProductImageCommand(p.Sku, ProductImageLibrary.ForSku(p.Sku), "image/png", p.Image + ".png"), ct);
        }, ct);
        var specValues = _catalog.Products.Sum(p => p.Specs.Values.Sum(v => v.Count));

        // Marca y modelo de cada producto (el catálogo no tiene caso de uso de marcas: se asignan como dato maestro)
        db.ChangeTracker.Clear();
        var models = new Dictionary<(string Brand, string Model), BrandModel>();
        var brandIds = await db.Set<Brand>().ToDictionaryAsync(b => b.Name, b => b.Id, StringComparer.Ordinal, ct);
        foreach (var product in await db.Products.ToListAsync(ct))
        {
            var p = _catalog.Product(product.Code);
            var name = p.Name.Length <= 80 ? p.Name : p.Sku;
            if (!models.TryGetValue((p.Brand, name), out var model))
            {
                model = new BrandModel(tenantId, brandIds[p.Brand], name);
                models[(p.Brand, name)] = model;
                db.Set<BrandModel>().Add(model);
            }
            product.AssignModel(model.Id);
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        log($"Catálogo de tecnología: {_catalog.Categories.Count} categorías, {_catalog.Specs.Count} especificaciones, {_catalog.Brands.Count} marcas, " +
            $"{_catalog.Products.Count} productos ({_catalog.Products.Count(p => p.TracksSerials)} con serie o IMEI) con ficha técnica ({specValues} valores) " +
            $"e imagen, {_catalog.Suppliers.Count} proveedores, {customers.Count} clientes y {bins.Values.Distinct().Count()} posiciones.");
        return new SeededCatalog(bins, suppliers, customers, seedCustomers, specValues);
    }

    // ---------------------------------------------------------------------------------------------- utilidades
    /// <summary>Registra elementos INDEPENDIENTES entre sí (sin numeración correlativa ni filas compartidas) repartidos entre
    /// varias sesiones que trabajan a la vez (cada sesión es su propio contexto de datos, como varios usuarios).</summary>
    private static Task InParallelAsync<T>(IReadOnlyList<SignedIn> sessions, IReadOnlyList<T> items, Func<SignedIn, T, Task> action, CancellationToken ct) =>
        Task.WhenAll(sessions.Select((session, k) => Task.Run(async () =>
        {
            for (var i = k; i < items.Count; i += sessions.Count)
            {
                await action(session, items[i]);
            }
        }, ct)));

    /// <summary>Equipo de una sucursal: su almacén, su caja, su bodega y quien arma las cotizaciones.</summary>
    private sealed record BranchTeam(string Branch, string Warehouse, SignedIn Cashier, SignedIn Keeper, SignedIn Seller);

    private sealed class SignedIn(IServiceScope scope) : ISeedSession, IDisposable
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

    /// <summary>
    /// Líneas de una venta con lo que HAY en el almacén (según la vista del inventario de la carga): 1 a
    /// <paramref name="maxLines"/> productos elegidos por popularidad, con las series (o IMEI) de las unidades serializadas
    /// escaneadas del stock del almacén. Lo tomado se descuenta; si la venta se rechaza, se devuelve con
    /// <see cref="SeedStock.GiveBack"/>.
    /// </summary>
    private List<SaleLineInput> PickLines(string warehouse, int maxLines, bool allowServices, Func<TechProduct, bool>? filter = null)
    {
        var lines = new List<SaleLineInput>();
        var count = _rng.Next(1, maxLines + 1);
        for (var tries = 0; lines.Count < count && tries < 16; tries++)
        {
            var p = _weighted[_rng.Next(_weighted.Count)];
            if (lines.Any(l => l.Sku == p.Sku) || (p.IsService && !allowServices) || (filter is not null && !filter(p)))
            {
                continue;
            }
            var quantity = p.Price <= 450 && _rng.NextDouble() < 0.3 ? 2 : 1;
            if (_stock.TryTake(warehouse, p, quantity, _rng, out var serials))
            {
                lines.Add(new SaleLineInput(p.Sku, quantity, _rng.NextDouble() < 0.08 ? 5 : 0, serials));
            }
        }
        return lines;
    }

    /// <summary>Series nuevas de un producto (recepción de compra o saldo inicial): serie del fabricante (letras de la marca,
    /// año y 8 caracteres) o IMEI de 15 dígitos con su dígito de Luhn. Únicas en toda la empresa.</summary>
    private List<string> NewSerials(TechProduct product, int count)
    {
        const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var prefix = new string(product.Brand.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).Take(3).ToArray()).PadRight(3, 'X');
        var result = new List<string>(count);
        while (result.Count < count)
        {
            string serial;
            if (product.SerialKind == SerialKind.Imei)
            {
                // TAC de 8 dígitos fijo por producto + número de serie de 6 dígitos + dígito verificador
                var tac = "35" + (Math.Abs(StableHash(product.Sku)) % 1_000_000).ToString("000000", CultureInfo.InvariantCulture);
                var first14 = tac + _rng.Next(0, 1_000_000).ToString("000000", CultureInfo.InvariantCulture);
                serial = first14 + Imei.CheckDigit(first14).ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                var body = new StringBuilder(prefix).Append(clock.UtcNow.Year % 100);
                for (var i = 0; i < 8; i++)
                {
                    body.Append(Alphabet[_rng.Next(Alphabet.Length)]);
                }
                serial = body.ToString();
            }
            if (_serials.Add(serial))
            {
                result.Add(serial);
            }
        }
        return result;
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in text)
            {
                hash = (hash * 31) + ch;
            }
            return hash == int.MinValue ? 0 : hash;
        }
    }

    private T Pick<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];

    /// <summary>Contraseña aleatoria fácil de dictar: «Illimani-4827» (letras, guion y números; nunca se versiona).</summary>
    private static string NewPassword() =>
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
