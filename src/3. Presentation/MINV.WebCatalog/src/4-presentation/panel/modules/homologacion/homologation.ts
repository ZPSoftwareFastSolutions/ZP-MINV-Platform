// Módulo «Homologación» · funciones puras (sin React): estados de la homologación de productos, unidades y medios de pago
// (los del escritorio: `HomologationViewModel`), filtros de las tres pestañas, actividades vigentes, textos de los códigos
// del SIN, sugerencias para aceptar en lote, resumen y columnas del CSV. El SIN decide los códigos: aquí solo se muestra,
// se filtra y se arma el pedido.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { matchesSearch, type CsvColumn } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del contrato

export type HomologationData = RpcResponseOf<'GetHomologationQuery'>;
export type ProductRowData = HomologationData['products'][number];
export type UnitRowData = HomologationData['units'][number];
export type MethodRowData = HomologationData['paymentMethods'][number];
export type ActivityData = HomologationData['activities'][number];
export type SinItemData = HomologationData['sinUnits'][number];
export type SinProductData = RpcResponseOf<'SearchSiatProductsQuery'>[number];
export type SuggestionData = RpcResponseOf<'SuggestProductHomologationQuery'>[number];
export type HomologationInput = RpcRequestOf<'SaveProductHomologationCommand'>['items'][number];

/** Documento sector de la factura compra venta (la actividad que se propone primero). */
export const SECTOR_PURCHASE_SALE = 1;

// ---------------------------------------------------------------------------------------------------- estados

export const PRODUCT_STATUSES = defineStatuses({
  pendiente: { label: 'Sin homologar', tone: 'warning' },
  homologado: { label: 'Homologado', tone: 'success' },
  inactivo: { label: 'Inactivo', tone: 'neutral' },
});

export const CODE_STATUSES = defineStatuses({
  pendiente: { label: 'Sin homologar', tone: 'warning' },
  homologado: { label: 'Homologado', tone: 'success' },
});

/** Estado de un producto: inactivo, sin homologar o homologado. */
export function productStatus(row: Pick<ProductRowData, 'isActive' | 'sinProductCode'>): 'pendiente' | 'homologado' | 'inactivo' {
  if (!row.isActive) return 'inactivo';
  return row.sinProductCode === null ? 'pendiente' : 'homologado';
}

export function codeStatus(code: number | null): 'pendiente' | 'homologado' {
  return code === null ? 'pendiente' : 'homologado';
}

/** «FACTURA MAL EMITIDA» → «Factura mal emitida». */
export function sentenceCase(text: string): string {
  const lower = text.trim().toLocaleLowerCase('es');
  return lower.charAt(0).toLocaleUpperCase('es') + lower.slice(1);
}

/** «83141 · Mouse» · «Sin homologar». */
export function sinText(code: number | null, description: string | null): string {
  if (code === null) return 'Sin homologar';
  return `${code} · ${description ? sentenceCase(description) : '(sin descripción)'}`;
}

/** Quita el ✔ / ⚠ / ✖ del principio de un mensaje del servidor (la pantalla pone su propio ícono). */
export function plainMessage(text: string | null | undefined): string {
  return (text ?? '').replace(/^[\s✔⚠✖]+/u, '').trim();
}

// ---------------------------------------------------------------------------------------------------- actividades

/** Actividades económicas vigentes, para la lista desplegable. */
export function activityOptions(activities: readonly ActivityData[] | undefined): { value: string; label: string }[] {
  return (activities ?? []).filter((activity) => activity.isCurrent).map((activity) => ({ value: activity.code, label: `${activity.code} · ${sentenceCase(activity.description)}` }));
}

/** La actividad que se propone: la del producto, la primera que factura compra venta o la primera vigente. */
export function defaultActivity(activities: readonly ActivityData[] | undefined, preferred?: string | null): string {
  const current = (activities ?? []).filter((activity) => activity.isCurrent);
  return (
    current.find((activity) => activity.code === preferred)?.code ??
    current.find((activity) => activity.sectors.includes(SECTOR_PURCHASE_SALE))?.code ??
    current[0]?.code ??
    ''
  );
}

/** Primera palabra del nombre del producto: la búsqueda con que se abre «Asignar código del SIN» (como el escritorio). */
export function firstWord(name: string): string {
  return name.trim().split(/\s+/)[0] ?? '';
}

// ---------------------------------------------------------------------------------------------------- filtros

/** Filtros de la pestaña «Productos» (en la dirección). */
export const PRODUCT_FILTERS = { q: '', estado: '', categoria: '', actividad: '' };
export type ProductFilters = typeof PRODUCT_FILTERS;

export function filterProducts(rows: readonly ProductRowData[], filters: ProductFilters): ProductRowData[] {
  return rows.filter(
    (row) =>
      (!filters.estado || productStatus(row) === filters.estado) &&
      (!filters.categoria || row.category === filters.categoria) &&
      (!filters.actividad || (filters.actividad === '-' ? row.activityCode === null : row.activityCode === filters.actividad)) &&
      matchesSearch(filters.q, [row.sku, row.name, row.category, row.sinProductCode, row.sinProductDescription]),
  );
}

export function categoryOptions(rows: readonly ProductRowData[]): { value: string; label: string }[] {
  return [...new Set(rows.map((row) => row.category))].sort((a, b) => a.localeCompare(b, 'es')).map((name) => ({ value: name, label: name }));
}

