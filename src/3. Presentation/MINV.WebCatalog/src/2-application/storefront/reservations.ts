// Casos de uso de las reservas (V6): reservar el armado, consultar y liberar una reserva, sobre el puerto
// IReservationGateway. Sin React ni red. Los errores llegan como StorefrontError (los deja pasar).

import type { BuildLine } from '@/1-domain/builder/types';
import type { IReservationGateway } from '@/1-domain/ports/IReservationGateway';
import { StorefrontError } from '@/1-domain/storefront/errors';
import { RESERVATION_LIMITS, type Reservation, type ReservationContact } from '@/1-domain/storefront/types';
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

export interface ReservationUseCases {
  /** Crea la reserva del armado en la tienda (todo o nada). */
  reserve(input: ReserveBuildInput): Promise<Reservation>;
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

export function createReservationUseCases(gateway: IReservationGateway): ReservationUseCases {
  return {
    async reserve(input) {
      const problem = validateReservationLines(input.lines);
      if (problem) throw new StorefrontError({ kind: 'validation', status: 400, detail: problem, errors: [problem] });
      return gateway.create({
        lines: toReservationLines(input.lines),
        contact: input.contact,
        notes: input.notes,
        name: input.name,
        idempotencyKey: input.idempotencyKey,
      });
    },
    lookup: (number, phone) => gateway.get(number.trim().toUpperCase(), phone.trim()),
    release: (number, phone) => gateway.cancel(number.trim().toUpperCase(), phone.trim()),
  };
}
