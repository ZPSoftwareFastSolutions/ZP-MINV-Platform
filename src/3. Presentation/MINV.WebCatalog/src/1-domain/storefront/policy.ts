// Plazos y tipos de una reserva (V7), reglas puras: cuántos días puede pedir quien reserva según lo que publica el
// catálogo, cuántas horas se guardó de verdad una reserva y de qué tipo es por su número (ARM-… armado · RES-… carrito).

import {
  DEFAULT_RESERVATION_POLICY,
  HOLD_DAYS_LIMIT,
  type Reservation,
  type ReservationKind,
  type ReservationPolicy,
} from './types';

const HOUR_MS = 3_600_000;

/** Horas de un día de reserva (la tienda cuenta 24 h por día pedido). */
export const HOURS_PER_HOLD_DAY = 24;

/** Número de una reserva web: `ARM-WEB-000001` (armado) o `RES-WEB-000001` (carrito). */
export const RESERVATION_NUMBER_PATTERN = /^(ARM|RES)-[A-Z0-9]+-\d{1,9}$/i;

function positiveInteger(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) && value >= 1 ? Math.trunc(value) : null;
}

/**
 * Plazos que publica el servidor, leídos con tolerancia: sin valor (o con uno inválido) valen 48 h y 3 días; los días se
 * acotan de 1 a 3 (el tope del contrato).
 */
export function normalizeReservationPolicy(raw: { reservationHours?: unknown; maxHoldDays?: unknown } | null | undefined): ReservationPolicy {
  const hours = positiveInteger(raw?.reservationHours) ?? DEFAULT_RESERVATION_POLICY.reservationHours;
  const days = positiveInteger(raw?.maxHoldDays) ?? DEFAULT_RESERVATION_POLICY.maxHoldDays;
  return { reservationHours: hours, maxHoldDays: Math.min(HOLD_DAYS_LIMIT, days) };
}

/** Horas que se guarda una reserva de `days` días. */
export function hoursForHoldDays(days: number): number {
  return days * HOURS_PER_HOLD_DAY;
}

/** Días que se pueden pedir con esta configuración: [1, 2, 3] acotado por `maxHoldDays`. */
export function holdDayChoices(policy: ReservationPolicy): number[] {
  const max = Math.max(1, Math.min(HOLD_DAYS_LIMIT, Math.trunc(policy.maxHoldDays)));
  return Array.from({ length: max }, (_, index) => index + 1);
}

/**
 * Días que se ofrecen elegidos al abrir el formulario: los que equivalen a las horas configuradas (48 h → 2 días) o, si
 * no coinciden, el máximo permitido.
 */
export function defaultHoldDays(policy: ReservationPolicy): number {
  const choices = holdDayChoices(policy);
  const matching = choices.find((days) => hoursForHoldDays(days) === policy.reservationHours);
  return matching ?? choices[choices.length - 1];
}

/** ¿Son unos días válidos para esta configuración? */
export function isHoldDays(value: number, policy: ReservationPolicy): boolean {
  return Number.isInteger(value) && holdDayChoices(policy).includes(value);
}

/** Horas que la tienda guarda (o guardó) la reserva: de su creación a su vencimiento, redondeadas. 0 si no se sabe. */
export function reservationHeldHours(reservation: Pick<Reservation, 'createdAt' | 'reservedUntil'>): number {
  const from = reservation.createdAt.getTime();
  const to = reservation.reservedUntil.getTime();
  if (Number.isNaN(from) || Number.isNaN(to) || to <= from) return 0;
  return Math.round((to - from) / HOUR_MS);
}

/** Tipo de una reserva por su número (`RES-…` carrito; cualquier otro, armado). */
export function reservationKindOf(number: string): ReservationKind {
  return /^RES-/i.test(number.trim()) ? 'cart' : 'build';
}

/** Lee el tipo que manda el servidor (`build` / `cart`, sin distinguir mayúsculas); si falta, lo deduce del número. */
export function parseReservationKind(value: unknown, number: string): ReservationKind {
  if (typeof value === 'string') {
    const kind = value.trim().toLowerCase();
    if (kind === 'cart' || kind === 'build') return kind;
  }
  return reservationKindOf(number);
}

export function isReservationNumber(value: string): boolean {
  return RESERVATION_NUMBER_PATTERN.test(value.trim());
}
