using MediatR;
using MINV.Application.Abstractions;
using MINV.Application.Behaviors;
using MINV.Application.Common;
using MINV.Application.Inventory.Movements;
using MINV.Application.Inventory.PhysicalCounts;
using MINV.Application.Sales;
using MINV.Domain.Common;
using MINV.Domain.Iam;

namespace MINV.Application.Tests;

internal sealed class FakeUser(params string[] permissions) : ICurrentUser
{
    public Guid? UserId { get; private set; } = permissions.Length > 0 ? Guid.NewGuid() : null;

    public string? Email { get; private set; } = "ana@demo.example";

    public string? DisplayName { get; private set; } = "Ana";

    public IReadOnlyCollection<string> Permissions { get; private set; } = permissions;

    public void SignIn(Guid userId, string email, string displayName, IReadOnlyCollection<string> p) => (UserId, Permissions) = (userId, p);

    public void SignOut() => UserId = null;
}

internal sealed class FakeLicenses(params string[] modules) : ILicenseService
{
    public Task<bool> IsModuleActiveAsync(string moduleCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(modules.Contains(moduleCode));
}

internal sealed class FakeAudit : IAuditTrail
{
    public List<AuditEntry> Entries { get; } = [];

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

public sealed class ValidationTests
{
    [Fact]
    public async Task Los_datos_invalidos_se_rechazan_antes_de_tocar_la_base_de_datos()
    {
        var behavior = new ValidationBehavior<RegisterMovementCommand, RegisterMovementResult>([new RegisterMovementValidator()]);
        var command = new RegisterMovementCommand("", "ALM01-GENERAL", "SALIDA", 0, DocumentReference: new string('x', 31));
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            behavior.Handle(command, _ => throw new InvalidOperationException("no debía ejecutarse"), CancellationToken.None));
        Assert.Contains("Elija el producto.", ex.Errors);
        Assert.Contains("La cantidad debe ser un número mayor que 0.", ex.Errors);
        Assert.Contains("El documento supera 30 caracteres.", ex.Errors);
    }
}

public sealed class AuthorizationTests
{
    private static Task<Guid> Next(CancellationToken _ = default) => Task.FromResult(Guid.NewGuid());

    [Fact]
    public async Task Sin_el_modulo_POS_licenciado_no_se_abre_caja()
    {
        var behavior = new AuthorizationBehavior<OpenPosSessionCommand, Guid>(new FakeUser(PermissionCodes.PosOperate), new FakeLicenses());
        var ex = await Assert.ThrowsAsync<AccessDeniedException>(() => behavior.Handle(new OpenPosSessionCommand("CAJA01", 100), Next, default));
        Assert.Contains(LicenseModuleCodes.PosHardware, ex.Message);
    }

    [Fact]
    public async Task Sin_el_permiso_del_rol_no_se_contabiliza_una_toma_fisica()
    {
        var behavior = new AuthorizationBehavior<PostPhysicalCountCommand, PostPhysicalCountResult>(
            new FakeUser(PermissionCodes.PhysicalCountRecord), new FakeLicenses());
        await Assert.ThrowsAsync<AccessDeniedException>(() => behavior.Handle(new PostPhysicalCountCommand(Guid.NewGuid(), true),
            _ => Task.FromResult<PostPhysicalCountResult>(null!), default));
    }

    [Fact]
    public async Task Con_permiso_y_licencia_el_caso_de_uso_se_ejecuta()
    {
        var behavior = new AuthorizationBehavior<OpenPosSessionCommand, Guid>(new FakeUser(PermissionCodes.PosOperate),
            new FakeLicenses(LicenseModuleCodes.PosHardware));
        Assert.NotEqual(Guid.Empty, await behavior.Handle(new OpenPosSessionCommand("CAJA01", 100), Next, default));
    }

