// Módulo «Sucursales» dentro del panel real, con un registro que tiene SOLO este módulo y un servidor SIMULADO en la
// prueba: directorio con filtros en la dirección, comparativo PLEGADO (se consulta recién al abrirlo), detalle con los
// enlaces a las transferencias, nueva sucursal y edición con el pedido exacto, desactivar con confirmación (el menú «⋯»
// no abre el detalle) y el error del servidor dentro del diálogo, usuarios por sucursal y por usuario con casillas, el
// stock consolidado con una columna por sucursal, «Reintentar» y lo que ve cada rol. Ninguna prueba toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { permissionName, type RpcOperationName, type RpcResponseOf } from '@/4-presentation/app/contract';
import { renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import sucursales from './module';

const REGISTRY = buildRegistry([{ source: '../modules/sucursales/module.tsx', definition: sucursales }]);
const NOW = new Date('2026-09-29T14:00:00Z');

beforeAll(async () => {
  await import('../../shell/PanelLayout');
  await import('./BranchesPage');
}, 30_000);

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(NOW);
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

const BRANCHES: RpcResponseOf<'GetBranchesQuery'> = [
  { id: 'b1', code: 'CM', name: 'Casa matriz La Paz', isActive: true, isVisible: true, stockValue: 150000, transfersIn: 1, transfersOut: 2, users: 3, warehouses: ['ALMCM'] },
  { id: 'b2', code: 'CB', name: 'Sucursal Cochabamba', isActive: true, isVisible: false, stockValue: null, transfersIn: 2, transfersOut: 0, users: 2, warehouses: ['ALMCB'] },
  { id: 'b3', code: 'SC', name: 'Sucursal Santa Cruz', isActive: false, isVisible: true, stockValue: 0, transfersIn: 0, transfersOut: 0, users: 0, warehouses: ['ALMSC'] },
];

const REPORT: RpcResponseOf<'GetBranchReportQuery'> = {
  from: '2026-08-31',
  to: '2026-09-29',
  branches: [
    { code: 'CM', name: 'Casa matriz La Paz', tickets: 120, revenue: 90000, tax: 11700, averageTicket: 750, sharePercent: 75, stockValue: 150000 },
    { code: 'SC', name: 'Sucursal Santa Cruz', tickets: 40, revenue: 30000, tax: 3900, averageTicket: 750, sharePercent: 25, stockValue: 0 },
  ],
  days: [],
  totalRevenue: 120000,
  totalStockValue: 150000,
  inTransitValue: 2160,
  refreshedAt: '2026-09-29T13:55:00Z',
};

const STOCK: RpcResponseOf<'ConsolidatedStockQuery'> = {
  branches: [
    { id: 'b1', code: 'CM', name: 'Casa matriz La Paz' },
    { id: 'b3', code: 'SC', name: 'Sucursal Santa Cruz' },
  ],
  rows: [
    { sku: 'MOU-01', name: 'Mouse Logitech G502', category: 'Periféricos', unit: 'UND', byBranch: [8, 2], inTransit: 2, total: 12, value: 960 },
    { sku: 'MON-01', name: 'Monitor 27"', category: 'Monitores', unit: 'UND', byBranch: [3, 0], inTransit: 0, total: 3, value: 4500 },
  ],
  valueByBranch: [4000, 1300],
  inTransitValue: 160,
  totalValue: 5460,
};

function user(overrides: Partial<RpcResponseOf<'GetUsersQuery'>[number]>): RpcResponseOf<'GetUsersQuery'>[number] {
  return { email: 'x@techzone.example', name: 'X', roles: ['BODEGA'], branchCodes: ['CM'], isActive: true, isLocked: false, hasPassword: true, mustChangePassword: false, failedAttempts: 0, lastAccess: null, ...overrides };
}

const USERS: RpcResponseOf<'GetUsersQuery'> = [
  user({ email: 'bodega@techzone.example', name: 'Bruno Mamani', roles: ['BODEGA'], branchCodes: ['CM'] }),
  user({ email: 'ventas@techzone.example', name: 'Carla Rojas', roles: ['VENTAS'], branchCodes: ['CM', 'CB'] }),
  user({ email: 'cajero@techzone.example', name: 'Diego Flores', roles: ['CAJERO'], branchCodes: ['CB'] }),
  user({ email: 'cliente@techzone.example', name: 'Valentina Aguirre', roles: ['CLIENTE'], branchCodes: [] }),
];

type Handler = (payload: unknown) => unknown;

function fakeServer(web: MockWeb, handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const spy = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const reply = (result: unknown) => ({ result: result as never, replayed: false, requestId: options?.requestId ?? 'prueba' });
    const custom = handlers[operation];
    if (custom) return reply(await custom(payload));
    switch (operation) {
      case 'GetBranchesQuery':
        return reply(BRANCHES);
      case 'GetBranchReportQuery':
        return reply(REPORT);
      case 'ConsolidatedStockQuery':
        return reply(STOCK);
      case 'GetUsersQuery':
        return reply(USERS);
      default:
        return real(operation as never, payload as never, options);
    }
  });
  const payloads = (operation: RpcOperationName) => spy.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
  return { spy, payloads };
}

