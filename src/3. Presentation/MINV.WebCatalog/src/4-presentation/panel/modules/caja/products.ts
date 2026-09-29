// Módulo «Caja» · productos a la venta (funciones puras, sin React). La lista del cajero sale de
// `GetSellableProductsQuery` (precio de la lista vigente y disponible = existencias − reservado, calculado por el
// servidor), completada con la ficha técnica (`SearchTechProductsQuery`: serie o IMEI, garantía, plataformas) y con lo
// reservado (`GetStockReservationsQuery`, solo informativo: el disponible ya lo descuenta). Aquí están los filtros de la
// lista, la búsqueda por código (lector de códigos o SKU), las opciones de las listas desplegables —siempre de los datos
// del servidor, nunca fijas (regla T-07)— y las columnas del CSV.

import type { SelectOption } from '@/4-presentation/panel/kit';
import { formatNumber, formatQuantity, matchesSearch, type CsvColumn } from '@/4-presentation/panel/lib';
import type { ProductLookupData, ReservedStockData, SellableData, SerialKindData, SpecDefinitionData, TechProductData } from './types';

/** Un producto de la lista de la caja: lo que manda el servidor, listo para mostrar y vender. */
export interface CajaProduct {
  sku: string;
  name: string;
  category: string;
  categoryCode: string;
  /** Código de la unidad («UND», «KG»). */
  unit: string;
  allowsDecimals: boolean;
  /** Precio de la lista vigente (con IVA). */
  price: number;
  /** Disponible = existencias − reservado (lo calcula el servidor; lo reservado nunca se vende a otro cliente). */
  available: number;
  /** Unidades reservadas para armados y carritos (informativo). */
  reserved: number;
  barcodes: readonly string[];
  /** Lleva serie o IMEI: cada unidad vendida se elige de las disponibles (regla T-02). */
  serialized: boolean;
  serialKind: SerialKindData;
  warrantyMonths: number;
  platforms: readonly string[];
  brand: string | null;
  /** Especificaciones clave en una línea («8 núcleos · 5 GHz»). */
  keySpecs: string;
}

/** Códigos de las especificaciones con significado propio (constantes del servidor: `TechSpecCodes`). */
export const PLATFORM_SPEC = 'plataforma';
export const PLATFORMS_SPEC = 'plataformas';
export const CONDITION_SPEC = 'condicion';

const upper = (text: string) => text.trim().toUpperCase();
const folded = (text: string) => text.trim().toLocaleLowerCase('es');

/** Une lo vendible con la ficha técnica y lo reservado. Sin ficha (no se pudo leer), el producto no lleva serie. */
export function toCajaProducts(
  sellable: readonly SellableData[],
  tech: readonly TechProductData[] | undefined,
  reserved: readonly ReservedStockData[] | undefined,
): CajaProduct[] {
  const bySku = new Map((tech ?? []).map((row) => [upper(row.sku), row]));
  const held = new Map((reserved ?? []).map((row) => [upper(row.sku), row.reserved]));
  return sellable.map((row) => {
    const info = bySku.get(upper(row.sku));
    return {
      sku: row.sku,
      name: row.name,
      category: row.category,
      categoryCode: row.categoryCode,
      unit: row.unit,
      allowsDecimals: row.allowsDecimals,
      price: row.price,
      available: row.available,
      reserved: held.get(upper(row.sku)) ?? 0,
      barcodes: row.barcodes ?? [],
      serialized: info?.trackSerials ?? false,
      serialKind: info?.serialKind ?? 'Serial',
      warrantyMonths: info?.warrantyMonths ?? 0,
      platforms: info?.platforms ?? [],
      brand: info?.brand ?? null,
      keySpecs: info?.keySpecs ?? '',
    };
  });
}

/** El producto de un SKU (sin distinguir mayúsculas). */
export function productBySku(products: readonly CajaProduct[], sku: string): CajaProduct | undefined {
  const wanted = upper(sku);
  return products.find((product) => upper(product.sku) === wanted);
}

