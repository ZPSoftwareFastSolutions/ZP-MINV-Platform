// Módulo «Catálogo» · funciones puras (sin React) de la pestaña «Productos»: la fila lista para mostrar (catálogo +
// ficha técnica resumida), los filtros de la dirección, las opciones de las listas desplegables (marca, plataforma,
// condición, facetas), los filtros por especificación que se envían al servidor, el margen (guía, como el escritorio),
// las columnas del CSV, el formulario del producto y su validación, el EAN-13 interno y el resumen del catálogo.
//
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07): se derivan del nombre de la operación.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses } from '@/4-presentation/panel/kit';
import { formatNumber, matchesSearch, roundTo, type CsvColumn } from '@/4-presentation/panel/lib';

/** Un producto de `GetCatalogQuery` tal como lo manda el servidor. */
export type CatalogRecord = RpcResponseOf<'GetCatalogQuery'>[number];
/** Listas del catálogo (categorías, unidades, proveedores, IVA) de `GetCatalogOptionsQuery`. */
export type CatalogOptionsRecord = RpcResponseOf<'GetCatalogOptionsQuery'>;
/** Ficha resumida de `SearchTechProductsQuery` (marca, plataformas, serie, garantía, disponible). Solo productos activos. */
export type TechRecord = RpcResponseOf<'SearchTechProductsQuery'>[number];
/** Una especificación de `GetSpecDefinitionsQuery`. */
export type SpecDefinitionRecord = RpcResponseOf<'GetSpecDefinitionsQuery'>[number];
/** Una faceta (especificación filtrable con sus valores) de `GetSpecFacetsQuery`. */
export type FacetRecord = RpcResponseOf<'GetSpecFacetsQuery'>[number];
/** Un filtro por especificación del pedido de `GetCatalogQuery`. */
export type SpecFilterInput = NonNullable<RpcRequestOf<'GetCatalogQuery'>['specFilters']>[number];
/** Contenido de `SaveProductCommand`. */
export type SaveProductPayload = RpcRequestOf<'SaveProductCommand'>;

export interface Option {
  value: string;
  label: string;
}

// ---------------------------------------------------------------------------------------------------- pedidos fijos

/** Todo el catálogo (sin categoría ni especificaciones: el filtro por categoría va en otro pedido). */
export const ALL_CATALOG: RpcRequestOf<'GetCatalogQuery'> = { categoryCode: null, specFilters: null };

/** Fichas resumidas de todos los productos activos (el servidor admite hasta 2000). */
export const TECH_SEARCH: RpcRequestOf<'SearchTechProductsQuery'> = { text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 };

/** Todas las especificaciones (de todas las categorías). */
export const ALL_DEFINITIONS: RpcRequestOf<'GetSpecDefinitionsQuery'> = { categoryCode: null };

/**
 * Códigos de las especificaciones con filtro propio (regla T-07: sus OPCIONES salen del servidor, no de una lista fija):
 * «plataforma» (juegos y consolas), «plataformas» (accesorios) y «condicion» (nuevo, reacondicionado, usado). Son los
 * `TechSpecCodes` del servidor.
 */
export const PLATFORM_CODES: readonly string[] = ['plataforma', 'plataformas'];
export const CONDITION_CODE = 'condicion';

// ---------------------------------------------------------------------------------------------------- estados y filtros

export const PRODUCT_STATES = defineStatuses({
  activo: { label: 'Activo', tone: 'success' },
  inactivo: { label: 'Inactivo', tone: 'neutral' },
});
export type ProductState = keyof typeof PRODUCT_STATES;

/** Filtros de la lista de productos (en la dirección de la página). */
export const PRODUCT_FILTERS = {
  q: '',
  categoria: '',
  marca: '',
  estado: '',
  imagen: '',
  precio: '',
  serie: '',
  plataforma: '',
  condicion: '',
  espec: '',
};
export type ProductFilters = typeof PRODUCT_FILTERS;

/** Valor del filtro «Marca» para los productos sin marca. */
export const NO_BRAND = '_sin';

