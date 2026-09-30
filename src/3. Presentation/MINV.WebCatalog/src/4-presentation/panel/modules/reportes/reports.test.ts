// Módulo «Reportes» · funciones puras: el período (atajos, fechas a mano y sus errores), las filas agrupadas de cada
// reporte, las columnas con sus totales, los filtros de movimientos, el inventario por categoría, tecnología, la
// comparación del mes y los reportes que se ofrecen según los permisos.

import { describe, expect, it } from 'vitest';
import type { RpcResponseOf } from '@/4-presentation/app/contract';
import { CUSTOM_PERIOD, PERIOD_FILTERS, daysBetween, filtersForDates, filtersForPeriodChoice, periodName, periodRange, rangeText, resolvePeriod } from './period';
import {
  categoryRows,
  filterMovements,
  inventorySummary,
  monthComparison,
  movementTypeOptions,
  movementUserOptions,
  purchasesColumns,
  purchasesRows,
  reportOf,
  reportTabs,
  salesColumns,
  salesRowLinks,
  salesRows,
  salesSummary,
  scopeText,
  searchRows,
  seriesPoints,
  signedText,
  techColumns,
  techRows,
  toMovementEntries,
  MOVEMENTS_FILTERS,
  type MovementRecord,
  type SalesData,
  type StockRecord,
  type TechData,
} from './reports';

/** 29/09/2026 a las 11:00 de La Paz. */
const NOW = new Date('2026-09-29T15:00:00Z');

function group(key: string, name: string, amount: number, extra: Partial<SalesData['byProduct'][number]> = {}): SalesData['byProduct'][number] {
  return { key, name, amount, quantity: 0, profit: 0, count: 0, ...extra };
}

const SALES: SalesData = {
  from: '2026-09-01',
  to: '2026-09-03',
  revenue: 1000,
  tax: 130,
  netRevenue: 870,
  cost: 500,
  grossProfit: 370,
  marginPercent: 42.5,
  tickets: 4,
  averageTicket: 250,
  units: 7,
  voided: 1,
  byDay: [
    { date: '2026-09-01', amount: 600, count: 2 },
    { date: '2026-09-02', amount: 0, count: 0 },
    { date: '2026-09-03', amount: 400, count: 2 },
  ],
  byProduct: [group('MOU-01', 'Mouse gamer', 300, { quantity: 3, profit: 90, count: 2 }), group('GPU-01', 'Tarjeta de video', 700, { quantity: 4, profit: 210, count: 3 })],
  byCategory: [group('PER', 'Periféricos', 300, { quantity: 3, profit: 90, count: 2 }), group('GPU', 'Tarjetas de video', 700, { quantity: 4, profit: 210, count: 3 })],
  byCustomer: [group('CLI-1', 'Consumidor final', 1000, { quantity: 7, profit: 300, count: 4 })],
  byCashier: [group('cajero@techzone.example', 'Diego Flores', 1000, { count: 4 })],
  byPaymentMethod: [group('EFE', 'Efectivo', 800, { count: 3 }), group('QR', 'QR', 200, { count: 1 })],
};

