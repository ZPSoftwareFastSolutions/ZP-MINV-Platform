// USO · Tabla de datos del panel: columnas tipadas, orden por columna, paginación en la página con filas por página,
// selección opcional, fila desplegable, pie con totales, estados de carga, vacío y error con «Reintentar», cabecera fija
// y vista de tarjetas por debajo de 640 px.
//
//   const columnas: DataTableColumn<Venta>[] = [
//     { id: 'numero', header: 'Número', value: (v) => v.number, card: 'title' },
//     { id: 'fecha', header: 'Fecha', value: (v) => new Date(v.date), cell: (v) => formatDateTime(v.date) },
//     { id: 'estado', header: 'Estado', value: (v) => v.status, cell: (v) => <StatusBadge status={v.status} statuses={ESTADOS} /> },
//     { id: 'total', header: 'Total', align: 'end', value: (v) => v.total, cell: (v) => formatMoney(v.total),
//       footer: (filas) => formatMoney(filas.reduce((suma, v) => suma + v.total, 0)) },
//   ];
//   <DataTable caption="Ventas" columns={columnas} rows={filas} rowKey={(v) => v.number}
//     loading={ventas.loading} refreshing={ventas.fetching} error={ventas.error} onRetry={ventas.reload}
//     {...tabla.tableProps}                                  ← orden y página en la dirección (useTableState)
//     selection={seleccion} onSelectionChange={setSeleccion}  ← opcional
//     renderExpanded={(v) => <DetalleLineas venta={v} />}  ← opcional
//     rowActions={(v) => [{ label: 'Anular', tone: 'danger', onSelect: () => … }]}
//     onRowOpen={(v) => setAbierta(v.number)} activeRowKey={abierta}
//     empty={{ title: 'No hay ventas', description: 'Pruebe con otras fechas.' }} />
//
// Sin `sort`/`page`/`pageSize` controlados, la tabla los maneja sola. Los totales del pie usan TODAS las filas (no solo
// la página). `rows` son las filas YA filtradas; la tabla solo ordena y pagina.