export const IMAGE_OPTIONS: readonly Option[] = [
  { value: 'con', label: 'Con imagen' },
  { value: 'sin', label: 'Sin imagen' },
];

/** Margen bajo: menos del 15 % sobre el precio sin IVA (el mismo umbral del escritorio). */
export const LOW_MARGIN = 0.15;

export const PRICE_OPTIONS: readonly Option[] = [
  { value: 'con', label: 'Con precio' },
  { value: 'sin', label: 'Sin precio' },
  { value: 'margen-bajo', label: 'Margen bajo (menos del 15 %)' },
];

export const SERIAL_OPTIONS: readonly Option[] = [
  { value: 'serie', label: 'Con número de serie' },
  { value: 'imei', label: 'Con IMEI' },
  { value: 'no', label: 'Sin serie (por cantidad)' },
];

/** Vista de la lista: tabla o galería con imágenes (`?vista=galeria`). */
export type CatalogView = 'lista' | 'galeria';
export const VIEW_PARAM = 'vista';
export function viewOf(value: string | null): CatalogView {
  return value === 'galeria' ? 'galeria' : 'lista';
}

// ---------------------------------------------------------------------------------------------------- margen (guía)

export interface TaxRule {
  /** Tasa del IVA en porcentaje (13 en Bolivia). */
  rate: number;
  /** El IVA se calcula sobre el importe facturado (Bolivia): el neto es el 87 % del precio. */
  onInvoicedAmount: boolean;
}

export function taxRuleOf(options: CatalogOptionsRecord | undefined): TaxRule {
  return { rate: options?.taxRate ?? 13, onInvoicedAmount: options?.vatOnInvoicedAmount ?? true };
}

/** Precio sin IVA (guía, la misma convención del escritorio y del servidor: en Bolivia, el 87 % del precio). */
export function netOf(price: number, tax: TaxRule): number {
  if (tax.rate <= 0) return price;
  return tax.onInvoicedAmount ? (price * (100 - tax.rate)) / 100 : (price * 100) / (100 + tax.rate);
}

/** Precio con IVA que corresponde a un neto. */
export function grossOf(net: number, tax: TaxRule): number {
  if (tax.rate <= 0) return net;
  return tax.onInvoicedAmount ? (net * 100) / (100 - tax.rate) : (net * (100 + tax.rate)) / 100;
}

/** IVA incluido en el precio (redondeado al centavo). */
export function includedTax(price: number, tax: TaxRule): number {
  if (tax.rate <= 0) return 0;
  return roundTo(tax.onInvoicedAmount ? (price * tax.rate) / 100 : (price * tax.rate) / (100 + tax.rate), 2);
}

/** Margen sobre el precio sin IVA (0,25 = 25 %); null si no hay precio. */
export function marginOf(price: number, cost: number, tax: TaxRule): number | null {
  if (!(price > 0)) return null;
  const net = netOf(price, tax);
  return net > 0 ? (net - cost) / net : null;
}

/** Precio con IVA que da ese margen sobre el costo (redondeado a un decimal, como el escritorio). */
export function priceForMargin(cost: number, margin: number, tax: TaxRule): number {
  return roundTo(grossOf(cost / (1 - margin), tax), 1);
}

/** Márgenes que ofrece «Aplicar margen». */
export const MARGIN_PRESETS: readonly Option[] = [20, 25, 30, 35, 40, 50].map((percent) => ({ value: String(percent), label: `Margen ${percent} %` }));

/** «25 %», «12,5 %» o «—» sin precio. */
export function marginText(margin: number | null): string {
  return margin === null ? '—' : `${formatNumber(roundTo(margin * 100, 1), { maxDecimals: 1 })} %`;
}

// ---------------------------------------------------------------------------------------------------- fila lista para mostrar

export type SerialMode = 'serie' | 'imei' | 'no';

/** Un producto listo para la tabla y la galería: el catálogo más su ficha técnica resumida (si está activo). */
export interface ProductEntry {
  /** El SKU (único). */
  key: string;
  row: CatalogRecord;
  tech: TechRecord | null;
  brand: string | null;
  platforms: readonly string[];
  /** Serie del fabricante, IMEI o sin serie (null si no hay ficha: los inactivos no la traen). */
  serial: SerialMode | null;
  warrantyMonths: number;
  keySpecs: string;
  /** Disponible en la sucursal activa (null si no hay ficha). */
  available: number | null;
  margin: number | null;
  state: ProductState;
}