describe('Reportes · período', () => {
  it('los atajos se calculan con el día de La Paz', () => {
    expect(periodRange('hoy', NOW)).toEqual({ from: '2026-09-29', to: '2026-09-29' });
    expect(periodRange('ultimos30', NOW)).toEqual({ from: '2026-08-31', to: '2026-09-29' });
    expect(periodRange('esteMes', NOW)).toEqual({ from: '2026-09-01', to: '2026-09-29' });
    expect(periodRange('mesAnterior', NOW)).toEqual({ from: '2026-08-01', to: '2026-08-31' });
    expect(periodRange('esteAnio', NOW)).toEqual({ from: '2026-01-01', to: '2026-09-29' });
    expect(daysBetween('2026-08-31', '2026-09-29')).toBe(30);
  });

  it('sin fechas usa el atajo (uno desconocido vuelve a «Últimos 30 días»); con fechas es «Personalizado»', () => {
    expect(resolvePeriod(PERIOD_FILTERS, NOW)).toEqual({ id: 'ultimos30', from: '2026-08-31', to: '2026-09-29', problem: null });
    expect(resolvePeriod({ ...PERIOD_FILTERS, periodo: 'raro' }, NOW).id).toBe('ultimos30');
    const custom = resolvePeriod({ ...PERIOD_FILTERS, desde: '2026-09-10', hasta: '2026-09-12' }, NOW);
    expect(custom).toEqual({ id: CUSTOM_PERIOD, from: '2026-09-10', to: '2026-09-12', problem: null });
    expect(periodName(custom)).toBe('Personalizado');
    expect(rangeText(custom)).toBe('del 10/09/2026 al 12/09/2026');
    // Una sola fecha: «desde» llega hasta hoy; «hasta» sola es ese día.
    expect(resolvePeriod({ ...PERIOD_FILTERS, desde: '2026-09-20' }, NOW)).toMatchObject({ from: '2026-09-20', to: '2026-09-29' });
    expect(resolvePeriod({ ...PERIOD_FILTERS, hasta: '2026-09-20' }, NOW)).toMatchObject({ from: '2026-09-20', to: '2026-09-20' });
  });

  it('avisa las fechas al revés, inválidas o de más de un año', () => {
    expect(resolvePeriod({ ...PERIOD_FILTERS, desde: '2026-09-12', hasta: '2026-09-10' }, NOW).problem).toBe('La fecha «Desde» no puede ser posterior a «Hasta».');
    expect(resolvePeriod({ ...PERIOD_FILTERS, desde: '2026-02-30' }, NOW).problem).toBe('Escriba una fecha válida.');
    expect(resolvePeriod({ ...PERIOD_FILTERS, desde: '2025-01-01', hasta: '2026-09-29' }, NOW).problem).toBe('Elija un período de hasta un año (366 días).');
  });

  it('elegir un atajo borra las fechas; «Personalizado» deja escritas las que se veían', () => {
    const current = resolvePeriod(PERIOD_FILTERS, NOW);
    expect(filtersForPeriodChoice('esteMes', current)).toEqual({ periodo: 'esteMes', desde: '', hasta: '' });
    expect(filtersForPeriodChoice(CUSTOM_PERIOD, current)).toEqual({ periodo: 'ultimos30', desde: '2026-08-31', hasta: '2026-09-29' });
    expect(filtersForDates({ from: '2026-09-01', to: null })).toEqual({ periodo: 'ultimos30', desde: '2026-09-01', hasta: '' });
  });
});

describe('Reportes · ventas y compras', () => {
  it('agrupa por producto de mayor a menor, con su parte de los ingresos', () => {
    const rows = salesRows(SALES, 'producto');
    expect(rows.map((row) => [row.rank, row.code, row.amount, row.quantity, row.profit, row.share])).toEqual([
      [1, 'GPU-01', 700, 4, 210, 0.7],
      [2, 'MOU-01', 300, 3, 90, 0.3],
    ]);
  });

  it('cajero y medio de pago suman pagos (sin cantidad ni utilidad); por día va en orden de fecha', () => {
    expect(salesRows(SALES, 'pago').map((row) => [row.name, row.quantity, row.profit, row.share])).toEqual([
      ['Efectivo', null, null, 0.8],
      ['QR', null, null, 0.2],
    ]);
    expect(salesRows(SALES, 'dia').map((row) => [row.day, row.name, row.count])).toEqual([
      ['2026-09-01', '01/09/2026', 2],
      ['2026-09-02', '02/09/2026', 0],
      ['2026-09-03', '03/09/2026', 2],
    ]);
  });

  it('las columnas cambian con la agrupación y el pie suma lo que se puede sumar', () => {
    const product = salesColumns('producto');
    expect(product.filter((column) => column.table !== false).map((column) => column.header)).toEqual(['#', 'Producto', 'Cantidad', 'Ventas', 'Utilidad (con IVA)', 'Operaciones', 'Participación']);
    const rows = salesRows(SALES, 'producto');
    const footer = Object.fromEntries(product.map((column) => [column.id, column.footer?.(rows) ?? null]));
    expect(footer).toMatchObject({ cantidad: '7', monto: 'Bs 1.000,00', utilidad: 'Bs 300,00', operaciones: null });
    // Un ticket con dos productos contaría dos veces: por producto no se suman las operaciones; por día, sí.
    const byDay = salesColumns('dia');
    expect(byDay.map((column) => column.header)).toEqual(['#', 'Día', 'Vendido', 'Ventas', 'Participación']);
    expect(byDay.find((column) => column.id === 'operaciones')?.footer?.(salesRows(SALES, 'dia'))).toBe('4');
    expect(salesColumns('pago').map((column) => column.header)).toEqual(['#', 'Medio de pago', 'Cobrado', 'Pagos', 'Participación']);
  });

  it('busca por nombre o código y arma los enlaces a otros módulos', () => {
    expect(searchRows(salesRows(SALES, 'producto'), 'mou').map((row) => row.code)).toEqual(['MOU-01']);
    const period = { from: '2026-09-01', to: '2026-09-03' };
    expect(salesRowLinks(salesRows(SALES, 'producto')[0], 'producto', period).map((link) => link.to)).toEqual([
      '/panel/stock?ficha=GPU-01',
      '/panel/movimientos?producto=GPU-01&desde=2026-09-01&hasta=2026-09-03',
    ]);
    expect(salesRowLinks(salesRows(SALES, 'cliente')[0], 'cliente', period)[0].to).toBe('/panel/ventas?cliente=CLI-1&desde=2026-09-01&hasta=2026-09-03');
    expect(salesRowLinks(salesRows(SALES, 'dia')[2], 'dia', period)[0].to).toBe('/panel/ventas?desde=2026-09-03&hasta=2026-09-03');
  });

  it('los totales del período salen tal cual del servidor, con formato', () => {
    expect(salesSummary(SALES)).toContainEqual({ label: 'Margen', value: '42,5 %' });
    expect(salesSummary(SALES)).toContainEqual({ label: 'Utilidad bruta', value: 'Bs 370,00' });
    expect(salesSummary(SALES)).toContainEqual({ label: 'Ventas anuladas', value: '1' });
  });

  it('compras: por proveedor (con recepciones) o por día (sin)', () => {
    const report: RpcResponseOf<'GetPurchasesReportQuery'> = {
      received: 900,
      receipts: 3,
      openOrders: 1,
      openAmount: 250,
      bySupplier: [group('PRV-2', 'Distribuidora Andina', 300, { count: 1 }), group('PRV-1', 'Importadora Sur', 600, { count: 2 })],
      byDay: [{ date: '2026-09-01', amount: 900, count: 0 }],
    };
    expect(purchasesRows(report, 'proveedor').map((row) => [row.rank, row.name, row.count])).toEqual([
      [1, 'Importadora Sur', 2],
      [2, 'Distribuidora Andina', 1],
    ]);
    expect(purchasesRows(report, 'dia')[0]).toMatchObject({ day: '2026-09-01', amount: 900, count: null, share: 1 });
    expect(purchasesColumns('dia').map((column) => column.header)).toEqual(['#', 'Día', 'Compras recibidas', 'Participación']);
  });

  it('una serie de más de 62 días se agrupa por semana', () => {
    const days = Array.from({ length: 70 }, (_, index) => ({ date: `2026-0${index < 30 ? 7 : 8}-${String((index % 30) + 1).padStart(2, '0')}`, amount: 10 }));
    const points = seriesPoints(days);
    expect(points).toHaveLength(10);
    expect(points[0]).toEqual({ label: 'Semana del 01/07', value: 70 });
    expect(seriesPoints(SALES.byDay).map((point) => point.label)).toEqual(['01/09', '02/09', '03/09']);
  });
});

