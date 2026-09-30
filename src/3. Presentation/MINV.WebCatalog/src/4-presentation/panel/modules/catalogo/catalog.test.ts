// Módulo «Catálogo» · pruebas de las funciones puras de la pestaña «Productos» (sin React ni red).

import { describe, expect, it } from 'vitest';
import {
  PRODUCT_FILTERS,
  brandOptions,
  catalogSummary,
  categoryOptions,
  draftOf,
  facetOptions,
  filterProducts,
  gtinCheckDigit,
  marginOf,
  marginText,
  needsServerFilter,
  netOf,
  newEan13,
  parseSpecChoices,
  priceForMargin,
  productProblems,
  specFiltersOf,
  specOptions,
  suggestCategoryCode,
  toProductEntries,
  toSaveProduct,
  toggleActivePayload,
  visibleFacets,
  warrantyText,
  withSpecChoice,
  writeSpecChoices,
  type CatalogRecord,
  type FacetRecord,
  type SpecDefinitionRecord,
  type TechRecord,
} from './catalog';

const BOLIVIA = { rate: 13, onInvoicedAmount: true };

function product(overrides: Partial<CatalogRecord>): CatalogRecord {
  return {
    variantId: 'v-1',
    sku: 'SKU-1',
    name: 'Producto',
    description: null,
    categoryCode: 'MON',
    category: 'Monitores',
    unit: 'UND',
    supplierCode: null,
    supplier: null,
    salePrice: 100,
    unitCost: 50,
    minimum: 0,
    maximum: 0,
    barcode: null,
    isActive: true,
    hasImage: false,
    binCode: null,
    ...overrides,
  };
}

function tech(overrides: Partial<TechRecord>): TechRecord {
  return {
    sku: 'SKU-1',
    name: 'Producto',
    categoryCode: 'MON',
    category: 'Monitores',
    brand: null,
    price: 100,
    stock: 0,
    trackSerials: false,
    warrantyMonths: 0,
    keySpecs: '',
    platforms: [],
    imageId: null,
    serialKind: 'Serial',
    ...overrides,
  };
}

function definition(overrides: Partial<SpecDefinitionRecord>): SpecDefinitionRecord {
  return {
    id: 'd-1',
    categoryCode: 'CON',
    categoryName: 'Consolas',
    code: 'plataforma',
    name: 'Plataforma',
    unit: null,
    dataType: 'Option',
    isMultiValued: false,
    isFilterable: true,
    isRequired: false,
    compatibilityKey: null,
    sortOrder: 10,
    options: [],
    isInherited: false,
    ...overrides,
  };
}

const CATALOG = [
  product({ sku: 'MON-1', name: 'Monitor LG', salePrice: 3200, unitCost: 2200, hasImage: true, barcode: '7790001' }),
  product({ sku: 'CON-1', name: 'PlayStation 5', categoryCode: 'CON', category: 'Consolas', salePrice: 5500, unitCost: 4600 }),
  product({ sku: 'CAB-1', name: 'Cable HDMI', categoryCode: 'CAB', category: 'Cables', salePrice: 0, unitCost: 20, isActive: false }),
  product({ sku: 'CEL-1', name: 'Galaxy A55', categoryCode: 'CEL', category: 'Celulares', salePrice: 3100, unitCost: 2000 }),
];
const TECH = [
  tech({ sku: 'MON-1', brand: 'LG', stock: 5, trackSerials: true, warrantyMonths: 12 }),
  tech({ sku: 'con-1', brand: 'Sony', platforms: ['PS5'], trackSerials: true }),
  tech({ sku: 'CEL-1', brand: 'Samsung', trackSerials: true, serialKind: 'Imei' }),
];

describe('Catálogo · margen (guía, la convención de Bolivia)', () => {
  it('el neto es el 87 % del precio y el margen se calcula sobre el neto', () => {
    expect(netOf(1000, BOLIVIA)).toBe(870);
    expect(marginOf(1000, 609, BOLIVIA)).toBeCloseTo(0.3, 10);
    expect(marginOf(0, 10, BOLIVIA)).toBeNull();
    expect(netOf(119, { rate: 19, onInvoicedAmount: false })).toBeCloseTo(100, 10);
  });

  it('«Aplicar margen» calcula el precio con IVA a partir del costo (un decimal, como el escritorio)', () => {
    // 609 / 0,7 = 870 de neto → 870 / 0,87 = 1000 con IVA.
    expect(priceForMargin(609, 0.3, BOLIVIA)).toBe(1000);
    expect(priceForMargin(100, 0.25, BOLIVIA)).toBe(153.3);
  });

  it('texto del margen', () => {
    expect(marginText(0.25)).toBe('25 %');
    expect(marginText(0.125)).toBe('12,5 %');
    expect(marginText(null)).toBe('—');
  });
});