export function serialOf(tech: TechRecord | null): SerialMode | null {
  if (!tech) return null;
  if (!tech.trackSerials) return 'no';
  return tech.serialKind === 'Imei' ? 'imei' : 'serie';
}

export function toProductEntries(catalog: readonly CatalogRecord[], tech: readonly TechRecord[], tax: TaxRule): ProductEntry[] {
  const bySku = new Map(tech.map((row) => [row.sku.toUpperCase(), row]));
  return catalog.map((row) => {
    const summary = bySku.get(row.sku.toUpperCase()) ?? null;
    return {
      key: row.sku,
      row,
      tech: summary,
      brand: summary?.brand ?? null,
      platforms: summary?.platforms ?? [],
      serial: serialOf(summary),
      warrantyMonths: summary?.warrantyMonths ?? 0,
      keySpecs: summary?.keySpecs ?? '',
      available: summary ? summary.stock : null,
      margin: marginOf(row.salePrice, row.unitCost, tax),
      state: row.isActive ? 'activo' : 'inactivo',
    };
  });
}

export function isLowMargin(entry: ProductEntry): boolean {
  return entry.margin !== null && entry.margin < LOW_MARGIN;
}

/**
 * Filtra en la página. `matched` = SKUs que devolvió el servidor para la categoría (con sus subcategorías), la condición y
 * las especificaciones elegidas (null si no hay ninguno de esos filtros).
 */
export function filterProducts(entries: readonly ProductEntry[], filters: ProductFilters, matched: ReadonlySet<string> | null): ProductEntry[] {
  return entries.filter((entry) => {
    const { row } = entry;
    if (matched && !matched.has(row.sku.toUpperCase())) return false;
    if (filters.estado && entry.state !== filters.estado) return false;
    if (filters.marca && (filters.marca === NO_BRAND ? entry.brand !== null : entry.brand !== filters.marca)) return false;
    if (filters.imagen === 'con' && !row.hasImage) return false;
    if (filters.imagen === 'sin' && row.hasImage) return false;
    if (filters.precio === 'con' && !(row.salePrice > 0)) return false;
    if (filters.precio === 'sin' && row.salePrice > 0) return false;
    if (filters.precio === 'margen-bajo' && !isLowMargin(entry)) return false;
    if (filters.serie && entry.serial !== filters.serie) return false;
    if (filters.plataforma && !entry.platforms.some((platform) => platform.toLowerCase() === filters.plataforma.toLowerCase())) return false;
    return matchesSearch(filters.q, [row.sku, row.name, row.barcode, row.supplier, row.category, entry.brand, row.description]);
  });
}

/** Marcas de los productos cargados (más «Sin marca» si hay alguno sin marca). */
export function brandOptions(entries: readonly ProductEntry[]): Option[] {
  const brands = [...new Set(entries.map((entry) => entry.brand).filter((brand): brand is string => Boolean(brand)))].sort((a, b) => a.localeCompare(b, 'es'));
  const options = brands.map((brand) => ({ value: brand, label: brand }));
  if (entries.some((entry) => entry.tech !== null && entry.brand === null)) options.push({ value: NO_BRAND, label: 'Sin marca' });
  return options;
}

/** Opciones de las especificaciones con esos códigos (sin repetir, en el orden del servidor). */
export function specOptions(definitions: readonly SpecDefinitionRecord[], codes: readonly string[]): Option[] {
  const seen = new Set<string>();
  const options: Option[] = [];
  for (const code of codes) {
    for (const definition of definitions.filter((item) => item.code === code)) {
      for (const option of definition.options) {
        const key = option.toLowerCase();
        if (seen.has(key)) continue;
        seen.add(key);
        options.push({ value: option, label: option });
      }
    }
  }
  return options;
}

