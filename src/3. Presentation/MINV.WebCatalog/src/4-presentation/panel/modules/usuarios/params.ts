// Módulo «Usuarios» · la pestaña abierta (`?pestana=`) y la acción pedida desde el tablero (`?accion=`), en la dirección
// de la página: se puede recargar o compartir el enlace. Cambiar de pestaña deja la dirección limpia (cada pestaña tiene
// sus propios filtros) y reemplaza la entrada del historial, como los filtros de `useTableState`.

import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';

export const TAB_PARAM = 'pestana';
export const ACTION_PARAM = 'accion';

/** Pestaña de la dirección (una desconocida vuelve a `fallback`) y cómo cambiarla, con filtros opcionales para la nueva. */
export function useTabParam<T extends string>(allowed: readonly T[], fallback: T): [T, (next: T, filters?: Readonly<Record<string, string>>) => void] {
  const [params, setParams] = useSearchParams();
  const raw = params.get(TAB_PARAM);
  const tab = allowed.find((item) => item === raw) ?? fallback;
  const setTab = useCallback(
    (next: T, filters: Readonly<Record<string, string>> = {}) => {
      setParams(
        () => {
          const fresh = new URLSearchParams();
          if (next !== fallback) fresh.set(TAB_PARAM, next);
          for (const [key, value] of Object.entries(filters)) fresh.set(key, value);
          return fresh;
        },
        { replace: true },
      );
    },
    [setParams, fallback],
  );
  return [tab, setTab];
}

/** Acción pedida por la dirección (`?accion=nuevo`) y cómo olvidarla una vez atendida. */
export function useActionParam(): [string, () => void] {
  const [params, setParams] = useSearchParams();
  const action = params.get(ACTION_PARAM) ?? '';
  const clear = useCallback(() => {
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete(ACTION_PARAM);
        return next;
      },
      { replace: true },
    );
  }, [setParams]);
  return [action, clear];
}
