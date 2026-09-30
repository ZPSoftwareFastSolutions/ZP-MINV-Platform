// Módulo «Reportes» dentro del panel real con un registro que solo tiene los módulos de este paquete (Reportes y
// Contabilidad): el reporte de ventas (pedido exacto, totales, agrupación, búsqueda, período con atajos y fechas a mano,
// gráfico plegado, CSV, impresión de TODAS las filas, detalle y enlaces), compras, movimientos (tipo al servidor, la
// guarda del menú «⋯»), inventario, sucursales (solo con varias sucursales), tecnología, el error con «Reintentar», los
// permisos, el tablero y la estadística plegada. El servidor se simula en la prueba: nada toca la red.

import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcOperationName, RpcResponseOf } from '@/4-presentation/app/contract';
import { ROLE_PERMISSIONS, renderPanel, signedInAs, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions, dashboardStats } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import contabilidad from '../contabilidad/module';
import { MonthSalesStat } from './MonthSalesStat';
import reportes from './module';
import { periodRange } from './period';

const REGISTRY = buildRegistry([
  { source: '../modules/reportes/module.tsx', definition: reportes },
  { source: '../modules/contabilidad/module.tsx', definition: contabilidad },
]);

beforeAll(async () => {
  await import('../../shell/PanelApp');
  await import('./ReportsPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

type Sales = RpcResponseOf<'GetSalesReportQuery'>;
type Group = Sales['byProduct'][number];

function group(key: string, name: string, amount: number, patch: Partial<Group> = {}): Group {
  return { key, name, amount, quantity: 1, profit: 10, count: 1, ...patch };
}

/** 30 productos (más de una página de 25) que suman Bs 4.650. */
const PRODUCTS: Group[] = Array.from({ length: 30 }, (_, index) => group(`SKU-${String(index + 1).padStart(2, '0')}`, `Producto ${String(index + 1).padStart(2, '0')}`, 300 - index * 10, { quantity: 2 }));

function salesReport(from: string, to: string, patch: Partial<Sales> = {}): Sales {
  return {
    from,
    to,
    revenue: 4650,
    tax: 604.5,
    netRevenue: 4045.5,
    cost: 2000,
    grossProfit: 2045.5,
    marginPercent: 50.6,
    tickets: 20,
    averageTicket: 232.5,
    units: 60,
    voided: 2,
    byDay: [
      { date: from, amount: 4000, count: 18 },
      { date: to, amount: 650, count: 2 },
    ],
    byProduct: PRODUCTS,
    byCategory: [group('GPU', 'Tarjetas de video', 3000, { quantity: 10 }), group('PER', 'Periféricos', 1650, { quantity: 50 })],
    byCustomer: [group('CLI-0042', 'Juan Pérez', 1000)],
    byCashier: [group('cajero@techzone.example', 'Diego Flores', 4650, { quantity: 0, profit: 0, count: 20 })],
    byPaymentMethod: [group('EFE', 'Efectivo', 4000, { quantity: 0, profit: 0, count: 15 }), group('QR', 'QR', 650, { quantity: 0, profit: 0, count: 5 })],
    ...patch,
  };
}

type Movement = RpcResponseOf<'GetMovementsReportQuery'>[number];

const MOVEMENTS: Movement[] = [
  { date: '2026-09-20', recordedAt: '2026-09-20T15:00:00Z', sku: 'MOU-01', name: 'Mouse gamer', typeCode: 'ENTRADA', typeName: 'Entrada', signed: 10, unit: 'u.', binCode: 'A-01', user: 'Bruno Mamani', document: 'OC-CM-000007', notes: 'Compra' },
  { date: '2026-09-21', recordedAt: '2026-09-21T16:00:00Z', sku: 'TEC-01', name: 'Teclado mecánico', typeCode: 'SALIDA', typeName: 'Salida', signed: -1, unit: 'u.', binCode: 'A-02', user: null, document: 'F-CM-000100', notes: null },
];

interface ServerOptions {
  failSalesOnce?: boolean;
}

function fakeServer({ failSalesOnce = false }: ServerOptions = {}) {
  let failures = failSalesOnce ? 1 : 0;
  const handlers: Partial<Record<RpcOperationName, (payload: never) => unknown>> = {
    GetSalesReportQuery: (payload: { from: string; to: string }) => {
      if (failures > 0) {
        failures -= 1;
        throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
      }
      return salesReport(payload.from, payload.to);
    },
    GetPurchasesReportQuery: (payload: { from: string; to: string }) => ({
      received: 9000,
      receipts: 3,
      openOrders: 2,
      openAmount: 1500,
      bySupplier: [group('PRV-02', 'Distribuidora Andina', 3000, { quantity: 0, profit: 0, count: 1 }), group('PRV-01', 'Importadora Sur', 6000, { quantity: 0, profit: 0, count: 2 })],
      byDay: [{ date: payload.from, amount: 9000, count: 0 }],
    }),
    GetMovementTypesQuery: () => [
      { code: 'ENTRADA', name: 'Entrada', description: null, domain: 'Warehouse', isInitialBalance: false, requiresNotes: false, stockFactor: 1 },
      { code: 'SALIDA', name: 'Salida', description: null, domain: 'Sales', isInitialBalance: false, requiresNotes: false, stockFactor: -1 },
    ],
    GetMovementsReportQuery: (payload: { typeCode?: string | null }) => MOVEMENTS.filter((movement) => !payload.typeCode || movement.typeCode === payload.typeCode),
    GetBranchesQuery: () =>
      ['CM', 'CB'].map((code) => ({ id: code, code, name: code === 'CM' ? 'La Paz' : 'Cochabamba', isActive: true, isVisible: true, warehouses: [`${code}-01`], users: 1, stockValue: null, transfersIn: 0, transfersOut: 0 })),
    GetStockProjectionQuery: (payload: { warehouseCode?: string | null }) => ({
      warehouseCode: payload.warehouseCode ?? 'CM-01',
      today: '2026-09-29',
      result: {
        alerts: [],
        movements: 5,
        order: [],
        stock: [
          { category: 'Periféricos', coverageDays: null, daysWithoutMovement: 2, entries: 0, inventoryValue: 400, isActive: true, issues: 0, lastMovement: null, level: null, maximum: 10, minimum: 1, name: 'Mouse gamer', sales30Days: 0, salesRank: null, sku: 'MOU-01', status: 'Optimal', stock: 20, supplier: 'X', unit: 'u.', unitCost: 20, variantId: 'v1' },
          { category: 'Tarjetas de video', coverageDays: null, daysWithoutMovement: 45, entries: 0, inventoryValue: payload.warehouseCode === 'CB-01' ? 100 : 1600, isActive: true, issues: 0, lastMovement: null, level: null, maximum: 10, minimum: 1, name: 'GPU', sales30Days: 0, salesRank: null, sku: 'GPU-01', status: 'Low', stock: 2, supplier: 'X', unit: 'u.', unitCost: 800, variantId: 'v2' },
        ],
      },
    }),
    GetBranchReportQuery: (payload: { from: string; to: string }) => ({
      from: payload.from,
      to: payload.to,
      branches: [
        { code: 'CM', name: 'La Paz', tickets: 12, revenue: 3000, tax: 390, averageTicket: 250, stockValue: 50000, sharePercent: 60 },
        { code: 'CB', name: 'Cochabamba', tickets: 8, revenue: 2000, tax: 260, averageTicket: 250, stockValue: 30000, sharePercent: 40 },
      ],
      days: [{ day: payload.from, revenueByBranch: [3000, 2000] }],
      totalRevenue: 5000,
      totalStockValue: 80000,
      inTransitValue: 1200,
      refreshedAt: '2026-09-29T12:00:00Z',
    }),
    GetTechDashboardQuery: () => ({
      buildsSold: 2, buildsSoldValue: 18000, claimsOutOfWarranty: 1, openClaims: 3, openClaimsByStatus: [{ name: 'Recibido', count: 2 }, { name: 'En diagnóstico', count: 1 }],
      quotesOpen: 4, quotesValue: 32000, salesByCategory: [{ name: 'Componentes', amount: 8000, quantity: 5 }, { name: 'Consolas', amount: 2000, quantity: 1 }], salesByPlatform: [{ name: 'PS5', amount: 2000, quantity: 1 }],
      serializedWithoutSerials: 0, serialsInStock: 12, serialsInStockByCategory: [{ name: 'Componentes', count: 12 }], topConsoles: [], topGpus: [{ name: 'RTX 4060', amount: 5000, quantity: 2 }],
      webReservationsActive: 1, webReservationsValue: 7000,
    }),
  };
  return handlers;
}

async function openReports(role: StaffRole = 'GERENCIA', query = '', options: ServerOptions = {}) {
  const web = await signedInAs(role);
  const handlers = fakeServer(options);
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const call = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, sendOptions) => {
    const handler = handlers[operation];
    if (!handler) return real(operation, payload, sendOptions);
    return { result: handler(payload as never) as never, replayed: false, requestId: sendOptions?.requestId ?? 'prueba' };
  });
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/reportes${query}`, path: '/panel/*' });
  return { web, call, location: view.location };
}

function requests(call: { mock: { calls: unknown[][] } }, operation: RpcOperationName): unknown[] {
  return call.mock.calls.filter((args) => args[0] === operation).map((args) => args[1]);
}

async function table(name: string): Promise<HTMLElement> {
  const region = await screen.findByRole('region', { name }, { timeout: 5000 });
  await waitFor(() => expect(within(region).queryAllByRole('row').length).toBeGreaterThan(1));
  return region;
}

function dataRows(region: HTMLElement): HTMLElement[] {
  return within(region)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

describe('Reportes · ventas', () => {
  it('pide los últimos 30 días, muestra los totales del servidor y la tabla con su pie; el gráfico está plegado', async () => {
    const { call } = await openReports();
    expect(await screen.findByRole('heading', { level: 1, name: 'Reportes' }, { timeout: 5000 })).toBeInTheDocument();
    const grid = await table('Ventas por producto');
    expect(requests(call, 'GetSalesReportQuery')).toEqual([periodRange('ultimos30')]);

    const totals = screen.getByTestId('ventas-totales');
    expect(totals).toHaveTextContent('Ingresos (con IVA)Bs 4.650,00');
    expect(totals).toHaveTextContent('Utilidad brutaBs 2.045,50');
    expect(totals).toHaveTextContent('Margen50,6 %');
    expect(totals).toHaveTextContent('Ventas anuladas2');

    expect(dataRows(grid)).toHaveLength(25);
    expect(dataRows(grid)[0]).toHaveTextContent('1Producto 01SKU-012Bs 300,00Bs 10,001');
    expect(within(grid).getByTestId('tabla-totales')).toHaveTextContent('Bs 4.650,00');
    expect(screen.getByText('30 de 30 filas · por producto')).toBeInTheDocument();

    // Nada de gráficos al entrar: se ven recién al abrir «Ver gráfico».
    expect(screen.queryByTestId('ventas-grafico')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Ver gráfico/ }));
    expect(await screen.findByTestId('ventas-grafico')).toHaveTextContent('Medios de pago');
  });

  it('agrupa por categoría y busca (en la dirección)', async () => {
    const { location } = await openReports();
    await table('Ventas por producto');
    fireEvent.change(screen.getByRole('combobox', { name: 'Agrupar' }), { target: { value: 'categoria' } });
    await waitFor(() => expect(location()).toBe('/panel/reportes?agrupar=categoria'));
    const grid = await table('Ventas por categoría');
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['GPU', 'PER']);

    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: 'perifericos' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['PER']));
    expect(location()).toBe('/panel/reportes?agrupar=categoria&q=perifericos');
    expect(screen.getByTestId('filtros-activos')).toHaveTextContent('2 activos');
  });

  it('el período: un atajo vuelve a pedir; fechas al revés avisan y no piden; fechas a mano piden ese rango', async () => {
    const { call, location } = await openReports();
    await table('Ventas por producto');
    fireEvent.change(screen.getByRole('combobox', { name: 'Período' }), { target: { value: 'mesAnterior' } });
    await waitFor(() => expect(requests(call, 'GetSalesReportQuery')).toEqual([periodRange('ultimos30'), periodRange('mesAnterior')]));
    expect(location()).toBe('/panel/reportes?periodo=mesAnterior');

    const previous = periodRange('mesAnterior');
    // «Desde» (hoy) después de «Hasta» (fin del mes anterior): se avisa y no se consulta.
    fireEvent.change(screen.getByLabelText('Desde'), { target: { value: periodRange('hoy').from } });
    expect(await screen.findByText('Revise las fechas')).toBeInTheDocument();
    expect(requests(call, 'GetSalesReportQuery')).toHaveLength(2);

    fireEvent.change(screen.getByLabelText('Desde'), { target: { value: previous.from } });
    fireEvent.change(screen.getByLabelText('Hasta'), { target: { value: previous.from } });
    await waitFor(() => expect(requests(call, 'GetSalesReportQuery').at(-1)).toEqual({ from: previous.from, to: previous.from }));
    expect(location()).toBe(`/panel/reportes?desde=${previous.from}&hasta=${previous.from}`);
    expect(screen.getByRole('combobox', { name: 'Período' })).toHaveValue('personalizado');
  });

  it('exporta a CSV las filas filtradas con sus columnas (y el código del producto)', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openReports();
    await table('Ventas por producto');
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText(/^Se descargó reporte-de-ventas-por-producto-.+\.csv \(30 filas\)\.$/)).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toBe('"Producto";"Código";"Cantidad";"Ventas";"Utilidad (con IVA)";"Operaciones";"Participación"');
    expect(lines[1]).toBe('"Producto 01";"SKU-01";2;300;10;1;6,5');
    expect(lines).toHaveLength(31);
  });

  it('imprime la hoja con TODAS las filas (no solo la página) y la quita al terminar', async () => {
    let printed: { rows: number; text: string } | null = null;
    vi.spyOn(window, 'print').mockImplementation(() => {
      const sheet = document.querySelector('[data-testid="reporte-impreso"]');
      printed = { rows: sheet?.querySelectorAll('tbody tr').length ?? 0, text: sheet?.textContent ?? '' };
    });
    await openReports();
    await table('Ventas por producto');
    fireEvent.click(screen.getByRole('button', { name: 'Imprimir' }));
    expect(printed).not.toBeNull();
    expect(printed!.rows).toBe(30);
    expect(printed!.text).toContain('Reporte de ventas por producto');
    expect(printed!.text).toContain('Tech Zone Gaming');
    expect(printed!.text).toContain('Todas las sucursales (vista consolidada)');
    expect(document.querySelector('[data-testid="reporte-impreso"]')).toBeNull();
  });

  it('el detalle lateral de una fila, con los enlaces a Stock y Movimientos', async () => {
    await openReports('ADMIN');
    const grid = await table('Ventas por producto');
    fireEvent.click(within(dataRows(grid)[1]).getByRole('button', { name: /^Producto 02/ }));
    const panel = await screen.findByRole('dialog', { name: 'Producto 02' });
    expect(within(panel).getByTestId('ventas-detalle')).toHaveTextContent('Puesto2 de 30');
    expect(within(panel).getByRole('link', { name: 'Ver ficha y kardex del producto' })).toHaveAttribute('href', '/panel/stock?ficha=SKU-02');
    expect(within(panel).getByRole('link', { name: 'Ver los movimientos del producto' }).getAttribute('href')).toMatch(/^\/panel\/movimientos\?producto=SKU-02&desde=/);
  });

  it('si el reporte falla muestra el error con «Reintentar» y se recupera', async () => {
    await openReports('GERENCIA', '', { failSalesOnce: true });
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    expect(error).toHaveTextContent('No se pudo conectar con el servidor');
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await table('Ventas por producto');
  });
});

describe('Reportes · otras pestañas', () => {
  it('compras por proveedor y por día; cambiar de pestaña conserva el período y olvida los demás filtros', async () => {
    const { call, location } = await openReports('GERENCIA', '?periodo=esteMes&agrupar=categoria');
    await table('Ventas por categoría');
    fireEvent.click(screen.getByRole('tab', { name: 'Compras' }));
    await waitFor(() => expect(location()).toBe('/panel/reportes?reporte=compras&periodo=esteMes'));
    const grid = await table('Compras por proveedor');
    expect(requests(call, 'GetPurchasesReportQuery')).toEqual([periodRange('esteMes')]);
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['PRV-01', 'PRV-02']);
    expect(screen.getByTestId('compras-totales')).toHaveTextContent('Por recibir (órdenes abiertas)Bs 1.500,00');
    fireEvent.change(screen.getByRole('combobox', { name: 'Agrupar' }), { target: { value: 'dia' } });
    expect(await table('Compras por día')).toBeInTheDocument();
  });

  it('movimientos: el tipo lo filtra el servidor; «Ver solo este producto» del menú no abre el detalle', async () => {
    const { call, location } = await openReports('BODEGA', '?reporte=movimientos');
    const grid = await table('Movimientos del período');
    expect(requests(call, 'GetMovementsReportQuery')).toEqual([{ ...periodRange('ultimos30'), typeCode: null }]);
    expect(screen.getByTestId('movimientos-totales')).toHaveTextContent('Entradas1 (10 unidades)');
    expect(dataRows(grid).map((row) => row.textContent)).toEqual([expect.stringContaining('-1 u.'), expect.stringContaining('+10 u.')]);

    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: 'SALIDA' } });
    await waitFor(() => expect(requests(call, 'GetMovementsReportQuery').at(-1)).toEqual({ ...periodRange('ultimos30'), typeCode: 'SALIDA' }));
    await waitFor(() => expect(dataRows(grid)).toHaveLength(1));
    expect(dataRows(grid)[0]).toHaveTextContent('Sistema');

    fireEvent.click(within(dataRows(grid)[0]).getByRole('button', { name: /^Acciones de / }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Ver solo este producto' }));
    await waitFor(() => expect(location()).toBe('/panel/reportes?reporte=movimientos&tipo=SALIDA&q=TEC-01'));
    // El clic del menú no se propaga a la fila: el detalle NO se abre.
    expect(screen.queryByTestId('movimiento-detalle')).not.toBeInTheDocument();

    fireEvent.click(within(dataRows(grid)[0]).getByRole('button', { name: /^Teclado mecánico/ }));
    expect(await screen.findByTestId('movimiento-detalle')).toHaveTextContent('F-CM-000100');
  });

  it('inventario por categoría (de hoy) y por sucursal', async () => {
    const { call } = await openReports('BODEGA', '?reporte=inventario');
    const grid = await table('Inventario por categoría');
    expect(requests(call, 'GetStockProjectionQuery')).toEqual([{ warehouseCode: null }]);
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['Tarjetas de video', 'Periféricos']);
    expect(screen.getByTestId('inventario-totales')).toHaveTextContent('Valor del inventarioBs 2.000,00');
    fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal' }), { target: { value: 'CB-01' } });
    await waitFor(() => expect(requests(call, 'GetStockProjectionQuery').at(-1)).toEqual({ warehouseCode: 'CB-01' }));
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['Periféricos', 'Tarjetas de video']));
  });

  it('sucursales: solo con varias sucursales; muestra de cuándo son los datos', async () => {
    const { call } = await openReports('GERENCIA', '?reporte=sucursales');
    const grid = await table('Reporte por sucursal');
    expect(requests(call, 'GetBranchReportQuery')).toEqual([periodRange('ultimos30')]);
    expect(within(grid).getByTestId('tabla-totales')).toHaveTextContent('Bs 5.000,00');
    expect(screen.getByTestId('sucursales-totales')).toHaveTextContent('Mercadería en tránsitoBs 1.200,00');
    expect(screen.getByTestId('sucursales-totales')).toHaveTextContent('Datos al29/09/2026 08:00');
  });

  it('bodega (una sola sucursal) no ve «Sucursales»; sin permiso de stock no hay «Inventario»', async () => {
    const { location } = await openReports('BODEGA', '?reporte=sucursales');
    await table('Ventas por producto');
    expect(screen.queryByRole('tab', { name: 'Sucursales' })).not.toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Inventario' })).toBeInTheDocument();
    expect(location()).toBe('/panel/reportes?reporte=sucursales');
  });

  it('tecnología: los últimos N días hasta hoy y la lista elegida', async () => {
    const { call } = await openReports('GERENCIA', '?reporte=tecnologia');
    const grid = await table('Ventas por categoría');
    expect(requests(call, 'GetTechDashboardQuery')).toEqual([{ days: 30, gpuCategoryCode: 'GPU', consoleCategoryCode: 'CON' }]);
    expect(dataRows(grid)).toHaveLength(2);
    fireEvent.change(screen.getByRole('combobox', { name: 'Período' }), { target: { value: '90' } });
    await waitFor(() => expect(requests(call, 'GetTechDashboardQuery').at(-1)).toEqual({ days: 90, gpuCategoryCode: 'GPU', consoleCategoryCode: 'CON' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Ver' }), { target: { value: 'garantias' } });
    const claims = await table('Garantías abiertas por estado');
    expect(dataRows(claims)[0]).toHaveTextContent('Recibido2');
    expect(screen.getByRole('link', { name: 'Ver las garantías' })).toHaveAttribute('href', '/panel/garantias');
  });
});

describe('Reportes · permisos, tablero y estadística', () => {
  it('las definiciones de Reportes y Contabilidad son válidas (sección Análisis, en orden)', () => {
    expect(REGISTRY.problems).toEqual([]);
    expect(REGISTRY.modules.map((module) => `${module.section}/${module.key}`)).toEqual(['analisis/reportes', 'analisis/contabilidad']);
  });

  it('un rol sin `reports.view` ve «No tiene acceso a esta pantalla»', async () => {
    await openReports('CAJERO');
    expect(await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.queryByTestId('tabla')).not.toBeInTheDocument();
  });

  it('ofrece al tablero «Reporte de ventas» y «Reporte de compras» y la estadística plegada, solo a quien ve reportes', () => {
    const actions = (role: StaffRole) => dashboardActions(REGISTRY, ROLE_PERMISSIONS[role]).flatMap((item) => item.actions.map((entry) => `${entry.action.label} → ${entry.to}`));
    expect(actions('CONSULTA')).toEqual(['Reporte de ventas → /panel/reportes', 'Reporte de compras → /panel/reportes?reporte=compras']);
    expect(actions('CAJERO')).toEqual([]);
    expect(dashboardStats(REGISTRY, ROLE_PERMISSIONS.CONSULTA).map((item) => item.stat.title)).toEqual(['Ventas del mes vs mes anterior']);
  });

  it('«Ventas del mes vs mes anterior» compara con el mismo tramo del mes anterior', async () => {
    const web = await signedInAs('GERENCIA');
    const current = periodRange('esteMes');
    const previous = periodRange('mesAnterior');
    const call = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
      if (operation !== 'GetSalesReportQuery') throw new Error(`Operación no simulada: ${operation}`);
      const range = payload as { from: string; to: string };
      const report =
        range.from === current.from
          ? salesReport(range.from, range.to, { revenue: 1200, tickets: 6, grossProfit: 400 })
          : salesReport(range.from, range.to, { revenue: 5000, byDay: [{ date: previous.from, amount: 1000, count: 3 }, { date: previous.to, amount: 4000, count: 9 }] });
      return { result: report as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
    });
    await renderPanel(<MonthSalesStat />, { web: web.services, route: '/panel', path: '/panel/*' });
    const stat = await screen.findByTestId('ventas-del-mes', undefined, { timeout: 5000 });
    await waitFor(() => expect(stat).toHaveTextContent('Bs 1.200,00'));
    expect(call.mock.calls.map(([, payload]) => payload)).toEqual([current, previous]);
    // Del 1 al mismo día del mes anterior: 1.000 (el último día del mes anterior no entra salvo a fin de mes).
    const day = Number(current.to.slice(8, 10));
    const same = day >= Number(previous.to.slice(8, 10)) ? 5000 : 1000;
    expect(stat).toHaveTextContent(`Mes completo: Bs 5.000,00`);
    expect(stat).toHaveTextContent(same === 1000 ? 'Bs 1.000,00' : 'Bs 5.000,00');
    expect(screen.getByRole('link', { name: 'Ver el reporte de ventas del mes' })).toHaveAttribute('href', '/panel/reportes?periodo=esteMes');
  });
});
