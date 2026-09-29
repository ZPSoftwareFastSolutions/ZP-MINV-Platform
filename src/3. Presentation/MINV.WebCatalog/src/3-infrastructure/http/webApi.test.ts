// Adaptador HTTP de la sesión web y del RPC con `fetch` simulado: cabecera propia, `credentials`, mismo origen, las dos
// formas de respuesta (sesión directa o envuelta), traducción de errores, 429 sin cuerpo, 401 y red caída. Ninguna
// prueba toca la red.

import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { isUuid } from '@/shared/ids';
import { ACCOUNT_OPERATIONS, isRpcOperation, permissionName, roleName, rpcOperation, RPC_OPERATIONS } from './contract';
import { HttpRpcGateway } from './HttpRpcGateway';
import { HttpSessionGateway } from './HttpSessionGateway';
import { RpcAccountGateway } from './RpcAccountGateway';
import { CLIENT_VERSION, CLIENT_VERSION_HEADER, toWebApiError, unwrapResult, WebApi } from './webApi';
import { toSession } from './webMappers';

const SESSION_DTO = {
  displayName: 'Valentina Aguirre',
  email: 'valentina@correo.example',
  roles: ['CLIENTE'],
  permissions: ['account.manage', 'account.reserve'],
  mustChangePassword: false,
  access: { allBranches: false, branches: [{ id: 'b-1', code: 'CM', name: 'Casa matriz La Paz' }], activeBranchId: 'b-1' },
  kind: 'customer',
  expiresAt: '2026-09-29T06:00:00+00:00',
  serverVersion: '7.0.0-alpha.1',
  company: 'Tech Zone Gaming S.R.L.',
};

const RESERVATION_VIEW = {
  number: 'RES-WEB-000012',
  status: 'Reserved',
  statusText: 'Reservada',
  createdAt: '2026-09-27T18:39:56.7982846+00:00',
  reservedUntil: '2026-09-29T18:39:56.7982846+00:00',
  total: 1299,
  contactName: 'Valentina Aguirre',
  branch: 'CM',
  notes: null,
  hasCompatibilityWarnings: false,
  lines: [{ slot: null, sku: 'CASE-COR-4000D', name: 'Gabinete Corsair 4000D Airflow negro', quantity: 1, unitPrice: 1299, subtotal: 1299 }],
  cancelReason: null,
  kind: 'cart',
  mailQueued: true,
};

function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json; charset=utf-8', ...headers } });
}

function rpcError(kind: string, message: string, status: number, extra: Record<string, unknown> = {}): Response {
  return json({ ok: false, result: null, error: { kind, message, errors: null, code: null, ...extra }, replayed: false }, status);
}

let fetchMock: Mock;