    [Fact]
    public void La_matriz_RBAC_por_defecto_respeta_los_dominios_de_la_V21()
    {
        Assert.Equal(PermissionCodes.All.Count, PermissionCodes.ForRole(RoleCodes.Admin).Count);
        Assert.Contains(PermissionCodes.MovementsRegisterWarehouse, PermissionCodes.ForRole(RoleCodes.Warehouse));
        Assert.DoesNotContain(PermissionCodes.MovementsRegisterSales, PermissionCodes.ForRole(RoleCodes.Warehouse));
        Assert.DoesNotContain(PermissionCodes.MovementsRegisterWarehouse, PermissionCodes.ForRole(RoleCodes.Sales));
        // V4.1 · Consulta también ve los documentos fiscales y los libros (sin emitir ni anular)
        Assert.Equal([PermissionCodes.StockView, PermissionCodes.ReportsView, PermissionCodes.BillingView], PermissionCodes.ForRole(RoleCodes.ReadOnly));
        // Funciones por rol (V3.1 con base local): contabilidad solo gerencia y administración; usuarios solo administración
        Assert.Contains(PermissionCodes.AccountingManage, PermissionCodes.ForRole(RoleCodes.Management));
        Assert.DoesNotContain(PermissionCodes.AccountingManage, PermissionCodes.ForRole(RoleCodes.Cashier));
        Assert.Contains(PermissionCodes.PurchasingManage, PermissionCodes.ForRole(RoleCodes.Warehouse));
        Assert.Contains(PermissionCodes.CustomersManage, PermissionCodes.ForRole(RoleCodes.Sales));
        Assert.All(RoleCodes.All.Where(r => r.Code != RoleCodes.Admin),
            r => Assert.DoesNotContain(PermissionCodes.UsersManage, PermissionCodes.ForRole(r.Code)));
    }
}

public sealed class AuditTests
{
    private static readonly RegisterMovementCommand Command = new("FER-001", "ALM01-GENERAL", "SALIDA", 2);

    [Fact]
    public async Task Un_comando_exitoso_queda_auditado_como_Succeeded()
    {
        var audit = new FakeAudit();
        var behavior = new AuditBehavior<RegisterMovementCommand, RegisterMovementResult>(audit);
        await behavior.Handle(Command, _ => Task.FromResult(new RegisterMovementResult(Guid.NewGuid(), Guid.NewGuid(), 8, 8, "✔")), default);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("RegisterMovement", entry.Action);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
        Assert.Contains("FER-001", entry.Details);
    }

    [Fact]
    public async Task Un_bloqueo_del_dominio_tambien_se_audita_como_Rejected()
    {
        var audit = new FakeAudit();
        var behavior = new AuditBehavior<RegisterMovementCommand, RegisterMovementResult>(audit);
        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            behavior.Handle(Command, _ => throw new InsufficientStockException(0, 2), default));
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditOutcome.Rejected, entry.Outcome);
        Assert.Contains("Stock insuficiente", entry.Details);
    }

    [Fact]
    public async Task Un_error_inesperado_se_audita_como_Failed_y_se_propaga()
    {
        var audit = new FakeAudit();
        var behavior = new AuditBehavior<RegisterMovementCommand, RegisterMovementResult>(audit);
        await Assert.ThrowsAsync<TimeoutException>(() => behavior.Handle(Command, _ => throw new TimeoutException("red"), default));
        Assert.Equal(AuditOutcome.Failed, Assert.Single(audit.Entries).Outcome);
    }

    [Fact]
    public void El_login_no_es_auditable_por_esta_via_para_no_exponer_la_contrasena() =>
        Assert.False(typeof(IAuditableRequest).IsAssignableFrom(typeof(Iam.LoginCommand)));

    [Fact]
    public void Todos_los_comandos_que_escriben_estan_auditados()
    {
        var writes = typeof(RegisterMovementCommand).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Command", StringComparison.Ordinal) && t.Name is not "LoginCommand" and not "RecordCountCommand");
        Assert.All(writes, t => Assert.True(typeof(IAuditableRequest).IsAssignableFrom(t), t.Name));
        Assert.True(typeof(IRequest<Guid>).IsAssignableFrom(typeof(OpenPosSessionCommand)));
    }
}
