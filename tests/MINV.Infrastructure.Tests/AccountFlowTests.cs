using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Accounts;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Partners;
using MINV.Application.Sales;
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Tests;

/// <summary>La empresa de prueba de las cuentas de cliente se carga UNA vez para toda la clase (cada prueba usa sus propios
/// correos y mide el stock y los correlativos respecto de lo que encuentra).</summary>
public sealed class AccountFlowFixture : IAsyncLifetime
{
    public const string Tenant = "CUENTAS";

    public ServiceProvider Services { get; private set; } = null!;

    public SeedResult Seed { get; private set; } = null!;

    public async Task InitializeAsync() => (Services, Seed) = await LocalDataSeederTests.SeedAsync(days: 4, tenant: Tenant, seed: 29);

    public async Task DisposeAsync() => await Services.DisposeAsync();
}

/// <summary>
/// V7 · Cuentas de cliente (reglas P-03 y P-04) sobre la empresa de prueba EN MEMORIA, con la tubería completa (validación,
/// permisos y auditoría): el registro crea usuario, credencial, rol CLIENTE, sucursal de la tienda, cliente correlativo y
/// cuenta; los casos de uso de la cuenta operan SOLO sobre el cliente del usuario de la sesión; la reserva de una cuenta
/// reutiliza la de la tienda y queda ligada a su cliente; y la caja la vende a nombre de ese cliente.
/// </summary>
public sealed class AccountFlowTests(AccountFlowFixture fixture) : IClassFixture<AccountFlowFixture>
{
    private const string Tenant = AccountFlowFixture.Tenant;
    private const string Password = "Cliente-2026";
    private readonly ServiceProvider _services = fixture.Services;
    private readonly SeedResult _seed = fixture.Seed;

