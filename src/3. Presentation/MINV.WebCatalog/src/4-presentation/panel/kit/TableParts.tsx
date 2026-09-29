// Piezas internas de DataTable (casilla de fila, botón de detalle, filas por página y paginador del teléfono). No se
// exportan desde `kit/index.ts`: los módulos usan DataTable.

import clsx from 'clsx';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useId } from 'react';
import { formatNumber } from '../lib/format';
import { TICK } from './styles';

export function RowCheckbox({ checked, indeterminate = false, label, onChange }: { checked: boolean; indeterminate?: boolean; label: string; onChange: () => void }) {
  return (
    <label className="-m-1 inline-flex size-11 cursor-pointer items-center justify-center rounded-lg hover:bg-surface-3" title={label}>
      <input
        ref={(element) => {
          if (element) element.indeterminate = indeterminate;
        }}
        type="checkbox"
        checked={checked}
        onChange={onChange}
        aria-label={label}
        className={clsx(TICK, 'mt-0')}
      />
    </label>
  );
}

export function ExpandToggle({ expanded, controls, label, onToggle }: { expanded: boolean; controls: string; label: string; onToggle: () => void }) {
  return (
    <button
      type="button"
      aria-expanded={expanded}
      aria-controls={expanded ? controls : undefined}
      aria-label={label}
      title={label}
      onClick={onToggle}
      className="inline-flex size-11 cursor-pointer items-center justify-center rounded-xl text-text-muted transition-colors duration-200 hover:bg-surface-3 hover:text-text"
    >
      <ChevronRight aria-hidden="true" className={clsx('size-5 transition-transform duration-200', expanded && 'rotate-90')} />
    </button>
  );
}

export function PageSizeSelect({ value, sizes, onChange }: { value: number; sizes: readonly number[]; onChange: (size: number) => void }) {
  const id = useId();
  return (
    <div className="flex items-center gap-2 text-sm text-text-muted">
      <label htmlFor={id} className="whitespace-nowrap">
        Filas por página
      </label>
      <select
        id={id}
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
        className="h-11 cursor-pointer rounded-xl border border-border-control bg-surface px-3 text-sm text-text transition-colors duration-200 hover:border-border-strong focus:border-accent"
      >
        {(sizes.includes(value) ? sizes : [...sizes, value].sort((a, b) => a - b)).map((size) => (
          <option key={size} value={size}>
            {size}
          </option>
        ))}
      </select>
    </div>
  );
}

/** Paginador compacto (teléfono): «‹ Anterior · Página 2 de 10 · Siguiente ›». */
export function CompactPager({ page, pageCount, onChange }: { page: number; pageCount: number; onChange: (page: number) => void }) {
  if (pageCount <= 1) return null;
  const button =
    'inline-flex min-h-11 cursor-pointer items-center gap-1 rounded-xl px-3 text-sm font-semibold text-text-muted transition-colors duration-200 hover:bg-surface-2 hover:text-text disabled:cursor-not-allowed disabled:opacity-40';
  return (
    <nav aria-label="Paginación" className="flex w-full items-center justify-between gap-2">
      <button type="button" className={button} disabled={page <= 1} onClick={() => onChange(page - 1)}>
        <ChevronLeft aria-hidden="true" className="size-5" />
        Anterior
      </button>
      <span className="text-sm text-text-muted tabular-nums">
        Página {formatNumber(page)} de {formatNumber(pageCount)}
      </span>
      <button type="button" className={button} disabled={page >= pageCount} onClick={() => onChange(page + 1)}>
        Siguiente
        <ChevronRight aria-hidden="true" className="size-5" />
      </button>
    </nav>
  );
}
