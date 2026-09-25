using MediatR;
using MINV.Application.Abstractions;
using MINV.Application.Iam;
using MINV.Application.Inventory.Queries;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.Services;

/// <summary>Cómo se conecta este cliente: PostgreSQL (producción) o la base en memoria de la demostración.</summary>
public sealed record ConnectionInfo(bool IsDemo, string Server, string Database, string User)
{
    public string Description => IsDemo ? "Demostración en memoria (datos de la V2.1)" : $"PostgreSQL · {Server} · {Database}";
}

/// <summary>Sesión de trabajo del usuario (una por ingreso): quién es, qué puede hacer y en qué empresa trabaja.</summary>
public sealed class SessionContext
{
    private LoginResult? _login;
    private WorkspaceInfo? _workspace;

    public LoginResult Login => _login ?? throw new InvalidOperationException("No hay sesión iniciada.");

    public WorkspaceInfo Workspace => _workspace ?? throw new InvalidOperationException("No hay sesión iniciada.");

    public ConnectionInfo Connection { get; private set; } = new(false, "", "", "");

    public DateTimeOffset StartedAt { get; private set; }

    public string DisplayName => Login.DisplayName;

    public string Email { get; private set; } = string.Empty;

    public string Initials => Fmt.Initials(DisplayName);

    public string RoleNames => string.Join(" · ", Login.Roles.Select(r => RoleCodes.All.FirstOrDefault(x => x.Code == r).Name ?? r));

    public bool IsDemo => Connection.IsDemo;

    public bool Can(string permission) => _login?.Permissions.Contains(permission) == true;

    public async Task StartAsync(IMediator mediator, LoginResult login, string email, ConnectionInfo connection, CancellationToken ct = default)
    {
        _login = login;
        Email = email;
        Connection = connection;
        StartedAt = DateTimeOffset.Now;
        _workspace = await mediator.Send(new GetWorkspaceQuery(), ct);
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
/// descarta lo rastreado, así nunca se decide con existencias leídas antes de que otra caja las cambiara.
/// </summary>
public sealed class SerialMediator(IMediator mediator, IMinvDbContext db)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> SendAsync<T>(IRequest<T> request, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            db.ClearTracking();
            return await mediator.Send(request, ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