describe('Catálogo · filas y filtros de la página', () => {
  const entries = toProductEntries(CATALOG, TECH, BOLIVIA);
  const keys = (filters: Partial<typeof PRODUCT_FILTERS>, matched: ReadonlySet<string> | null = null) =>
    filterProducts(entries, { ...PRODUCT_FILTERS, ...filters }, matched).map((entry) => entry.key);

  it('cruza el catálogo con la ficha resumida (sin distinguir mayúsculas del SKU); los inactivos no traen ficha', () => {
    const monitor = entries.find((entry) => entry.key === 'MON-1');
    expect(monitor).toMatchObject({ brand: 'LG', available: 5, serial: 'serie', warrantyMonths: 12, state: 'activo' });
    expect(entries.find((entry) => entry.key === 'CON-1')?.brand).toBe('Sony');
    expect(entries.find((entry) => entry.key === 'CAB-1')).toMatchObject({ tech: null, serial: null, available: null, state: 'inactivo', margin: null });
    expect(entries.find((entry) => entry.key === 'CEL-1')?.serial).toBe('imei');
  });

  it('filtra por marca, estado, imagen, precio, margen bajo, serie, plataforma y búsqueda', () => {
    expect(keys({ marca: 'Sony' })).toEqual(['CON-1']);
    expect(keys({ estado: 'inactivo' })).toEqual(['CAB-1']);
    expect(keys({ imagen: 'con' })).toEqual(['MON-1']);
    expect(keys({ precio: 'sin' })).toEqual(['CAB-1']);
    expect(keys({ precio: 'margen-bajo' })).toEqual(['CON-1']);
    expect(keys({ serie: 'imei' })).toEqual(['CEL-1']);
    expect(keys({ serie: 'no' })).toEqual([]);
    expect(keys({ plataforma: 'ps5' })).toEqual(['CON-1']);
    expect(keys({ q: '7790001' })).toEqual(['MON-1']);
    expect(keys({ q: 'galaxy samsung' })).toEqual(['CEL-1']);
  });

  it('lo que devolvió el servidor (categoría, condición y especificaciones) acota la lista', () => {
    expect(keys({}, new Set(['CON-1', 'CEL-1']))).toEqual(['CON-1', 'CEL-1']);
  });

  it('opciones de marca (con «Sin marca» si hay fichas sin marca) y de categoría con sus productos propios', () => {
    const withoutBrand = toProductEntries([product({ sku: 'X' })], [tech({ sku: 'X' })], BOLIVIA);
    expect(brandOptions([...entries, ...withoutBrand]).map((option) => option.label)).toEqual(['LG', 'Samsung', 'Sony', 'Sin marca']);
    const options = { categories: [{ code: 'CAB', name: 'Cables' }, { code: 'PER', name: 'Periféricos' }], units: [], suppliers: [], taxRate: 13, priceListName: 'GENERAL', vatOnInvoicedAmount: true };
    expect(categoryOptions(options, CATALOG)).toEqual([
      { value: 'CAB', label: 'Cables (1)' },
      { value: 'PER', label: 'Periféricos' },
    ]);
  });

  it('plataformas y condiciones salen de las opciones de las especificaciones (regla T-07), sin repetir', () => {
    const definitions = [
      definition({ code: 'plataforma', options: ['PS4', 'PS5'] }),
      definition({ code: 'plataformas', categoryCode: 'ACC', options: ['PS5', 'PC'] }),
      definition({ code: 'condicion', options: ['Nuevo', 'Usado'] }),
    ];
    expect(specOptions(definitions, ['plataforma', 'plataformas']).map((option) => option.value)).toEqual(['PS4', 'PS5', 'PC']);
    expect(specOptions(definitions, ['condicion']).map((option) => option.value)).toEqual(['Nuevo', 'Usado']);
  });

  it('resumen del catálogo', () => {
    const summary = catalogSummary(entries);
    expect(summary).toMatchObject({ total: 4, active: 3, withImage: 1, withoutImage: 3, withoutPrice: 0, lowMargin: 1 });
    expect(summary.byCategory.map((item) => item.label)).toEqual(['Celulares', 'Consolas', 'Monitores']);
  });
});

