// USO · Avisos breves del panel (esquina inferior derecha; se leen en voz alta). Español neutro.
//
//   const notify = useNotify();
//   notify.success('Cliente guardado');
//   notify.info('Exportación lista', 'Se descargó ventas-2026-09-28.csv');
//   notify.warning('La caja sigue abierta', 'Ciérrela antes de terminar el turno.');
//   notify.error('No se pudo exportar', error);      ← un error del servidor se explica en palabras (y el permiso que falta)
//
// Los errores y advertencias duran 8 s (con el texto completo); el resto, 4 s. Para comandos, useRpcCommand ya avisa solo.

import { useMemo } from 'react';
import { asWebApiError } from '@/1-domain/auth/errors';
import { useToast } from '@/4-presentation/hooks/useToast';
import { usePermissions } from '../hooks/usePermissions';
import { ACCESS_DENIED_TITLE, describePanelError, missingPermissions, missingPermissionsText } from '../lib/rpc';

export interface Notify {
  success(title: string, description?: string): void;
  info(title: string, description?: string): void;
  warning(title: string, description?: string): void;
  /** `detail`: un texto o el error (del servidor o cualquiera). */
  error(title: string, detail?: unknown): void;
}

const LONG_MS = 8000;

export function useNotify(): Notify {
  const toast = useToast();
  const { permissions } = usePermissions();
  return useMemo<Notify>(
    () => ({
      success: (title, description) => {
        toast.notify({ tone: 'success', title, description });
      },
      info: (title, description) => {
        toast.notify({ tone: 'info', title, description });
      },
      warning: (title, description) => {
        toast.notify({ tone: 'warning', title, description, duration: LONG_MS });
      },
      error: (title, detail) => {
        if (detail === undefined || typeof detail === 'string') {
          toast.notify({ tone: 'danger', title, description: detail, duration: LONG_MS });
          return;
        }
        const failure = asWebApiError(detail);
        const missing = missingPermissions(failure, { granted: permissions });
        toast.notify({
          tone: 'danger',
          title: missing.length > 0 ? ACCESS_DENIED_TITLE : title,
          description: missing.length > 0 ? missingPermissionsText(missing) : describePanelError(failure, { granted: permissions }),
          duration: LONG_MS,
        });
      },
    }),
    [toast, permissions],
  );
}
