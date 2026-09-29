// Cliente HTTP de la SESIÓN WEB y del RPC (`/api/v1/web/*`, servidor en la nube). Junto con `api.ts` (tienda pública) es
// el único archivo de la web que usa `fetch` (reglas S-07 y P-08; lo vigila src/architecture.test.ts).
//
// SEGURIDAD (regla P-02):
// - Siempre al MISMO origen (rutas relativas, `mode: 'same-origin'`): la cookie de la sesión nunca viaja a otro sitio.
// - `credentials: 'same-origin'`: el navegador adjunta la cookie HttpOnly; el JavaScript no la ve ni la guarda.
// - Cabecera propia `X-MINV-Client-Version` en TODA petición: el servidor la exige como defensa contra CSRF.
// - Nada se guarda en el navegador y ninguna respuesta se deja en caché (`cache: 'no-store'`).
//
// Traduce cada falla a un WebApiError del dominio: cuerpo `{ ok: false, error }` del servidor, `problem+json`, 429 sin
// cuerpo y red caída.

import { WebApiError, type WebApiErrorKind } from '@/1-domain/auth/errors';

/** Prefijo de todas las rutas de la sesión web (el nginx del catálogo lo reenvía al servidor en la nube). */
export const WEB_API_PREFIX = '/api/v1/web';
export const CLIENT_VERSION_HEADER = 'X-MINV-Client-Version';
/** Versión de la web: el servidor exige la misma versión mayor. */
export const CLIENT_VERSION = '7.0.0';

export interface WebApiRequestOptions {
  method?: 'GET' | 'POST';
  body?: unknown;
  signal?: AbortSignal;
  /** Identificador del pedido RPC: queda en el error para reintentar con el mismo id. */
  requestId?: string;
}

export interface WebApiResponse {
  status: number;
  /** Cuerpo JSON ya leído (undefined si la respuesta no trae cuerpo). */
  body: unknown;
}

const KNOWN_KINDS: readonly WebApiErrorKind[] = [
  'validation',
  'authentication',
  'access_denied',
  'not_found',
  'concurrency',
  'domain',
  'idempotency',
  'unsupported',
  'rate_limited',
  'server',
];

const DEFAULT_MESSAGE: Record<WebApiErrorKind, string> = {
  validation: 'Los datos enviados no son válidos.',
  authentication: 'La sesión venció o se cerró: vuelva a iniciar sesión.',
  access_denied: 'Su cuenta no tiene permiso para realizar esta operación.',
  not_found: 'No se encontró lo solicitado.',
  concurrency: 'Otra persona cambió estos datos al mismo tiempo. Actualice la pantalla y vuelva a intentar.',
  domain: 'El servidor no pudo procesar la solicitud.',
  idempotency: 'Ese pedido ya se envió con otro contenido. Vuelva a intentar.',
  unsupported: 'Esta versión de la página ya no es compatible con el servidor. Actualice la página.',
  rate_limited: 'Se superó el límite de solicitudes. Espere un minuto y vuelva a intentar.',
  server: 'El servidor no pudo completar la operación. Intente de nuevo; si persiste, avise a soporte.',
  network: 'No hubo respuesta del servidor. Revise la conexión e intente de nuevo.',
  unknown: 'Ocurrió un error inesperado.',
};