describe('Reportes · movimientos, inventario y tecnología', () => {
  const records: MovementRecord[] = [
    { date: '2026-09-02', recordedAt: '2026-09-02T14:00:00Z', sku: 'MOU-01', name: 'Mouse gamer', typeCode: 'ENTRADA', typeName: 'Entrada', signed: 10, unit: 'u.', binCode: 'A-01', user: 'Bruno Mamani', document: 'OC-CM-000001', notes: null },
    { date: '2026-09-02', recordedAt: '2026-09-02T15:00:00Z', sku: 'MOU-01', name: 'Mouse gamer', typeCode: 'SALIDA', typeName: 'Salida', signed: -2, unit: 'u.', binCode: 'A-01', user: null, document: null, notes: 'Venta' },
  ];

  it('filtra entradas o salidas, usuario y búsqueda; sin usuario es «Sistema»', () => {
    const entries = toMovementEntries(records);
    expect(filterMovements(entries, { ...MOVEMENTS_FILTERS, flujo: 'entradas' }).map((entry) => entry.record.typeCode)).toEqual(['ENTRADA']);
    expect(filterMovements(entries, { ...MOVEMENTS_FILTERS, usuario: 'Sistema' }).map((entry) => entry.record.typeCode)).toEqual(['SALIDA']);
    expect(filterMovements(entries, { ...MOVEMENTS_FILTERS, q: 'oc-cm' })).toHaveLength(1);
    expect(movementUserOptions(entries).map((option) => option.value)).toEqual(['Bruno Mamani', 'Sistema']);
    expect(signedText(records[0])).toBe('+10 u.');
    expect(signedText(records[1])).toBe('-2 u.');
    // Sin el catálogo de tipos (permiso), la lista sale de lo que trae el reporte.
    expect(movementTypeOptions(undefined, entries)).toEqual([
      { value: 'ENTRADA', label: 'Entrada' },
      { value: 'SALIDA', label: 'Salida' },
    ]);
  });

  it('el inventario se agrupa por categoría (solo activos), con alertas y productos quietos', () => {
    const base: StockRecord = {
      category: 'Periféricos', coverageDays: null, daysWithoutMovement: 3, entries: 0, inventoryValue: 100, isActive: true, issues: 0, lastMovement: null, level: null,
      maximum: 10, minimum: 1, name: 'Mouse', sales30Days: 0, salesRank: null, sku: 'MOU-01', status: 'Optimal', stock: 5, supplier: 'X', unit: 'u.', unitCost: 20, variantId: 'v1',
    };
    const stock: StockRecord[] = [
      base,
      { ...base, sku: 'TEC-01', name: 'Teclado', inventoryValue: 50, status: 'Low', daysWithoutMovement: null, stock: 2 },
      { ...base, sku: 'GPU-01', name: 'GPU', category: 'Tarjetas de video', inventoryValue: 850, status: 'OutOfStock', stock: 0, daysWithoutMovement: 40 },
      { ...base, sku: 'OLD-01', name: 'Viejo', inventoryValue: 999, isActive: false },
    ];
    const rows = categoryRows(stock);
    expect(rows.map((row) => [row.rank, row.name, row.products, row.units, row.value, row.alerts, row.idle])).toEqual([
      [1, 'Tarjetas de video', 1, 0, 850, 1, 1],
      [2, 'Periféricos', 2, 7, 150, 1, 1],
    ]);
    expect(rows[1].share).toBeCloseTo(0.15);
    expect(inventorySummary(stock, 2)).toContainEqual({ label: 'Valor del inventario', value: 'Bs 1.000,00' });
  });

  it('tecnología: listas de ventas (monto) o de conteos (garantías, series)', () => {
    const view: TechData = {
      buildsSold: 1, buildsSoldValue: 9000, claimsOutOfWarranty: 0, openClaims: 3, openClaimsByStatus: [{ name: 'Recibido', count: 2 }, { name: 'En diagnóstico', count: 1 }],
      quotesOpen: 2, quotesValue: 15000, salesByCategory: [{ name: 'Componentes', amount: 800, quantity: 2 }, { name: 'Consolas', amount: 200, quantity: 1 }], salesByPlatform: [],
      serializedWithoutSerials: 0, serialsInStock: 12, serialsInStockByCategory: [], topConsoles: [], topGpus: [], webReservationsActive: 1, webReservationsValue: 5000,
    };
    expect(techRows(view, 'categorias').map((row) => [row.name, row.amount, row.share])).toEqual([
      ['Componentes', 800, 0.8],
      ['Consolas', 200, 0.2],
    ]);
    expect(techRows(view, 'garantias').map((row) => [row.name, row.count, row.amount])).toEqual([
      ['Recibido', 2, null],
      ['En diagnóstico', 1, null],
    ]);
    expect(techColumns('garantias').map((column) => column.header)).toEqual(['#', 'Estado', 'Casos', 'Participación']);
    expect(techColumns('gpu').map((column) => column.header)).toEqual(['#', 'Tarjeta de video', 'Unidades', 'Ventas', 'Participación']);
  });
});

