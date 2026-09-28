// Cliente HTTP de la API pública de tienda. Es el ÚNICO archivo de la web que usa `fetch` (regla S-07; lo vigila
// src/architecture.test.ts). Traduce cada respuesta al dominio: JSON en éxito, StorefrontError en cualquier falla
// (problem+json, 429 sin cuerpo, red caída).

import { StorefrontError } from '@/1-domain/storefront/errors';
import { STOREFRONT_PREFIX, toStorefrontError, type StorefrontProblemDto } from '@/2-application/storefront';

/** Gateway local de `tools\servidores_locales.ps1`. */
export const DEFAULT_API_URL = 'http://localhost:5090';
/** Valor de `VITE_API_URL` que usa el mock de la V5 en vez de la API. */
export const MOCK_API_URL = 'mock';

/**
 * Base de la API a partir de `VITE_API_URL` (sin barra final; vacía o ausente → el gateway local). «/» significa el MISMO
 * origen que la web (base vacía → rutas relativas `/storefront/v1/…`): es lo que usa la tienda pública, donde el nginx del
 * catálogo reenvía `/storefront/` al gateway y un solo enlace sirve la web y su API.
 */
export function resolveApiUrl(raw: string | undefined | null): string {
  const value = (raw ?? '').trim();
  if (!value) return DEFAULT_API_URL;
  return value.replace(/\/+$/, '');
}

export function isMockApiUrl(raw: string | undefined | null): boolean {
  return (raw ?? '').trim().toLowerCase() === MOCK_API_URL;
}

export interface ApiResponse<T> {
  status: number;
  body: T;
  headers: Headers;
}

export interface ApiRequestOptions {
  method?: 'GET' | 'POST';
  body?: unknown;
  headers?: Record<string, string>;
  signal?: AbortSignal;
}

async function readProblem(response: Response): Promise<StorefrontProblemDto | undefined> {
  const type = response.headers.get('content-type') ?? '';
  if (!/json/i.test(type)) return undefined;
  try {
    return (await response.json()) as StorefrontProblemDto;
  } catch {
    return undefined;
  }
}

/** Cliente mínimo sobre `fetch` para `/storefront/v1`. */
export class StorefrontApi {
  readonly baseUrl: string;

  constructor(baseUrl: string) {
    this.baseUrl = resolveApiUrl(baseUrl);
  }

  /** URL absoluta de una ruta de la tienda (`/catalog` → `http://…/storefront/v1/catalog`). */
  url(path: string): string {
    return `${this.baseUrl}${STOREFRONT_PREFIX}${path.startsWith('/') ? path : `/${path}`}`;
  }

  /** Petición JSON; lanza StorefrontError si no hubo respuesta o si el código no es 2xx. */
  async request<T>(path: string, options: ApiRequestOptions = {}): Promise<ApiResponse<T>> {
    const headers: Record<string, string> = { Accept: 'application/json', ...options.headers };
    if (options.body !== undefined) headers['Content-Type'] = 'application/json';
    let response: Response;
    try {
      response = await fetch(this.url(path), {
        method: options.method ?? 'GET',
        headers,
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
        signal: options.signal,
        mode: 'cors',
        credentials: 'omit',
      });
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') throw error;
      throw new StorefrontError({ kind: 'network', status: 0, detail: 'No hubo respuesta de la tienda.' });
    }
    if (!response.ok) throw toStorefrontError(response.status, await readProblem(response));
    const body = response.status === 204 ? (undefined as T) : ((await response.json()) as T);
    return { status: response.status, body, headers: response.headers };
  }

  get<T>(path: string, options: Omit<ApiRequestOptions, 'method' | 'body'> = {}): Promise<ApiResponse<T>> {
    return this.request<T>(path, { ...options, method: 'GET' });
  }

  post<T>(path: string, body: unknown, options: Omit<ApiRequestOptions, 'method' | 'body'> = {}): Promise<ApiResponse<T>> {
    return this.request<T>(path, { ...options, method: 'POST', body });
  }
}
