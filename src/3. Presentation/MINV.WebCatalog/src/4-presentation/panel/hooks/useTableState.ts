// Estado de una lista del panel (filtros, orden, página y filas por página) guardado en la DIRECCIÓN de la página: se
// puede recargar, copiar el enlace o volver atrás y la lista queda igual. Nada se guarda en el navegador.
//
//   const tabla = useTableState({ filters: { q: '', estado: '', sucursal: '', desde: '', hasta: '' }, sort: { column: 'fecha', direction: 'desc' } });
//   <FilterBar activeCount={tabla.activeFilterCount} onClear={tabla.clearFilters}>
//     <SearchField label="Buscar" value={tabla.filters.q} onChange={(q) => tabla.setFilter('q', q)} />
//     <DateRangeField label="Fechas" value={tabla.dateRange()} onChange={(rango) => tabla.setDateRange(rango)} />
//   </FilterBar>
//   <DataTable {...tabla.tableProps} rows={filasFiltradas} … />
//
// En la dirección: cada filtro con su nombre (`?estado=pagada&q=monitor`), `orden` y `sentido` (asc | desc), `pagina` y
// `filas`. Los valores por defecto NO se escriben (direcciones limpias). Cambiar un filtro, el orden o las filas por
// página vuelve a la página 1. Los cambios REEMPLAZAN la entrada del historial (escribir en la búsqueda no llena el
// botón «Atrás»). Con `prefix` dos listas conviven en la misma pantalla (`v_estado`, `v_pagina`…). Los nombres `orden`,
// `sentido`, `pagina` y `filas` están reservados: no los use como filtro.

import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import { EMPTY_RANGE, type DateRange } from '../lib/dates';
import { stableKey } from '../lib/rpc';
import { DEFAULT_PAGE_SIZE, PAGE_SIZES, type SortState } from '../lib/table';

/** Filtros de una lista: nombre en la dirección → valor ('' = sin filtro). */
export type FilterValues = Record<string, string>;

/** Parámetros de la dirección que usa la tabla (reservados). */
export const TABLE_PARAMS = { sort: 'orden', direction: 'sentido', page: 'pagina', pageSize: 'filas' } as const;

export interface TableStateOptions<F extends FilterValues> {
  /** Filtros con su valor por defecto ('' = sin filtro). */
  filters?: F;
  /** Orden por defecto (null = el orden en que llegan las filas). */
  sort?: SortState | null;
  /** Filas por página por defecto (25). */
  pageSize?: number;
  /** Prefijo de los parámetros (dos listas en la misma pantalla). */
  prefix?: string;
  /** Pares de filtros que cuentan como UNO en el contador (por defecto `desde` y `hasta`, si existen). */
  ranges?: readonly (readonly [string, string])[];
}

export interface TableSortProps {
  sort: SortState | null;
  onSortChange: (sort: SortState | null) => void;
  page: number;
  onPageChange: (page: number) => void;
  pageSize: number;
  onPageSizeChange: (size: number) => void;
}

export interface TableState<F extends FilterValues> {
  filters: F;
  /** Cambia un filtro (y vuelve a la página 1). */
  setFilter(key: keyof F & string, value: string): void;
  /** Cambia varios filtros de una vez. */
  setFilters(values: Partial<F>): void;
  /** «Limpiar filtros»: todos a su valor por defecto (conserva el orden y las filas por página). */
  clearFilters(): void;
  /** Filtros distintos de su valor por defecto (un rango de fechas cuenta como uno). */
  activeFilterCount: number;
  sort: SortState | null;
  setSort(sort: SortState | null): void;
  page: number;
  setPage(page: number): void;
  pageSize: number;
  setPageSize(size: number): void;
  /** Rango de fechas guardado en dos filtros (por defecto `desde` y `hasta`). */
  dateRange(fromKey?: keyof F & string, toKey?: keyof F & string): DateRange;
  setDateRange(range: DateRange, fromKey?: keyof F & string, toKey?: keyof F & string): void;
  /** Lo que `DataTable` necesita para ordenar y paginar con este estado: `<DataTable {...tabla.tableProps} />`. */
  tableProps: TableSortProps;
}

function sameSort(a: SortState | null, b: SortState | null): boolean {
  return a === b || (a !== null && b !== null && a.column === b.column && a.direction === b.direction);
}

