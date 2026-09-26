using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MINV.Application;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Catalog;
using MINV.Application.Corporate;
using MINV.Application.Iam;
using MINV.Application.Inventory.Movements;
using MINV.Application.Sales;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Infrastructure.Billing.Simulator;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Persistence.Interceptors;
using MINV.Infrastructure.Provisioning;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>
/// V4.1 · Anfitrión de pruebas de la facturación (agente D): la base en memoria real (MinvWriteDbContext, filtros, guardas),
/// la tubería MediatR completa (validación, permisos, licencias, auditoría) y el simulador del SIN EN PROCESO (el mismo
/// contrato que el cliente SOAP). El reloj de la demostración arranca a una hora fija (10:00 en Bolivia) y el simulador
/// marca la misma hora.
/// </summary>
internal sealed class D_BillingTestHost : IAsyncDisposable
{
    public const string TenantCode = "FACT";
    public const string CompanyName = "Ferretería El Tornillo S.R.L.";
    public const string AdminEmail = "admin@tornillo.example";
    public const string AdminName = "Administración Tornillo";
    public const string AdminPassword = "Prueba-Siat-2026";
    public const long Nit = 1003579028;
    public const string SystemCode = "SIS-MINV-PRUEBA";
    public const string Token = "token-delegado-de-prueba-0001";
    public const string QrBase = "https://pilotosiat.impuestos.gob.bo/consulta/QR";

    /// <summary>25/09/2026 14:00 UTC = 10:00 en La Paz.</summary>
    public static readonly DateTimeOffset Start = new(2026, 9, 25, 14, 0, 0, TimeSpan.Zero);

    private D_BillingTestHost(ServiceProvider services) => Services = services;

    public ServiceProvider Services { get; }

    public SiatSimulatorEngine Simulator => Services.GetRequiredService<SiatSimulatorEngine>();

    public DemoClock Clock => Services.GetRequiredService<DemoClock>();

    /// <summary>
    /// Raíz en memoria COMPARTIDA por todos los anfitriones de estas pruebas (cada uno con su propia base, por nombre): EF Core
    /// arma un proveedor interno por raíz y, pasados 20 en el mismo proceso, lanza ManyServiceProvidersCreatedWarning. Así
    /// estas pruebas usan uno solo, sin cambiar lo que hace <c>AddMinvDemoInfrastructure</c>.
    /// </summary>
    private static readonly InMemoryDatabaseRoot SharedRoot = new();

    public static async Task<D_BillingTestHost> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddMinvApplication();
        services.AddMinvDemoInfrastructure();
        var database = "minv-facturacion-" + Guid.NewGuid().ToString("N");
        services.RemoveAll<DbContextOptions<MinvWriteDbContext>>();
        services.AddDbContextFactory<MinvWriteDbContext>((provider, options) =>
        {
            DependencyInjection.ConfigureInMemory(options, database, SharedRoot);
            options.AddInterceptors(provider.GetRequiredService<MinvSaveChangesInterceptor>());
        }, ServiceLifetime.Scoped);
        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<DemoClock>().StartAt(Start);
        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().ProvisionAsync(new ProvisionTenantRequest(TenantCode, CompanyName,
                Nit.ToString(System.Globalization.CultureInfo.InvariantCulture), AdminEmail, AdminName, AdminPassword));
        }
        return new D_BillingTestHost(sp);
    }

    public async Task<D_Session> SignInAsync(string email = AdminEmail, string password = AdminPassword)
    {
        var scope = Services.CreateScope();
        var login = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand(TenantCode, email, password, "PRUEBAS", "4.1.0"));
        return new D_Session(scope, login);
    }

    /// <summary>Configuración mínima completa: conexión del ambiente 2 con token, casa matriz (0) y El Alto (1) en el Padrón,
    /// empresa (activada o no) y, si se pide, «Preparar SIAT» (punto 0, CUIS, CUFD, hora y catálogos).</summary>
    public static async Task<SiatMaintenanceResult?> ConfigureAsync(D_Session admin, bool enable = true, bool prepare = true, bool elAlto = true,
        DateOnly? tokenValidUntil = null)
    {
        await admin.Send(new SaveSiatProfileCommand(SiatCodes.EnvironmentTest, SiatEndpointSet.ForBaseUrl("http://127.0.0.1:5095"), QrBase, 15, Token,
            tokenValidUntil ?? new DateOnly(2027, 12, 31)));
        await admin.Send(new SaveSiatBranchCommand("CM", 0, "La Paz", "2800000"));
        if (elAlto)
        {
            await admin.Send(new CreateBranchCommand("EA", "Sucursal El Alto", "ALMEA", "Almacén El Alto"));
            await admin.Send(new SaveSiatBranchCommand("EA", 1, "El Alto", null));
        }
        await admin.Send(new SaveSiatSettingsCommand(Nit, CompanyName, SystemCode, SiatCodes.EnvironmentTest, null, null, enable));
        return prepare ? await admin.Send(new PrepareSiatCommand()) : null;
    }

    /// <summary>Producto con precio y existencia en la casa matriz.</summary>
    public static async Task CreateProductAsync(D_Session admin, string sku, string name, string category = "FER", decimal price = 45m,
        decimal stock = 50m, bool active = true)
    {
        var categories = await admin.Db.Set<Category>().Select(c => c.Code).ToListAsync();
        if (!categories.Contains(category))
        {
            await admin.Send(new SaveCategoryCommand(category, category == "FER" ? "Ferretería" : category == "SEG" ? "Seguridad industrial" : category));
        }
        await admin.Send(new SaveProductCommand(null, sku, name, null, category, "UND", null, 1, 100, Math.Round(price / 1.5m, 2), price, null, active));
        if (stock > 0)
        {
            await admin.Send(new RegisterMovementCommand(sku, "ALM01-GENERAL", MovementTypeCodes.InitialBalance, stock, null, "INV-INICIAL", "Saldo inicial"));
        }
    }

    /// <summary>Venta de caja (abre el turno de CAJA01 si hace falta). Se hace ANTES de activar la facturación.</summary>
    public static async Task<CheckoutResult> SellAsync(D_Session admin, params SaleLineInput[] lines)
    {
        var open = await admin.Db.Set<PosSession>().AnyAsync(s => s.Status == PosSessionStatus.Open && s.OpenedByUserId == admin.Login.UserId);
        if (!open)
        {
            await admin.Send(new OpenPosSessionCommand("CAJA01", 100m));
        }
        return await admin.Send(new CheckoutCommand("CF", "EFECTIVO", lines, 10_000m));
    }

    public async ValueTask DisposeAsync() => await Services.DisposeAsync();
}

