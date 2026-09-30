// Módulo «Transferencias» dentro del panel real, con un registro que tiene SOLO este módulo y un servidor de
// transferencias SIMULADO en la prueba: lista con filtros en la dirección (también `?pendiente=recibir` del tablero),
// resumen plegado, detalle con series y bitácora, solicitar (origen = sucursal activa, destino en lista, series elegidas
// de las disponibles) con el pedido exacto, despachar (con el error del servidor dentro de la confirmación y sin abrir el
// detalle desde el menú «⋯»), recibir con faltantes, motivo y series que no llegaron, anular con motivo, «Reintentar» y lo
// que ve cada rol. Ninguna prueba toca la red. La fecha queda fija: 29/09/2026 en La Paz.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { permissionName, type RpcOperationName, type RpcResponseOf } from '@/4-presentation/app/contract';
import { ROLE_PERMISSIONS, renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions, dashboardStats } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import transferencias from './module';
import { TransfersStat } from './TransfersStat';

const REGISTRY = buildRegistry([{ source: '../modules/transferencias/module.tsx', definition: transferencias }]);
const NOW = new Date('2026-09-29T14:00:00Z');

beforeAll(async () => {
  await import('../../shell/PanelLayout');
  await import('./TransfersPage');
}, 30_000);

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(NOW);
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------------------------------- servidor simulado

type Transfer = RpcResponseOf<'GetTransfersQuery'>[number];

function transfer(overrides: Partial<Transfer>): Transfer {
  return {
    id: 't0',
    number: 'TR-CM-000000',
    fromBranch: 'Casa matriz La Paz',
    fromBranchCode: 'CM',
    fromWarehouse: 'ALMCM',
    toBranch: 'Sucursal Cochabamba',
    toBranchCode: 'CB',
    toWarehouse: 'ALMCB',
    status: 'Pending',
    statusLabel: 'Pendiente',
    requestedAt: '2026-09-20T14:00:00Z',
    dispatchedAt: null,
    receivedAt: null,
    lines: 1,
    quantity: 1,
    value: 0,
    shortage: 0,
    notes: null,
    canDispatch: false,
    canReceive: false,
    canCancel: false,
    ...overrides,
  };
}

const TRANSFERS: Transfer[] = [
  transfer({ id: 't3', number: 'TR-CM-000003', requestedAt: '2026-09-28T15:00:00Z', lines: 2, quantity: 3, canDispatch: true, canCancel: true, notes: 'Para la feria' }),
  transfer({
    id: 't2',
    number: 'TR-SC-000002',
    fromBranch: 'Sucursal Santa Cruz',
    fromBranchCode: 'SC',
    fromWarehouse: 'ALMSC',
    toBranch: 'Casa matriz La Paz',
    toBranchCode: 'CM',
    toWarehouse: 'ALMCM',
    status: 'Dispatched',
    statusLabel: 'En tránsito',
    requestedAt: '2026-09-25T14:00:00Z',
    dispatchedAt: '2026-09-26T14:00:00Z',
    lines: 2,
    quantity: 4,
    value: 2160,
    canReceive: true,
  }),
  transfer({ id: 't1', number: 'TR-CM-000001', toBranch: 'Sucursal Santa Cruz', toBranchCode: 'SC', toWarehouse: 'ALMSC', status: 'Received', statusLabel: 'Recibida', requestedAt: '2026-09-10T14:00:00Z', receivedAt: '2026-09-12T14:00:00Z', value: 500, shortage: 1 }),
];

