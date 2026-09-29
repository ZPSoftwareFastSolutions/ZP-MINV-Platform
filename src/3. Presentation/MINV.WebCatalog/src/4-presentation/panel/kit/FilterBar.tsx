// USO · Barra de filtros de una lista, con el contador de filtros activos y «Limpiar filtros». Los controles se reparten
// en una grilla (1 columna en el teléfono, 2 desde 640 px y 4 desde 1024 px; DateRangeField ocupa dos).
//
//   <FilterBar activeCount={tabla.activeFilterCount} onClear={tabla.clearFilters}>
//     <SearchField label="Buscar" value={tabla.filters.q} onChange={(q) => tabla.setFilter('q', q)} />
//     <SelectField label="Estado" value={tabla.filters.estado} onChange={(v) => tabla.setFilter('estado', v)} options={…} />
//     <SelectField label="Sucursal" allLabel="Todas las sucursales" … />
//     <DateRangeField label="Fechas" value={tabla.dateRange()} onChange={(rango) => tabla.setDateRange(rango)} />
//   </FilterBar>
//
// En el teléfono los filtros se pliegan detrás de «Mostrar filtros» (se abren solos si hay alguno activo al entrar).

import clsx from 'clsx';
import { ChevronDown, SlidersHorizontal, X } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { Button } from '@/4-presentation/components/ui/Button';

export interface FilterBarProps {
  /** Filtros distintos de su valor por defecto (useTableState().activeFilterCount). */
  activeCount: number;
  /** «Limpiar filtros». */
  onClear: () => void;
  /** Controles (SearchField, SelectField, ComboBox, DateRangeField). */
  children: ReactNode;
  /** Título de la barra (por defecto «Filtros»). */
  title?: string;
  /** Algo más a la derecha del título. */
  extra?: ReactNode;
  className?: string;
}

export function FilterBar({ activeCount, onClear, children, title = 'Filtros', extra, className }: FilterBarProps) {
  const bodyId = useId();
  const titleId = useId();
  const [open, setOpen] = useState(activeCount > 0);
  const active = activeCount > 0;
  return (
    <section aria-labelledby={titleId} className={clsx('min-w-0 rounded-card border border-border bg-surface p-3 sm:p-4', className)}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex min-w-0 items-center gap-2">
          <SlidersHorizontal aria-hidden="true" className="size-4 shrink-0 text-accent" />
          <h2 id={titleId} className="font-display text-base font-semibold">
            {title}
          </h2>
          <span
            className={clsx('rounded-full px-2 py-0.5 text-xs font-semibold', active ? 'bg-accent-soft text-accent-hover' : 'bg-surface-2 text-text-muted')}
            data-testid="filtros-activos"
          >
            {activeCount === 0 ? 'Ninguno activo' : activeCount === 1 ? '1 activo' : `${activeCount} activos`}
          </span>
        </div>
        <div className="flex flex-wrap items-center gap-1">
          {extra}
          <Button
            variant="ghost"
            leftIcon={<X />}
            aria-disabled={!active || undefined}
            onClick={() => {
              if (active) onClear();
            }}
          >
            Limpiar filtros
          </Button>
          <Button
            variant="ghost"
            className="sm:hidden"
            aria-expanded={open}
            aria-controls={bodyId}
            aria-label={open ? 'Ocultar filtros' : 'Mostrar filtros'}
            rightIcon={<ChevronDown className={clsx('transition-transform duration-200', open && 'rotate-180')} />}
            onClick={() => setOpen((current) => !current)}
          >
            {open ? 'Ocultar' : 'Mostrar'}
          </Button>
        </div>
      </div>
      <div id={bodyId} className={clsx('mt-3 grid grid-cols-1 items-start gap-3 sm:grid-cols-2 lg:grid-cols-4', !open && 'max-sm:hidden')}>
        {children}
      </div>
    </section>
  );
}
