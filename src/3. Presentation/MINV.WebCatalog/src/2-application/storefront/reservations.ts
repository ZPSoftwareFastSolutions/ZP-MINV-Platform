// Casos de uso de las reservas (V6): reservar el armado, consultar y liberar una reserva, sobre el puerto
// IReservationGateway. Sin React ni red. Los errores llegan como StorefrontError (los deja pasar).
// V7: reservar un CARRITO (o un artículo suelto) por la tienda pública, con los días para recogerlo y los datos
// opcionales para la factura.

import type { BuildLine } from '@/1-domain/builder/types';
import type { CartItem } from '@/1-domain/cart/types';
import type { IReservationGateway } from '@/1-domain/ports/IReservationGateway';
import { toSingleLine } from '@/1-domain/storefront/contact';
import { StorefrontError } from '@/1-domain/storefront/errors';
import { HOLD_DAYS_LIMIT, RESERVATION_LIMITS, type Reservation, type ReservationBuyer, type ReservationContact } from '@/1-domain/storefront/types';
import { toReservationLines } from './mappers';

export interface ReserveBuildInput {
  lines: readonly BuildLine[];
  contact: ReservationContact;
  notes?: string;
  /** Nombre del armado (opcional). */
  name?: string;
  /** UUID por intento (`newIdempotencyKey()`): repetir el envío con la misma llave devuelve la misma reserva. */
  idempotencyKey: string;
}

/** V7 · Reserva de un carrito o de un artículo suelto por la tienda pública (sin sesión). */
export interface ReserveCartInput {
  /** SKU y cantidad de cada producto (las líneas de un carrito van sin ranura). */
  items: readonly CartItem[];
  contact: ReservationContact;
  /** Días para pasar a recogerla (1 a 3). */
  holdDays?: number;
  /** Datos opcionales para la factura. */
  buyer?: ReservationBuyer;
  /** Se envían en UNA línea. */
  notes?: string;
  /** La misma llave si se reintenta por red caída; otra si cambió algo (ver 2-application/checkout). */
  idempotencyKey: string;
}

export interface ReservationUseCases {
  /** Crea la reserva del armado en la tienda (todo o nada). */
  reserve(input: ReserveBuildInput): Promise<Reservation>;
  /** V7: reserva un carrito (o un artículo suelto) en la tienda, todo o nada, con `kind = "cart"`. */
  reserveCart(input: ReserveCartInput): Promise<Reservation>;
  /** Estado de una reserva por número y teléfono. */
  lookup(number: string, phone: string): Promise<Reservation>;
  /** Libera una reserva activa (el stock vuelve). */
  release(number: string, phone: string): Promise<Reservation>;
}

/** UUID v4 para la cabecera `Idempotency-Key` (uno nuevo por intento de envío). */
export function newIdempotencyKey(): string {
  const cryptoApi = globalThis.crypto;
  if (cryptoApi && typeof cryptoApi.randomUUID === 'function') return cryptoApi.randomUUID();
  // Sin Web Crypto (entornos muy viejos): identificador aleatorio suficiente para no repetir una reserva.
  return `web-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;
}

/** Comprueba en la web los límites del contrato antes de viajar al servidor (mismo texto que devolvería la API). */
export function validateReservationLines(lines: readonly BuildLine[]): string | null {
  if (lines.length === 0) return 'Agregue al menos una pieza al armado.';
  if (lines.length > RESERVATION_LIMITS.maxLines) return `Una reserva admite como máximo ${RESERVATION_LIMITS.maxLines} piezas distintas.`;
  const excess = lines.find((line) => line.quantity < 1 || line.quantity > RESERVATION_LIMITS.maxQuantityPerLine);
  if (excess) return `La cantidad de ${excess.product.shortName} debe estar entre 1 y ${RESERVATION_LIMITS.maxQuantityPerLine}.`;
  return null;
}

/** Límites del contrato para las líneas de un carrito (mismo texto que la API). */
export function validateCartReservationItems(items: readonly Pick<CartItem, 'quantity'>[]): string | null {
  if (items.length === 0) return 'Agregá al menos un producto para reservar.';
  if (items.length > RESERVATION_LIMITS.maxLines) return `Una reserva admite como máximo ${RESERVATION_LIMITS.maxLines} productos distintos.`;
  if (items.some((item) => !Number.isInteger(item.quantity) || item.quantity < 1 || item.quantity > RESERVATION_LIMITS.maxQuantityPerLine)) {
    return `La cantidad de cada producto va de 1 a ${RESERVATION_LIMITS.maxQuantityPerLine}.`;
  }
  return null;
}

/** Días para recoger dentro del tope del contrato (1 a 3). */
export function validateHoldDays(days: number | undefined): string | null {
  if (days === undefined) return null;
  return Number.isInteger(days) && days >= 1 && days <= HOLD_DAYS_LIMIT ? null : `Los días para recoger la reserva van de 1 a ${HOLD_DAYS_LIMIT}.`;
}

function rejectInvalid(problem: string | null): void {
  if (problem) throw new StorefrontError({ kind: 'validation', status: 400, detail: problem, errors: [problem] });
}

export function createReservationUseCases(gateway: IReservationGateway): ReservationUseCases {
  return {
    async reserve(input) {
      rejectInvalid(validateReservationLines(input.lines));
      return gateway.create({
        lines: toReservationLines(input.lines),
        contact: input.contact,
        // El servidor rechaza los saltos de línea en medio de las notas: se unen con un espacio.
        notes: input.notes ? toSingleLine(input.notes) || undefined : undefined,
        name: input.name,
        idempotencyKey: input.idempotencyKey,
      });
    },
    async reserveCart(input) {
      rejectInvalid(validateCartReservationItems(input.items) ?? validateHoldDays(input.holdDays));
      return gateway.create({
        lines: input.items.map((item) => ({ sku: item.sku, quantity: item.quantity })),
        contact: input.contact,
        notes: input.notes ? toSingleLine(input.notes) || undefined : undefined,
        idempotencyKey: input.idempotencyKey,
        kind: 'cart',
        ...(input.holdDays !== undefined ? { holdDays: input.holdDays } : {}),
        ...(input.buyer ? { buyer: input.buyer } : {}),
      });
    },
    lookup: (number, phone) => gateway.get(number.trim().toUpperCase(), phone.trim()),
    release: (number, phone) => gateway.cancel(number.trim().toUpperCase(), phone.trim()),
  };
}