export function useTableState<F extends FilterValues = FilterValues>(options: TableStateOptions<F> = {}): TableState<F> {
  const [params, setParams] = useSearchParams();
  const prefix = options.prefix ?? '';
  // Las opciones se comparan por valor: un objeto literal nuevo en cada dibujo no cambia nada.
  const optionsKey = stableKey({ filters: options.filters ?? {}, sort: options.sort ?? null, pageSize: options.pageSize ?? DEFAULT_PAGE_SIZE, ranges: options.ranges ?? null });
  const defaults = useMemo(
    () => JSON.parse(optionsKey) as { filters: F; sort: SortState | null; pageSize: number; ranges: [string, string][] | null },
    [optionsKey],
  );
  const search = params.toString();

  const state = useMemo(() => {
    const current = new URLSearchParams(search);
    const name = (key: string) => `${prefix}${key}`;
    const filters = { ...defaults.filters };
    for (const key of Object.keys(defaults.filters) as (keyof F & string)[]) {
      const raw = current.get(name(key));
      if (raw !== null) filters[key] = raw as F[keyof F & string];
    }
    const rawSort = current.get(name(TABLE_PARAMS.sort));
    const sort: SortState | null =
      rawSort === null ? defaults.sort : rawSort === '' ? null : { column: rawSort, direction: current.get(name(TABLE_PARAMS.direction)) === 'desc' ? 'desc' : 'asc' };
    const rawPage = Number(current.get(name(TABLE_PARAMS.page)));
    const page = Number.isInteger(rawPage) && rawPage >= 1 ? rawPage : 1;
    const rawSize = Number(current.get(name(TABLE_PARAMS.pageSize)));
    const pageSize = PAGE_SIZES.includes(rawSize) ? rawSize : defaults.pageSize;

    const keys = Object.keys(defaults.filters);
    const ranges = defaults.ranges ?? (keys.includes('desde') && keys.includes('hasta') ? [['desde', 'hasta'] as [string, string]] : []);
    const grouped = new Set(ranges.flat());
    const differs = (key: string) => filters[key] !== defaults.filters[key];
    const activeFilterCount =
      ranges.filter(([from, to]) => differs(from) || differs(to)).length + keys.filter((key) => !grouped.has(key) && differs(key)).length;
    return { filters, sort, page, pageSize, activeFilterCount };
  }, [search, prefix, defaults]);

  /** Aplica un cambio sobre la dirección actual (reemplaza la entrada del historial). */
  const update = useCallback(
    (change: (next: URLSearchParams, name: (key: string) => string) => void) => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          change(next, (key) => `${prefix}${key}`);
          return next;
        },
        { replace: true },
      );
    },
    [setParams, prefix],
  );

  const writeFilters = useCallback(
    (values: Partial<F>) =>
      update((next, name) => {
        for (const [key, value] of Object.entries(values)) {
          if (!(key in defaults.filters) || value === undefined) continue;
          if (value === defaults.filters[key]) next.delete(name(key));
          else next.set(name(key), value);
        }
        next.delete(name(TABLE_PARAMS.page));
      }),
    [update, defaults],
  );

  const setFilter = useCallback((key: keyof F & string, value: string) => writeFilters({ [key]: value } as Partial<F>), [writeFilters]);

  const clearFilters = useCallback(
    () =>
      update((next, name) => {
        for (const key of Object.keys(defaults.filters)) next.delete(name(key));
        next.delete(name(TABLE_PARAMS.page));
      }),
    [update, defaults],
  );

  const setSort = useCallback(
    (sort: SortState | null) =>
      update((next, name) => {
        if (sameSort(sort, defaults.sort)) {
          next.delete(name(TABLE_PARAMS.sort));
          next.delete(name(TABLE_PARAMS.direction));
        } else if (sort === null) {
          next.set(name(TABLE_PARAMS.sort), '');
          next.delete(name(TABLE_PARAMS.direction));
        } else {
          next.set(name(TABLE_PARAMS.sort), sort.column);
          next.set(name(TABLE_PARAMS.direction), sort.direction);
        }
        next.delete(name(TABLE_PARAMS.page));
      }),
    [update, defaults],
  );

  const setPage = useCallback(
    (page: number) =>
      update((next, name) => {
        if (page <= 1) next.delete(name(TABLE_PARAMS.page));
        else next.set(name(TABLE_PARAMS.page), String(Math.trunc(page)));
      }),
    [update],
  );

  const setPageSize = useCallback(
    (size: number) =>
      update((next, name) => {
        if (size === defaults.pageSize || !PAGE_SIZES.includes(size)) next.delete(name(TABLE_PARAMS.pageSize));
        else next.set(name(TABLE_PARAMS.pageSize), String(size));
        next.delete(name(TABLE_PARAMS.page));
      }),
    [update, defaults],
  );

  const dateRange = useCallback(
    (fromKey: keyof F & string = 'desde', toKey: keyof F & string = 'hasta'): DateRange => {
      const from = state.filters[fromKey] ?? '';
      const to = state.filters[toKey] ?? '';
      return from || to ? { from: from || null, to: to || null } : EMPTY_RANGE;
    },
    [state.filters],
  );

  const setDateRange = useCallback(
    (range: DateRange, fromKey: keyof F & string = 'desde', toKey: keyof F & string = 'hasta') =>
      writeFilters({ [fromKey]: range.from ?? '', [toKey]: range.to ?? '' } as Partial<F>),
    [writeFilters],
  );

  const tableProps = useMemo<TableSortProps>(
    () => ({ sort: state.sort, onSortChange: setSort, page: state.page, onPageChange: setPage, pageSize: state.pageSize, onPageSizeChange: setPageSize }),
    [state.sort, state.page, state.pageSize, setSort, setPage, setPageSize],
  );

  return {
    filters: state.filters,
    setFilter,
    setFilters: writeFilters,
    clearFilters,
    activeFilterCount: state.activeFilterCount,
    sort: state.sort,
    setSort,
    page: state.page,
    setPage,
    pageSize: state.pageSize,
    setPageSize,
    dateRange,
    setDateRange,
    tableProps,
  };
}
