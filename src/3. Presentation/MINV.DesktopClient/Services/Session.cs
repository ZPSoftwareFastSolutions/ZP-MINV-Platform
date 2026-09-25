using MediatR;
using MINV.Application.Iam;
using MINV.Application.Inventory.Queries;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.Services;

/// <summary>Cómo se conecta este cliente: PostgreSQL directo (base local), el servidor M-INV en la nube (V4) o la base en
/// memoria de la demostración.</summary>
public sealed record ConnectionInfo(bool IsDemo, string Server, string Database, string User, bool IsCloud = false)
{
    public string Description => IsDemo ? "Demostración en memoria (datos de la V2.1)"
        : IsCloud ? $"Nube · servidor {Server}" : $"PostgreSQL · {Server} · {Database}";
}

/// <summary>Sesión de trabajo del usuario (una por ingreso): quién es, qué puede hacer, en qué empresa y (V4) en qué
/// sucursal trabaja.</summary>
public sealed class SessionContext
{
    private LoginResult? _login;
    private WorkspaceInfo? _workspace;
    private BranchAccess? _access;

    public LoginResult Login => _login ?? throw new InvalidOperationException("No hay sesión iniciada.");

    public WorkspaceInfo Workspace => _workspace ?? throw new InvalidOperationException("No hay sesión iniciada.");

    public ConnectionInfo Connection { get; private set; } = new(false, "", "", "");

    public DateTimeOffset StartedAt { get; private set; }

    public string DisplayName => Login.DisplayName;

    public string Email { get; private set; } = string.Empty;

    public string Initials => Fmt.Initials(DisplayName);

    public string RoleNames => string.Join(" · ", Login.Roles.Select(r => RoleCodes.All.FirstOrDefault(x => x.Code == r).Name ?? r));

    public bool IsDemo => Connection.IsDemo;

    public bool IsCloud => Connection.IsCloud;

    /// <summary>V4 · Sucursales visibles y la activa (las decide el servidor al iniciar sesión).</summary>
    public BranchAccess Access => _access ?? throw new InvalidOperationException("No hay sesión iniciada.");

    /// <summary>V4 · Nombre de la sucursal activa (o «Todas las sucursales»).</summary>
    public string BranchText => _access?.Active is { } b ? $"{b.Code} · {b.Name}" : "Todas las sucursales";

    public bool Can(string permission) => _login?.Permissions.Contains(permission) == true;

    /// <summary>V4 · Cambió la sucursal activa: las pantallas recargan con los datos de la nueva sucursal.</summary>
    public event EventHandler? BranchChanged;

    public async Task StartAsync(SerialMediator mediator, LoginResult login, string email, ConnectionInfo connection, CancellationToken ct = default)
    {
        _login = login;
        _access = login.Access;
        Email = email;
        Connection = connection;
        StartedAt = DateTimeOffset.Now;
        await LoadWorkspaceAsync(mediator, ct);
    }

    /// <summary>V4 · Cambia la sucursal activa (el servidor valida que esté entre las del usuario).</summary>
    public async Task SelectBranchAsync(SerialMediator mediator, Guid? branchId, CancellationToken ct = default)
    {
        _access = await mediator.SendAsync(new SelectBranchCommand(Login.SessionId, branchId), ct);
        await LoadWorkspaceAsync(mediator, ct);
        BranchChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadWorkspaceAsync(SerialMediator mediator, CancellationToken ct)
    {
        _workspace = await mediator.SendAsync(new GetWorkspaceQuery(), ct);
        Fmt.CurrencySymbol = _workspace.CurrencySymbol;
        Fmt.CurrencyDecimals = Math.Clamp(_workspace.CurrencyDecimals, 0, 4);
    }
}

/// <summary>
/// Datos compartidos entre pantallas (proyección de stock y catálogo de búsqueda). Se leen una vez y se invalidan
/// después de cada escritura: el tablero, el stock, las alertas y el pedido muestran siempre lo mismo.
/// </summary>
public sealed class DataCache(SerialMediator mediator)
{
    private Task<StockProjectionView>? _projection;
    private Task<IReadOnlyList<ProductLookupItem>>? _lookup;

    /// <summary>Se incrementa con cada cambio de datos (las pantallas recargan al volver a mostrarse).</summary>
    public int Version { get; private set; }

    public event EventHandler? Changed;

    public Task<StockProjectionView> ProjectionAsync(bool force = false)
    {
        if (force || _projection is null || _projection.IsFaulted || _projection.IsCanceled)
        {
            _projection = mediator.SendAsync(new GetStockProjectionQuery());
        }
        return _projection;
    }

    public Task<IReadOnlyList<ProductLookupItem>> LookupAsync(bool force = false)
    {
        if (force || _lookup is null || _lookup.IsFaulted || _lookup.IsCanceled)
        {
            _lookup = mediator.SendAsync(new GetProductLookupQuery());
        }
        return _lookup;
    }

    /// <summary>Después de registrar movimientos o contabilizar un conteo.</summary>
    public void Invalidate()
    {
        _projection = null;
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Envía los casos de uso de a uno: el contexto de datos de la sesión (EF Core) no admite dos consultas a la vez, y la
/// interfaz puede pedir datos desde varias pantallas al mismo tiempo. Cada envío es una unidad de trabajo: antes se
/// descarta lo rastreado, así nunca se decide con existencias leídas antes de que otra caja las cambiara. V4: el envío lo
/// hace el transporte de la sesión (directo en este equipo o al servidor en la nube).
/// </summary>
public sealed class SerialMediator(IRequestTransport transport)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> SendAsync<T>(IRequest<T> request, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await transport.SendAsync(request, ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