beforeEach(() => {
  fetchMock = vi.fn();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function lastRequest(): { url: string; init: RequestInit; headers: Record<string, string>; body: unknown } {
  const [url, init] = fetchMock.mock.calls.at(-1) as [string, RequestInit];
  return { url, init, headers: init.headers as Record<string, string>, body: init.body === undefined ? undefined : JSON.parse(String(init.body)) };
}

describe('WebApi', () => {
  it('toda petición va al mismo origen con la cabecera propia y la cookie de la sesión', async () => {
    fetchMock.mockResolvedValue(json(SESSION_DTO));
    await new WebApi().get('/session');
    const { url, init, headers } = lastRequest();
    expect(url).toBe('/api/v1/web/session');
    expect(init.method).toBe('GET');
    expect(init.credentials).toBe('same-origin');
    expect(init.mode).toBe('same-origin');
    expect(init.cache).toBe('no-store');
    expect(headers[CLIENT_VERSION_HEADER]).toBe('7.0.0');
    expect(CLIENT_VERSION_HEADER).toBe('X-MINV-Client-Version');
    expect(CLIENT_VERSION).toBe('7.0.0');
    expect(headers.Accept).toBe('application/json');
    expect(headers['Content-Type']).toBeUndefined();
    expect(init.body).toBeUndefined();
  });

  it('un POST lleva el cuerpo en JSON y la misma cabecera propia', async () => {
    fetchMock.mockResolvedValue(json({ ok: true, result: 1 }));
    await new WebApi().post('rpc', { requestId: 'x', type: 'T', payload: {} });
    const { url, init, headers, body } = lastRequest();
    expect(url).toBe('/api/v1/web/rpc');
    expect(init.method).toBe('POST');
    expect(init.credentials).toBe('same-origin');
    expect(headers[CLIENT_VERSION_HEADER]).toBe('7.0.0');
    expect(headers['Content-Type']).toBe('application/json');
    expect(body).toEqual({ requestId: 'x', type: 'T', payload: {} });
  });

  it('nunca envía ni espera un token: ni cabecera Authorization ni datos en la URL', async () => {
    fetchMock.mockResolvedValue(json(SESSION_DTO));
    await new WebApi().post('/session/login', { email: 'a@b.example', password: 'Clave123' });
    const { url, headers } = lastRequest();
    expect(Object.keys(headers).map((name) => name.toLowerCase())).not.toContain('authorization');
    expect(url).not.toContain('Clave123');
    expect(url).not.toContain('?');
  });

  it('sin respuesta del servidor lanza un error de red (con el id del pedido para reintentar)', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(new WebApi().post('/rpc', {}, { requestId: 'r-1' })).rejects.toMatchObject({ kind: 'network', status: 0, requestId: 'r-1' });
  });

  it('una petición cancelada no se convierte en error de red', async () => {
    fetchMock.mockRejectedValue(new DOMException('cancelada', 'AbortError'));
    await expect(new WebApi().get('/session')).rejects.toMatchObject({ name: 'AbortError' });
  });

  it('un 429 sin cuerpo da un mensaje claro; con Retry-After dice cuánto esperar', async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 429 }));
    await expect(new WebApi().post('/session/login', {})).rejects.toMatchObject({
      kind: 'rate_limited',
      status: 429,
      message: 'Se superó el límite de solicitudes. Espere un minuto y vuelva a intentar.',
    });
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 429, headers: { 'Retry-After': '30' } }));
    await expect(new WebApi().post('/session/login', {})).rejects.toMatchObject({ kind: 'rate_limited', message: expect.stringContaining('30 segundos') });
    fetchMock.mockResolvedValueOnce(rpcError('validation', 'Demasiados registros desde su conexión.', 429));
    await expect(new WebApi().post('/account/register', {})).rejects.toMatchObject({ kind: 'rate_limited', message: 'Demasiados registros desde su conexión.' });
  });

  it('una página HTML de un intermediario (502) no rompe: error del servidor con texto propio', async () => {
    fetchMock.mockResolvedValue(new Response('<html><body>Bad Gateway</body></html>', { status: 502, headers: { 'Content-Type': 'text/html' } }));
    await expect(new WebApi().get('/session')).rejects.toMatchObject({ kind: 'server', status: 502, message: expect.stringContaining('El servidor no pudo completar') });
  });

  it('un 204 no trae cuerpo', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));
    await expect(new WebApi().post('/session/logout')).resolves.toEqual({ status: 204, body: undefined });
  });

  it('un sobre `ok: false` con código 200 también es un error', async () => {
    fetchMock.mockResolvedValue(json({ ok: false, error: { kind: 'domain', message: 'Regla del negocio.', code: 'pcbuild.state' } }, 200));
    await expect(new WebApi().post('/rpc', {})).rejects.toMatchObject({ kind: 'domain', code: 'pcbuild.state', message: 'Regla del negocio.' });
  });
});

