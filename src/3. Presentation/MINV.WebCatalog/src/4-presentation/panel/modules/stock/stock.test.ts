// Funciones puras del módulo «Stock»: filas con reservado y disponible, marca, «sin rotación» (regla de la V1), filtros,
// opciones de las listas desplegables (sucursales visibles y consolidado), textos, kardex, enlaces, CSV y resúmenes.

import { describe, expect, it } from 'vitest';
import { buildCsv } from '@/4-presentation/panel/lib';
import {
  ALL_BRANCHES,
  NO_BRAND,
  STOCK_CSV,
  STOCK_FILTERS,
  activeWarehouseLabel,
  alertCounts,
  brandBySku,
  brandOptions,
  categoryOptions,
  consolidatedRowOf,
  coverageText,
  filterConsolidated,
  filterStock,
  inventorySummary,
  isWithoutRotation,
  kardexCountText,
  lastMovementText,
  minMaxText,
  movementLink,
  reservedBySku,
  sentenceCase,
  signedText,
  statusLabel,
  statusPriority,
  toConsolidatedEntries,
  toKardexEntries,
  toStockEntries,
  warehouseOptions,
  type BranchRecord,
  type ConsolidatedData,
  type StockAlertRecord,
  type StockRecord,
} from './stock';

function row(sku: string, patch: Partial<StockRecord> = {}): StockRecord {
  return {
    variantId: `v-${sku}`,
    sku,
    name: `Producto ${sku}`,
    category: 'Periféricos',
    supplier: 'Distribuidora Andina S.R.L.',
    unit: 'u.',
    isActive: true,
    entries: 10,
    issues: 2,
    stock: 8,
    minimum: 3,
    maximum: 20,
    level: 0.4,
    status: 'Optimal',
    unitCost: 100,
    inventoryValue: 800,
    lastMovement: '2026-09-20',
    daysWithoutMovement: 9,
    sales30Days: 6,
    coverageDays: 40,
    salesRank: 3,
    ...patch,
  };
}

const ROWS: StockRecord[] = [
  row('MOU-01', { name: 'Mouse gamer', status: 'Low', stock: 4 }),
  row('TEC-01', { name: 'Teclado mecánico', category: 'Teclados', status: 'OutOfStock', stock: 0, inventoryValue: 0 }),
  row('MON-01', { name: 'Monitor 27', category: 'Monitores', daysWithoutMovement: 90, lastMovement: '2026-07-01', inventoryValue: 5000 }),
  row('OLD-01', { name: 'Producto viejo', isActive: false, status: 'Inactive', daysWithoutMovement: 200 }),
];

