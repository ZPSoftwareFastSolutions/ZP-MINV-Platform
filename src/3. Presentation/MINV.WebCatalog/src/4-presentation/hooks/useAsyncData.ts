import { useCallback, useEffect, useRef, useState } from 'react';
import { asWebApiError, type WebApiError } from '@/1-domain/auth/errors';

export type AsyncState<T> = { status: 'loading' } | { status: 'error'; error: WebApiError } | { status: 'ready'; data: T };

export interface AsyncData<T> {
  state: AsyncState<T>;
  /** Hay una recarga en curso (el botón «Reintentar» se muestra ocupado; lo que ya se ve sigue en pantalla). */
  reloading: boolean;
  reload(): void;
  /** Cambia los datos ya cargados sin volver al servidor (después de guardar o liberar). */
  update(change: (current: T) => T): void;
}

/**
 * Carga datos del servidor al montar, con sus tres estados (cargando, error y listo) y «Reintentar». Si el componente
 * se desmonta antes de la respuesta, el pedido se cancela y el resultado se descarta.
 */
export function useAsyncData<T>(load: (signal: AbortSignal) => Promise<T>): AsyncData<T> {
  const [state, setState] = useState<AsyncState<T>>({ status: 'loading' });
  const [attempt, setAttempt] = useState(0);
  const [reloading, setReloading] = useState(false);
  const loadRef = useRef(load);

  useEffect(() => {
    loadRef.current = load;
  });

  useEffect(() => {
    const controller = new AbortController();
    loadRef.current(controller.signal).then(
      (data) => {
        if (controller.signal.aborted) return;
        setState({ status: 'ready', data });
        setReloading(false);
      },
      (error: unknown) => {
        if (controller.signal.aborted) return;
        setState({ status: 'error', error: asWebApiError(error) });
        setReloading(false);
      },
    );
    return () => controller.abort();
  }, [attempt]);

  const reload = useCallback(() => {
    setReloading(true);
    setAttempt((current) => current + 1);
  }, []);

  const update = useCallback((change: (current: T) => T) => {
    setState((current) => (current.status === 'ready' ? { status: 'ready', data: change(current.data) } : current));
  }, []);

  return { state, reloading, reload, update };
}
