// Módulo de ejemplo «Actividad» dentro del panel real (registro del proyecto): lista de `GetActivityQuery` con filtros en
// la dirección (usuario y resultado con listas desplegables, fechas, búsqueda y cuántos registros traer), tabla, detalle
// lateral, exportar CSV, el comando `ResetUserPasswordCommand` (solo para quien administra usuarios), la estadística
// «Actividad de hoy» y el acceso por permisos. Servidor en memoria (actividad de muestra): ninguna prueba toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcResponseOf } from '@/4-presentation/app/contract';
import { preloadPanel, renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { toIsoDate } from '../../lib';
import { PANEL_REGISTRY } from '../../registry/discovery';
import { PanelApp } from '../../shell/PanelApp';
import { ActivityTodayStat } from './ActivityTodayStat';

beforeAll(async () => {
  await preloadPanel();
  await import('./ActivityPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

async function openActivity(role: StaffRole = 'ADMIN', query = ''): Promise<{ web: MockWeb; location: () => string }> {
  const web = await signedInAs(role);
  const view = await renderPanel(<PanelApp registry={PANEL_REGISTRY} />, { web: web.services, route: `/panel/actividad${query}`, path: '/panel/*' });
  return { web, location: view.location };
}

async function table(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 5000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').length).toBeGreaterThan(1));
  return found;
}

/** Filas de datos de la tabla (sin la cabecera). */
function dataRows(tableElement: HTMLElement): HTMLElement[] {
  return within(tableElement)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

describe('Actividad · lista', () => {
  it('muestra la actividad del servidor, lo más reciente primero, con el resumen y el título de la pestaña', async () => {
    const { web } = await openActivity();
    expect(await screen.findByRole('heading', { level: 1, name: 'Actividad' }, { timeout: 5000 })).toBeInTheDocument();
    const grid = await table();
    const all = await web.backend.rpc.send('GetActivityQuery', { take: 500 });
    expect(screen.getByTestId('actividad-resumen')).toHaveTextContent(`${all.length} de ${all.length} registros`);
    expect(dataRows(grid)).toHaveLength(Math.min(25, all.length));
    // Orden por fecha, de la más reciente a la más antigua.
    expect(within(grid).getByRole('columnheader', { name: /Fecha y hora/ })).toHaveAttribute('aria-sort', 'descending');
    expect(document.title).toBe('Actividad · Panel · Tech Zone Gaming');
  });

  it('filtra por resultado y por usuario con listas desplegables (y lo deja en la dirección)', async () => {
    const view = await openActivity();
    const grid = await table();
    fireEvent.change(screen.getByRole('combobox', { name: 'Resultado' }), { target: { value: 'Rejected' } });
    await waitFor(() => expect(view.location()).toBe('/panel/actividad?resultado=Rejected'));
    await waitFor(() => {
      expect(dataRows(grid).length).toBeGreaterThan(0);
      for (const row of dataRows(grid)) expect(row).toHaveTextContent('Rechazado');
    });

    fireEvent.change(screen.getByRole('combobox', { name: 'Resultado' }), { target: { value: 'Succeeded' } });
    const user = screen.getByRole('combobox', { name: 'Usuario' });
    expect(within(user).getByRole('option', { name: 'Bruno Mamani (bodega@techzone.example)' })).toBeInTheDocument();
    // El ingreso de la administradora quedó en la actividad: siempre hay al menos esa fila.
    fireEvent.change(user, { target: { value: 'admin@techzone.example' } });
    await waitFor(() => expect(view.location()).toBe('/panel/actividad?resultado=Succeeded&usuario=admin%40techzone.example'));
    await waitFor(() => {
      expect(dataRows(grid).length).toBeGreaterThan(0);
      for (const row of dataRows(grid)) expect(row).toHaveTextContent('Andrea Quiroga');
    });
    expect(screen.getByText('Inició sesión')).toBeInTheDocument();
    expect(screen.getByTestId('filtros-activos')).toHaveTextContent('2 activos');

    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await waitFor(() => expect(view.location()).toBe('/panel/actividad'));
  });

  it('filtra por fechas: días de La Paz, desde la dirección', async () => {
    const web = await signedInAs('ADMIN');
    const all = await web.backend.rpc.send('GetActivityQuery', { take: 500 });
    const day = toIsoDate(all[all.length - 1].occurredAt)!;
    const onThatDay = all.filter((row) => toIsoDate(row.occurredAt) === day).length;
    await openActivityAgain(web, `?desde=${day}&hasta=${day}`);
    await table();
    await waitFor(() => expect(screen.getByTestId('actividad-resumen')).toHaveTextContent(`${onThatDay} de ${all.length} registros`));
    expect(screen.getByTestId('filtros-activos')).toHaveTextContent('1 activo');
  });

  it('busca sin acentos (Enter aplica al instante y queda en la dirección)', async () => {
    const view = await openActivity();
    const grid = await table();
    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: 'inicio sesion' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(view.location()).toBe('/panel/actividad?q=inicio+sesion'));
    await waitFor(() => {
      expect(dataRows(grid).length).toBeGreaterThan(0);
      for (const row of dataRows(grid)) expect(row).toHaveTextContent('Inició sesión');
    });
  });

  it('«Registros a revisar» se pide al servidor: cambiarlo vuelve a consultar con esa cantidad', async () => {
    const web = await signedInAs('ADMIN');
    const call = vi.spyOn(web.backend.rpc, 'call');
    await openActivityAgain(web);
    await table();
    expect(call.mock.calls.filter(([operation]) => operation === 'GetActivityQuery').map(([, payload]) => payload)).toEqual([{ take: 500 }]);
    fireEvent.change(screen.getByRole('combobox', { name: 'Registros a revisar' }), { target: { value: '200' } });
    await waitFor(() => expect(call.mock.calls.filter(([operation]) => operation === 'GetActivityQuery').map(([, payload]) => payload)).toEqual([{ take: 500 }, { take: 200 }]));
  });

  it('muestra lo que responde el servidor (respuesta simulada en la prueba, sin tocar el modo mock)', async () => {
    const web = await signedInAs('GERENCIA');
    const rows: RpcResponseOf<'GetActivityQuery'> = [
      { occurredAt: '2026-09-29T14:00:00Z', userEmail: 'cajero@techzone.example', userName: 'Diego Flores', action: 'VoidSale', outcome: 'Rejected', details: '{"request":{},"result":null,"error":"La venta ya fue anulada."}' },
      { occurredAt: '2026-09-29T13:00:00Z', userEmail: null, userName: null, action: 'ExpirePcBuildReservations', outcome: 'Succeeded', details: null },
    ];
    vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, _payload, options) => {
      if (operation === 'GetActivityQuery') return { result: rows, replayed: false, requestId: options?.requestId ?? 'prueba' };
      throw new Error(`Operación no simulada en esta prueba: ${operation}`);
    });
    await openActivityAgain(web);
    const grid = await table();
    expect(dataRows(grid).map((row) => row.textContent)).toEqual([
      '29/09/2026 10:00Diego Florescajero@techzone.exampleAnuló una ventaRechazadoLa venta ya fue anulada.',
      '29/09/2026 09:00SistemaVenció reservas automáticamenteCorrecto—',
    ]);
  });

  it('si la consulta falla muestra el error con «Reintentar» y se recupera', async () => {
    const web = await signedInAs('ADMIN');
    const real = web.backend.rpc.call.bind(web.backend.rpc);
    vi.spyOn(web.backend.rpc, 'call').mockRejectedValueOnce(new WebApiError({ kind: 'network', message: 'Sin conexión' })).mockImplementation(real);
    await openActivityAgain(web);
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    expect(error).toHaveTextContent('No se pudo cargar la información');
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await table();
  });
});

describe('Actividad · detalle, exportar y comando', () => {
  it('abre el detalle lateral de una fila con los datos registrados y filtra por su usuario', async () => {
    const view = await openActivity();
    const grid = await table();
    const first = dataRows(grid)[0];
    fireEvent.click(within(first).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog');
    expect(within(panel).getByText('Código de la operación')).toBeInTheDocument();
    expect(within(panel).getByText('Resultado')).toBeInTheDocument();
    const onlyUser = within(panel).queryByRole('button', { name: /^Ver solo la actividad de / });
    if (onlyUser) {
      fireEvent.click(onlyUser);
      await waitFor(() => expect(view.location()).toMatch(/^\/panel\/actividad\?usuario=/));
    }
  });

  it('exporta las filas filtradas a CSV con el aviso del archivo', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openActivity('ADMIN', '?resultado=Failed');
    await table();
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    expect(screen.getByText(/^Se descargó actividad-\d{4}-\d{2}-\d{2}\.csv \(\d+ filas\)\.$/)).toBeInTheDocument();
    const text = await created[0].text();
    const lines = text.replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toBe('"Fecha y hora";"Usuario";"Correo";"Acción";"Código de la acción";"Resultado";"Detalle"');
    expect(lines.slice(1).every((line) => line.includes('"Con error"'))).toBe(true);
  });

  it('el administrador asigna una contraseña temporal: valida, envía el comando y la cuenta queda desbloqueada', async () => {
    const { web } = await openActivity('ADMIN', '?usuario=bodega%40techzone.example');
    const grid = await table();
    const row = dataRows(grid)[0];
    fireEvent.click(within(row).getByRole('button', { name: /^Acciones de / }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Asignar contraseña temporal' }));
    const dialog = await screen.findByRole('dialog', { name: 'Asignar una contraseña temporal' });
    expect(dialog).toHaveTextContent('Bruno Mamani · bodega@techzone.example');

    fireEvent.click(within(dialog).getByRole('button', { name: 'Asignar contraseña' }));
    expect(await within(dialog).findByText('Use entre 8 y 128 caracteres.')).toBeInTheDocument();

    const field = within(dialog).getByLabelText(/^Contraseña temporal/);
    fireEvent.change(field, { target: { value: 'Temporal2026' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Asignar contraseña' }));
    });
    expect(await screen.findByText('Contraseña temporal asignada a bodega@techzone.example')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Asignar una contraseña temporal' })).not.toBeInTheDocument());

    // En el servidor: ingresa con la temporal y debe cambiarla; la auditoría no guarda la contraseña.
    await web.backend.session.logout();
    const session = await web.backend.session.login({ email: 'bodega@techzone.example', password: 'Temporal2026' });
    expect(session.mustChangePassword).toBe(true);
    await web.backend.session.login({ email: 'admin@techzone.example', password: 'Demo1234' });
    const audit = await web.backend.rpc.send('GetActivityQuery', { take: 20 });
    const reset = audit.find((item) => item.action === 'ResetUserPassword');
    expect(reset).toMatchObject({ outcome: 'Succeeded', userEmail: 'admin@techzone.example' });
    expect(reset?.details).toContain('bodega@techzone.example');
    expect(reset?.details).not.toContain('Temporal2026');
  });

  it('quien no administra usuarios (gerencia) ve la actividad pero no el comando', async () => {
    await openActivity('GERENCIA');
    const grid = await table();
    fireEvent.click(within(dataRows(grid)[0]).getByRole('button', { name: /^Acciones de / }));
    const menu = await screen.findByTestId('menu-acciones');
    expect(within(menu).getByRole('menuitem', { name: 'Ver detalle' })).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: 'Asignar contraseña temporal' })).not.toBeInTheDocument();
  });

  it('un rol sin `iam.audit.view` ve «No tiene acceso a esta pantalla» con el permiso que falta', async () => {
    await openActivity('VENTAS');
    expect(await screen.findByTestId('sin-acceso')).toHaveTextContent('Falta el permiso «Consultar la auditoría y la actividad»');
    expect(screen.queryByTestId('tabla')).not.toBeInTheDocument();
  });
});

describe('Actividad · estadística «Actividad de hoy»', () => {
  it('resume lo de hoy (La Paz) con su propio estado y enlaza a la lista filtrada por hoy', async () => {
    const web = await signedInAs('ADMIN');
    const all = await web.backend.rpc.send('GetActivityQuery', { take: 1000 });
    const today = toIsoDate(new Date())!;
    const todays = all.filter((row) => toIsoDate(row.occurredAt) === today);
    await renderPanel(<ActivityTodayStat />, { web: web.services });
    const stat = await screen.findByTestId('actividad-de-hoy');
    await waitFor(() => expect(within(stat).getByRole('link', { name: 'Ver la actividad de hoy' })).toHaveAttribute('href', `/panel/actividad?desde=${today}&hasta=${today}`));
    expect(stat).toHaveTextContent(`Operaciones${todays.length}`);
    expect(stat).toHaveTextContent(`Rechazadas${todays.filter((row) => row.outcome === 'Rejected').length}`);
    expect(stat).toHaveTextContent(`Con error${todays.filter((row) => row.outcome === 'Failed').length}`);
  });

  it('si falla, muestra su error con «Reintentar» (sin afectar al resto del tablero)', async () => {
    const web = await signedInAs('ADMIN');
    vi.spyOn(web.backend.rpc, 'call').mockRejectedValue(new WebApiError({ kind: 'server', status: 500, message: 'Falla' }));
    await renderPanel(<ActivityTodayStat />, { web: web.services });
    expect(await screen.findByTestId('estado-error')).toHaveTextContent('No se pudo cargar la información');
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });
});

/** Abre la lista con una sesión ya preparada (para espiar o preparar el servidor antes). */
async function openActivityAgain(web: MockWeb, query = ''): Promise<{ location: () => string }> {
  const view = await renderPanel(<PanelApp registry={PANEL_REGISTRY} />, { web: web.services, route: `/panel/actividad${query}`, path: '/panel/*' });
  return { location: view.location };
}
