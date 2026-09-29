// Consulta del servidor para una pantalla del panel (listas, detalles, tableros), con sus estados.
//
//   const ventas = useRpcQuery('GetSalesQuery', { desde, hasta, estado });
//   <DataTable rows={ventas.data} loading={ventas.loading} error={ventas.error} onRetry={ventas.reload} … />
//
// - Carga al montar y cada vez que cambia el contenido del pedido (se compara por valor, no por identidad).
// - Descarta las respuestas viejas: si el filtro cambia antes de que llegue la respuesta anterior, esa se ignora (y el
//   pedido se cancela). Al desmontar también se cancela.
// - Vuelve a consultar cuando cambia la sucursal activa (después de `SelectBranchCommand` el esqueleto refresca la
//   sesión con `useSession().refresh()`). Los datos de la otra sucursal NO se muestran mientras tanto.
// - Mientras llega la respuesta de un filtro nuevo se siguen viendo los datos anteriores (sin parpadeo); `loading` es
//   solo la primera carga. `fetching` avisa que hay un pedido en curso.
// - `enabled: false` no consulta (por ejemplo, hasta que se elija un filtro obligatorio).
// - Un 401 cierra la sesión solo (lo vigila el RPC de la sesión); aquí queda como error.

import { useCallback, useEffect, useRef, useState } from 'react';
import { asWebApiError, type WebApiError } from '@/1-domain/auth/errors';
import type { RpcOperationName, RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { useRpc } from '@/4-presentation/hooks/useRpc';
import { useSession } from '@/4-presentation/hooks/useSession';
import { isAbort, stableKey } from '../lib/rpc';

export interface RpcQueryOptions {
  /** false = no consulta (y conserva lo último que tenía). Por defecto true. */
  enabled?: boolean;
  /**
   * Mientras llega la respuesta de un contenido nuevo, sigue mostrando los datos anteriores (por defecto sí: la tabla
   * no parpadea al filtrar). Con false, un contenido nuevo vuelve a «cargando».
   */
  keepPreviousData?: boolean;
}

export type RpcQueryStatus = 'idle' | 'loading' | 'success' | 'error';

export interface RpcQuery<T> {
  /** Último resultado (undefined mientras no hay ninguno para mostrar). */
  data: T | undefined;
  /** Falla del último pedido (null si respondió bien o si todavía no respondió). */
  error: WebApiError | null;
  status: RpcQueryStatus;
  /** Primera carga: no hay datos para mostrar todavía (esqueleto). */
  loading: boolean;
  /** Hay un pedido en curso (primera carga, filtro nuevo o recarga). */
  fetching: boolean;
  /** Vuelve a consultar («Reintentar», «Actualizar» o después de un comando). */
  reload(): void;
  /** Cambia los datos en la página sin volver al servidor (después de un comando que ya devolvió la fila nueva). */
  setData(update: T | ((current: T | undefined) => T | undefined)): void;
}

interface Settled<T> {
  /** Operación + contenido del pedido que respondió bien. */
  base: string;
  /** Sucursal activa con la que respondió. */
  branch: string;
  /** Llave completa (con la sucursal y el número de recarga) del pedido que respondió bien. */
  request: string;
  data: T | undefined;
  hasData: boolean;
  error: WebApiError | null;
  /** Llave del pedido que falló. */
  errorRequest: string | null;
}

/** Sucursal activa de la sesión, como texto (la vista de todas las sucursales de la gerencia es «todas»). */
function branchKeyOf(activeBranchId: string | null | undefined): string {
  return activeBranchId ?? 'todas';
}

export function useRpcQuery<K extends RpcOperationName>(operation: K, payload: RpcRequestOf<K>, options: RpcQueryOptions = {}): RpcQuery<RpcResponseOf<K>> {
  const { enabled = true, keepPreviousData = true } = options;
  const rpc = useRpc();
  const { session } = useSession();
  const branch = branchKeyOf(session?.access.activeBranchId);
  const base = `${operation}\n${stableKey(payload)}`;
  const [reloads, setReloads] = useState(0);
  const request = `${base}\n${branch}\n${reloads}`;
  const [state, setState] = useState<Settled<RpcResponseOf<K>>>(() => ({
    base: '',
    branch,
    request: '',
    data: undefined,
    hasData: false,
    error: null,
    errorRequest: null,
  }));

  // El contenido se lee de una referencia: el pedido se repite cuando cambia su VALOR (`base`), no su identidad.
  const payloadRef = useRef(payload);
  useEffect(() => {
    payloadRef.current = payload;
  });

  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    rpc.send(operation, payloadRef.current, { signal: controller.signal }).then(
      (data) => {
        if (controller.signal.aborted) return;
        setState({ base, branch, request, data, hasData: true, error: null, errorRequest: null });
      },
      (error: unknown) => {
        if (controller.signal.aborted || isAbort(error)) return;
        setState((current) => ({ ...current, error: asWebApiError(error), errorRequest: request }));
      },
    );
    return () => controller.abort();
  }, [rpc, operation, base, branch, request, enabled]);

  const reload = useCallback(() => setReloads((count) => count + 1), []);

  const setData = useCallback((update: RpcResponseOf<K> | ((current: RpcResponseOf<K> | undefined) => RpcResponseOf<K> | undefined)) => {
    setState((current) => {
      const data = typeof update === 'function' ? (update as (value: RpcResponseOf<K> | undefined) => RpcResponseOf<K> | undefined)(current.data) : update;
      return { ...current, data, hasData: data !== undefined };
    });
  }, []);

  const settled = state.request === request || state.errorRequest === request;
  const fetching = enabled && !settled;
  const visible = state.hasData && state.branch === branch && (keepPreviousData || state.base === base);
  const data = visible ? state.data : undefined;
  const error = state.errorRequest === request ? state.error : null;
  const status: RpcQueryStatus = error ? 'error' : visible ? 'success' : fetching ? 'loading' : 'idle';

  return { data, error, status, loading: fetching && !visible, fetching, reload, setData };
}
