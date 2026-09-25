using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Inventory.Movements;
using MINV.Application.Inventory.PhysicalCounts;
using MINV.Application.Inventory.Queries;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Infrastructure.Demo;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// Modo demostración del cliente de escritorio: el libro REAL de la V2.1 migrado a la base en memoria y los casos de uso
/// de verdad (tubería MediatR completa: validación, RBAC, auditoría) sobre el modelo EF Core completo. También prueba
/// las consultas que usa la interfaz (ficha y kardex, tendencia, últimos movimientos, toma física, contraseña).
/// </summary>
public sealed class DemoWorkspaceTests
{
    internal static async Task<(ServiceProvider Services, DemoSession Demo)> PrepareAsync()
    {
        var services = new ServiceCollection();
        services.AddMinvApplication();
        services.AddMinvDemoInfrastructure();
        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var demo = await scope.ServiceProvider.GetRequiredService<DemoWorkspace>()
            .PrepareAsync(Path.Combine(V21MigrationTests.RepoRoot(), "src", "M-INV_V2_Colaborativo.xlsx"));
        return (sp, demo);
    }

    internal static async Task<(IServiceScope Scope, IMediator Mediator, LoginResult Login)> SignInAsync(ServiceProvider sp, DemoSession demo,
        string email = DemoWorkspace.AdminEmail, string? password = null)
    {
        var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var login = await mediator.Send(new LoginCommand(demo.TenantCode, email, password ?? demo.Password, "PRUEBAS", "test"));
        return (scope, mediator, login);
    }

    [Fact]
    public async Task La_demostracion_migra_la_V21_con_paridad_y_permite_ingresar_con_cada_rol()
    {
        var (sp, demo) = await PrepareAsync();
        Assert.True(demo.Import.Parity.Ok, string.Join(" · ", demo.Import.Parity.Differences));
        Assert.Equal(473, demo.Import.Report.Movements);
        Assert.Equal(RoleCodes.Admin, demo.Users[0].RoleCode);
        Assert.True(demo.Users.Count >= 5);

        var (scope, mediator, login) = await SignInAsync(sp, demo);
        using (scope)
        {
            Assert.Contains(RoleCodes.Admin, login.Roles);
            var workspace = await mediator.Send(new GetWorkspaceQuery());
            Assert.Equal("Distribuidora Demo S.A.S.", workspace.CompanyName);
            Assert.Equal("ALM01", workspace.WarehouseCode);
            Assert.Equal(demo.Today, workspace.Today);
            var view = await mediator.Send(new GetStockProjectionQuery());
            Assert.Equal(34, view.Result.Stock.Count);
            Assert.Equal(473, view.Result.Movements);
            Assert.NotEmpty(view.Result.Alerts);
            Assert.NotEmpty(view.Result.Order);
        }

        // Cada usuario de la V2.1 entra con su rol (la interfaz se adapta a sus permisos)
        foreach (var user in demo.Users)
        {
            var (s, _, l) = await SignInAsync(sp, demo, user.Email);
            using (s)
            {
                Assert.Contains(user.RoleCode, l.Roles);
            }
        }
    }

    [Fact]
    public async Task Consultas_de_la_interfaz_ficha_kardex_tendencia_y_ultimos_movimientos()
    {
        var (sp, demo) = await PrepareAsync();
        var (scope, mediator, _) = await SignInAsync(sp, demo);
        using var _s = scope;
        var stock = (await mediator.Send(new GetStockProjectionQuery())).Result.Stock;

        var lookup = await mediator.Send(new GetProductLookupQuery());
        Assert.Equal(stock.Count(r => r.IsActive), lookup.Count);
        Assert.All(lookup, p => Assert.False(string.IsNullOrEmpty(p.PrimaryBin)));
        Assert.NotEmpty(await mediator.Send(new GetBinsQuery()));
        var types = await mediator.Send(new GetMovementTypesQuery());
        Assert.Contains(types, t => t.Code == MovementTypeCodes.Receipt && t.StockFactor == 1);

        // Ficha: el kardex termina en el stock de la proyección y el saldo acumulado cuadra fila por fila
        foreach (var row in stock.Where(r => r.Entries + r.Issues > 0).Take(8))
        {
            var card = await mediator.Send(new GetProductCardQuery(row.Sku, 5000));
            Assert.Equal(row.Stock, card.OnHand);
            Assert.Equal(row.Status, card.Status);
            Assert.Equal(row.Stock, card.Movements[0].Balance);
            Assert.Equal(card.OnHand, card.Movements.Sum(m => m.Signed));
            Assert.Equal(card.TotalMovements, card.Movements.Count);
            Assert.Equal(card.OnHand, card.Bins.Sum(b => b.OnHand));
        }

        var recent = await mediator.Send(new GetRecentMovementsQuery(10));
        Assert.Equal(10, recent.Count);
        Assert.True(recent.Zip(recent.Skip(1)).All(p => p.First.RecordedAt >= p.Second.RecordedAt));

        var trend = await mediator.Send(new GetMovementTrendQuery(30));
        Assert.Equal(30, trend.Count);
        Assert.Equal(demo.Today, trend[^1].Date);
        Assert.True(trend.Sum(d => d.Movements) > 0);
    }