// ---------------------------------------------------------------------------------------------------- filtros

/** Filtros de la lista de productos (en la dirección de la página). */
export const PRODUCT_FILTERS = { q: '', categoria: '', plataforma: '', condicion: '', stock: '' };
export type ProductFilters = typeof PRODUCT_FILTERS;

/** «Disponibilidad»: con stock para vender o agotados. */
export const STOCK_OPTIONS: readonly SelectOption[] = [
  { value: 'con', label: 'Con stock disponible' },
  { value: 'sin', label: 'Agotados' },
];

/**
 * Aplica los filtros de la página. `conditionSkus` son los SKU de la condición elegida (los filtra el servidor); null =
 * sin ese filtro (o todavía no respondió).
 */
export function filterProducts(products: readonly CajaProduct[], filters: ProductFilters, conditionSkus: ReadonlySet<string> | null = null): CajaProduct[] {
  const platform = folded(filters.plataforma);
  return products.filter(
    (product) =>
      (!filters.categoria || product.categoryCode === filters.categoria) &&
      (!platform || product.platforms.some((item) => folded(item) === platform)) &&
      (!filters.condicion || conditionSkus === null || conditionSkus.has(upper(product.sku))) &&
      (filters.stock !== 'con' || product.available > 0) &&
      (filters.stock !== 'sin' || product.available <= 0) &&
      matchesSearch(filters.q, [product.name, product.sku, product.category, product.brand, product.keySpecs, ...product.barcodes]),
  );
}

/** SKU (en mayúsculas) de las filas que devolvió el servidor para un filtro por especificación. */
export function skuSet(rows: readonly TechProductData[]): Set<string> {
  return new Set(rows.map((row) => upper(row.sku)));
}

/** «Categoría»: las de los productos a la venta, por nombre, con cuántos hay. */
export function categoryOptions(products: readonly CajaProduct[]): SelectOption[] {
  const counts = new Map<string, { label: string; count: number }>();
  for (const product of products) {
    const current = counts.get(product.categoryCode);
    if (current) current.count += 1;
    else counts.set(product.categoryCode, { label: product.category, count: 1 });
  }
  return [...counts.entries()]
    .sort((a, b) => a[1].label.localeCompare(b[1].label, 'es'))
    .map(([value, item]) => ({ value, label: `${item.label} (${formatNumber(item.count)})` }));
}

/** Opciones distintas (sin distinguir mayúsculas) de las especificaciones indicadas, en su orden. */
function specOptions(specs: readonly SpecDefinitionData[] | undefined, codes: readonly string[]): string[] {
  const seen = new Set<string>();
  const options: string[] = [];
  const definitions = (specs ?? []).filter((spec) => codes.includes(spec.code)).sort((a, b) => codes.indexOf(a.code) - codes.indexOf(b.code));
  for (const definition of definitions) {
    for (const option of definition.options) {
      const key = folded(option);
      if (key.length === 0 || seen.has(key)) continue;
      seen.add(key);
      options.push(option.trim());
    }
  }
  return options;
}

/**
 * «Plataforma»: las opciones de las especificaciones «plataforma» (juegos y consolas) y «plataformas» (accesorios), solo
 * las que tienen algún producto a la venta, con cuántos (como los filtros rápidos del escritorio).
 */
export function platformOptions(specs: readonly SpecDefinitionData[] | undefined, products: readonly CajaProduct[]): SelectOption[] {
  return specOptions(specs, [PLATFORM_SPEC, PLATFORMS_SPEC])
    .map((option) => ({ option, count: products.filter((product) => product.platforms.some((item) => folded(item) === folded(option))).length }))
    .filter((item) => item.count > 0)
    .map((item) => ({ value: item.option, label: `${item.option} (${formatNumber(item.count)})` }));
}

