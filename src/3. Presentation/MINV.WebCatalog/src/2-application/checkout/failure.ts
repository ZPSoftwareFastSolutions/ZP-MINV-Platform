// Por qué no se pudo reservar (V7), en UNA forma para las dos vías: la tienda pública (StorefrontError) y la cuenta del
// cliente por RPC (WebApiError). La página de reserva muestra el título y el mensaje, marca los campos que el servidor
// rechazó (cuando se puede ubicar el campo) y, si faltó stock, qué productos y cuánto hay.

import { isWebApiError, type WebApiError } from '@/1-domain/auth/errors';
import type { CheckoutField, CheckoutFormErrors } from '@/1-domain/storefront/checkoutForm';
import { isStorefrontError, type StorefrontError } from '@/1-domain/storefront/errors';
import type { StockShortage } from '@/1-domain/storefront/types';

/** Código del servidor cuando falta stock (409 en la tienda; 422 `domain` por el RPC). */
export const INSUFFICIENT_STOCK_CODE = 'storefront.insufficient_stock';

export type CheckoutFailureKind =
  /** Falta stock de uno o más productos: no se reservó nada. */
  | 'insufficient_stock'
  /** Datos rechazados (400/422 con campo o regla). */
  | 'invalid'
  /** Demasiadas solicitudes seguidas (429). */
  | 'rate_limited'
  /** Sin respuesta: el reintento viaja con la misma llave. */
  | 'network'
  /** La tienda o el servidor no están disponibles (503/500). */
  | 'unavailable'
  /** La sesión venció (401): hay que volver a ingresar. */
  | 'session'
  /** La misma llave con otro contenido (422 idempotencia): se estrena llave al reintentar. */
  | 'retry'
  | 'unknown';

export interface CheckoutFailure {
  kind: CheckoutFailureKind;
  title: string;
  message: string;
  /** Mensajes del servidor que se pudieron ubicar en un campo del formulario. */
  fieldErrors: CheckoutFormErrors;
  /** Mensajes del servidor que no corresponden a un campo. */
  messages: string[];
  /** Productos sin stock suficiente y cuánto hay (solo en `insufficient_stock`). */
  shortages: StockShortage[];
}

const CODE_FIELDS: Readonly<Record<string, CheckoutField>> = {
  'pcbuild.contact_name': 'name',
  'pcbuild.contact_phone': 'phone',
  'pcbuild.contact_email': 'email',
  'storefront.hold_days': 'holdDays',
  'buyer.doc_type': 'documentType',
  'pcbuild.buyer': 'documentType',
  'buyer.doc_number': 'documentNumber',
  'buyer.doc_numeric': 'documentNumber',
  'buyer.complement': 'complement',
};

/** Campo al que se refiere un mensaje del servidor (por su código o por el texto), o undefined si es general. */
export function fieldOfServerMessage(message: string, code?: string | null): CheckoutField | undefined {
  if (code && CODE_FIELDS[code]) return CODE_FIELDS[code];
  const text = message.toLowerCase();
  if (/razón social|razon social/.test(text)) return 'buyerName';
  if (/complemento/.test(text)) return 'complement';
  if (/tipo de documento/.test(text)) return 'documentType';
  if (/número de documento|numero de documento|\bci o nit\b/.test(text)) return 'documentNumber';
  if (/\bnotas?\b/.test(text)) return 'notes';
  if (/recoger|días para|dias para/.test(text)) return 'holdDays';
  if (/tel[eé]fono|whatsapp/.test(text)) return 'phone';
  if (/correo|e-?mail/.test(text)) return 'email';
  if (/nombre de contacto|nombre de quien|nombre/.test(text)) return 'name';
  return undefined;
}

/**
 * Faltantes a partir del mensaje del servidor («No hay stock suficiente para 2 pieza(s): MON-LG-27 (pedido 3, disponible
 * 1); SSD-X (pedido 2, disponible 0)»): el RPC los manda solo en el texto.
 */
export function parseShortages(message: string): StockShortage[] {
  const shortages: StockShortage[] = [];
  for (const match of message.matchAll(/([A-Z0-9][A-Z0-9_-]*)\s*\(pedido\s+(\d+),\s*disponible\s+(-?[\d.,]+)\)/gi)) {
    const available = Number.parseFloat(match[3].replace(',', '.'));
    shortages.push({ sku: match[1].toUpperCase(), name: match[1].toUpperCase(), requested: Number(match[2]), available: Number.isFinite(available) ? Math.max(0, Math.floor(available)) : 0 });
  }
  return shortages;
}

function placeMessages(messages: readonly string[], code?: string | null): { fieldErrors: CheckoutFormErrors; rest: string[] } {
  const fieldErrors: CheckoutFormErrors = {};
  const rest: string[] = [];
  for (const message of messages) {
    const field = fieldOfServerMessage(message, code);
    if (field && !fieldErrors[field]) fieldErrors[field] = message;
    else if (!field) rest.push(message);
  }
  return { fieldErrors, rest };
}

/** Mensaje general cuando todos los errores quedaron en sus campos. */
const FIX_FIELDS = 'Corregí los campos marcados y confirmá de nuevo.';

