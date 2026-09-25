using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MINV.Application.Accounting;
using MINV.Application.Common;
using MINV.Application.Corporate;
using MINV.Application.Iam;
using MINV.Application.Inventory.Transfers;
using MINV.Application.Remote;
using MINV.Domain.Iam;
using MINV.Infrastructure.Seeding;

namespace MINV.Integration.Tests;

/// <summary>Cliente mínimo del contrato RPC (lo mismo que hace el escritorio en modo nube).</summary>
internal sealed class RpcTestClient(HttpClient http)
{
    public const string ClientVersion = "4.0.0-alpha.1";

    public string? Token { get; private set; }

    public static HttpClient Configure(HttpClient http)
    {
        http.DefaultRequestHeaders.Add("X-MINV-Client-Version", ClientVersion);
        return http;
    }

    public async Task<(HttpStatusCode Status, CloudLoginResponse? Login, RpcResponse? Error)> LoginAsync(string tenant, SeedUser user, string? password = null)
    {
        var response = await http.PostAsJsonAsync("/api/v1/session/login",
            new CloudLoginRequest(tenant, user.Email, password ?? user.Password, "PRUEBAS", ClientVersion), RpcJson.Options);
        if (!response.IsSuccessStatusCode)
        {
            return (response.StatusCode, null, await response.Content.ReadFromJsonAsync<RpcResponse>(RpcJson.Options));
        }
        var login = await response.Content.ReadFromJsonAsync<CloudLoginResponse>(RpcJson.Options);
        Token = login!.Token;
        return (response.StatusCode, login, null);
    }

    public async Task<(HttpStatusCode Status, RpcResponse Response)> SendRawAsync(object request, Guid? requestId = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/rpc")
        {
            Content = JsonContent.Create(new RpcRequest(requestId ?? Guid.NewGuid(), RpcCatalog.NameOf(request.GetType()),
                JsonSerializer.SerializeToElement(request, request.GetType(), RpcJson.Options)), options: RpcJson.Options),
        };
        if (Token is not null)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }
        var response = await http.SendAsync(message);
        return (response.StatusCode, (await response.Content.ReadFromJsonAsync<RpcResponse>(RpcJson.Options))!);
    }

    public async Task<T> SendAsync<T>(MediatR.IRequest<T> request, Guid? requestId = null)
    {
        var (_, response) = await SendRawAsync(request, requestId);
        if (!response.Ok)
        {
            throw RpcCatalog.ToException(response.Error!);
        }
        return response.Result!.Value.Deserialize<T>(RpcJson.Options)!;
    }
}

public sealed class CloudServerTests(CloudServerFixture server) : IClassFixture<CloudServerFixture>
{
    private RpcTestClient Client() => new(RpcTestClient.Configure(server.CreateClient()));

    private SeedUser User(string role, string? branches = null) =>
        server.Seed.Users.First(u => u.RoleCode == role && (branches is null || u.Branches == branches));