    private async Task<IServiceScope> LoginAsync(string role, string? branch = null)
    {
        var scope = _services.CreateScope();
        var who = _seed.Users.First(u => u.RoleCode == role && (branch is null || u.Branches == branch));
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand(Tenant, who.Email, who.Password, "pruebas", "7.0"));
        return scope;
    }

    /// <summary>Registro como lo envía el servidor en la nube: antes de autenticar no hay sucursal visible y el canal es el de la web.</summary>
    private async Task<(IServiceScope Scope, CustomerAccountSession Account)> RegisterAsync(string name, string email, string phone = "71234567",
        string? branch = null, string password = Password, string tenant = Tenant)
    {
        var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetBranches(new BranchScope(false, [Guid.Empty], null));
        scope.ServiceProvider.GetRequiredService<IRequestOrigin>().Set(RequestChannels.Web, null);
        try
        {
            var account = await scope.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new RegisterCustomerAccountCommand(tenant, name, email, phone, password, branch, "web 203.0.113.7", "7.0.0"));
            return (scope, account);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private static Task<T> SendAsync<T>(IServiceScope scope, IRequest<T> request)
    {
        scope.ServiceProvider.GetRequiredService<IMinvDbContext>().ClearTracking();
        return scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    private static async Task<string> CodeAsync(Func<Task> action) => (await Assert.ThrowsAsync<DomainException>(action)).Code;

    /// <summary>Último correlativo de los clientes de la web (0 si todavía no hay ninguno).</summary>
    private static async Task<int> LastWebCustomerAsync(MinvWriteDbContext db) =>
        (await db.Customers.AsNoTracking().Where(c => c.Code.StartsWith(AccountRules.CustomerCodePrefix + "-")).Select(c => c.Code).ToListAsync())
        .Select(c => int.Parse(c[(AccountRules.CustomerCodePrefix.Length + 1)..], System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();

    private static async Task<(int Users, int Credentials, int Roles, int Branches, int Customers, int Accounts, int Sessions)> CountsAsync(MinvWriteDbContext db)
    {
        db.ChangeTracker.Clear();
        return (await db.Users.CountAsync(), await db.UserCredentials.CountAsync(), await db.UserRoles.CountAsync(), await db.BranchUsers.CountAsync(),
            await db.Customers.CountAsync(), await db.CustomerAccounts.CountAsync(), await db.Sessions.CountAsync());
    }

    private static async Task<(decimal OnHand, decimal Reserved)> StockAsync(MinvWriteDbContext db, string sku)
    {
        db.ChangeTracker.Clear();
        var branch = await db.Branches.SingleAsync(b => b.Code == LocalDataSeeder.BranchMain);
        var rows = await (from l in db.StockLevels join b in db.Batches on l.BatchId equals b.Id join v in db.ProductVariants on b.VariantId equals v.Id
                          where v.Sku == sku && l.BranchId == branch.Id select new { l.QuantityOnHand, l.QuantityReserved }).ToListAsync();
        return (rows.Sum(r => r.QuantityOnHand), rows.Sum(r => r.QuantityReserved));
    }

    // ------------------------------------------------------------------------------------------------ registro
    [Fact]
    public async Task Registrarse_crea_usuario_credencial_rol_CLIENTE_sucursal_cliente_correlativo_y_cuenta()
    {
        using var admin = await LoginAsync(RoleCodes.Admin);
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var hasher = _services.GetRequiredService<IPasswordHasher>();
        var last = await LastWebCustomerAsync(db);
        var before = await CountsAsync(db);

        const string email = "valentina.aguirre@correo.example";
        var (scope, account) = await RegisterAsync("  Valentina Aguirre ", "  Valentina.Aguirre@Correo.Example ", "+591 7123-4567");
        using var mine = scope;
        // La sesión que devuelve: SOLO el rol CLIENTE, sus dos permisos y la sucursal de la tienda (la del almacén principal)
        Assert.Equal($"WEB-{last + 1:000000}", account.CustomerCode);
        Assert.Equal([RoleCodes.Customer], account.Login.Roles);
        Assert.Equal([PermissionCodes.AccountManage, PermissionCodes.AccountReserve], account.Login.Permissions.Order(StringComparer.Ordinal));
        Assert.Equal(("Valentina Aguirre", false, false), (account.Login.DisplayName, account.Login.MustChangePassword, account.Login.Access.AllBranches));
        Assert.Equal(LocalDataSeeder.BranchMain, Assert.Single(account.Login.Access.Branches).Code);
        Assert.Equal(LocalDataSeeder.BranchMain, account.Login.Access.Active!.Code);
        var current = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
        Assert.Equal((account.Login.UserId, email), (current.UserId!.Value, current.Email));
        Assert.Equal([PermissionCodes.AccountManage, PermissionCodes.AccountReserve], current.Permissions.Order(StringComparer.Ordinal));
        Assert.Equal((false, LocalDataSeeder.BranchMain), (scope.ServiceProvider.GetRequiredService<ITenantContext>().Branches.AllBranches,
            account.Login.Access.Branches.Single(b => b.Id == scope.ServiceProvider.GetRequiredService<ITenantContext>().Branches.ActiveBranchId).Code));

        // En la base: una fila de cada cosa, todas de la misma empresa
        var after = await CountsAsync(db);
        Assert.Equal((before.Users + 1, before.Credentials + 1, before.Roles + 1, before.Branches + 1, before.Customers + 1, before.Accounts + 1, before.Sessions + 1),
            after);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        Assert.Equal((account.Login.UserId, "Valentina Aguirre", true, account.Login.TenantId), (user.Id, user.DisplayName, user.IsActive, user.TenantId));
        Assert.Equal([RoleCodes.Customer],
            await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == user.Id select r.Code).ToListAsync());
        Assert.Equal([LocalDataSeeder.BranchMain],
            await (from bu in db.BranchUsers join b in db.Branches on bu.BranchId equals b.Id where bu.UserId == user.Id select b.Code).ToListAsync());
        var credential = await db.UserCredentials.AsNoTracking().SingleAsync(c => c.UserId == user.Id);
        Assert.Equal((hasher.Algorithm, hasher.Iterations, false, 0), (credential.Algorithm, credential.Iterations, credential.MustChangePassword, credential.FailedAttempts));
        Assert.StartsWith("PBKDF2", credential.Algorithm, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, credential.PasswordHash, StringComparison.Ordinal);
        Assert.True(hasher.Verify(Password, credential.PasswordHash, credential.Iterations));
        Assert.False(hasher.Verify("Otra-Clave-2026", credential.PasswordHash, credential.Iterations));
        var link = await db.CustomerAccounts.AsNoTracking().SingleAsync(a => a.UserId == user.Id);
        var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == link.CustomerId);
        var general = await db.Customers.AsNoTracking().Where(c => c.Code == "CF").Select(c => c.CustomerCategoryId).SingleAsync();
        Assert.Equal((account.CustomerCode, "Valentina Aguirre", email, "+59171234567", general, true, (int?)null, (string?)null, user.TenantId),
            (customer.Code, customer.Name, customer.Email, customer.Phone, customer.CustomerCategoryId, customer.IsActive, customer.DocumentType, customer.TaxId,
                customer.TenantId));
        Assert.True(link.BelongsTo(user.Id));
        var session = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == account.Login.SessionId);
        Assert.Equal((user.Id, true, account.Login.Access.ActiveBranchId, "web 203.0.113.7"), (session.UserId, session.IsOpen, session.ActiveBranchId, session.MachineName));

        // Rastro: registro de acceso y auditoría (canal web, a nombre del usuario nuevo), SIN la contraseña y con el teléfono enmascarado
        var access = Assert.Single(await db.AccessLogs.AsNoTracking().Where(a => a.AttemptedEmail == email).ToListAsync());
        Assert.Equal((true, user.Id, (string?)null, "web 203.0.113.7"), (access.Succeeded, access.UserId!.Value, access.FailureReason, access.MachineName));
        var audit = Assert.Single(await db.AuditLogs.AsNoTracking().Where(a => a.Action == "RegisterCustomerAccount" && a.UserId == user.Id).ToListAsync());
        Assert.Equal((AuditOutcome.Succeeded, RequestChannels.Web), (audit.Outcome, audit.Channel));
        Assert.DoesNotContain(Password, audit.Details!, StringComparison.Ordinal);
        Assert.DoesNotContain("7123-4567", audit.Details!, StringComparison.Ordinal);
        Assert.DoesNotContain("71234567", audit.Details!, StringComparison.Ordinal);
        Assert.Contains(account.CustomerCode, audit.Details!, StringComparison.Ordinal);

        // La cuenta sirve de inmediato y también al ingresar después con el correo y la contraseña
        Assert.Equal(new MyAccountView("Valentina Aguirre", email, "+59171234567", null, null, null, account.CustomerCode),
            await SendAsync(scope, new GetMyAccountQuery()));
        using (var again = _services.CreateScope())
        {
            var login = await again.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand(Tenant, email.ToUpperInvariant(), Password, "pruebas", "7.0"));
            Assert.Equal((user.Id, RoleCodes.Customer, LocalDataSeeder.BranchMain), (login.UserId, Assert.Single(login.Roles), login.Access.Active!.Code));
            Assert.Equal(account.CustomerCode, (await SendAsync(again, new GetMyAccountQuery())).CustomerCode);
        }

        // Correlativo: el siguiente cliente toma el número siguiente; con la sucursal configurada, esa es su sucursal
        var (second, next) = await RegisterAsync("Mateo Condori", "mateo.condori@correo.example", "76543210", branch: " cb ");
        using var other = second;
        Assert.Equal($"WEB-{last + 2:000000}", next.CustomerCode);
        Assert.Equal((LocalDataSeeder.BranchCochabamba, LocalDataSeeder.BranchCochabamba, false),
            (Assert.Single(next.Login.Access.Branches).Code, next.Login.Access.Active!.Code, next.Login.Access.AllBranches));
        Assert.Equal("76543210", (await SendAsync(second, new GetMyAccountQuery())).Phone);
    }

    [Fact]
    public async Task Un_registro_rechazado_no_crea_nada()
    {
        using var admin = await LoginAsync(RoleCodes.Admin);
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var (first, account) = await RegisterAsync("Lucía Rojas", "lucia.rojas@correo.example");
        first.Dispose();
        var before = await CountsAsync(db);

        // Correo repetido (con otras mayúsculas o espacios, y también el de un usuario del personal): account.email_taken
        var staff = _seed.Users.First(u => u.RoleCode == RoleCodes.Management);
        foreach (var repeated in new[] { "lucia.rojas@correo.example", "LUCIA.ROJAS@correo.example", "  Lucia.Rojas@Correo.Example ", staff.Email, staff.Email.ToUpperInvariant() })
        {
            var error = await Assert.ThrowsAsync<DomainException>(() => RegisterAsync("Otra Persona", repeated, "76543210", password: "Otra-Clave-2026"));
            Assert.Equal((AccountRules.EmailTaken, RegisterCustomerAccountHandler.EmailTakenMessage), (error.Code, error.Message));
        }
        // Teléfono que no es boliviano (la misma regla de las reservas), empresa que no existe y sucursal que no existe
        Assert.Equal("pcbuild.contact_phone", await CodeAsync(() => RegisterAsync("Ana Quispe", "ana.1@correo.example", "12345")));
        Assert.Equal("pcbuild.contact_phone", await CodeAsync(() => RegisterAsync("Ana Quispe", "ana.1@correo.example", "+1 202 555 0147")));
        Assert.Equal("account.unavailable", await CodeAsync(() => RegisterAsync("Ana Quispe", "ana.1@correo.example", tenant: "NOEXISTE")));
        Assert.Equal("account.branch", await CodeAsync(() => RegisterAsync("Ana Quispe", "ana.1@correo.example", branch: "ZZ")));
        // Datos mal formados: la validación los rechaza antes de tocar la base (y sin repetir la contraseña)
        var weak = await Assert.ThrowsAsync<RequestValidationException>(() => RegisterAsync("Ana Quispe", "ana.1@correo.example", password: "solamenteletras"));
        Assert.Equal([AccountRules.PasswordMixMessage], weak.Errors);
        Assert.DoesNotContain("solamenteletras", weak.Message, StringComparison.Ordinal);
        Assert.Equal([AccountRules.PasswordLengthMessage],
            (await Assert.ThrowsAsync<RequestValidationException>(() => RegisterAsync("Ana Quispe", "ana.1@correo.example", password: "Abc-123"))).Errors);
        Assert.Equal([AccountRules.EmailMessage],
            (await Assert.ThrowsAsync<RequestValidationException>(() => RegisterAsync("Ana Quispe", "Ana <ana.1@correo.example>"))).Errors);

        Assert.Equal(before, await CountsAsync(db));
        Assert.Equal(0, await db.Users.AsNoTracking().CountAsync(u => u.Email == "ana.1@correo.example"));
        // …y el correo repetido no le cambió la contraseña a nadie
        using (var scope = _services.CreateScope())
        {
            await Assert.ThrowsAsync<AuthenticationFailedException>(() => scope.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new LoginCommand(Tenant, staff.Email, "Otra-Clave-2026", "pruebas", "7.0")));
            Assert.Equal(staff.Name, (await scope.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new LoginCommand(Tenant, staff.Email, staff.Password, "pruebas", "7.0"))).DisplayName);
        }
        // El siguiente registro válido sigue el correlativo: los rechazados no gastaron números
        var (valid, next) = await RegisterAsync("Ana Quispe", "ana.1@correo.example");
        valid.Dispose();
        Assert.Equal(int.Parse(account.CustomerCode[4..], System.Globalization.CultureInfo.InvariantCulture) + 1,
            int.Parse(next.CustomerCode[4..], System.Globalization.CultureInfo.InvariantCulture));
        // Los rechazos del caso de uso quedaron auditados, sin la contraseña
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "RegisterCustomerAccount" && a.Outcome == AuditOutcome.Rejected).ToListAsync();
        Assert.Contains(audits, a => a.Details!.Contains(AccountRules.CustomerCodePrefix, StringComparison.Ordinal) || a.Details.Contains("Ya existe una cuenta", StringComparison.Ordinal));
        Assert.DoesNotContain(audits, a => a.Details!.Contains("Otra-Clave-2026", StringComparison.Ordinal) || a.Details.Contains(Password, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ mi cuenta
    [Fact]
    public async Task Los_casos_de_uso_de_la_cuenta_operan_solo_sobre_el_cliente_de_la_sesion()
    {
        using var admin = await LoginAsync(RoleCodes.Admin);
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var clock = _services.GetRequiredService<DemoClock>();
        var (anaScope, anaAccount) = await RegisterAsync("Ana Quispe", "ana.quispe@correo.example", "71234567");
        var (luisScope, luisAccount) = await RegisterAsync("Luis Rojas", "luis.rojas@correo.example", "76543210");
        using var ana = anaScope;
        using var luis = luisScope;
        var snapshot = await SendAsync(admin, new GetStorefrontCatalogQuery());
        var game = snapshot.Products.Where(p => p.Category == "JUE" && !p.Serialized && p.Available >= 6).OrderBy(p => p.Sku, StringComparer.Ordinal).First();
        var cabinet = snapshot.Products.Where(p => p.Category == "CASE" && p.Available >= 2).OrderBy(p => p.Sku, StringComparer.Ordinal).First();
        var stock = await StockAsync(db, game.Sku);

        // Ana reserva un carrito (1 día) y Luis un armado: cada reserva con el contacto de SU cuenta y ligada a SU cliente
        var mine = await SendAsync(ana, new CreateMyReservationCommand([new StorefrontReservationLineInput(game.Sku.ToLowerInvariant(), 2)], HoldDays: 1, Notes: " Paso el sábado "));
        Assert.StartsWith(PcBuild.WebCartNumberPrefix + "-", mine.Number, StringComparison.Ordinal);
        // V7 (B3) · La confirmación queda encolada al correo de su cuenta (mailQueued de verdad)
        Assert.Equal(("Reserved", "cart", "Ana Quispe", LocalDataSeeder.BranchMain, "Paso el sábado", 2 * game.Price, true),
            (mine.Status, mine.Kind, mine.ContactName, mine.Branch, mine.Notes, mine.Total, mine.MailQueued));
        Assert.Equal((game.Sku, (string?)null, 2), (Assert.Single(mine.Lines).Sku, mine.Lines[0].Slot, mine.Lines[0].Quantity));
        Assert.InRange((mine.ReservedUntil!.Value - clock.UtcNow).TotalHours, 23.9, 24.1);
        Assert.Equal((stock.OnHand, stock.Reserved + 2), await StockAsync(db, game.Sku));
        var other = await SendAsync(luis, new CreateMyReservationCommand([new StorefrontReservationLineInput(cabinet.Sku)], PcBuildKind.Build, Name: "PC de Luis"));
        Assert.StartsWith(PcBuild.WebNumberPrefix + "-", other.Number, StringComparison.Ordinal);
        Assert.Equal(("Reserved", "build", "Luis Rojas", "case"), (other.Status, other.Kind, other.ContactName, Assert.Single(other.Lines).Slot));
        Assert.InRange((other.ReservedUntil!.Value - clock.UtcNow).TotalHours, 47.9, 48.1);

        db.ChangeTracker.Clear();
        var anaCustomer = await db.Customers.AsNoTracking().SingleAsync(c => c.Code == anaAccount.CustomerCode);
        var luisCustomer = await db.Customers.AsNoTracking().SingleAsync(c => c.Code == luisAccount.CustomerCode);
        var cart = await db.PcBuilds.AsNoTracking().Include(b => b.Lines).SingleAsync(b => b.Number == mine.Number);
        Assert.Equal((anaCustomer.Id, PcBuildChannel.Web, PcBuildKind.Cart, PcBuildStatus.Reserved, "Ana Quispe", "71234567", "ana.quispe@correo.example", false,
                anaAccount.Login.UserId),
            (cart.CustomerId!.Value, cart.Channel, cart.Kind, cart.Status, cart.ContactName, cart.ContactPhone, cart.ContactEmail, cart.HasBuyer, cart.CreatedByUserId));
        var lineIds = cart.Lines.Select(l => l.Id).ToList();
        var reservation = Assert.Single(await db.StockReservations.AsNoTracking().Where(r => r.PcBuildLineId != null && lineIds.Contains(r.PcBuildLineId.Value)).ToListAsync());
        Assert.Equal((2m, ReservationStatus.Active), (reservation.Quantity, reservation.Status));
        Assert.Equal(luisCustomer.Id, (await db.PcBuilds.AsNoTracking().SingleAsync(b => b.Number == other.Number)).CustomerId);

        // Cada uno ve SOLO lo suyo; la reserva del otro «no existe» (el mismo mensaje que un número inventado) y sigue reservada
        Assert.Equal([mine.Number], (await SendAsync(ana, new GetMyReservationsQuery())).Select(r => r.Number));
        Assert.Equal([other.Number], (await SendAsync(luis, new GetMyReservationsQuery())).Select(r => r.Number));
        var denied = await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(luis, new CancelMyReservationCommand(mine.Number)));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(luis, new CancelMyReservationCommand("RES-WEB-999999")));
        Assert.Equal(denied.Message.Replace(mine.Number, "X", StringComparison.Ordinal), unknown.Message.Replace("RES-WEB-999999", "X", StringComparison.Ordinal));
        Assert.Equal((stock.OnHand, stock.Reserved + 2), await StockAsync(db, game.Sku));
        // Una reserva anónima de la tienda (sin cliente) con el MISMO teléfono tampoco aparece en la cuenta ni se cancela desde ella
        var anonymous = (await SendAsync(admin, new CreateStorefrontReservationCommand([new StorefrontReservationLineInput(game.Sku)],
            new StorefrontContactInput("Ana Quispe", "71234567", "ana.quispe@correo.example"), null, "anonima-1", null, PcBuildKind.Cart))).Reservation;
        Assert.Equal([mine.Number], (await SendAsync(ana, new GetMyReservationsQuery())).Select(r => r.Number));
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(ana, new CancelMyReservationCommand(anonymous.Number)));

        // Ana cancela la suya (el número no distingue mayúsculas): el stock vuelve y la reserva sigue en su historial
        var cancelled = await SendAsync(ana, new CancelMyReservationCommand(" " + mine.Number.ToLowerInvariant() + " "));
        Assert.Equal(("Cancelled", "Cancelada", CancelMyReservationHandler.Reason), (cancelled.Status, cancelled.StatusText, cancelled.CancelReason));
        Assert.Equal((stock.OnHand, stock.Reserved + 1), await StockAsync(db, game.Sku));   // queda la anónima
        Assert.Equal("pcbuild.state", await CodeAsync(() => SendAsync(ana, new CancelMyReservationCommand(mine.Number))));
        Assert.Equal("Cancelled", Assert.Single(await SendAsync(ana, new GetMyReservationsQuery())).Status);
        var released = await db.PcBuilds.AsNoTracking().Include(b => b.History).SingleAsync(b => b.Number == mine.Number);
        Assert.Equal((PcBuildEventAction.Released, anaAccount.Login.UserId), (released.History.OrderBy(h => h.OccurredAt).Last().Action,
            released.History.OrderBy(h => h.OccurredAt).Last().UserId));
        // Sin stock suficiente no se reserva nada (todo o nada)
        await Assert.ThrowsAsync<StorefrontStockException>(() => SendAsync(ana, new CreateMyReservationCommand(
            [new StorefrontReservationLineInput(game.Sku, 1), new StorefrontReservationLineInput(cabinet.Sku, PcBuild.MaxQuantity)])));
        Assert.Equal((stock.OnHand, stock.Reserved + 1), await StockAsync(db, game.Sku));
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(ana, new CreateMyReservationCommand([])));
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(ana, new CreateMyReservationCommand([new StorefrontReservationLineInput("NO-EXISTE-0001")])));

        // Mis datos: cambia SU nombre (usuario y cliente), SU teléfono y SU documento; el correo no cambia; los de Luis, intactos
        var updated = await SendAsync(ana, new UpdateMyAccountCommand(" Ana Quispe Mamani ", "(2) 221-2345", 1, " 4567890 ", "1a"));
        Assert.Equal(new MyAccountView("Ana Quispe Mamani", "ana.quispe@correo.example", "22212345", 1, "4567890", "1A", anaAccount.CustomerCode), updated);
        Assert.Equal(updated, await SendAsync(ana, new GetMyAccountQuery()));
        Assert.Equal(new MyAccountView("Luis Rojas", "luis.rojas@correo.example", "76543210", null, null, null, luisAccount.CustomerCode),
            await SendAsync(luis, new GetMyAccountQuery()));
        db.ChangeTracker.Clear();
        Assert.Equal("Ana Quispe Mamani", (await db.Users.AsNoTracking().SingleAsync(u => u.Id == anaAccount.Login.UserId)).DisplayName);
        var stored = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == anaCustomer.Id);
        Assert.Equal(("Ana Quispe Mamani", "ana.quispe@correo.example", "22212345", 1, "4567890", "1A", anaCustomer.CustomerCategoryId, true),
            (stored.Name, stored.Email, stored.Phone, stored.DocumentType!.Value, stored.TaxId, stored.Complement, stored.CustomerCategoryId, stored.HasFiscalIdentity));
        Assert.Equal("buyer.doc_numeric", await CodeAsync(() => SendAsync(ana, new UpdateMyAccountCommand("Ana Quispe", "71234567", 1, "45678-LP"))));
        Assert.Equal("pcbuild.contact_phone", await CodeAsync(() => SendAsync(ana, new UpdateMyAccountCommand("Ana Quispe", "12345"))));
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(ana, new UpdateMyAccountCommand("Ana Quispe", "71234567", 5, "1023456029", "1A")));
        Assert.Equal(updated, await SendAsync(ana, new GetMyAccountQuery()));   // los rechazos no cambiaron nada
        // Quitar el documento: sin tipo no queda número ni complemento
        var cleared = await SendAsync(ana, new UpdateMyAccountCommand("Ana Quispe Mamani", "71234567"));
        Assert.Equal(((int?)null, (string?)null, (string?)null, "71234567"), (cleared.DocumentType, cleared.DocumentNumber, cleared.Complement, cleared.Phone));
        // Las reservas nuevas salen con el contacto vigente de la cuenta
        var renamed = await SendAsync(ana, new CreateMyReservationCommand([new StorefrontReservationLineInput(game.Sku)]));
        Assert.Equal("Ana Quispe Mamani", renamed.ContactName);
        Assert.Equal([renamed.Number, mine.Number], (await SendAsync(ana, new GetMyReservationsQuery())).Select(r => r.Number));

        // La auditoría de la cuenta: canal web, a nombre de cada cliente, sin el teléfono ni el documento completos
        var audits = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "UpdateMyAccount" || a.Action == "CreateMyReservation" || a.Action == "CancelMyReservation").ToListAsync();
        Assert.Contains(audits, a => a.Action == "UpdateMyAccount" && a.Outcome == AuditOutcome.Succeeded && a.UserId == anaAccount.Login.UserId);
        Assert.Contains(audits, a => a.Action == "CreateMyReservation" && a.Outcome == AuditOutcome.Succeeded && a.UserId == luisAccount.Login.UserId);
        Assert.Contains(audits, a => a.Action == "CancelMyReservation" && a.Outcome == AuditOutcome.Rejected && a.UserId == luisAccount.Login.UserId);
        Assert.All(audits, a => Assert.Equal(RequestChannels.Web, a.Channel));
        Assert.DoesNotContain(audits, a => a.Action == "UpdateMyAccount" && (a.Details!.Contains("4567890", StringComparison.Ordinal)
                                                                             || a.Details.Contains("221-2345", StringComparison.Ordinal)
                                                                             || a.Details.Contains("22212345", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Sin_cuenta_de_cliente_o_con_la_cuenta_desactivada_no_hay_casos_de_uso_de_la_cuenta()
    {
        // La administración tiene todos los permisos, pero no una cuenta de cliente
        using var admin = await LoginAsync(RoleCodes.Admin);
        Assert.Equal(AccountRules.Missing, await CodeAsync(() => SendAsync(admin, new GetMyAccountQuery())));
        Assert.Equal(AccountRules.Missing, await CodeAsync(() => SendAsync(admin, new GetMyReservationsQuery())));
        Assert.Equal(AccountRules.Missing, await CodeAsync(() => SendAsync(admin, new UpdateMyAccountCommand("Administrador", "71234567"))));
        Assert.Equal(AccountRules.Missing, await CodeAsync(() => SendAsync(admin, new CancelMyReservationCommand("RES-WEB-000001"))));
        Assert.Equal(AccountRules.Missing, await CodeAsync(() => SendAsync(admin,
            new CreateMyReservationCommand([new StorefrontReservationLineInput("CASE-COR-4000D")]))));
        // El resto del personal ni siquiera tiene el permiso; y sin sesión tampoco se entra
        using var cashier = await LoginAsync(RoleCodes.Cashier, LocalDataSeeder.BranchMain);
        Assert.Contains(PermissionCodes.AccountManage, (await Assert.ThrowsAsync<AccessDeniedException>(() => SendAsync(cashier, new GetMyAccountQuery()))).Message,
            StringComparison.Ordinal);
        using (var anonymous = _services.CreateScope())
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => SendAsync(anonymous, new GetMyReservationsQuery()));
        }

        // Un cliente no ejecuta casos de uso del personal: la tubería de permisos lo rechaza (defensa en profundidad de la lista del RPC)
        var (scope, account) = await RegisterAsync("Diego Mamani", "diego.mamani@correo.example");
        using var diego = scope;
        await Assert.ThrowsAsync<AccessDeniedException>(() => SendAsync(diego, new GetUsersQuery()));
        await Assert.ThrowsAsync<AccessDeniedException>(() => SendAsync(diego, new GetCustomersQuery()));
        await Assert.ThrowsAsync<AccessDeniedException>(() => SendAsync(diego, new GetPcBuildsQuery()));
        await Assert.ThrowsAsync<AccessDeniedException>(() => SendAsync(diego, new GetStorefrontCatalogQuery()));
        await Assert.ThrowsAsync<AccessDeniedException>(() => SendAsync(diego,
            new SaveUserCommand("diego.mamani@correo.example", "diego.mamani@correo.example", "Diego Mamani", RoleCodes.Admin, true, null)));
        await Assert.ThrowsAsync<AccessDeniedException>(() => SendAsync(diego,
            new ReserveCartCommand([new CartItemInput("CASE-COR-4000D")], "Diego Mamani", "71234567")));

        // Si la tienda desactiva al cliente, su cuenta deja de operar (y vuelve al activarlo)
        var reserved = await SendAsync(diego, new CreateMyReservationCommand([new StorefrontReservationLineInput("CASE-COR-4000D")]));
        Assert.Equal(account.CustomerCode,
            await SendAsync(admin, new SaveCustomerCommand(account.CustomerCode, "Diego Mamani", null, "diego.mamani@correo.example", "71234567", "GENERAL", false)));
        Assert.Equal("account.inactive", await CodeAsync(() => SendAsync(diego, new GetMyAccountQuery())));
        Assert.Equal("account.inactive", await CodeAsync(() => SendAsync(diego, new GetMyReservationsQuery())));
        Assert.Equal("account.inactive", await CodeAsync(() => SendAsync(diego, new CancelMyReservationCommand(reserved.Number))));
        Assert.Equal("account.inactive", await CodeAsync(() => SendAsync(diego,
            new CreateMyReservationCommand([new StorefrontReservationLineInput("CASE-COR-4000D")]))));
        await SendAsync(admin, new SaveCustomerCommand(account.CustomerCode, "Diego Mamani", null, "diego.mamani@correo.example", "71234567", "GENERAL", true));
        Assert.Equal("Reserved", Assert.Single(await SendAsync(diego, new GetMyReservationsQuery())).Status);
    }

    // ------------------------------------------------------------------------------------------------ venta en caja
    [Fact]
    public async Task La_reserva_de_una_cuenta_se_vende_en_caja_a_nombre_de_su_cliente_y_el_cliente_la_ve_vendida()
    {
        using var admin = await LoginAsync(RoleCodes.Admin);
        using var pos = await LoginAsync(RoleCodes.Cashier, LocalDataSeeder.BranchMain);
        var db = admin.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        var (scope, account) = await RegisterAsync("Camila Flores", "camila.flores@correo.example", "70012345");
        using var camila = scope;
        await SendAsync(camila, new UpdateMyAccountCommand("Camila Flores", "70012345", 1, "6543210"));
        var snapshot = await SendAsync(admin, new GetStorefrontCatalogQuery());
        var loose = snapshot.Products.Where(p => !p.Serialized && p.Available >= 4 && p.Category is "JUE" or "CAB").OrderBy(p => p.Sku, StringComparer.Ordinal).First();
        var before = await StockAsync(db, loose.Sku);

        var reserved = await SendAsync(camila, new CreateMyReservationCommand([new StorefrontReservationLineInput(loose.Sku, 2)], HoldDays: 3));
        Assert.Equal((before.OnHand, before.Reserved + 2), await StockAsync(db, loose.Sku));
        // El personal la ve con su cliente y su contacto; los datos para la factura no se copiaron a la reserva
        var row = (await SendAsync(admin, new GetPcBuildsQuery(Kind: PcBuildKind.Cart))).Single(r => r.Number == reserved.Number);
        Assert.Equal(("Camila Flores", "Camila Flores", "70012345", "camila.flores@correo.example", PcBuildChannel.Web, (string?)null),
            (row.Customer, row.ContactName, row.ContactPhone, row.ContactEmail, row.Channel, row.BuyerDocumentNumber));

        // La caja cobra sin capturar comprador: la venta sale a nombre del cliente de la cuenta, con SU documento
        var command = new SellPcBuildCommand(reserved.Number, "EFECTIVO", null, 100000m);
        CheckoutResult sale;
        try
        {
            sale = await SendAsync(pos, command);
        }
        catch (DomainException ex) when (ex.Code == "pos.closed")
        {
            await SendAsync(pos, new OpenPosSessionCommand("CAJA03", 500));
            sale = await SendAsync(pos, command);
        }
        Assert.Equal((reserved.Total, "Camila Flores"), (sale.Total, sale.Customer));
        Assert.Equal((before.OnHand - 2, before.Reserved), await StockAsync(db, loose.Sku));
        db.ChangeTracker.Clear();
        var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Code == account.CustomerCode);
        Assert.Equal(customer.Id, (await db.SalesOrders.AsNoTracking().SingleAsync(o => o.Number == sale.OrderNumber)).CustomerId);
        Assert.NotNull(sale.FiscalDocumentId);
        var document = await db.Set<FiscalDocument>().AsNoTracking().SingleAsync(d => d.Id == sale.FiscalDocumentId);
        Assert.Equal((1, "6543210", "Camila Flores"), (document.BuyerDocumentType, document.BuyerDocumentNumber, document.BuyerName));

        // El cliente la ve «Vendida» en su cuenta y ya no la puede cancelar
        var sold = Assert.Single(await SendAsync(camila, new GetMyReservationsQuery()));
        Assert.Equal((reserved.Number, "Sold", "Vendida", "cart"), (sold.Number, sold.Status, sold.StatusText, sold.Kind));
        Assert.Equal("pcbuild.state", await CodeAsync(() => SendAsync(camila, new CancelMyReservationCommand(reserved.Number))));
        Assert.Equal((before.OnHand - 2, before.Reserved), await StockAsync(db, loose.Sku));
    }
}
