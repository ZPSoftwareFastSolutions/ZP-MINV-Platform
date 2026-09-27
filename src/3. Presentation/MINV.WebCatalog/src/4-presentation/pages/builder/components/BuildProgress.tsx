import clsx from 'clsx';
import type { BuildProgress as Progress } from '@/1-domain/builder/build';

export interface BuildProgressProps {
  progress: Progress;
  /** `md` muestra la leyenda «5 de 6 piezas esenciales»; `sm` solo la barra (la leyenda va aparte). */
  size?: 'sm' | 'md';
  className?: string;
}

/** Barra de progreso de las piezas esenciales (degradado de marca; solo transform al animar). */
export function BuildProgress({ progress, size = 'md', className }: BuildProgressProps) {
  const label = `${progress.covered} de ${progress.required} piezas esenciales`;
  return (
    <div className={className}>
      {size === 'md' && (
        <div className="mb-2 flex items-center justify-between gap-3 text-sm">
          <span className="font-semibold text-text">{progress.complete ? 'Armado completo' : 'Progreso del armado'}</span>
          <span className="tabular-nums text-text-muted">{label}</span>
        </div>
      )}
      <div
        role="progressbar"
        aria-label="Piezas esenciales del armado"
        aria-valuemin={0}
        aria-valuemax={progress.required}
        aria-valuenow={progress.covered}
        aria-valuetext={label}
        className={clsx('overflow-hidden rounded-full bg-surface-3', size === 'md' ? 'h-2.5' : 'h-1.5')}
      >
        <div
          className={clsx(
            'h-full origin-left rounded-full transition-transform duration-300 ease-out',
            progress.complete ? 'bg-success' : 'bg-linear-to-r from-primary to-accent',
          )}
          style={{ transform: `scaleX(${progress.ratio})` }}
        />
      </div>
    </div>
  );
}