    [Fact]
    public async Task El_servidor_responde_su_estado_y_rechaza_escritorios_de_otra_version()
    {
        var http = server.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/v1/health")).StatusCode);
        http.DefaultRequestHeaders.Add("X-MINV-Client-Version", "3.1.0");
        var old = await http.PostAsJsonAsync("/api/v1/session/login", new CloudLoginRequest("NUBE", "x@y.example", "z", "PC", "3.1.0"), RpcJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        var body = await old.Content.ReadFromJsonAsync<RpcResponse>(RpcJson.Options);
        Assert.Equal(RpcErrorKinds.Unsupported, body!.Error!.Kind);
    }

    [Fact]
    public async Task Sin_token_o_con_clave_incorrecta_no_hay_acceso()
    {
        var client = Client();
        var (status, _, error) = await client.LoginAsync("NUBE", User(RoleCodes.Management), "Clave-Equivocada-1");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(RpcErrorKinds.Authentication, error!.Error!.Kind);
        var (rpcStatus, response) = await client.SendRawAsync(new GetBranchesQuery());
        Assert.Equal(HttpStatusCode.Unauthorized, rpcStatus);
        Assert.False(response.Ok);
    }

    [Fact]
    public async Task El_alcance_por_sucursal_lo_decide_el_servidor()
    {
        var manager = Client();
        var (_, login, _) = await manager.LoginAsync("NUBE", User(RoleCodes.Management));
        Assert.True(login!.Login.Access.AllBranches);
        Assert.StartsWith("mses_", login.Token, StringComparison.Ordinal);
        Assert.All(await manager.SendAsync(new GetBranchesQuery()), b => Assert.True(b.IsVisible));

        var cashier = Client();
        var (_, cashierLogin, _) = await cashier.LoginAsync("NUBE", User(RoleCodes.Cashier, "EA"));
        Assert.Equal("EA", cashierLogin!.Login.Access.Active!.Code);
        var branches = await cashier.SendAsync(new GetBranchesQuery());
        Assert.Equal(["EA"], branches.Where(b => b.IsVisible).Select(b => b.Code));
        await Assert.ThrowsAsync<AccessDeniedException>(() => cashier.SendAsync(new GetUsersQuery()));
    }

    [Fact]
    public async Task Un_comando_repetido_con_el_mismo_id_no_se_ejecuta_dos_veces()
    {
        var manager = Client();
        await manager.LoginAsync("NUBE", User(RoleCodes.Management));
        var id = Guid.NewGuid();
        var command = new CreateJournalEntryCommand(server.Seed.To, "Pago de servicios (prueba de idempotencia)",
            [new JournalLineSpec("6.1.03", 90, 0), new JournalLineSpec(MINV.Domain.Accounting.AccountCodes.Bank, 0, 90)]);
        var (_, first) = await manager.SendRawAsync(command, id);
        var (_, again) = await manager.SendRawAsync(command, id);
        Assert.True(first.Ok && again.Ok);
        Assert.False(first.Replayed);
        Assert.True(again.Replayed);
        Assert.Equal(first.Result!.Value.GetString(), again.Result!.Value.GetString());
        var entries = await manager.SendAsync(new GetJournalQuery(server.Seed.To, server.Seed.To));
        Assert.Single(entries, e => e.Description.Contains("idempotencia", StringComparison.Ordinal));

        var other = command with { Description = "Otro contenido con el mismo id" };
        var (status, conflict) = await manager.SendRawAsync(other, id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(RpcErrorKinds.Idempotency, conflict.Error!.Kind);
    }

    [Fact]
    public async Task Los_errores_del_dominio_llegan_con_su_codigo()
    {
        var keeper = Client();
        await keeper.LoginAsync("NUBE", User(RoleCodes.Warehouse, "CM"));
        var (status, response) = await keeper.SendRawAsync(new CreateTransferCommand("ALM01", [new TransferLineInput("FER-001", 1)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal("transfer.same_warehouse", response.Error!.Code);
        var (notFound, _) = await keeper.SendRawAsync(new CancelTransferCommand(Guid.NewGuid(), "no existe"));
        Assert.Equal(HttpStatusCode.NotFound, notFound);
        var (invalid, validation) = await keeper.SendRawAsync(new CancelTransferCommand(Guid.NewGuid(), ""));
        Assert.Equal(HttpStatusCode.BadRequest, invalid);
        Assert.NotEmpty(validation.Error!.Errors!);
    }

    [Fact]
    public void Todos_los_comandos_y_respuestas_se_pueden_serializar()
    {
        Assert.True(RpcCatalog.Names.Count > 80, $"Solo {RpcCatalog.Names.Count} operaciones");
        Assert.DoesNotContain(RpcCatalog.NameOf(typeof(LoginCommand)), RpcCatalog.Names);
        foreach (var name in RpcCatalog.Names)
        {
            Assert.True(RpcCatalog.TryResolve(name, out var request, out var response));
            _ = RpcJson.Options.GetTypeInfo(request);
            _ = RpcJson.Options.GetTypeInfo(response);
        }
    }
}
