// Orden y paginación EN LA PÁGINA de las tablas del panel (DataTable, useTableState). Funciones puras y sin React.
//
//   sortRows(filas, (v) => v.total, 'desc')   → copia ordenada; los vacíos siempre al final; estable
//   paginate(filas, 2, 25)                     → { rows, page, pageCount, from: 26, to: 50, total }
//   formatCellValue(valor)                     → texto de una celda sin `cell` propio («—», «Sí», «1.234,5», fecha y hora)

import { normalizeText } from '@/shared/text';
import { EMPTY_VALUE, formatDateTime, formatNumber } from './format';

export type SortDirection = 'asc' | 'desc';

/** Orden de una tabla: la columna (`id` de la columna) y el sentido. */
export interface SortState {
  column: string;
  direction: SortDirection;
}

/** Valor crudo de una celda: lo que se ordena, se exporta a CSV y se muestra si la columna no dibuja otra cosa. */
export type CellValue = string | number | boolean | Date | null | undefined;

/** Tamaños de página que ofrece la tabla. */
export const PAGE_SIZES: readonly number[] = [10, 25, 50, 100];
export const DEFAULT_PAGE_SIZE = 25;

const collator = new Intl.Collator('es', { numeric: true, sensitivity: 'base' });

/** ¿Celda vacía? (null, undefined, texto en blanco, NaN o una fecha inválida). */
export function isEmptyValue(value: CellValue): boolean {
  if (value == null) return true;
  if (typeof value === 'string') return value.trim().length === 0;
  if (typeof value === 'number') return Number.isNaN(value);
  if (value instanceof Date) return Number.isNaN(value.getTime());
  return false;
}

/** Compara dos valores NO vacíos: números y fechas por magnitud, texto en español con números naturales («F-2» < «F-10»). */
export function compareValues(a: CellValue, b: CellValue): number {
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  if (a instanceof Date && b instanceof Date) return a.getTime() - b.getTime();
  if (typeof a === 'boolean' && typeof b === 'boolean') return Number(a) - Number(b);
  return collator.compare(String(a), String(b));
}

/** Copia ordenada de las filas. Los vacíos quedan al final en los dos sentidos y el orden es estable. */
export function sortRows<T>(rows: readonly T[], valueOf: (row: T) => CellValue, direction: SortDirection): T[] {
  const factor = direction === 'asc' ? 1 : -1;
  const decorated = rows.map((row, index) => ({ row, index, value: valueOf(row) }));
  decorated.sort((a, b) => {
    const emptyA = isEmptyValue(a.value);
    const emptyB = isEmptyValue(b.value);
    if (emptyA !== emptyB) return emptyA ? 1 : -1;
    if (emptyA) return a.index - b.index;
    return compareValues(a.value, b.value) * factor || a.index - b.index;
  });
  return decorated.map((item) => item.row);
}

/** Siguiente orden al pulsar el encabezado: sin orden → ascendente → descendente → sin orden. */
export function nextSort(current: SortState | null, column: string): SortState | null {
  if (!current || current.column !== column) return { column, direction: 'asc' };
  if (current.direction === 'asc') return { column, direction: 'desc' };
  return null;
}

/** Cantidad de páginas (al menos 1). */
export function pageCountOf(total: number, pageSize: number): number {
  return Math.max(1, Math.ceil(Math.max(0, total) / Math.max(1, pageSize)));
}

/** Página válida entre 1 y `pageCount` (un número raro de la URL cae en la 1). */
export function clampPage(page: number, pageCount: number): number {
  const whole = Number.isFinite(page) ? Math.trunc(page) : 1;
  return Math.min(Math.max(1, whole), Math.max(1, pageCount));
}

export interface PageSlice<T> {
  rows: T[];
  /** Página mostrada (ya acotada). */
  page: number;
  pageCount: number;
  /** Número (desde 1) de la primera y la última fila mostradas; 0 si no hay filas. */
  from: number;
  to: number;
  total: number;
}

/** Filas de una página. Una página fuera de rango muestra la última que existe. */
export function paginate<T>(rows: readonly T[], page: number, pageSize: number): PageSlice<T> {
  const size = Math.max(1, Math.trunc(pageSize) || DEFAULT_PAGE_SIZE);
  const pageCount = pageCountOf(rows.length, size);
  const current = clampPage(page, pageCount);
  const start = (current - 1) * size;
  const slice = rows.slice(start, start + size);
  return { rows: slice, page: current, pageCount, from: slice.length > 0 ? start + 1 : 0, to: start + slice.length, total: rows.length };
}

/** «Mostrando 26–50 de 230» · «Sin filas». */
export function rangeText(slice: Pick<PageSlice<unknown>, 'from' | 'to' | 'total'>): string {
  if (slice.total === 0) return 'Sin filas';
  return `Mostrando ${formatNumber(slice.from)}–${formatNumber(slice.to)} de ${formatNumber(slice.total)}`;
}

/** Texto de una celda que no dibuja nada propio: vacío «—», sí/no, número con separadores y fecha con hora de La Paz. */
export function formatCellValue(value: CellValue): string {
  if (isEmptyValue(value)) return EMPTY_VALUE;
  if (typeof value === 'boolean') return value ? 'Sí' : 'No';
  if (typeof value === 'number') return formatNumber(value, { maxDecimals: 2 });
  if (value instanceof Date) return formatDateTime(value);
  return String(value);
}

/** ¿El texto de búsqueda aparece en alguno de los valores? (sin acentos ni mayúsculas; todas las palabras). */
export function matchesSearch(query: string, values: readonly CellValue[]): boolean {
  const words = normalizeText(query).split(' ').filter(Boolean);
  if (words.length === 0) return true;
  const haystack = normalizeText(values.filter((value) => !isEmptyValue(value)).map((value) => (value instanceof Date ? formatDateTime(value) : String(value))).join(' '));
  return words.every((word) => haystack.includes(word));
}

/** Columna de tabla con lo mínimo para exportarla (DataTableColumn cumple esta forma). */
export interface ExportableColumn<T> {
  header: string;
  value?: (row: T) => CellValue;
  /** false = no va al CSV. */
  csv?: boolean;
}

/** Columnas de CSV a partir de las de una tabla: las que tienen valor y no se excluyeron. */
export function csvColumnsOf<T>(columns: readonly ExportableColumn<T>[]): { header: string; value: (row: T) => CellValue }[] {
  return columns
    .filter((column): column is ExportableColumn<T> & { value: (row: T) => CellValue } => typeof column.value === 'function' && column.csv !== false)
    .map((column) => ({ header: column.header, value: column.value }));
}
