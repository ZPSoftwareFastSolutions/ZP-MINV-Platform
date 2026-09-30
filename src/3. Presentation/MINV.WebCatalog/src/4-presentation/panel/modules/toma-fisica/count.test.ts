// Módulo «Toma física» · pruebas de las funciones puras (sin React ni red).

import { describe, expect, it } from 'vitest';
import {
  COUNT_FILTERS,
  countProgress,
  countSummary,
  counterOptions,
  defaultBin,
  differenceText,
  filterEntries,
  findByCode,
  kindOf,
  pendingProducts,
  plainMessage,
  printRows,
  systemAt,
  toCountEntries,
  toOpenCount,
  toRecordCount,
  toRemoveCount,
  warehouseOptions,
  type BranchRecord,
  type CardRecord,
  type CountLineRecord,
  type CountSheetRecord,
  type LookupRecord,
} from './count';

function item(sku: string, overrides: Partial<LookupRecord> = {}): LookupRecord {
  return { sku, name: sku, category: 'Monitores', unit: 'UND', allowsDecimals: false, barcodes: [], isActive: true, primaryBin: 'CM-A1', variantId: `v-${sku}`, ...overrides };
}

function line(sku: string, counted: number, system: number, overrides: Partial<CountLineRecord> = {}): CountLineRecord {
  return {
    sku,
    name: sku,
    unit: 'UND',
    binCode: 'CM-A1',
    lotNumber: 'SIN-LOTE',
    systemQuantity: system,
    countedQuantity: counted,
    difference: counted - system,
    countedBy: 'Bruno Mamani',
    countedAt: '2026-09-29T14:00:00Z',
    ...overrides,
  };
}

const PRODUCTS = [
  item('MON-1', { name: 'Monitor LG', barcodes: ['7790001'] }),
  item('MON-2', { name: 'Monitor AOC', primaryBin: 'CM-A2' }),
  item('TEC-1', { name: 'Teclado', category: 'Periféricos', primaryBin: null }),
];

const SHEET: CountSheetRecord = {
  id: 'toma-1',
  number: 'CF-20260929-1',
  countDate: '2026-09-29',
  warehouseCode: 'CM-PRINCIPAL',
  notes: null,
  lines: [
    line('MON-1', 7, 5),
    line('TEC-1', 2, 4, { binCode: 'CM-B1', countedBy: 'Andrea Quiroga' }),
    line('MON-1', 1, 1, { binCode: 'CM-A2', lotNumber: 'L-2026' }),
  ],
};

describe('Toma física · diferencias y planilla', () => {
  it('la diferencia en palabras y su tipo', () => {
    expect(kindOf(2)).toBe('sobrante');
    expect(kindOf(-1)).toBe('faltante');
    expect(kindOf(0)).toBe('cuadra');
    expect(differenceText(2, 'UND')).toBe('+2 UND');
    expect(differenceText(-1.5)).toBe('−1,5');
    expect(differenceText(0, 'UND')).toBe('Cuadra');
  });

  it('las líneas llevan la categoría del producto y se filtran por categoría, ubicación, diferencia, quién contó y búsqueda', () => {
    const entries = toCountEntries(SHEET, PRODUCTS);
    expect(entries.map((entry) => [entry.key, entry.category, entry.kind])).toEqual([
      ['MON-1|CM-A1|SIN-LOTE', 'Monitores', 'sobrante'],
      ['TEC-1|CM-B1|SIN-LOTE', 'Periféricos', 'faltante'],
      ['MON-1|CM-A2|L-2026', 'Monitores', 'cuadra'],
    ]);
    const skus = (filters: Partial<typeof COUNT_FILTERS>) => filterEntries(entries, { ...COUNT_FILTERS, ...filters }).map((entry) => entry.key);
    expect(skus({ categoria: 'Periféricos' })).toEqual(['TEC-1|CM-B1|SIN-LOTE']);
    expect(skus({ ubicacion: 'CM-A2' })).toEqual(['MON-1|CM-A2|L-2026']);
    expect(skus({ diferencia: 'con' })).toHaveLength(2);
    expect(skus({ diferencia: 'faltante' })).toEqual(['TEC-1|CM-B1|SIN-LOTE']);
    expect(skus({ contador: 'Andrea Quiroga' })).toEqual(['TEC-1|CM-B1|SIN-LOTE']);
    expect(skus({ q: 'cm-b1' })).toEqual(['TEC-1|CM-B1|SIN-LOTE']);
    expect(counterOptions(entries).map((option) => option.value)).toEqual(['Andrea Quiroga', 'Bruno Mamani']);
    expect(toCountEntries(null, PRODUCTS)).toEqual([]);
  });

  it('pendientes, progreso y resumen de diferencias', () => {
    expect(pendingProducts(PRODUCTS, SHEET, COUNT_FILTERS).map((product) => product.sku)).toEqual(['MON-2']);
    expect(pendingProducts(PRODUCTS, null, { ...COUNT_FILTERS, q: '7790001' }).map((product) => product.sku)).toEqual(['MON-1']);
    expect(countProgress(PRODUCTS, SHEET, COUNT_FILTERS)).toEqual({ expected: 3, counted: 2, percent: 67 });
    expect(countProgress(PRODUCTS, SHEET, { categoria: 'Monitores', ubicacion: 'CM-A2' })).toEqual({ expected: 1, counted: 0, percent: 0 });
    expect(countProgress([], null, COUNT_FILTERS)).toEqual({ expected: 0, counted: 0, percent: 0 });
    const summary = countSummary(SHEET);
    expect(summary).toMatchObject({ lines: 3, products: 2, surpluses: 1, shortages: 1, matching: 1 });
    expect(summary.differences.map((item) => item.sku)).toEqual(['MON-1', 'TEC-1']);
  });

  it('planilla impresa: el alcance elegido, por posición (sin posición al final) y nombre', () => {
    expect(printRows(PRODUCTS, COUNT_FILTERS).map((product) => product.sku)).toEqual(['MON-1', 'MON-2', 'TEC-1']);
    expect(printRows(PRODUCTS, { ...COUNT_FILTERS, categoria: 'Periféricos' }).map((product) => product.sku)).toEqual(['TEC-1']);
  });
});

