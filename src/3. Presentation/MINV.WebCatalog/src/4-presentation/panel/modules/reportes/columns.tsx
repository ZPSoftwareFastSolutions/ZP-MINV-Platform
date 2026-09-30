// Módulo «Reportes» · columnas de un reporte declaradas UNA vez y usadas en tres lugares: la tabla de la pantalla
// (`DataTable`), el archivo CSV y la hoja impresa. `value` es el dato crudo (ordena y va al CSV); `text`, lo que se lee
// (pantalla e impresión); `secondary`, una segunda línea más tenue en la pantalla (el SKU debajo del nombre).

import type { ReactNode } from 'react';
import type { DataTableColumn } from '@/4-presentation/panel/kit';
import { sortRows, type CellValue, type CsvColumn, type SortState } from '@/4-presentation/panel/lib';

export interface ReportColumn<T> {
  id: string;
  header: string;
  /** Dato crudo: ordena la tabla y va al CSV. */
  value: (row: T) => CellValue;
  /** Texto de la celda (pantalla e impresión). */
  text: (row: T) => string;
  /** Segunda línea en la pantalla (null = ninguna). */
  secondary?: (row: T) => string | null;
  /** Color del texto en la pantalla (entradas en verde, salidas en ámbar). */
  tone?: (row: T) => 'success' | 'warning' | null;
  align?: 'start' | 'end';
  /** Pie (totales) con TODAS las filas; null = sin total en esa columna. */
  footer?: (rows: readonly T[]) => string | null;
  card?: 'title' | 'hidden';
  className?: string;
  sortable?: boolean;
  /** false = no se ve en la tabla de la pantalla (va al CSV y a la hoja impresa). */
  table?: boolean;
  /** false = no va al CSV (el puesto, que depende del orden). */
  csv?: boolean;
}

const TONES = { success: 'text-success-text', warning: 'text-warning-text' } as const;

function cellOf<T>(column: ReportColumn<T>): (row: T) => ReactNode {
  return (row) => {
    const secondary = column.secondary?.(row) ?? null;
    const tone = column.tone?.(row) ?? null;
    if (secondary === null && tone === null) return column.text(row);
    return (
      <span className="block">
        <span className={tone ? `block ${TONES[tone]}` : 'block'}>{column.text(row)}</span>
        {secondary !== null && <span className="block text-xs font-normal text-text-muted">{secondary}</span>}
      </span>
    );
  };
}

/** Columnas de `DataTable`. */
export function toTableColumns<T>(columns: readonly ReportColumn<T>[]): DataTableColumn<T>[] {
  return columns
    .filter((column) => column.table !== false)
    .map((column) => ({
      id: column.id,
      header: column.header,
      value: column.value,
      cell: cellOf(column),
      align: column.align,
      footer: column.footer ? (rows: readonly T[]) => column.footer?.(rows) ?? null : undefined,
      card: column.card,
      className: column.className,
      sortable: column.sortable,
    }));
}

/** Columnas del CSV (encabezado y dato crudo). */
export function toCsvColumns<T>(columns: readonly ReportColumn<T>[]): CsvColumn<T>[] {
  return columns.filter((column) => column.csv !== false).map((column) => ({ header: column.header, value: column.value }));
}

/** Las filas en el orden en que las muestra la tabla (para exportar e imprimir lo mismo que se ve). */
export function inTableOrder<T>(rows: readonly T[], columns: readonly ReportColumn<T>[], sort: SortState | null): readonly T[] {
  const column = sort ? columns.find((item) => item.id === sort.column) : undefined;
  return sort && column ? sortRows(rows, column.value, sort.direction) : rows;
}
