// Estados de una pantalla que carga datos del servidor (regla P-10): cargando (esqueleto que se anuncia una vez) y error
// con «Reintentar». El estado vacío usa `EmptyState` de components/ui.

import { CloudOff, RotateCcw } from 'lucide-react';
import clsx from 'clsx';
import { Button } from '@/4-presentation/components/ui/Button';
import { Skeleton } from '@/4-presentation/components/ui/Skeleton';

export interface LoadingStateProps {
  /** Texto para lectores de pantalla («Cargando tus reservas…»). */
  label: string;
  /** Cantidad de filas del esqueleto. */
  rows?: number;
  className?: string;
}

export function LoadingState({ label, rows = 3, className }: LoadingStateProps) {
  return (
    <div role="status" aria-live="polite" className={clsx('space-y-3', className)} data-testid="estado-cargando">
      <span className="sr-only">{label}</span>
      {Array.from({ length: rows }, (_, index) => (
        <div key={index} aria-hidden="true" className="rounded-card border border-border bg-surface p-4">
          <Skeleton className="h-4 w-40" />
          <Skeleton className="mt-3 h-4 w-full max-w-md" />
          <Skeleton className="mt-2 h-4 w-2/3 max-w-sm" />
        </div>
      ))}
    </div>
  );
}

export interface ErrorStateProps {
  title: string;
  message: string;
  retrying?: boolean;
  onRetry: () => void;
  className?: string;
}

export function ErrorState({ title, message, retrying = false, onRetry, className }: ErrorStateProps) {
  return (
    <div
      role="alert"
      className={clsx('flex flex-col items-center gap-3 rounded-card border border-danger/40 bg-danger-soft/60 px-6 py-10 text-center', className)}
      data-testid="estado-error"
    >
      <span aria-hidden="true" className="flex size-14 items-center justify-center rounded-2xl bg-danger-soft text-danger-text">
        <CloudOff className="size-7" />
      </span>
      <p className="font-display text-lg font-semibold text-text">{title}</p>
      <p className="max-w-md text-sm text-text-muted">{message}</p>
      <Button variant="outline" leftIcon={<RotateCcw />} loading={retrying} onClick={onRetry}>
        Reintentar
      </Button>
    </div>
  );
}
