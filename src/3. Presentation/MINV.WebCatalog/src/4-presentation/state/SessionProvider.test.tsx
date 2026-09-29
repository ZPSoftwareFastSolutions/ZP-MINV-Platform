// SessionProvider: carga la sesión al arrancar, expone ingresar, registrarse y salir, limpia la sesión ante un 401 de
// cualquier RPC y no guarda nada en el navegador. Los puertos se simulan: ninguna prueba toca la red.

import { act, render, screen, waitFor } from '@testing-library/react';
import { useEffect } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { Session } from '@/1-domain/auth/types';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';
import type { WebRpc } from '@/4-presentation/app/container';
import { useRpc } from '@/4-presentation/hooks/useRpc';
import { useSession } from '@/4-presentation/hooks/useSession';
import { demoUser, mockWeb, signedInWeb, webWith } from '@/test-utils';
import type { SessionApi } from './SessionContext';
import { SessionProvider } from './SessionProvider';

/** Lo que las pruebas toman del árbol de React (se guarda en un efecto, no durante el render). */
const captured: { api: SessionApi | null; rpc: WebRpc | null } = { api: null, rpc: null };

function api(): SessionApi {
  if (!captured.api) throw new Error('La sonda todavía no se dibujó.');
  return captured.api;
}

function Probe() {
  const session = useSession();
  useEffect(() => {
    captured.api = session;
  });
  return (
    <p data-testid="sonda">
      {session.status}|{session.session?.displayName ?? 'sin sesión'}|{session.session?.kind ?? '-'}|{session.ended ?? '-'}|{session.loadError?.kind ?? '-'}
    </p>
  );
}

const CUSTOMER = demoUser('customer');
const STAFF = demoUser('staff');

const SESSION: Session = {
  displayName: 'Valentina Aguirre',
  email: 'valentina@correo.example',
  roles: ['CLIENTE'],
  permissions: ['account.manage'],
  mustChangePassword: false,
  access: { allBranches: false, branches: [], activeBranchId: null },
  kind: 'customer',
  expiresAt: new Date('2026-09-29T06:00:00Z'),
  serverVersion: '7.0.0',
  company: 'Tech Zone Gaming S.R.L.',
};

const NO_RPC: WebRpc = { call: vi.fn(), send: vi.fn() };

function gatewayWith(overrides: Partial<ISessionGateway>): ISessionGateway {
  return {
    login: vi.fn().mockResolvedValue(SESSION),
    register: vi.fn().mockResolvedValue(SESSION),
    current: vi.fn().mockResolvedValue(null),
    logout: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  };
}

afterEach(() => {
  vi.restoreAllMocks();
  captured.api = null;
  captured.rpc = null;
});

