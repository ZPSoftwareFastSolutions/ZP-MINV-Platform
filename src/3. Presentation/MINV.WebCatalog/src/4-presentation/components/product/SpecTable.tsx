import clsx from 'clsx';
import type { Spec } from '@/1-domain/catalog/types';

export interface SpecTableProps {
  specs: readonly Spec[];
  /** Título accesible de la lista («Especificaciones de …»). */
  caption?: string;
  /** Solo las especificaciones filtrables (las principales). */
  filterableOnly?: boolean;
  limit?: number;
  columns?: 1 | 2;
  className?: string;
}

/** Ficha técnica como lista de definiciones (etiqueta → valor formateado), en 1 o 2 columnas. */
export function SpecTable({ specs, caption, filterableOnly = false, limit, columns = 1, className }: SpecTableProps) {
  let rows = filterableOnly ? specs.filter((spec) => spec.filterable) : [...specs];
  if (limit != null) rows = rows.slice(0, limit);
  if (rows.length === 0) return null;
  return (
    <div className={className}>
      {caption && <p className="sr-only">{caption}</p>}
      <dl className={clsx('grid grid-cols-1 gap-x-8', columns === 2 && 'sm:grid-cols-2')}>
        {rows.map((spec) => (
          <div key={spec.key} className="flex items-baseline justify-between gap-4 border-b border-border py-2.5 text-sm">
            <dt className="shrink-0 text-text-muted">{spec.label}</dt>
            <dd className="text-right font-medium text-text">{spec.text}</dd>
          </div>
        ))}
      </dl>
    </div>
  );
}