/** Categorías para la lista desplegable, con cuántos productos propios tiene cada una (una madre puede no tener). */
export function categoryOptions(options: CatalogOptionsRecord | undefined, catalog: readonly CatalogRecord[]): Option[] {
  const own = new Map<string, number>();
  for (const row of catalog) own.set(row.categoryCode, (own.get(row.categoryCode) ?? 0) + 1);
  return (options?.categories ?? []).map((category) => {
    const count = own.get(category.code) ?? 0;
    return { value: category.code, label: count > 0 ? `${category.name} (${formatNumber(count)})` : category.name };
  });
}

// ---------------------------------------------------------------------------------------------------- especificaciones

/** Una elección de faceta guardada en la dirección: `?espec=socket:AM5|vram:8`. */
export interface SpecChoice {
  code: string;
  value: string;
}

/** Lee `?espec=` (pares código:valor separados por «|»; cada parte va codificada). */
export function parseSpecChoices(text: string): SpecChoice[] {
  if (!text) return [];
  const choices: SpecChoice[] = [];
  for (const part of text.split('|')) {
    const separator = part.indexOf(':');
    if (separator <= 0) continue;
    try {
      const code = decodeURIComponent(part.slice(0, separator));
      const value = decodeURIComponent(part.slice(separator + 1));
      if (code && value && !choices.some((choice) => choice.code === code)) choices.push({ code, value });
    } catch {
      // Una parte mal escrita a mano en la dirección se ignora.
    }
  }
  return choices;
}

export function writeSpecChoices(choices: readonly SpecChoice[]): string {
  return choices
    .filter((choice) => choice.code && choice.value)
    .map((choice) => `${encodeURIComponent(choice.code)}:${encodeURIComponent(choice.value)}`)
    .join('|');
}

/** Cambia (o quita, con '') la elección de una faceta. */
export function withSpecChoice(text: string, code: string, value: string): string {
  const others = parseSpecChoices(text).filter((choice) => choice.code !== code);
  return writeSpecChoices(value ? [...others, { code, value }] : others);
}

/** Filtros por especificación para el servidor: las facetas elegidas y la condición. */
export function specFiltersOf(filters: Pick<ProductFilters, 'espec' | 'condicion'>): SpecFilterInput[] {
  const list: SpecFilterInput[] = parseSpecChoices(filters.espec)
    .filter((choice) => choice.code !== CONDITION_CODE)
    .map((choice) => ({ code: choice.code, values: [choice.value], min: null, max: null }));
  if (filters.condicion) list.push({ code: CONDITION_CODE, values: [filters.condicion], min: null, max: null });
  return list;
}

/** ¿Hay que pedir al servidor qué productos cumplen la categoría, la condición o las especificaciones? */
export function needsServerFilter(filters: Pick<ProductFilters, 'categoria' | 'espec' | 'condicion'>): boolean {
  return Boolean(filters.categoria) || specFiltersOf(filters).length > 0;
}

/** Facetas que se ofrecen como listas (plataforma y condición tienen su filtro propio). */
export function visibleFacets(facets: readonly FacetRecord[]): FacetRecord[] {
  return facets.filter((facet) => !PLATFORM_CODES.includes(facet.code) && facet.code !== CONDITION_CODE && facet.values.length > 0);
}

/** Número guardado con punto («3.5») → como se lee en Bolivia («3,5»). Un texto que no es número queda igual. */
export function showNumber(raw: string): string {
  return /^-?\d+(\.\d+)?$/.test(raw.trim()) ? formatNumber(Number(raw), { maxDecimals: 4 }) : raw;
}

/** Opciones de una faceta: «16 GB (4)». */
export function facetOptions(facet: FacetRecord): Option[] {
  return facet.values.map((item) => {
    const number = facet.dataType === 'Number';
    const text = number ? showNumber(item.value) : item.value;
    const unit = number && facet.unit ? ` ${facet.unit}` : '';
    return { value: item.value, label: `${text}${unit} (${formatNumber(item.count)})` };
  });
}

// ---------------------------------------------------------------------------------------------------- textos

