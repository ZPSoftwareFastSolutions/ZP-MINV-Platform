// Módulo «Alertas» dentro del panel real con un registro que solo tiene este módulo: lista priorizada con la acción
// sugerida, filtros en la dirección (tipo, categoría, proveedor, sucursal y búsqueda), detalle lateral con los accesos
// (ficha, entrada o ajuste, pedido sugerido) solo para quien puede, exportar CSV, resumen plegado, «Todo en orden» y el
// error con «Reintentar». El servidor se simula en la prueba: nada toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcOperationName, RpcResponseOf } from '@/4-presentation/app/contract';
import { renderPanel, signedInAs, type StaffRole } from '@/test-utils';
import { buildRegistry } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import alertasModule from './module';

const REGISTRY = buildRegistry([{ source: '../modules/alertas/module.tsx', definition: alertasModule }]);

beforeAll(async () => {
  await import('../../shell/PanelApp');
  await import('./AlertsPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

type AlertRecord = RpcResponseOf<'GetStockProjectionQuery'>['result']['alerts'][number];

function alert(position: number, status: AlertRecord['status'], sku: string, name: string, patch: Partial<AlertRecord> = {}): AlertRecord {
  return { position, status, sku, name, category: 'Periféricos', supplier: 'Distribuidora Andina S.R.L.', stock: 3, minimum: 5, maximum: 20, unit: 'u.', shortfall: 2, suggestedQuantity: 17, lastMovement: '2026-09-20', ...patch };
}

const CM_ALERTS: AlertRecord[] = [
  alert(1, 'Inconsistent', 'CAB-HDMI', 'Cable HDMI 2 m', { category: 'Cables', supplier: 'Importadora Sur', stock: -2, shortfall: 7, suggestedQuantity: 0 }),
  alert(2, 'OutOfStock', 'TEC-RZ-BW', 'Teclado Razer BlackWidow', { category: 'Teclados', stock: 0, shortfall: 5, suggestedQuantity: 20 }),
  alert(3, 'Critical', 'MOU-LOG-G502', 'Mouse Logitech G502'),
  alert(4, 'Low', 'AUD-HX-C2', 'Audífonos HyperX Cloud II', { supplier: '', stock: 6, shortfall: 0, suggestedQuantity: 9, maximum: 15, lastMovement: null }),
  alert(5, 'Overstock', 'SSD-SAM-1T', 'SSD Samsung 1 TB', { category: 'Almacenamiento', supplier: 'Importadora Sur', stock: 30, shortfall: 0, suggestedQuantity: 0 }),
];

function fakeServer({ alerts = CM_ALERTS, failOnce = false }: { alerts?: AlertRecord[]; failOnce?: boolean } = {}) {
  let failures = failOnce ? 1 : 0;
  const handlers: Partial<Record<RpcOperationName, (payload: never) => unknown>> = {
    GetBranchesQuery: () =>
      ['CM', 'CB'].map((code) => ({ id: code, code, name: code === 'CM' ? 'Casa matriz La Paz' : 'Sucursal Cochabamba', isActive: true, isVisible: true, warehouses: [`${code}-01`], users: 1, stockValue: null, transfersIn: 0, transfersOut: 0 })),
    GetStockProjectionQuery: (payload: { warehouseCode?: string | null }) => {
      if (failures > 0) {
        failures -= 1;
        throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
      }
      const code = payload.warehouseCode ?? 'CM-01';
      return { warehouseCode: code, today: '2026-09-29', result: { stock: [], alerts: code === 'CB-01' ? alerts.slice(2, 3) : alerts, order: [], movements: 10 } };
    },
  };
  return {
    handles: (operation: string) => operation in handlers,
    handle: (operation: RpcOperationName, payload: unknown) => handlers[operation]?.(payload as never),
  };
}

async function openAlerts(role: StaffRole = 'ADMIN', query = '', server = fakeServer()) {
  const web = await signedInAs(role);
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const call = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    if (!server.handles(operation)) return real(operation, payload, options);
    return { result: server.handle(operation, payload) as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
  });
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/alertas${query}`, path: '/panel/*' });
  return { call, location: view.location };
}

async function table(): Promise<HTMLElement> {
  const region = await screen.findByRole('region', { name: 'Alertas del almacén' }, { timeout: 5000 });
  await waitFor(() => expect(within(region).queryAllByRole('row').length).toBeGreaterThan(1));
  return region;
}

function dataRows(region: HTMLElement): HTMLElement[] {
  return within(region)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

const keys = (region: HTMLElement) => dataRows(region).map((row) => row.getAttribute('data-row-key'));

function cells(row: HTMLElement): string[] {
  return within(row)
    .getAllByRole('cell')
    .map((cell) => cell.textContent ?? '')
    .filter((text) => text.length > 0);
}

describe('Alertas · lista priorizada', () => {
  it('muestra las alertas por urgencia con la existencia, lo que falta, lo sugerido y la acción', async () => {
    await openAlerts();
    expect(await screen.findByRole('heading', { level: 1, name: 'Alertas' }, { timeout: 5000 })).toBeInTheDocument();
    const grid = await table();
    expect(keys(grid)).toEqual(['CAB-HDMI', 'TEC-RZ-BW', 'MOU-LOG-G502', 'AUD-HX-C2', 'SSD-SAM-1T']);
    expect(cells(dataRows(grid)[0])).toEqual(['#1', 'Cable HDMI 2 mCAB-HDMI · Cables', 'Inconsistente', '-2 u.mín. 5', '7', '—', 'Revisar los movimientos y registrar un ajuste']);
    expect(cells(dataRows(grid)[1])).toEqual(['#2', 'Teclado Razer BlackWidowTEC-RZ-BW · Teclados', 'Sin stock', '0 u.mín. 5', '5', '20', 'Reabastecer de inmediato']);
    expect(screen.getByTestId('alertas-resumen')).toHaveTextContent('5 de 5 alertas · almacén CM-01 · al 29/09/2026');
    expect(document.title).toBe('Alertas · Panel · Tech Zone Gaming');
  });

  it('filtra por tipo, proveedor, categoría y búsqueda, y lo deja en la dirección', async () => {
    const { location } = await openAlerts();
    const grid = await table();
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: 'Critical' } });
    await waitFor(() => expect(location()).toBe('/panel/alertas?tipo=Critical'));
    await waitFor(() => expect(keys(grid)).toEqual(['MOU-LOG-G502']));
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Proveedor' }), { target: { value: 'Sin proveedor' } });
    await waitFor(() => expect(keys(grid)).toEqual(['AUD-HX-C2']));
    fireEvent.change(screen.getByRole('combobox', { name: 'Proveedor' }), { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Categoría' }), { target: { value: 'Almacenamiento' } });
    await waitFor(() => expect(keys(grid)).toEqual(['SSD-SAM-1T']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: 'hyperx' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(keys(grid)).toEqual(['AUD-HX-C2']));
    expect(location()).toBe('/panel/alertas?q=hyperx');
  });

  it('otra sucursal vuelve a consultar las alertas de su almacén', async () => {
    const { call, location } = await openAlerts();
    const grid = await table();
    fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal' }), { target: { value: 'CB-01' } });
    await waitFor(() => expect(location()).toBe('/panel/alertas?sucursal=CB-01'));
    await waitFor(() => expect(keys(grid)).toEqual(['MOU-LOG-G502']));
    expect(call.mock.calls.filter(([name]) => name === 'GetStockProjectionQuery').map(([, payload]) => payload)).toEqual([{ warehouseCode: null }, { warehouseCode: 'CB-01' }]);
  });

  it('sin alertas muestra «¡Todo en orden!»; si la consulta falla, el error con «Reintentar»', async () => {
    await openAlerts('ADMIN', '', fakeServer({ alerts: [], failOnce: true }));
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByText('¡Todo en orden!')).toBeInTheDocument();
    expect(screen.getByText('Ningún producto está sin stock, crítico, bajo, inconsistente ni con exceso.')).toBeInTheDocument();
  });
});

describe('Alertas · detalle y accesos', () => {
  it('bodega abre el detalle y tiene «Registrar entrada», el pedido sugerido y la ficha', async () => {
    await openAlerts('BODEGA');
    const grid = await table();
    fireEvent.click(within(dataRows(grid)[2]).getByRole('button', { name: /^Mouse Logitech G502/ }));
    const panel = await screen.findByRole('dialog', { name: 'Mouse Logitech G502' });
    expect(within(panel).getByTestId('detalle-alerta')).toHaveTextContent('Acción sugeridaEmitir una orden de compra');
    expect(within(panel).getByTestId('detalle-alerta')).toHaveTextContent('Faltan para el mínimo2 u.');
    expect(within(panel).getByRole('link', { name: 'Registrar entrada' })).toHaveAttribute('href', '/panel/movimientos?registrar=ENTRADA&sku=MOU-LOG-G502');
    expect(within(panel).getByRole('link', { name: 'Ver en el pedido sugerido' })).toHaveAttribute('href', '/panel/pedido?q=MOU-LOG-G502');
    expect(within(panel).getByRole('link', { name: 'Ver ficha y kardex' })).toHaveAttribute('href', '/panel/stock?ficha=MOU-LOG-G502');
  });

  it('para el exceso se ofrece el ajuste (no la entrada ni el pedido)', async () => {
    await openAlerts('BODEGA');
    const grid = await table();
    fireEvent.click(within(dataRows(grid)[4]).getByRole('button', { name: /^Acciones de / }));
    const menu = await screen.findByTestId('menu-acciones');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Ver detalle', 'Ver ficha y kardex', 'Registrar ajuste', 'Ver solo este proveedor']);
  });

  it('consulta no ve los botones de registro; la acción del menú lleva a la ficha sin abrir el detalle', async () => {
    const { location } = await openAlerts('CONSULTA');
    const grid = await table();
    fireEvent.click(within(dataRows(grid)[1]).getByRole('button', { name: /^Acciones de / }));
    const menu = await screen.findByTestId('menu-acciones');
    expect(within(menu).queryByRole('menuitem', { name: 'Registrar entrada' })).not.toBeInTheDocument();
    fireEvent.click(within(menu).getByRole('menuitem', { name: 'Ver ficha y kardex' }));
    await waitFor(() => expect(location()).toBe('/panel/stock?ficha=TEC-RZ-BW'));
  });

  it('«Ver solo este proveedor» filtra la lista', async () => {
    const { location } = await openAlerts();
    const grid = await table();
    fireEvent.click(within(dataRows(grid)[0]).getByRole('button', { name: /^Acciones de / }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Ver solo este proveedor' }));
    await waitFor(() => expect(location()).toBe('/panel/alertas?proveedor=Importadora+Sur'));
    await waitFor(() => expect(keys(grid)).toEqual(['CAB-HDMI', 'SSD-SAM-1T']));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('Alertas · exportar y resumen', () => {
  it('exporta las alertas filtradas a CSV', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openAlerts('ADMIN', '?proveedor=Importadora+Sur');
    const grid = await table();
    await waitFor(() => expect(keys(grid)).toHaveLength(2));
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText(/^Se descargó alertas-cm-01-\d{4}-\d{2}-\d{2}\.csv \(2 filas\)\.$/)).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines.slice(1).map((line) => line.split(';')[2])).toEqual(['"CAB-HDMI"', '"SSD-SAM-1T"']);
  });

  it('el resumen por tipo está plegado al entrar', async () => {
    await openAlerts();
    await table();
    expect(screen.queryByTestId('resumen-alertas')).not.toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /Ver resumen de alertas/ }));
    });
    const summary = await screen.findByTestId('resumen-alertas');
    expect(summary).toHaveTextContent('Sin stock1Reabastecer de inmediato');
    expect(summary).toHaveTextContent('Exceso1Frenar compras y rotar el inventario');
  });

  it('la definición ofrece «Ver alertas» con el permiso de ver stock', () => {
    expect(alertasModule).toMatchObject({ key: 'alertas', section: 'inventario', order: 50, permissions: { all: ['inventory.stock.view'] } });
    expect(alertasModule.actions?.map((action) => action.label)).toEqual(['Ver alertas']);
    expect(REGISTRY.problems).toEqual([]);
  });
});
