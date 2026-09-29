// Módulo «Caja» · productos a la venta: unión con la ficha técnica y lo reservado, filtros (categoría, plataforma,
// condición del servidor, disponibilidad y búsqueda sin acentos), códigos del lector, opciones de las listas desplegables
// (de los datos del servidor) y CSV.

import { describe, expect, it } from 'vitest';
import { buildCsv } from '@/4-presentation/panel/lib';
import {
  PRODUCT_CSV,
  PRODUCT_FILTERS,
  availabilityText,
  categoryOptions,
  conditionOptions,
  filterProducts,
  findByCode,
  findLookup,
  platformOptions,
  toCajaProducts,
  unknownCodeText,
  warrantyText,
} from './products';
import type { ProductLookupData, SellableData, SpecDefinitionData, TechProductData } from './types';

const sellable: SellableData[] = [
  { variantId: 'v1', sku: 'CON-SONY-PS5', name: 'Consola PlayStation 5', categoryCode: 'CON', category: 'Consolas', unit: 'UND', allowsDecimals: false, price: 4999, available: 2, barcodes: ['7501'] },
  { variantId: 'v2', sku: 'JUE-PS5-GT7', name: 'Juego Gran Turismo 7', categoryCode: 'JUE', category: 'Juegos', unit: 'UND', allowsDecimals: false, price: 399, available: 0, barcodes: [] },
  { variantId: 'v3', sku: 'CAB-HDMI', name: 'Cable HDMI · 2 m', categoryCode: 'ACC', category: 'Accesorios', unit: 'M', allowsDecimals: true, price: 35.5, available: 12.5, barcodes: ['7503'] },
];

const tech: TechProductData[] = [
  { sku: 'CON-SONY-PS5', name: 'Consola PlayStation 5', categoryCode: 'CON', category: 'Consolas', brand: 'Sony', price: 4999, stock: 3, trackSerials: true, warrantyMonths: 12, keySpecs: '825 GB', platforms: ['PS5'], imageId: null, serialKind: 'Serial' },
  { sku: 'JUE-PS5-GT7', name: 'Juego Gran Turismo 7', categoryCode: 'JUE', category: 'Juegos', brand: null, price: 399, stock: 0, trackSerials: false, warrantyMonths: 0, keySpecs: '', platforms: ['ps5'], imageId: null, serialKind: 'Serial' },
];

function spec(code: string, options: string[]): SpecDefinitionData {
  return { id: code, categoryCode: 'CON', categoryName: 'Consolas', code, name: code, unit: null, dataType: 'Option', isMultiValued: false, isFilterable: true, isRequired: false, compatibilityKey: null, sortOrder: 1, options, isInherited: false };
}

const products = toCajaProducts(sellable, tech, [{ sku: 'con-sony-ps5', reserved: 1 }]);