export function serialText(entry: Pick<ProductEntry, 'serial'>): string {
  switch (entry.serial) {
    case 'serie':
      return 'Número de serie';
    case 'imei':
      return 'IMEI';
    case 'no':
      return 'Sin serie';
    default:
      return '—';
  }
}

export function warrantyText(months: number): string {
  if (!(months > 0)) return 'Sin garantía';
  if (months % 12 === 0) return months === 12 ? '1 año de garantía' : `${months / 12} años de garantía`;
  return months === 1 ? '1 mes de garantía' : `${months} meses de garantía`;
}

export function priceText(price: number, format: (value: number) => string): string {
  return price > 0 ? format(price) : 'Sin precio';
}

// ---------------------------------------------------------------------------------------------------- CSV

export const PRODUCTS_CSV: readonly CsvColumn<ProductEntry>[] = [
  { header: 'SKU', value: (entry) => entry.row.sku },
  { header: 'Producto', value: (entry) => entry.row.name },
  { header: 'Categoría', value: (entry) => entry.row.category },
  { header: 'Marca', value: (entry) => entry.brand },
  { header: 'Unidad', value: (entry) => entry.row.unit },
  { header: 'Proveedor', value: (entry) => entry.row.supplier ?? 'Sin proveedor' },
  { header: 'Costo', value: (entry) => entry.row.unitCost },
  { header: 'Precio (IVA incl.)', value: (entry) => entry.row.salePrice },
  { header: 'Margen', value: (entry) => (entry.margin === null ? null : roundTo(entry.margin, 4)) },
  { header: 'Mínimo', value: (entry) => entry.row.minimum },
  { header: 'Máximo', value: (entry) => entry.row.maximum },
  { header: 'Disponible', value: (entry) => entry.available },
  { header: 'Posición', value: (entry) => entry.row.binCode },
  { header: 'Código de barras', value: (entry) => entry.row.barcode },
  { header: 'Activo', value: (entry) => entry.row.isActive },
  { header: 'Imagen', value: (entry) => entry.row.hasImage },
  { header: 'Serie', value: (entry) => (entry.serial === null ? null : serialText(entry)) },
  { header: 'Garantía (meses)', value: (entry) => (entry.tech ? entry.warrantyMonths : null) },
  { header: 'Plataformas', value: (entry) => entry.platforms.join(', ') },
];

// ---------------------------------------------------------------------------------------------------- galería

/** Orden de la galería (no tiene encabezados): cada opción es el orden de una columna de la tabla. */
export const GALLERY_SORTS: readonly (Option & { column: string; direction: 'asc' | 'desc' })[] = [
  { value: 'producto-asc', label: 'Nombre (A → Z)', column: 'producto', direction: 'asc' },
  { value: 'precio-desc', label: 'Precio: mayor a menor', column: 'precio', direction: 'desc' },
  { value: 'precio-asc', label: 'Precio: menor a mayor', column: 'precio', direction: 'asc' },
  { value: 'disponible-desc', label: 'Disponible: mayor a menor', column: 'disponible', direction: 'desc' },
  { value: 'margen-desc', label: 'Margen: mayor a menor', column: 'margen', direction: 'desc' },
  { value: 'categoria-asc', label: 'Categoría', column: 'categoria', direction: 'asc' },
];

// ---------------------------------------------------------------------------------------------------- resumen

export interface CatalogSummary {
  total: number;
  active: number;
  withImage: number;
  withoutImage: number;
  withoutPrice: number;
  lowMargin: number;
  /** Precio promedio de los activos con precio. */
  averagePrice: number | null;
  /** Margen promedio de los activos con precio. */
  averageMargin: number | null;
  /** Productos por categoría (de más a menos). */
  byCategory: { label: string; value: number }[];
}