const DETAILS: Record<string, RpcResponseOf<'GetTransferQuery'>> = {
  t3: {
    header: TRANSFERS[0],
    lines: [
      { lineId: 'l31', sku: 'CEL-01', name: 'Celular Samsung A55', unit: 'UND', quantity: 1, unitCost: null, received: 0, shortage: 0, shortageReason: null, lots: [], serials: ['356938035643809'] },
      { lineId: 'l32', sku: 'MOU-01', name: 'Mouse Logitech G502', unit: 'UND', quantity: 2, unitCost: null, received: 0, shortage: 0, shortageReason: null, lots: [], serials: null },
    ],
    history: [{ occurredAt: '2026-09-28T15:00:00Z', statusLabel: 'Pendiente', user: 'Andrea Quiroga', detail: 'Solicitada desde ALMCM' }],
  },
  t2: {
    header: TRANSFERS[1],
    lines: [
      { lineId: 'l21', sku: 'MOU-01', name: 'Mouse Logitech G502', unit: 'UND', quantity: 2, unitCost: 80, received: 0, shortage: 0, shortageReason: null, lots: ['L-01 × 2'], serials: null },
      { lineId: 'l22', sku: 'CEL-01', name: 'Celular Samsung A55', unit: 'UND', quantity: 2, unitCost: 1000, received: 0, shortage: 0, shortageReason: null, lots: [], serials: ['356938035643817', '356938035643825'] },
    ],
    history: [
      { occurredAt: '2026-09-25T14:00:00Z', statusLabel: 'Pendiente', user: 'Bruno Mamani', detail: '' },
      { occurredAt: '2026-09-26T14:00:00Z', statusLabel: 'En tránsito', user: 'Bruno Mamani', detail: 'Despachada' },
    ],
  },
};

const BRANCHES: RpcResponseOf<'GetBranchesQuery'> = [
  { id: '5d0c1f6e-0a51-4d0e-9d11-000000000001', code: 'CM', name: 'Casa matriz La Paz', isActive: true, isVisible: true, stockValue: 1, transfersIn: 1, transfersOut: 1, users: 3, warehouses: ['ALMCM'] },
  { id: '5d0c1f6e-0a51-4d0e-9d11-000000000002', code: 'CB', name: 'Sucursal Cochabamba', isActive: true, isVisible: true, stockValue: 1, transfersIn: 1, transfersOut: 0, users: 2, warehouses: ['ALMCB'] },
  { id: '5d0c1f6e-0a51-4d0e-9d11-000000000003', code: 'SC', name: 'Sucursal Santa Cruz', isActive: true, isVisible: true, stockValue: 1, transfersIn: 0, transfersOut: 1, users: 1, warehouses: ['ALMSC'] },
];

const PRODUCTS = [
  { sku: 'CEL-01', name: 'Celular Samsung A55', categoryCode: 'CEL', category: 'Celulares', brand: null, price: 2000, stock: 3, trackSerials: true, warrantyMonths: 12, keySpecs: '', platforms: [], imageId: null, serialKind: 'Imei' },
  { sku: 'MOU-01', name: 'Mouse Logitech G502', categoryCode: 'PER', category: 'Periféricos', brand: null, price: 150, stock: 10, trackSerials: false, warrantyMonths: 0, keySpecs: '', platforms: [], imageId: null, serialKind: 'Serial' },
] as RpcResponseOf<'SearchTechProductsQuery'>;

function serialRow(serial: string): RpcResponseOf<'GetAvailableSerialsQuery'>[number] {
  return { serial, sku: 'CEL-01', product: 'Celular Samsung A55', kind: 'Imei', status: 'InStock', branch: 'CM', warehouse: 'ALMCM', customer: null, invoiceNumber: null, receivedAt: null, soldAt: null, warrantyUntil: null };
}

type Handler = (payload: unknown) => unknown;

function fakeServer(web: MockWeb, handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const spy = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const reply = (result: unknown) => ({ result: result as never, replayed: false, requestId: options?.requestId ?? 'prueba' });
    const custom = handlers[operation];
    if (custom) return reply(await custom(payload));
    switch (operation) {
      case 'GetTransfersQuery':
        return reply(TRANSFERS);
      case 'GetTransferQuery':
        return reply(DETAILS[(payload as { id: string }).id]);
      case 'GetBranchesQuery':
        return reply(BRANCHES);
      case 'SearchTechProductsQuery':
        return reply(PRODUCTS);
      case 'GetAvailableSerialsQuery':
        return reply([serialRow('356938035643809'), serialRow('356938035643833')]);
      default:
        return real(operation as never, payload as never, options);
    }
  });
  const payloads = (operation: RpcOperationName) => spy.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
  return { spy, payloads };
}