/// <summary>Sesión iniciada (un scope DI, como el escritorio): cada caso de uso parte con el rastreo limpio.</summary>
internal sealed class D_Session(IServiceScope scope, LoginResult login) : IDisposable
{
    public LoginResult Login { get; } = login;

    public IServiceProvider Services => scope.ServiceProvider;

    public IMinvDbContext Db => scope.ServiceProvider.GetRequiredService<IMinvDbContext>();

    public Task<T> Send<T>(IRequest<T> request)
    {
        Db.ClearTracking();
        return scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    /// <summary>Lookups y códigos del SIN con los servicios de este scope (para llamar al mantenimiento directo).</summary>
    public (BillingLookups Lookups, SiatCodeManager Codes) Billing()
    {
        var lookups = new BillingLookups(Db, Services.GetService<ISecretProtector>(), Services.GetRequiredService<IClock>());
        return (lookups, new SiatCodeManager(lookups, Services.GetRequiredService<ISiatGateway>()));
    }

    public void Dispose() => scope.Dispose();
}

/// <summary>
/// V4.1 · Documentos fiscales de prueba emitidos con el DOMINIO (FiscalDocument.IssueInvoice / IssueCreditNote) y el
/// serializador real (XML validado contra el XSD), con el punto de venta, CUIS y CUFD que dejó «Preparar SIAT». Así las
/// consultas se prueban sin depender de la emisión desde la caja.
/// </summary>
internal static class D_Documents
{
    public const string Legend = "Ley N° 453: Tienes derecho a recibir información sobre las características y contenidos de los servicios que utilices.";

    public sealed record Place(SiatPointOfSale Point, SiatBranch Mapping, SiatCuis Cuis, SiatCufd Cufd, SiatSettings Settings);

    public static async Task<Place> PlaceAsync(D_Session session, string branchCode = "CM", int pointCode = 0)
    {
        var db = session.Db;
        db.ClearTracking();
        var branch = await db.Set<MINV.Domain.Warehousing.Branch>().FirstAsync(b => b.Code == branchCode);
        var point = await db.Set<SiatPointOfSale>().FirstAsync(p => p.BranchId == branch.Id && p.Code == pointCode && p.ClosedAt == null);
        var mapping = await db.Set<SiatBranch>().FirstAsync(b => b.BranchId == branch.Id);
        var cuis = await db.Set<SiatCuis>().Where(c => c.PointOfSaleId == point.Id).OrderByDescending(c => c.ObtainedAt).FirstAsync();
        var cufd = await db.Set<SiatCufd>().Where(c => c.PointOfSaleId == point.Id).OrderByDescending(c => c.ObtainedAt).FirstAsync();
        var settings = await db.Set<SiatSettings>().FirstAsync();
        return new Place(point, mapping, cuis, cufd, settings);
    }

    /// <summary>Hora fiscal de ahora (Bolivia, con milisegundos) desplazada unos segundos para no repetir el CUF.</summary>
    public static DateTime FiscalNow(D_Session session, int offsetSeconds = 0) =>
        MINV.Tests.Siat.SiatTestKit.Bolivia(session.Services.GetRequiredService<IClock>().UtcNow.AddSeconds(offsetSeconds));

