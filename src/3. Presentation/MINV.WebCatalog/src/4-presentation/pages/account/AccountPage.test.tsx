// «Mi cuenta»: pestañas por URL, «Mis reservas» (lista, filtro por estado, detalle, liberar con confirmación, estados
// de carga, vacío y error con «Reintentar»), «Mis datos» y «Cambiar contraseña». Sesión y RPC en memoria.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { Session } from '@/1-domain/auth/types';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';
import type { WebRpc } from '@/4-presentation/app/container';
import { demoUser, renderRoutes, signedInWeb, webWith, type MockWeb } from '@/test-utils';

const CUSTOMER = demoUser('customer');

const SESSION: Session = {
  displayName: 'Valentina Aguirre',
  email: 'valentina@correo.example',
  roles: ['CLIENTE'],
  permissions: ['account.manage', 'account.reserve'],
  mustChangePassword: false,
  access: { allBranches: false, branches: [], activeBranchId: null },
  kind: 'customer',
  expiresAt: new Date('2026-09-29T06:00:00Z'),
  serverVersion: '7.0.0',
  company: 'Tech Zone Gaming S.R.L.',
};

function signedGateway(): ISessionGateway {
  return { login: vi.fn(), register: vi.fn(), current: vi.fn().mockResolvedValue(SESSION), logout: vi.fn() };
}

async function openAccount(route = '/mi-cuenta', web?: MockWeb) {
  const backend = web ?? (await signedInWeb('customer'));
  const app = await renderRoutes({ web: backend.services, route });
  await screen.findByRole('heading', { level: 1, name: `Hola, ${CUSTOMER.name}` });
  return { app, web: backend };
}

describe('AccountPage · pestañas', () => {
  it('tiene tres secciones y la pestaña activa sale de la URL', async () => {
    const { app } = await openAccount('/mi-cuenta');
    const tabs = within(screen.getByRole('tablist', { name: 'Secciones de mi cuenta' })).getAllByRole('tab');
    expect(tabs.map((tab) => tab.textContent)).toEqual(['ReservasMis reservas', 'DatosMis datos', 'ContraseñaCambiar contraseña']);
    expect(screen.getByRole('tab', { name: /Mis reservas/ })).toHaveAttribute('aria-selected', 'true');

    fireEvent.click(screen.getByRole('tab', { name: /Mis datos/ }));
    await waitFor(() => expect(app.location()).toBe('/mi-cuenta/datos'));
    expect(screen.getByRole('tab', { name: /Mis datos/ })).toHaveAttribute('aria-selected', 'true');
    expect(await screen.findByRole('heading', { level: 2, name: 'Mis datos' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: /Cambiar contraseña/ }));
    await waitFor(() => expect(app.location()).toBe('/mi-cuenta/contrasena'));
    expect(await screen.findByRole('heading', { level: 2, name: 'Cambiar contraseña' })).toBeInTheDocument();
  });

  it('se recorren con las flechas del teclado', async () => {
    const { app } = await openAccount('/mi-cuenta/reservas');
    fireEvent.keyDown(screen.getByRole('tab', { name: /Mis reservas/ }), { key: 'ArrowRight' });
    await waitFor(() => expect(app.location()).toBe('/mi-cuenta/datos'));
    fireEvent.keyDown(screen.getByRole('tab', { name: /Mis datos/ }), { key: 'End' });
    await waitFor(() => expect(app.location()).toBe('/mi-cuenta/contrasena'));
  });

  it('una sección que no existe lleva a la primera', async () => {
    const { app } = await openAccount('/mi-cuenta/otra-cosa');
    await waitFor(() => expect(app.location()).toBe('/mi-cuenta'));
    expect(screen.getByRole('tab', { name: /Mis reservas/ })).toHaveAttribute('aria-selected', 'true');
  });
});

