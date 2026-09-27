import clsx from 'clsx';
import { Star } from 'lucide-react';
import { formatNumber } from '@/shared/format';

export interface RatingProps {
  /** Puntaje de 0 a 5. Si no hay dato (undefined/null) no se muestra nada: no se inventan valoraciones. */
  value?: number | null;
  /** Cantidad de valoraciones. */
  count?: number;
  size?: 'sm' | 'md';
  className?: string;
}

/** Estrellas decorativas con texto accesible («4,5 de 5»). Devuelve null cuando no hay valor. */
export function Rating({ value, count, size = 'sm', className }: RatingProps) {
  if (value == null || !Number.isFinite(value)) return null;
  const clamped = Math.min(5, Math.max(0, value));
  return (
    <div className={clsx('inline-flex items-center gap-1.5 text-text-muted', size === 'sm' ? 'text-xs' : 'text-sm', className)}>
      <span role="img" aria-label={`${formatNumber(clamped, { maxDecimals: 1 })} de 5 estrellas`} className="inline-flex gap-0.5">
        {Array.from({ length: 5 }, (_, index) => {
          const fill = Math.min(1, Math.max(0, clamped - index));
          return (
            <span key={index} className={clsx('relative inline-flex', size === 'sm' ? 'size-3.5' : 'size-4')}>
              <Star aria-hidden="true" className="absolute inset-0 size-full text-border-strong" />
              <span aria-hidden="true" className="absolute inset-0 overflow-hidden" style={{ width: `${fill * 100}%` }}>
                <Star className="size-full fill-warning text-warning" />
              </span>
            </span>
          );
        })}
      </span>
      <span aria-hidden="true" className="font-medium tabular-nums">
        {formatNumber(clamped, { maxDecimals: 1 })}
      </span>
      {count != null && <span className="tabular-nums">({formatNumber(count)})</span>}
    </div>
  );
}
