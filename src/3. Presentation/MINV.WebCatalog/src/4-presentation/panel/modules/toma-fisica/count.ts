// Módulo «Toma física» · funciones puras (sin React): la planilla abierta lista para mostrar (diferencias en palabras y en
// color), los filtros (categoría, ubicación, diferencia, quién contó y búsqueda), los productos que faltan contar, el
// progreso, el resumen de diferencias que se confirma antes de generar los ajustes, el lector de códigos (SKU o código de
// barras), los contenidos EXACTOS de los comandos y las columnas del CSV y de la planilla impresa.
//
// La diferencia y el stock exacto los calcula el SERVIDOR (`GetOpenPhysicalCountQuery` y `PostPhysicalCountCommand`,
// regla P-01); aquí solo se muestran, se filtran y se cuentan para el resumen.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses } from '@/4-presentation/panel/kit';
import { formatNumber, matchesSearch, toDate, type CsvColumn } from '@/4-presentation/panel/lib';

/** La toma abierta de `GetOpenPhysicalCountQuery` (el servidor responde null si no hay ninguna). */
export type CountSheetRecord = NonNullable<RpcResponseOf<'GetOpenPhysicalCountQuery'>>;
export type CountLineRecord = CountSheetRecord['lines'][number];
/** Un producto de `GetProductLookupQuery` (SKU, nombre, códigos de barras, categoría, posición principal, unidad). */
export type LookupRecord = RpcResponseOf<'GetProductLookupQuery'>[number];
export type BinRecord = RpcResponseOf<'GetBinsQuery'>[number];
export type CardRecord = RpcResponseOf<'GetProductCardQuery'>;
export type BranchRecord = RpcResponseOf<'GetBranchesQuery'>[number];
export type PostResultRecord = RpcResponseOf<'PostPhysicalCountCommand'>;

export interface Option {
  value: string;
  label: string;
}

/** Lote por defecto del servidor (`Batch.DefaultLotNumber`): los conteos sin lote van a él. */
export const DEFAULT_LOT = 'SIN-LOTE';

// ---------------------------------------------------------------------------------------------------- diferencias

export const DIFFERENCES = defineStatuses({
  cuadra: { label: 'Cuadra', tone: 'success' },
  sobrante: { label: 'Sobrante', tone: 'info' },
  faltante: { label: 'Faltante', tone: 'danger' },
});
export type DifferenceKind = keyof typeof DIFFERENCES;

export function kindOf(difference: number): DifferenceKind {
  if (difference > 0) return 'sobrante';
  if (difference < 0) return 'faltante';
  return 'cuadra';
}

/** «Cuadra», «+3 UND», «−2 UND». */
export function differenceText(difference: number, unit?: string): string {
  if (difference === 0) return 'Cuadra';
  const sign = difference > 0 ? '+' : '−';
  const amount = formatNumber(Math.abs(difference), { maxDecimals: 3 });
  return unit ? `${sign}${amount} ${unit}` : `${sign}${amount}`;
}

// ---------------------------------------------------------------------------------------------------- planilla

/** Una línea de la planilla lista para la tabla. */
export interface CountEntry {
  key: string;
  line: CountLineRecord;
  /** Categoría del producto (del catálogo de búsqueda; null si ya no está activo). */
  category: string | null;
  kind: DifferenceKind;
}

export function lineKey(line: Pick<CountLineRecord, 'sku' | 'binCode' | 'lotNumber'>): string {
  return `${line.sku}|${line.binCode}|${line.lotNumber}`;
}

export function toCountEntries(sheet: CountSheetRecord | null | undefined, products: readonly LookupRecord[]): CountEntry[] {
  const categories = new Map(products.map((product) => [product.sku.toUpperCase(), product.category]));
  return (sheet?.lines ?? []).map((line) => ({
    key: lineKey(line),
    line,
    category: categories.get(line.sku.toUpperCase()) ?? null,
    kind: kindOf(line.difference),
  }));
}

/** Filtros de la pantalla (en la dirección de la página). */
export const COUNT_FILTERS = { q: '', categoria: '', ubicacion: '', diferencia: '', contador: '' };
export type CountFilters = typeof COUNT_FILTERS;

export const DIFFERENCE_OPTIONS: readonly Option[] = [
  { value: 'con', label: 'Con diferencia' },
  { value: 'sobrante', label: 'Sobrantes' },
  { value: 'faltante', label: 'Faltantes' },
  { value: 'cuadra', label: 'Cuadran' },
];