describe('Mis reservas', () => {
  it('lista cada reserva con número, fecha, estado, total y hasta cuándo se guarda', async () => {
    await openAccount('/mi-cuenta/reservas');
    const cards = await screen.findAllByTestId('reserva');
    expect(cards).toHaveLength(3);
    expect(screen.getByTestId('reservas-conteo')).toHaveTextContent('3 reservas');
    // De la más reciente a la más antigua.
    expect(cards.map((card) => within(card).getByTestId('reserva-numero').textContent)).toEqual(['RES-WEB-000012', 'ARM-WEB-000007', 'RES-WEB-000003']);
    expect(cards.map((card) => card.getAttribute('data-status'))).toEqual(['Reserved', 'Sold', 'Expired']);

    const active = cards[0];
    expect(within(active).getByText('Reservada')).toBeInTheDocument();
    expect(within(active).getByText('Fecha')).toBeInTheDocument();
    expect(within(active).getByText('Te la guardamos hasta')).toBeInTheDocument();
    expect(within(active).getByText(/^Total \(3 productos\)$/)).toBeInTheDocument();
    expect(active).toHaveTextContent(/Bs \d/);
    expect(within(cards[1]).getByText('Vendida')).toBeInTheDocument();
    expect(within(cards[2]).getByText('Vencida')).toBeInTheDocument();
    // Solo la reserva activa se puede liberar.
    expect(within(active).getByRole('button', { name: /Liberar mi reserva/ })).toBeInTheDocument();
    expect(within(cards[1]).queryByRole('button', { name: /Liberar mi reserva/ })).not.toBeInTheDocument();
    expect(within(cards[2]).queryByRole('button', { name: /Liberar mi reserva/ })).not.toBeInTheDocument();
  });

  it('filtra por estado con una lista desplegable', async () => {
    await openAccount('/mi-cuenta/reservas');
    await screen.findAllByTestId('reserva');
    const filter = screen.getByLabelText('Estado');
    expect(filter.tagName).toBe('SELECT');
    expect(within(filter).getAllByRole('option').map((option) => option.textContent)).toEqual(['Todas (3)', 'Reservadas (1)', 'Vendidas (1)', 'Vencidas (1)', 'Canceladas (0)']);

    fireEvent.change(filter, { target: { value: 'Sold' } });
    expect(screen.getAllByTestId('reserva').map((card) => card.getAttribute('data-status'))).toEqual(['Sold']);
    expect(screen.getByTestId('reservas-conteo')).toHaveTextContent('1 de 3 reservas');

    fireEvent.change(filter, { target: { value: 'Cancelled' } });
    expect(screen.queryAllByTestId('reserva')).toHaveLength(0);
    expect(screen.getByText('No tenés reservas con ese estado')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ver todas' }));
    expect(screen.getAllByTestId('reserva')).toHaveLength(3);
    expect(filter).toHaveValue('all');
  });

  it('muestra el detalle con sus productos al pedirlo', async () => {
    await openAccount('/mi-cuenta/reservas');
    const [active] = await screen.findAllByTestId('reserva');
    const toggle = within(active).getByRole('button', { name: /Ver detalle/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(within(active).queryByTestId('reserva-detalle')).not.toBeInTheDocument();

    fireEvent.click(toggle);
    expect(within(active).getByRole('button', { name: /Ocultar detalle/ })).toHaveAttribute('aria-expanded', 'true');
    const detail = within(active).getByTestId('reserva-detalle');
    const rows = within(within(detail).getByRole('table')).getAllByRole('row');
    expect(rows).toHaveLength(3);
    expect(rows[0]).toHaveTextContent('ProductoCant.Subtotal');
    expect(detail).toHaveTextContent('Casa matriz La Paz');
    expect(detail).toHaveTextContent('Paso a retirar el sábado por la mañana');
    expect(document.getElementById(toggle.getAttribute('aria-controls')!)).toContainElement(detail);

    fireEvent.click(within(active).getByRole('button', { name: /Ocultar detalle/ }));
    expect(within(active).queryByTestId('reserva-detalle')).not.toBeInTheDocument();
  });

  it('libera una reserva con confirmación', async () => {
    const { web } = await openAccount('/mi-cuenta/reservas');
    const [active] = await screen.findAllByTestId('reserva');
    fireEvent.click(within(active).getByRole('button', { name: /Liberar mi reserva/ }));
    expect(within(active).getByRole('alert')).toHaveTextContent('¿Liberar la reserva RES-WEB-000012?');

    // «No» la conserva.
    fireEvent.click(within(active).getByRole('button', { name: 'No, conservarla' }));
    expect(within(active).queryByRole('alert')).not.toBeInTheDocument();
    expect((await web.backend.rpc.send('GetMyReservationsQuery', {}))[0].status).toBe('Reserved');

    fireEvent.click(within(active).getByRole('button', { name: /Liberar mi reserva/ }));
    fireEvent.click(within(active).getByRole('button', { name: 'Sí, liberar' }));
    await waitFor(() => expect(active).toHaveAttribute('data-status', 'Cancelled'));
    expect(within(active).getByText('Cancelada')).toBeInTheDocument();
    expect(within(active).queryByRole('button', { name: /Liberar mi reserva/ })).not.toBeInTheDocument();
    expect(await screen.findByText('Reserva liberada')).toBeInTheDocument();
    expect((await web.backend.rpc.send('GetMyReservationsQuery', {}))[0].status).toBe('Cancelled');
    expect(within(screen.getByLabelText('Estado')).getAllByRole('option').map((option) => option.textContent)).toContain('Canceladas (1)');
  });

  it('si la reserva ya no estaba activa, avisa y muestra su estado real', async () => {
    const { web } = await openAccount('/mi-cuenta/reservas');
    const [active] = await screen.findAllByTestId('reserva');
    // Mientras tanto, la tienda la liberó por otro lado.
    await web.backend.rpc.send('CancelMyReservationCommand', { number: 'RES-WEB-000012' });
    fireEvent.click(within(active).getByRole('button', { name: /Liberar mi reserva/ }));
    fireEvent.click(within(active).getByRole('button', { name: 'Sí, liberar' }));
    expect(await screen.findByText('No pudimos liberar la reserva')).toBeInTheDocument();
    await waitFor(() => expect(screen.getAllByTestId('reserva')[0]).toHaveAttribute('data-status', 'Cancelled'));
  });

  it('un reintento por red caída viaja con el MISMO id de pedido', async () => {
    const reservation = {
      number: 'RES-WEB-000012',
      status: 'Reserved',
      statusText: 'Reservada',
      createdAt: '2026-09-27T18:00:00Z',
      reservedUntil: '2026-09-29T18:00:00Z',
      total: 100,
      contactName: 'Valentina',
      branch: 'CM',
      notes: null,
      hasCompatibilityWarnings: false,
      lines: [{ slot: null, sku: 'X-1', name: 'Producto', quantity: 1, unitPrice: 100, subtotal: 100 }],
      cancelReason: null,
    };
    const ids: (string | undefined)[] = [];
    const call = vi.fn(async (operation: string, _payload: unknown, options?: { requestId?: string }) => {
      if (operation === 'GetMyReservationsQuery') return { result: [reservation], replayed: false, requestId: 'q' };
      ids.push(options?.requestId);
      if (ids.length === 1) throw new WebApiError({ kind: 'network', message: 'sin red' });
      return { result: { ...reservation, status: 'Cancelled', statusText: 'Cancelada' }, replayed: false, requestId: options?.requestId ?? '' };
    });
    const rpc = { call, send: vi.fn() } as unknown as WebRpc;
    await renderRoutes({ web: webWith({ session: signedGateway(), rpc }), route: '/mi-cuenta/reservas' });
    const [card] = await screen.findAllByTestId('reserva');
    fireEvent.click(within(card).getByRole('button', { name: /Liberar mi reserva/ }));
    fireEvent.click(within(card).getByRole('button', { name: 'Sí, liberar' }));
    expect(await screen.findByText('No pudimos liberar la reserva')).toBeInTheDocument();
    // La confirmación sigue abierta para reintentar.
    fireEvent.click(await within(card).findByRole('button', { name: 'Sí, liberar' }));
    await waitFor(() => expect(card).toHaveAttribute('data-status', 'Cancelled'));
    expect(ids).toHaveLength(2);
    expect(ids[0]).toBeTruthy();
    expect(ids[1]).toBe(ids[0]);
  });

  it('muestra «cargando», el error con «Reintentar» y se recupera', async () => {
    let fail: (error: unknown) => void = () => undefined;
    const call = vi
      .fn()
      .mockReturnValueOnce(new Promise((_resolve, reject) => (fail = reject)))
      .mockRejectedValueOnce(new WebApiError({ kind: 'server', status: 500, message: 'El servidor no pudo completar la operación.' }))
      .mockResolvedValue({ result: [], replayed: false, requestId: 'q' });
    const rpc = { call, send: vi.fn() } as unknown as WebRpc;
    await renderRoutes({ web: webWith({ session: signedGateway(), rpc }), route: '/mi-cuenta/reservas' });

    expect(await screen.findByTestId('estado-cargando')).toHaveTextContent('Cargando tus reservas…');
    await act(async () => fail(new WebApiError({ kind: 'network', message: 'sin red' })));
    const error = await screen.findByTestId('estado-error');
    expect(error).toHaveTextContent('No pudimos cargar tus reservas');
    expect(error).toHaveTextContent('Revisá tu conexión');

    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await waitFor(() => expect(screen.getByTestId('estado-error')).toHaveTextContent('El servidor no pudo completar la operación.'));
    fireEvent.click(within(screen.getByTestId('estado-error')).getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByText('Todavía no tenés reservas')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver el catálogo' })).toHaveAttribute('href', '/catalogo');
    expect(call).toHaveBeenCalledTimes(3);
  });
});

describe('Mis datos', () => {
  it('carga los datos de la cuenta; el correo no se puede cambiar', async () => {
    await openAccount('/mi-cuenta/datos');
    expect(await screen.findByLabelText(/^Nombre y apellido/)).toHaveValue(CUSTOMER.name);
    expect(screen.getByLabelText(/^Correo/)).toHaveValue(CUSTOMER.email);
    expect(screen.getByLabelText(/^Correo/)).toHaveAttribute('readonly');
    expect(screen.getByLabelText(/^Teléfono o WhatsApp/)).toHaveValue('+591 71234567');
    expect(screen.getByLabelText(/^Tipo de documento/)).toHaveValue('');
    expect(screen.getByLabelText(/^Número de documento/)).toBeDisabled();
    expect(screen.getByLabelText(/^Complemento/)).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled();
  });

  it('ofrece los cinco tipos de documento en una lista desplegable', async () => {
    await openAccount('/mi-cuenta/datos');
    const select = await screen.findByLabelText(/^Tipo de documento/);
    expect(within(select).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Sin documento',
      'Cédula de identidad (CI)',
      'Cédula de extranjero (CEX)',
      'Pasaporte (PAS)',
      'Otro documento (OD)',
      'NIT',
    ]);
  });

  it('valida por campo y guarda nombre, teléfono y documento', async () => {
    const { web } = await openAccount('/mi-cuenta/datos');
    fireEvent.change(await screen.findByLabelText(/^Nombre y apellido/), { target: { value: 'Valentina A. Rojas' } });
    fireEvent.change(screen.getByLabelText(/^Teléfono o WhatsApp/), { target: { value: '123' } });
    fireEvent.change(screen.getByLabelText(/^Tipo de documento/), { target: { value: '5' } });
    expect(screen.getByLabelText(/^Número de documento/)).toBeEnabled();
    expect(screen.getByLabelText(/^Complemento/)).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/^Número de documento/), { target: { value: '10203040A' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText('El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.')).toBeInTheDocument();
    expect(screen.getByText('Con NIT el número de documento solo admite dígitos.')).toBeInTheDocument();
    expect(await web.backend.rpc.send('GetMyAccountQuery', {})).toMatchObject({ name: CUSTOMER.name, documentType: null });

    fireEvent.change(screen.getByLabelText(/^Teléfono o WhatsApp/), { target: { value: '7765 4321' } });
    fireEvent.change(screen.getByLabelText(/^Número de documento/), { target: { value: '1020304050' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText('Datos guardados')).toBeInTheDocument();
    expect(await web.backend.rpc.send('GetMyAccountQuery', {})).toEqual({
      name: 'Valentina A. Rojas',
      email: CUSTOMER.email,
      phone: '+591 77654321',
      documentType: 5,
      documentNumber: '1020304050',
      complement: null,
    });
    expect(screen.getByLabelText(/^Teléfono o WhatsApp/)).toHaveValue('+591 77654321');
    // El saludo y la cabecera toman el nombre nuevo de la sesión.
    expect(await screen.findByRole('heading', { level: 1, name: 'Hola, Valentina A. Rojas' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled();
  });

  it('el complemento solo se habilita con cédula de identidad y se limpia al cambiar de tipo', async () => {
    await openAccount('/mi-cuenta/datos');
    fireEvent.change(await screen.findByLabelText(/^Tipo de documento/), { target: { value: '1' } });
    fireEvent.change(screen.getByLabelText(/^Número de documento/), { target: { value: '1234567' } });
    fireEvent.change(screen.getByLabelText(/^Complemento/), { target: { value: '1A' } });
    expect(screen.getByLabelText(/^Complemento/)).toBeEnabled();
    fireEvent.change(screen.getByLabelText(/^Tipo de documento/), { target: { value: '3' } });
    expect(screen.getByLabelText(/^Complemento/)).toBeDisabled();
    expect(screen.getByLabelText(/^Complemento/)).toHaveValue('');
    expect(screen.getByLabelText(/^Número de documento/)).toHaveValue('1234567');

    fireEvent.click(screen.getByRole('button', { name: 'Descartar cambios' }));
    expect(screen.getByLabelText(/^Tipo de documento/)).toHaveValue('');
    expect(screen.getByLabelText(/^Número de documento/)).toHaveValue('');
  });

  it('muestra el error del servidor si rechaza el cambio, y el error de carga con «Reintentar»', async () => {
    const account = { name: 'Valentina Aguirre', email: 'valentina@correo.example', phone: '+591 71234567', documentType: null, documentNumber: null, complement: null };
    const call = vi.fn(async (operation: string) => {
      if (operation === 'GetMyAccountQuery') return { result: account, replayed: false, requestId: 'q' };
      throw new WebApiError({ kind: 'concurrency', status: 409, message: 'Otra persona cambió estos datos al mismo tiempo.' });
    });
    const first = await renderRoutes({ web: webWith({ session: signedGateway(), rpc: { call, send: vi.fn() } as unknown as WebRpc }), route: '/mi-cuenta/datos' });
    fireEvent.change(await screen.findByLabelText(/^Nombre y apellido/), { target: { value: 'Otro Nombre' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('No pudimos guardar tus datos');
    expect(alert).toHaveTextContent('Otra persona cambió estos datos al mismo tiempo.');
    expect(screen.getByLabelText(/^Nombre y apellido/)).toHaveValue('Otro Nombre');
    first.unmount();

    const failing = vi.fn().mockRejectedValueOnce(new WebApiError({ kind: 'network', message: 'sin red' })).mockResolvedValue({ result: account, replayed: false, requestId: 'q' });
    await renderRoutes({ web: webWith({ session: signedGateway(), rpc: { call: failing, send: vi.fn() } as unknown as WebRpc }), route: '/mi-cuenta/datos' });
    const error = await screen.findByTestId('estado-error');
    expect(error).toHaveTextContent('No pudimos cargar tus datos');
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByLabelText(/^Nombre y apellido/)).toHaveValue('Valentina Aguirre');
  });
});

describe('Cambiar contraseña (en Mi cuenta)', () => {
  function fillPasswords(current: string, next: string, confirm: string) {
    fireEvent.change(screen.getByLabelText(/^Contraseña actual/), { target: { value: current } });
    fireEvent.change(screen.getByLabelText(/^Nueva contraseña/), { target: { value: next } });
    fireEvent.change(screen.getByLabelText(/^Repetí la nueva contraseña/), { target: { value: confirm } });
  }

  it('valida la contraseña nueva antes de enviar', async () => {
    await openAccount('/mi-cuenta/contrasena');
    await screen.findByLabelText(/^Contraseña actual/);
    fillPasswords(CUSTOMER.password, 'debil', 'otra');
    fireEvent.click(screen.getByRole('button', { name: 'Cambiar contraseña' }));
    expect(await screen.findByText('La contraseña debe tener entre 8 y 128 caracteres y combinar letras y números.')).toBeInTheDocument();
    expect(screen.getByText('Las contraseñas no coinciden.')).toBeInTheDocument();
    fillPasswords(CUSTOMER.password, CUSTOMER.password, CUSTOMER.password);
    expect(await screen.findByText('La nueva contraseña debe ser distinta de la actual.')).toBeInTheDocument();
  });

  it('con la contraseña actual incorrecta marca el campo y NO cierra la sesión', async () => {
    const { app, web } = await openAccount('/mi-cuenta/contrasena');
    await screen.findByLabelText(/^Contraseña actual/);
    fillPasswords('Equivocada1', 'Nueva4567', 'Nueva4567');
    fireEvent.click(screen.getByRole('button', { name: 'Cambiar contraseña' }));
    expect(await screen.findByText('La contraseña actual no es correcta.')).toBeInTheDocument();
    expect(screen.getByLabelText(/^Contraseña actual/)).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText(/^Contraseña actual/)).toHaveValue('');
    expect(app.location()).toBe('/mi-cuenta/contrasena');
    expect(await web.backend.session.current()).not.toBeNull();
    expect(screen.getByRole('heading', { level: 1, name: `Hola, ${CUSTOMER.name}` })).toBeInTheDocument();
  });

  it('cambia la contraseña y sigue con la sesión iniciada', async () => {
    const { app, web } = await openAccount('/mi-cuenta/contrasena');
    await screen.findByLabelText(/^Contraseña actual/);
    fillPasswords(CUSTOMER.password, 'Nueva4567', 'Nueva4567');
    fireEvent.click(screen.getByRole('button', { name: 'Cambiar contraseña' }));
    expect(await screen.findByText('Contraseña cambiada')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText(/^Contraseña actual/)).toHaveValue(''));
    expect(screen.getByLabelText(/^Nueva contraseña/)).toHaveValue('');
    expect(app.location()).toBe('/mi-cuenta/contrasena');

    await web.backend.session.logout();
    await expect(web.backend.session.login({ email: CUSTOMER.email, password: 'Nueva4567' })).resolves.toMatchObject({ kind: 'customer' });
  });
});
