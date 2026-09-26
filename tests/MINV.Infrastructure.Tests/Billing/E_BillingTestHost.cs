using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Catalog;
using MINV.Application.Iam;
using MINV.Application.Inventory.Movements;
using MINV.Application.Sales;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Infrastructure.Billing.Simulator;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Provisioning;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>Sesión de un usuario en el anfitrión de prueba (su scope DI y su MediatR con la tubería completa). Como el
/// transporte local del escritorio (<c>LocalTransport</c>), cada envío es una unidad de trabajo con el rastreo limpio.</summary>
internal sealed record E_Session(IServiceScope Scope, IMediator Mediator) : IDisposable
{
    public Task<T> Send<T>(IRequest<T> request)
    {
        Scope.ServiceProvider.GetRequiredService<IMinvDbContext>().ClearTracking();
        return Mediator.Send(request);
    }

    public void Dispose() => Scope.Dispose();
}

/// <summary>Las pruebas del agente E comparten el anfitrión (reloj y simulador): corren de a una.</summary>
[CollectionDefinition(Name)]
public sealed class E_BillingCollection
{
    public const string Name = "E_Billing";
}

/// <summary>
/// V4.1 · Anfitrión de prueba de la facturación (agente E): la base en memoria real (<see cref="MinvWriteDbContext"/>), los
/// casos de uso con la tubería de MediatR y el simulador del SIN EN PROCESO (<see cref="InProcessSiatGateway"/> con su
/// <see cref="SiatSimulatorEngine"/> controlable: <c>Simulator.Available = false</c> simula el corte). Arma una empresa que
/// FACTURA: ajustes activos en el ambiente 2, perfil con el token cifrado por el protector de la demostración, sucursal
/// del Padrón 0, punto de venta 0 y el punto de venta de la caja CAJA01 (registrado en el simulador), CUIS y CUFD pedidos
/// con <see cref="SiatCodeManager"/>, catálogos sincronizados y homologación de todos los productos, unidades y medios de
/// pago. Reloj fijo de arranque (25/09/2026 10:00 en Bolivia) que se puede mover (<see cref="MoveClockTo"/>).
/// <para>Un solo contenedor DI (una base en memoria) para todas las pruebas: EF Core admite pocas configuraciones de
/// contexto por proceso. Cada prueba es una empresa NUEVA con su propio NIT (el simulador separa su estado por NIT).</para>
/// </summary>
internal sealed class E_BillingTestHost : IAsyncDisposable
{
    public const string Token = "token-delegado-de-prueba-E-0001";
    public const string SystemCode = "SIS-MINV-E";

    private static readonly Lazy<ServiceProvider> Shared = new(() =>
    {
        var services = new ServiceCollection();
        services.AddMinvApplication();
        services.AddMinvDemoInfrastructure();
        return services.BuildServiceProvider();
    });

    private static int _sequence;
    public const string Activity = SiatSimulatorCatalogs.MainActivity;
    public const string Register = "CAJA01";
    public const string Bin = "ALM01-GENERAL";

    /// <summary>Productos de la empresa de prueba: SKU, nombre, unidad, costo, precio, existencia inicial.</summary>
    public static readonly (string Sku, string Name, string Unit, decimal Cost, decimal Price, decimal Stock)[] Products =
    [
        ("FER-001", "Martillo carpintero 16 oz", "UND", 30m, 48.50m, 100),
        ("FER-002", "Tornillo drywall (caja x100)", "CAJA", 12m, 18.90m, 100),
        ("ELE-001", "Cable dúplex 2x14 AWG (metro)", "MT", 2.5m, 4.25m, 500),
        ("AMO-001", "Amortiguador delantero", "UND", 450m, 700m, 20),
        ("TOR-001", "Tornillos de sujeción (juego)", "UND", 40m, 75m, 50),
    ];

    /// <summary>Arranque del reloj: 25/09/2026 14:00 UTC = 10:00 en Bolivia.</summary>
    public static readonly DateTimeOffset Start = new(2026, 9, 25, 14, 0, 0, TimeSpan.Zero);