export function filterEntries(entries: readonly CountEntry[], filters: CountFilters): CountEntry[] {
  return entries.filter((entry) => {
    if (filters.categoria && entry.category !== filters.categoria) return false;
    if (filters.ubicacion && entry.line.binCode !== filters.ubicacion) return false;
    if (filters.diferencia === 'con' && entry.kind === 'cuadra') return false;
    if (filters.diferencia && filters.diferencia !== 'con' && entry.kind !== filters.diferencia) return false;
    if (filters.contador && (entry.line.countedBy ?? '') !== filters.contador) return false;
    return matchesSearch(filters.q, [entry.line.sku, entry.line.name, entry.line.binCode, entry.line.countedBy]);
  });
}

/** Categorías de los productos (orden alfabético). */
export function categoryOptions(products: readonly LookupRecord[]): Option[] {
  return [...new Set(products.map((product) => product.category))].sort((a, b) => a.localeCompare(b, 'es')).map((category) => ({ value: category, label: category }));
}

/** Posiciones del almacén de la toma. */
export function binOptions(bins: readonly BinRecord[]): Option[] {
  return bins.map((bin) => ({ value: bin.code, label: bin.zone ? `${bin.code} · ${bin.zone}` : bin.code }));
}

/** Quiénes contaron (para el filtro). */
export function counterOptions(entries: readonly CountEntry[]): Option[] {
  return [...new Set(entries.map((entry) => entry.line.countedBy).filter((name): name is string => Boolean(name)))]
    .sort((a, b) => a.localeCompare(b, 'es'))
    .map((name) => ({ value: name, label: name }));
}

// ---------------------------------------------------------------------------------------------------- pendientes y progreso

/** Productos del alcance elegido (categoría y ubicación = su posición principal). */
export function productsInScope(products: readonly LookupRecord[], filters: Pick<CountFilters, 'categoria' | 'ubicacion'>): LookupRecord[] {
  return products.filter((product) => (!filters.categoria || product.category === filters.categoria) && (!filters.ubicacion || product.primaryBin === filters.ubicacion));
}

/** SKUs que ya tienen algún conteo en la toma. */
export function countedSkus(sheet: CountSheetRecord | null | undefined): Set<string> {
  return new Set((sheet?.lines ?? []).map((line) => line.sku.toUpperCase()));
}

/** Productos del alcance que todavía no se contaron (con la búsqueda aplicada). */
export function pendingProducts(products: readonly LookupRecord[], sheet: CountSheetRecord | null | undefined, filters: CountFilters): LookupRecord[] {
  const counted = countedSkus(sheet);
  return productsInScope(products, filters).filter(
    (product) => !counted.has(product.sku.toUpperCase()) && matchesSearch(filters.q, [product.sku, product.name, product.category, product.primaryBin, ...product.barcodes]),
  );
}

export interface CountProgress {
  /** Productos del alcance. */
  expected: number;
  /** De ellos, contados. */
  counted: number;
  /** 0 a 100. */
  percent: number;
}

export function countProgress(products: readonly LookupRecord[], sheet: CountSheetRecord | null | undefined, filters: Pick<CountFilters, 'categoria' | 'ubicacion'>): CountProgress {
  const scope = productsInScope(products, filters);
  const counted = countedSkus(sheet);
  const done = scope.filter((product) => counted.has(product.sku.toUpperCase())).length;
  return { expected: scope.length, counted: done, percent: scope.length === 0 ? 0 : Math.round((done / scope.length) * 100) };
}

// ---------------------------------------------------------------------------------------------------- resumen

export interface CountSummary {
  lines: number;
  /** Productos distintos contados. */
  products: number;
  surpluses: number;
  shortages: number;
  matching: number;
  /** Las líneas con diferencia, de la mayor a la menor (en valor absoluto). */
  differences: CountLineRecord[];
}

export function countSummary(sheet: CountSheetRecord | null | undefined): CountSummary {
  const lines = sheet?.lines ?? [];
  return {
    lines: lines.length,
    products: new Set(lines.map((line) => line.sku.toUpperCase())).size,
    surpluses: lines.filter((line) => line.difference > 0).length,
    shortages: lines.filter((line) => line.difference < 0).length,
    matching: lines.filter((line) => line.difference === 0).length,
    differences: lines.filter((line) => line.difference !== 0).sort((a, b) => Math.abs(b.difference) - Math.abs(a.difference) || a.sku.localeCompare(b.sku, 'es')),
  };
}

// ---------------------------------------------------------------------------------------------------- lector de códigos

/** Producto por SKU o código de barras exactos (el lector escribe el código y Enter). */
export function findByCode(products: readonly LookupRecord[], code: string): LookupRecord | null {
  const text = code.trim();
  if (!text) return null;
  const upper = text.toUpperCase();
  return products.find((product) => product.sku.toUpperCase() === upper) ?? products.find((product) => product.barcodes.some((barcode) => barcode === text)) ?? null;
}

