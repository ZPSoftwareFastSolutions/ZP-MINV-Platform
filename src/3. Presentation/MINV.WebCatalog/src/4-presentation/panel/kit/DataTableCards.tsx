// Vista de tarjetas de DataTable (por debajo de 640 px): una tarjeta por fila con el título, los demás datos como
// «etiqueta: valor», la casilla de selección, el menú de acciones y el detalle desplegable. El orden se elige con una
// lista desplegable porque los encabezados no se ven. Uso interno: los módulos usan DataTable.

import clsx from 'clsx';
import { ArrowDownWideNarrow, ArrowUpNarrowWide, ChevronDown } from 'lucide-react';
import { Fragment, type ReactNode } from 'react';
import type { SortState } from '../lib/table';
import { canSortColumn, primaryColumnIndex, renderCell, type DataTableColumn } from './tableColumns';
import { RowActions, type RowActionItem } from './RowActions';
import { SelectField } from './SelectField';
import { RowCheckbox } from './TableParts';

export interface TableViewProps<T> {
  caption: string;
  columns: readonly DataTableColumn<T>[];
  /** Filas de la página. */
  rows: readonly T[];
  /** Todas las filas (ordenadas): para los totales. */
  allRows: readonly T[];
  rowKey: (row: T) => string;
  rowName: (row: T, key: string) => string;
  selectable: boolean;
  selection: ReadonlySet<string>;
  toggleRow: (key: string) => void;
  pageSelection: { all: boolean; some: boolean };
  togglePage: () => void;
  renderExpanded?: (row: T) => ReactNode;
  expanded: ReadonlySet<string>;
  toggleExpanded: (key: string) => void;
  detailId: (index: number) => string;
  rowActions?: (row: T) => readonly RowActionItem[];
  onRowOpen?: (row: T) => void;
  activeRowKey?: string | null;
  sort: SortState | null;
  setSort: (sort: SortState | null) => void;
  footerLabel: string;
}

export function DataTableCards<T>({
  caption,
  columns,
  rows,
  allRows,
  rowKey,
  rowName,
  selectable,
  selection,
  toggleRow,
  pageSelection,
  togglePage,
  renderExpanded,
  expanded,
  toggleExpanded,
  detailId,
  rowActions,
  onRowOpen,
  activeRowKey,
  sort,
  setSort,
  footerLabel,
}: TableViewProps<T>) {
  const primary = primaryColumnIndex(columns);
  const titleColumn = columns[primary];
  const details = columns.filter((column, index) => index !== primary && column.card !== 'hidden');
  const sortable = columns.filter(canSortColumn);
  const footers = columns.filter((column) => column.footer);

  return (
    <div>
      {(sortable.length > 0 || (selectable && rows.length > 0)) && (
        <div className="flex flex-wrap items-end gap-2 border-b border-border p-3">
          {sortable.length > 0 && (
            <>
              <SelectField
                label="Ordenar por"
                className="min-w-0 flex-1"
                value={sort?.column ?? ''}
                allLabel="Sin orden"
                options={sortable.map((column) => ({ value: column.id, label: column.header }))}
                onChange={(column) => setSort(column ? { column, direction: sort?.direction ?? 'asc' } : null)}
              />
              {sort && (
                <button
                  type="button"
                  onClick={() => setSort({ column: sort.column, direction: sort.direction === 'asc' ? 'desc' : 'asc' })}
                  aria-label={sort.direction === 'asc' ? 'Orden ascendente: cambiar a descendente' : 'Orden descendente: cambiar a ascendente'}
                  className="inline-flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-xl border border-border-control bg-surface text-text-muted hover:text-text"
                >
                  {sort.direction === 'asc' ? <ArrowUpNarrowWide aria-hidden="true" className="size-5" /> : <ArrowDownWideNarrow aria-hidden="true" className="size-5" />}
                </button>
              )}
            </>
          )}
          {selectable && rows.length > 0 && (
            <div className="flex w-full items-center gap-2 text-sm text-text-muted">
              <RowCheckbox checked={pageSelection.all} indeterminate={pageSelection.some} label="Seleccionar todas las filas de esta página" onChange={togglePage} />
              <span aria-hidden="true">Seleccionar todas</span>
            </div>
          )}
        </div>
      )}

      <ul aria-label={caption} className="divide-y divide-border">
        {rows.map((row, index) => {
          const key = rowKey(row);
          const name = rowName(row, key);
          const isSelected = selection.has(key);
          const isExpanded = expanded.has(key);
          const title = renderCell(titleColumn, row);
          const actions = rowActions?.(row) ?? [];
          return (
            <li key={key} data-row-key={key} className={clsx('p-4', isSelected ? 'bg-primary-soft/40' : key === activeRowKey && 'bg-accent-soft/40')}>
              <div className="flex items-start gap-2">
                {selectable && <RowCheckbox checked={isSelected} label={`Seleccionar ${name}`} onChange={() => toggleRow(key)} />}
                <div className="min-w-0 flex-1 pt-2">
                  <div className="text-base font-semibold text-text">
                    {onRowOpen ? (
                      <button type="button" onClick={() => onRowOpen(row)} className="min-h-11 cursor-pointer text-left underline-offset-2 hover:text-accent-hover hover:underline">
                        {title}
                      </button>
                    ) : (
                      title
                    )}
                  </div>
                  {details.length > 0 && (
                    <dl className="mt-2 grid grid-cols-[minmax(0,auto)_minmax(0,1fr)] gap-x-4 gap-y-1.5 text-sm">
                      {details.map((column) => (
                        <Fragment key={column.id}>
                          <dt className="text-text-muted">{column.header}</dt>
                          <dd className={clsx('min-w-0 break-words text-text', column.align === 'end' && 'tabular-nums')}>{renderCell(column, row)}</dd>
                        </Fragment>
                      ))}
                    </dl>
                  )}
                </div>
                {actions.length > 0 && <RowActions label={`Acciones de ${name}`} actions={actions} />}
              </div>
              {renderExpanded && (
                <>
                  <button
                    type="button"
                    aria-expanded={isExpanded}
                    aria-controls={isExpanded ? detailId(index) : undefined}
                    onClick={() => toggleExpanded(key)}
                    className="mt-1 inline-flex min-h-11 cursor-pointer items-center gap-1 text-sm font-semibold text-accent hover:text-accent-hover"
                  >
                    {isExpanded ? 'Ocultar detalle' : 'Ver detalle'}
                    <ChevronDown aria-hidden="true" className={clsx('size-4 transition-transform duration-200', isExpanded && 'rotate-180')} />
                  </button>
                  {isExpanded && (
                    <div id={detailId(index)} className="mt-1 rounded-xl bg-surface-2 p-3 text-sm">
                      {renderExpanded(row)}
                    </div>
                  )}
                </>
              )}
            </li>
          );
        })}
      </ul>

      {footers.length > 0 && allRows.length > 0 && (
        <div className="border-t border-border-strong bg-surface-2 p-4 text-sm" data-testid="tabla-totales">
          <p className="font-semibold text-text">{footerLabel}</p>
          <dl className="mt-2 grid grid-cols-[minmax(0,auto)_minmax(0,1fr)] gap-x-4 gap-y-1.5">
            {footers.map((column) => (
              <Fragment key={column.id}>
                <dt className="text-text-muted">{column.header}</dt>
                <dd className="font-semibold text-text tabular-nums">{column.footer?.(allRows)}</dd>
              </Fragment>
            ))}
          </dl>
        </div>
      )}
    </div>
  );
}