describe('SessionProvider', () => {
  it('al arrancar pregunta por la sesión: primero «cargando», después la sesión del servidor', async () => {
    const current = vi.fn().mockResolvedValue(SESSION);
    render(
      <SessionProvider web={webWith({ session: gatewayWith({ current }), rpc: NO_RPC })}>
        <Probe />
      </SessionProvider>,
    );
    expect(screen.getByTestId('sonda')).toHaveTextContent('loading|sin sesión');
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent('ready|Valentina Aguirre|customer|-|-'));
    expect(current).toHaveBeenCalledTimes(1);
    expect(api().can('account.manage')).toBe(true);
    expect(api().can('sales.view')).toBe(false);
  });

  it('sin sesión queda listo y sin nadie ingresado (no es un error)', async () => {
    render(
      <SessionProvider web={mockWeb().services}>
        <Probe />
      </SessionProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent('ready|sin sesión|-|-|-'));
    expect(api().can('account.manage')).toBe(false);
    expect(api().mode).toBe('mock');
    expect(api().demoUsers).toHaveLength(2);
  });

  it('acepta la promesa de los servicios (el modo mock se carga en un fragmento aparte)', async () => {
    const web = await signedInWeb('staff');
    render(
      <SessionProvider web={Promise.resolve(web.services)}>
        <Probe />
      </SessionProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent(`ready|${STAFF.name}|staff`));
  });

  it('ingresar deja la sesión; con credenciales incorrectas no hay sesión y el error llega a la pantalla', async () => {
    render(
      <SessionProvider web={mockWeb().services}>
        <Probe />
      </SessionProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent('ready'));

    let failure: unknown;
    await act(async () => {
      failure = await api().login({ email: CUSTOMER.email, password: 'Incorrecta1' }).catch((error: unknown) => error);
    });
    expect(failure).toMatchObject({ kind: 'authentication', message: 'Correo o contraseña incorrectos.' });
    expect(screen.getByTestId('sonda')).toHaveTextContent('ready|sin sesión|-|-|-');

    await act(async () => {
      await api().login({ email: CUSTOMER.email, password: CUSTOMER.password });
    });
    expect(screen.getByTestId('sonda')).toHaveTextContent(`ready|${CUSTOMER.name}|customer|-|-`);
  });

  it('registrarse deja iniciada una sesión de cliente', async () => {
    render(
      <SessionProvider web={mockWeb().services}>
        <Probe />
      </SessionProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent('ready'));
    await act(async () => {
      await api().register({ name: 'Nueva Persona', email: 'nueva@correo.example', phone: '71112233', password: 'Clave123' });
    });
    expect(screen.getByTestId('sonda')).toHaveTextContent('ready|Nueva Persona|customer');
  });

  it('salir limpia la sesión y avisa que la cerró la persona', async () => {
    const web = await signedInWeb('customer');
    render(
      <SessionProvider web={web.services}>
        <Probe />
      </SessionProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent(CUSTOMER.name));
    await act(async () => {
      await api().logout();
    });
    expect(screen.getByTestId('sonda')).toHaveTextContent('ready|sin sesión|-|logout|-');
    expect(await web.backend.session.current()).toBeNull();
    act(() => api().acknowledgeEnd());
    expect(screen.getByTestId('sonda')).toHaveTextContent('ready|sin sesión|-|-|-');
  });

  it('si el servidor no responde al cerrar, la sesión NO se da por cerrada', async () => {
    const logout = vi.fn().mockRejectedValue(new WebApiError({ kind: 'network', message: 'sin red' }));
    render(
      <SessionProvider web={webWith({ session: gatewayWith({ current: vi.fn().mockResolvedValue(SESSION), logout }), rpc: NO_RPC })}>
        <Probe />
      </SessionProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent('Valentina Aguirre'));
    await act(async () => {
      await expect(api().logout()).rejects.toMatchObject({ kind: 'network' });
    });
    expect(screen.getByTestId('sonda')).toHaveTextContent('ready|Valentina Aguirre|customer|-|-');
  });

  it('un 401 en CUALQUIER pedido RPC limpia la sesión y la marca como vencida', async () => {
    const web = await signedInWeb('customer');
    function RpcProbe() {
      const { status, session } = useSession();
      return status === 'ready' && session ? <Reader /> : null;
    }
    function Reader() {
      const rpc = useRpc();
      useEffect(() => {
        captured.rpc = rpc;
      });
      return null;
    }
    render(
      <SessionProvider web={web.services}>
        <Probe />
        <RpcProbe />
      </SessionProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent(CUSTOMER.name));
    await waitFor(() => expect(captured.rpc).not.toBeNull());
    const rpc = captured.rpc!;
    await expect(rpc.send('GetMyReservationsQuery', {})).resolves.toHaveLength(3);

    web.backend.expire();
    await act(async () => {
      await expect(rpc.send('GetMyReservationsQuery', {})).rejects.toMatchObject({ kind: 'authentication', status: 401 });
    });
    expect(screen.getByTestId('sonda')).toHaveTextContent('ready|sin sesión|-|expired|-');
  });

  it('la contraseña actual incorrecta (401 del cambio de contraseña) NO cierra la sesión', async () => {
    const web = await signedInWeb('customer');
    render(
      <SessionProvider web={web.services}>
        <Probe />
      </SessionProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent(CUSTOMER.name));
    await act(async () => {
      await expect(web.services.rpc.send('ChangePasswordCommand', { currentPassword: 'Equivocada1', newPassword: 'Nueva4567' })).rejects.toMatchObject({ kind: 'authentication' });
    });
    expect(screen.getByTestId('sonda')).toHaveTextContent(`ready|${CUSTOMER.name}|customer|-|-`);
  });

  it('si no se puede preguntar por la sesión queda el error y «refresh» se recupera', async () => {
    const current = vi.fn().mockRejectedValueOnce(new WebApiError({ kind: 'network', message: 'sin red' })).mockResolvedValue(SESSION);
    render(
      <SessionProvider web={webWith({ session: gatewayWith({ current }), rpc: NO_RPC })}>
        <Probe />
      </SessionProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent('ready|sin sesión|-|-|network'));
    await act(async () => {
      await expect(api().refresh()).resolves.toMatchObject({ email: 'valentina@correo.example' });
    });
    expect(screen.getByTestId('sonda')).toHaveTextContent('ready|Valentina Aguirre|customer|-|-');
  });

  it('una falla pasajera al comprobar no tira una sesión que está funcionando', async () => {
    const current = vi.fn().mockResolvedValueOnce(SESSION).mockRejectedValue(new WebApiError({ kind: 'network', message: 'sin red' }));
    render(
      <SessionProvider web={webWith({ session: gatewayWith({ current }), rpc: NO_RPC })}>
        <Probe />
      </SessionProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent('Valentina Aguirre'));
    await act(async () => {
      await api().refresh();
    });
    expect(screen.getByTestId('sonda')).toHaveTextContent('ready|Valentina Aguirre|customer|-|-');
  });

  it('al volver a la pestaña vuelve a comprobar: si se cerró en otra pestaña, aquí también', async () => {
    const web = await signedInWeb('customer');
    render(
      <SessionProvider web={web.services} recheckMs={0}>
        <Probe />
      </SessionProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent(CUSTOMER.name));
    web.backend.expire();
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent('ready|sin sesión|-|expired|-'));
  });

  it('NADA de la sesión se guarda en el navegador (ni almacenamiento ni cookies propias)', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    const cookieBefore = document.cookie;
    render(
      <SessionProvider web={mockWeb().services}>
        <Probe />
      </SessionProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('sonda')).toHaveTextContent('ready'));
    await act(async () => {
      await api().login({ email: CUSTOMER.email, password: CUSTOMER.password });
    });
    await act(async () => {
      await api().logout();
    });
    expect(setItem).not.toHaveBeenCalled();
    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
    expect(document.cookie).toBe(cookieBefore);
  });

  it('useSession fuera del proveedor avisa con un error claro', () => {
    const silence = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    expect(() => render(<Probe />)).toThrow(/dentro de <SessionProvider>/);
    silence.mockRestore();
  });
});
