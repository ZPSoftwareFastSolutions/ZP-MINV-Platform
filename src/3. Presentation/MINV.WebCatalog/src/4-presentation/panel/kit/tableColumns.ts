// Tipos y ayudas de DataTable (sin componentes: así el archivo del componente solo exporta componentes).

import { useCallback, useState, type ReactNode } from 'react';
import { formatCellValue, type CellValue } from '../lib/table';

export interface DataTableColumn<T> {
  /** Identificador estable (se usa en el orden y en la dirección: `?orden=total`). */
  id: string;
  /** Encabezado visible (y etiqueta de la columna en la vista de tarjetas y en el CSV). */
  header: string;
  /** Cómo se dibuja la celda. Sin `cell`, se muestra `value` con formato («—», «Sí», «1.234,5», fecha y hora). */
  cell?: (row: T) => ReactNode;
  /** Valor crudo: ordena, se exporta a CSV y es la celda por defecto. */
  value?: (row: T) => CellValue;
  /** false = no se ordena por esta columna. Por defecto, sí si tiene `value`. */
  sortable?: boolean;
  /** Alineación ('end' para importes y cantidades: cifras alineadas y sin cortes). */
  align?: 'start' | 'center' | 'end';
  /** Pie de la columna (totales), calculado con TODAS las filas (no solo las de la página). */
  footer?: (rows: readonly T[]) => ReactNode;
  /** Clases de la columna (ancho: 'w-32', 'min-w-48'). */
  className?: string;
  /** El encabezado queda solo para lectores de pantalla (columnas de íconos). */
  hideHeader?: boolean;
  /** Vista de tarjetas (< 640 px): 'title' = título de la tarjeta (si ninguna lo es, la primera); 'hidden' = no se muestra. */
  card?: 'title' | 'hidden';
  /** false = no va al CSV (`csvColumnsOf`). */
  csv?: boolean;
}

/** Por debajo de este ancho la tabla se muestra como tarjetas. */
export const CARDS_QUERY = '(max-width: 639.98px)';

export const EMPTY_SELECTION: ReadonlySet<string> = new Set();

export function canSortColumn<T>(column: DataTableColumn<T>): boolean {
  return column.sortable ?? typeof column.value === 'function';
}

export function alignClass(align: DataTableColumn<unknown>['align']): string {
  if (align === 'end') return 'text-right';
  if (align === 'center') return 'text-center';
  return 'text-left';
}

/** Contenido de una celda. */
export function renderCell<T>(column: DataTableColumn<T>, row: T): ReactNode {
  if (column.cell) return column.cell(row);
  return formatCellValue(column.value?.(row));
}

/** Índice de la columna principal (el título de la tarjeta y el botón que abre el detalle). */
export function primaryColumnIndex<T>(columns: readonly DataTableColumn<T>[]): number {
  const index = columns.findIndex((column) => column.card === 'title');
  return index >= 0 ? index : 0;
}

/** Texto que nombra una fila para los lectores de pantalla («Seleccionar F-CM-000123»). */
export function rowNameOf<T>(columns: readonly DataTableColumn<T>[], row: T, key: string): string {
  const primary = columns[primaryColumnIndex(columns)];
  const text = primary?.value ? formatCellValue(primary.value(row)) : '';
  return text && text !== '—' ? text : key;
}

/** Estado controlado desde afuera (si llega el valor) o propio (si no). */
export function useControlled<T>(controlled: T | undefined, initial: T, onChange?: (value: T) => void): [T, (value: T) => void] {
  const [own, setOwn] = useState(initial);
  const isControlled = controlled !== undefined;
  const set = useCallback(
    (next: T) => {
      if (!isControlled) setOwn(next);
      onChange?.(next);
    },
    [isControlled, onChange],
  );
  return [isControlled ? controlled : own, set];
}
