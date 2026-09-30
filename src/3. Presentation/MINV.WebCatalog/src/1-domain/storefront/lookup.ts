// Reglas puras de «Consultar mi reserva» (V7): se busca con el código de la reserva O con el número de celular (al menos
// uno; los dos también valen). Con el código solo, el servidor devuelve esa reserva con el contacto enmascarado; con el
// celular solo, la lista de las reservas hechas con ese celular. Cancelar SIEMPRE exige el código y el celular (regla S-06).

import { isBolivianPhone } from './contact';
import { isReservationNumber } from './policy';

export interface ReservationLookupInput {
  number: string;
  phone: string;
}

export interface ReservationLookupErrors {
  /** Ninguno de los dos campos tiene datos. */
  form?: string;
  number?: string;
  phone?: string;
}

/** Cómo se va a consultar: por el código (con o sin celular) o solo por el celular. */
export type ReservationLookupMode = 'code' | 'phone';

export const LOOKUP_HINT = 'Complete al menos uno';
export const LOOKUP_EMPTY = 'Completá el código de reserva o el número de celular (al menos uno).';
export const LOOKUP_NUMBER_FORMAT = 'El código tiene la forma RES-WEB-000001 (compras) o ARM-WEB-000001 (armados).';
export const LOOKUP_PHONE_FORMAT = 'El celular debe tener 7 u 8 dígitos (Bolivia), con o sin +591.';

/** Valida el formulario: al menos uno de los dos; el que venga, con su forma. Vacío = sin errores. */
export function validateReservationLookup(input: ReservationLookupInput): ReservationLookupErrors {
  const number = input.number.trim();
  const phone = input.phone.trim();
  if (!number && !phone) return { form: LOOKUP_EMPTY };
  const errors: ReservationLookupErrors = {};
  if (number && !isReservationNumber(number)) errors.number = LOOKUP_NUMBER_FORMAT;
  if (phone && !isBolivianPhone(phone)) errors.phone = LOOKUP_PHONE_FORMAT;
  return errors;
}

/** Con código se consulta esa reserva (el celular, si viene, la muestra completa); sin código, la lista del celular. */
export function reservationLookupMode(input: ReservationLookupInput): ReservationLookupMode {
  return input.number.trim() ? 'code' : 'phone';
}
