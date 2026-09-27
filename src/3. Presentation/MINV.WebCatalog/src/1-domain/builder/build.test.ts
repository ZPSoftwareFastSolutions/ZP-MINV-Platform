import { describe, expect, it } from 'vitest';
import { PRODUCTS } from '@/3-infrastructure/data/catalog.data';
import {
  EMPTY_BUILD,
  MAX_QUANTITY,
  buildCount,
  buildProgress,
  buildReducer,
  buildTotal,
  isInBuild,
  missingSlots,
  summarizeBuild,
  type BuildState,
} from './build';
import { BUILD_SLOTS, REQUIRED_SLOTS, isBuildable, slotForProduct } from './slots';

const bySku = (sku: string) => PRODUCTS.find((product) => product.sku === sku)!;
const cpu = bySku('CPU-AMD-7600');
const cpu2 = bySku('CPU-AMD-5600');
const ssd = bySku('SSD-KNG-NV3-1TB');
const ssd2 = bySku('SSD-SAM-990PRO-1TB');
const ps5 = PRODUCTS.find((product) => product.category === 'CPS')!;

describe('ranuras', () => {
  it('define 11 ranuras ordenadas con 6 obligatorias', () => {
    expect(BUILD_SLOTS.map((slot) => slot.key)).toEqual([
      'cpu', 'motherboard', 'ram', 'gpu', 'storage', 'psu', 'case', 'cooler', 'monitor', 'peripherals', 'software',
    ]);
    expect(BUILD_SLOTS.map((slot) => slot.order)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
    expect(REQUIRED_SLOTS.map((slot) => slot.key)).toEqual(['cpu', 'motherboard', 'ram', 'storage', 'psu', 'case']);
    expect(BUILD_SLOTS.filter((slot) => slot.multiple).map((slot) => slot.key)).toEqual(['storage', 'peripherals', 'software']);
    expect(BUILD_SLOTS.every((slot) => slot.hint.length > 20 && slot.icon.length > 0)).toBe(true);
  });

  it('infiere la ranura de un producto por su categoría', () => {
    expect(slotForProduct(cpu)?.key).toBe('cpu');
    expect(slotForProduct(ssd)?.key).toBe('storage');
    expect(slotForProduct(bySku('KEY-LOG-MK270'))?.key).toBe('peripherals');
    expect(slotForProduct(bySku('LIC-MS-W11HOME'))?.key).toBe('software');
    expect(slotForProduct(ps5)).toBeUndefined();
    expect(isBuildable(ps5)).toBe(false);
  });
});

describe('reductor del armado', () => {
  it('agrega una pieza y reemplaza la anterior en ranuras de una sola pieza', () => {
    let state: BuildState = buildReducer(EMPTY_BUILD, { type: 'add', product: cpu });
    expect(state.lines).toHaveLength(1);
    expect(state.lines[0]).toMatchObject({ slot: 'cpu', quantity: 1 });
    state = buildReducer(state, { type: 'add', product: cpu2 });
    expect(state.lines).toHaveLength(1);
    expect(state.lines[0].product.sku).toBe(cpu2.sku);
  });

  it('en ranuras múltiples acumula productos distintos y suma cantidad del mismo SKU', () => {
    let state = buildReducer(EMPTY_BUILD, { type: 'add', product: ssd });
    state = buildReducer(state, { type: 'add', product: ssd2 });
    state = buildReducer(state, { type: 'add', product: ssd, quantity: 2 });
    expect(state.lines).toHaveLength(2);
    expect(state.lines.find((line) => line.product.sku === ssd.sku)?.quantity).toBe(3);
    expect(buildCount(state.lines)).toBe(4);
  });

  it('ignora productos sin ranura y mantiene las líneas en el orden de las ranuras', () => {
    let state = buildReducer(EMPTY_BUILD, { type: 'add', product: ssd });
    expect(buildReducer(state, { type: 'add', product: ps5 })).toBe(state);
    state = buildReducer(state, { type: 'add', product: cpu });
    expect(state.lines.map((line) => line.slot)).toEqual(['cpu', 'storage']);
  });

  it('cambia cantidades con límites y quita con cantidad 0', () => {
    let state = buildReducer(EMPTY_BUILD, { type: 'add', product: ssd });
    state = buildReducer(state, { type: 'setQuantity', sku: ssd.sku, quantity: 99 });
    expect(state.lines[0].quantity).toBe(MAX_QUANTITY);
    state = buildReducer(state, { type: 'setQuantity', sku: ssd.sku, quantity: 0 });
    expect(state.lines).toHaveLength(0);
  });

  it('quita, vacía y carga un armado completo', () => {
    let state = buildReducer(EMPTY_BUILD, { type: 'add', product: cpu });
    state = buildReducer(state, { type: 'add', product: ssd });
    state = buildReducer(state, { type: 'remove', sku: cpu.sku });
    expect(isInBuild(state.lines, cpu.sku)).toBe(false);
    expect(isInBuild(state.lines, ssd.sku)).toBe(true);
    expect(buildReducer(state, { type: 'clear' })).toBe(EMPTY_BUILD);
    const loaded = buildReducer(state, {
      type: 'loadPreset',
      lines: [
        { slot: 'storage', product: ssd, quantity: 1 },
        { slot: 'cpu', product: cpu, quantity: 1 },
      ],
    });
    expect(loaded.lines.map((line) => line.slot)).toEqual(['cpu', 'storage']);
  });
});

describe('cálculos del armado', () => {
  it('suma total, cuenta piezas y mide el progreso de las ranuras obligatorias', () => {
    let state = buildReducer(EMPTY_BUILD, { type: 'add', product: cpu });
    state = buildReducer(state, { type: 'add', product: ssd, quantity: 2 });
    expect(buildTotal(state.lines)).toBe(cpu.price + ssd.price * 2);
    expect(buildCount(state.lines)).toBe(3);
    const progress = buildProgress(state.lines);
    expect(progress).toMatchObject({ covered: 2, required: 6, complete: false });
    expect(progress.ratio).toBeCloseTo(2 / 6);
    expect(missingSlots(state.lines).map((slot) => slot.key)).toEqual(['motherboard', 'ram', 'psu', 'case']);
  });

  it('resume un armado vacío y uno completo', () => {
    const empty = summarizeBuild([]);
    expect(empty).toMatchObject({ total: 0, count: 0, savings: 0 });
    expect(empty.progress.covered).toBe(0);
    expect(empty.slots).toHaveLength(11);

    const parts = ['CPU-AMD-7600', 'MB-MSI-B650-GPWIFI', 'RAM-KNG-D5-32-6000', 'SSD-SAM-990PRO-1TB', 'PSU-COR-RM750X', 'CASE-COR-4000D'];
    let state = EMPTY_BUILD;
    for (const sku of parts) state = buildReducer(state, { type: 'add', product: bySku(sku) });
    const summary = summarizeBuild(state.lines);
    expect(summary.progress.complete).toBe(true);
    expect(summary.missing).toEqual([]);
    expect(summary.total).toBe(buildTotal(state.lines));
    expect(summary.slots.find((entry) => entry.slot.key === 'cpu')?.subtotal).toBe(cpu.price);
    expect(summary.savings).toBeGreaterThanOrEqual(0);
  });
});