    public static FiscalLineInput Line(string sku, string description, decimal quantity, decimal price, decimal? discount = null, int? tx = null,
        int sinProduct = 1001903, string activity = "4752100") =>
        new(null, activity, sinProduct, sku, description, quantity, 57, price, discount, tx);

    /// <summary>Emite (y guarda) una factura: con su XML, su evento «Issued» y, si se pide, validada (908).</summary>
    public static async Task<FiscalDocument> InvoiceAsync(D_Session session, Place place, long number, IReadOnlyList<FiscalLineInput> lines,
        Guid? invoiceId = null, bool accept = true, DateTime? issuedAt = null, string buyerName = "Juan Pérez", string buyerDocument = "5115889",
        int documentType = SiatCodes.DocumentCi, decimal additionalDiscount = 0m, int emission = SiatCodes.EmissionOnline, string? cafc = null,
        Guid? eventId = null, Guid? replaces = null)
    {
        var db = session.Db;
        var now = session.Services.GetRequiredService<IClock>().UtcNow;
        var emissionData = new FiscalEmission(place.Point.BranchId, SiatCodes.EnvironmentTest, place.Settings.Nit, place.Point.Id, place.Mapping.SiatCode,
            place.Point.Code, place.Cuis.Id, place.Cufd.Id, place.Cufd.ControlCode, emission, issuedAt ?? FiscalNow(session, (int)number), number, Legend,
            "ADMIN", eventId, cafc);
        var buyer = FiscalBuyer.Create(null, "CF", documentType, buyerDocument, null, buyerName, "juan@correo.example");
        var document = FiscalDocument.IssueInvoice(place.Settings.TenantId, emissionData, buyer, invoiceId, lines, 1, null, additionalDiscount, 0m, false, now,
            replaces);
        await SaveAsync(session, place, document, accept, now);
        return document;
    }

    public static async Task<FiscalDocument> CreditNoteAsync(D_Session session, Place place, long number, FiscalDocument original,
        IReadOnlyList<FiscalLineInput> lines, Guid? salesReturnId = null, bool accept = true)
    {
        var now = session.Services.GetRequiredService<IClock>().UtcNow;
        var emissionData = new FiscalEmission(place.Point.BranchId, SiatCodes.EnvironmentTest, place.Settings.Nit, place.Point.Id, place.Mapping.SiatCode,
            place.Point.Code, place.Cuis.Id, place.Cufd.Id, place.Cufd.ControlCode, SiatCodes.EmissionOnline, FiscalNow(session, 100 + (int)number), number,
            Legend, "ADMIN");
        var buyer = FiscalBuyer.Create(null, "CF", SiatCodes.DocumentCi, "5115889", null, "Juan Pérez", null);
        var note = FiscalDocument.IssueCreditNote(place.Settings.TenantId, emissionData, buyer, salesReturnId,
            new FiscalOriginalInvoice(original.Id, original.Number, original.Cuf, original.IssuedAt, null), lines, now);
        await SaveAsync(session, place, note, accept, now);
        return note;
    }

    private static async Task SaveAsync(D_Session session, Place place, FiscalDocument document, bool accept, DateTimeOffset now)
    {
        var db = session.Db;
        var serializer = session.Services.GetRequiredService<IFiscalDocumentSerializer>();
        var xml = serializer.BuildXml(document, new FiscalXmlContext(place.Settings.Nit, place.Settings.BusinessName, place.Mapping.Municipality,
            place.Mapping.Phone, place.Mapping.SiatCode, place.Point.Code, place.Cufd.Code, place.Cufd.Address));
        db.Set<FiscalDocument>().Add(document);
        db.Set<FiscalDocumentFile>().Add(new FiscalDocumentFile(document.TenantId, document.BranchId, document.Id, xml,
            serializer.Sha256Hex(serializer.Gzip(xml)), now));
        db.Set<FiscalDocumentEvent>().Add(BillingLookups.Log(document, FiscalDocumentAction.Issued, now, session.Login.UserId, description: "Emitido"));
        if (accept && document.Status == FiscalDocumentStatus.Pending)
        {
            document.Accept("REC-" + document.Number, SiatCodes.ReceptionValidated, now);
        }
        await db.SaveChangesAsync();
        db.ClearTracking();
    }

    /// <summary>Cambia el estado de un documento guardado con un método del dominio (anular, rechazar, sin respuesta…).</summary>
    public static async Task ChangeAsync(D_Session session, Guid documentId, Action<FiscalDocument> change)
    {
        var db = session.Db;
        db.ClearTracking();
        var document = await db.Set<FiscalDocument>().Include(d => d.Lines).FirstAsync(d => d.Id == documentId);
        change(document);
        await db.SaveChangesAsync();
        db.ClearTracking();
    }
}
