using System.Reflection;
using System.Text.Json;
using MediatR;
using MINV.Application.Accounts;
using MINV.Application.Behaviors;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Inventory.Movements;
using MINV.Application.Inventory.Queries;
using MINV.Application.Remote;
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Application.Tests.Accounts;

/// <summary>
/// V7 · Contrato de las cuentas de cliente (reglas P-03 y P-04), sin base de datos: el rol CLIENTE y sus dos permisos, lo que
/// una sesión de cliente puede ejecutar por RPC, el registro que no tiene dónde recibir un rol ni una sucursal elegida por el
/// cliente, la contraseña que nunca sale en un texto ni en la auditoría, y la validación de cada caso de uso de la cuenta.
/// </summary>
public sealed class AccountContractTests
{
    private const string Password = "Cliente-2026";

    private static RegisterCustomerAccountCommand Register(string name = "Ana Quispe", string email = "ana@correo.example", string phone = "+591 7123-4567",
        string password = Password, string tenant = "TECHZONE") => new(tenant, name, email, phone, password, "CM");

    private static string[] Errors<T>(FluentValidation.IValidator<T> validator, T request) =>
        validator.Validate(request).Errors.Select(e => e.ErrorMessage).ToArray();

    // ------------------------------------------------------------------------------------------------ rol y permisos
    [Fact]
    public void El_rol_CLIENTE_solo_tiene_los_dos_permisos_de_su_cuenta()
    {
        Assert.Equal(("CLIENTE", "account.manage", "account.reserve", "account."),
            (RoleCodes.Customer, PermissionCodes.AccountManage, PermissionCodes.AccountReserve, PermissionCodes.AccountPrefix));
        Assert.Contains((RoleCodes.Customer, "Cliente web"), RoleCodes.All);
        Assert.Equal([PermissionCodes.AccountManage, PermissionCodes.AccountReserve], PermissionCodes.ForRole(RoleCodes.Customer));
        Assert.All(PermissionCodes.ForRole(RoleCodes.Customer), p => Assert.Contains(PermissionCodes.All, x => x.Code == p));
        // La administración los recibe por tener todos; ningún otro rol del personal los tiene
        Assert.Contains(PermissionCodes.AccountManage, PermissionCodes.ForRole(RoleCodes.Admin));
        Assert.All(RoleCodes.All.Where(r => r.Code is not (RoleCodes.Admin or RoleCodes.Customer)),
            r => Assert.DoesNotContain(PermissionCodes.ForRole(r.Code), p => p.StartsWith(PermissionCodes.AccountPrefix, StringComparison.Ordinal)));
        // Códigos únicos (una empresa nueva se aprovisiona con RoleCodes.All y PermissionCodes.All)
        Assert.Equal(RoleCodes.All.Count, RoleCodes.All.Select(r => r.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(PermissionCodes.All.Count, PermissionCodes.All.Select(p => p.Code).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Una_sesion_es_de_cliente_solo_cuando_su_unico_rol_es_CLIENTE()
    {
        Assert.True(RoleCodes.IsCustomerOnly([RoleCodes.Customer]));
        Assert.False(RoleCodes.IsCustomerOnly([]));
        Assert.False(RoleCodes.IsCustomerOnly([RoleCodes.Cashier]));
        Assert.False(RoleCodes.IsCustomerOnly([RoleCodes.Customer, RoleCodes.Cashier]));
        Assert.False(RoleCodes.IsCustomerOnly([RoleCodes.Customer, RoleCodes.Customer]));
        Assert.False(RoleCodes.IsCustomerOnly(["cliente"]));
        Assert.Equal(("customer", "staff"), (WebSessionKinds.Customer, WebSessionKinds.Staff));
        Assert.Equal(WebSessionKinds.Customer, WebSessionKinds.Of([RoleCodes.Customer]));
        Assert.Equal(WebSessionKinds.Staff, WebSessionKinds.Of([RoleCodes.Admin]));
        Assert.Equal(WebSessionKinds.Staff, WebSessionKinds.Of([RoleCodes.Customer, RoleCodes.Sales]));
        Assert.Equal(WebSessionKinds.Staff, WebSessionKinds.Of([]));
    }

    // ------------------------------------------------------------------------------------------------ RPC
    [Fact]
    public void El_registro_no_viaja_por_RPC_y_los_casos_de_uso_de_la_cuenta_si()
    {
        Assert.DoesNotContain(RpcCatalog.NameOf(typeof(RegisterCustomerAccountCommand)), RpcCatalog.Names);
        Assert.DoesNotContain(RpcCatalog.NameOf(typeof(LoginCommand)), RpcCatalog.Names);
        Assert.False(RpcCatalog.TryResolve("MINV.Application.Accounts.RegisterCustomerAccountCommand", out _, out _));
        var account = new (Type Request, Type Response, string Permission, bool Command)[]
        {
            (typeof(GetMyAccountQuery), typeof(MyAccountView), PermissionCodes.AccountManage, false),
            (typeof(UpdateMyAccountCommand), typeof(MyAccountView), PermissionCodes.AccountManage, true),
            (typeof(GetMyReservationsQuery), typeof(IReadOnlyList<StorefrontReservationView>), PermissionCodes.AccountManage, false),
            (typeof(CancelMyReservationCommand), typeof(StorefrontReservationView), PermissionCodes.AccountManage, true),
            (typeof(CreateMyReservationCommand), typeof(StorefrontReservationView), PermissionCodes.AccountReserve, true),
        };
        foreach (var (request, response, permission, command) in account)
        {
            Assert.Equal("MINV.Application.Accounts." + request.Name, RpcCatalog.NameOf(request));
            Assert.True(RpcCatalog.TryResolve(RpcCatalog.NameOf(request), out var resolved, out var answer), request.Name);
            Assert.Equal((request, response), (resolved, answer));
            Assert.Equal([permission], RpcCatalog.PermissionsOf(request));
            Assert.Empty(RpcCatalog.ModulesOf(request));
            Assert.Equal(command, RpcCatalog.IsCommand(request));
        }
    }

    [Fact]
    public void Una_sesion_de_cliente_solo_ejecuta_los_casos_de_uso_de_su_cuenta_y_los_de_su_sesion()
    {
        var allowed = RpcCatalog.Names.Select(n => RpcCatalog.TryResolve(n, out var request, out _) ? request : null!)
            .Where(RpcCatalog.IsAllowedForCustomer).Select(t => t.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(new[]
        {
            nameof(CancelMyReservationCommand), nameof(ChangePasswordCommand), nameof(CreateMyReservationCommand), nameof(GetMyAccountQuery),
            nameof(GetMyReservationsQuery), nameof(LogoutCommand), nameof(UpdateMyAccountCommand),
        }, allowed);
        // Lo del personal no, tenga o no permisos declarados (los que no declaran ninguno solo piden una sesión o lo comprueban por dentro)
        Assert.All(new[]
        {
            typeof(GetUsersQuery), typeof(GetStockProjectionQuery), typeof(RegisterMovementCommand), typeof(SelectBranchCommand), typeof(GetBillingAccessQuery),
            typeof(SaveUserCommand), typeof(ResetUserPasswordCommand), typeof(ReserveCartCommand), typeof(GetPcBuildsQuery),
            typeof(CreateStorefrontReservationCommand), typeof(GetStorefrontCatalogQuery), typeof(CancelStorefrontReservationCommand),
        }, t => Assert.False(RpcCatalog.IsAllowedForCustomer(t), t.Name));
        Assert.All(new[] { typeof(RegisterMovementCommand), typeof(SelectBranchCommand), typeof(GetBillingAccessQuery) },
            t => Assert.Empty(RpcCatalog.PermissionsOf(t)));
        // Tampoco los previos a la sesión ni un tipo cualquiera
        Assert.False(RpcCatalog.IsAllowedForCustomer(typeof(LoginCommand)));
        Assert.False(RpcCatalog.IsAllowedForCustomer(typeof(RegisterCustomerAccountCommand)));
        Assert.False(RpcCatalog.IsAllowedForCustomer(typeof(string)));
        Assert.Throws<ArgumentNullException>(() => RpcCatalog.IsAllowedForCustomer(null!));
    }

    [Fact]
    public void Ningun_caso_de_uso_de_la_cuenta_recibe_un_identificador_de_cliente_ni_de_usuario()
    {
        var requests = typeof(GetMyAccountQuery).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(GetMyAccountQuery).Namespace
                        && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
                        && t != typeof(RegisterCustomerAccountCommand)).ToList();
        Assert.Equal(5, requests.Count);
        foreach (var request in requests)
        {
            // Todos exigen un permiso account.* (la tubería lo comprueba) …
            Assert.All(RpcCatalog.PermissionsOf(request), p => Assert.StartsWith(PermissionCodes.AccountPrefix, p, StringComparison.Ordinal));
            Assert.NotEmpty(RpcCatalog.PermissionsOf(request));
            // … y ninguno tiene dónde recibir a QUIÉN se refiere: ni un identificador ni un código de cliente, usuario o correo
            var inputs = request.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != nameof(IAuditableRequest.AuditDetails)
                                                                                                        && p.Name != "EqualityContract").ToList();
            Assert.DoesNotContain(inputs, p => p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?));
            Assert.DoesNotContain(inputs, p => p.Name.Contains("Customer", StringComparison.OrdinalIgnoreCase)
                                               || p.Name.Contains("User", StringComparison.OrdinalIgnoreCase)
                                               || p.Name.Contains("Email", StringComparison.OrdinalIgnoreCase)
                                               || p.Name.Contains("Tenant", StringComparison.OrdinalIgnoreCase)
                                               || p.Name.Contains("Branch", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ------------------------------------------------------------------------------------------------ registro
    [Fact]
    public void El_registro_no_tiene_donde_recibir_rol_permisos_ni_datos_de_otra_cuenta()
    {
        // Lo que llega del navegador: cuatro campos y nada más (cualquier otro campo del cuerpo se descarta al leerlo)
        Assert.Equal(["Email", "Name", "Password", "Phone"],
            typeof(WebRegisterRequest).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract").Order(StringComparer.Ordinal));
        Assert.Equal(["Email", "Password"],
            typeof(WebLoginRequest).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract").Order(StringComparer.Ordinal));
        var body = JsonSerializer.Deserialize<WebRegisterRequest>(
            """{"name":"Ana Quispe","email":"ana@correo.example","phone":"71234567","password":"Cliente-2026","role":"ADMIN","roles":["ADMIN"],"permissions":["iam.users.manage"],"branchCode":"SC","tenantCode":"OTRA","kind":"staff"}""",
            RpcJson.Options)!;
        Assert.Equal(new WebRegisterRequest("Ana Quispe", "ana@correo.example", "71234567", Password), body);
        // El comando: la empresa y la sucursal las pone el servidor desde su configuración; no hay rol ni permisos
        var inputs = typeof(RegisterCustomerAccountCommand).GetProperties().Select(p => p.Name)
            .Where(n => n is not ("EqualityContract" or nameof(IAuditableRequest.AuditDetails))).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["BranchCode", "ClientVersion", "Email", "MachineName", "Name", "Password", "Phone", "TenantCode"], inputs);
        Assert.DoesNotContain(inputs, n => n.Contains("Role", StringComparison.OrdinalIgnoreCase) || n.Contains("Permission", StringComparison.OrdinalIgnoreCase));
        // La sesión web nunca lleva el token ni el identificador de la sesión
        Assert.Equal(["Access", "Company", "DisplayName", "Email", "ExpiresAt", "Kind", "MustChangePassword", "Permissions", "Roles", "ServerVersion"],
            typeof(WebSession).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task La_contrasena_no_sale_en_el_texto_del_comando_ni_en_la_auditoria()
    {
        var command = Register();
        Assert.DoesNotContain(Password, command.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("7123", command.ToString(), StringComparison.Ordinal);
        Assert.Contains("ana@correo.example", command.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, new WebRegisterRequest("Ana", "ana@correo.example", "71234567", Password).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, new WebLoginRequest("ana@correo.example", Password).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("71234567", new WebRegisterRequest("Ana", "ana@correo.example", "71234567", Password).ToString(), StringComparison.Ordinal);

        var details = JsonSerializer.Serialize(command.AuditDetails);
        Assert.DoesNotContain(Password, details, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("7123-4567", details, StringComparison.Ordinal);
        Assert.Contains("ana@correo.example", details, StringComparison.Ordinal);

        // La tubería audita el registro logrado y el rechazado SIN la contraseña; la respuesta, sin el contacto
        var login = new LoginResult(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Ana Quispe", [RoleCodes.Customer],
            PermissionCodes.ForRole(RoleCodes.Customer), false, new BranchAccess(false, [new BranchInfo(Guid.NewGuid(), "CM", "Casa matriz")], null));
        var audit = new FakeAudit();
        var behavior = new AuditBehavior<RegisterCustomerAccountCommand, CustomerAccountSession>(audit);
        var created = await behavior.Handle(command, _ => Task.FromResult(new CustomerAccountSession(login, "WEB-000001")), CancellationToken.None);
        Assert.Equal("WEB-000001", created.CustomerCode);
        await Assert.ThrowsAsync<DomainException>(() => behavior.Handle(command,
            _ => throw new DomainException(AccountRules.EmailTaken, RegisterCustomerAccountHandler.EmailTakenMessage), CancellationToken.None));
        Assert.Equal([AuditOutcome.Succeeded, AuditOutcome.Rejected], audit.Entries.Select(e => e.Outcome));
        Assert.All(audit.Entries, e =>
        {
            Assert.Equal("RegisterCustomerAccount", e.Action);
            Assert.DoesNotContain(Password, e.Details, StringComparison.Ordinal);
            Assert.DoesNotContain("7123-4567", e.Details, StringComparison.Ordinal);
        });
        Assert.Contains("WEB-000001", audit.Entries[0].Details, StringComparison.Ordinal);
        Assert.Contains(login.SessionId.ToString(), audit.Entries[0].Details, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditResult", JsonSerializer.Serialize(created, RpcJson.Options), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void El_registro_valida_nombre_correo_telefono_y_una_contrasena_de_8_a_128_con_letras_y_numeros()
    {
        var validator = new RegisterCustomerAccountValidator();
        Assert.True(validator.Validate(Register()).IsValid);
        Assert.True(validator.Validate(Register(password: "abcdefg1")).IsValid);                          // 8: el mínimo
        Assert.True(validator.Validate(Register(password: "a1" + new string('x', 126))).IsValid);         // 128: el máximo
        Assert.True(validator.Validate(Register(name: " Ana ", email: "  ANA@Correo.Example ")).IsValid);  // los espacios de los extremos se recortan

        Assert.Equal([AccountRules.PasswordLengthMessage], Errors(validator, Register(password: "abcdef1")));
        Assert.Equal([AccountRules.PasswordLengthMessage], Errors(validator, Register(password: "a1" + new string('x', 127))));
        Assert.Equal([AccountRules.PasswordMixMessage], Errors(validator, Register(password: "solamenteletras")));
        Assert.Equal([AccountRules.PasswordMixMessage], Errors(validator, Register(password: "1234567890")));
        Assert.Equal([AccountRules.PasswordMixMessage], Errors(validator, Register(password: "--------!!")));
        Assert.Equal(["Indique una contraseña."], Errors(validator, Register(password: "")));
        Assert.All(new[] { "sin-arroba.example", "ana@correo", "ana @correo.example", "ana@correo.example, otra@correo.example", "Ana <ana@correo.example>",
                "ana@correo.example\r\nBcc: otra@correo.example", "ana@correo.example;otra@correo.example", new string('a', 250) + "@correo.example" },
            email => Assert.Equal([AccountRules.EmailMessage], Errors(validator, Register(email: email))));
        Assert.Equal(["Indique su correo."], Errors(validator, Register(email: " ")));
        Assert.Equal(["Indique su nombre."], Errors(validator, Register(name: "")));
        Assert.Equal(["El nombre debe tener al menos 2 caracteres."], Errors(validator, Register(name: " A ")));
        Assert.Equal([$"El nombre supera {AccountRules.MaxNameLength} caracteres."], Errors(validator, Register(name: new string('a', 121))));
        Assert.Equal(["El nombre " + ReservationRules.ControlCharacters], Errors(validator, Register(name: "Ana\r\nBcc: otra@correo.example")));
        Assert.Equal(["Indique un teléfono o WhatsApp."], Errors(validator, Register(phone: "")));
        Assert.Equal(["El teléfono supera 30 caracteres."], Errors(validator, Register(phone: new string('7', 31))));
        Assert.Equal(["La tienda no está configurada para registrar cuentas."], Errors(validator, Register(tenant: " ")));
        // Ningún mensaje repite la contraseña
        Assert.DoesNotContain(Errors(validator, Register(password: "clave-débil")), e => e.Contains("clave-débil", StringComparison.Ordinal));

        Assert.True(AccountRules.IsStrongPassword(Password));
        Assert.False(AccountRules.IsStrongPassword(null));
        Assert.True(AccountRules.IsEmail(" ana.quispe+tienda@correo.example "));
        Assert.False(AccountRules.IsEmail(null));
        Assert.Equal(("account.email_taken", "account.missing", "WEB"), (AccountRules.EmailTaken, AccountRules.Missing, AccountRules.CustomerCodePrefix));
    }

    // ------------------------------------------------------------------------------------------------ mi cuenta y mis reservas
    [Fact]
    public void Mis_datos_se_validan_y_se_auditan_enmascarados()
    {
        var validator = new UpdateMyAccountValidator();
        Assert.True(validator.Validate(new UpdateMyAccountCommand("Ana Quispe", "71234567")).IsValid);
        Assert.True(validator.Validate(new UpdateMyAccountCommand("Ana Quispe", "71234567", 1, "4567890", "1A")).IsValid);
        Assert.True(validator.Validate(new UpdateMyAccountCommand("Comercial Andina", "22212345", 5, "1023456029")).IsValid);

        Assert.Contains("Indique su nombre.", Errors(validator, new UpdateMyAccountCommand("", "71234567")));
        Assert.Contains("El nombre debe tener al menos 2 caracteres.", Errors(validator, new UpdateMyAccountCommand("A", "71234567")));
        Assert.Contains("El nombre " + ReservationRules.ControlCharacters, Errors(validator, new UpdateMyAccountCommand("Ana\tQuispe", "71234567")));
        Assert.Contains("Indique un teléfono o WhatsApp.", Errors(validator, new UpdateMyAccountCommand("Ana Quispe", " ")));
        Assert.Contains("El tipo de documento va de 1 (CI) a 5 (NIT).", Errors(validator, new UpdateMyAccountCommand("Ana Quispe", "71234567", 6, "123")));
        Assert.Contains(AccountRules.DocumentPairMessage, Errors(validator, new UpdateMyAccountCommand("Ana Quispe", "71234567", null, "4567890")));
        Assert.Contains(AccountRules.DocumentPairMessage, Errors(validator, new UpdateMyAccountCommand("Ana Quispe", "71234567", 1, " ")));
        Assert.Contains(AccountRules.ComplementMessage, Errors(validator, new UpdateMyAccountCommand("Ana Quispe", "71234567", 5, "1023456029", "1A")));
        Assert.Contains("El número de documento supera 20 caracteres.",
            Errors(validator, new UpdateMyAccountCommand("Ana Quispe", "71234567", 3, new string('7', 21))));
        Assert.Contains("El complemento tiene como máximo 5 caracteres.",
            Errors(validator, new UpdateMyAccountCommand("Ana Quispe", "71234567", 1, "4567890", "ABCDEF")));

        var command = new UpdateMyAccountCommand("Ana Quispe", "+591 7123-4567", 1, " 4567890 ", "1A");
        var details = JsonSerializer.Serialize(command.AuditDetails);
        Assert.DoesNotContain("4567890", details, StringComparison.Ordinal);
        Assert.DoesNotContain("7123-4567", details, StringComparison.Ordinal);
        Assert.DoesNotContain("1A", details, StringComparison.Ordinal);
        Assert.Contains("****890", details, StringComparison.Ordinal);
        Assert.DoesNotContain("4567890", command.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("7123", command.ToString(), StringComparison.Ordinal);

        // La respuesta viaja completa a su dueño y se audita enmascarada
        var view = new MyAccountView("Ana Quispe", "ana@correo.example", "+59171234567", 1, "4567890", "1A", "WEB-000001");
        var audited = JsonSerializer.Serialize(((IAuditableResponse)view).AuditResult);
        Assert.DoesNotContain("4567890", audited, StringComparison.Ordinal);
        Assert.DoesNotContain("59171234567", audited, StringComparison.Ordinal);
        Assert.DoesNotContain("1A", audited, StringComparison.Ordinal);
        Assert.Contains("WEB-000001", audited, StringComparison.Ordinal);
        var wire = JsonSerializer.Serialize(view, RpcJson.Options);
        Assert.Contains("\"documentNumber\":\"4567890\"", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("auditResult", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4567890", view.ToString(), StringComparison.Ordinal);
        var empty = JsonSerializer.Serialize(((IAuditableResponse)new MyAccountView("Ana", "ana@correo.example", null, null, null, null, "WEB-000002")).AuditResult);
        Assert.Contains("\"Phone\":null", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Mi_reserva_se_valida_como_la_de_la_tienda_y_es_un_carrito_por_defecto()
    {
        var validator = new CreateMyReservationValidator();
        var line = new StorefrontReservationLineInput("MON-LG-24GS60F");
        var reservation = new CreateMyReservationCommand([line]);
        Assert.Equal((PcBuildKind.Cart, (int?)null, (string?)null, (string?)null), (reservation.Kind, reservation.HoldDays, reservation.Notes, reservation.Name));
        Assert.True(validator.Validate(reservation).IsValid);
        Assert.True(validator.Validate(new CreateMyReservationCommand([line], PcBuildKind.Build, 3, "Paso el sábado", "Mi PC")).IsValid);

        Assert.Equal(["Agregue al menos un producto a la reserva."], Errors(validator, new CreateMyReservationCommand([])));
        Assert.Contains($"Una reserva admite como máximo {PcBuild.MaxLines} líneas.",
            Errors(validator, new CreateMyReservationCommand(Enumerable.Range(0, PcBuild.MaxLines + 1).Select(i => new StorefrontReservationLineInput("X" + i)).ToList())));
        Assert.Contains("Cada producto necesita su SKU.", Errors(validator, new CreateMyReservationCommand([new StorefrontReservationLineInput(" ")])));
        Assert.Contains($"La cantidad de cada producto va de 1 a {PcBuild.MaxQuantity}.",
            Errors(validator, new CreateMyReservationCommand([line with { Quantity = PcBuild.MaxQuantity + 1 }])));
        Assert.Contains($"La cantidad de cada producto va de 1 a {PcBuild.MaxQuantity}.", Errors(validator, new CreateMyReservationCommand([line with { Quantity = 0 }])));
        Assert.Contains(ReservationRules.HoldDaysMessage, Errors(validator, new CreateMyReservationCommand([line], HoldDays: 0)));
        Assert.Contains(ReservationRules.HoldDaysMessage, Errors(validator, new CreateMyReservationCommand([line], HoldDays: 4)));
        Assert.Contains("El tipo de reserva no existe.", Errors(validator, new CreateMyReservationCommand([line], (PcBuildKind)9)));
        Assert.Contains("Las notas superan 500 caracteres.", Errors(validator, new CreateMyReservationCommand([line], Notes: new string('n', 501))));
        Assert.Contains("Las notas van en una sola línea: " + ReservationRules.ControlCharacters,
            Errors(validator, new CreateMyReservationCommand([line], Notes: "uno\r\ndos")));
        Assert.Contains("El nombre de la reserva " + ReservationRules.ControlCharacters, Errors(validator, new CreateMyReservationCommand([line], Name: "Mi\tcarrito")));

        var cancel = new CancelMyReservationValidator();
        Assert.True(cancel.Validate(new CancelMyReservationCommand("RES-WEB-000001")).IsValid);
        Assert.Equal(["Indique el número de la reserva."], Errors(cancel, new CancelMyReservationCommand(" ")));
        Assert.False(cancel.Validate(new CancelMyReservationCommand(new string('9', 41))).IsValid);
        Assert.Contains("RES-WEB-000001", JsonSerializer.Serialize(new CancelMyReservationCommand("RES-WEB-000001").AuditDetails), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ permisos y errores
    [Fact]
    public async Task La_tuberia_exige_el_permiso_de_la_cuenta_aunque_la_lista_de_permitidos_falle()
    {
        var customer = new FakeUser(PermissionCodes.AccountManage, PermissionCodes.AccountReserve);
        var view = new MyAccountView("Ana Quispe", "ana@correo.example", "71234567", null, null, null, "WEB-000001");
        Assert.Same(view, await new AuthorizationBehavior<GetMyAccountQuery, MyAccountView>(customer, new FakeLicenses())
            .Handle(new GetMyAccountQuery(), _ => Task.FromResult(view), default));
        // El personal sin el permiso no entra a los casos de uso de la cuenta, y un cliente no entra a los del personal
        var cashier = new FakeUser([.. PermissionCodes.ForRole(RoleCodes.Cashier)]);
        var denied = await Assert.ThrowsAsync<AccessDeniedException>(() => new AuthorizationBehavior<GetMyAccountQuery, MyAccountView>(cashier, new FakeLicenses())
            .Handle(new GetMyAccountQuery(), _ => Task.FromResult(view), default));
        Assert.Contains(PermissionCodes.AccountManage, denied.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<AccessDeniedException>(() => new AuthorizationBehavior<CreateMyReservationCommand, StorefrontReservationView>(
            new FakeUser(PermissionCodes.AccountManage), new FakeLicenses())
            .Handle(new CreateMyReservationCommand([new StorefrontReservationLineInput("X")]), _ => throw new InvalidOperationException("no debía ejecutarse"), default));
        await Assert.ThrowsAsync<AccessDeniedException>(() => new AuthorizationBehavior<GetUsersQuery, IReadOnlyList<UserRow>>(customer, new FakeLicenses())
            .Handle(new GetUsersQuery(), _ => throw new InvalidOperationException("no debía ejecutarse"), default));
        await Assert.ThrowsAsync<AccessDeniedException>(() => new AuthorizationBehavior<GetStockProjectionQuery, StockProjectionView>(customer, new FakeLicenses())
            .Handle(new GetStockProjectionQuery(), _ => throw new InvalidOperationException("no debía ejecutarse"), default));
        // Sin sesión tampoco
        await Assert.ThrowsAsync<AccessDeniedException>(() => new AuthorizationBehavior<GetMyAccountQuery, MyAccountView>(new FakeUser(), new FakeLicenses())
            .Handle(new GetMyAccountQuery(), _ => Task.FromResult(view), default));
    }

    [Fact]
    public void La_cuenta_bloqueada_viaja_con_su_codigo_estable()
    {
        Assert.Equal("auth.locked", AuthenticationCodes.Locked);
        var locked = RpcCatalog.ToError(new AuthenticationFailedException("Cuenta bloqueada por 5 intentos fallidos: espere 15 minutos.", AuthenticationCodes.Locked));
        Assert.Equal((RpcErrorKinds.Authentication, AuthenticationCodes.Locked, 401), (locked.Kind, locked.Code, RpcCatalog.HttpStatusOf(locked.Kind)));
        var wrong = RpcCatalog.ToError(new AuthenticationFailedException("Empresa, correo o contraseña incorrectos."));
        Assert.Equal((RpcErrorKinds.Authentication, (string?)null), (wrong.Kind, wrong.Code));
        // De vuelta en el cliente es la misma excepción, con su código
        var back = Assert.IsType<AuthenticationFailedException>(RpcCatalog.ToException(locked));
        Assert.Equal((locked.Message, AuthenticationCodes.Locked), (back.Message, back.Code));
        Assert.Null(Assert.IsType<AuthenticationFailedException>(RpcCatalog.ToException(wrong)).Code);
        // El correo repetido es un error del dominio con su código
        var taken = RpcCatalog.ToError(new DomainException(AccountRules.EmailTaken, RegisterCustomerAccountHandler.EmailTakenMessage));
        Assert.Equal((RpcErrorKinds.Domain, AccountRules.EmailTaken, 422), (taken.Kind, taken.Code, RpcCatalog.HttpStatusOf(taken.Kind)));
    }
}