describe('Stock · filas y filtros', () => {
  it('suma el reservado por SKU (sin distinguir mayúsculas) y el disponible nunca es negativo', () => {
    const reserved = reservedBySku([
      { sku: 'mou-01', reserved: 3 },
      { sku: 'MOU-01', reserved: 3 },
    ]);
    const entries = toStockEntries(ROWS, reserved, new Map(), null);
    expect(entries[0]).toMatchObject({ key: 'MOU-01', reserved: 6, available: 0 });
    expect(entries[2]).toMatchObject({ reserved: 0, available: 8 });
  });

  it('«sin rotación» es la regla de la V1: activo, con stock y con el último movimiento hace MÁS días que los de la empresa', () => {
    expect(isWithoutRotation(ROWS[2], 60)).toBe(true);
    expect(isWithoutRotation(ROWS[2], 90)).toBe(false);
    expect(isWithoutRotation(ROWS[3], 60)).toBe(false); // inactivo
    expect(isWithoutRotation(row('X', { stock: 0, daysWithoutMovement: 300 }), 60)).toBe(false); // sin stock
    expect(isWithoutRotation(row('X', { daysWithoutMovement: null, lastMovement: null }), 60)).toBe(false); // sin movimientos
    expect(isWithoutRotation(ROWS[2], null)).toBe(false); // sin la configuración
  });

  it('filtra por semáforo, categoría, marca, reservas, rotación y búsqueda sin acentos', () => {
    const brands = brandBySku([
      { sku: 'MOU-01', brand: 'Logitech' },
      { sku: 'MON-01', brand: ' ' },
    ] as never);
    const entries = toStockEntries(ROWS, reservedBySku([{ sku: 'MON-01', reserved: 1 }]), brands, 60);
    const skus = (filters: Partial<typeof STOCK_FILTERS>) => filterStock(entries, { ...STOCK_FILTERS, ...filters }).map((entry) => entry.key);
    expect(skus({})).toEqual(['MOU-01', 'TEC-01', 'MON-01', 'OLD-01']);
    expect(skus({ estado: 'OutOfStock' })).toEqual(['TEC-01']);
    expect(skus({ categoria: 'Monitores' })).toEqual(['MON-01']);
    expect(skus({ marca: 'Logitech' })).toEqual(['MOU-01']);
    expect(skus({ marca: NO_BRAND })).toEqual(['TEC-01', 'MON-01', 'OLD-01']);
    expect(skus({ reservas: 'con' })).toEqual(['MON-01']);
    expect(skus({ rotacion: 'sin' })).toEqual(['MON-01']);
    expect(skus({ q: 'teclado mecanico' })).toEqual(['TEC-01']);
    expect(skus({ q: 'logitech' })).toEqual(['MOU-01']);
  });

  it('el consolidado filtra por búsqueda, categoría y marca, y encuentra la fila EXACTA de un SKU', () => {
    const data: ConsolidatedData = {
      branches: [{ id: '1', code: 'CM', name: 'Casa matriz' }],
      rows: [
        { sku: 'MOU-01', name: 'Mouse gamer', category: 'Periféricos', unit: 'u.', byBranch: [4], inTransit: 1, total: 5, value: 500 },
        { sku: 'MOU-01-XL', name: 'Mouse gamer XL', category: 'Periféricos', unit: 'u.', byBranch: [2], inTransit: 0, total: 2, value: 300 },
      ],
      valueByBranch: [700],
      inTransitValue: 100,
      totalValue: 800,
    };
    const entries = toConsolidatedEntries(data.rows, new Map([['MOU-01-XL', 'Razer']]));
    expect(filterConsolidated(entries, { ...STOCK_FILTERS, marca: 'Razer' }).map((entry) => entry.key)).toEqual(['MOU-01-XL']);
    expect(filterConsolidated(entries, { ...STOCK_FILTERS, q: 'xl' }).map((entry) => entry.key)).toEqual(['MOU-01-XL']);
    expect(consolidatedRowOf(data, 'mou-01')?.total).toBe(5);
    expect(consolidatedRowOf(data, 'NO-EXISTE')).toBeNull();
  });
});

describe('Stock · opciones de las listas desplegables', () => {
  const branch = (code: string, patch: Partial<BranchRecord> = {}): BranchRecord => ({
    id: code,
    code,
    name: `Sucursal ${code}`,
    isActive: true,
    isVisible: true,
    warehouses: [`${code}-01`],
    users: 1,
    stockValue: 0,
    transfersIn: 0,
    transfersOut: 0,
    ...patch,
  });

  it('ofrece un almacén por sucursal VISIBLE y activa, y el consolidado solo si ve más de una', () => {
    const branches = [branch('CM', { warehouses: ['CM-01', 'CM-02'] }), branch('CB'), branch('SC', { isVisible: false }), branch('TJ', { isActive: false })];
    expect(warehouseOptions(branches)).toEqual([
      { value: 'CM-01', label: 'CM · Sucursal CM (CM-01)' },
      { value: 'CM-02', label: 'CM · Sucursal CM (CM-02)' },
      { value: 'CB-01', label: 'CB · Sucursal CB' },
      { value: ALL_BRANCHES, label: 'Todas las sucursales (consolidado)' },
    ]);
    expect(warehouseOptions([branch('CM')])).toEqual([{ value: 'CM-01', label: 'CM · Sucursal CM' }]);
    expect(activeWarehouseLabel({ code: 'CM', name: 'Casa matriz' })).toBe('Sucursal activa (CM)');
    expect(activeWarehouseLabel(null)).toBe('Almacén principal de la empresa');
  });

  it('categorías y marcas ordenadas y sin repetir; «Sin marca» si falta alguna', () => {
    expect(categoryOptions(['Teclados', 'Monitores', 'Teclados', ' '])).toEqual([
      { value: 'Monitores', label: 'Monitores' },
      { value: 'Teclados', label: 'Teclados' },
    ]);
    expect(brandOptions(['Razer', null, 'Logitech', 'Razer'])).toEqual([
      { value: 'Logitech', label: 'Logitech' },
      { value: 'Razer', label: 'Razer' },
      { value: NO_BRAND, label: 'Sin marca' },
    ]);
    expect(brandOptions([null, null])).toEqual([]);
  });
});

