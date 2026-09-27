// Barra de resultados: botón «Filtros» (móvil), conteo, búsqueda dentro del alcance, orden y vista grilla/lista.

import clsx from 'clsx';
import { ChevronDown, LayoutGrid, Rows3, Search, SlidersHorizontal, X } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { isProductSort, PRODUCT_SORTS, type ProductSort } from '@/1-domain/catalog/products';
import { Button } from '@/4-presentation/components/ui/Button';
import { IconButton } from '@/4-presentation/components/ui/IconButton';
import { formatNumber } from '@/shared/format';
import type { CatalogView } from './catalogFilters';

export interface ResultsToolbarProps {
  total: number;
  page: number;
  pageCount: number;
  sort: ProductSort;
  view: CatalogView;
  q?: string;
  /** Dónde se busca («Tarjetas de video», «el catálogo»). */
  scopeLabel: string;
  activeFilters: number;
  onSortChange: (sort: ProductSort) => void;
  onViewChange: (view: CatalogView) => void;
  onSearch: (q: string | undefined) => void;
  onOpenFilters: () => void;
}

export function ResultsToolbar({ total, page, pageCount, sort, view, q, scopeLabel, activeFilters, onSortChange, onViewChange, onSearch, onOpenFilters }: ResultsToolbarProps) {
  const id = useId();
  const [text, setText] = useState(q ?? '');
  const [syncedQ, setSyncedQ] = useState(q ?? '');
  if (syncedQ !== (q ?? '')) {
    setSyncedQ(q ?? '');
    setText(q ?? '');
  }

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    onSearch(text.trim() || undefined);
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p role="status" aria-live="polite" className="text-sm text-text-muted">
          <span className="font-display text-lg font-semibold text-text tabular-nums">{formatNumber(total)}</span> {total === 1 ? 'producto' : 'productos'}
          {pageCount > 1 && (
            <span className="text-text-faint">
              {' '}
              · página {page} de {pageCount}
            </span>
          )}
        </p>
        <Button variant="subtle" leftIcon={<SlidersHorizontal />} onClick={onOpenFilters} className="lg:hidden">
          Filtros
          {activeFilters > 0 && (
            <span className="ml-1 rounded-full bg-accent px-1.5 font-display text-xs font-bold text-bg tabular-nums">
              {activeFilters}
              <span className="sr-only"> activos</span>
            </span>
          )}
        </Button>
      </div>

      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
        <form role="search" onSubmit={submit} className="relative flex min-w-0 flex-1 items-center">
          <label htmlFor={`${id}-q`} className="sr-only">
            Buscar en {scopeLabel}
          </label>
          <Search aria-hidden="true" className="pointer-events-none absolute left-3 size-4 text-text-faint" />
          <input
            id={`${id}-q`}
            type="search"
            value={text}
            onChange={(event) => setText(event.target.value)}
            placeholder={`Buscar en ${scopeLabel}…`}
            autoComplete="off"
            enterKeyHint="search"
            className={clsx(
              'h-11 w-full rounded-xl border border-border bg-surface-2 pl-9 text-sm text-text placeholder:text-text-faint',
              'transition-colors duration-200 hover:border-border-strong focus:border-accent focus:outline-none [&::-webkit-search-cancel-button]:hidden',
              text ? 'pr-20' : 'pr-11',
            )}
          />
          <div className="absolute right-1 flex items-center">
            {text && (
              <IconButton
                size="sm"
                label="Borrar búsqueda"
                icon={<X />}
                onClick={() => {
                  setText('');
                  if (q) onSearch(undefined);
                }}
              />
            )}
            <IconButton size="sm" type="submit" label="Buscar" icon={<Search />} variant="primary" />
          </div>
        </form>

        <div className="flex items-center gap-2">
          <label htmlFor={`${id}-orden`} className="text-sm text-text-muted max-md:sr-only">
            Ordenar por
          </label>
          <div className="relative flex-1 sm:flex-none">
            <select
              id={`${id}-orden`}
              value={sort}
              onChange={(event) => {
                if (isProductSort(event.target.value)) onSortChange(event.target.value);
              }}
              className="h-11 w-full cursor-pointer appearance-none rounded-xl border border-border bg-surface-2 pr-9 pl-3 text-sm font-medium text-text transition-colors duration-200 hover:border-border-strong focus:border-accent focus:outline-none sm:w-44"
            >
              {PRODUCT_SORTS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            <ChevronDown aria-hidden="true" className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-text-faint" />
          </div>

          <div role="group" aria-label="Vista de los resultados" className="inline-flex shrink-0 rounded-xl border border-border bg-surface-2 p-0.5">
            <IconButton
              size="sm"
              label="Ver en grilla"
              icon={<LayoutGrid />}
              aria-pressed={view === 'grilla'}
              onClick={() => onViewChange('grilla')}
              className="h-10 w-10 rounded-[0.625rem] aria-pressed:bg-surface-3 aria-pressed:text-accent-hover"
            />
            <IconButton
              size="sm"
              label="Ver en lista"
              icon={<Rows3 />}
              aria-pressed={view === 'lista'}
              onClick={() => onViewChange('lista')}
              className="h-10 w-10 rounded-[0.625rem] aria-pressed:bg-surface-3 aria-pressed:text-accent-hover"
            />
          </div>
        </div>
      </div>
    </div>
  );
}