describe('traducción de errores', () => {
  it.each([
    [400, 'validation'],
    [401, 'authentication'],
    [403, 'access_denied'],
    [404, 'not_found'],
    [409, 'concurrency'],
    [422, 'domain'],
    [429, 'rate_limited'],
    [500, 'server'],
    [503, 'server'],
    [418, 'unknown'],
  ])('HTTP %i sin cuerpo → %s con un mensaje propio', (status, kind) => {
    const error = toWebApiError(status);
    expect(error.kind).toBe(kind);
    expect(error.status).toBe(status);
    expect(error.message.length).toBeGreaterThan(10);
    expect(error.errors).toEqual([]);
    expect(error.code).toBeNull();
  });

  it('toma kind, message, errors y code del sobre del servidor', () => {
    const error = toWebApiError(400, { ok: false, error: { kind: 'validation', message: 'Datos no válidos', errors: ['Indique su correo.', 'Indique su contraseña.'], code: null } });
    expect(error).toMatchObject({ kind: 'validation', status: 400, message: 'Datos no válidos', errors: ['Indique su correo.', 'Indique su contraseña.'], code: null });
    expect(toWebApiError(422, { ok: false, error: { kind: 'domain', message: 'Ese correo ya tiene una cuenta.', code: 'account.email_taken' } })).toMatchObject({
      kind: 'domain',
      code: 'account.email_taken',
    });
  });

  it('422 distingue la regla del negocio de la idempotencia; 400 la versión no compatible', () => {
    expect(toWebApiError(422, { ok: false, error: { kind: 'idempotency', message: 'Ese identificador de pedido ya se usó con otro contenido.' } }).kind).toBe('idempotency');
    expect(toWebApiError(400, { ok: false, error: { kind: 'unsupported', message: 'Este servidor es M-INV 8.0.0: actualice.' } }).kind).toBe('unsupported');
    // Un `kind` que la web no conoce se deduce del código HTTP.
    expect(toWebApiError(403, { ok: false, error: { kind: 'otro', message: 'x' } }).kind).toBe('access_denied');
  });

  it('entiende problem+json (detail, title y errors por campo)', () => {
    const error = toWebApiError(400, { type: 'about:blank', title: 'Validación', status: 400, detail: 'Hay datos que corregir.', errors: { Email: ['Indique su correo.'], Password: ['Indique su contraseña.'] } });
    expect(error).toMatchObject({ kind: 'validation', message: 'Hay datos que corregir.', errors: ['Indique su correo.', 'Indique su contraseña.'] });
  });

  it('en una falla técnica no muestra el detalle interno del servidor', () => {
    const error = toWebApiError(500, { ok: false, error: { kind: 'server', message: 'NpgsqlException: connection refused at 10.0.0.5:5432' } });
    expect(error.kind).toBe('server');
    expect(error.message).not.toContain('Npgsql');
    expect(error.message).not.toContain('10.0.0.5');
  });

  it('quita el sobre `{ ok, result }` y reconoce la repetición idempotente', () => {
    expect(unwrapResult({ ok: true, result: { a: 1 }, replayed: true })).toEqual({ result: { a: 1 }, replayed: true });
    expect(unwrapResult({ ok: true, result: null })).toEqual({ result: null, replayed: false });
    expect(unwrapResult({ ok: true })).toEqual({ result: null, replayed: false });
    expect(unwrapResult(SESSION_DTO)).toEqual({ result: SESSION_DTO, replayed: false });
    expect(unwrapResult(undefined)).toEqual({ result: undefined, replayed: false });
  });
});

