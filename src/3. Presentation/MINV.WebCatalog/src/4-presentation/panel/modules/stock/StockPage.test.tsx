// Módulo «Stock» dentro del panel real (esqueleto, rutas y permisos) con un registro que solo tiene este módulo: lista del
// almacén de trabajo con reservado, disponible, mínimo, cobertura y semáforo; filtros en la dirección (sucursal,
// categoría, marca, semáforo, reservas, rotación y búsqueda); otra sucursal y el consolidado; ficha lateral con kardex,
// reservas y existencias por sucursal (también por `?ficha=`); exportar CSV; resumen plegado; errores con «Reintentar»;
// botones solo para quien puede; y las dos estadísticas del tablero. El servidor se simula en la prueba: nada toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcOperationName, RpcResponseOf } from '@/4-presentation/app/contract';
import { preloadPanel, renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import stockModule from './module';
import { StockAlertsStat } from './StockAlertsStat';
import { StockValueStat } from './StockValueStat';

const REGISTRY = buildRegistry([{ source: '../modules/stock/module.tsx', definition: stockModule }]);

beforeAll(async () => {
  await preloadPanel();
  await import('./StockPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------------------------------- servidor simulado

type StockRecord = RpcResponseOf<'GetStockProjectionQuery'>['result']['stock'][number];

function stockRow(sku: string, name: string, category: string, patch: Partial<StockRecord> = {}): StockRecord {
  return {
    variantId: `v-${sku}`,
    sku,
    name,
    category,
    supplier: 'Distribuidora Andina S.R.L.',
    unit: 'u.',
    isActive: true,
    entries: 20,
    issues: 10,
    stock: 10,
    minimum: 5,
    maximum: 20,
    level: 0.5,
    status: 'Optimal',
    unitCost: 100,
    inventoryValue: 1000,
    lastMovement: '2026-09-27',
    daysWithoutMovement: 2,
    sales30Days: 12,
    coverageDays: 25,
    salesRank: 1,
    ...patch,
  };
}

const CM_ROWS: StockRecord[] = [
  stockRow('MOU-LOG-G502', 'Mouse Logitech G502', 'Periféricos', { stock: 4, status: 'Low', coverageDays: 12, inventoryValue: 400 }),
  stockRow('TEC-RZ-BW', 'Teclado Razer BlackWidow', 'Teclados', { stock: 0, status: 'OutOfStock', coverageDays: null, inventoryValue: 0, sales30Days: 0 }),
  stockRow('MON-LG-27', 'Monitor LG 27"', 'Monitores', { stock: 8, status: 'Optimal', daysWithoutMovement: 90, lastMovement: '2026-07-01', inventoryValue: 12000, unitCost: 1500 }),
  stockRow('SSD-SAM-1T', 'SSD Samsung 1 TB', 'Almacenamiento', { stock: 30, status: 'Overstock', coverageDays: 1200, inventoryValue: 21000, unitCost: 700 }),
];

const CB_ROWS: StockRecord[] = [stockRow('MOU-LOG-G502', 'Mouse Logitech G502', 'Periféricos', { stock: 9, status: 'Optimal', coverageDays: 30 })];

const BRANCH_IDS = { CM: '5d0c1f6e-0a51-4d0e-9d11-000000000001', CB: '5d0c1f6e-0a51-4d0e-9d11-000000000002', SC: '5d0c1f6e-0a51-4d0e-9d11-000000000003' };

function branches(visible: readonly string[]): RpcResponseOf<'GetBranchesQuery'> {
  return (['CM', 'CB', 'SC'] as const).map((code) => ({
    id: BRANCH_IDS[code],
    code,
    name: code === 'CM' ? 'Casa matriz La Paz' : code === 'CB' ? 'Sucursal Cochabamba' : 'Sucursal Santa Cruz',
    isActive: true,
    isVisible: visible.includes(code),
    warehouses: [`${code}-01`],
    users: 2,
    stockValue: null,
    transfersIn: 0,
    transfersOut: 0,
  }));
}

const CONSOLIDATED: RpcResponseOf<'ConsolidatedStockQuery'> = {
  branches: [
    { id: BRANCH_IDS.CM, code: 'CM', name: 'Casa matriz La Paz' },
    { id: BRANCH_IDS.CB, code: 'CB', name: 'Sucursal Cochabamba' },
    { id: BRANCH_IDS.SC, code: 'SC', name: 'Sucursal Santa Cruz' },
  ],
  rows: [
    { sku: 'MON-LG-27', name: 'Monitor LG 27"', category: 'Monitores', unit: 'u.', byBranch: [8, 0, 3], inTransit: 1, total: 12, value: 18000 },
    { sku: 'MOU-LOG-G502', name: 'Mouse Logitech G502', category: 'Periféricos', unit: 'u.', byBranch: [4, 9, 0], inTransit: 0, total: 13, value: 1300 },
  ],
  valueByBranch: [16400, 900, 4500],
  inTransitValue: 1500,
  totalValue: 23300,
};

function card(sku: string): RpcResponseOf<'GetProductCardQuery'> {
  const row = CM_ROWS.find((item) => item.sku === sku) ?? CM_ROWS[0];
  return {
    variantId: row.variantId,
    sku: row.sku,
    name: row.name,
    category: row.category,
    unit: row.unit,
    allowsDecimals: false,
    isActive: true,
    supplier: row.supplier,
    barcodes: ['7790001112223'],
    minimum: row.minimum,
    maximum: row.maximum,
    unitCost: row.unitCost,
    onHand: row.stock,
    reserved: sku === 'MOU-LOG-G502' ? 1 : 0,
    available: row.stock - (sku === 'MOU-LOG-G502' ? 1 : 0),
    status: row.status,
    primaryBin: 'CM-A-01',
    bins: [{ binCode: 'CM-A-01', lotNumber: 'SIN-LOTE', onHand: row.stock, reserved: sku === 'MOU-LOG-G502' ? 1 : 0, available: row.stock - (sku === 'MOU-LOG-G502' ? 1 : 0) }],
    movements: [
      { recordedAt: '2026-09-27T15:30:00Z', businessDate: '2026-09-27', typeCode: 'VENTA_POS', typeName: 'VENTA POS', signed: -6, balance: 4, binCode: 'CM-A-01', document: 'F-CM-000120', notes: null, userName: 'Diego Flores' },
      { recordedAt: '2026-09-20T13:00:00Z', businessDate: '2026-09-20', typeCode: 'ENTRADA', typeName: 'ENTRADA', signed: 10, balance: 10, binCode: 'CM-A-01', document: 'REM-55', notes: 'Compra a Distribuidora Andina', userName: 'Bruno Mamani' },
    ],
    totalMovements: 2,
  };
}

interface FakeOptions {
  /** Sucursales que la sesión ve (por defecto todas). */
  visible?: readonly string[];
  fail?: Partial<Record<RpcOperationName, WebApiError>>;
}

function fakeServer({ visible = ['CM', 'CB', 'SC'], fail = {} }: FakeOptions = {}) {
  const handlers: Partial<Record<RpcOperationName, (payload: never) => unknown>> = {
    GetBranchesQuery: () => branches(visible),
    GetWorkspaceQuery: () => ({
      tenantCode: 'TECHZONE',
      companyName: 'Tech Zone Gaming S.R.L.',
      taxId: null,
      currencyCode: 'BOB',
      currencySymbol: 'Bs',
      currencyDecimals: 2,
      warehouseCode: 'CM-01',
      warehouseName: 'Almacén central',
      timeZoneId: 'America/La_Paz',
      today: '2026-09-29',
      alertMargin: 0.2,
      daysWithoutRotation: 60,
    }),
    GetStockProjectionQuery: (payload: { warehouseCode?: string | null }) => {
      const code = payload.warehouseCode ?? 'CM-01';
      const stock = code === 'CB-01' ? CB_ROWS : CM_ROWS;
      const alerts = stock
        .filter((row) => ['OutOfStock', 'Critical', 'Low', 'Overstock', 'Inconsistent'].includes(row.status))
        .map((row, index) => ({ position: index + 1, status: row.status, sku: row.sku, name: row.name, category: row.category, supplier: row.supplier, stock: row.stock, minimum: row.minimum, maximum: row.maximum, unit: row.unit, shortfall: 0, suggestedQuantity: 0, lastMovement: row.lastMovement }));
      return { warehouseCode: code, today: '2026-09-29', result: { stock, alerts, order: [], movements: 57 } };
    },
    GetStockReservationsQuery: (payload: { warehouseCode?: string | null }) =>
      (payload.warehouseCode ?? 'CM-01') === 'CM-01'
        ? [
            { sku: 'MOU-LOG-G502', reserved: 1 },
            { sku: 'MON-LG-27', reserved: 2 },
          ]
        : [],
    SearchTechProductsQuery: () =>
      [
        ['MOU-LOG-G502', 'Logitech'],
        ['TEC-RZ-BW', 'Razer'],
        ['MON-LG-27', 'LG'],
        ['SSD-SAM-1T', 'Samsung'],
      ].map(([sku, brand]) => ({ sku, brand, name: sku, category: '', categoryCode: '', imageId: null, keySpecs: '', platforms: [], price: 0, serialKind: 'Serial', stock: 0, trackSerials: false, warrantyMonths: 12 })),
    ConsolidatedStockQuery: (payload: { search?: string | null }) =>
      payload.search ? { ...CONSOLIDATED, rows: CONSOLIDATED.rows.filter((row) => row.sku.includes(payload.search ?? '')) } : CONSOLIDATED,
    GetProductCardQuery: (payload: { skuOrBarcode: string }) => card(payload.skuOrBarcode),
  };
  return {
    handles: (operation: string) => operation in handlers,
    handle(operation: RpcOperationName, payload: unknown): unknown {
      const failure = fail[operation];
      if (failure) throw failure;
      return handlers[operation]?.(payload as never);
    },
  };
}

type FakeServer = ReturnType<typeof fakeServer>;

async function openStock(role: StaffRole = 'ADMIN', query = '', server: FakeServer = fakeServer()) {
  const web = await signedInAs(role);
  const call = spyServer(web, server);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/stock${query}`, path: '/panel/*' });
  return { web, call, location: view.location };
}

function spyServer(web: MockWeb, server: FakeServer) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  return vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    if (!server.handles(operation)) return real(operation, payload, options);
    return { result: server.handle(operation, payload) as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
  });
}

function payloadsOf(call: ReturnType<typeof spyServer>, operation: RpcOperationName): unknown[] {
  return call.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
}

async function table(caption = 'Stock del almacén'): Promise<HTMLElement> {
  const region = await screen.findByRole('region', { name: caption }, { timeout: 5000 });
  await waitFor(() => expect(within(region).queryAllByRole('row').length).toBeGreaterThan(1));
  return region;
}

function dataRows(region: HTMLElement): HTMLElement[] {
  return within(region)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

/** Texto de cada celda de una fila (sin la del botón de detalle ni la de acciones). */
function cells(row: HTMLElement): string[] {
  return within(row)
    .getAllByRole('cell')
    .map((cell) => cell.textContent ?? '')
    .filter((text) => text.length > 0);
}

function rowKeys(region: HTMLElement): string[] {
  return dataRows(region).map((row) => row.getAttribute('data-row-key') ?? '');
}

// ---------------------------------------------------------------------------------------------------- pruebas

describe('Stock · lista del almacén', () => {
  it('muestra existencias, reservado, disponible, mínimo, cobertura y semáforo, ordenado por producto', async () => {
    const { call } = await openStock();
    expect(await screen.findByRole('heading', { level: 1, name: 'Stock' }, { timeout: 5000 })).toBeInTheDocument();
    const grid = await table();
    expect(rowKeys(grid)).toEqual(['MON-LG-27', 'MOU-LOG-G502', 'SSD-SAM-1T', 'TEC-RZ-BW']);
    expect(cells(dataRows(grid)[1])).toEqual(['Mouse Logitech G502MOU-LOG-G502 · Periféricos', 'Bajo', '4 u.', '1', '3', '5', '12 días']);
    expect(cells(dataRows(grid)[2])).toEqual(['SSD Samsung 1 TBSSD-SAM-1T · Almacenamiento', 'Exceso', '30 u.', '—', '30', '5', '+999 días']);
    expect(cells(dataRows(grid)[3])).toEqual(['Teclado Razer BlackWidowTEC-RZ-BW · Teclados', 'Sin stock', '0 u.', '—', '0', '5', '—']);
    expect(screen.getByTestId('stock-resumen')).toHaveTextContent('4 de 4 productos · almacén CM-01 · al 29/09/2026 · 2 con reservas');
    // El almacén de trabajo se pide sin código (lo decide el servidor) y con TODOS los parámetros.
    expect(payloadsOf(call, 'GetStockProjectionQuery')).toEqual([{ warehouseCode: null }]);
    expect(payloadsOf(call, 'GetStockReservationsQuery')).toEqual([{ warehouseCode: null }]);
    expect(payloadsOf(call, 'SearchTechProductsQuery')).toEqual([{ text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 }]);
    expect(document.title).toBe('Stock · Panel · Tech Zone Gaming');
  });

  it('filtra con listas desplegables (semáforo, categoría, marca, reservas y rotación) y lo deja en la dirección', async () => {
    const view = await openStock();
    const grid = await table();
    fireEvent.change(screen.getByRole('combobox', { name: 'Semáforo' }), { target: { value: 'OutOfStock' } });
    await waitFor(() => expect(view.location()).toBe('/panel/stock?estado=OutOfStock'));
    await waitFor(() => expect(rowKeys(grid)).toEqual(['TEC-RZ-BW']));

    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await waitFor(() => expect(rowKeys(grid)).toHaveLength(4));
    const brand = screen.getByRole('combobox', { name: 'Marca' });
    await waitFor(() => expect(within(brand).getByRole('option', { name: 'Logitech' })).toBeInTheDocument());
    fireEvent.change(brand, { target: { value: 'Logitech' } });
    await waitFor(() => expect(rowKeys(grid)).toEqual(['MOU-LOG-G502']));

    fireEvent.change(brand, { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Categoría' }), { target: { value: 'Monitores' } });
    await waitFor(() => expect(rowKeys(grid)).toEqual(['MON-LG-27']));

    fireEvent.change(screen.getByRole('combobox', { name: 'Categoría' }), { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Reservas' }), { target: { value: 'con' } });
    await waitFor(() => expect(rowKeys(grid)).toEqual(['MON-LG-27', 'MOU-LOG-G502']));

    fireEvent.change(screen.getByRole('combobox', { name: 'Reservas' }), { target: { value: '' } });
    const rotation = screen.getByRole('combobox', { name: 'Rotación' });
    expect(screen.getByText('Sin rotación: con stock y sin movimientos hace más de 60 días.')).toBeInTheDocument();
    fireEvent.change(rotation, { target: { value: 'sin' } });
    await waitFor(() => expect(rowKeys(grid)).toEqual(['MON-LG-27']));
    expect(view.location()).toBe('/panel/stock?rotacion=sin');
  });

  it('busca sin acentos por nombre, SKU, marca o proveedor', async () => {
    const view = await openStock();
    const grid = await table();
    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: 'razer' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(view.location()).toBe('/panel/stock?q=razer'));
    await waitFor(() => expect(rowKeys(grid)).toEqual(['TEC-RZ-BW']));
    fireEvent.change(search, { target: { value: 'perifericos' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(rowKeys(grid)).toEqual(['MOU-LOG-G502']));
  });

  it('otra sucursal visible vuelve a consultar ese almacén; «Todas» muestra el consolidado con una columna por sucursal', async () => {
    const { call, location } = await openStock();
    const grid = await table();
    const branch = screen.getByRole('combobox', { name: 'Sucursal' });
    expect(within(branch).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Sucursal activa (CM)',
      'CM · Casa matriz La Paz',
      'CB · Sucursal Cochabamba',
      'SC · Sucursal Santa Cruz',
      'Todas las sucursales (consolidado)',
    ]);
    fireEvent.change(branch, { target: { value: 'CB-01' } });
    await waitFor(() => expect(location()).toBe('/panel/stock?sucursal=CB-01'));
    await waitFor(() => expect(rowKeys(grid)).toEqual(['MOU-LOG-G502']));
    expect(payloadsOf(call, 'GetStockProjectionQuery')).toContainEqual({ warehouseCode: 'CB-01' });

    fireEvent.change(branch, { target: { value: 'todas' } });
    const consolidated = await table('Stock consolidado de las sucursales');
    expect(within(consolidated).getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['Producto', 'CM', 'CB', 'SC', 'En tránsito', 'Total', 'Valor', 'Acciones']);
    expect(cells(dataRows(consolidated)[0])).toEqual(['Monitor LG 27"MON-LG-27 · Monitores · u.', '8', '0', '3', '1', '12', 'Bs 18.000,00']);
    expect(within(consolidated).getByTestId('tabla-totales')).toHaveTextContent('Totales12931125Bs 19.300,00');
    expect(payloadsOf(call, 'ConsolidatedStockQuery')).toEqual([{ search: null }]);
    // El semáforo es de cada almacén: en el consolidado no se usa.
    expect(screen.getByRole('combobox', { name: 'Semáforo' })).toBeDisabled();
  });

  it('quien ve una sola sucursal no tiene el consolidado', async () => {
    await openStock('BODEGA', '', fakeServer({ visible: ['CM'] }));
    await table();
    const branch = screen.getByRole('combobox', { name: 'Sucursal' });
    expect(within(branch).getAllByRole('option').map((option) => option.textContent)).toEqual(['Sucursal activa (CM)', 'CM · Casa matriz La Paz']);
  });

  it('si la consulta falla muestra el error con «Reintentar» y se recupera', async () => {
    const web = await signedInAs('ADMIN');
    const server = fakeServer();
    const call = spyServer(web, server);
    const failing = fakeServer({ fail: { GetStockProjectionQuery: new WebApiError({ kind: 'network', message: 'Sin conexión' }) } });
    call.mockImplementationOnce(async (operation, payload, options) => ({ result: failing.handle(operation, payload) as never, replayed: false, requestId: options?.requestId ?? 'x' }));
    // La primera consulta de la pantalla es la de sucursales; se hace fallar la de stock directamente.
    call.mockImplementation(async (operation, payload, options) => {
      const source = operation === 'GetStockProjectionQuery' && payloadsOf(call, 'GetStockProjectionQuery').length === 1 ? failing : server;
      return { result: source.handle(operation, payload) as never, replayed: false, requestId: options?.requestId ?? 'x' };
    });
    await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: '/panel/stock', path: '/panel/*' });
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    expect(error).toHaveTextContent('No se pudo cargar la información');
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await table();
  });
});

describe('Stock · ficha lateral', () => {
  it('abre la ficha con el kardex (saldo), las reservas y las existencias por sucursal; queda en la dirección', async () => {
    const { call, location } = await openStock();
    const grid = await table();
    fireEvent.click(within(dataRows(grid)[1]).getByRole('button', { name: /Mouse Logitech G502/ }));
    const panel = await screen.findByRole('dialog', { name: 'Mouse Logitech G502' }, { timeout: 5000 });
    await waitFor(() => expect(location()).toBe('/panel/stock?ficha=MOU-LOG-G502'));
    expect(payloadsOf(call, 'GetProductCardQuery')).toEqual([{ skuOrBarcode: 'MOU-LOG-G502', take: 300 }]);
    const ficha = await within(panel).findByTestId('ficha-producto');
    expect(ficha).toHaveTextContent('Existencias4 u.');
    expect(ficha).toHaveTextContent('Disponible3 u.');
    expect(within(ficha).getByTestId('ficha-posiciones')).toHaveTextContent('CM-A-01 · lote SIN-LOTE4 · reservado 1 · disponible 3');

    fireEvent.click(within(panel).getByRole('tab', { name: /Kardex/ }));
    expect(within(panel).getByTestId('kardex-resumen')).toHaveTextContent('2 movimientos');
    const kardex = within(panel).getByRole('region', { name: 'Kardex de MOU-LOG-G502' });
    expect(dataRows(kardex).map((row) => cells(row))).toEqual([
      ['27/09/202611:30', 'Venta POSCM-A-01 · F-CM-000120 · Diego Flores', '−6', '4'],
      ['20/09/202609:00', 'EntradaCM-A-01 · REM-55 · Bruno MamaniCompra a Distribuidora Andina', '+10', '10'],
    ]);

    fireEvent.click(within(panel).getByRole('tab', { name: 'Reservas' }));
    expect(within(panel).getByTestId('ficha-reservas')).toHaveTextContent('CM-A-01 · lote SIN-LOTE1 u. reservadas');
    expect(within(panel).getByRole('link', { name: 'Ver quién reservó' })).toHaveAttribute('href', '/panel/reservas?q=MOU-LOG-G502');

    fireEvent.click(within(panel).getByRole('tab', { name: 'Por sucursal' }));
    const branchesList = await within(panel).findByTestId('ficha-sucursales');
    expect(branchesList).toHaveTextContent('CM · Casa matriz La Paz4 u.');
    expect(branchesList).toHaveTextContent('CB · Sucursal Cochabamba9 u.');
    expect(branchesList).toHaveTextContent('Total de la empresa13 u.');
    expect(payloadsOf(call, 'ConsolidatedStockQuery')).toEqual([{ search: 'MOU-LOG-G502' }]);

    // El administrador puede registrar entradas, salidas y ajustes y editar el catálogo.
    expect(within(panel).getByRole('link', { name: 'Registrar entrada' })).toHaveAttribute('href', '/panel/movimientos?registrar=ENTRADA&sku=MOU-LOG-G502');
    expect(within(panel).getByRole('link', { name: 'Registrar salida' })).toHaveAttribute('href', '/panel/movimientos?registrar=SALIDA&sku=MOU-LOG-G502');
    expect(within(panel).getByRole('link', { name: 'Registrar ajuste' })).toHaveAttribute('href', '/panel/movimientos?registrar=ajuste&sku=MOU-LOG-G502');

    fireEvent.keyDown(panel, { key: 'Escape' });
    await waitFor(() => expect(location()).toBe('/panel/stock'));
  });

  it('`?ficha=SKU` (desde otro módulo) abre la ficha directamente', async () => {
    await openStock('ADMIN', '?ficha=TEC-RZ-BW');
    const panel = await screen.findByRole('dialog', { name: 'Teclado Razer BlackWidow' }, { timeout: 5000 });
    expect(await within(panel).findByTestId('ficha-producto')).toHaveTextContent('Existencias0 u.');
    fireEvent.click(within(panel).getByRole('tab', { name: 'Reservas' }));
    expect(within(panel).getByText('Sin reservas activas')).toBeInTheDocument();
  });

  it('bodega registra entradas y ajustes pero no salidas; consulta no ve ningún botón de registro', async () => {
    await openStock('BODEGA', '?ficha=MOU-LOG-G502', fakeServer({ visible: ['CM'] }));
    let panel = await screen.findByRole('dialog', { name: 'Mouse Logitech G502' }, { timeout: 5000 });
    await within(panel).findByTestId('ficha-producto');
    expect(within(panel).getByRole('link', { name: 'Registrar entrada' })).toBeInTheDocument();
    expect(within(panel).getByRole('link', { name: 'Registrar ajuste' })).toBeInTheDocument();
    expect(within(panel).queryByRole('link', { name: 'Registrar salida' })).not.toBeInTheDocument();
    // Bodega no maneja reservas: no se le ofrece ver quién reservó.
    fireEvent.click(within(panel).getByRole('tab', { name: 'Reservas' }));
    expect(within(panel).queryByRole('link', { name: 'Ver quién reservó' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Registrar movimiento' })).toHaveAttribute('href', '/panel/movimientos?registrar=1');

    vi.restoreAllMocks();
    document.body.innerHTML = '';
    await openStock('CONSULTA', '?ficha=MOU-LOG-G502', fakeServer({ visible: ['SC'] }));
    panel = await screen.findByRole('dialog', { name: 'Mouse Logitech G502' }, { timeout: 5000 });
    await within(panel).findByTestId('ficha-producto');
    expect(within(panel).queryByRole('link', { name: /^Registrar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Registrar movimiento' })).not.toBeInTheDocument();
  });

  it('las acciones de la fila llevan a «Movimientos» con el producto elegido', async () => {
    const view = await openStock();
    const grid = await table();
    fireEvent.click(within(dataRows(grid)[3]).getByRole('button', { name: /^Acciones de / }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Registrar entrada' }));
    await waitFor(() => expect(view.location()).toBe('/panel/movimientos?registrar=ENTRADA&sku=TEC-RZ-BW'));
  });
});

describe('Stock · exportar y resumen', () => {
  it('exporta las filas filtradas a CSV con el aviso del archivo', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openStock('ADMIN', '?reservas=con');
    const grid = await table();
    await waitFor(() => expect(rowKeys(grid)).toHaveLength(2));
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    expect(screen.getByText(/^Se descargó stock-cm-01-\d{4}-\d{2}-\d{2}\.csv \(2 filas\)\.$/)).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines).toHaveLength(3);
    expect(lines[0]).toContain('"SKU";"Producto";"Categoría";"Marca"');
    expect(lines[1]).toContain('"MON-LG-27";"Monitor LG 27""";"Monitores";"LG"');
  });

  it('el resumen está PLEGADO al entrar y al abrirlo usa lo ya cargado (sin consultas nuevas)', async () => {
    const { call } = await openStock();
    await table();
    expect(screen.queryByTestId('resumen-inventario')).not.toBeInTheDocument();
    const before = call.mock.calls.length;
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /Ver resumen del inventario/ }));
    });
    const summary = await screen.findByTestId('resumen-inventario');
    expect(summary).toHaveTextContent('Valor del inventarioBs 34.400,00');
    expect(summary).toHaveTextContent('En alerta3');
    expect(summary).toHaveTextContent('Con reservas2');
    expect(call.mock.calls.length).toBe(before);
  });
});

describe('Stock · estadísticas del tablero', () => {
  it('«Valor del inventario» y «Productos en alerta» consultan al abrirse y enlazan a su pantalla', async () => {
    const web = await signedInAs('ADMIN');
    spyServer(web, fakeServer());
    await renderPanel(
      <>
        <StockValueStat />
        <StockAlertsStat />
      </>,
      { web: web.services },
    );
    const value = await screen.findByTestId('valor-del-inventario');
    await waitFor(() => expect(value).toHaveTextContent('ValorBs 34.400,00'));
    expect(value).toHaveTextContent('Productos con stock3 de 4');
    expect(within(value).getByRole('link', { name: 'Consultar el stock' })).toHaveAttribute('href', '/panel/stock');
    const alerts = screen.getByTestId('productos-en-alerta');
    await waitFor(() => expect(alerts).toHaveTextContent('Sin stock1'));
    expect(alerts).toHaveTextContent('Bajos1');
    expect(alerts).toHaveTextContent('Exceso o inconsistentes1');
    expect(within(alerts).getByRole('link', { name: 'Ver las 3 alertas' })).toHaveAttribute('href', '/panel/alertas');
  });

  it('si falla muestra su error con «Reintentar»', async () => {
    const web = await signedInAs('ADMIN');
    vi.spyOn(web.backend.rpc, 'call').mockRejectedValue(new WebApiError({ kind: 'server', status: 500, message: 'Falla' }));
    await renderPanel(<StockValueStat />, { web: web.services });
    expect(await screen.findByTestId('estado-error')).toHaveTextContent('No se pudo cargar la información');
  });

  it('la definición ofrece «Consultar stock» y las dos estadísticas, con el permiso de ver stock', () => {
    expect(stockModule).toMatchObject({ key: 'stock', section: 'inventario', order: 10, permissions: { all: ['inventory.stock.view'] } });
    expect(stockModule.actions?.map((action) => action.label)).toEqual(['Consultar stock']);
    expect(stockModule.stats?.map((stat) => stat.title)).toEqual(['Valor del inventario', 'Productos en alerta']);
    expect(REGISTRY.problems).toEqual([]);
  });
});
