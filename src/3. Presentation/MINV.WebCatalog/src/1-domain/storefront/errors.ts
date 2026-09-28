// Errores de la tienda conectada en términos del dominio: qué pasó (clase) y qué decirle al cliente, sin HTTP.
// El adaptador HTTP traduce cada respuesta `application/problem+json` a uno de estos (2-application/storefront/mappers).

import type { StockShortage } from './types';

export type StorefrontErrorKind =
  /** 400: el cuerpo no pasó la validación (`errors[]`). */
  | 'validation'
  /** 404: producto o reserva inexistente, o el teléfono no coincide (no se distingue, regla S-06). */
  | 'not_found'
  /** 409: falta stock de una o más piezas; no se reservó nada (`shortages[]`). */
  | 'insufficient_stock'
  /** 422 `domain`: regla del dominio (`code`: `pcbuild.contact_phone`, `pcbuild.slot`, `pcbuild.state`…). */
  | 'domain'
  /** 422 `idempotency`: la misma llave con otro contenido. */
  | 'idempotency'
  /** 429: límite por IP superado. */
  | 'rate_limited'
  /** 503: la tienda no está configurada en el servidor. */
  | 'unavailable'
  /** No hubo respuesta: sin conexión, CORS o servidor caído. */
  | 'network'
  | 'unknown';

export interface StorefrontErrorInit {
  kind: StorefrontErrorKind;
  /** Código HTTP (0 cuando no hubo respuesta). */
  status: number;
  /** Texto en español para mostrar tal cual (`detail` del problema o uno propio). */
  detail: string;
  /** Código estable del dominio (`storefront.insufficient_stock`, `pcbuild.state`…). */
  code?: string | null;
  errors?: string[];
  shortages?: StockShortage[];
}

export class StorefrontError extends Error {
  readonly kind: StorefrontErrorKind;
  readonly status: number;
  readonly detail: string;
  readonly code: string | null;
  readonly errors: string[];
  readonly shortages: StockShortage[];

  constructor(init: StorefrontErrorInit) {
    super(init.detail);
    this.name = 'StorefrontError';
    this.kind = init.kind;
    this.status = init.status;
    this.detail = init.detail;
    this.code = init.code ?? null;
    this.errors = init.errors ?? [];
    this.shortages = init.shortages ?? [];
  }
}

export function isStorefrontError(error: unknown): error is StorefrontError {
  return error instanceof StorefrontError;
}

/** Convierte cualquier fallo en un StorefrontError (los desconocidos quedan como `unknown`). */
export function asStorefrontError(error: unknown): StorefrontError {
  if (isStorefrontError(error)) return error;
  const detail = error instanceof Error && error.message ? error.message : 'Ocurrió un error inesperado.';
  return new StorefrontError({ kind: 'unknown', status: 0, detail });
}

/** Mensaje para el cliente según la clase del error (el `detail` de la API ya viene en español y tiene prioridad). */
export function describeStorefrontError(error: StorefrontError): string {
  switch (error.kind) {
    case 'network':
      return 'No pudimos conectarnos con la tienda. Revisá tu conexión e intentá de nuevo.';
    case 'rate_limited':
      return 'Hiciste demasiadas solicitudes seguidas. Esperá un minuto e intentá de nuevo.';
    case 'unavailable':
      return 'La tienda web no está disponible en este momento. Intentá más tarde.';
    case 'unknown':
      return error.detail || 'Ocurrió un error inesperado. Intentá de nuevo.';
    default:
      return error.detail;
  }
}