describe('HttpSessionGateway', () => {
  const gateway = () => new HttpSessionGateway(new WebApi());

  it('ingresa con POST /session/login y acepta la sesión DIRECTA', async () => {
    fetchMock.mockResolvedValue(json(SESSION_DTO));
    const session = await gateway().login({ email: 'valentina@correo.example', password: 'Clave123' });
    const { url, init, body } = lastRequest();
    expect(url).toBe('/api/v1/web/session/login');
    expect(init.method).toBe('POST');
    expect(body).toEqual({ email: 'valentina@correo.example', password: 'Clave123' });
    expect(session).toMatchObject({ displayName: 'Valentina Aguirre', kind: 'customer', roles: ['CLIENTE'], mustChangePassword: false, company: 'Tech Zone Gaming S.R.L.' });
    expect(session.expiresAt.toISOString()).toBe('2026-09-29T06:00:00.000Z');
    expect(session.access.branches[0]).toEqual({ id: 'b-1', code: 'CM', name: 'Casa matriz La Paz' });
  });

  it('acepta la sesión ENVUELTA en `{ ok, result }`', async () => {
    fetchMock.mockResolvedValue(json({ ok: true, result: { ...SESSION_DTO, kind: 'staff', roles: ['ADMIN'] }, error: null, replayed: false }));
    const session = await gateway().login({ email: 'admin@techzone.example', password: 'Clave123' });
    expect(session).toMatchObject({ kind: 'staff', roles: ['ADMIN'], email: 'valentina@correo.example' });
  });

  it('la sesión nunca expone un token aunque el servidor lo enviara por error', async () => {
    fetchMock.mockResolvedValue(json({ ...SESSION_DTO, token: 'mses_secreto', sessionId: 'abc' }));
    const session = await gateway().login({ email: 'valentina@correo.example', password: 'Clave123' });
    expect(JSON.stringify(session)).not.toContain('mses_secreto');
    expect(Object.keys(session).sort()).toEqual(['access', 'company', 'displayName', 'email', 'expiresAt', 'kind', 'mustChangePassword', 'permissions', 'roles', 'serverVersion']);
  });

  it('credenciales incorrectas: 401 con el mensaje único del servidor', async () => {
    fetchMock.mockResolvedValue(rpcError('authentication', 'Correo o contraseña incorrectos.', 401));
    await expect(gateway().login({ email: 'valentina@correo.example', password: 'mala' })).rejects.toMatchObject({
      kind: 'authentication',
      status: 401,
      message: 'Correo o contraseña incorrectos.',
    });
  });

  it('registra con POST /account/register (201) y sin enviar rol', async () => {
    fetchMock.mockResolvedValue(json(SESSION_DTO, 201));
    const session = await gateway().register({ name: 'Valentina Aguirre', email: 'valentina@correo.example', phone: '+591 71234567', password: 'Clave123' });
    const { url, body } = lastRequest();
    expect(url).toBe('/api/v1/web/account/register');
    expect(body).toEqual({ name: 'Valentina Aguirre', email: 'valentina@correo.example', phone: '+591 71234567', password: 'Clave123' });
    expect(session.kind).toBe('customer');
  });

  it('correo ya registrado: 422 con el código account.email_taken', async () => {
    fetchMock.mockResolvedValue(rpcError('domain', 'Ese correo ya tiene una cuenta.', 422, { code: 'account.email_taken' }));
    await expect(gateway().register({ name: 'V', email: 'valentina@correo.example', phone: '71234567', password: 'Clave123' })).rejects.toMatchObject({
      kind: 'domain',
      code: 'account.email_taken',
    });
  });

  it('lee la sesión con GET /session; un 401 significa «sin sesión» (null), no un error', async () => {
    fetchMock.mockResolvedValueOnce(json({ ok: true, result: SESSION_DTO }));
    expect(await gateway().current()).toMatchObject({ email: 'valentina@correo.example' });
    expect(lastRequest().url).toBe('/api/v1/web/session');
    expect(lastRequest().init.method).toBe('GET');

    fetchMock.mockResolvedValueOnce(rpcError('authentication', 'La sesión venció o se cerró: vuelva a iniciar sesión.', 401));
    expect(await gateway().current()).toBeNull();
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 401 }));
    expect(await gateway().current()).toBeNull();

    fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    await expect(gateway().current()).rejects.toMatchObject({ kind: 'network' });
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 429 }));
    await expect(gateway().current()).rejects.toMatchObject({ kind: 'rate_limited' });
  });

  it('cierra la sesión con POST /session/logout (204); cerrar una sesión ya cerrada no es un error', async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));
    await expect(gateway().logout()).resolves.toBeUndefined();
    expect(lastRequest().url).toBe('/api/v1/web/session/logout');
    expect(lastRequest().init.method).toBe('POST');
    expect(lastRequest().headers[CLIENT_VERSION_HEADER]).toBe('7.0.0');
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 401 }));
    await expect(gateway().logout()).resolves.toBeUndefined();
    fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    await expect(gateway().logout()).rejects.toMatchObject({ kind: 'network' });
  });

  it('rechaza una respuesta que no es una sesión', async () => {
    fetchMock.mockResolvedValue(json({ hola: 'mundo' }));
    await expect(gateway().login({ email: 'a@b.example', password: 'x' })).rejects.toMatchObject({ kind: 'server' });
  });
});