    private E_BillingTestHost(ServiceProvider services, int sequence)
    {
        Services = services;
        TenantCode = "FE" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Nit = 2_000_000_000L + sequence;
    }

    public ServiceProvider Services { get; }

    /// <summary>Empresa de ESTA prueba (una nueva por prueba).</summary>
    public string TenantCode { get; }

    /// <summary>NIT del emisor de ESTA prueba.</summary>
    public long Nit { get; }

    public DemoClock Clock => Services.GetRequiredService<DemoClock>();

    public SiatSimulatorEngine Simulator => Services.GetRequiredService<SiatSimulatorEngine>();

    public Guid TenantId { get; private set; }

    public Guid BranchId { get; private set; }

    public string AdminEmail { get; } = "admin@fiscal-e.example";

    public string AdminPassword { get; } = "Admin-" + Guid.NewGuid().ToString("N")[..10];

    public string CashierEmail { get; } = "cajera@fiscal-e.example";

    public string CashierPassword { get; } = "Caja-" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Punto de venta 0 (sin punto de venta: API y oficina).</summary>
    public Guid PointZeroId { get; private set; }

    /// <summary>Punto de venta registrado en el SIN y vinculado a la caja CAJA01.</summary>
    public Guid CashPointId { get; private set; }

    public static Task<E_BillingTestHost> CreateAsync() => CreateAsync(Shared.Value);

    /// <summary>V4.2 · La misma empresa que factura sobre otro contenedor (p. ej. PostgreSQL real con el simulador en proceso,
    /// un <c>DemoClock</c> como reloj y un protector de secretos efímero).</summary>
    public static async Task<E_BillingTestHost> CreateAsync(ServiceProvider services)
    {
        var host = new E_BillingTestHost(services, Interlocked.Increment(ref _sequence));
        host.Clock.StartAt(Start);
        host.Simulator.Available = true;
        await host.ProvisionAsync();
        await host.PrepareBillingAsync();
        return host;
    }

