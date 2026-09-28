// Reglas puras del contacto de una reserva: teléfono boliviano, nombre, correo opcional y notas. Son las mismas que
// aplica la API (`pcbuild.contact_phone`, `validation`); validarlas antes evita un viaje al servidor.

import type { ReservationContact } from './types';
import { RESERVATION_LIMITS } from './types';

/** Prefijo internacional de Bolivia. */
export const BOLIVIA_COUNTRY_CODE = '591';

/**
 * Normaliza un teléfono de Bolivia a sus dígitos locales (7 u 8): acepta `+591 71234567`, `591-7123-4567`, `7123 4567`.
 * Devuelve null si no es un teléfono boliviano válido.
 */
export function normalizeBolivianPhone(input: string): string | null {
  const digits = input.replace(/\D/g, '');
  if (digits.length === 0) return null;
  const local =
    digits.length >= 10 && digits.startsWith(BOLIVIA_COUNTRY_CODE) ? digits.slice(BOLIVIA_COUNTRY_CODE.length) : digits;
  return local.length === 7 || local.length === 8 ? local : null;
}

export function isBolivianPhone(input: string): boolean {
  return normalizeBolivianPhone(input) !== null;
}

/** «+591 71234567» a partir de cualquier forma válida; el texto original si no se reconoce. */
export function formatBolivianPhone(input: string): string {
  const local = normalizeBolivianPhone(input);
  return local ? `+${BOLIVIA_COUNTRY_CODE} ${local}` : input.trim();
}

/** Validación mínima y local de un correo (la API vuelve a validarlo). */
export function isValidEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(value.trim());
}

export interface ReservationFormInput {
  name: string;
  phone: string;
  email: string;
  notes: string;
}

export type ReservationFormField = keyof ReservationFormInput;

export type ReservationFormErrors = Partial<Record<ReservationFormField, string>>;

/** Errores por campo (vacío si todo está bien), con los textos que se muestran junto a cada campo. */
export function validateReservationForm(input: ReservationFormInput): ReservationFormErrors {
  const errors: ReservationFormErrors = {};
  const name = input.name.trim();
  if (name.length === 0) errors.name = 'Indicá tu nombre para guardar la reserva.';
  else if (name.length > RESERVATION_LIMITS.nameMaxLength) errors.name = `El nombre no puede superar ${RESERVATION_LIMITS.nameMaxLength} caracteres.`;

  const phone = input.phone.trim();
  if (phone.length === 0) errors.phone = 'Indicá un teléfono o WhatsApp: con él consultás y liberás tu reserva.';
  else if (!isBolivianPhone(phone)) errors.phone = 'El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.';

  const email = input.email.trim();
  if (email.length > 0 && !isValidEmail(email)) errors.email = 'Revisá el correo: debe tener la forma nombre@dominio.';

  if (input.notes.length > RESERVATION_LIMITS.notesMaxLength) errors.notes = `Las notas no pueden superar ${RESERVATION_LIMITS.notesMaxLength} caracteres.`;
  return errors;
}

/** Contacto listo para enviar (recortado; el correo solo si se escribió). Exige un formulario ya validado. */
export function toReservationContact(input: Pick<ReservationFormInput, 'name' | 'phone' | 'email'>): ReservationContact {
  const email = input.email.trim();
  return { name: input.name.trim(), phone: formatBolivianPhone(input.phone), ...(email ? { email } : {}) };
}
