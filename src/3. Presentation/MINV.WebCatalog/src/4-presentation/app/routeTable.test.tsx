// Tabla de rutas REAL con sus guardas: `/panel/*` exige sesión del personal, `/mi-cuenta/*` sesión de cliente, sin
// sesión se va a `/ingresar?volver=…`, un `volver` malicioso se ignora y `mustChangePassword` obliga a cambiar la
// contraseña. Sesión en memoria: ninguna prueba toca la red.
//
// V7 · W3b: el panel ya no es el punto de montaje provisional («Panel en construcción») sino el esqueleto real
// (`data-testid="panel-esqueleto"`) con el tablero «Inicio» en `/panel`. Las pruebas comprueban lo mismo que antes
// (quién entra al panel y a dónde lo lleva cada guarda) buscando el esqueleto en lugar del aviso provisional.

import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';
import { demoUser, mockWeb, preloadPanel, renderRoutes, signedInWeb, webWith } from '@/test-utils';
import type { WebRpc } from './container';
import { ROUTES } from './routes';

const CUSTOMER = demoUser('customer');
const STAFF = demoUser('staff');

// El panel real es un fragmento grande: se descarga una vez para que su primera carga no se coma la espera de las búsquedas.
beforeAll(preloadPanel, 30_000);

async function signIn(email: string, password: string) {
  fireEvent.change(await screen.findByLabelText(/^Correo/), { target: { value: email } });
  fireEvent.change(screen.getByLabelText(/^Contraseña/), { target: { value: password } });
  fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
}

