// «Ingresar»: con credenciales incorrectas NO se sale de la pantalla y se ve el mensaje único del servidor; aviso de
// cuenta bloqueada; el botón se deshabilita mientras envía; mostrar u ocultar la contraseña; enlace a registrarse.

import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { Session } from '@/1-domain/auth/types';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';
import { demoUser, mockWeb, renderRoutes, webWith } from '@/test-utils';

const CUSTOMER = demoUser('customer');

function fill(email: string, password: string) {
  fireEvent.change(screen.getByLabelText(/^Correo/), { target: { value: email } });
  fireEvent.change(screen.getByLabelText(/^Contraseña/), { target: { value: password } });
}

async function openLogin(options: Parameters<typeof renderRoutes>[0] = {}) {
  const app = await renderRoutes({ route: '/ingresar', ...options });
  await screen.findByRole('heading', { level: 1, name: 'Ingresar' });
  return app;
}

function gatewayThatFails(error: WebApiError): ISessionGateway & { login: ReturnType<typeof vi.fn> } {
  return { login: vi.fn().mockRejectedValue(error), register: vi.fn(), current: vi.fn().mockResolvedValue(null), logout: vi.fn() };
}

describe('LoginPage', () => {
  it('muestra el formulario con sus etiquetas, el enlace a registrarse y el botón «Ingresar»', async () => {
    await openLogin();
    expect(screen.getByLabelText(/^Correo/)).toHaveAttribute('type', 'email');
    expect(screen.getByLabelText(/^Correo/)).toHaveAttribute('autocomplete', 'username');
    expect(screen.getByLabelText(/^Contraseña/)).toHaveAttribute('type', 'password');
    expect(screen.getByLabelText(/^Contraseña/)).toHaveAttribute('autocomplete', 'current-password');
    expect(screen.getByRole('button', { name: 'Ingresar' })).toBeEnabled();
    expect(screen.getByRole('link', { name: 'Registrate' })).toHaveAttribute('href', '/registrarse');
  });

  it('el enlace a registrarse conserva un `volver` interno y descarta uno malicioso', async () => {
    const first = await openLogin({ route: '/ingresar?volver=%2Fcarrito' });
    expect(screen.getByRole('link', { name: 'Registrate' })).toHaveAttribute('href', '/registrarse?volver=%2Fcarrito');
    first.unmount();
    await openLogin({ route: '/ingresar?volver=%2F%2Fevil.example' });
    expect(screen.getByRole('link', { name: 'Registrate' })).toHaveAttribute('href', '/registrarse');
  });

  it('valida antes de enviar: sin correo ni contraseña no viaja al servidor', async () => {
    const gateway = gatewayThatFails(new WebApiError({ kind: 'server', message: 'x' }));
    await openLogin({ web: webWith({ session: gateway, rpc: { call: vi.fn(), send: vi.fn() } }) });
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
    expect(await screen.findByText('Indicá tu correo.')).toBeInTheDocument();
    expect(screen.getByText('Indicá tu contraseña.')).toBeInTheDocument();
    expect(screen.getByLabelText(/^Correo/)).toHaveAttribute('aria-invalid', 'true');
    fill('sin-arroba', 'x');
    expect(await screen.findByText('Revisá el correo: debe tener la forma nombre@dominio.')).toBeInTheDocument();
    expect(gateway.login).not.toHaveBeenCalled();
  });

  it('con credenciales incorrectas NO navega: sigue en /ingresar con el mensaje único del servidor', async () => {
    const web = mockWeb();
    const app = await openLogin({ web: web.services, route: '/ingresar?volver=%2Fmi-cuenta%2Fdatos' });
    fill(CUSTOMER.email, 'Incorrecta1');
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('No pudimos ingresar');
    expect(alert).toHaveTextContent('Correo o contraseña incorrectos.');
    expect(app.location()).toBe('/ingresar?volver=%2Fmi-cuenta%2Fdatos');
    expect(screen.getByRole('heading', { level: 1, name: 'Ingresar' })).toBeInTheDocument();
    expect(await web.backend.session.current()).toBeNull();
    // No revela cuál de los dos datos falló: ningún campo queda marcado y la contraseña se borra.
    expect(screen.getByLabelText(/^Correo/)).not.toHaveAttribute('aria-invalid');
    expect(screen.getByLabelText(/^Contraseña/)).not.toHaveAttribute('aria-invalid');
    expect(screen.getByLabelText(/^Correo/)).toHaveValue(CUSTOMER.email);
    expect(screen.getByLabelText(/^Contraseña/)).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Ingresar' })).toBeEnabled();
  });

  it('un correo que no existe muestra EXACTAMENTE el mismo mensaje', async () => {
    const app = await openLogin();
    fill('nadie@correo.example', 'Clave1234');
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Correo o contraseña incorrectos.');
    expect(app.location()).toBe('/ingresar');
  });

  it('después de fallar, con las credenciales correctas sí ingresa', async () => {
    const app = await openLogin();
    fill(CUSTOMER.email, 'Incorrecta1');
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
    await screen.findByRole('alert');
    fill(CUSTOMER.email, CUSTOMER.password);
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
    expect(await screen.findByRole('heading', { level: 1, name: `Hola, ${CUSTOMER.name}` })).toBeInTheDocument();
    expect(app.location()).toBe('/mi-cuenta');
  });

  it('avisa cuando la cuenta está bloqueada por intentos fallidos', async () => {
    const app = await openLogin();
    for (let attempt = 1; attempt <= 5; attempt += 1) {
      fill(CUSTOMER.email, `Incorrecta${attempt}`);
      fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
      await waitFor(() => expect(screen.getByLabelText(/^Contraseña/)).toHaveValue(''));
    }
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Cuenta bloqueada por intentos fallidos');
    expect(alert).toHaveTextContent('espere 15 minutos');
    expect(app.location()).toBe('/ingresar');
  });

  it('deshabilita el botón mientras envía y no envía dos veces', async () => {
    let finish: (session: Session) => void = () => undefined;
    const login = vi.fn().mockReturnValue(new Promise<Session>((resolve) => (finish = resolve)));
    const gateway: ISessionGateway = { login, register: vi.fn(), current: vi.fn().mockResolvedValue(null), logout: vi.fn() };
    await openLogin({ web: webWith({ session: gateway, rpc: { call: vi.fn(), send: vi.fn() } }) });
    fill('valentina@correo.example', 'Clave1234');
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));

    const busy = await screen.findByRole('button', { name: 'Ingresando…' });
    expect(busy).toBeDisabled();
    expect(busy).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByLabelText(/^Correo/)).toBeDisabled();
    fireEvent.click(busy);
    fireEvent.submit(busy.closest('form')!);
    expect(login).toHaveBeenCalledTimes(1);
    expect(login).toHaveBeenCalledWith({ email: 'valentina@correo.example', password: 'Clave1234' });

    await act(async () => {
      finish({
        displayName: 'Valentina Aguirre',
        email: 'valentina@correo.example',
        roles: ['CLIENTE'],
        permissions: [],
        mustChangePassword: false,
        access: { allBranches: false, branches: [], activeBranchId: null },
        kind: 'customer',
        expiresAt: new Date(),
        serverVersion: '7.0.0',
        company: 'Tech Zone Gaming S.R.L.',
      });
    });
  });

  it('muestra un texto propio cuando hay demasiados intentos (429) o no hay conexión', async () => {
    const limited = gatewayThatFails(new WebApiError({ kind: 'rate_limited', status: 429, message: 'Se superó el límite de solicitudes.' }));
    const first = await openLogin({ web: webWith({ session: limited, rpc: { call: vi.fn(), send: vi.fn() } }) });
    fill('valentina@correo.example', 'Clave1234');
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/Demasiados intentos.*Esperá un minuto/);
    first.unmount();

    const offline = gatewayThatFails(new WebApiError({ kind: 'network', message: 'No hubo respuesta del servidor.' }));
    const second = await openLogin({ web: webWith({ session: offline, rpc: { call: vi.fn(), send: vi.fn() } }) });
    fill('valentina@correo.example', 'Clave1234');
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/Sin conexión con el servidor.*Revisá tu conexión/);
    expect(second.location()).toBe('/ingresar');
  });

  it('muestra y oculta la contraseña', async () => {
    await openLogin();
    const password = screen.getByLabelText(/^Contraseña/);
    fill(CUSTOMER.email, 'Clave1234');
    const show = screen.getByRole('button', { name: 'Mostrar la contraseña' });
    expect(show).toHaveAttribute('aria-pressed', 'false');
    fireEvent.click(show);
    expect(password).toHaveAttribute('type', 'text');
    expect(password).toHaveValue('Clave1234');
    const hide = screen.getByRole('button', { name: 'Ocultar la contraseña' });
    expect(hide).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(hide);
    expect(password).toHaveAttribute('type', 'password');
  });

  it('la contraseña nunca viaja en la URL', async () => {
    const app = await openLogin();
    fill(CUSTOMER.email, 'Incorrecta1');
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
    await screen.findByRole('alert');
    expect(app.location()).not.toContain('Incorrecta1');
    expect(app.location()).not.toContain(encodeURIComponent(CUSTOMER.email));
  });

  it('en el modo de demostración ofrece los usuarios de muestra', async () => {
    await openLogin();
    expect(screen.getByRole('heading', { name: 'Demostración sin servidor' })).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole('button', { name: 'Usar estos datos' })[1]);
    expect(screen.getByLabelText(/^Correo/)).toHaveValue(CUSTOMER.email);
    expect(screen.getByLabelText(/^Contraseña/)).toHaveValue(CUSTOMER.password);
  });

  it('contra el servidor no muestra usuarios de muestra', async () => {
    const gateway = gatewayThatFails(new WebApiError({ kind: 'server', message: 'x' }));
    await openLogin({ web: webWith({ session: gateway, rpc: { call: vi.fn(), send: vi.fn() } }) });
    expect(screen.queryByText('Demostración sin servidor')).not.toBeInTheDocument();
  });
});