async function openTransfers(role: StaffRole = 'ADMIN', query = '', handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const web = await signedInAs(role);
  const server = fakeServer(web, handlers);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/transferencias${query}`, path: '/panel/*' });
  return { server, location: view.location };
}

async function transfersTable(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 5000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').filter((row) => row.hasAttribute('data-row-key')).length).toBeGreaterThan(0));
  return found;
}

function keys(table: HTMLElement): (string | null)[] {
  return within(table)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'))
    .map((row) => row.getAttribute('data-row-key'));
}

function rowOf(table: HTMLElement, key: string): HTMLElement {
  const row = within(table)
    .getAllByRole('row')
    .find((item) => item.getAttribute('data-row-key') === key);
  if (!row) throw new Error(`No está la fila ${key}`);
  return row;
}

async function openRowMenu(table: HTMLElement, key: string): Promise<HTMLElement> {
  fireEvent.click(within(rowOf(table, key)).getByRole('button', { name: /^Acciones de / }));
  return screen.findByTestId('menu-acciones');
}

// ---------------------------------------------------------------------------------------------------- lista

describe('Transferencias · lista', () => {
  it('muestra las transferencias con lo que espera a mis sucursales y el resumen plegado (pedidos exactos)', async () => {
    const { server } = await openTransfers();
    expect(await screen.findByRole('heading', { level: 1, name: 'Transferencias' }, { timeout: 5000 })).toBeInTheDocument();
    const table = await transfersTable();
    expect(keys(table)).toEqual(['t3', 't2', 't1']);
    expect(server.payloads('GetTransfersQuery')).toEqual([{ status: null, take: 300 }]);
    expect(server.payloads('GetBranchesQuery')).toEqual([{}]);
    expect(rowOf(table, 't3')).toHaveTextContent('PendientePor despachar aquí');
    expect(rowOf(table, 't2')).toHaveTextContent('DespachadaPor recibir aquí');
    expect(rowOf(table, 't3')).toHaveTextContent('CM → CB');
    expect(screen.getByTestId('transferencias-resumen')).toHaveTextContent('3 de 3 transferencias · 1 por despachar y 1 por recibir');
    expect(screen.queryByTestId('resumen-transferencias')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Ver resumen de las transferencias/ }));
    expect(await screen.findByTestId('resumen-transferencias')).toHaveTextContent('Faltantes este mes1');
  });

  it('filtra con listas desplegables (también `?pendiente=recibir` del tablero) y cambia cuántas revisa', async () => {
    const { server, location } = await openTransfers('ADMIN', '?pendiente=recibir');
    const table = await transfersTable();
    await waitFor(() => expect(keys(table)).toEqual(['t2']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Origen' }), { target: { value: 'CM' } });
    await waitFor(() => expect(location()).toBe('/panel/transferencias?origen=CM'));
    await waitFor(() => expect(keys(table)).toEqual(['t3', 't1']));
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'Received' } });
    await waitFor(() => expect(keys(table)).toEqual(['t1']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Transferencias a revisar' }), { target: { value: '1000' } });
    await waitFor(() => expect(server.payloads('GetTransfersQuery')).toContainEqual({ status: null, take: 1000 }));
  });

  it('el detalle muestra líneas, series, lotes y la bitácora, con los botones del lado de la sesión', async () => {
    await openTransfers();
    const table = await transfersTable();
    fireEvent.click(within(rowOf(table, 't2')).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'Transferencia TR-SC-000002' });
    const lines = await within(panel).findByTestId('lineas-transferencia');
    expect(lines).toHaveTextContent('Lotes: L-01 × 2');
    expect(lines).toHaveTextContent('Series: 356938035643817, 356938035643825');
    expect(within(panel).getByTestId('bitacora-transferencia')).toHaveTextContent('En tránsito26/09/2026 10:00Bruno Mamani · Despachada');
    expect(within(panel).getByRole('button', { name: 'Recibir' })).toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: 'Despachar' })).not.toBeInTheDocument();
  });

  it('si la consulta falla muestra «Reintentar» y se recupera', async () => {
    let fail = true;
    await openTransfers('ADMIN', '', {
      GetTransfersQuery: () => {
        if (fail) {
          fail = false;
          throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
        }
        return TRANSFERS;
      },
    });
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await transfersTable();
  });
});

// ---------------------------------------------------------------------------------------------------- comandos

describe('Transferencias · comandos', () => {
  it('solicitar: origen = sucursal activa, destino en lista, series elegidas de las disponibles; pedido exacto', async () => {
    const created = transfer({ id: 't4', number: 'TR-CM-000004', canDispatch: true, canCancel: true });
    let requested = false;
    const { server } = await openTransfers('ADMIN', '', {
      CreateTransferCommand: () => {
        requested = true;
        return { id: 't4', number: 'TR-CM-000004', message: '✔ Transferencia TR-CM-000004 solicitada de ALMCM a ALMSC (2 productos).' };
      },
      GetTransfersQuery: () => (requested ? [created, ...TRANSFERS] : TRANSFERS),
      GetTransferQuery: () => ({ header: created, lines: [], history: [] }),
    });
    await transfersTable();
    fireEvent.click(screen.getByRole('button', { name: 'Nueva transferencia' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nueva transferencia' });
    const origin = within(dialog).getByRole('combobox', { name: 'Sale de (almacén de su sucursal)' });
    await waitFor(() => expect(origin).toHaveValue('ALMCM'));
    const destination = within(dialog).getByRole('combobox', { name: 'Llega a (almacén de destino)' });
    expect(within(destination).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Elija el almacén de destino',
      'ALMCB · CB Sucursal Cochabamba',
      'ALMSC · SC Sucursal Santa Cruz',
    ]);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Solicitar' }));
    expect(await within(dialog).findByText('Agregue al menos un producto.')).toBeInTheDocument();

    fireEvent.change(destination, { target: { value: 'ALMSC' } });
    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: 'Agregar producto' })).toBeEnabled());
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Agregar producto' }), { target: { value: 'Celular' } });
    fireEvent.click(await within(dialog).findByRole('option', { name: /^Celular Samsung A55/ }));
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Agregar producto' }), { target: { value: 'Mouse' } });
    fireEvent.click(await within(dialog).findByRole('option', { name: /^Mouse Logitech G502/ }));
    fireEvent.change(within(within(dialog).getByTestId('linea-MOU-01')).getByLabelText(/^Cantidad/), { target: { value: '4' } });
    const phoneLine = within(dialog).getByTestId('linea-CEL-01');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Solicitar' }));
    expect(await within(phoneLine).findByText('Elija 1 IMEI (una por unidad): hay 0.')).toBeInTheDocument();
    fireEvent.click(await within(phoneLine).findByRole('checkbox', { name: '356938035643809' }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Solicitar' }));
    });
    expect(await screen.findByText('Transferencia solicitada')).toBeInTheDocument();
    expect(server.payloads('CreateTransferCommand')).toEqual([
      {
        toWarehouseCode: 'ALMSC',
        lines: [
          { sku: 'CEL-01', quantity: 1, serials: ['356938035643809'] },
          { sku: 'MOU-01', quantity: 4, serials: null },
        ],
        notes: null,
        fromWarehouseCode: 'ALMCM',
      },
    ]);
    expect(server.payloads('GetAvailableSerialsQuery')).toEqual([{ sku: 'CEL-01', warehouseCode: 'ALMCM' }]);
    expect(await screen.findByRole('dialog', { name: 'Transferencia TR-CM-000004' })).toBeInTheDocument();
  });

  it('despachar desde el menú «⋯»: confirma lo que sale, no abre el detalle y muestra el error del servidor', async () => {
    let attempts = 0;
    const { server } = await openTransfers('ADMIN', '', {
      DispatchTransferCommand: () => {
        attempts += 1;
        if (attempts === 1) throw new WebApiError({ kind: 'domain', status: 422, message: 'No hay stock suficiente de MOU-01 para despachar 2: faltan 1 UND.' });
        return { id: 't3', number: 'TR-CM-000003', message: '✔ Transferencia TR-CM-000003 despachada: la mercadería está en tránsito hacia Sucursal Cochabamba.' };
      },
    });
    const table = await transfersTable();
    fireEvent.click(within(await openRowMenu(table, 't3')).getByRole('menuitem', { name: 'Despachar' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Despachar la transferencia TR-CM-000003?' });
    expect(screen.queryByRole('dialog', { name: 'Transferencia TR-CM-000003' })).not.toBeInTheDocument();
    expect(await within(confirm).findByTestId('despacho-lineas')).toHaveTextContent('Series: 356938035643809');
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Despachar' }));
    });
    expect(await within(confirm).findByText('No hay stock suficiente de MOU-01 para despachar 2: faltan 1 UND.')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Despachar' }));
    });
    expect(await screen.findByText('Transferencia despachada')).toBeInTheDocument();
    expect(server.payloads('DispatchTransferCommand')).toEqual([{ id: 't3' }, { id: 't3' }]);
  });

  it('recibir: un faltante exige motivo y, con serie, marcar la que no llegó; pedido exacto', async () => {
    const { server } = await openTransfers('ADMIN', '', {
      ReceiveTransferCommand: () => ({ id: 't2', number: 'TR-SC-000002', message: '✔ Transferencia TR-SC-000002 recibida con faltantes.' }),
    });
    const table = await transfersTable();
    fireEvent.click(within(await openRowMenu(table, 't2')).getByRole('menuitem', { name: 'Recibir' }));
    const dialog = await screen.findByRole('dialog', { name: 'Recibir TR-SC-000002' });
    const mouse = await within(dialog).findByTestId('recibir-MOU-01');
    const phone = within(dialog).getByTestId('recibir-CEL-01');
    expect(within(mouse).getByLabelText(/^Llegó/)).toHaveValue('2');
    fireEvent.change(within(mouse).getByLabelText(/^Llegó/), { target: { value: '1' } });
    fireEvent.change(within(phone).getByLabelText(/^Llegó/), { target: { value: '1' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirmar recepción' }));
    expect(await within(mouse).findByText('Indique el motivo del faltante.')).toBeInTheDocument();
    expect(within(phone).getByText('Marque la unidad que no llegó (marcadas: 0).')).toBeInTheDocument();
    expect(server.payloads('ReceiveTransferCommand')).toHaveLength(0);

    fireEvent.change(within(mouse).getByRole('combobox', { name: 'Motivo del faltante' }), { target: { value: 'Llegó dañado' } });
    fireEvent.change(within(phone).getByRole('combobox', { name: 'Motivo del faltante' }), { target: { value: 'otro' } });
    fireEvent.change(within(phone).getByLabelText(/^Escriba el motivo/), { target: { value: 'Caja abierta en el camino' } });
    fireEvent.click(within(phone).getByRole('checkbox', { name: '356938035643825' }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Confirmar recepción' }));
    });
    expect(await screen.findByText('Transferencia recibida')).toBeInTheDocument();
    expect(server.payloads('ReceiveTransferCommand')).toEqual([
      {
        id: 't2',
        lines: [
          { sku: 'MOU-01', receivedQuantity: 1, shortageReason: 'Llegó dañado', missingSerials: null },
          { sku: 'CEL-01', receivedQuantity: 1, shortageReason: 'Caja abierta en el camino', missingSerials: ['356938035643825'] },
        ],
      },
    ]);
  });

  it('anular pide el motivo y confirmación; envía el pedido exacto', async () => {
    const { server } = await openTransfers('ADMIN', '', {
      CancelTransferCommand: () => ({ id: 't3', number: 'TR-CM-000003', message: '✔ Transferencia TR-CM-000003 anulada.' }),
    });
    const table = await transfersTable();
    fireEvent.click(within(await openRowMenu(table, 't3')).getByRole('menuitem', { name: 'Anular' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Anular la transferencia TR-CM-000003?' });
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Anular transferencia' }));
    });
    expect(await within(confirm).findByText('Elija el motivo o escriba uno.')).toBeInTheDocument();
    expect(server.payloads('CancelTransferCommand')).toHaveLength(0);
    fireEvent.change(within(confirm).getByRole('combobox', { name: 'Motivo de la anulación' }), { target: { value: 'Se pidió por error' } });
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Anular transferencia' }));
    });
    expect(await screen.findByText('Transferencia anulada')).toBeInTheDocument();
    expect(server.payloads('CancelTransferCommand')).toEqual([{ id: 't3', reason: 'Se pidió por error' }]);
  });

  it('desde el tablero (?nueva=1) abre la transferencia nueva y el parámetro se quita', async () => {
    const { location } = await openTransfers('ADMIN', '?nueva=1');
    expect(await screen.findByRole('dialog', { name: 'Nueva transferencia' }, { timeout: 5000 })).toBeInTheDocument();
    await waitFor(() => expect(location()).toBe('/panel/transferencias'));
  });
});

// ---------------------------------------------------------------------------------------------------- roles

describe('Transferencias · lo que ve cada rol', () => {
  it('ventas (sin gestionar transferencias) ve «No tiene acceso a esta pantalla»', async () => {
    await openTransfers('VENTAS');
    const denied = await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 });
    expect(denied).toHaveTextContent(permissionName('inventory.transfers.manage'));
  });

  it('ofrece «Nueva transferencia» y «Recibir transferencia» al tablero solo a quien gestiona transferencias', () => {
    expect(REGISTRY.problems).toEqual([]);
    const offered = (role: StaffRole) => dashboardActions(REGISTRY, ROLE_PERMISSIONS[role]).flatMap((group) => group.actions.map((item) => item.to));
    expect(offered('BODEGA')).toEqual(['/panel/transferencias?nueva=1', '/panel/transferencias?pendiente=recibir']);
    expect(offered('GERENCIA')).toEqual(['/panel/transferencias?nueva=1', '/panel/transferencias?pendiente=recibir']);
    expect(offered('CAJERO')).toEqual([]);
    expect(offered('CONSULTA')).toEqual([]);
    expect(dashboardStats(REGISTRY, ROLE_PERMISSIONS.BODEGA).map((item) => item.stat.title)).toEqual(['Transferencias en curso']);
  });

  it('la estadística del tablero resume las transferencias y muestra su error con «Reintentar»', async () => {
    const web = await signedInAs('BODEGA');
    fakeServer(web);
    await renderPanel(<TransfersStat />, { web: web.services });
    const stat = await screen.findByTestId('resumen-transferencias');
    await waitFor(() => expect(stat).toHaveTextContent('Pendientes de despacho1'));
    expect(stat).toHaveTextContent('En tránsitoBs 2.160,00');
    expect(screen.getByRole('link', { name: 'Ver las que llegan a mis sucursales' })).toHaveAttribute('href', '/panel/transferencias?pendiente=recibir');
  });

  it('si la estadística no puede cargar, muestra el error con «Reintentar»', async () => {
    const web = await signedInAs('BODEGA');
    fakeServer(web, {
      GetTransfersQuery: () => {
        throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
      },
    });
    await renderPanel(<TransfersStat />, { web: web.services });
    expect(await screen.findByTestId('estado-error')).toHaveTextContent('No se pudo cargar la información');
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });
});