/** «Condición» (Nuevo, Reacondicionado, Usado…): las opciones de la especificación «condicion». */
export function conditionOptions(specs: readonly SpecDefinitionData[] | undefined): SelectOption[] {
  return specOptions(specs, [CONDITION_SPEC]).map((option) => ({ value: option, label: option }));
}

// ---------------------------------------------------------------------------------------------------- códigos

/** El producto de un código leído o escrito: el SKU exacto (sin distinguir mayúsculas) o un código de barras exacto. */
export function findByCode(products: readonly CajaProduct[], code: string): CajaProduct | null {
  const text = code.trim();
  if (text.length === 0) return null;
  return productBySku(products, text) ?? products.find((product) => product.barcodes.includes(text)) ?? null;
}

/** El producto del catálogo completo (activos e inactivos) con ese código, para explicar por qué no está a la venta. */
export function findLookup(rows: readonly ProductLookupData[], code: string): ProductLookupData | null {
  const text = code.trim();
  if (text.length === 0) return null;
  return rows.find((row) => upper(row.sku) === upper(text)) ?? rows.find((row) => row.barcodes.includes(text)) ?? null;
}

/** Por qué un código leído no agregó nada a la venta. */
export function unknownCodeText(code: string, found: ProductLookupData | null): string {
  const text = code.trim();
  if (!found) return `Ningún producto tiene el código «${text}». Revise el código o busque el producto por su nombre.`;
  if (!found.isActive) return `«${found.name}» (${found.sku}) está inactivo y no se vende. Pida que lo activen en el catálogo.`;
  return `«${found.name}» (${found.sku}) no está a la venta: no tiene precio en la lista de precios vigente.`;
}

// ---------------------------------------------------------------------------------------------------- textos

/** «Serie» o «IMEI» (insignia). */
export function serialLabel(kind: SerialKindData): string {
  return kind === 'Imei' ? 'IMEI' : 'Serie';
}

/** «IMEI» o «serie»/«series» (en una frase). */
export function serialNoun(kind: SerialKindData, count = 2): string {
  if (kind === 'Imei') return 'IMEI';
  return count === 1 ? 'serie' : 'series';
}

/** «3 UND» · «Agotado» · «2 UND (reservado 1)»: disponible = existencias − reservado. */
export function availabilityText(product: CajaProduct): string {
  const reserved = product.reserved > 0 ? ` (reservado ${formatQuantity(product.reserved)})` : '';
  if (product.available <= 0) return product.reserved > 0 ? `Agotado · reservado ${formatQuantity(product.reserved)}` : 'Agotado';
  return `${formatQuantity(product.available, { unit: product.unit })}${reserved}`;
}

/** «Garantía de 12 meses» (o null sin garantía). */
export function warrantyText(months: number): string | null {
  if (!(months > 0)) return null;
  return months === 1 ? 'Garantía de 1 mes' : `Garantía de ${formatNumber(months)} meses`;
}

/** Columnas del CSV de la lista de productos (más completas que la tabla). */
export const PRODUCT_CSV: readonly CsvColumn<CajaProduct>[] = [
  { header: 'SKU', value: (product) => product.sku },
  { header: 'Producto', value: (product) => product.name },
  { header: 'Categoría', value: (product) => product.category },
  { header: 'Marca', value: (product) => product.brand },
  { header: 'Precio (Bs)', value: (product) => product.price },
  { header: 'Disponible', value: (product) => product.available },
  { header: 'Reservado', value: (product) => product.reserved },
  { header: 'Unidad', value: (product) => product.unit },
  { header: 'Lleva serie', value: (product) => (product.serialized ? serialLabel(product.serialKind) : 'No') },
  { header: 'Garantía (meses)', value: (product) => (product.warrantyMonths > 0 ? product.warrantyMonths : null) },
  { header: 'Plataformas', value: (product) => product.platforms.join(', ') },
  { header: 'Códigos de barras', value: (product) => product.barcodes.join(', ') },
];