describe('guardas de ruta', () => {
  it('sin sesión, /panel redirige a ingresar conservando a dónde iba', async () => {
    const app = await renderRoutes({ route: '/panel/ventas?estado=abierta' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Ingresar' })).toBeInTheDocument();
    expect(app.location()).toBe('/ingresar?volver=%2Fpanel%2Fventas%3Festado%3Dabierta');
    expect(screen.queryByTestId('panel-esqueleto')).not.toBeInTheDocument();
  });

  it('sin sesión, /mi-cuenta redirige a ingresar conservando la sección', async () => {
    const app = await renderRoutes({ route: '/mi-cuenta/datos' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Ingresar' })).toBeInTheDocument();
    expect(app.location()).toBe('/ingresar?volver=%2Fmi-cuenta%2Fdatos');
  });

  it('el personal entra al panel (esqueleto y tablero) y a sus rutas internas', async () => {
    const web = await signedInWeb('staff');
    const app = await renderRoutes({ web: web.services, route: '/panel' });
    expect(await screen.findByTestId('panel-esqueleto')).toBeInTheDocument();
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent(`Hola, ${STAFF.name}`);
    expect(app.location()).toBe('/panel');
    await act(async () => {
      await app.router.navigate('/panel/ventas/caja');
    });
    // Una ruta interna sin módulo todavía: sigue dentro del panel (con su aviso), sin salir de la dirección.
    expect(await screen.findByTestId('pantalla-no-encontrada')).toBeInTheDocument();
    expect(screen.getByTestId('panel-esqueleto')).toBeInTheDocument();
    expect(app.location()).toBe('/panel/ventas/caja');
  });

  it('un cliente NO entra al panel: va a su cuenta', async () => {
    const web = await signedInWeb('customer');
    const app = await renderRoutes({ web: web.services, route: '/panel/usuarios' });
    expect(await screen.findByRole('heading', { level: 1, name: `Hola, ${CUSTOMER.name}` })).toBeInTheDocument();
    expect(app.location()).toBe('/mi-cuenta');
    expect(screen.queryByTestId('panel-esqueleto')).not.toBeInTheDocument();
  });

  it('el personal NO entra a «Mi cuenta»: va al panel', async () => {
    const web = await signedInWeb('staff');
    const app = await renderRoutes({ web: web.services, route: '/mi-cuenta/reservas' });
    expect(await screen.findByTestId('panel-esqueleto')).toBeInTheDocument();
    expect(app.location()).toBe('/panel');
  });

  it('el cliente entra a «Mi cuenta» y ve sus reservas', async () => {
    const web = await signedInWeb('customer');
    const app = await renderRoutes({ web: web.services, route: '/mi-cuenta' });
    expect(await screen.findByRole('tab', { name: /Mis reservas/, selected: true })).toBeInTheDocument();
    expect(await screen.findAllByTestId('reserva')).toHaveLength(3);
    expect(app.location()).toBe('/mi-cuenta');
  });

  it('mientras se comprueba la sesión la ruta protegida espera (no redirige ni muestra datos)', async () => {
    let release: (value: null) => void = () => undefined;
    const current = vi.fn().mockReturnValue(new Promise<null>((resolve) => (release = resolve)));
    const session: ISessionGateway = { login: vi.fn(), register: vi.fn(), current, logout: vi.fn() };
    const rpc: WebRpc = { call: vi.fn(), send: vi.fn() };
    const app = await renderRoutes({ web: webWith({ session, rpc }), route: '/panel' });
    expect(await screen.findByTestId('sesion-cargando')).toBeInTheDocument();
    expect(app.location()).toBe('/panel');
    await act(async () => release(null));
    await waitFor(() => expect(app.location()).toBe('/ingresar?volver=%2Fpanel'));
  });

  it('si no se puede comprobar la sesión muestra el error con «Reintentar»', async () => {
    const current = vi.fn().mockRejectedValueOnce(new WebApiError({ kind: 'network', message: 'sin red' })).mockResolvedValue(null);
    const session: ISessionGateway = { login: vi.fn(), register: vi.fn(), current, logout: vi.fn() };
    const app = await renderRoutes({ web: webWith({ session, rpc: { call: vi.fn(), send: vi.fn() } }), route: '/mi-cuenta' });
    const error = await screen.findByTestId('estado-error');
    expect(error).toHaveTextContent('No se pudo comprobar la sesión');
    expect(app.location()).toBe('/mi-cuenta');
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await waitFor(() => expect(app.location()).toBe('/ingresar?volver=%2Fmi-cuenta'));
  });
});

describe('después de ingresar', () => {
  it('el cliente vuelve a donde iba (`volver` interno)', async () => {
    const app = await renderRoutes({ route: '/mi-cuenta/datos' });
    await signIn(CUSTOMER.email, CUSTOMER.password);
    expect(await screen.findByRole('tab', { name: /Mis datos/, selected: true })).toBeInTheDocument();
    expect(app.location()).toBe('/mi-cuenta/datos');
  });

  it('sin `volver`, el personal va al panel y el cliente a su cuenta', async () => {
    const staff = await renderRoutes({ route: ROUTES.login });
    await signIn(STAFF.email, STAFF.password);
    expect(await screen.findByTestId('panel-esqueleto')).toBeInTheDocument();
    expect(staff.location()).toBe('/panel');
    staff.unmount();

    const customer = await renderRoutes({ route: ROUTES.login });
    await signIn(CUSTOMER.email, CUSTOMER.password);
    expect(await screen.findByRole('heading', { level: 1, name: `Hola, ${CUSTOMER.name}` })).toBeInTheDocument();
    expect(customer.location()).toBe('/mi-cuenta');
  });

  it.each([
    ['//evil.example', '%2F%2Fevil.example'],
    ['https://evil.example/panel', 'https%3A%2F%2Fevil.example%2Fpanel'],
    ['/\\evil.example', '%2F%5Cevil.example'],
    ['javascript:alert(1)', 'javascript%3Aalert(1)'],
  ])('un `volver` malicioso (%s) se ignora: va a su inicio', async (_value, encoded) => {
    const before = window.location.href;
    const app = await renderRoutes({ route: `/ingresar?volver=${encoded}` });
    await signIn(CUSTOMER.email, CUSTOMER.password);
    expect(await screen.findByRole('heading', { level: 1, name: `Hola, ${CUSTOMER.name}` })).toBeInTheDocument();
    expect(app.location()).toBe('/mi-cuenta');
    // La página no salió del sitio.
    expect(window.location.href).toBe(before);
  });

  it('un `volver` de otro tipo de sesión se ignora (un cliente no va al panel)', async () => {
    const app = await renderRoutes({ route: '/ingresar?volver=%2Fpanel%2Fusuarios' });
    await signIn(CUSTOMER.email, CUSTOMER.password);
    expect(await screen.findByRole('heading', { level: 1, name: `Hola, ${CUSTOMER.name}` })).toBeInTheDocument();
    expect(app.location()).toBe('/mi-cuenta');
  });

  it('quien ya ingresó y abre /ingresar va directo a su inicio', async () => {
    const web = await signedInWeb('staff');
    const app = await renderRoutes({ web: web.services, route: ROUTES.login });
    expect(await screen.findByTestId('panel-esqueleto')).toBeInTheDocument();
    expect(app.location()).toBe('/panel');
  });
});

describe('cambio de contraseña obligatorio', () => {
  it('toda ruta protegida lleva a cambiar la contraseña hasta cambiarla, y después sigue a donde iba', async () => {
    const web = await signedInWeb('staff');
    web.backend.requirePasswordChange(STAFF.email);
    const app = await renderRoutes({ web: web.services, route: '/panel/ventas' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Cambiar contraseña' })).toBeInTheDocument();
    expect(app.location()).toBe('/cambiar-contrasena?volver=%2Fpanel%2Fventas');
    expect(screen.getByRole('alert')).toHaveTextContent('Debe cambiar su contraseña');

    // Intentar ir a otra ruta protegida vuelve a la misma pantalla.
    await act(async () => {
      await app.router.navigate('/panel');
    });
    await waitFor(() => expect(app.location()).toBe('/cambiar-contrasena?volver=%2Fpanel'));
    expect(screen.queryByTestId('panel-esqueleto')).not.toBeInTheDocument();

    fireEvent.change(await screen.findByLabelText(/^Contraseña actual/), { target: { value: STAFF.password } });
    fireEvent.change(screen.getByLabelText(/^Nueva contraseña/), { target: { value: 'Nueva4567' } });
    fireEvent.change(screen.getByLabelText(/^Repita la nueva contraseña/), { target: { value: 'Nueva4567' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cambiar contraseña' }));

    expect(await screen.findByTestId('panel-esqueleto')).toBeInTheDocument();
    expect(app.location()).toBe('/panel');
  });

  it('al ingresar con el cambio pendiente, el cliente pasa primero por esa pantalla', async () => {
    const web = mockWeb();
    web.backend.requirePasswordChange(CUSTOMER.email);
    const app = await renderRoutes({ web: web.services, route: '/mi-cuenta/reservas' });
    await signIn(CUSTOMER.email, CUSTOMER.password);
    expect(await screen.findByRole('heading', { level: 1, name: 'Cambiar contraseña' })).toBeInTheDocument();
    expect(app.location()).toBe('/cambiar-contrasena?volver=%2Fmi-cuenta%2Freservas');
    expect(screen.getByRole('alert')).toHaveTextContent('Tenés que cambiar tu contraseña');
  });

  it('sin sesión, /cambiar-contrasena pide ingresar', async () => {
    const app = await renderRoutes({ route: '/cambiar-contrasena' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Ingresar' })).toBeInTheDocument();
    expect(app.location()).toBe('/ingresar');
  });
});

describe('fin de la sesión', () => {
  it('si la sesión vence en una ruta protegida, va a ingresar con el aviso y conservando a dónde iba', async () => {
    const web = await signedInWeb('customer');
    const app = await renderRoutes({ web: web.services, route: '/mi-cuenta/reservas' });
    expect(await screen.findAllByTestId('reserva')).toHaveLength(3);

    web.backend.expire();
    fireEvent.click(screen.getByRole('button', { name: 'Actualizar' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Ingresar' })).toBeInTheDocument();
    expect(app.location()).toBe('/ingresar?volver=%2Fmi-cuenta%2Freservas');
    expect(screen.getByText('Tu sesión venció')).toBeInTheDocument();

    await signIn(CUSTOMER.email, CUSTOMER.password);
    expect(await screen.findAllByTestId('reserva')).toHaveLength(3);
    expect(app.location()).toBe('/mi-cuenta/reservas');
  });

  it('si un pedido descubre la sesión vencida desde una página pública, también va a ingresar', async () => {
    const web = await signedInWeb('customer');
    const app = await renderRoutes({ web: web.services, route: '/carrito' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Carrito' })).toBeInTheDocument();
    web.backend.expire();
    await act(async () => {
      await web.services.rpc.send('GetMyAccountQuery', {}).catch(() => undefined);
    });
    await waitFor(() => expect(app.location()).toBe('/ingresar?volver=%2Fcarrito'));
    expect(await screen.findByText('Tu sesión venció')).toBeInTheDocument();
  });

  it('cerrar sesión desde una ruta protegida vuelve a la tienda (no a ingresar)', async () => {
    const web = await signedInWeb('staff');
    const app = await renderRoutes({ web: web.services, route: '/panel' });
    // En el panel real, «Cerrar sesión» está en el menú del usuario de la barra superior.
    fireEvent.click(await screen.findByRole('button', { name: /^Cuenta de / }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Cerrar sesión' }));
    await waitFor(() => expect(app.location()).toBe('/'));
    expect(await web.backend.session.current()).toBeNull();
    // Después, entrar de nuevo a una ruta protegida pide ingresar como siempre.
    await act(async () => {
      await app.router.navigate('/panel');
    });
    await waitFor(() => expect(app.location()).toBe('/ingresar?volver=%2Fpanel'));
  });
});

describe('rutas nuevas con carga diferida', () => {
  it.each([
    ['/registrarse', 'Crear cuenta'],
    ['/carrito', 'Carrito'],
    ['/reservar', 'Reservar'],
  ])('%s dibuja «%s» dentro de la tienda', async (route, heading) => {
    const app = await renderRoutes({ route });
    expect(await screen.findByRole('heading', { level: 1, name: heading })).toBeInTheDocument();
    expect(app.location()).toBe(route);
    // Dentro de la estructura de la tienda (con su cabecera).
    expect(screen.getByRole('button', { name: 'Abrir menú' })).toBeInTheDocument();
  });

  it('una ruta que no existe sigue mostrando la página no encontrada', async () => {
    await renderRoutes({ route: '/no-existe' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Esta página no existe' })).toBeInTheDocument();
  });
});