describe('Reportes · pestañas, alcance y estadística', () => {
  it('inventario solo con permiso para ver el stock; sucursales solo si se ven varias', () => {
    expect(reportTabs({ inventory: false, branches: false }).map((tab) => tab.id)).toEqual(['ventas', 'compras', 'movimientos', 'tecnologia']);
    const all = reportTabs({ inventory: true, branches: true });
    expect(all.map((tab) => tab.id)).toEqual(['ventas', 'compras', 'movimientos', 'inventario', 'sucursales', 'tecnologia']);
    expect(reportOf('sucursales', reportTabs({ inventory: true, branches: false }))).toBe('ventas');
    expect(reportOf('compras', all)).toBe('compras');
  });

  it('el alcance: la sucursal activa o la vista consolidada', () => {
    const branches = [{ id: 'b1', code: 'CM', name: 'La Paz' }];
    expect(scopeText({ allBranches: false, activeBranchId: 'b1', branches })).toBe('Sucursal CM · La Paz');
    expect(scopeText({ allBranches: true, activeBranchId: null, branches })).toBe('Todas las sucursales (vista consolidada)');
  });

  it('el mes en curso se compara con el MISMO tramo del mes anterior', () => {
    const previous: SalesData = { ...SALES, revenue: 3000, byDay: [{ date: '2026-08-01', amount: 500, count: 1 }, { date: '2026-08-02', amount: 300, count: 1 }, { date: '2026-08-30', amount: 2200, count: 5 }] };
    const current: SalesData = { ...SALES, revenue: 1000 };
    expect(monthComparison(current, previous, '2026-09-02')).toEqual({ current: 1000, previousSamePeriod: 800, previousMonth: 3000, change: 25 });
    expect(monthComparison(current, { ...previous, byDay: [] }, '2026-09-02').change).toBeNull();
  });
});
