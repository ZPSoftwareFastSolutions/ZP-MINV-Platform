// Barra fija inferior de móvil: progreso, total y «Ver resumen» (abre la hoja inferior con el resumen completo).
// Su alto se reserva al final del documento con <html data-builder-bar> (ver builder.css).

import { ChevronUp } from 'lucide-react';
import type { RefObject } from 'react';
import type { BuildProgress as Progress } from '@/1-domain/builder/build';
import { formatMoney, pluralize } from '@/shared/format';
import { BuildProgress } from './BuildProgress';
import { RefButton } from './RefButton';

export interface MobileSummaryBarProps {
  count: number;
  total: number;
  progress: Progress;
  onOpen: () => void;
  /** Referencia al botón «Ver resumen»: los diálogos abiertos desde la hoja le devuelven el foco al cerrarse. */
  buttonRef?: RefObject<HTMLButtonElement | null>;
}

export function MobileSummaryBar({ count, total, progress, onOpen, buttonRef }: MobileSummaryBarProps) {
  return (
    <div className="fixed inset-x-0 bottom-0 z-30 border-t border-border bg-surface/95 shadow-card backdrop-blur-md lg:hidden" style={{ paddingBottom: 'env(safe-area-inset-bottom)' }}>
      <BuildProgress progress={progress} size="sm" className="[&>div]:rounded-none" />
      <div className="mx-auto flex max-w-7xl items-center justify-between gap-3 px-4 py-2.5 sm:px-6">
        <div className="min-w-0">
          <p className="text-xs text-text-muted">
            {count > 0 ? pluralize(count, 'pieza', 'piezas') : 'Sin piezas'} · {progress.covered} de {progress.required} esenciales
          </p>
          <p className="font-display text-xl font-semibold leading-tight text-text tabular-nums">{formatMoney(total)}</p>
        </div>
        <RefButton buttonRef={buttonRef} variant="brand" rightIcon={<ChevronUp aria-hidden="true" />} onClick={onOpen} aria-haspopup="dialog">
          Ver resumen
        </RefButton>
      </div>
    </div>
  );
}