import clsx from 'clsx';
import { ArrowDown, ArrowUp, ChevronsUpDown, RotateCcw } from 'lucide-react';
import { Fragment, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import { Alert } from '@/4-presentation/components/ui/Alert';
import { Button } from '@/4-presentation/components/ui/Button';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { Pagination } from '@/4-presentation/components/ui/Pagination';
import { Skeleton } from '@/4-presentation/components/ui/Skeleton';
import { useMediaQuery } from '@/4-presentation/hooks/useMediaQuery';
import { formatNumber } from '../lib/format';
import { describePanelError } from '../lib/rpc';
import { DEFAULT_PAGE_SIZE, PAGE_SIZES, nextSort, paginate, rangeText, sortRows, type SortState } from '../lib/table';
import { fromInteractive } from './aria';
import { DataTableCards } from './DataTableCards';
import { CARDS_QUERY, EMPTY_SELECTION, alignClass, canSortColumn, primaryColumnIndex, renderCell, rowNameOf, useControlled, type DataTableColumn } from './tableColumns';
import { RowActions, type RowActionItem } from './RowActions';
import { ErrorState } from './States';
import { CompactPager, ExpandToggle, PageSizeSelect, RowCheckbox } from './TableParts';

export interface DataTableEmpty {
  title: string;
  description?: string;
  /** Botones (por ejemplo «Limpiar filtros» o «Nueva venta»). */
  action?: ReactNode;
  icon?: ReactNode;
}

export interface DataTableProps<T> {
  /** Nombre de la tabla para lectores de pantalla («Ventas de la sucursal»). */
  caption: string;
  columns: readonly DataTableColumn<T>[];
  /** Filas ya filtradas (undefined mientras no llegaron). */
  rows: readonly T[] | undefined;
  /** Identificador único y estable de cada fila. */
  rowKey: (row: T) => string;
  /** Cómo nombrar una fila para los lectores de pantalla («la venta F-CM-000123»); por defecto, su columna principal. */
  rowLabel?: (row: T) => string;
  /** Primera carga (esqueleto). */
  loading?: boolean;
  /** Recarga con filas a la vista (barra de progreso, sin esqueleto). */
  refreshing?: boolean;
  error?: unknown;
  onRetry?: () => void;
  /** Operación que carga las filas (para nombrar el permiso que falta si el servidor rechaza). */
  operation?: string;
  empty?: DataTableEmpty;

  sort?: SortState | null;
  onSortChange?: (sort: SortState | null) => void;
  /** Orden inicial si la tabla maneja su propio orden. */
  defaultSort?: SortState | null;

  /** false = sin paginación (listas cortas). */
  paginate?: boolean;
  page?: number;
  onPageChange?: (page: number) => void;
  pageSize?: number;
  onPageSizeChange?: (size: number) => void;
  pageSizes?: readonly number[];

  /** Filas seleccionadas (por `rowKey`). Con `onSelectionChange` aparece la columna de casillas. */
  selection?: ReadonlySet<string>;
  onSelectionChange?: (selection: Set<string>) => void;

  /** Contenido de la fila desplegable. */
  renderExpanded?: (row: T) => ReactNode;
  /** Acciones de cada fila (menú «⋯» en la última columna). */
  rowActions?: (row: T) => readonly RowActionItem[];
  /** Abre el detalle: la columna principal se vuelve un botón y la fila responde al clic. */
  onRowOpen?: (row: T) => void;
  /** Resalta una fila (la abierta en el panel lateral). */
  activeRowKey?: string | null;
  /** Título de la fila de totales (por defecto «Totales»). */
  footerLabel?: string;
  /** Alto máximo con la cabecera fija, desde 640 px (clase de Tailwind). Por defecto el 70 % de la ventana. */
  maxHeightClass?: string;
  className?: string;
}

const TH = 'sticky top-0 z-10 border-b border-border bg-surface-2 px-3 text-xs font-semibold uppercase tracking-wide whitespace-nowrap text-text-muted';
const TD = 'border-b border-border px-3 py-2 align-middle text-text';
const TF = 'sticky bottom-0 z-10 border-t border-border-strong bg-surface-2 px-3 py-3 font-semibold text-text tabular-nums';

export function DataTable<T>(props: DataTableProps<T>) {
  const {
    caption,
    columns,
    rows,
    rowKey,
    rowLabel,
    loading = false,
    refreshing = false,
    error,
    onRetry,
    operation,
    empty,
    paginate: paginated = true,
    pageSizes = PAGE_SIZES,
    selection = EMPTY_SELECTION,
    onSelectionChange,
    renderExpanded,
    rowActions,
    onRowOpen,
    activeRowKey,
    footerLabel = 'Totales',
    maxHeightClass = 'sm:max-h-[70vh]',
    className,
  } = props;

  const cards = useMediaQuery(CARDS_QUERY);
  const tableId = useId();
  const scrollRef = useRef<HTMLDivElement>(null);
  const [sort, setSort] = useControlled(props.sort, props.defaultSort ?? null, props.onSortChange);
  const [page, setPage] = useControlled(props.page, 1, props.onPageChange);
  const [pageSize, setPageSize] = useControlled(props.pageSize, DEFAULT_PAGE_SIZE, props.onPageSizeChange);
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(EMPTY_SELECTION);

  const data = useMemo(() => rows ?? [], [rows]);
  const sortColumn = sort ? columns.find((column) => column.id === sort.column && typeof column.value === 'function') : undefined;
  const sorted = useMemo(
    () => (sortColumn?.value && sort ? sortRows(data, sortColumn.value, sort.direction) : [...data]),
    [data, sortColumn, sort],
  );
  const slice = paginated ? paginate(sorted, page, pageSize) : { rows: sorted, page: 1, pageCount: 1, from: sorted.length > 0 ? 1 : 0, to: sorted.length, total: sorted.length };

  const selectable = typeof onSelectionChange === 'function';
  const expandable = typeof renderExpanded === 'function';
  const hasActions = typeof rowActions === 'function';
  const hasFooter = columns.some((column) => column.footer);
  const primary = primaryColumnIndex(columns);
  const columnCount = columns.length + (selectable ? 1 : 0) + (expandable ? 1 : 0) + (hasActions ? 1 : 0);
  const rowName = (row: T, key: string) => rowLabel?.(row) ?? rowNameOf(columns, row, key);
  const detailId = (index: number) => `${tableId}-detalle-${index}`;

  const pageKeys = slice.rows.map(rowKey);
  const selectedOnPage = pageKeys.filter((key) => selection.has(key)).length;
  const pageSelection = { all: pageKeys.length > 0 && selectedOnPage === pageKeys.length, some: selectedOnPage > 0 && selectedOnPage < pageKeys.length };

  const toggleRow = (key: string) => {
    const next = new Set(selection);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    onSelectionChange?.(next);
  };
  const togglePage = () => {
    const next = new Set(selection);
    for (const key of pageKeys) {
      if (pageSelection.all) next.delete(key);
      else next.add(key);
    }
    onSelectionChange?.(next);
  };
  const toggleExpanded = (key: string) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  const goToPage = (next: number) => {
    setPage(next);
    scrollRef.current?.scrollTo?.({ top: 0 });
  };

  const noRows = data.length === 0;
  const errorText = error ? describePanelError(error, { operation }) : null;
  const multiPage = slice.pageCount > 1;
  const totalsLabel = multiPage ? `${footerLabel} (las ${formatNumber(slice.total)} filas)` : footerLabel;

  const viewProps = {
    caption,
    columns,
    rows: slice.rows,
    allRows: sorted,
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
    footerLabel: totalsLabel,
  };

  // Estados sin filas: la primera carga, un error o nada para mostrar.
  let body: ReactNode = null;
  if (noRows && loading) body = null;
  else if (noRows && error) body = <ErrorState error={error} operation={operation} onRetry={onRetry} className="m-3 sm:m-4" />;
  else if (noRows)
    body = (
      <EmptyState size="sm" icon={empty?.icon} title={empty?.title ?? 'No hay nada para mostrar'} description={empty?.description ?? 'Pruebe con otros filtros.'}>
        {empty?.action}
      </EmptyState>
    );

  return (
    <div className={clsx('relative min-w-0 overflow-hidden rounded-card border border-border bg-surface shadow-card', className)} data-testid="tabla" aria-busy={loading || refreshing || undefined}>
      {(loading || refreshing) && (
        <>
          <div aria-hidden="true" className="absolute inset-x-0 top-0 z-20 h-0.5 overflow-hidden">
            <div className="h-full w-1/3 animate-pulse bg-accent" />
          </div>
          <p role="status" className="sr-only">
            {loading ? `Cargando ${caption.toLowerCase()}…` : `Actualizando ${caption.toLowerCase()}…`}
          </p>
        </>
      )}

      {errorText !== null && !noRows && (
        <Alert
          tone="danger"
          title="No se pudo actualizar la lista"
          className="m-3"
          actions={
            onRetry && (
              <Button variant="outline" leftIcon={<RotateCcw />} onClick={onRetry}>
                Reintentar
              </Button>
            )
          }
        >
          {errorText}
        </Alert>
      )}

      {body !== null ? (
        body
      ) : cards ? (
        noRows ? (
          <SkeletonCards />
        ) : (
          <DataTableCards {...viewProps} />
        )
      ) : (
        <div ref={scrollRef} className={clsx('overflow-auto', maxHeightClass)} tabIndex={0} role="region" aria-label={caption}>
          <table className="w-full border-separate border-spacing-0 text-sm">
            <caption className="sr-only">{caption}</caption>
            <thead>
              <tr>
                {selectable && (
                  <th scope="col" className={clsx(TH, 'w-12 pr-0')}>
                    <RowCheckbox checked={pageSelection.all} indeterminate={pageSelection.some} label="Seleccionar todas las filas de esta página" onChange={togglePage} />
                  </th>
                )}
                {expandable && (
                  <th scope="col" className={clsx(TH, 'w-12 pr-0')}>
                    <span className="sr-only">Detalle</span>
                  </th>
                )}
                {columns.map((column) => {
                  const active = sort?.column === column.id ? sort : null;
                  return (
                    <th
                      key={column.id}
                      scope="col"
                      aria-sort={active ? (active.direction === 'asc' ? 'ascending' : 'descending') : undefined}
                      className={clsx(TH, alignClass(column.align), column.className)}
                    >
                      {canSortColumn(column) ? (
                        <button
                          type="button"
                          title={`Ordenar por ${column.header.toLowerCase()}`}
                          onClick={() => setSort(nextSort(sort, column.id))}
                          className={clsx(
                            'group inline-flex min-h-11 cursor-pointer items-center gap-1 uppercase tracking-wide transition-colors duration-200 hover:text-text',
                            column.align === 'end' && 'flex-row-reverse',
                            active && 'text-text',
                          )}
                        >
                          <span className={clsx(column.hideHeader && 'sr-only')}>{column.header}</span>
                          {active ? (
                            active.direction === 'asc' ? (
                              <ArrowUp aria-hidden="true" className="size-4 text-accent" />
                            ) : (
                              <ArrowDown aria-hidden="true" className="size-4 text-accent" />
                            )
                          ) : (
                            <ChevronsUpDown aria-hidden="true" className="size-4 opacity-40 group-hover:opacity-100" />
                          )}
                        </button>
                      ) : (
                        <span className={clsx('inline-flex min-h-11 items-center', column.hideHeader && 'sr-only')}>{column.header}</span>
                      )}
                    </th>
                  );
                })}
                {hasActions && (
                  <th scope="col" className={clsx(TH, 'w-14')}>
                    <span className="sr-only">Acciones</span>
                  </th>
                )}
              </tr>
            </thead>

            {noRows ? (
              <tbody aria-hidden="true">
                {Array.from({ length: 5 }, (_, index) => (
                  <tr key={index}>
                    {Array.from({ length: columnCount }, (__, cell) => (
                      <td key={cell} className={clsx(TD, 'py-3.5')}>
                        <Skeleton className="h-4 w-full max-w-32" />
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            ) : (
              <tbody>
                {slice.rows.map((row, index) => {
                  const key = rowKey(row);
                  const name = rowName(row, key);
                  const isSelected = selection.has(key);
                  const isExpanded = expanded.has(key);
                  const actions = rowActions?.(row) ?? [];
                  return (
                    <Fragment key={key}>
                      <tr
                        data-row-key={key}
                        data-selected={isSelected || undefined}
                        onClick={
                          onRowOpen
                            ? (event) => {
                                if (!fromInteractive(event.target, event.currentTarget)) onRowOpen(row);
                              }
                            : undefined
                        }
                        className={clsx(
                          'transition-colors duration-150',
                          isSelected ? 'bg-primary-soft/40' : key === activeRowKey ? 'bg-accent-soft/40' : 'hover:bg-surface-2/70',
                          onRowOpen && 'cursor-pointer',
                        )}
                      >
                        {selectable && (
                          <td className={clsx(TD, 'pr-0')}>
                            <RowCheckbox checked={isSelected} label={`Seleccionar ${name}`} onChange={() => toggleRow(key)} />
                          </td>
                        )}
                        {expandable && (
                          <td className={clsx(TD, 'pr-0')}>
                            <ExpandToggle expanded={isExpanded} controls={detailId(index)} label={`Detalle de ${name}`} onToggle={() => toggleExpanded(key)} />
                          </td>
                        )}
                        {columns.map((column, columnIndex) => (
                          <td key={column.id} className={clsx(TD, alignClass(column.align), column.align === 'end' && 'whitespace-nowrap tabular-nums', column.className)}>
                            {onRowOpen && columnIndex === primary ? (
                              <button
                                type="button"
                                onClick={() => onRowOpen(row)}
                                className="min-h-11 cursor-pointer text-left font-medium text-text underline-offset-2 hover:text-accent-hover hover:underline"
                              >
                                {renderCell(column, row)}
                              </button>
                            ) : (
                              renderCell(column, row)
                            )}
                          </td>
                        ))}
                        {hasActions && <td className={clsx(TD, 'py-0 text-right')}>{actions.length > 0 && <RowActions label={`Acciones de ${name}`} actions={actions} />}</td>}
                      </tr>
                      {expandable && isExpanded && (
                        <tr id={detailId(index)}>
                          <td colSpan={columnCount} className="border-b border-border bg-surface-2/60 px-4 py-3">
                            {renderExpanded?.(row)}
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  );
                })}
              </tbody>
            )}

            {hasFooter && !noRows && (
              <tfoot>
                <tr data-testid="tabla-totales">
                  {selectable && <td className={TF} />}
                  {expandable && <td className={TF} />}
                  {columns.map((column, columnIndex) => (
                    <td key={column.id} className={clsx(TF, alignClass(column.align), column.align === 'end' && 'whitespace-nowrap')}>
                      {column.footer ? column.footer(sorted) : columnIndex === 0 ? totalsLabel : null}
                    </td>
                  ))}
                  {hasActions && <td className={TF} />}
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      )}

      {!noRows && (paginated || (selectable && selection.size > 0)) && (
        <div className="flex flex-col gap-3 border-t border-border px-3 py-2 sm:flex-row sm:flex-wrap sm:items-center sm:justify-between">
          <div className="flex min-h-11 flex-wrap items-center gap-x-3 gap-y-1 text-sm text-text-muted">
            <span>{rangeText(slice)}</span>
            {selectable && selection.size > 0 && (
              <>
                <span aria-hidden="true">·</span>
                <span role="status">{selection.size === 1 ? '1 seleccionada' : `${formatNumber(selection.size)} seleccionadas`}</span>
                <button type="button" onClick={() => onSelectionChange?.(new Set())} className="min-h-11 cursor-pointer font-semibold text-accent hover:text-accent-hover">
                  Quitar selección
                </button>
              </>
            )}
          </div>
          {paginated && (
            <div className="flex flex-wrap items-center gap-3">
              <PageSizeSelect value={pageSize} sizes={pageSizes} onChange={setPageSize} />
              {cards ? (
                <CompactPager page={slice.page} pageCount={slice.pageCount} onChange={goToPage} />
              ) : (
                <Pagination page={slice.page} pageCount={slice.pageCount} onChange={goToPage} />
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function SkeletonCards() {
  return (
    <ul aria-hidden="true" className="divide-y divide-border">
      {Array.from({ length: 4 }, (_, index) => (
        <li key={index} className="space-y-2 p-4">
          <Skeleton className="h-5 w-40" />
          <Skeleton className="h-4 w-full max-w-xs" />
          <Skeleton className="h-4 w-2/3 max-w-56" />
        </li>
      ))}
    </ul>
  );
}
