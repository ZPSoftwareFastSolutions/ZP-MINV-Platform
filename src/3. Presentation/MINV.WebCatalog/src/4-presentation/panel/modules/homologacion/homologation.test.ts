// Módulo «Homologación» · funciones puras: estado de cada producto, unidad y medio de pago, filtros, actividades,
// catálogos del SIN, resumen y sugerencias para aceptar en lote.

import { describe, expect, it } from 'vitest';
import {
  CODE_FILTERS,
  PRODUCT_FILTERS,
  activityOptions,
  codeStatus,
  defaultActivity,
  filterMethods,
  filterProducts,
  filterUnits,
  firstWord,
  homologationSummary,
  productActivityOptions,
  productStatus,
  sinOptions,
  sinText,
  toSuggestions,
  type ActivityData,
  type HomologationData,
  type ProductRowData,
} from './homologation';

function product(overrides: Partial<ProductRowData>): ProductRowData {
  return { activityCode: null, category: 'Periféricos', isActive: true, name: 'Producto', productId: 'p', sinProductCode: null, sinProductDescription: null, sku: 'SKU', ...overrides };
}

const ACTIVITIES: ActivityData[] = [
  { activityType: 'S', code: '620000', description: 'SERVICIOS DE PROGRAMACION', isCurrent: true, sectors: [2] },
  { activityType: 'P', code: '461000', description: 'VENTA AL POR MAYOR', isCurrent: true, sectors: [1, 24] },
  { activityType: 'P', code: '479000', description: 'ACTIVIDAD RETIRADA', isCurrent: false, sectors: [1] },
];

const PRODUCTS: ProductRowData[] = [
  product({ productId: 'p1', sku: 'MOU-01', name: 'Mouse gamer', activityCode: '461000', sinProductCode: 83141, sinProductDescription: 'MOUSE' }),
  product({ productId: 'p2', sku: 'GPU-01', name: 'Tarjeta de video RTX', category: 'Video' }),
  product({ productId: 'p3', sku: 'OLD-01', name: 'Producto viejo', category: 'Otros', isActive: false }),
];

describe('Homologación · estados y textos', () => {
  it('estado del producto: inactivo, sin homologar u homologado; código del SIN en palabras', () => {
    expect(PRODUCTS.map(productStatus)).toEqual(['homologado', 'pendiente', 'inactivo']);
    expect(codeStatus(null)).toBe('pendiente');
    expect(codeStatus(58)).toBe('homologado');
    expect(sinText(83141, 'MOUSE INALAMBRICO')).toBe('83141 · Mouse inalambrico');
    expect(sinText(null, null)).toBe('Sin homologar');
    expect(firstWord('  Tarjeta de video ')).toBe('Tarjeta');
  });

  it('actividades vigentes y la propuesta: la del producto, la primera de compra venta o la primera', () => {
    expect(activityOptions(ACTIVITIES).map((option) => option.value)).toEqual(['620000', '461000']);
    expect(defaultActivity(ACTIVITIES, '620000')).toBe('620000');
    expect(defaultActivity(ACTIVITIES, '479000')).toBe('461000');
    expect(defaultActivity(ACTIVITIES)).toBe('461000');
    expect(defaultActivity([ACTIVITIES[0]])).toBe('620000');
    expect(defaultActivity([])).toBe('');
  });
});