export function catalogSummary(entries: readonly ProductEntry[]): CatalogSummary {
  const active = entries.filter((entry) => entry.row.isActive);
  const priced = active.filter((entry) => entry.row.salePrice > 0);
  const margins = priced.map((entry) => entry.margin).filter((margin): margin is number => margin !== null);
  const categories = new Map<string, number>();
  for (const entry of active) categories.set(entry.row.category, (categories.get(entry.row.category) ?? 0) + 1);
  return {
    total: entries.length,
    active: active.length,
    withImage: entries.filter((entry) => entry.row.hasImage).length,
    withoutImage: entries.filter((entry) => !entry.row.hasImage).length,
    withoutPrice: active.filter((entry) => !(entry.row.salePrice > 0)).length,
    lowMargin: priced.filter(isLowMargin).length,
    averagePrice: priced.length > 0 ? priced.reduce((sum, entry) => sum + entry.row.salePrice, 0) / priced.length : null,
    averageMargin: margins.length > 0 ? margins.reduce((sum, margin) => sum + margin, 0) / margins.length : null,
    byCategory: [...categories.entries()].map(([label, value]) => ({ label, value })).sort((a, b) => b.value - a.value || a.label.localeCompare(b.label, 'es')),
  };
}

// ---------------------------------------------------------------------------------------------------- formulario

export interface ProductDraft {
  sku: string;
  name: string;
  description: string;
  categoryCode: string;
  unitCode: string;
  supplierCode: string;
  binCode: string;
  barcode: string;
  isActive: boolean;
  unitCost: number | null;
  salePrice: number | null;
  minimum: number | null;
  maximum: number | null;
}

export const LIMITS = { sku: 40, name: 150, description: 1000, barcode: 64 } as const;

/** Unidad por defecto de un producto nuevo (la del escritorio). */
export const DEFAULT_UNIT = 'UND';

export function draftOf(row: CatalogRecord | null, options: CatalogOptionsRecord | undefined): ProductDraft {
  if (row) {
    return {
      sku: row.sku,
      name: row.name,
      description: row.description ?? '',
      categoryCode: row.categoryCode,
      unitCode: row.unit,
      supplierCode: row.supplierCode ?? '',
      binCode: row.binCode ?? '',
      barcode: row.barcode ?? '',
      isActive: row.isActive,
      unitCost: row.unitCost,
      salePrice: row.salePrice,
      minimum: row.minimum,
      maximum: row.maximum,
    };
  }
  const units = options?.units ?? [];
  return {
    sku: '',
    name: '',
    description: '',
    categoryCode: '',
    unitCode: units.find((unit) => unit.code === DEFAULT_UNIT)?.code ?? units[0]?.code ?? '',
    supplierCode: '',
    binCode: '',
    barcode: '',
    isActive: true,
    unitCost: null,
    salePrice: null,
    minimum: 0,
    maximum: 0,
  };
}

export type ProductField = 'sku' | 'name' | 'description' | 'categoryCode' | 'unitCode' | 'barcode' | 'unitCost' | 'salePrice' | 'minimum' | 'maximum';
export type ProductProblems = Partial<Record<ProductField, string>>;

const CODE_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_-]*$/;

/** Las mismas reglas del servidor (SaveProductValidator y Guard.Code), para avisar antes de enviar. */
export function productProblems(draft: ProductDraft, isNew: boolean): ProductProblems {
  const problems: ProductProblems = {};
  const sku = draft.sku.trim();
  if (isNew) {
    if (!sku) problems.sku = 'Indique el SKU.';
    else if (sku.length > LIMITS.sku) problems.sku = `Use como máximo ${LIMITS.sku} caracteres.`;
    else if (!CODE_PATTERN.test(sku)) problems.sku = 'Solo letras, números, guion y guion bajo (sin espacios).';
  }
  const name = draft.name.trim();
  if (!name) problems.name = 'Indique el nombre.';
  else if (name.length < 2) problems.name = 'El nombre es muy corto.';
  else if (name.length > LIMITS.name) problems.name = `Use como máximo ${LIMITS.name} caracteres.`;
  if (draft.description.trim().length > LIMITS.description) problems.description = `Use como máximo ${LIMITS.description} caracteres.`;
  if (!draft.categoryCode) problems.categoryCode = 'Elija la categoría.';
  if (!draft.unitCode) problems.unitCode = 'Elija la unidad.';
  if (draft.barcode.trim().length > LIMITS.barcode) problems.barcode = `Use como máximo ${LIMITS.barcode} caracteres.`;
  if (draft.unitCost === null) problems.unitCost = 'Indique el costo (0 si todavía no lo sabe).';
  if (draft.salePrice === null) problems.salePrice = 'Indique el precio de venta (0 = sin precio).';
  if (draft.minimum === null) problems.minimum = 'Indique el mínimo (0 = sin mínimo).';
  if (draft.maximum === null) problems.maximum = 'Indique el máximo (0 = sin máximo).';
  if (draft.minimum !== null && draft.maximum !== null && draft.maximum > 0 && draft.maximum < draft.minimum) {
    problems.maximum = 'El máximo debe ser mayor o igual al mínimo.';
  }
  return problems;
}