describe('caja · productos', () => {
  it('une lo vendible con la ficha técnica (serie, plataformas, garantía) y lo reservado', () => {
    expect(products[0]).toMatchObject({ sku: 'CON-SONY-PS5', serialized: true, serialKind: 'Serial', warrantyMonths: 12, platforms: ['PS5'], brand: 'Sony', reserved: 1, available: 2 });
    expect(products[2]).toMatchObject({ sku: 'CAB-HDMI', serialized: false, platforms: [], reserved: 0 });
    // Sin ficha técnica (no se pudo leer), nada lleva serie.
    expect(toCajaProducts(sellable, undefined, undefined).every((product) => !product.serialized)).toBe(true);
  });

  it('filtra por categoría, plataforma (sin distinguir mayúsculas), condición, disponibilidad y búsqueda sin acentos', () => {
    const skus = (filters: Partial<typeof PRODUCT_FILTERS>, condition: Set<string> | null = null) =>
      filterProducts(products, { ...PRODUCT_FILTERS, ...filters }, condition).map((product) => product.sku);
    expect(skus({})).toHaveLength(3);
    expect(skus({ categoria: 'JUE' })).toEqual(['JUE-PS5-GT7']);
    expect(skus({ plataforma: 'PS5' })).toEqual(['CON-SONY-PS5', 'JUE-PS5-GT7']);
    expect(skus({ condicion: 'Nuevo' }, new Set(['CAB-HDMI']))).toEqual(['CAB-HDMI']);
    expect(skus({ stock: 'con' })).toEqual(['CON-SONY-PS5', 'CAB-HDMI']);
    expect(skus({ stock: 'sin' })).toEqual(['JUE-PS5-GT7']);
    expect(skus({ q: 'gran turismo' })).toEqual(['JUE-PS5-GT7']);
    expect(skus({ q: 'sony' })).toEqual(['CON-SONY-PS5']);
    expect(skus({ q: '7503' })).toEqual(['CAB-HDMI']);
  });

  it('el lector de códigos encuentra por SKU exacto (sin mayúsculas) o por código de barras exacto', () => {
    expect(findByCode(products, 'con-sony-ps5')?.sku).toBe('CON-SONY-PS5');
    expect(findByCode(products, ' 7503 ')?.sku).toBe('CAB-HDMI');
    expect(findByCode(products, '750')).toBeNull();
    expect(findByCode(products, '')).toBeNull();
  });

  it('explica un código que no está a la venta: inactivo, sin precio o desconocido', () => {
    const rows: ProductLookupData[] = [
      { variantId: 'x', sku: 'MOU-OLD', name: 'Mouse viejo', category: 'Mouse', unit: 'UND', allowsDecimals: false, isActive: false, barcodes: ['111'], primaryBin: null },
      { variantId: 'y', sku: 'MOU-NEW', name: 'Mouse nuevo', category: 'Mouse', unit: 'UND', allowsDecimals: false, isActive: true, barcodes: ['222'], primaryBin: null },
    ];
    expect(unknownCodeText('111', findLookup(rows, '111'))).toBe('«Mouse viejo» (MOU-OLD) está inactivo y no se vende. Pida que lo activen en el catálogo.');
    expect(unknownCodeText('mou-new', findLookup(rows, 'mou-new'))).toBe('«Mouse nuevo» (MOU-NEW) no está a la venta: no tiene precio en la lista de precios vigente.');
    expect(unknownCodeText('999', findLookup(rows, '999'))).toBe('Ningún producto tiene el código «999». Revise el código o busque el producto por su nombre.');
  });

  it('las listas desplegables salen de los datos del servidor: categorías con cuántos hay, plataformas con productos y condiciones', () => {
    expect(categoryOptions(products)).toEqual([
      { value: 'ACC', label: 'Accesorios (1)' },
      { value: 'CON', label: 'Consolas (1)' },
      { value: 'JUE', label: 'Juegos (1)' },
    ]);
    const specs = [spec('plataformas', ['PS5', 'PC']), spec('plataforma', ['PS5', 'Nintendo Switch']), spec('condicion', ['Nuevo', 'Usado', 'nuevo'])];
    // «plataforma» primero; sin repetir; solo las que tienen productos a la venta.
    expect(platformOptions(specs, products)).toEqual([{ value: 'PS5', label: 'PS5 (2)' }]);
    expect(conditionOptions(specs)).toEqual([
      { value: 'Nuevo', label: 'Nuevo' },
      { value: 'Usado', label: 'Usado' },
    ]);
    expect(platformOptions(undefined, products)).toEqual([]);
  });

  it('disponible = existencias − reservado, en palabras; garantía en palabras', () => {
    expect(availabilityText(products[0])).toBe('2 UND (reservado 1)');
    expect(availabilityText(products[1])).toBe('Agotado');
    expect(availabilityText({ ...products[1], reserved: 2 })).toBe('Agotado · reservado 2');
    expect(availabilityText(products[2])).toBe('12,5 M');
    expect(warrantyText(12)).toBe('Garantía de 12 meses');
    expect(warrantyText(1)).toBe('Garantía de 1 mes');
    expect(warrantyText(0)).toBeNull();
  });

  it('el CSV lleva el SKU, el precio como número, lo reservado, la serie y las plataformas', () => {
    const [header, first] = buildCsv(PRODUCT_CSV, products.slice(0, 1), { bom: false }).split('\r\n');
    expect(header).toBe('"SKU";"Producto";"Categoría";"Marca";"Precio (Bs)";"Disponible";"Reservado";"Unidad";"Lleva serie";"Garantía (meses)";"Plataformas";"Códigos de barras"');
    expect(first).toBe('"CON-SONY-PS5";"Consola PlayStation 5";"Consolas";"Sony";4999;2;1;"UND";"Serie";12;"PS5";"7501"');
  });
});
