// Errores de la sesión web y del RPC en términos del dominio: qué pasó (`kind`), qué decirle a la persona (`message`),
// la lista de validaciones (`errors`) y el código estable de la regla (`code`), sin HTTP. El adaptador
// (3-infrastructure/http/webApi.ts) traduce cada respuesta del servidor a uno de estos.

export type WebApiErrorKind =
  /** 400: los datos no pasaron la validación (`errors[]`). */
  | 'validation'
  /** 401: credenciales incorrectas, cuenta bloqueada o sesión vencida. */
  | 'authentication'
  /** 403: la sesión no tiene el permiso, el módulo o la sucursal. */
  | 'access_denied'
  /** 404. */
  | 'not_found'
  /** 409: otra persona cambió el dato; hay que recargar y repetir. */
  | 'concurrency'
  /** 422: regla del negocio (`code`: `account.email_taken`, `pcbuild.state`…). */
  | 'domain'
  /** 422: el mismo `requestId` con otro contenido. */
  | 'idempotency'
  /** 400: versión de la web distinta a la del servidor u operación desconocida. */
  | 'unsupported'
  /** 429: demasiadas solicitudes (puede llegar sin cuerpo). */
  | 'rate_limited'
  /** 500: falla técnica del servidor (sin detalles internos). */
  | 'server'
  /** No hubo respuesta: sin conexión o servidor caído. */
  | 'network'
  | 'unknown';

export interface WebApiErrorInit {
  kind: WebApiErrorKind;
  /** Texto en español para mostrar. */
  message: string;
  /** Código HTTP (0 cuando no hubo respuesta). */
  status?: number;
  errors?: readonly string[] | null;
  code?: string | null;
  /** Identificador del pedido RPC que falló: se reutiliza al reintentar por red caída (idempotencia). */
  requestId?: string | null;
}

export class WebApiError extends Error {
  readonly kind: WebApiErrorKind;
  readonly status: number;
  readonly errors: string[];
  readonly code: string | null;
  readonly requestId: string | null;

  constructor(init: WebApiErrorInit) {
    super(init.message);
    this.name = 'WebApiError';
    this.kind = init.kind;
    this.status = init.status ?? 0;
    this.errors = [...(init.errors ?? [])];
    this.code = init.code ?? null;
    this.requestId = init.requestId ?? null;
  }
}

export function isWebApiError(error: unknown): error is WebApiError {
  return error instanceof WebApiError;
}

/** Convierte cualquier fallo en un WebApiError (los desconocidos quedan como `unknown`, sin detalles técnicos). */
export function asWebApiError(error: unknown): WebApiError {
  if (isWebApiError(error)) return error;
  return new WebApiError({ kind: 'unknown', message: 'Ocurrió un error inesperado.' });
}

/** Código del dominio cuando el correo del registro ya tiene una cuenta. */
export const EMAIL_TAKEN_CODE = 'account.email_taken';

export function isEmailTaken(error: WebApiError): boolean {
  return error.code === EMAIL_TAKEN_CODE;
}

/**
 * ¿El servidor avisó que la cuenta está bloqueada por intentos fallidos? Llega como `authentication` con su propio
 * mensaje («Cuenta bloqueada por 5 intentos fallidos: espere 15 minutos.») o, si el servidor lo envía, con un código.
 */
export function isAccountLocked(error: WebApiError): boolean {
  if (error.kind !== 'authentication') return false;
  return /lock|bloque/i.test(error.code ?? '') || /bloquead/i.test(error.message);
}

/** La tienda trata de «vos»; el panel del personal usa español neutro. */
export type Voice = 'store' | 'panel';

const OWN_TEXT: Record<Voice, Partial<Record<WebApiErrorKind, string>>> = {
  store: {
    network: 'No pudimos conectarnos con el servidor. Revisá tu conexión e intentá de nuevo.',
    rate_limited: 'Hiciste demasiados intentos seguidos. Esperá un minuto e intentá de nuevo.',
    unknown: 'Ocurrió un error inesperado. Intentá de nuevo.',
  },
  panel: {
    network: 'No se pudo conectar con el servidor. Revise la conexión e intente de nuevo.',
    rate_limited: 'Se hicieron demasiadas solicitudes seguidas. Espere un minuto e intente de nuevo.',
    unknown: 'Ocurrió un error inesperado. Intente de nuevo.',
  },
};

/** Mensaje para mostrar: el del servidor tiene prioridad, salvo en las fallas que el servidor no pudo explicar. */
export function describeWebApiError(error: WebApiError, voice: Voice = 'store'): string {
  return OWN_TEXT[voice][error.kind] ?? (error.message || OWN_TEXT[voice].unknown!);
}
