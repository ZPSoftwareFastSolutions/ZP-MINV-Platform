// Módulo «Integraciones» dentro del panel real con un registro que tiene SOLO este módulo. Servidor simulado en memoria
// en cada prueba (catálogo, llaves, webhooks, entregas, correos y sus comandos): ninguna prueba toca la red.
//
// Cubre: las pestañas en la dirección, los filtros que van al servidor y los de la página, crear una API Key y un
// webhook (validación, pedido exacto y secreto mostrado una vez con «Copiar»), revocar, rotar y desactivar con
// confirmación, «Ver sus entregas», la cola de correos con su reenvío (y el error del servidor dentro del diálogo), el
// resumen PLEGADO que no consulta hasta abrirse, exportar CSV, el error con «Reintentar» y quién ve qué.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';
import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { renderPanel, signedInAs, webWith, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import { MailsStat } from './MailsStat';
import integraciones from './module';

const REGISTRY = buildRegistry([{ source: '../modules/integraciones/module.tsx', definition: integraciones }]);

beforeAll(async () => {
  await import('./IntegrationsPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------------------------------- servidor simulado

type KeyRecord = RpcResponseOf<'GetApiKeysQuery'>[number];
type HookRecord = RpcResponseOf<'GetWebhooksQuery'>[number];
type DeliveryRecord = RpcResponseOf<'GetWebhookDeliveriesQuery'>[number];
type MailRecord = RpcResponseOf<'GetOutgoingMailsQuery'>[number];

const CATALOG: RpcResponseOf<'GetIntegrationCatalogQuery'> = {
  scopes: [
    { item1: 'catalog:read', item2: 'Leer productos, precios, códigos de barras y sucursales' },
    { item1: 'stock:read', item2: 'Leer existencias por sucursal y consolidadas' },
    { item1: 'orders:write', item2: 'Registrar pedidos de e-commerce como ventas' },
    { item1: 'reports:read', item2: 'Leer reportes gerenciales por sucursal' },
  ],
  events: [
    { item1: 'sale.completed', item2: 'Venta cobrada (POS o e-commerce)' },
    { item1: 'sale.voided', item2: 'Venta anulada (el stock volvió)' },
    { item1: 'pcbuild.reserved', item2: 'Armado de PC reservado' },
  ],
};

function branch(code: string, name: string): RpcResponseOf<'GetBranchesQuery'>[number] {
  return { id: `id-${code}`, code, name, isActive: true, isVisible: true, stockValue: 0, transfersIn: 0, transfersOut: 0, users: 1, warehouses: [code] };
}

function fakeServer() {
  const keys: KeyRecord[] = [
    {
      id: 'k-1',
      name: 'Tienda en línea',
      prefix: 'ab12cd34',
      owner: 'admin@techzone.example',
      branch: null,
      scopes: ['catalog:read', 'stock:read'],
      createdAt: '2026-09-20T15:00:00Z',
      expiresAt: null,
      revokedAt: null,
      lastUsedAt: '2026-09-29T12:00:00Z',
      isUsable: true,
    },
    {
      id: 'k-2',
      name: 'ERP contable',
      prefix: 'ef56gh78',
      owner: 'admin@techzone.example',
      branch: 'CB',
      scopes: ['reports:read'],
      createdAt: '2026-09-10T15:00:00Z',
      expiresAt: null,
      revokedAt: '2026-09-25T10:00:00Z',
      lastUsedAt: null,
      isUsable: false,
    },
  ];
  const hooks: HookRecord[] = [
    {
      id: 'h-1',
      url: 'https://erp.example/hooks/minv',
      description: 'ERP',
      events: ['sale.completed', 'sale.voided'],
      branch: null,
      isActive: true,
      createdAt: '2026-09-10T15:00:00Z',
      secretVersion: 1,
      delivered: 12,
      failed: 2,
      lastAttemptAt: '2026-09-29T14:00:00Z',
      lastError: '500: Error interno',
    },
    {
      id: 'h-2',
      url: 'https://tienda.example/avisos',
      description: null,
      events: ['pcbuild.reserved'],
      branch: 'CM',
      isActive: false,
      createdAt: '2026-09-11T15:00:00Z',
      secretVersion: 1,
      delivered: 3,
      failed: 0,
      lastAttemptAt: null,
      lastError: null,
    },
  ];
  const deliveries: (DeliveryRecord & { hook: string })[] = [
    { hook: 'h-1', attemptedAt: '2026-09-29T14:00:00Z', url: 'https://erp.example/hooks/minv', eventType: 'sale.completed', attempt: 1, statusCode: 200, succeeded: true, error: null, durationMs: 120 },
    { hook: 'h-1', attemptedAt: '2026-09-29T13:00:00Z', url: 'https://erp.example/hooks/minv', eventType: 'sale.voided', attempt: 3, statusCode: 500, succeeded: false, error: 'Error interno', durationMs: 900 },
    { hook: 'h-2', attemptedAt: '2026-09-27T13:00:00Z', url: 'https://tienda.example/avisos', eventType: 'pcbuild.reserved', attempt: 1, statusCode: null, succeeded: false, error: 'Tiempo de espera agotado', durationMs: 10000 },
  ];
  const mail = (overrides: Partial<MailRecord> & Pick<MailRecord, 'id' | 'reservation' | 'status' | 'statusText'>): MailRecord => ({
    reservationKind: 'Build',
    branchCode: 'CM',
    kind: 'ReservationConfirmed',
    kindText: 'Confirmación de reserva',
    recipient: 'valentina@cliente.example',
    attempts: 1,
    maxAttempts: 5,
    lastError: null,
    requestedAt: '2026-09-29T12:00:00Z',
    nextAttemptAt: null,
    lastAttemptAt: null,
    completedAt: null,
    ...overrides,
  });
  const mails: MailRecord[] = [
    mail({ id: 'm-1', reservation: 'ARM-WEB-000007', status: 'Sent', statusText: 'Enviado', completedAt: '2026-09-29T12:00:05Z' }),
    mail({ id: 'm-2', reservation: 'RES-WEB-000012', reservationKind: 'Cart', branchCode: 'CB', status: 'Pending', statusText: 'Pendiente', attempts: 2, lastError: 'Buzón lleno' }),
    mail({ id: 'm-3', reservation: 'ARM-CM-000100', status: 'Exhausted', statusText: 'Agotado', attempts: 5, lastError: 'Dirección inexistente', recipient: 'jorge@cliente.example' }),
  ];
  const sent: { operation: string; payload: unknown }[] = [];
  const handlers: Record<string, (payload: unknown) => unknown> = {
    GetIntegrationCatalogQuery: () => CATALOG,
    GetBranchesQuery: () => [branch('CM', 'Casa matriz La Paz'), branch('CB', 'Sucursal Cochabamba')],
    GetApiKeysQuery: () => keys.map((item) => ({ ...item })),
    GetWebhooksQuery: () => hooks.map((item) => ({ ...item })),
    GetWebhookDeliveriesQuery: (payload) => {
      const query = payload as RpcRequestOf<'GetWebhookDeliveriesQuery'>;
      return deliveries.filter((item) => !query.endpointId || item.hook === query.endpointId).slice(0, query.take ?? 200);
    },
    GetOutgoingMailsQuery: (payload) => {
      const query = payload as RpcRequestOf<'GetOutgoingMailsQuery'>;
      return mails.filter((item) => (!query.status || item.status === query.status) && (!query.number || item.reservation === query.number)).slice(0, query.take ?? 200);
    },
    CreateApiKeyCommand: (payload) => {
      const command = payload as RpcRequestOf<'CreateApiKeyCommand'>;
      keys.unshift({ ...keys[0], id: 'k-new', name: command.name, prefix: 'nn11pp22', scopes: [...command.scopes], branch: command.branchCode ?? null, lastUsedAt: null });
      return { id: 'k-new', name: command.name, prefix: 'nn11pp22', token: 'minv_nn11pp22_TOKEN-DE-PRUEBA', effectivePermissions: ['inventory.stock.view'] };
    },
    RevokeApiKeyCommand: (payload) => {
      const target = keys.find((item) => item.id === (payload as { id: string }).id)!;
      Object.assign(target, { revokedAt: '2026-09-29T16:00:00Z', isUsable: false });
      return `✔ Llave ${target.name} (${target.prefix}) revocada: el gateway la rechaza desde ahora.`;
    },
    CreateWebhookCommand: (payload) => {
      const command = payload as RpcRequestOf<'CreateWebhookCommand'>;
      hooks.push({ ...hooks[0], id: 'h-new', url: command.url, description: command.description ?? null, events: [...command.events], delivered: 0, failed: 0, lastError: null });
      return { id: 'h-new', url: command.url, secret: 'whsec_SECRETO-DE-PRUEBA', message: '✔ Webhook registrado. Guarde el secreto: se muestra una sola vez.' };
    },
    RotateWebhookSecretCommand: (payload) => {
      const target = hooks.find((item) => item.id === (payload as { id: string }).id)!;
      target.secretVersion += 1;
      return { id: target.id, url: target.url, secret: 'whsec_NUEVO', message: '✔ Secreto rotado: durante 24 horas cada entrega lleva las dos firmas.' };
    },
    DisableWebhookCommand: (payload) => {
      const target = hooks.find((item) => item.id === (payload as { id: string }).id)!;
      target.isActive = false;
      return `✔ Webhook ${target.url} desactivado.`;
    },
    ResendReservationMailCommand: (payload) => {
      const command = payload as RpcRequestOf<'ResendReservationMailCommand'>;
      if (command.number === 'ARM-CM-000100') {
        throw new WebApiError({ kind: 'domain', status: 422, code: 'pcbuild.state', message: 'La reserva ARM-CM-000100 está vendida: solo se reenvía la confirmación de una reserva vigente.' });
      }
      const row = mail({ id: `m-${mails.length + 1}`, reservation: command.number, status: 'Pending', statusText: 'Pendiente', recipient: command.email ?? 'valentina@cliente.example' });
      mails.unshift(row);
      return row;
    },
  };
  return { keys, hooks, mails, sent, handlers };
}

type FakeServer = ReturnType<typeof fakeServer>;

function serve(web: MockWeb, server: FakeServer) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  return vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const handler = server.handlers[operation];
    if (!handler) return real(operation, payload, options);
    server.sent.push({ operation, payload });
    return { result: (await handler(payload)) as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
  });
}

function sentOf(server: FakeServer, operation: string): unknown[] {
  return server.sent.filter((item) => item.operation === operation).map((item) => item.payload);
}

async function openAs(role: StaffRole = 'ADMIN', query = '', server = fakeServer()) {
  const web = await signedInAs(role);
  serve(web, server);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/integraciones${query}`, path: '/panel/*' });
  return { server, location: view.location };
}

async function openWithPermissions(permissions: readonly string[], query = '') {
  const web = await signedInAs('ADMIN');
  const server = fakeServer();
  serve(web, server);
  const base = web.backend.session;
  const session: ISessionGateway = {
    login: (credentials) => base.login(credentials),
    register: (registration) => base.register(registration),
    logout: () => base.logout(),
    current: async () => {
      const current = await base.current();
      return current && { ...current, permissions: [...permissions] };
    },
  };
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: webWith({ session, rpc: web.backend.rpc }), route: `/panel/integraciones${query}`, path: '/panel/*' });
  return { server, location: view.location };
}

async function table(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 5000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').length).toBeGreaterThan(1));
  return found;
}

function keysOf(grid: HTMLElement): string[] {
  return within(grid)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'))
    .map((row) => row.getAttribute('data-row-key') ?? '');
}

function rowOf(grid: HTMLElement, key: string): HTMLElement {
  const row = grid.querySelector<HTMLElement>(`[data-row-key="${key}"]`);
  if (!row) throw new Error(`No está la fila ${key}`);
  return row;
}

async function rowAction(grid: HTMLElement, key: string, label: string) {
  fireEvent.click(within(rowOf(grid, key)).getByRole('button', { name: /^Acciones de / }));
  fireEvent.click(await screen.findByRole('menuitem', { name: label }));
}

function stubClipboard() {
  const writeText = vi.fn(async () => undefined);
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
  return writeText;
}

// ---------------------------------------------------------------------------------------------------- API Keys

describe('Integraciones · API Keys', () => {
  it('lista las llaves con su prefijo (nunca el token) y filtra por estado con una lista desplegable', async () => {
    const { location } = await openAs();
    expect(await screen.findByRole('heading', { level: 1, name: 'Integraciones' })).toBeInTheDocument();
    const grid = await table();
    expect(keysOf(grid)).toEqual(['k-1', 'k-2']);
    expect(rowOf(grid, 'k-1')).toHaveTextContent('minv_ab12cd34_••••');
    expect(screen.getByTestId('llaves-resumen')).toHaveTextContent('2 de 2 llaves · 1 activas');
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'revocada' } });
    await waitFor(() => expect(keysOf(grid)).toEqual(['k-2']));
    expect(location()).toBe('/panel/integraciones?estado=revocada');
  });

  it('«Nueva API Key» valida, envía el pedido exacto y muestra el token UNA vez con «Copiar»', async () => {
    const writeText = stubClipboard();
    const { server } = await openAs();
    await table();
    fireEvent.click(screen.getByRole('button', { name: 'Nueva API Key' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nueva API Key' });
    expect(within(dialog).getByRole('checkbox', { name: 'Leer productos, precios, códigos de barras y sucursales' })).toBeChecked();
    fireEvent.change(within(dialog).getByLabelText(/^¿Para qué es\?/), { target: { value: ' ' } });
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Leer productos, precios, códigos de barras y sucursales' }));
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Leer existencias por sucursal y consolidadas' }));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear la llave' }));
    expect(await within(dialog).findByText('Elija al menos un alcance.')).toBeInTheDocument();
    expect(within(dialog).getByText('Indique para qué es la llave (por ejemplo «Tienda en línea»).')).toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText(/^¿Para qué es\?/), { target: { value: 'ERP Cochabamba' } });
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Registrar pedidos de e-commerce como ventas' }));
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Leer productos, precios, códigos de barras y sucursales' }));
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Sucursal' }), { target: { value: 'CB' } });
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Vencimiento' }), { target: { value: '365' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear la llave' }));
    });
    expect(sentOf(server, 'CreateApiKeyCommand')).toEqual([{ name: 'ERP Cochabamba', scopes: ['catalog:read', 'orders:write'], branchCode: 'CB', expiresInDays: 365 }]);

    const secret = await screen.findByRole('dialog', { name: 'API Key «ERP Cochabamba»' });
    expect(within(secret).getByTestId('secreto')).toHaveValue('minv_nn11pp22_TOKEN-DE-PRUEBA');
    expect(secret).toHaveTextContent('Permisos efectivos: Consultar stock, alertas y pedido sugerido.');
    expect(secret).toHaveTextContent('cabecera X-Api-Key');
    await act(async () => {
      fireEvent.click(within(secret).getByRole('button', { name: 'Copiar' }));
    });
    expect(writeText).toHaveBeenCalledWith('minv_nn11pp22_TOKEN-DE-PRUEBA');
    fireEvent.click(within(secret).getByRole('button', { name: 'Listo' }));
    await waitFor(() => expect(screen.queryByTestId('secreto')).not.toBeInTheDocument());
    // La lista se vuelve a leer con la llave nueva.
    await waitFor(() => expect(keysOf(screen.getByTestId('tabla'))).toContain('k-new'));
  });

  it('revocar pide confirmación y envía RevokeApiKeyCommand', async () => {
    const { server } = await openAs();
    const grid = await table();
    await rowAction(grid, 'k-1', 'Revocar');
    const confirm = await screen.findByRole('alertdialog', { name: '¿Revocar «Tienda en línea»?' });
    expect(confirm).toHaveTextContent('deja de funcionar de inmediato');
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Revocar' }));
    });
    expect(sentOf(server, 'RevokeApiKeyCommand')).toEqual([{ id: 'k-1' }]);
    expect(await screen.findByText('Llave Tienda en línea (ab12cd34) revocada: el gateway la rechaza desde ahora.')).toBeInTheDocument();
    await waitFor(() => expect(rowOf(grid, 'k-1')).toHaveTextContent('Revocada'));
  });

  it('exporta las llaves a CSV y, si la lista no carga, ofrece «Reintentar»', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const server = fakeServer();
    const original = server.handlers.GetApiKeysQuery;
    let failures = 1;
    server.handlers.GetApiKeysQuery = (payload) => {
      if (failures-- > 0) throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
      return original(payload);
    };
    await openAs('ADMIN', '', server);
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await table();
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines).toHaveLength(3);
    expect(lines[0]).toBe('"Nombre";"Llave";"Estado";"Alcances";"Sucursal";"Creada por";"Creada";"Vence";"Revocada";"Último uso"');
  });
});

// ---------------------------------------------------------------------------------------------------- webhooks y entregas

describe('Integraciones · webhooks y entregas', () => {
  it('«Nuevo webhook» exige https, envía el pedido exacto y muestra el secreto UNA vez', async () => {
    const { server, location } = await openAs('ADMIN', '?pestana=webhooks');
    const grid = await table();
    expect(keysOf(grid)).toEqual(['h-1', 'h-2']);
    fireEvent.click(screen.getByRole('button', { name: 'Nuevo webhook' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo webhook' });
    const url = within(dialog).getByLabelText(/^Dirección \(URL\)/);
    fireEvent.change(url, { target: { value: 'http://erp.example/avisos' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar el webhook' }));
    expect(await within(dialog).findByText('El webhook debe usar https (http solo para pruebas en este mismo equipo).')).toBeInTheDocument();
    fireEvent.change(url, { target: { value: 'https://erp.example/avisos' } });
    fireEvent.change(within(dialog).getByLabelText(/^Descripción/), { target: { value: 'ERP nuevo' } });
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Armado de PC reservado' }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar el webhook' }));
    });
    expect(sentOf(server, 'CreateWebhookCommand')).toEqual([{ url: 'https://erp.example/avisos', events: ['sale.completed', 'pcbuild.reserved'], description: 'ERP nuevo', branchCode: null }]);
    const secret = await screen.findByRole('dialog', { name: 'Secreto del webhook' });
    expect(within(secret).getByTestId('secreto')).toHaveValue('whsec_SECRETO-DE-PRUEBA');
    expect(secret).toHaveTextContent('X-MINV-Signature');
    expect(location()).toBe('/panel/integraciones?pestana=webhooks');
  });

  it('rotar el secreto pide confirmación y muestra el secreto nuevo; desactivar pide confirmación', async () => {
    const { server } = await openAs('ADMIN', '?pestana=webhooks');
    const grid = await table();
    await rowAction(grid, 'h-1', 'Rotar el secreto');
    const rotate = await screen.findByRole('alertdialog', { name: '¿Rotar el secreto del webhook?' });
    await act(async () => {
      fireEvent.click(within(rotate).getByRole('button', { name: 'Rotar el secreto' }));
    });
    expect(sentOf(server, 'RotateWebhookSecretCommand')).toEqual([{ id: 'h-1' }]);
    const secret = await screen.findByRole('dialog', { name: 'Secreto nuevo del webhook' });
    expect(within(secret).getByTestId('secreto')).toHaveValue('whsec_NUEVO');
    fireEvent.click(within(secret).getByRole('button', { name: 'Listo' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Secreto nuevo del webhook' })).not.toBeInTheDocument());

    await rowAction(grid, 'h-1', 'Desactivar');
    const disable = await screen.findByRole('alertdialog', { name: '¿Desactivar el webhook?' });
    await act(async () => {
      fireEvent.click(within(disable).getByRole('button', { name: 'Desactivar' }));
    });
    expect(sentOf(server, 'DisableWebhookCommand')).toEqual([{ id: 'h-1' }]);
    await waitFor(() => expect(rowOf(grid, 'h-1')).toHaveTextContent('Desactivado'));
  });

  it('«Ver sus entregas» abre las entregas de ese webhook (pedido al servidor) y la página filtra por resultado', async () => {
    const { server, location } = await openAs('ADMIN', '?pestana=webhooks');
    const grid = await table();
    await rowAction(grid, 'h-1', 'Ver sus entregas');
    await waitFor(() => expect(location()).toBe('/panel/integraciones?pestana=entregas&webhook=h-1'));
    await waitFor(() => expect(screen.getByTestId('entregas-resumen')).toHaveTextContent('2 de 2 entregas · 1 fallidas'));
    expect(sentOf(server, 'GetWebhookDeliveriesQuery')).toEqual([{ endpointId: 'h-1', take: 200 }]);
    fireEvent.change(screen.getByRole('combobox', { name: 'Resultado' }), { target: { value: 'fallida' } });
    await waitFor(() => expect(screen.getByTestId('entregas-resumen')).toHaveTextContent('1 de 2 entregas'));
    expect(screen.getByText('Fallida (500)')).toBeInTheDocument();
    fireEvent.change(screen.getByRole('combobox', { name: 'Entregas a revisar' }), { target: { value: '1000' } });
    await waitFor(() => expect(sentOf(server, 'GetWebhookDeliveriesQuery')).toContainEqual({ endpointId: 'h-1', take: 1000 }));
  });
});

// ---------------------------------------------------------------------------------------------------- correos

describe('Integraciones · correos de reservas', () => {
  it('el botón del tablero abre la cola; el estado va al servidor y el reenvío envía el pedido exacto', async () => {
    const { server } = await openAs('ADMIN', '?pestana=correos');
    const grid = await table();
    expect(screen.getByRole('tab', { name: 'Correos de reservas' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByTestId('correos-resumen')).toHaveTextContent('3 de 3 correos · 1 pendientes · 1 agotados');
    expect(sentOf(server, 'GetOutgoingMailsQuery')).toEqual([{ status: null, number: null, take: 200 }]);
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'Pending' } });
    await waitFor(() => expect(keysOf(grid)).toEqual(['m-2']));
    expect(sentOf(server, 'GetOutgoingMailsQuery')).toContainEqual({ status: 'Pending', number: null, take: 200 });

    await rowAction(grid, 'm-2', 'Reenviar el correo');
    const dialog = await screen.findByRole('dialog', { name: 'Reenviar la confirmación' });
    expect(dialog).toHaveTextContent('Reserva RES-WEB-000012 · último envío a valentina@cliente.example');
    fireEvent.change(within(dialog).getByLabelText(/^Enviar a otro correo/), { target: { value: 'otro@' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reenviar' }));
    expect(await within(dialog).findByText('Escriba un correo válido o déjelo vacío.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText(/^Enviar a otro correo/), { target: { value: 'otro@cliente.example' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Reenviar' }));
    });
    expect(sentOf(server, 'ResendReservationMailCommand')).toEqual([{ number: 'RES-WEB-000012', email: 'otro@cliente.example' }]);
    expect(await screen.findByText('Confirmación de RES-WEB-000012 en cola para otro@cliente.example')).toBeInTheDocument();
  });

  it('si la reserva ya no está vigente, el mensaje del servidor se ve dentro del diálogo', async () => {
    await openAs('ADMIN', '?pestana=correos');
    await table();
    fireEvent.click(screen.getByRole('button', { name: 'Reenviar una confirmación' }));
    const dialog = await screen.findByRole('dialog', { name: 'Reenviar la confirmación' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reenviar' }));
    expect(await within(dialog).findByText('Indique el número de la reserva.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText(/^Número de la reserva/), { target: { value: 'arm-cm-000100' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Reenviar' }));
    });
    expect(await within(dialog).findByText('La reserva ARM-CM-000100 está vendida: solo se reenvía la confirmación de una reserva vigente.')).toBeInTheDocument();
  });

  it('«Ver la reserva» lleva a Ventas › Reservas buscando ese número', async () => {
    const { location } = await openAs('ADMIN', '?pestana=correos');
    const grid = await table();
    await rowAction(grid, 'm-3', 'Ver la reserva');
    await waitFor(() => expect(location()).toBe('/panel/reservas?q=ARM-CM-000100'));
  });

  it('la estadística resume la cola y enlaza a los agotados', async () => {
    const web = await signedInAs('ADMIN');
    serve(web, fakeServer());
    await renderPanel(<MailsStat />, { web: web.services });
    const stat = await screen.findByTestId('resumen-correos');
    await waitFor(() => expect(stat).toHaveTextContent('Pendientes1'));
    expect(stat).toHaveTextContent('Agotados (sin enviar)1');
    expect(within(stat).getByRole('link', { name: 'Ver los correos agotados' })).toHaveAttribute('href', '/panel/integraciones?pestana=correos&estado=Exhausted');
  });
});

// ---------------------------------------------------------------------------------------------------- resumen y permisos

describe('Integraciones · resumen plegado y permisos', () => {
  it('«Ver resumen de las integraciones» está cerrado al entrar y consulta recién al abrirlo', async () => {
    const { server } = await openAs();
    await table();
    const toggle = screen.getByRole('button', { name: /Ver resumen de las integraciones/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByTestId('resumen-integraciones')).not.toBeInTheDocument();
    expect(sentOf(server, 'GetWebhooksQuery')).toEqual([]);
    fireEvent.click(toggle);
    const summary = await screen.findByTestId('resumen-integraciones');
    await waitFor(() => expect(summary).toHaveTextContent('Webhooks activos1'));
    expect(summary).toHaveTextContent('Entregas fallidas2');
    expect(within(summary).getByRole('link', { name: 'Ver las entregas fallidas' })).toHaveAttribute('href', '/panel/integraciones?pestana=entregas&resultado=fallida');
  });

  it('sin el permiso de las reservas no está la pestaña «Correos de reservas» (y su dirección vuelve a las llaves)', async () => {
    await openWithPermissions(['integration.manage', 'inventory.stock.view'], '?pestana=correos');
    await table();
    expect(screen.queryByRole('tab', { name: 'Correos de reservas' })).not.toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'API Keys' })).toHaveAttribute('aria-selected', 'true');
  });

  it('un rol sin `integration.manage` (ventas) ve «No tiene acceso a esta pantalla»', async () => {
    await openAs('VENTAS');
    expect(await screen.findByTestId('sin-acceso')).toHaveTextContent('Falta el permiso «Administrar API Keys y webhooks de integración B2B»');
  });
});
