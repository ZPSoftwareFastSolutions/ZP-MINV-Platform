// Módulo «Catálogo» · pruebas de las funciones puras de las fichas técnicas, las especificaciones y las categorías.

import { describe, expect, it } from 'vitest';
import type { CatalogRecord, SpecDefinitionRecord } from './catalog';
import { CATEGORY_FILTERS, categoryDraftOf, categoryProblems, filterCategories, toCategoryEntries, toSaveCategory, withCategoryName } from './categories';
import {
  SPEC_FILTERS,
  compatibilityOptions,
  filterSpecs,
  nextSortOrder,
  specDraftOf,
  specFlags,
  specProblems,
  specValues,
  suggestSpecCode,
  techDraftOf,
  techProblems,
  toSaveSpec,
  toSaveTech,
  toSpecEntries,
  type ProductTechRecord,
} from './specs';

function definition(overrides: Partial<SpecDefinitionRecord>): SpecDefinitionRecord {
  return {
    id: 'd',
    categoryCode: 'MON',
    categoryName: 'Monitores',
    code: 'tamano',
    name: 'Tamaño',
    unit: 'pulgadas',
    dataType: 'Number',
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

const SIZE = definition({ code: 'tamano', isRequired: true });
const PANEL = definition({ code: 'panel', name: 'Panel', unit: null, dataType: 'Option', options: ['IPS', 'VA'], sortOrder: 20 });
const PORTS = definition({ code: 'puertos', name: 'Puertos', unit: null, dataType: 'Option', isMultiValued: true, options: ['HDMI', 'DisplayPort', 'USB-C'], sortOrder: 30 });
const MODEL = definition({ code: 'modelo', name: 'Modelo', unit: null, dataType: 'Text', isFilterable: false, sortOrder: 40 });
const HZ = definition({ code: 'frecuencias', name: 'Frecuencias', unit: 'Hz', isMultiValued: true, sortOrder: 50 });

const SAVED: ProductTechRecord = {
  sku: 'MON-1',
  name: 'Monitor',
  category: 'Monitores',
  brand: 'LG',
  serialKind: 'Serial',
  serialsInStock: 3,
  trackSerials: true,
  warrantyMonths: 12,
  specs: [
    { code: 'tamano', name: 'Tamaño', unit: 'pulgadas', dataType: 'Number', values: ['27.5'], display: '27,5 pulgadas', compatibilityKey: null, isRequired: true },
    { code: 'puertos', name: 'Puertos', unit: null, dataType: 'Option', values: ['HDMI', 'USB-C'], display: 'HDMI, USB-C', compatibilityKey: null, isRequired: false },
  ],
};

describe('Fichas técnicas · formulario del producto', () => {
  it('la ficha guardada se abre con los números en formato de Bolivia y las opciones elegidas', () => {
    const draft = techDraftOf(SAVED);
    expect(draft).toMatchObject({ trackSerials: true, serialKind: 'Serial', warrantyMonths: 12 });
    expect(draft.values.tamano).toEqual({ text: '27,5', picked: ['27.5'] });
    expect(draft.values.puertos.picked).toEqual(['HDMI', 'USB-C']);
    expect(techDraftOf(null)).toEqual({ trackSerials: false, serialKind: 'Serial', warrantyMonths: 0, values: {} });
  });

  it('los valores respetan el tipo: número con punto, una opción, varias opciones de ESA especificación y texto', () => {
    expect(specValues(SIZE, { text: ' 27,5 ', picked: [] })).toEqual(['27.5']);
    expect(specValues(HZ, { text: '60, 144;165', picked: [] })).toEqual(['60', '144', '165']);
    expect(specValues(PANEL, { text: '', picked: ['ips', 'VA'] })).toEqual(['IPS']);
    expect(specValues(PORTS, { text: '', picked: ['USB-C', 'VGA', 'hdmi'] })).toEqual(['HDMI', 'USB-C']);
    expect(specValues(MODEL, { text: '  27GP850  ', picked: [] })).toEqual(['27GP850']);
    expect(specValues(MODEL, undefined)).toEqual([]);
  });

  it('avisa números mal escritos, obligatorias vacías y la garantía fuera de 0 a 120', () => {
    const draft = { ...techDraftOf(null), warrantyMonths: 130, values: { frecuencias: { text: '60, rápido', picked: [] } } };
    const problems = techProblems([SIZE, HZ], draft);
    expect(problems.warranty).toBe('La garantía va de 0 a 120 meses.');
    expect(problems.specs.tamano).toBe('Indique tamaño: es obligatoria.');
    expect(problems.specs.frecuencias).toContain('«rápido» no es un número');
    expect(techProblems([SIZE], { ...techDraftOf(SAVED) })).toEqual({ specs: {} });
  });

  it('`SaveProductTechCommand`: solo las especificaciones de la categoría que tienen valor', () => {
    const draft = techDraftOf(SAVED);
    draft.values.panel = { text: '', picked: ['VA'] };
    draft.values.otra = { text: 'no es de la categoría', picked: [] };
    expect(toSaveTech('MON-1', [SIZE, PANEL, PORTS, MODEL], { ...draft, warrantyMonths: 24 })).toEqual({
      sku: 'MON-1',
      trackSerials: true,
      serialKind: 'Serial',
      warrantyMonths: 24,
      specs: [
        { code: 'tamano', values: ['27.5'] },
        { code: 'panel', values: ['VA'] },
        { code: 'puertos', values: ['HDMI', 'USB-C'] },
      ],
    });
  });
});

describe('Especificaciones · lista y formulario', () => {
  const inherited = definition({ code: 'socket', name: 'Socket', categoryCode: 'CPU', categoryName: 'Procesadores', dataType: 'Option', options: ['AM5'], compatibilityKey: 'cpu_socket', isInherited: true });

  it('marcas, filtros y claves del armador que ya usan las especificaciones (con su nombre en español)', () => {
    expect(specFlags(inherited)).toEqual(['Filtra el catálogo', 'Armador: Socket del procesador', 'Heredada de Procesadores']);
    const entries = toSpecEntries([SIZE, PANEL, MODEL, inherited]);
    const codes = (filters: Partial<typeof SPEC_FILTERS>) => filterSpecs(entries, { ...SPEC_FILTERS, ...filters }).map((entry) => entry.definition.code);
    expect(codes({ tipo: 'Option' })).toEqual(['panel', 'socket']);
    expect(codes({ uso: 'obligatoria' })).toEqual(['tamano']);
    expect(codes({ uso: 'armador' })).toEqual(['socket']);
    expect(codes({ uso: 'heredada' })).toEqual(['socket']);
    expect(codes({ q: 'ips' })).toEqual(['panel']);
    expect(compatibilityOptions([SIZE, inherited], 'ram_type')).toEqual([
      { value: 'cpu_socket', label: 'Socket del procesador' },
      { value: 'ram_type', label: 'Tipo de RAM' },
    ]);
    expect(nextSortOrder([SIZE, PANEL, inherited], 'MON')).toBe(30);
    expect(nextSortOrder([], 'MON')).toBe(10);
  });

  it('valida y arma `SaveSpecDefinitionCommand` con todos sus parámetros', () => {
    const draft = { ...specDraftOf(null, 'MON', 30), name: 'Tipo de panel', code: suggestSpecCode('Tipo de panel'), dataType: 'Option' as const, options: 'IPS\n\n VA \nips' };
    expect(draft.code).toBe('tipo_de_panel');
    expect(specProblems(draft, true)).toEqual({ options: 'La opción «ips» está repetida.' });
    expect(specProblems({ ...draft, code: 'Tipo Panel', options: '' }, true)).toEqual({
      code: 'Minúsculas, números, guion y guion bajo (sin espacios).',
      options: 'Escriba al menos una opción (una por renglón).',
    });
    expect(toSaveSpec({ ...draft, options: 'IPS\n VA ', compatibilityKey: '' })).toEqual({
      categoryCode: 'MON',
      code: 'tipo_de_panel',
      name: 'Tipo de panel',
      unit: null,
      dataType: 'Option',
      isMultiValued: false,
      isFilterable: true,
      isRequired: false,
      compatibilityKey: null,
      sortOrder: 30,
      options: ['IPS', 'VA'],
    });
    // Una de texto no envía opciones; al editar, el código no se valida (no cambia).
    const edited = specDraftOf(MODEL, 'MON', 10);
    expect(toSaveSpec({ ...edited, options: 'x' }).options).toEqual([]);
    expect(specProblems({ ...edited, code: 'NO SE VALIDA' }, false)).toEqual({});
  });
});

describe('Categorías', () => {
  const options = {
    categories: [
      { code: 'CPU', name: 'Procesadores' },
      { code: 'MON', name: 'Monitores' },
    ],
    units: [],
    suppliers: [],
    taxRate: 13,
    priceListName: 'GENERAL',
    vatOnInvoicedAmount: true,
  };
  const catalog = [{ categoryCode: 'MON', isActive: true }, { categoryCode: 'MON', isActive: false }] as CatalogRecord[];

  it('cuenta productos y especificaciones propias, y filtra', () => {
    const entries = toCategoryEntries(options, catalog, [SIZE, PANEL, definition({ categoryCode: 'MON', isInherited: true })]);
    expect(entries).toEqual([
      { key: 'CPU', code: 'CPU', name: 'Procesadores', products: 0, activeProducts: 0, specs: 0 },
      { key: 'MON', code: 'MON', name: 'Monitores', products: 2, activeProducts: 1, specs: 2 },
    ]);
    expect(filterCategories(entries, { ...CATEGORY_FILTERS, productos: 'sin' }).map((entry) => entry.code)).toEqual(['CPU']);
    expect(filterCategories(entries, { ...CATEGORY_FILTERS, q: 'moni' }).map((entry) => entry.code)).toEqual(['MON']);
  });

  it('el código se sugiere del nombre hasta que se escribe a mano; una existente no cambia de madre', () => {
    let draft = categoryDraftOf('subcategoria', { code: 'CPU', name: 'Procesadores' });
    draft = withCategoryName(draft, 'Procesadores AMD', ['CPU', 'MON', 'PRO']);
    expect(draft).toMatchObject({ code: 'PRO2', parentCode: 'CPU' });
    expect(toSaveCategory(draft, 'subcategoria')).toEqual({ code: 'PRO2', name: 'Procesadores AMD', parentCode: 'CPU' });
    expect(categoryProblems({ ...draft, code: 'MON' }, 'nueva', ['CPU', 'MON'])).toEqual({ code: 'Ya existe una categoría con el código MON.' });
    const renamed = { ...categoryDraftOf('renombrar', { code: 'MON', name: 'Monitores' }), name: 'Monitores y pantallas' };
    expect(categoryProblems(renamed, 'renombrar', ['MON'])).toEqual({});
    expect(toSaveCategory(renamed, 'renombrar')).toEqual({ code: 'MON', name: 'Monitores y pantallas', parentCode: null });
  });
});