function kindForStatus(status: number): WebApiErrorKind {
  switch (status) {
    case 400:
      return 'validation';
    case 401:
      return 'authentication';
    case 403:
      return 'access_denied';
    case 404:
      return 'not_found';
    case 409:
      return 'concurrency';
    case 422:
      return 'domain';
    case 429:
      return 'rate_limited';
    default:
      return status >= 500 ? 'server' : 'unknown';
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null;
}

/** Lista de mensajes: acepta `string[]` (contrato del RPC) y `{ campo: string[] }` (validación de ASP.NET). */
function messages(value: unknown): string[] {
  if (Array.isArray(value)) return value.filter((item): item is string => typeof item === 'string' && item.trim().length > 0);
  if (isRecord(value)) return Object.values(value).flatMap(messages);
  return [];
}

/** Segundos de la cabecera `Retry-After` (solo la forma numérica). */
function retryAfterSeconds(headers: Headers | undefined): number | null {
  const raw = headers?.get('Retry-After');
  if (!raw || !/^\d+$/.test(raw.trim())) return null;
  return Number.parseInt(raw, 10);
}

/**
 * Respuesta de error (código HTTP + cuerpo si lo hubo) → WebApiError. Entiende el sobre del servidor
 * `{ ok: false, error: { kind, message, errors?, code? } }` y también `problem+json` (`title`, `detail`, `errors`).
 */
export function toWebApiError(status: number, body?: unknown, extra: { requestId?: string; headers?: Headers } = {}): WebApiError {
  const envelope = isRecord(body) && isRecord(body.error) ? body.error : isRecord(body) ? body : {};
  const declared = text(envelope.kind);
  // 429 manda sobre lo que diga el cuerpo; un `kind` desconocido se deduce del código HTTP.
  const kind = status === 429 ? 'rate_limited' : declared && (KNOWN_KINDS as readonly string[]).includes(declared) ? (declared as WebApiErrorKind) : kindForStatus(status);
  const errors = messages(envelope.errors);
  const retryAfter = kind === 'rate_limited' ? retryAfterSeconds(extra.headers) : null;
  const own = retryAfter !== null ? `Se superó el límite de solicitudes. Espere ${retryAfter} segundos y vuelva a intentar.` : DEFAULT_MESSAGE[kind];
  // En una falla técnica no se muestra el detalle del servidor (puede traer datos internos): siempre el texto propio.
  const message = kind === 'server' ? own : (text(envelope.message) ?? text(envelope.detail) ?? errors[0] ?? own);
  return new WebApiError({ kind, status, message, errors, code: text(envelope.code), requestId: extra.requestId ?? null });
}

async function readBody(response: Response): Promise<unknown> {
  if (response.status === 204) return undefined;
  let raw: string;
  try {
    raw = await response.text();
  } catch {
    return undefined;
  }
  if (raw.trim().length === 0) return undefined;
  try {
    return JSON.parse(raw) as unknown;
  } catch {
    // Una página HTML de un intermediario (502 del nginx, por ejemplo): no hay cuerpo que interpretar.
    return undefined;
  }
}

/** Cliente mínimo sobre `fetch` para `/api/v1/web`. */
export class WebApi {
  /** Ruta de la sesión web (`/rpc` → `/api/v1/web/rpc`). Siempre relativa: el mismo origen de la página. */
  url(path: string): string {
    return `${WEB_API_PREFIX}${path.startsWith('/') ? path : `/${path}`}`;
  }

  /** Petición JSON; lanza WebApiError si no hubo respuesta, si el código no es 2xx o si el sobre dice `ok: false`. */
  async request(path: string, options: WebApiRequestOptions = {}): Promise<WebApiResponse> {
    const headers: Record<string, string> = { Accept: 'application/json', [CLIENT_VERSION_HEADER]: CLIENT_VERSION };
    if (options.body !== undefined) headers['Content-Type'] = 'application/json';
    let response: Response;
    try {
      response = await fetch(this.url(path), {
        method: options.method ?? 'GET',
        headers,
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
        signal: options.signal,
        mode: 'same-origin',
        credentials: 'same-origin',
        cache: 'no-store',
      });
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') throw error;
      throw new WebApiError({ kind: 'network', status: 0, message: DEFAULT_MESSAGE.network, requestId: options.requestId ?? null });
    }
    const body = await readBody(response);
    if (!response.ok) throw toWebApiError(response.status, body, { requestId: options.requestId, headers: response.headers });
    if (isRecord(body) && body.ok === false) throw toWebApiError(response.status, body, { requestId: options.requestId, headers: response.headers });
    return { status: response.status, body };
  }

  get(path: string, options: Omit<WebApiRequestOptions, 'method' | 'body'> = {}): Promise<WebApiResponse> {
    return this.request(path, { ...options, method: 'GET' });
  }

  post(path: string, body?: unknown, options: Omit<WebApiRequestOptions, 'method' | 'body'> = {}): Promise<WebApiResponse> {
    return this.request(path, { ...options, method: 'POST', body });
  }
}

/** Quita el sobre `{ ok: true, result }` si la respuesta lo trae; si no, el cuerpo tal cual. */
export function unwrapResult(body: unknown): { result: unknown; replayed: boolean } {
  if (isRecord(body) && typeof body.ok === 'boolean') return { result: body.result ?? null, replayed: body.replayed === true };
  return { result: body, replayed: false };
}
