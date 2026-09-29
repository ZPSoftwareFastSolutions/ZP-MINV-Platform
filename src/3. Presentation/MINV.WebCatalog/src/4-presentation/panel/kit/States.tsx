// USO · Estados de una pantalla o sección del panel (regla P-10: carga, vacío y error con «Reintentar»).
//
//   <ErrorState error={consulta.error} onRetry={consulta.reload} />                  ← el texto sale del error
//   <EmptyState title="No hay ventas en estas fechas" description="Pruebe con otro rango.">
//     <Button onClick={tabla.clearFilters}>Limpiar filtros</Button>
//   </EmptyState>
//   <LoadingState label="Cargando ventas…" rows={3} />   ·   <Skeleton className="h-4 w-32" />
//
// ErrorState describe el error en palabras (si faltan permisos, dice cuáles). EmptyState, LoadingState y Skeleton son
// los mismos de la tienda (se exportan desde `kit/index.ts`).

import clsx from 'clsx';
import { CloudOff, RotateCcw, ShieldAlert } from 'lucide-react';
import { asWebApiError } from '@/1-domain/auth/errors';
import { Button } from '@/4-presentation/components/ui/Button';
import { describePanelError, missingPermissions } from '../lib/rpc';
import { usePermissions } from '../hooks/usePermissions';

export interface ErrorStateProps {
  /** El error (de useRpcQuery o de cualquier pedido). */
  error: unknown;
  /** Título (por defecto «No se pudo cargar la información»; sin permisos, «No tiene permiso para ver esto»). */
  title?: string;
  /** Operación que falló (para nombrar el permiso que falta si el servidor no lo dice). */
  operation?: string;
  onRetry?: () => void;
  retrying?: boolean;
  className?: string;
}

export function ErrorState({ error, title, operation, onRetry, retrying = false, className }: ErrorStateProps) {
  const { permissions } = usePermissions();
  const failure = asWebApiError(error);
  // Sin un permiso, reintentar no sirve: se explica cuál falta. Otros rechazos (sucursal, módulo) sí pueden reintentarse.
  const missing = missingPermissions(failure, { operation, granted: permissions });
  const denied = failure.kind === 'access_denied';
  const Icon = denied ? ShieldAlert : CloudOff;
  return (
    <div
      role="alert"
      className={clsx('flex flex-col items-center gap-3 rounded-card border border-danger/40 bg-danger-soft/60 px-6 py-10 text-center', className)}
      data-testid="estado-error"
    >
      <span aria-hidden="true" className="flex size-14 items-center justify-center rounded-2xl bg-danger-soft text-danger-text">
        <Icon className="size-7" />
      </span>
      <p className="font-display text-lg font-semibold text-text">{title ?? (denied ? 'No tiene permiso para ver esto' : 'No se pudo cargar la información')}</p>
      <p className="max-w-md text-sm text-text-muted">{describePanelError(failure, { operation, granted: permissions })}</p>
      {onRetry && missing.length === 0 && (
        <Button variant="outline" leftIcon={<RotateCcw />} loading={retrying} onClick={onRetry}>
          Reintentar
        </Button>
      )}
    </div>
  );
}