describe('Stock · textos', () => {
  it('semáforo en palabras y su prioridad (lo urgente primero)', () => {
    expect(statusLabel('OutOfStock')).toBe('Sin stock');
    expect(statusLabel('Overstock')).toBe('Exceso');
    expect(statusLabel('Desconocido')).toBe('Desconocido');
    expect(statusPriority('Inconsistent')).toBeLessThan(statusPriority('OutOfStock'));
    expect(statusPriority('Low')).toBeLessThan(statusPriority('Optimal'));
    expect(statusPriority('Otro')).toBe(99);
  });

  it('cobertura, último movimiento, mínimo y máximo, signo y tipo oración como el escritorio', () => {
    expect([coverageText(null), coverageText(0), coverageText(1), coverageText(45), coverageText(1200)]).toEqual(['—', '0 días', '1 día', '45 días', '+999 días']);
    expect(lastMovementText({ lastMovement: null, daysWithoutMovement: null })).toBe('Sin movimientos');
    expect(lastMovementText({ lastMovement: '2026-09-29', daysWithoutMovement: 0 })).toBe('Hoy');
    expect(lastMovementText({ lastMovement: '2026-09-28', daysWithoutMovement: 1 })).toBe('Ayer');
    expect(lastMovementText({ lastMovement: '2026-09-01', daysWithoutMovement: 28 })).toBe('Hace 28 días');
    expect(minMaxText(0, 0)).toBe('—');
    expect(minMaxText(3, 1500)).toBe('3 / 1.500');
    expect([signedText(5), signedText(-2.5), signedText(0)]).toEqual(['+5', '−2,5', '0']);
    expect(sentenceCase('DEVOLUCIÓN DE CLIENTE')).toBe('Devolución de cliente');
    expect(sentenceCase('VENTA POS')).toBe('Venta POS');
  });

  it('kardex: cuántos movimientos se ven y claves estables', () => {
    const line = { recordedAt: '2026-09-29T14:00:00Z', businessDate: '2026-09-29', typeCode: 'ENTRADA', typeName: 'ENTRADA', signed: 5, balance: 5, binCode: 'A-01', document: null, notes: null, userName: null };
    expect(kardexCountText({ movements: [], totalMovements: 0 })).toBe('Sin movimientos');
    expect(kardexCountText({ movements: [line], totalMovements: 1 })).toBe('1 movimiento');
    expect(kardexCountText({ movements: [line, line], totalMovements: 2 })).toBe('2 movimientos');
    expect(kardexCountText({ movements: [line], totalMovements: 1204 })).toBe('Últimos 1 de 1.204 movimientos');
    expect(toKardexEntries([line, line]).map((entry) => entry.key)).toEqual(['0', '1']);
  });

  it('enlace a «Movimientos» con el diálogo de registro abierto', () => {
    expect(movementLink('ENTRADA', 'MOU-01')).toBe('/panel/movimientos?registrar=ENTRADA&sku=MOU-01');
    expect(movementLink('1')).toBe('/panel/movimientos?registrar=1');
  });
});

describe('Stock · CSV y resúmenes', () => {
  it('el CSV lleva reservado, disponible, semáforo en palabras y «sin rotación»', () => {
    const entries = toStockEntries([ROWS[2]], reservedBySku([{ sku: 'MON-01', reserved: 2 }]), new Map([['MON-01', 'LG']]), 60);
    const lines = buildCsv(STOCK_CSV, entries, { bom: false }).trim().split('\r\n');
    expect(lines[0]).toBe(
      '"SKU";"Producto";"Categoría";"Marca";"Proveedor";"Unidad";"Existencias";"Reservado";"Disponible";"Mínimo";"Máximo";"Semáforo";"Salidas 30 días";"Cobertura (días)";"Costo unitario";"Valor";"Último movimiento";"Sin rotación"',
    );
    expect(lines[1]).toBe('"MON-01";"Monitor 27";"Monitores";"LG";"Distribuidora Andina S.R.L.";"u.";8;2;6;3;20;"Normal";6;40;100;5000;"01/07/2026";"Sí"');
  });

  it('resumen del inventario y de las alertas', () => {
    const summary = inventorySummary(ROWS);
    expect(summary).toMatchObject({ value: 6600, products: 4, active: 3, withStock: 2 });
    expect(summary.byCategory[0]).toEqual({ label: 'Monitores', value: 5000 });
    const alert = (status: string) => ({ status }) as StockAlertRecord;
    const counts = alertCounts([alert('OutOfStock'), alert('Critical'), alert('Low'), alert('Low'), alert('Overstock'), alert('Inconsistent')]);
    expect(counts).toMatchObject({ total: 6, outOfStock: 1, critical: 1, low: 2, other: 2 });
    expect(counts.byStatus.map((item) => `${item.label} ${item.value}`)).toEqual(['Inconsistente 1', 'Sin stock 1', 'Crítico 1', 'Bajo 2', 'Exceso 1']);
  });
});