describe('Toma física · lector de códigos, guía y comandos', () => {
  it('el lector encuentra el producto por SKU (sin distinguir mayúsculas) o por código de barras exacto', () => {
    expect(findByCode(PRODUCTS, ' mon-2 ')?.sku).toBe('MON-2');
    expect(findByCode(PRODUCTS, '7790001')?.sku).toBe('MON-1');
    expect(findByCode(PRODUCTS, '779000')).toBeNull();
    expect(findByCode(PRODUCTS, '')).toBeNull();
  });

  it('lo que dice el sistema en la posición (lote por defecto) y la posición con la que se abre un producto', () => {
    const card = {
      primaryBin: 'CM-A2',
      bins: [
        { binCode: 'CM-A1', lotNumber: 'SIN-LOTE', onHand: 4, available: 4, reserved: 0 },
        { binCode: 'CM-A1', lotNumber: 'L-1', onHand: 9, available: 9, reserved: 0 },
        { binCode: 'CM-A2', lotNumber: 'SIN-LOTE', onHand: 1, available: 1, reserved: 0 },
      ],
    } as unknown as CardRecord;
    expect(systemAt(card, 'CM-A1')).toBe(4);
    expect(systemAt(card, 'CM-B9')).toBe(0);
    expect(systemAt(undefined, 'CM-A1')).toBeNull();
    const bins = [
      { code: 'CM-A1', zone: 'A', warehouseCode: 'CM' },
      { code: 'CM-A2', zone: 'A', warehouseCode: 'CM' },
    ];
    expect(defaultBin(PRODUCTS[0], card, bins)).toBe('CM-A2');
    expect(defaultBin(PRODUCTS[2], undefined, bins)).toBe('CM-A1');
  });

  it('contenidos EXACTOS de los comandos (con todos sus parámetros)', () => {
    expect(toOpenCount('CM-PRINCIPAL', '2026-09-29', '  Conteo mensual ')).toEqual({ warehouseCode: 'CM-PRINCIPAL', countDate: '2026-09-29', notes: 'Conteo mensual' });
    expect(toOpenCount('CM-PRINCIPAL', '', ' ')).toEqual({ warehouseCode: 'CM-PRINCIPAL', countDate: null, notes: null });
    expect(toRecordCount('toma-1', 'MON-1', 'CM-A1', 7)).toEqual({ physicalCountId: 'toma-1', sku: 'MON-1', binCode: 'CM-A1', countedQuantity: 7, lotNumber: null });
    expect(toRemoveCount('toma-1', SHEET.lines[0])).toEqual({ physicalCountId: 'toma-1', sku: 'MON-1', binCode: 'CM-A1', lotNumber: null });
    expect(toRemoveCount('toma-1', SHEET.lines[2])).toEqual({ physicalCountId: 'toma-1', sku: 'MON-1', binCode: 'CM-A2', lotNumber: 'L-2026' });
    expect(plainMessage('✔ Toma física CF-1 anulada.')).toBe('Toma física CF-1 anulada.');
  });

  it('almacenes: los de la sucursal activa (o los visibles, sin sucursal activa)', () => {
    const branch = (id: string, code: string, warehouses: string[], overrides: Partial<BranchRecord> = {}): BranchRecord => ({
      id,
      code,
      name: `Sucursal ${code}`,
      isActive: true,
      isVisible: true,
      stockValue: null,
      transfersIn: 0,
      transfersOut: 0,
      users: 1,
      warehouses,
      ...overrides,
    });
    const branches = [branch('b-cm', 'CM', ['CM-PRINCIPAL', 'CM-TIENDA']), branch('b-cb', 'CB', ['CB-PRINCIPAL']), branch('b-sc', 'SC', ['SC-PRINCIPAL'], { isVisible: false })];
    expect(warehouseOptions(branches, 'b-cm')).toEqual([
      { value: 'CM-PRINCIPAL', label: 'CM · Sucursal CM (CM-PRINCIPAL)' },
      { value: 'CM-TIENDA', label: 'CM · Sucursal CM (CM-TIENDA)' },
    ]);
    expect(warehouseOptions(branches, 'b-cb')).toEqual([{ value: 'CB-PRINCIPAL', label: 'CB · Sucursal CB' }]);
    expect(warehouseOptions(branches, null).map((option) => option.value)).toEqual(['CM-PRINCIPAL', 'CM-TIENDA', 'CB-PRINCIPAL']);
  });
});