/** Actividades de los productos (y «Sin actividad»). */
export function productActivityOptions(rows: readonly ProductRowData[]): { value: string; label: string }[] {
  const codes = [...new Set(rows.map((row) => row.activityCode).filter((code): code is string => code !== null))].sort();
  return [...codes.map((code) => ({ value: code, label: code })), ...(rows.some((row) => row.activityCode === null) ? [{ value: '-', label: 'Sin actividad' }] : [])];
}

/** Filtros de las pestañas «Unidades» (prefijo `u_`) y «Medios de pago» (prefijo `m_`). */
export const CODE_FILTERS = { q: '', estado: '' };
export type CodeFilters = typeof CODE_FILTERS;

export function filterUnits(rows: readonly UnitRowData[], filters: CodeFilters): UnitRowData[] {
  return rows.filter((row) => (!filters.estado || codeStatus(row.sinUnitCode) === filters.estado) && matchesSearch(filters.q, [row.code, row.name, row.sinUnitCode, row.sinUnitDescription]));
}

export function filterMethods(rows: readonly MethodRowData[], filters: CodeFilters): MethodRowData[] {
  return rows.filter((row) => (!filters.estado || codeStatus(row.sinCode) === filters.estado) && matchesSearch(filters.q, [row.code, row.name, row.sinCode, row.sinDescription]));
}

/** Códigos vigentes de un catálogo del SIN (unidades o métodos de pago), para la lista desplegable. */
export function sinOptions(items: readonly SinItemData[]): { value: string; label: string }[] {
  return items
    .filter((item) => item.isCurrent)
    .sort((a, b) => a.code - b.code)
    .map((item) => ({ value: String(item.code), label: `${item.code} · ${sentenceCase(item.description)}` }));
}

// ---------------------------------------------------------------------------------------------------- resumen

export interface HomologationSummary {
  pending: number;
  activeProducts: number;
  homologatedProducts: number;
  units: number;
  unitsDone: number;
  methods: number;
  methodsDone: number;
}

export function homologationSummary(view: HomologationData): HomologationSummary {
  const active = view.products.filter((row) => row.isActive).length;
  return {
    pending: view.pendingProducts,
    activeProducts: active,
    homologatedProducts: active - view.pendingProducts,
    units: view.units.length,
    unitsDone: view.units.filter((row) => row.sinUnitCode !== null).length,
    methods: view.paymentMethods.length,
    methodsDone: view.paymentMethods.filter((row) => row.sinCode !== null).length,
  };
}

// ---------------------------------------------------------------------------------------------------- sugerencias

/** Una sugerencia lista para revisar: el producto, el código del SIN con su descripción y si se acepta. */
export interface SuggestionItem {
  input: HomologationInput;
  productText: string;
  sinText: string;
  accepted: boolean;
}

/** Arma las sugerencias con el nombre del producto y la descripción del catálogo del SIN de la actividad. */
export function toSuggestions(suggestions: readonly SuggestionData[], products: readonly ProductRowData[], catalog: readonly SinProductData[]): SuggestionItem[] {
  const names = new Map(products.map((row) => [row.sku.toUpperCase(), row.name]));
  const descriptions = new Map<number, string>();
  for (const item of catalog) if (!descriptions.has(item.productCode)) descriptions.set(item.productCode, item.description);
  return suggestions.map((suggestion) => ({
    input: { sku: suggestion.sku, activityCode: suggestion.activityCode, sinProductCode: suggestion.sinProductCode },
    productText: `${suggestion.sku} · ${names.get(suggestion.sku.toUpperCase()) ?? suggestion.sku}`,
    sinText: sinText(suggestion.sinProductCode, descriptions.get(suggestion.sinProductCode) ?? null),
    accepted: true,
  }));
}

// ---------------------------------------------------------------------------------------------------- CSV

export const PRODUCT_CSV: readonly CsvColumn<ProductRowData>[] = [
  { header: 'SKU', value: (row) => row.sku },
  { header: 'Producto', value: (row) => row.name },
  { header: 'Categoría', value: (row) => row.category },
  { header: 'Actividad', value: (row) => row.activityCode },
  { header: 'Código del SIN', value: (row) => row.sinProductCode },
  { header: 'Descripción del SIN', value: (row) => row.sinProductDescription },
  { header: 'Estado', value: (row) => statusOf(PRODUCT_STATUSES, productStatus(row)).label },
];

export const UNIT_CSV: readonly CsvColumn<UnitRowData>[] = [
  { header: 'Código', value: (row) => row.code },
  { header: 'Unidad', value: (row) => row.name },
  { header: 'Unidad del SIN', value: (row) => row.sinUnitCode },
  { header: 'Descripción del SIN', value: (row) => row.sinUnitDescription },
  { header: 'Estado', value: (row) => statusOf(CODE_STATUSES, codeStatus(row.sinUnitCode)).label },
];

export const METHOD_CSV: readonly CsvColumn<MethodRowData>[] = [
  { header: 'Código', value: (row) => row.code },
  { header: 'Medio de pago', value: (row) => row.name },
  { header: 'Método del SIN', value: (row) => row.sinCode },
  { header: 'Descripción del SIN', value: (row) => row.sinDescription },
  { header: 'Estado', value: (row) => statusOf(CODE_STATUSES, codeStatus(row.sinCode)).label },
];
