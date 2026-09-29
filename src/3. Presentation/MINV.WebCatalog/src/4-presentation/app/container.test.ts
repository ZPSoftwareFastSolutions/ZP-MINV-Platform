import { afterEach, describe, expect, it, vi } from 'vitest';
import { createSources, createWebServices } from './container';

// La base de la API se resuelve UNA sola vez: «/» (tienda pública detrás del nginx del catálogo) debe quedar como mismo
// origen y no volver al gateway local por una segunda resolución.
describe('createSources', () => {
  it('«/» usa el mismo origen (rutas relativas /storefront/v1)', async () => {
    const sources = await createSources('/');
    expect(sources.mode).toBe('api');
    expect(sources.apiUrl).toBe('');
  });

  it('vacío usa el gateway local; sin argumento manda VITE_API_URL (en las pruebas, el mock)', async () => {
    expect((await createSources('')).apiUrl).toBe('http://localhost:5090');
    expect((await createSources()).mode).toBe('mock');
  });

  it('una URL propia se respeta sin la barra final', async () => {
    expect((await createSources('https://api.tienda.example/')).apiUrl).toBe('https://api.tienda.example');
  });
});

// V7 · La sesión web: en memoria con «mock» (dos usuarios de muestra) y por `/api/v1/web` en cualquier otro caso.
describe('createWebServices', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('con «mock» arma la sesión y el RPC en memoria, con un usuario del personal y un cliente', async () => {
    const web = await createWebServices('mock');
    expect(web.mode).toBe('mock');
    expect(web.demoUsers.map((user) => user.kind).sort()).toEqual(['customer', 'staff']);
    expect(await web.session.current()).toBeNull();

    const customer = web.demoUsers.find((user) => user.kind === 'customer')!;
    const session = await web.session.login({ email: customer.email, password: customer.password });
    expect(session.kind).toBe('customer');
    expect(await web.account.load()).toMatchObject({ email: customer.email });
    expect((await web.account.reservations()).length).toBeGreaterThan(0);
    expect(await web.rpc.send('GetMyAccountQuery', {})).toMatchObject({ name: customer.name });
  });

  it('sin argumento manda VITE_API_URL (en las pruebas, el mock)', async () => {
    expect((await createWebServices()).mode).toBe('mock');
  });

  it('contra el servidor usa el mismo origen y no trae usuarios de muestra', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);
    const web = await createWebServices('http://localhost:5090');
    expect(web.mode).toBe('api');
    expect(web.demoUsers).toEqual([]);
    expect(await web.session.current()).toBeNull();
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    // La base de la tienda (VITE_API_URL) NO se usa para la sesión: siempre la ruta relativa del mismo origen.
    expect(url).toBe('/api/v1/web/session');
    expect(init.credentials).toBe('same-origin');
  });

  it('avisa a quien escucha cuando un pedido descubre que la sesión venció, y deja de avisar al darse de baja', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ ok: false, error: { kind: 'authentication', message: 'La sesión venció o se cerró: vuelva a iniciar sesión.' } }), { status: 401, headers: { 'Content-Type': 'application/json' } }));
    vi.stubGlobal('fetch', fetchMock);
    const web = await createWebServices('/');
    const listener = vi.fn();
    const stop = web.onSessionExpired(listener);
    await expect(web.rpc.send('GetMyReservationsQuery', {})).rejects.toMatchObject({ kind: 'authentication' });
    expect(listener).toHaveBeenCalledTimes(1);
    stop();
    await expect(web.account.load()).rejects.toMatchObject({ kind: 'authentication' });
    expect(listener).toHaveBeenCalledTimes(1);
  });
});