describe('Homologación · filtros', () => {
  it('productos: sin homologar, categoría, actividad («Sin actividad») y búsqueda sin acentos', () => {
    expect(filterProducts(PRODUCTS, { ...PRODUCT_FILTERS, estado: 'pendiente' }).map((row) => row.sku)).toEqual(['GPU-01']);
    expect(filterProducts(PRODUCTS, { ...PRODUCT_FILTERS, categoria: 'Video' }).map((row) => row.sku)).toEqual(['GPU-01']);
    expect(filterProducts(PRODUCTS, { ...PRODUCT_FILTERS, actividad: '-' }).map((row) => row.sku)).toEqual(['GPU-01', 'OLD-01']);
    expect(filterProducts(PRODUCTS, { ...PRODUCT_FILTERS, q: '83141' }).map((row) => row.sku)).toEqual(['MOU-01']);
    expect(filterProducts(PRODUCTS, { ...PRODUCT_FILTERS, q: 'tarjeta video' }).map((row) => row.sku)).toEqual(['GPU-01']);
    expect(productActivityOptions(PRODUCTS)).toEqual([
      { value: '461000', label: '461000' },
      { value: '-', label: 'Sin actividad' },
    ]);
  });

  it('unidades y medios de pago: sin homologar y búsqueda; catálogo del SIN vigente y ordenado', () => {
    const units = [
      { code: 'UND', name: 'Unidad', sinUnitCode: 58, sinUnitDescription: 'UNIDAD (BIENES)', unitId: 'u1' },
      { code: 'KIT', name: 'Kit', sinUnitCode: null, sinUnitDescription: null, unitId: 'u2' },
    ];
    expect(filterUnits(units, { ...CODE_FILTERS, estado: 'pendiente' }).map((row) => row.code)).toEqual(['KIT']);
    expect(filterUnits(units, { ...CODE_FILTERS, q: 'bienes' }).map((row) => row.code)).toEqual(['UND']);
    const methods = [
      { code: 'QR', name: 'Pago QR', paymentMethodId: 'm1', sinCode: null, sinDescription: null },
      { code: 'EFECTIVO', name: 'Efectivo', paymentMethodId: 'm2', sinCode: 1, sinDescription: 'EFECTIVO' },
    ];
    expect(filterMethods(methods, { ...CODE_FILTERS, estado: 'homologado' }).map((row) => row.code)).toEqual(['EFECTIVO']);
    expect(
      sinOptions([
        { catalog: 'UNIDAD_MEDIDA', code: 58, description: 'UNIDAD (BIENES)', isCurrent: true },
        { catalog: 'UNIDAD_MEDIDA', code: 47, description: 'JUEGO', isCurrent: true },
        { catalog: 'UNIDAD_MEDIDA', code: 99, description: 'RETIRADA', isCurrent: false },
      ]),
    ).toEqual([
      { value: '47', label: '47 · Juego' },
      { value: '58', label: '58 · Unidad (bienes)' },
    ]);
  });
});

describe('Homologación · resumen y sugerencias', () => {
  it('resumen: pendientes, homologados de los activos, unidades y medios con código', () => {
    const view = {
      activities: [],
      paymentMethods: [{ code: 'QR', name: 'Pago QR', paymentMethodId: 'm1', sinCode: null, sinDescription: null }],
      pendingProducts: 1,
      products: PRODUCTS,
      sinPaymentMethods: [],
      sinUnits: [],
      units: [{ code: 'UND', name: 'Unidad', sinUnitCode: 58, sinUnitDescription: 'UNIDAD', unitId: 'u1' }],
    } satisfies HomologationData;
    expect(homologationSummary(view)).toEqual({ pending: 1, activeProducts: 2, homologatedProducts: 1, units: 1, unitsDone: 1, methods: 1, methodsDone: 0 });
  });

  it('sugerencias con el nombre del producto y la descripción del SIN, todas marcadas', () => {
    const items = toSuggestions(
      [
        { sku: 'gpu-01', activityCode: '461000', sinProductCode: 83142 },
        { sku: 'NUEVO', activityCode: '461000', sinProductCode: 99999 },
      ],
      PRODUCTS,
      [{ activityCode: '461000', productCode: 83142, description: 'TARJETA DE VIDEO', isCurrent: true }],
    );
    expect(items).toEqual([
      { input: { sku: 'gpu-01', activityCode: '461000', sinProductCode: 83142 }, productText: 'gpu-01 · Tarjeta de video RTX', sinText: '83142 · Tarjeta de video', accepted: true },
      { input: { sku: 'NUEVO', activityCode: '461000', sinProductCode: 99999 }, productText: 'NUEVO · NUEVO', sinText: '99999 · (sin descripción)', accepted: true },
    ]);
  });
});