describe('mapeo de la sesión', () => {
  it('tolera listas ausentes y deduce el tipo: cliente SOLO si su único rol es CLIENTE', () => {
    expect(toSession({ email: 'a@b.example' })).toMatchObject({ displayName: 'a@b.example', roles: [], permissions: [], kind: 'staff', mustChangePassword: false });
    expect(toSession({ email: 'a@b.example', roles: ['CLIENTE'] }).kind).toBe('customer');
    expect(toSession({ email: 'a@b.example', roles: ['CLIENTE', 'VENTAS'] }).kind).toBe('staff');
    expect(toSession({ email: 'a@b.example', roles: ['CLIENTE'], kind: 'staff' }).kind).toBe('staff');
    expect(toSession({ email: 'a@b.example', access: null }).access).toEqual({ allBranches: false, branches: [], activeBranchId: null });
    expect(() => toSession(null)).toThrow();
    expect(() => toSession({ displayName: 'Sin correo' })).toThrow();
  });
});

describe('HttpRpcGateway', () => {
  const gateway = () => new HttpRpcGateway(new WebApi());

  it('envía requestId (UUID), el nombre COMPLETO del caso de uso y el payload', async () => {
    fetchMock.mockResolvedValue(json({ ok: true, result: { name: 'Valentina', email: 'v@correo.example', phone: '', documentType: null, documentNumber: null, complement: null }, error: null, replayed: false }));
    const result = await gateway().send('GetMyAccountQuery', {});
    const { url, init, headers, body } = lastRequest();
    expect(url).toBe('/api/v1/web/rpc');
    expect(init.method).toBe('POST');
    expect(init.credentials).toBe('same-origin');
    expect(headers[CLIENT_VERSION_HEADER]).toBe('7.0.0');
    expect(body).toMatchObject({ type: 'MINV.Application.Accounts.GetMyAccountQuery', payload: {} });
    expect(isUuid((body as { requestId: string }).requestId)).toBe(true);
    expect(result.name).toBe('Valentina');
  });

  it('cada intento lleva un id nuevo; un reintento usa el MISMO id que se le pasa', async () => {
    fetchMock.mockImplementation(async () => json({ ok: true, result: true }));
    await gateway().send('ChangePasswordCommand', { currentPassword: 'a', newPassword: 'b' });
    const first = (lastRequest().body as { requestId: string }).requestId;
    await gateway().send('ChangePasswordCommand', { currentPassword: 'a', newPassword: 'b' });
    expect((lastRequest().body as { requestId: string }).requestId).not.toBe(first);

    const requestId = '4f6a0c2e-8d1b-4c3e-9a7f-5b2d1e0c9a8b';
    await gateway().send('CancelMyReservationCommand', { number: 'RES-WEB-000012' }, { requestId });
    await gateway().send('CancelMyReservationCommand', { number: 'RES-WEB-000012' }, { requestId });
    const sent = fetchMock.mock.calls.slice(-2).map(([, init]) => JSON.parse(String((init as RequestInit).body)) as { requestId: string; type: string });
    expect(sent.map((item) => item.requestId)).toEqual([requestId, requestId]);
    expect(sent[0].type).toBe('MINV.Application.Accounts.CancelMyReservationCommand');
  });

  it('`call` informa si fue una repetición idempotente', async () => {
    fetchMock.mockResolvedValue(json({ ok: true, result: RESERVATION_VIEW, error: null, replayed: true }));
    const outcome = await gateway().call('CancelMyReservationCommand', { number: 'RES-WEB-000012' }, { requestId: 'r-7' });
    expect(outcome).toMatchObject({ replayed: true, requestId: 'r-7' });
    expect(outcome.result.number).toBe('RES-WEB-000012');
  });

  it('un 401 llega como `authentication` (la sesión venció)', async () => {
    fetchMock.mockResolvedValue(rpcError('authentication', 'La sesión venció o se cerró: vuelva a iniciar sesión.', 401));
    await expect(gateway().send('GetMyReservationsQuery', {})).rejects.toMatchObject({ kind: 'authentication', status: 401 });
  });

  it('403, 404, 409, 422 y 500 llegan con su clase y el id del pedido', async () => {
    const cases: [number, string, string][] = [
      [403, 'access_denied', 'Una cuenta de cliente no puede ejecutar esta operación.'],
      [404, 'not_found', 'La reserva no existe.'],
      [409, 'concurrency', 'Otra persona cambió el dato.'],
      [422, 'domain', 'La reserva ya no está activa.'],
      [500, 'server', 'El servidor no pudo completar la operación.'],
    ];
    for (const [status, kind, message] of cases) {
      fetchMock.mockResolvedValueOnce(rpcError(kind, message, status));
      await expect(gateway().send('CancelMyReservationCommand', { number: 'X' }, { requestId: 'r-1' })).rejects.toMatchObject({ kind, status, requestId: 'r-1' });
    }
  });

  it('una operación que el contrato no conoce no viaja al servidor', async () => {
    await expect(gateway().send('BorrarTodo' as never, {} as never)).rejects.toMatchObject({ kind: 'unsupported' });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('contrato', () => {
  it('cada operación tiene su nombre completo y sus permisos', () => {
    expect(RPC_OPERATIONS.GetMyAccountQuery).toMatchObject({ type: 'MINV.Application.Accounts.GetMyAccountQuery', command: false, permissions: ['account.manage'], customer: true });
    expect(RPC_OPERATIONS.CreateMyReservationCommand.permissions).toEqual(['account.reserve']);
    expect(RPC_OPERATIONS.ChangePasswordCommand.type).toBe('MINV.Application.Iam.ChangePasswordCommand');
    expect(RPC_OPERATIONS.SelectBranchCommand.customer).toBe(false);
    for (const name of Object.values(ACCOUNT_OPERATIONS)) expect(isRpcOperation(name)).toBe(true);
    expect(rpcOperation('NoExiste')).toBeUndefined();
    expect(isRpcOperation('constructor')).toBe(false);
  });

  it('nombra los permisos y los roles en español', () => {
    expect(roleName('CLIENTE')).toBe('Cliente web');
    expect(roleName('ADMIN')).toBe('Administrador');
    expect(roleName('OTRO')).toBe('OTRO');
    expect(permissionName('account.manage')).toMatch(/Cuenta de cliente/);
    expect(permissionName('no.existe')).toBe('no.existe');
  });
});

describe('RpcAccountGateway', () => {
  const account = () => new RpcAccountGateway(new HttpRpcGateway(new WebApi()));
  const ACCOUNT_VIEW = { name: 'Valentina Aguirre', email: 'valentina@correo.example', phone: '+591 71234567', documentType: 1, documentNumber: '1234567', complement: '1A' };

  it('lee la cuenta SIN enviar ningún identificador de cliente', async () => {
    fetchMock.mockResolvedValue(json({ ok: true, result: ACCOUNT_VIEW }));
    expect(await account().account()).toEqual(ACCOUNT_VIEW);
    expect(lastRequest().body).toMatchObject({ type: 'MINV.Application.Accounts.GetMyAccountQuery', payload: {} });
  });

  it('descarta un tipo de documento desconocido y el complemento sin CI', async () => {
    fetchMock.mockResolvedValueOnce(json({ ok: true, result: { ...ACCOUNT_VIEW, documentType: 9 } }));
    expect(await account().account()).toMatchObject({ documentType: null, documentNumber: null, complement: null });
    fetchMock.mockResolvedValueOnce(json({ ok: true, result: { ...ACCOUNT_VIEW, documentType: 5, documentNumber: '1020304050' } }));
    expect(await account().account()).toMatchObject({ documentType: 5, documentNumber: '1020304050', complement: null });
  });

  it('guarda los datos; si el comando no devuelve la cuenta, la vuelve a leer', async () => {
    const update = { name: 'Valentina A.', phone: '+591 71234567', documentType: 5 as const, documentNumber: '1020304050', complement: null };
    fetchMock.mockResolvedValueOnce(json({ ok: true, result: { ...ACCOUNT_VIEW, name: 'Valentina A.' } }));
    expect((await account().updateAccount(update, { requestId: 'r-1' })).name).toBe('Valentina A.');
    expect(lastRequest().body).toEqual({ requestId: 'r-1', type: 'MINV.Application.Accounts.UpdateMyAccountCommand', payload: update });
    expect(fetchMock).toHaveBeenCalledTimes(1);

    fetchMock.mockResolvedValueOnce(json({ ok: true, result: true })).mockResolvedValueOnce(json({ ok: true, result: { ...ACCOUNT_VIEW, name: 'Releída' } }));
    expect((await account().updateAccount(update)).name).toBe('Releída');
    expect(lastRequest().body).toMatchObject({ type: 'MINV.Application.Accounts.GetMyAccountQuery' });
  });

  it('trae las reservas en el dominio (fechas, líneas de carrito sin ranura)', async () => {
    fetchMock.mockResolvedValue(json({ ok: true, result: [RESERVATION_VIEW, { ...RESERVATION_VIEW, number: 'ARM-WEB-000007', status: 'Sold', statusText: 'Vendida' }] }));
    const reservations = await account().reservations();
    expect(reservations.map((item) => [item.number, item.status])).toEqual([
      ['RES-WEB-000012', 'Reserved'],
      ['ARM-WEB-000007', 'Sold'],
    ]);
    expect(reservations[0].createdAt).toBeInstanceOf(Date);
    expect(reservations[0].lines[0]).toMatchObject({ slot: '', sku: 'CASE-COR-4000D', subtotal: 1299 });
    expect(lastRequest().body).toMatchObject({ type: 'MINV.Application.Accounts.GetMyReservationsQuery', payload: {} });
  });

  it('libera una reserva por su número; si el comando no devuelve la reserva, la busca en la lista', async () => {
    fetchMock.mockResolvedValueOnce(json({ ok: true, result: { ...RESERVATION_VIEW, status: 'Cancelled', statusText: 'Cancelada', cancelReason: 'Cancelada por el cliente' } }));
    expect(await account().cancelReservation('RES-WEB-000012', { requestId: 'r-2' })).toMatchObject({ status: 'Cancelled', cancelReason: 'Cancelada por el cliente' });
    expect(lastRequest().body).toEqual({ requestId: 'r-2', type: 'MINV.Application.Accounts.CancelMyReservationCommand', payload: { number: 'RES-WEB-000012' } });

    fetchMock.mockResolvedValueOnce(json({ ok: true, result: true })).mockResolvedValueOnce(json({ ok: true, result: [{ ...RESERVATION_VIEW, status: 'Cancelled', statusText: 'Cancelada' }] }));
    expect((await account().cancelReservation('RES-WEB-000012')).status).toBe('Cancelled');

    fetchMock.mockResolvedValueOnce(json({ ok: true, result: true })).mockResolvedValueOnce(json({ ok: true, result: [] }));
    await expect(account().cancelReservation('RES-WEB-000012')).rejects.toMatchObject({ kind: 'not_found' });
  });

  it('cambia la contraseña; la contraseña actual incorrecta llega como 401', async () => {
    fetchMock.mockResolvedValueOnce(json({ ok: true, result: true }));
    await expect(account().changePassword({ currentPassword: 'Vieja123', newPassword: 'Nueva456' })).resolves.toBeUndefined();
    expect(lastRequest().body).toMatchObject({ type: 'MINV.Application.Iam.ChangePasswordCommand', payload: { currentPassword: 'Vieja123', newPassword: 'Nueva456' } });
    fetchMock.mockResolvedValueOnce(rpcError('authentication', 'La contraseña actual no es correcta.', 401));
    await expect(account().changePassword({ currentPassword: 'mala', newPassword: 'Nueva456' })).rejects.toMatchObject({ kind: 'authentication', message: 'La contraseña actual no es correcta.' });
  });

  it('rechaza una respuesta que no tiene la forma esperada', async () => {
    fetchMock.mockResolvedValueOnce(json({ ok: true, result: { otra: 'cosa' } }));
    await expect(account().account()).rejects.toMatchObject({ kind: 'server' });
    fetchMock.mockResolvedValueOnce(json({ ok: true, result: { reservas: [] } }));
    await expect(account().reservations()).rejects.toMatchObject({ kind: 'server' });
  });
});