/** Pestaña del formulario donde está cada campo (para llevar a la persona al primer error). */
export const FIELD_TAB: Readonly<Record<ProductField, 'datos' | 'precios' | 'stock'>> = {
  sku: 'datos',
  name: 'datos',
  description: 'datos',
  categoryCode: 'datos',
  unitCode: 'datos',
  barcode: 'datos',
  unitCost: 'precios',
  salePrice: 'precios',
  minimum: 'stock',
  maximum: 'stock',
};

function textOrNull(value: string): string | null {
  const text = value.trim();
  return text.length > 0 ? text : null;
}

/**
 * `SaveProductCommand` con TODOS sus parámetros. `originalSku` null = producto nuevo; el SKU y la unidad no cambian
 * después de crearlo (el servidor ignora el cambio).
 */
export function toSaveProduct(originalSku: string | null, draft: ProductDraft): SaveProductPayload {
  return {
    originalSku,
    sku: (originalSku ?? draft.sku).trim().toUpperCase(),
    name: draft.name.trim(),
    description: textOrNull(draft.description),
    categoryCode: draft.categoryCode,
    unitCode: draft.unitCode,
    supplierCode: textOrNull(draft.supplierCode),
    minimum: draft.minimum ?? 0,
    maximum: draft.maximum ?? 0,
    unitCost: draft.unitCost ?? 0,
    salePrice: draft.salePrice ?? 0,
    barcode: textOrNull(draft.barcode),
    isActive: draft.isActive,
    binCode: textOrNull(draft.binCode),
  };
}

/** Activar o desactivar un producto: el mismo producto con `isActive` cambiado (el escritorio lo hace desde el editor). */
export function toggleActivePayload(row: CatalogRecord, isActive: boolean): SaveProductPayload {
  return { ...toSaveProduct(row.sku, draftOf(row, undefined)), isActive };
}

// ---------------------------------------------------------------------------------------------------- códigos

/** Dígito de control GTIN (módulo 10 con pesos 3 y 1 desde la derecha), como `BarcodeRules` del servidor. */
export function gtinCheckDigit(digits: string): string {
  let sum = 0;
  for (let index = 0; index < digits.length; index++) {
    const digit = Number(digits[digits.length - 1 - index]);
    sum += index % 2 === 0 ? digit * 3 : digit;
  }
  return String((10 - (sum % 10)) % 10);
}

/** EAN-13 interno (prefijo 2: uso en tienda, no choca con los códigos de los fabricantes). */
export function newEan13(random: (max: number) => number = secureRandom): string {
  let digits = '2';
  while (digits.length < 12) digits += String(random(10));
  return digits + gtinCheckDigit(digits);
}

function secureRandom(max: number): number {
  const values = new Uint32Array(1);
  crypto.getRandomValues(values);
  return values[0] % max;
}

/** Letras sin acentos en mayúsculas («Periféricos» → «PERIFERICOS»). */
function asciiLetters(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .replace(/[^A-Za-z]/g, '')
    .toUpperCase();
}

/** Código corto y único para una categoría nueva («Periféricos» → PER, PER2…), como el escritorio. */
export function suggestCategoryCode(name: string, existing: readonly string[]): string {
  const letters = asciiLetters(name);
  const base = letters.length >= 3 ? letters.slice(0, 3) : `${letters}CAT`.slice(0, 3);
  const taken = new Set(existing.map((code) => code.toUpperCase()));
  let code = base;
  for (let index = 2; taken.has(code); index++) code = `${base}${index}`;
  return code;
}
