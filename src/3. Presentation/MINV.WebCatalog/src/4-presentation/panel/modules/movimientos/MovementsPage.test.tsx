// Módulo «Movimientos» dentro del panel real con un registro que solo tiene este módulo: últimos movimientos con filtros
// en la dirección, detalle lateral, exportar CSV, tendencia plegada (no consulta hasta abrirse) y el REGISTRO: tipos
// según el rol, validación por campo, poka-yoke (campo en rojo y botón deshabilitado), observación obligatoria, series
// escritas o elegidas, el contenido EXACTO de `RegisterMovementCommand`, el error del servidor dentro del diálogo y la
// apertura desde otro módulo (`?registrar=…&sku=…`). Roles sin permiso de registro: sin acceso. Nada toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcOperationName, RpcResponseOf } from '@/4-presentation/app/contract';
import { renderPanel, signedInAs, type StaffRole } from '@/test-utils';
import { buildRegistry } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import movimientosModule from './module';

const REGISTRY = buildRegistry([{ source: '../modules/movimientos/module.tsx', definition: movimientosModule }]);

beforeAll(async () => {
  await import('../../shell/PanelApp');
  await import('./MovementsPage');
  await import('./RegisterMovementDialog');
  await import('./MovementTrend');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------------------------------- servidor simulado

type TypeRecord = RpcResponseOf<'GetMovementTypesQuery'>[number];
type RecentRecord = RpcResponseOf<'GetRecentMovementsQuery'>[number];

const TYPES: TypeRecord[] = [
  ['SALDO_INICIAL', 'SALDO INICIAL', 1, 'Warehouse', false],
  ['ENTRADA', 'ENTRADA', 1, 'Warehouse', false],
  ['AJUSTE_POS', 'AJUSTE (+)', 1, 'Warehouse', true],
  ['AJUSTE_NEG', 'AJUSTE (-)', -1, 'Warehouse', true],
  ['SALIDA', 'SALIDA', -1, 'Sales', false],
  ['VENTA_POS', 'VENTA POS', -1, 'Sales', false],
  ['DEVOLUCION_CLIENTE', 'DEVOLUCIÓN DE CLIENTE', 1, 'Sales', true],
  ['RECEPCION_COMPRA', 'RECEPCIÓN DE COMPRA', 1, 'Warehouse', false],
  ['TRASLADO_ENTRADA', 'TRASLADO (ENTRADA)', 1, 'Warehouse', false],
].map(([code, name, stockFactor, domain, requiresNotes]) => ({
  code: code as string,
  name: name as string,
  description: code === 'ENTRADA' ? 'Compra, recepción de mercancía o devolución de un cliente.' : null,
  stockFactor: stockFactor as number,
  domain: domain as TypeRecord['domain'],
  requiresNotes: requiresNotes as boolean,
  isInitialBalance: code === 'SALDO_INICIAL',
}));

function recent(recordedAt: string, typeCode: string, sku: string, name: string, quantity: number, patch: Partial<RecentRecord> = {}): RecentRecord {
  const type = TYPES.find((item) => item.code === typeCode)!;
  return { recordedAt, businessDate: recordedAt.slice(0, 10), sku, name, typeCode, typeName: type.name, stockFactor: type.stockFactor, quantity, unit: 'u.', binCode: 'CM-A-01', userName: 'Bruno Mamani', document: null, ...patch };
}

const RECENT: RecentRecord[] = [
  recent('2026-09-29T15:00:00Z', 'ENTRADA', 'MOU-LOG-G502', 'Mouse Logitech G502', 10, { document: 'REM-55' }),
  recent('2026-09-29T14:00:00Z', 'VENTA_POS', 'TEC-RZ-BW', 'Teclado Razer BlackWidow', 1, { binCode: 'CM-A-02', userName: 'Diego Flores', document: 'F-CM-000120' }),
  recent('2026-09-28T13:00:00Z', 'AJUSTE_NEG', 'MOU-LOG-G502', 'Mouse Logitech G502', 2),
  recent('2026-09-20T12:00:00Z', 'TRASLADO_ENTRADA', 'SSD-KIN-480', 'SSD Kingston 480 GB', 5, { userName: null, document: 'TR-CB-000009' }),
];

const PRODUCTS = [
  { sku: 'MOU-LOG-G502', name: 'Mouse Logitech G502', category: 'Periféricos', onHand: 8, available: 7, bin: 'CM-A-01', serial: false },
  { sku: 'TEC-RZ-BW', name: 'Teclado Razer BlackWidow', category: 'Teclados', onHand: 0, available: 0, bin: 'CM-A-02', serial: false },
  { sku: 'CEL-SAM-A15', name: 'Celular Samsung Galaxy A15', category: 'Celulares', onHand: 3, available: 3, bin: 'CM-B-01', serial: true },
];

const SERIALS = ['356938035643809', '356938035643817', '356938035643825'];

interface FakeOptions {
  registerFails?: WebApiError;
}

function fakeServer({ registerFails }: FakeOptions = {}) {
  const product = (sku: string) => PRODUCTS.find((item) => item.sku === sku) ?? PRODUCTS[0];
  const handlers: Partial<Record<RpcOperationName, (payload: never) => unknown>> = {
    GetRecentMovementsQuery: (payload: { take?: number }) => RECENT.slice(0, payload.take ?? 20),
    GetMovementTypesQuery: () => TYPES,
    GetWorkspaceQuery: () => ({ warehouseCode: 'CM-01', warehouseName: 'Almacén central', today: '2026-09-29', daysWithoutRotation: 60 }),
    GetBinsQuery: () => [
      { code: 'CM-A-01', zone: 'Zona A', warehouseCode: 'CM-01' },
      { code: 'CM-A-02', zone: 'Zona A', warehouseCode: 'CM-01' },
      { code: 'CM-B-01', zone: 'Zona B', warehouseCode: 'CM-01' },
    ],
    GetProductLookupQuery: () =>
      PRODUCTS.map((item) => ({ variantId: `v-${item.sku}`, sku: item.sku, name: item.name, category: item.category, unit: 'u.', allowsDecimals: false, isActive: true, barcodes: [`779${item.sku.length}`], primaryBin: item.bin })),
    GetProductCardQuery: (payload: { skuOrBarcode: string }) => {
      const item = product(payload.skuOrBarcode);
      return {
        variantId: `v-${item.sku}`,
        sku: item.sku,
        name: item.name,
        category: item.category,
        unit: 'u.',
        allowsDecimals: false,
        isActive: true,
        supplier: null,
        barcodes: [],
        minimum: 5,
        maximum: 20,
        unitCost: 100,
        onHand: item.onHand,
        reserved: item.onHand - item.available,
        available: item.available,
        status: 'Low',
        primaryBin: item.bin,
        bins: [{ binCode: item.bin, lotNumber: 'SIN-LOTE', onHand: item.onHand, reserved: item.onHand - item.available, available: item.available }],
        movements: [],
        totalMovements: 0,
      };
    },
    GetProductTechQuery: (payload: { sku: string }) => {
      const item = product(payload.sku);
      return { sku: item.sku, name: item.name, category: item.category, brand: null, trackSerials: item.serial, serialKind: item.serial ? 'Imei' : 'Serial', warrantyMonths: 12, specs: [], serialsInStock: item.serial ? 3 : 0 };
    },
    GetAvailableSerialsQuery: () =>
      SERIALS.map((serial) => ({ serial, kind: 'Imei', sku: 'CEL-SAM-A15', product: 'Celular Samsung Galaxy A15', status: 'InStock', branch: 'CM', warehouse: 'CM-01', receivedAt: null, soldAt: null, invoiceNumber: null, customer: null, warrantyUntil: null })),
    GetMovementTrendQuery: (payload: { days?: number }) =>
      Array.from({ length: payload.days ?? 14 }, (_, index) => ({ date: `2026-09-${String(index + 1).padStart(2, '0')}`, entries: index === 0 ? 10 : 1, issues: 2, movements: 3, opening: 0 })),
    RegisterMovementCommand: (payload: { sku: string; movementTypeCode: string; quantity: number }) => {
      if (registerFails) throw registerFails;
      const item = product(payload.sku);
      const factor = TYPES.find((type) => type.code === payload.movementTypeCode)?.stockFactor ?? 1;
      const onHand = item.onHand + factor * payload.quantity;
      return { movementId: 'm-1', stockLevelId: 's-1', quantityOnHand: onHand, available: onHand, message: '✔ Registrado' };
    },
  };
  return {
    handles: (operation: string) => operation in handlers,
    handle: (operation: RpcOperationName, payload: unknown) => handlers[operation]?.(payload as never),
  };
}

async function openMovements(role: StaffRole = 'BODEGA', query = '', server = fakeServer()) {
  const web = await signedInAs(role);
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const call = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    if (!server.handles(operation)) return real(operation, payload, options);
    return { result: server.handle(operation, payload) as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
  });
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/movimientos${query}`, path: '/panel/*' });
  return { call, location: view.location };
}

function payloadsOf(call: { mock: { calls: unknown[][] } }, operation: RpcOperationName): unknown[] {
  return call.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
}

async function table(): Promise<HTMLElement> {
  const region = await screen.findByRole('region', { name: 'Últimos movimientos' }, { timeout: 5000 });
  await waitFor(() => expect(within(region).queryAllByRole('row').length).toBeGreaterThan(1));
  return region;
}

function dataRows(region: HTMLElement): HTMLElement[] {
  return within(region)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

function cells(row: HTMLElement): string[] {
  return within(row)
    .getAllByRole('cell')
    .map((cell) => cell.textContent ?? '')
    .filter((text) => text.length > 0);
}

async function registerDialog(): Promise<HTMLElement> {
  return screen.findByRole('dialog', { name: 'Registrar un movimiento' }, { timeout: 5000 });
}

/** Elige un producto en el diálogo escribiendo parte de su nombre. */
async function pickProduct(dialog: HTMLElement, text: string, name: string) {
  const combo = within(dialog).getByRole('combobox', { name: /^Producto/ });
  await waitFor(() => expect(within(dialog).queryByText('Cargando el catálogo…')).not.toBeInTheDocument());
  fireEvent.change(combo, { target: { value: text } });
  fireEvent.click(await within(dialog).findByRole('option', { name: new RegExp(`^${name}`) }));
  await within(dialog).findByTestId('producto-elegido');
}

function quantityField(dialog: HTMLElement): HTMLElement {
  return within(dialog).getByRole('textbox', { name: /^Cantidad/ });
}

// ---------------------------------------------------------------------------------------------------- lista

describe('Movimientos · lista', () => {
  it('muestra los últimos movimientos, lo más reciente primero, con entrada o salida y quién', async () => {
    const { call } = await openMovements();
    expect(await screen.findByRole('heading', { level: 1, name: 'Movimientos' }, { timeout: 5000 })).toBeInTheDocument();
    const grid = await table();
    expect(cells(dataRows(grid)[0])).toEqual(['29/09/2026 11:00', 'Entrada', 'Mouse Logitech G502MOU-LOG-G502', '+10 u.', 'CM-A-01', 'REM-55', 'Bruno Mamani']);
    expect(cells(dataRows(grid)[1])).toEqual(['29/09/2026 10:00', 'Venta POS', 'Teclado Razer BlackWidowTEC-RZ-BW', '−1 u.', 'CM-A-02', 'F-CM-000120', 'Diego Flores']);
    expect(cells(dataRows(grid)[3])).toEqual(['20/09/2026 08:00', 'Traslado (entrada)', 'SSD Kingston 480 GBSSD-KIN-480', '+5 u.', 'CM-A-01', 'TR-CB-000009', 'Sistema']);
    expect(screen.getByTestId('movimientos-resumen')).toHaveTextContent('4 de 4 movimientos · 2 entradas y 2 salidas');
    expect(payloadsOf(call, 'GetRecentMovementsQuery')).toEqual([{ take: 200 }]);
    expect(document.title).toBe('Movimientos · Panel · Tech Zone Gaming');
  });

  it('filtra por tipo, entradas o salidas, usuario, producto y fechas; «a revisar» vuelve a consultar', async () => {
    const { call, location } = await openMovements();
    const grid = await table();
    const rows = () => dataRows(grid).map((row) => cells(row)[2]);
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: 'VENTA_POS' } });
    await waitFor(() => expect(rows()).toEqual(['Teclado Razer BlackWidowTEC-RZ-BW']));
    expect(location()).toBe('/panel/movimientos?tipo=VENTA_POS');
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Entradas o salidas' }), { target: { value: 'salidas' } });
    await waitFor(() => expect(rows()).toHaveLength(2));
    fireEvent.change(screen.getByRole('combobox', { name: 'Entradas o salidas' }), { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Usuario' }), { target: { value: '_sistema' } });
    await waitFor(() => expect(rows()).toEqual(['SSD Kingston 480 GBSSD-KIN-480']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));

    const productFilter = screen.getByRole('combobox', { name: 'Producto' });
    fireEvent.change(productFilter, { target: { value: 'mouse' } });
    fireEvent.click(await screen.findByRole('option', { name: /^Mouse Logitech G502/ }));
    await waitFor(() => expect(location()).toBe('/panel/movimientos?producto=MOU-LOG-G502'));
    await waitFor(() => expect(rows()).toHaveLength(2));

    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByLabelText('Desde'), { target: { value: '2026-09-28' } });
    fireEvent.change(screen.getByLabelText('Hasta'), { target: { value: '2026-09-28' } });
    await waitFor(() => expect(rows()).toEqual(['Mouse Logitech G502MOU-LOG-G502']));

    fireEvent.change(screen.getByRole('combobox', { name: 'Movimientos a revisar' }), { target: { value: '50' } });
    await waitFor(() => expect(payloadsOf(call, 'GetRecentMovementsQuery')).toEqual([{ take: 200 }, { take: 50 }]));
  });

  it('abre el detalle y desde ahí registra otro movimiento del mismo producto', async () => {
    const { location } = await openMovements();
    const grid = await table();
    fireEvent.click(within(dataRows(grid)[2]).getByRole('button', { name: /^Mouse Logitech G502/ }));
    const panel = await screen.findByRole('dialog', { name: 'Ajuste (-)' });
    expect(within(panel).getByTestId('detalle-movimiento')).toHaveTextContent('Cantidad−2 u.');
    expect(within(panel).getByRole('link', { name: 'Ver ficha y kardex' })).toHaveAttribute('href', '/panel/stock?ficha=MOU-LOG-G502');
    fireEvent.click(within(panel).getByRole('button', { name: 'Registrar otro movimiento de este producto' }));
    await waitFor(() => expect(location()).toBe('/panel/movimientos?registrar=1&sku=MOU-LOG-G502'));
    const dialog = await registerDialog();
    expect(await within(dialog).findByTestId('producto-elegido')).toHaveTextContent('Mouse Logitech G502 · MOU-LOG-G502');
  });

  it('exporta los movimientos filtrados a CSV', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openMovements('BODEGA', '?flujo=entradas');
    const grid = await table();
    await waitFor(() => expect(dataRows(grid)).toHaveLength(2));
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText(/^Se descargó movimientos-\d{4}-\d{2}-\d{2}\.csv \(2 filas\)\.$/)).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines.slice(1).map((line) => line.split(';')[4])).toEqual(['"MOU-LOG-G502"', '"SSD-KIN-480"']);
  });

  it('la tendencia está plegada: consulta recién al abrirla y al cambiar el período', async () => {
    const { call } = await openMovements();
    await table();
    expect(payloadsOf(call, 'GetMovementTrendQuery')).toEqual([]);
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /Ver tendencia de movimientos/ }));
    });
    const trend = await screen.findByTestId('tendencia-movimientos');
    await waitFor(() => expect(trend).toHaveTextContent('Entradas23'));
    expect(payloadsOf(call, 'GetMovementTrendQuery')).toEqual([{ days: 14 }]);
    fireEvent.change(within(trend).getByRole('combobox', { name: 'Período' }), { target: { value: '30' } });
    await waitFor(() => expect(payloadsOf(call, 'GetMovementTrendQuery')).toEqual([{ days: 14 }, { days: 30 }]));
  });

  it('un rol sin permiso para registrar movimientos (consulta) no entra', async () => {
    await openMovements('CONSULTA');
    expect(await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Últimos movimientos' })).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------------------------------- registro

describe('Movimientos · registrar', () => {
  it('bodega registra una entrada: envía `RegisterMovementCommand` con la forma exacta y la lista se actualiza', async () => {
    const { call, location } = await openMovements('BODEGA');
    await table();
    // Bodega registra los tipos de bodega; no las salidas de ventas.
    expect(screen.queryByRole('button', { name: 'Registrar salida' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Registrar entrada' }));
    await waitFor(() => expect(location()).toBe('/panel/movimientos?registrar=ENTRADA'));
    const dialog = await registerDialog();
    const typeSelect = within(dialog).getByRole('combobox', { name: /^Tipo de movimiento/ });
    await waitFor(() => expect(typeSelect).toHaveValue('ENTRADA'));
    expect(within(typeSelect).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Elija el tipo',
      'Entrada (suma)',
      'Ajuste (+) (suma)',
      'Ajuste (-) (resta)',
      'Saldo inicial (suma)',
    ]);

    await pickProduct(dialog, 'mouse', 'Mouse Logitech G502');
    expect(within(dialog).getByRole('combobox', { name: /^Posición/ })).toHaveValue('CM-A-01');
    fireEvent.change(quantityField(dialog), { target: { value: '5' } });
    expect(within(dialog).getByTestId('vista-previa')).toHaveTextContent('En CM-A-01: 7 u. disponibles → quedarán 12 u.');
    fireEvent.change(within(dialog).getByRole('textbox', { name: /^Documento/ }), { target: { value: 'REM-77' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar entrada' }));
    });
    expect(await screen.findByText('Movimiento registrado')).toBeInTheDocument();
    expect(screen.getByText('Entrada: +5 u. · MOU-LOG-G502 · quedan 13 u. en CM-A-01')).toBeInTheDocument();
    expect(payloadsOf(call, 'RegisterMovementCommand')).toEqual([
      {
        sku: 'MOU-LOG-G502',
        binCode: 'CM-A-01',
        movementTypeCode: 'ENTRADA',
        quantity: 5,
        businessDate: null,
        documentReference: 'REM-77',
        notes: null,
        lotNumber: null,
        adjustmentReasonCode: null,
        serials: null,
      },
    ]);
    await waitFor(() => expect(location()).toBe('/panel/movimientos'));
    await waitFor(() => expect(payloadsOf(call, 'GetRecentMovementsQuery')).toHaveLength(2));
    expect(payloadsOf(call, 'GetBinsQuery')).toContainEqual({ warehouseCode: 'CM-01' });
  });

  it('valida cada campo y exige la observación de los ajustes (sin enviar nada)', async () => {
    const { call } = await openMovements('BODEGA', '?registrar=ajuste');
    const dialog = await registerDialog();
    const typeSelect = within(dialog).getByRole('combobox', { name: /^Tipo de movimiento/ });
    await waitFor(() => expect(typeSelect).toHaveValue('AJUSTE_NEG'));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar ajuste (-)' }));
    expect(await within(dialog).findByText('Elija el producto.')).toBeInTheDocument();
    expect(within(dialog).getByText('Indique una cantidad mayor que 0.')).toBeInTheDocument();
    expect(within(dialog).getByText('Escriba la observación: es obligatoria para este tipo.')).toBeInTheDocument();
    expect(payloadsOf(call, 'RegisterMovementCommand')).toEqual([]);
  });

  it('POKA-YOKE: una salida que dejaría la posición en negativo marca el campo en rojo y deshabilita el botón', async () => {
    await openMovements('VENTAS', '?registrar=SALIDA&sku=MOU-LOG-G502');
    const dialog = await registerDialog();
    await within(dialog).findByTestId('producto-elegido');
    const typeSelect = within(dialog).getByRole('combobox', { name: /^Tipo de movimiento/ });
    expect(within(typeSelect).getAllByRole('option').map((option) => option.textContent)).toEqual(['Elija el tipo', 'Salida (resta)', 'Devolución de cliente (suma)', 'Venta POS (resta)']);
    const quantity = quantityField(dialog);
    fireEvent.change(quantity, { target: { value: '9' } });
    expect(quantity).toHaveAttribute('aria-invalid', 'true');
    expect(within(dialog).getByText('La salida dejaría la posición en negativo: solo hay 7 disponibles. Revise la cantidad o la posición.')).toBeInTheDocument();
    expect(within(dialog).getByTestId('vista-previa')).toHaveTextContent('quedarán -2 u.');
    const submit = within(dialog).getByRole('button', { name: 'Registrar salida' });
    expect(submit).toBeDisabled();
    fireEvent.change(quantity, { target: { value: '7' } });
    await waitFor(() => expect(submit).toBeEnabled());
    expect(quantity).not.toHaveAttribute('aria-invalid');
  });

  it('el rechazo del servidor se muestra dentro del diálogo, sin cerrarlo', async () => {
    const failure = new WebApiError({ kind: 'domain', status: 422, code: 'product.inactive', message: 'El producto MOU-LOG-G502 está inactivo.' });
    await openMovements('VENTAS', '?registrar=SALIDA&sku=MOU-LOG-G502', fakeServer({ registerFails: failure }));
    const dialog = await registerDialog();
    await within(dialog).findByTestId('producto-elegido');
    fireEvent.change(quantityField(dialog), { target: { value: '1' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar salida' }));
    });
    expect(await within(dialog).findByText('El producto MOU-LOG-G502 está inactivo.')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Registrar un movimiento' })).toBeInTheDocument();
  });

  it('un producto con IMEI pide una serie por unidad al entrar (y las envía)', async () => {
    const { call } = await openMovements('BODEGA', '?registrar=ENTRADA');
    const dialog = await registerDialog();
    await pickProduct(dialog, 'galaxy', 'Celular Samsung Galaxy A15');
    const series = await within(dialog).findByTestId('series');
    expect(series).toHaveTextContent('Lleva IMEI');
    fireEvent.change(quantityField(dialog), { target: { value: '2' } });
    const text = within(dialog).getByRole('textbox', { name: /^IMEI \(uno por línea\)/ });
    fireEvent.change(text, { target: { value: '356938035643833' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar entrada' }));
    expect(await within(dialog).findByText('Indique 2 series (una por unidad): hay 1.')).toBeInTheDocument();
    fireEvent.change(text, { target: { value: '356938035643833\n356938035643841' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar entrada' }));
    });
    await waitFor(() => expect(payloadsOf(call, 'RegisterMovementCommand')).toHaveLength(1));
    expect(payloadsOf(call, 'RegisterMovementCommand')[0]).toMatchObject({ sku: 'CEL-SAM-A15', binCode: 'CM-B-01', quantity: 2, serials: ['356938035643833', '356938035643841'] });
  });

  it('al sacar un producto con IMEI se eligen las unidades disponibles de la sucursal', async () => {
    const { call } = await openMovements('BODEGA', '?registrar=ajuste&sku=CEL-SAM-A15');
    const dialog = await registerDialog();
    await within(dialog).findByTestId('producto-elegido');
    fireEvent.change(quantityField(dialog), { target: { value: '1' } });
    fireEvent.click(await within(dialog).findByRole('checkbox', { name: SERIALS[1] }));
    fireEvent.change(within(dialog).getByRole('textbox', { name: /^Observaciones/ }), { target: { value: 'Pantalla rota' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar ajuste (-)' }));
    });
    await waitFor(() => expect(payloadsOf(call, 'RegisterMovementCommand')).toHaveLength(1));
    expect(payloadsOf(call, 'RegisterMovementCommand')[0]).toEqual({
      sku: 'CEL-SAM-A15',
      binCode: 'CM-B-01',
      movementTypeCode: 'AJUSTE_NEG',
      quantity: 1,
      businessDate: null,
      documentReference: null,
      notes: 'Pantalla rota',
      lotNumber: null,
      adjustmentReasonCode: null,
      serials: [SERIALS[1]],
    });
    expect(payloadsOf(call, 'GetAvailableSerialsQuery')).toContainEqual({ sku: 'CEL-SAM-A15', warehouseCode: 'CM-01' });
  });

  it('la definición ofrece «Registrar entrada» y «Registrar ajuste» solo a bodega', () => {
    expect(movimientosModule).toMatchObject({
      key: 'movimientos',
      section: 'inventario',
      order: 30,
      permissions: { all: ['inventory.stock.view'], any: ['inventory.movements.register.warehouse', 'inventory.movements.register.sales'] },
    });
    expect(movimientosModule.actions?.map((action) => [action.label, action.to, action.permissions])).toEqual([
      ['Registrar entrada', '?registrar=ENTRADA', { all: ['inventory.movements.register.warehouse'] }],
      ['Registrar ajuste', '?registrar=ajuste', { all: ['inventory.movements.register.warehouse'] }],
    ]);
    expect(REGISTRY.problems).toEqual([]);
  });
});
