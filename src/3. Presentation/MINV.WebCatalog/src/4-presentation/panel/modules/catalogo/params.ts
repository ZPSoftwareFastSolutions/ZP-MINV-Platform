// Módulo «Catálogo» · parámetros de la dirección que NO son filtros: la pestaña (`?pestana=`), los pedidos de una sola vez
// que llegan del tablero o de otros módulos (`?nuevo=producto|categoria|especificacion`, `?editar=SKU`) y el detalle
// abierto (`?producto=SKU`).

import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

export type CatalogTab = 'productos' | 'categorias' | 'especificaciones';

export const TAB_PARAM = 'pestana';
export const NEW_PARAM = 'nuevo';
export const EDIT_PARAM = 'editar';
export const DETAIL_PARAM = 'producto';

/** `?nuevo=` → qué se crea (el tablero manda `producto`; `1` también vale, como en otros módulos). */
export type NewRequest = 'producto' | 'categoria' | 'especificacion';

export function newRequestOf(value: string | null): NewRequest | null {
  if (value === '1' || value === 'producto') return 'producto';
  if (value === 'categoria' || value === 'especificacion') return value;
  return null;
}

const TAB_OF_REQUEST: Readonly<Record<NewRequest, CatalogTab>> = { producto: 'productos', categoria: 'categorias', especificacion: 'especificaciones' };

/** Pestaña a la vista: la del pedido de creación (si hay) o la de la dirección. */
export function tabOf(params: URLSearchParams): CatalogTab {
  const request = newRequestOf(params.get(NEW_PARAM));
  if (request) return TAB_OF_REQUEST[request];
  if (params.get(EDIT_PARAM)) return 'productos';
  const value = params.get(TAB_PARAM);
  return value === 'categorias' || value === 'especificaciones' ? value : 'productos';
}

/**
 * Atiende UNA vez un pedido que llega en la dirección (`?nuevo=producto`, `?editar=SKU`) y lo quita (sin llenar el
 * historial). `ready` dice si ya se puede atender (por ejemplo, cuando llegó el catálogo); mientras no, espera.
 */
export function useOneShotParam(name: string, ready: (value: string) => boolean, handle: (value: string) => void, keep?: Readonly<Record<string, string>>): void {
  const [params, setParams] = useSearchParams();
  const value = params.get(name);
  const [handled, setHandled] = useState<string | null>(null);
  if (value !== handled && (value === null || ready(value))) {
    setHandled(value);
    if (value !== null) handle(value);
  }
  const done = value !== null && value === handled;
  const keepKey = keep ? JSON.stringify(keep) : '';
  useEffect(() => {
    if (!done) return;
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete(name);
        const extra = keepKey ? (JSON.parse(keepKey) as Record<string, string>) : {};
        for (const [key, item] of Object.entries(extra)) next.set(key, item);
        return next;
      },
      { replace: true },
    );
  }, [done, name, keepKey, setParams]);
}