describe('Catálogo · filtros por especificación (servidor)', () => {
  it('las facetas elegidas viajan en la dirección (código:valor codificados) y se leen igual', () => {
    const text = withSpecChoice(withSpecChoice('', 'tamano', '27'), 'panel', 'IPS | mate');
    expect(parseSpecChoices(text)).toEqual([
      { code: 'tamano', value: '27' },
      { code: 'panel', value: 'IPS | mate' },
    ]);
    expect(parseSpecChoices(withSpecChoice(text, 'tamano', ''))).toEqual([{ code: 'panel', value: 'IPS | mate' }]);
    expect(parseSpecChoices('%E0:1|solo|:x')).toEqual([]);
    expect(writeSpecChoices([{ code: 'a', value: 'b:c' }])).toBe('a:b%3Ac');
  });

  it('arma los `specFilters` con TODOS los parámetros y decide si hace falta el servidor', () => {
    expect(specFiltersOf({ espec: 'tamano:27', condicion: 'Usado' })).toEqual([
      { code: 'tamano', values: ['27'], min: null, max: null },
      { code: 'condicion', values: ['Usado'], min: null, max: null },
    ]);
    expect(needsServerFilter({ categoria: '', espec: '', condicion: '' })).toBe(false);
    expect(needsServerFilter({ categoria: 'MON', espec: '', condicion: '' })).toBe(true);
    expect(needsServerFilter({ categoria: '', espec: '', condicion: 'Nuevo' })).toBe(true);
  });

  it('las facetas se muestran con la unidad y la cantidad; plataforma y condición tienen su filtro propio', () => {
    const size: FacetRecord = { code: 'tamano', name: 'Tamaño', unit: 'pulgadas', dataType: 'Number', values: [{ value: '23.8', count: 2 }], min: 23.8, max: 23.8 };
    expect(facetOptions(size)).toEqual([{ value: '23.8', label: '23,8 pulgadas (2)' }]);
    const platform: FacetRecord = { ...size, code: 'plataforma', dataType: 'Option', unit: null };
    expect(visibleFacets([size, platform]).map((facet) => facet.code)).toEqual(['tamano']);
  });
});

describe('Catálogo · formulario del producto', () => {
  const options = {
    categories: [{ code: 'MON', name: 'Monitores' }],
    units: [
      { code: 'KG', name: 'Kilogramo', allowsDecimals: true },
      { code: 'UND', name: 'Unidad', allowsDecimals: false },
    ],
    suppliers: [],
    taxRate: 13,
    priceListName: 'GENERAL',
    vatOnInvoicedAmount: true,
  };

  it('un producto nuevo empieza con la unidad «UND», activo y sin costo ni precio', () => {
    expect(draftOf(null, options)).toMatchObject({ sku: '', unitCode: 'UND', isActive: true, unitCost: null, salePrice: null, minimum: 0, maximum: 0 });
  });

  it('valida como el servidor: SKU, nombre, categoría, unidad, números y máximo ≥ mínimo', () => {
    const empty = productProblems(draftOf(null, options), true);
    expect(Object.keys(empty).sort()).toEqual(['categoryCode', 'name', 'salePrice', 'sku', 'unitCost']);
    const draft = { ...draftOf(null, options), sku: 'MON 27', name: 'Monitor', categoryCode: 'MON', unitCost: 1, salePrice: 2, minimum: 5, maximum: 2 };
    expect(productProblems(draft, true)).toEqual({ sku: 'Solo letras, números, guion y guion bajo (sin espacios).', maximum: 'El máximo debe ser mayor o igual al mínimo.' });
    // Al editar, el SKU no se valida (no cambia).
    expect(productProblems({ ...draft, maximum: 0 }, false)).toEqual({});
  });

  it('`SaveProductCommand` con todos sus parámetros (SKU en mayúsculas, vacíos como null)', () => {
    const draft = { ...draftOf(null, options), sku: 'mon-27', name: ' Monitor 27 ', categoryCode: 'MON', unitCost: 1000, salePrice: 1500, description: '  ', barcode: ' 779 ' };
    expect(toSaveProduct(null, draft)).toEqual({
      originalSku: null,
      sku: 'MON-27',
      name: 'Monitor 27',
      description: null,
      categoryCode: 'MON',
      unitCode: 'UND',
      supplierCode: null,
      minimum: 0,
      maximum: 0,
      unitCost: 1000,
      salePrice: 1500,
      barcode: '779',
      isActive: true,
      binCode: null,
    });
  });

  it('activar o desactivar reenvía el MISMO producto con `isActive` cambiado', () => {
    const row = product({ sku: 'MON-1', supplierCode: 'PRV-1', barcode: '779', binCode: 'CM-A1', minimum: 2, maximum: 9 });
    expect(toggleActivePayload(row, false)).toEqual({
      originalSku: 'MON-1',
      sku: 'MON-1',
      name: 'Producto',
      description: null,
      categoryCode: 'MON',
      unitCode: 'UND',
      supplierCode: 'PRV-1',
      minimum: 2,
      maximum: 9,
      unitCost: 50,
      salePrice: 100,
      barcode: '779',
      isActive: false,
      binCode: 'CM-A1',
    });
  });

  it('EAN-13 interno con dígito de control válido y prefijo 2', () => {
    expect(gtinCheckDigit('590123412345')).toBe('7');
    const code = newEan13(() => 4);
    expect(code).toBe(`244444444444${gtinCheckDigit('244444444444')}`);
    expect(newEan13()).toMatch(/^2\d{12}$/);
  });

  it('código sugerido de una categoría nueva (como el escritorio) y textos de garantía', () => {
    expect(suggestCategoryCode('Periféricos', ['PER'])).toBe('PER2');
    expect(suggestCategoryCode('TV', [])).toBe('TVC');
    expect(warrantyText(0)).toBe('Sin garantía');
    expect(warrantyText(12)).toBe('1 año de garantía');
    expect(warrantyText(24)).toBe('2 años de garantía');
    expect(warrantyText(6)).toBe('6 meses de garantía');
  });
});