    /// <summary>El contenedor es compartido: solo se deja el simulador encendido para la prueba siguiente.</summary>
    public ValueTask DisposeAsync()
    {
        Simulator.Available = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>Mueve el reloj de la aplicación y del simulador (la misma hora).</summary>
    public void MoveClockTo(DateTimeOffset instant) => Clock.StartAt(instant);

    /// <summary>Hora de Bolivia (sin zona) de un instante (la hora fiscal de la empresa de prueba).</summary>
    public static DateTime Bolivia(DateTimeOffset instant) => DateTime.SpecifyKind(instant.ToOffset(TimeSpan.FromHours(-4)).DateTime, DateTimeKind.Unspecified);

    public DateTime FiscalNow => Bolivia(Clock.UtcNow);

    // ------------------------------------------------------------------------------------------------ sesiones
    public async Task<E_Session> SignInAsync(string email, string password)
    {
        var scope = Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.Send(new LoginCommand(TenantCode, email, password, "PRUEBAS", "test"));
        return new E_Session(scope, mediator);
    }

    public Task<E_Session> AdminAsync() => SignInAsync(AdminEmail, AdminPassword);

    public Task<E_Session> CashierAsync() => SignInAsync(CashierEmail, CashierPassword);

    /// <summary>Scope de plataforma (empresa fijada, todas las sucursales, sin usuario): como el trabajo en segundo plano.</summary>
    public IServiceScope PlatformScope()
    {
        var scope = Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(TenantId);
        tenant.SetBranches(BranchScope.Unrestricted);
        return scope;
    }

    /// <summary>Consulta (o cambia) la base con un contexto nuevo de plataforma.</summary>
    public async Task<T> DbAsync<T>(Func<MinvWriteDbContext, Task<T>> action)
    {
        using var scope = PlatformScope();
        return await action(scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>());
    }

    /// <summary>Trabajo automático de la facturación (como lo ejecuta el servicio en segundo plano).</summary>
    public async Task<T> WorkerAsync<T>(Func<SiatWorker, Task<T>> action)
    {
        using var scope = PlatformScope();
        return await action(scope.ServiceProvider.GetRequiredService<SiatWorker>());
    }

    public Task<FiscalDocument> DocumentAsync(Guid id) =>
        DbAsync(db => db.Set<FiscalDocument>().AsNoTracking().Include(d => d.Lines).Include(d => d.NoteReference).FirstAsync(d => d.Id == id));

    public Task<SiatPointOfSale> PointAsync(Guid id) => DbAsync(db => db.Set<SiatPointOfSale>().AsNoTracking().FirstAsync(p => p.Id == id));

    public Task<decimal> OnHandAsync(string sku) => DbAsync(async db => await (from l in db.Set<StockLevel>()
                                                                             join b in db.Set<Batch>() on l.BatchId equals b.Id
                                                                             join v in db.Set<ProductVariant>() on b.VariantId equals v.Id
                                                                             where v.Sku == sku
                                                                             select l.QuantityOnHand).SumAsync());

    /// <summary>Código del catálogo de eventos significativos sincronizado cuya descripción contiene el texto.</summary>
    public Task<int> EventCodeAsync(string text) => DbAsync(async db =>
        (await db.Set<SiatCatalogItem>().Where(i => i.Catalog == SiatCatalogNames.SignificantEvents).ToListAsync())
        .First(i => FiscalIssuer.Plain(i.Description).Contains(FiscalIssuer.Plain(text), StringComparison.Ordinal)).Code);

    /// <summary>Cobro en la caja CAJA01 (efectivo) con los datos de facturación dados.</summary>
    public static Task<CheckoutResult> SellAsync(E_Session cashier, FiscalBuyerInput? buyer, params SaleLineInput[] lines) =>
        cashier.Send(new CheckoutCommand("CF", "EFECTIVO", lines, 100_000m, null, buyer));

    public static FiscalBuyerInput Ci(string number = "4567890", string? name = "Juan Pérez", string? email = null) =>
        new(SiatCodes.DocumentCi, number, null, name, email);

    public static FiscalBuyerInput NitBuyer(string number = "1234567019", string? name = "CONSTRUCTORA ANDINA S.R.L.") =>
        new(SiatCodes.DocumentNit, number, null, name, null);

    // ------------------------------------------------------------------------------------------------ armado
    private async Task ProvisionAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var provisioned = await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().ProvisionAsync(new ProvisionTenantRequest(TenantCode,
                "Ferretería Fiscal S.R.L.", Nit.ToString(System.Globalization.CultureInfo.InvariantCulture), AdminEmail, "Administración", AdminPassword,
                MinBusinessDate: new DateOnly(2026, 1, 1)));
            TenantId = provisioned.TenantId;
            BranchId = provisioned.BranchId;
        }
        using var admin = await AdminAsync();
        await admin.Send(new SaveCategoryCommand("FER", "Ferretería"));
        foreach (var (sku, name, unit, cost, price, _) in Products)
        {
            await admin.Send(new SaveProductCommand(null, sku, name, null, "FER", unit, null, 1, 1000, cost, price, null, true, Bin));
        }
        var today = DateOnly.FromDateTime(Bolivia(Clock.UtcNow));
        foreach (var (sku, _, _, _, _, stock) in Products)
        {
            await admin.Send(new RegisterMovementCommand(sku, Bin, MovementTypeCodes.InitialBalance, stock, today, "INV-INICIAL", "Inventario inicial"));
        }
        await admin.Send(new SaveUserCommand(null, CashierEmail, "Cajera Fiscal", RoleCodes.Cashier, true, CashierPassword, ["CM"]));
        await admin.Send(new ResetUserPasswordCommand(CashierEmail, CashierPassword, MustChange: false));
        using var cashier = await CashierAsync();
        await cashier.Send(new OpenPosSessionCommand(Register, 500m));
    }

    private async Task PrepareBillingAsync()
    {
        using var scope = PlatformScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MinvWriteDbContext>();
        var protector = sp.GetRequiredService<ISecretProtector>();
        var clock = sp.GetRequiredService<IClock>();
        var gateway = sp.GetRequiredService<ISiatGateway>();
        var now = clock.UtcNow;
        var settings = new SiatSettings(TenantId, Nit, "FERRETERÍA FISCAL S.R.L.", SystemCode, SiatCodes.EnvironmentTest);
        settings.Enable();
        var profile = new SiatEnvironmentProfile(TenantId, SiatCodes.EnvironmentTest,
            SiatEndpointSet.ForBaseUrl("https://pilotosiatservicios.impuestos.gob.bo"), "https://pilotosiat.impuestos.gob.bo/consulta/QR", 15);
        profile.SetToken(protector.Protect(Token), protector.CurrentKeyId, null, now);
        var branch = new SiatBranch(TenantId, BranchId, 0, "La Paz", "2-2445566");
        var pointZero = new SiatPointOfSale(TenantId, BranchId, SiatCodes.EnvironmentTest, 0, 0, "Casa matriz (sin punto de venta)", null, null, now);
        db.AddRange(settings, profile, branch, pointZero);
        await db.SaveChangesAsync();

        var lookups = new BillingLookups(db, protector, clock);
        var codes = new SiatCodeManager(lookups, gateway);
        var context = await lookups.ContextAsync(default);
        var cuisZero = await codes.EnsureCuisAsync(context, branch, pointZero, false, default);
        await db.SaveChangesAsync();
        var registered = await gateway.RegisterPointOfSaleAsync(context.Connection, BillingLookups.Place(branch, pointZero), cuisZero.Code,
            SiatCodes.PointOfSaleCashier, "Caja 1", "Caja 1 de la casa matriz");
        var register = await db.Set<PosRegister>().FirstAsync(r => r.Code == Register);
        var cashPoint = new SiatPointOfSale(TenantId, BranchId, SiatCodes.EnvironmentTest, registered.Code!.Value, SiatCodes.PointOfSaleCashier, "Caja 1",
            "Caja 1 de la casa matriz", register.Id, now);
        db.Add(cashPoint);
        await db.SaveChangesAsync();
        await codes.EnsureCuisAsync(context, branch, cashPoint, false, default);
        await db.SaveChangesAsync();   // el gestor de códigos no guarda: el CUFD se pide con el CUIS ya guardado
        await codes.EnsureCufdAsync(context, branch, pointZero, false, default);
        await codes.EnsureCufdAsync(context, branch, cashPoint, false, default);
        await db.SaveChangesAsync();
        var sync = await codes.SyncCatalogsAsync(context, BillingLookups.Place(branch, pointZero), cuisZero.Code, null, null, default);
        Assert.Empty(sync.Errors);
        await db.SaveChangesAsync();

        // Homologación: productos (actividad principal), unidades y medios de pago contra los catálogos sincronizados
        var sinProduct = await db.Set<SiatProduct>().Where(p => p.ActivityCode == Activity).OrderBy(p => p.ProductCode).FirstAsync();
        foreach (var product in await db.Set<Product>().ToListAsync())
        {
            db.Add(new ProductSiatCode(TenantId, product.Id, Activity, sinProduct.ProductCode));
        }
        var unitCodes = new Dictionary<string, int>
        {
            ["UND"] = 57, ["CAJA"] = 6, ["PAQ"] = 42, ["PAR"] = 43, ["ROLLO"] = 68, ["KG"] = 22, ["GL"] = 16, ["LT"] = 23, ["MT"] = 30,
        };
        foreach (var unit in await db.Set<UnitOfMeasure>().ToListAsync())
        {
            db.Add(new UnitSiatCode(TenantId, unit.Id, unitCodes.GetValueOrDefault(unit.Code, 62)));
        }
        var paymentCodes = new Dictionary<string, int> { ["EFECTIVO"] = 1, ["TARJETA"] = 2, ["QR"] = 33, ["TRANSFERENCIA"] = 7 };
        foreach (var method in await db.Set<PaymentMethod>().ToListAsync())
        {
            db.Add(new PaymentMethodSiatCode(TenantId, method.Id, paymentCodes.GetValueOrDefault(method.Code, 5)));
        }
        await db.SaveChangesAsync();
        PointZeroId = pointZero.Id;
        CashPointId = cashPoint.Id;
    }
}