    [Fact]
    public async Task Registrar_un_movimiento_actualiza_la_ficha_y_el_poka_yoke_bloquea_el_negativo()
    {
        var (sp, demo) = await PrepareAsync();
        var (scope, mediator, _) = await SignInAsync(sp, demo);
        using var _s = scope;
        var row = (await mediator.Send(new GetStockProjectionQuery())).Result.Stock.First(r => r.Stock > 0 && r.IsActive);
        var before = await mediator.Send(new GetProductCardQuery(row.Sku));
        var bin = before.PrimaryBin!;
        var result = await mediator.Send(new RegisterMovementCommand(row.Sku, bin, MovementTypeCodes.Receipt, 5, DocumentReference: "FAC-1"));
        Assert.StartsWith("✔", result.Message);
        var after = await mediator.Send(new GetProductCardQuery(row.Sku));
        Assert.Equal(before.OnHand + 5, after.OnHand);
        Assert.Equal("FAC-1", after.Movements[0].Document);
        Assert.Equal(before.TotalMovements + 1, after.TotalMovements);
        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            mediator.Send(new RegisterMovementCommand(row.Sku, bin, MovementTypeCodes.Issue, after.OnHand + 1)));
        Assert.Equal(after.OnHand, (await mediator.Send(new GetProductCardQuery(row.Sku))).OnHand);
    }

    [Fact]
    public async Task Toma_fisica_abrir_contar_corregir_y_contabilizar()
    {
        var (sp, demo) = await PrepareAsync();
        var (scope, mediator, _) = await SignInAsync(sp, demo);
        using var _s = scope;
        // La V2.1 dejó una toma en curso: se anula para empezar de cero
        if (await mediator.Send(new GetOpenPhysicalCountQuery()) is { } previous)
        {
            await mediator.Send(new CancelPhysicalCountCommand(previous.Id));
        }
        Assert.Null(await mediator.Send(new GetOpenPhysicalCountQuery()));
        var opened = await mediator.Send(new OpenPhysicalCountCommand("ALM01"));
        var row = (await mediator.Send(new GetStockProjectionQuery())).Result.Stock.First(r => r.Stock > 0 && r.IsActive);
        var card = await mediator.Send(new GetProductCardQuery(row.Sku));
        var bin = card.Bins.First(b => b.OnHand > 0);

        await mediator.Send(new RecordCountCommand(opened.PhysicalCountId, row.Sku, bin.BinCode, bin.OnHand + 3));
        var sheet = (await mediator.Send(new GetOpenPhysicalCountQuery()))!;
        Assert.Equal(opened.Number, sheet.Number);
        Assert.Equal(3, Assert.Single(sheet.Lines).Difference);

        Assert.True(await mediator.Send(new RemoveCountCommand(opened.PhysicalCountId, row.Sku, bin.BinCode)));
        Assert.Empty((await mediator.Send(new GetOpenPhysicalCountQuery()))!.Lines);
        await mediator.Send(new RecordCountCommand(opened.PhysicalCountId, row.Sku, bin.BinCode, bin.OnHand + 2));

        var posted = await mediator.Send(new PostPhysicalCountCommand(opened.PhysicalCountId, Confirmed: true));
        Assert.Equal(1, posted.Surpluses);
        Assert.Null(await mediator.Send(new GetOpenPhysicalCountQuery()));
        var after = await mediator.Send(new GetProductCardQuery(row.Sku));
        Assert.Equal(card.OnHand + 2, after.OnHand);
        Assert.Equal(MovementTypeCodes.AdjustmentIn, after.Movements[0].TypeCode);
        Assert.Equal(opened.Number, after.Movements[0].Document);
    }

    [Fact]
    public async Task Cambiar_la_contrasena_cerrar_sesion_y_permisos_por_rol()
    {
        var (sp, demo) = await PrepareAsync();
        var (scope, mediator, login) = await SignInAsync(sp, demo);
        using (scope)
        {
            await Assert.ThrowsAsync<AuthenticationFailedException>(() => mediator.Send(new ChangePasswordCommand("incorrecta1", "NuevaClave2026")));
            var invalid = await Assert.ThrowsAsync<RequestValidationException>(() => mediator.Send(new ChangePasswordCommand(demo.Password, "corta")));
            Assert.Contains(invalid.Errors, e => e.Contains("8 caracteres", StringComparison.Ordinal));
            Assert.True(await mediator.Send(new ChangePasswordCommand(demo.Password, "NuevaClave2026")));
            var activity = await mediator.Send(new GetActivityQuery(20));
            Assert.Contains(activity, a => a.Action == "ChangePassword" && a.Outcome == AuditOutcome.Rejected);
            Assert.Contains(activity, a => a.Action == "ChangePassword" && a.Outcome == AuditOutcome.Succeeded);
            Assert.True(await mediator.Send(new LogoutCommand(login.SessionId)));
            Assert.False(await mediator.Send(new LogoutCommand(login.SessionId)));   // ya estaba cerrada
            var db = scope.ServiceProvider.GetRequiredService<MINV.Infrastructure.Persistence.MinvWriteDbContext>();
            Assert.False((await db.Sessions.FindAsync(login.SessionId))!.IsOpen);
            Assert.Contains(await mediator.Send(new GetActivityQuery(5)), a => a.Action == "Logout" && a.UserName == "Administrador M-INV");
        }
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => SignInAsync(sp, demo));
        var (again, _, _) = await SignInAsync(sp, demo, password: "NuevaClave2026");
        again.Dispose();

        // Ventas no registra entradas de bodega ni cuenta; sí consulta el stock
        var seller = demo.Users.First(u => u.RoleCode == RoleCodes.Sales);
        var (s2, m2, _) = await SignInAsync(sp, demo, seller.Email);
        using (s2)
        {
            Assert.NotEmpty((await m2.Send(new GetStockProjectionQuery())).Result.Stock);
            var sku = (await m2.Send(new GetProductLookupQuery()))[0];
            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                m2.Send(new RegisterMovementCommand(sku.Sku, sku.PrimaryBin!, MovementTypeCodes.Receipt, 1)));
            await Assert.ThrowsAsync<AccessDeniedException>(() => m2.Send(new GetOpenPhysicalCountQuery()));
        }
    }
}