/** Lo que dice el sistema en esa posición (lote por defecto), para la guía antes de registrar. */
export function systemAt(card: CardRecord | undefined, binCode: string): number | null {
  if (!card || !binCode) return null;
  return card.bins.filter((bin) => bin.binCode === binCode && bin.lotNumber === DEFAULT_LOT).reduce((sum, bin) => sum + bin.onHand, 0);
}

/** Posición con la que se abre un producto: la principal, o la primera donde tiene existencias. */
export function defaultBin(product: LookupRecord | null, card: CardRecord | undefined, bins: readonly BinRecord[]): string {
  const known = (code: string | null | undefined) => (code && (bins.length === 0 || bins.some((bin) => bin.code === code)) ? code : null);
  return known(card?.primaryBin) ?? known(product?.primaryBin) ?? known(card?.bins[0]?.binCode) ?? bins[0]?.code ?? '';
}

// ---------------------------------------------------------------------------------------------------- comandos

export function toOpenCount(warehouseCode: string, countDate: string, notes: string): RpcRequestOf<'OpenPhysicalCountCommand'> {
  const text = notes.trim();
  return { warehouseCode, countDate: countDate || null, notes: text.length > 0 ? text : null };
}

export function toRecordCount(physicalCountId: string, sku: string, binCode: string, countedQuantity: number): RpcRequestOf<'RecordCountCommand'> {
  return { physicalCountId, sku, binCode, countedQuantity, lotNumber: null };
}

/** Quitar un conteo: el lote por defecto viaja como null (como el escritorio); otro lote, con su número. */
export function toRemoveCount(physicalCountId: string, line: CountLineRecord): RpcRequestOf<'RemoveCountCommand'> {
  return { physicalCountId, sku: line.sku, binCode: line.binCode, lotNumber: line.lotNumber === DEFAULT_LOT ? null : line.lotNumber };
}

/** El mensaje del servidor sin la marca «✔» del escritorio. */
export function plainMessage(message: string): string {
  return message.replace(/^[✔\s]+/u, '').trim();
}

// ---------------------------------------------------------------------------------------------------- almacenes

/** Almacenes que se pueden contar: los de la sucursal activa (o, sin sucursal activa, los de las sucursales visibles). */
export function warehouseOptions(branches: readonly BranchRecord[], activeBranchId: string | null | undefined): Option[] {
  const active = activeBranchId ? branches.filter((branch) => branch.id === activeBranchId) : branches.filter((branch) => branch.isVisible && branch.isActive);
  return active.flatMap((branch) =>
    branch.warehouses.map((warehouse) => ({ value: warehouse, label: branch.warehouses.length > 1 || active.length > 1 ? `${branch.code} · ${branch.name} (${warehouse})` : `${branch.code} · ${branch.name}` })),
  );
}

// ---------------------------------------------------------------------------------------------------- CSV e impresión

export const COUNT_CSV: readonly CsvColumn<CountEntry>[] = [
  { header: 'SKU', value: (entry) => entry.line.sku },
  { header: 'Producto', value: (entry) => entry.line.name },
  { header: 'Categoría', value: (entry) => entry.category },
  { header: 'Posición', value: (entry) => entry.line.binCode },
  { header: 'Lote', value: (entry) => entry.line.lotNumber },
  { header: 'Unidad', value: (entry) => entry.line.unit },
  { header: 'Sistema', value: (entry) => entry.line.systemQuantity },
  { header: 'Contado', value: (entry) => entry.line.countedQuantity },
  { header: 'Diferencia', value: (entry) => entry.line.difference },
  { header: 'Resultado', value: (entry) => DIFFERENCES[entry.kind].label },
  { header: 'Contó', value: (entry) => entry.line.countedBy },
  { header: 'Hora del conteo', value: (entry) => toDate(entry.line.countedAt) },
];

/** Filas de la planilla impresa: los productos del alcance, por posición y nombre. */
export function printRows(products: readonly LookupRecord[], filters: CountFilters): LookupRecord[] {
  return productsInScope(products, filters)
    .filter((product) => matchesSearch(filters.q, [product.sku, product.name, product.category, product.primaryBin, ...product.barcodes]))
    .sort((a, b) => byBin(a.primaryBin, b.primaryBin) || a.name.localeCompare(b.name, 'es'));
}

/** Posiciones en orden alfabético; los productos sin posición al final. */
function byBin(a: string | null, b: string | null): number {
  if (a === b) return 0;
  if (a === null) return 1;
  if (b === null) return -1;
  return a.localeCompare(b, 'es');
}