async function openBranches(role: StaffRole = 'ADMIN', query = '', handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const web = await signedInAs(role);
  const server = fakeServer(web, handlers);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/sucursales${query}`, path: '/panel/*' });
  return { server, location: view.location };
}

async function branchesTable(): Promise<HTMLElement> {
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

describe('Sucursales · directorio y comparativo', () => {
  it('muestra las sucursales; el comparativo está plegado y se consulta recién al abrirlo (pedidos exactos)', async () => {
    const { server } = await openBranches();
    expect(await screen.findByRole('heading', { level: 1, name: 'Sucursales' }, { timeout: 5000 })).toBeInTheDocument();
    const table = await branchesTable();
    expect(keys(table)).toEqual(['CB', 'CM', 'SC']);
    expect(server.payloads('GetBranchesQuery')).toEqual([{}]);
    expect(rowOf(table, 'CB')).toHaveTextContent('Sin acceso');
    expect(rowOf(table, 'SC')).toHaveTextContent('Inactiva');
    expect(screen.getByTestId('sucursales-resumen')).toHaveTextContent('3 de 3 sucursales · Gerencia global');
    expect(server.payloads('GetBranchReportQuery')).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: /Ver comparativo por sucursal/ }));
    const report = await screen.findByTestId('comparativo-sucursales');
    await waitFor(() => expect(server.payloads('GetBranchReportQuery')).toEqual([{ from: '2026-08-31', to: '2026-09-29' }]));
    expect(await within(report).findByTestId('comparativo-frescura')).toHaveTextContent('Datos del modelo de lectura actualizados el 29/09/2026 09:55');
    expect(within(report).getByRole('link', { name: 'Ver lo que está en tránsito' })).toHaveAttribute('href', '/panel/transferencias?estado=Dispatched');
  });

  it('filtra con listas desplegables y búsqueda; el detalle lleva a sus transferencias', async () => {
    const { location } = await openBranches();
    const table = await branchesTable();
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'inactiva' } });
    await waitFor(() => expect(location()).toBe('/panel/sucursales?estado=inactiva'));
    await waitFor(() => expect(keys(table)).toEqual(['SC']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Acceso' }), { target: { value: 'otras' } });
    await waitFor(() => expect(keys(table)).toEqual(['CB']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await waitFor(() => expect(keys(table)).toEqual(['CB', 'CM', 'SC']));
    fireEvent.click(within(rowOf(table, 'CM')).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'CM · Casa matriz La Paz' });
    expect(panel).toHaveTextContent('2 salen · 1 llegan');
    expect(within(panel).getByRole('link', { name: 'Transferencias que salen' })).toHaveAttribute('href', '/panel/transferencias?origen=CM');
    expect(within(panel).getByRole('link', { name: 'Transferencias que llegan' })).toHaveAttribute('href', '/panel/transferencias?destino=CM');
  });

  it('si la consulta falla muestra «Reintentar» y se recupera', async () => {
    let fail = true;
    await openBranches('ADMIN', '', {
      GetBranchesQuery: () => {
        if (fail) {
          fail = false;
          throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
        }
        return BRANCHES;
      },
    });
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await branchesTable();
  });
});

describe('Sucursales · alta, edición y estado', () => {
  it('nueva sucursal: propone el almacén, valida y envía el pedido exacto', async () => {
    const { server } = await openBranches('ADMIN', '', { CreateBranchCommand: () => '✔ Sucursal TJ · Sucursal Tarija creada con el almacén ALMTJ.' });
    await branchesTable();
    fireEvent.click(screen.getByRole('button', { name: 'Nueva sucursal' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nueva sucursal' });
    fireEvent.change(within(dialog).getByLabelText(/^Código de la sucursal/), { target: { value: 'cm' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear sucursal' }));
    expect(await within(dialog).findByText('Ya existe la sucursal CM.')).toBeInTheDocument();
    expect(within(dialog).getByText('Indique el nombre de la sucursal.')).toBeInTheDocument();
    expect(server.payloads('CreateBranchCommand')).toHaveLength(0);
    fireEvent.change(within(dialog).getByLabelText(/^Código de la sucursal/), { target: { value: 'tj' } });
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Nombre' }), { target: { value: 'Sucursal Tarija' } });
    expect(within(dialog).getByLabelText(/^Código del almacén/)).toHaveValue('ALMTJ');
    expect(within(dialog).getByLabelText(/^Nombre del almacén/)).toHaveValue('Almacén Tarija');
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Crear también su caja/ }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear sucursal' }));
    });
    expect(await screen.findByText('Sucursal creada')).toBeInTheDocument();
    expect(server.payloads('CreateBranchCommand')).toEqual([{ code: 'TJ', name: 'Sucursal Tarija', warehouseCode: 'ALMTJ', warehouseName: 'Almacén Tarija', createPosRegister: false }]);
  });

  it('editar desde el menú «⋯» (sin abrir el detalle) envía el nombre y el estado', async () => {
    const { server } = await openBranches('ADMIN', '', { UpdateBranchCommand: () => '✔ Sucursal CB actualizada.' });
    const table = await branchesTable();
    fireEvent.click(within(await openRowMenu(table, 'CB')).getByRole('menuitem', { name: 'Editar' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar la sucursal CB' });
    expect(screen.queryByRole('dialog', { name: 'CB · Sucursal Cochabamba' })).not.toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText(/^Nombre/), { target: { value: 'Sucursal Cochabamba Centro' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    });
    expect(await screen.findByText('Sucursal actualizada')).toBeInTheDocument();
    expect(server.payloads('UpdateBranchCommand')).toEqual([{ code: 'CB', name: 'Sucursal Cochabamba Centro', isActive: true }]);
  });

  it('desactivar pide confirmación y muestra el error del servidor; activar es directo', async () => {
    let attempts = 0;
    const { server } = await openBranches('ADMIN', '', {
      UpdateBranchCommand: (payload) => {
        const { isActive } = payload as { isActive: boolean };
        if (!isActive && ++attempts === 1) throw new WebApiError({ kind: 'domain', status: 422, message: 'La sucursal tiene transferencias en tránsito.' });
        return '✔ Sucursal actualizada.';
      },
    });
    const table = await branchesTable();
    fireEvent.click(within(await openRowMenu(table, 'CM')).getByRole('menuitem', { name: 'Desactivar' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Desactivar la sucursal CM?' });
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Desactivar' }));
    });
    expect(await within(confirm).findByText('La sucursal tiene transferencias en tránsito.')).toBeInTheDocument();
    expect(server.payloads('UpdateBranchCommand')).toEqual([{ code: 'CM', name: 'Casa matriz La Paz', isActive: false }]);
    fireEvent.click(within(confirm).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    await act(async () => {
      fireEvent.click(within(await openRowMenu(table, 'SC')).getByRole('menuitem', { name: 'Activar' }));
    });
    expect(await screen.findByText('Sucursal activada')).toBeInTheDocument();
    expect(server.payloads('UpdateBranchCommand')[1]).toEqual({ code: 'SC', name: 'Sucursal Santa Cruz', isActive: true });
  });
});

describe('Sucursales · usuarios', () => {
  it('usuarios de la sucursal con casillas: nadie queda sin sucursales y se envían todas las de cada persona', async () => {
    const { server } = await openBranches('ADMIN', '', { AssignUserBranchesCommand: () => '✔ Asignadas.' });
    const table = await branchesTable();
    fireEvent.click(within(await openRowMenu(table, 'CB')).getByRole('menuitem', { name: 'Usuarios de la sucursal' }));
    const dialog = await screen.findByRole('dialog', { name: 'Usuarios de la sucursal CB' });
    // Las cuentas de cliente no se ofrecen.
    await within(dialog).findByRole('checkbox', { name: /Bruno Mamani/ });
    expect(within(dialog).queryByRole('checkbox', { name: /Valentina/ })).not.toBeInTheDocument();
    expect(within(dialog).getByRole('checkbox', { name: /Carla Rojas/ })).toBeChecked();
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Diego Flores/ }));
    expect(within(dialog).getByText('Diego Flores quedaría sin sucursales: asígnele otra primero.')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Diego Flores/ }));
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Bruno Mamani/ }));
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Carla Rojas/ }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    });
    expect(await screen.findByText('Usuarios de la sucursal guardados')).toBeInTheDocument();
    expect(server.payloads('AssignUserBranchesCommand')).toEqual([
      { email: 'bodega@techzone.example', branchCodes: ['CM', 'CB'] },
      { email: 'ventas@techzone.example', branchCodes: ['CM'] },
    ]);
  });

  it('asignar sucursales a un usuario (como el escritorio)', async () => {
    const { server } = await openBranches('ADMIN', '', { AssignUserBranchesCommand: () => '✔ Diego Flores trabaja en: CM, CB.' });
    await branchesTable();
    fireEvent.click(screen.getByRole('button', { name: 'Asignar usuarios' }));
    const dialog = await screen.findByRole('dialog', { name: 'Asignar sucursales a un usuario' });
    fireEvent.change(await within(dialog).findByRole('combobox', { name: 'Usuario' }), { target: { value: 'cajero@techzone.example' } });
    expect(within(dialog).getByRole('checkbox', { name: 'CB · Sucursal Cochabamba' })).toBeChecked();
    // Una sucursal inactiva no se ofrece (salvo que ya la tenga, para poder quitarla).
    expect(within(dialog).queryByRole('checkbox', { name: 'SC · Sucursal Santa Cruz' })).not.toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'CM · Casa matriz La Paz' }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar sucursales' }));
    });
    expect(await screen.findByText('Sucursales asignadas')).toBeInTheDocument();
    expect(server.payloads('AssignUserBranchesCommand')).toEqual([{ email: 'cajero@techzone.example', branchCodes: ['CM', 'CB'] }]);
  });
});

describe('Sucursales · stock consolidado', () => {
  it('una columna por sucursal, lo que está en tránsito y la búsqueda en el servidor', async () => {
    const { server } = await openBranches('ADMIN', '?pestana=stock');
    const table = await branchesTable();
    expect(server.payloads('ConsolidatedStockQuery')).toEqual([{ search: null }]);
    expect(within(table).getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual(['Producto', 'CM', 'SC', 'En tránsito', 'Total', 'Valor']);
    expect(keys(table)).toEqual(['MON-01', 'MOU-01']);
    fireEvent.change(screen.getByRole('combobox', { name: 'En tránsito' }), { target: { value: 'con' } });
    await waitFor(() => expect(keys(table)).toEqual(['MOU-01']));
    const search = screen.getByRole('searchbox', { name: 'Buscar producto' });
    fireEvent.change(search, { target: { value: 'mouse' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(server.payloads('ConsolidatedStockQuery')).toContainEqual({ search: 'mouse' }));
    fireEvent.click(screen.getByRole('button', { name: /Ver valor por sucursal/ }));
    expect(await screen.findByTestId('valor-por-sucursal')).toHaveTextContent('Valor totalBs 5.460,00');
  });
});

describe('Sucursales · lo que ve cada rol', () => {
  it('consulta (reportes, sin administrar sucursales) ve el directorio sin «Nueva sucursal» ni «Editar»', async () => {
    await openBranches('CONSULTA');
    const table = await branchesTable();
    expect(screen.queryByRole('button', { name: 'Nueva sucursal' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Asignar usuarios' })).not.toBeInTheDocument();
    const menu = await openRowMenu(table, 'CM');
    expect(within(menu).queryByRole('menuitem', { name: 'Editar' })).not.toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: 'Transferencias que salen' })).not.toBeInTheDocument();
  });

  it('el cajero (sin reportes ni sucursales) ve «No tiene acceso a esta pantalla»', async () => {
    await openBranches('CAJERO');
    const denied = await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 });
    expect(denied).toHaveTextContent(permissionName('reports.view'));
  });
});
