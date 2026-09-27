// Barra fija inferior de móvil: progreso, total y «Ver resumen» (abre la hoja inferior con el resumen completo).

import { ChevronUp } from 'lucide-react';
import type { BuildProgress as Progress } from '@/1-domain/builder/build';
import { Button } from '@/4-presentation/components/ui/Button';
import { formatMoney, pluralize } from '@/shared/format';
import { BuildProgress } from './BuildProgress';

export interface MobileSummaryBarProps {
  count: number;
  total: number;
  progress: Progress;
  onOpen: () => void;
}

export function MobileSummaryBar({ count, total, progress, onOpen }: MobileSummaryBarProps) {
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
        <Button variant="brand" rightIcon={<ChevronUp />} onClick={onOpen} aria-haspopup="dialog">
          Ver resumen
        </Button>
      </div>
    </div>
  );
}