const TEXTS = {
  stock: { title: 'No alcanzó el stock para reservar todo', message: 'No se reservó nada. Te marcamos qué productos cambiaron y cuánto hay: ajustá a lo disponible y confirmá de nuevo.' },
  invalid: { title: 'Revisá los datos marcados', message: 'Algunos datos no son válidos.' },
  rate: { title: 'Hiciste demasiados intentos seguidos', message: 'Esperá un minuto y confirmá de nuevo. Tus datos siguen acá.' },
  network: {
    title: 'No pudimos conectarnos con la tienda',
    message: 'Revisá tu conexión y tocá «Confirmar reserva» otra vez: si la reserva ya se había registrado, no se va a duplicar.',
  },
  unavailable: { title: 'La tienda no está disponible en este momento', message: 'Intentá de nuevo en unos minutos. Tus datos siguen acá.' },
  session: { title: 'Tu sesión venció', message: 'Volvé a ingresar para reservar con tu cuenta.' },
  retry: { title: 'No pudimos confirmar tu reserva', message: 'Tocá «Confirmar reserva» otra vez.' },
  unknown: { title: 'No pudimos registrar la reserva', message: 'Ocurrió un error inesperado. Intentá de nuevo.' },
} as const;

function failure(kind: CheckoutFailureKind, texts: { title: string; message: string }, extra: Partial<CheckoutFailure> = {}): CheckoutFailure {
  return { kind, title: texts.title, message: texts.message, fieldErrors: {}, messages: [], shortages: [], ...extra };
}

/** Datos rechazados: ubica cada mensaje en su campo; lo que no se ubica queda como mensaje general. */
function invalid(messages: readonly string[], code: string | null, fallback: string): CheckoutFailure {
  const list = messages.length > 0 ? messages : [fallback];
  const { fieldErrors, rest } = placeMessages(list, code);
  const placed = Object.keys(fieldErrors).length > 0;
  if (!placed) return failure('invalid', { title: TEXTS.unknown.title, message: rest[0] ?? fallback }, { messages: rest.slice(1) });
  return failure('invalid', { title: TEXTS.invalid.title, message: rest[0] ?? FIX_FIELDS }, { fieldErrors, messages: rest.slice(1) });
}

function fromStorefront(error: StorefrontError): CheckoutFailure {
  switch (error.kind) {
    case 'insufficient_stock':
      return failure('insufficient_stock', TEXTS.stock, { shortages: error.shortages.length > 0 ? error.shortages : parseShortages(error.detail) });
    case 'validation':
      return invalid(error.errors, error.code, error.detail);
    case 'domain':
      return invalid([error.detail], error.code, error.detail);
    case 'rate_limited':
      return failure('rate_limited', TEXTS.rate);
    case 'network':
      return failure('network', TEXTS.network);
    case 'unavailable':
      return failure('unavailable', TEXTS.unavailable);
    case 'idempotency':
      return failure('retry', TEXTS.retry);
    default:
      return failure('unknown', { title: TEXTS.unknown.title, message: error.status >= 500 || !error.detail ? TEXTS.unknown.message : error.detail });
  }
}

function fromWebApi(error: WebApiError): CheckoutFailure {
  if (error.code === INSUFFICIENT_STOCK_CODE) return failure('insufficient_stock', TEXTS.stock, { shortages: parseShortages(error.message) });
  switch (error.kind) {
    case 'validation':
      return invalid(error.errors, error.code, error.message);
    case 'domain':
      return invalid([error.message], error.code, error.message);
    case 'rate_limited':
      return failure('rate_limited', TEXTS.rate);
    case 'network':
      return failure('network', TEXTS.network);
    case 'server':
      return failure('unavailable', TEXTS.unavailable);
    case 'authentication':
      return failure('session', TEXTS.session);
    case 'idempotency':
      return failure('retry', TEXTS.retry);
    case 'concurrency':
      return failure('retry', { title: TEXTS.retry.title, message: 'Otra persona reservó al mismo tiempo. Tocá «Confirmar reserva» otra vez.' });
    default:
      return failure('unknown', { title: TEXTS.unknown.title, message: error.message || TEXTS.unknown.message });
  }
}

/**
 * Deja en los campos solo los errores de los campos que la pantalla muestra (con una cuenta, por ejemplo, no hay campo
 * de nombre ni de teléfono): el resto pasa a los mensajes generales para que la persona igual los vea.
 */
export function restrictToFields(failure: CheckoutFailure, fields: readonly CheckoutField[]): CheckoutFailure {
  const fieldErrors: CheckoutFormErrors = {};
  const hidden: string[] = [];
  for (const [field, message] of Object.entries(failure.fieldErrors) as [CheckoutField, string | undefined][]) {
    if (!message) continue;
    if (fields.includes(field)) fieldErrors[field] = message;
    else hidden.push(message);
  }
  if (hidden.length === 0) return failure;
  if (Object.keys(fieldErrors).length === 0) {
    const general = failure.message === FIX_FIELDS ? [] : [failure.message];
    return { ...failure, title: TEXTS.unknown.title, message: hidden[0], fieldErrors, messages: [...hidden.slice(1), ...general, ...failure.messages] };
  }
  return { ...failure, fieldErrors, messages: [...failure.messages, ...hidden] };
}

/** Cualquier falla al reservar → CheckoutFailure (lo desconocido queda como `unknown`, sin detalles técnicos). */
export function toCheckoutFailure(error: unknown): CheckoutFailure {
  if (isStorefrontError(error)) return fromStorefront(error);
  if (isWebApiError(error)) return fromWebApi(error);
  return failure('unknown', TEXTS.unknown);
}
