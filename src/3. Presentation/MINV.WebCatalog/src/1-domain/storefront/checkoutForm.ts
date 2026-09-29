// Reglas puras del formulario de reserva (`/reservar`, V7): datos de quien reserva (nombre, teléfono o WhatsApp y correo),
// cuándo pasa a recogerlo (1 a `maxHoldDays` días), datos opcionales para la factura (tipo y número de documento,
// complemento y nombre o razón social) y notas. Son las mismas reglas que aplica el servidor; validarlas antes permite
// marcar el campo exacto al salir de él y al enviar.
//
// Con una cuenta de cliente (`mode: 'account'`) los datos de contacto salen de la cuenta: solo se validan los días y las
// notas.

import { documentType, parseDocumentType, validateBuyerDocument } from '@/1-domain/account/documents';
import { hasControlChars, personNameError, phoneError } from '@/1-domain/auth/validation';
import { formatBolivianPhone, isValidEmail, toSingleLine } from './contact';
import { defaultHoldDays, isHoldDays } from './policy';
import { RESERVATION_LIMITS, type ReservationBuyer, type ReservationContact, type ReservationPolicy } from './types';

export interface CheckoutFormInput {
  name: string;
  phone: string;
  email: string;
  /** Días para recogerla, como texto de la lista desplegable («1», «2», «3»). */
  holdDays: string;
  /** Código del tipo de documento como texto de la lista («» = sin datos para la factura). */
  documentType: string;
  documentNumber: string;
  complement: string;
  /** Nombre o razón social para la factura. */
  buyerName: string;
  notes: string;
}

export type CheckoutField = keyof CheckoutFormInput;
export type CheckoutFormErrors = Partial<Record<CheckoutField, string>>;

/** `guest`: sin sesión (pide todos los datos) · `account`: cliente con sesión (los datos salen de su cuenta). */
export type CheckoutMode = 'guest' | 'account';

export interface CheckoutRules {
  mode: CheckoutMode;
  policy: ReservationPolicy;
}

/** Campos en el orden en que aparecen (el foco va al primero con error). */
export const CHECKOUT_FIELDS: readonly CheckoutField[] = ['name', 'phone', 'email', 'holdDays', 'documentType', 'documentNumber', 'complement', 'buyerName', 'notes'];

/** Campos de los datos para la factura (la sección plegable). */
export const BUYER_FIELDS: readonly CheckoutField[] = ['documentType', 'documentNumber', 'complement', 'buyerName'];

const ACCOUNT_FIELDS: readonly CheckoutField[] = ['holdDays', 'notes'];

/** Campos que se piden según haya o no una cuenta de cliente. */
export function checkoutFields(mode: CheckoutMode): readonly CheckoutField[] {
  return mode === 'account' ? ACCOUNT_FIELDS : CHECKOUT_FIELDS;
}

export function emptyCheckoutForm(policy: ReservationPolicy): CheckoutFormInput {
  return { name: '', phone: '', email: '', holdDays: String(defaultHoldDays(policy)), documentType: '', documentNumber: '', complement: '', buyerName: '', notes: '' };
}

const EMAIL_INVALID = 'Revisá el correo: debe tener la forma nombre@dominio.';

function emailError(value: string): string | undefined {
  const email = value.trim();
  if (email.length === 0) return undefined;
  if (email.length > RESERVATION_LIMITS.emailMaxLength || hasControlChars(email) || !isValidEmail(email)) return EMAIL_INVALID;
  return undefined;
}

function holdDaysError(value: string, policy: ReservationPolicy): string | undefined {
  const days = Number.parseInt(value, 10);
  return isHoldDays(days, policy) ? undefined : 'Elegí cuándo pasás a recogerlo.';
}

function buyerNameError(input: CheckoutFormInput): string | undefined {
  const raw = input.buyerName;
  if (raw.trim().length === 0) return undefined;
  if (hasControlChars(raw)) return 'El nombre o razón social va en una sola línea, sin tabuladores ni caracteres especiales.';
  if (raw.trim().length > RESERVATION_LIMITS.buyerNameMaxLength) return `El nombre o razón social tiene como máximo ${RESERVATION_LIMITS.buyerNameMaxLength} caracteres.`;
  if (!documentType(parseDocumentType(input.documentType))) return 'Para poner un nombre en la factura, elegí también el tipo y el número de documento.';
  return undefined;
}

function notesError(value: string): string | undefined {
  const notes = toSingleLine(value);
  if (notes.length > RESERVATION_LIMITS.notesMaxLength) return `Las notas pueden tener hasta ${RESERVATION_LIMITS.notesMaxLength} caracteres.`;
  return undefined;
}

/** Error de UN campo (para validar al salir de él); undefined si está bien o si ese modo no lo pide. */
export function checkoutFieldError(field: CheckoutField, input: CheckoutFormInput, rules: CheckoutRules): string | undefined {
  if (!checkoutFields(rules.mode).includes(field)) return undefined;
  switch (field) {
    case 'name':
      return personNameError(input.name, 'store');
    case 'phone':
      return phoneError(input.phone, 'store');
    case 'email':
      return emailError(input.email);
    case 'holdDays':
      return holdDaysError(input.holdDays, rules.policy);
    case 'documentType':
    case 'documentNumber':
    case 'complement':
      return validateBuyerDocument(input)[field];
    case 'buyerName':
      return buyerNameError(input);
    case 'notes':
      return notesError(input.notes);
    default:
      return undefined;
  }
}

/** Errores de todos los campos que pide el modo (vacío si se puede enviar). */
export function validateCheckoutForm(input: CheckoutFormInput, rules: CheckoutRules): CheckoutFormErrors {
  const errors: CheckoutFormErrors = {};
  for (const field of checkoutFields(rules.mode)) {
    const message = checkoutFieldError(field, input, rules);
    if (message) errors[field] = message;
  }
  return errors;
}

/** Primer campo con error en el orden del formulario (para llevarle el foco). */
export function firstInvalidField(errors: CheckoutFormErrors): CheckoutField | undefined {
  return CHECKOUT_FIELDS.find((field) => Boolean(errors[field]));
}

/** Contacto listo para enviar (recortado, en una línea; el correo solo si se escribió). */
export function toCheckoutContact(input: Pick<CheckoutFormInput, 'name' | 'phone' | 'email'>): ReservationContact {
  const email = input.email.trim();
  return { name: toSingleLine(input.name), phone: formatBolivianPhone(input.phone), ...(email ? { email } : {}) };
}

/** Días elegidos (ya validados). */
export function toHoldDays(input: Pick<CheckoutFormInput, 'holdDays'>): number {
  return Number.parseInt(input.holdDays, 10);
}

/** Notas en UNA línea (el servidor rechaza los saltos de línea); undefined si quedan vacías. */
export function toCheckoutNotes(input: Pick<CheckoutFormInput, 'notes'>): string | undefined {
  return toSingleLine(input.notes) || undefined;
}

/** Datos para la factura listos para enviar, o undefined si no eligió tipo de documento. */
export function toReservationBuyer(input: Pick<CheckoutFormInput, 'documentType' | 'documentNumber' | 'complement' | 'buyerName'>): ReservationBuyer | undefined {
  const type = parseDocumentType(input.documentType);
  if (type === null) return undefined;
  const complement = input.complement.trim().toUpperCase();
  const name = toSingleLine(input.buyerName);
  return {
    documentType: type,
    documentNumber: input.documentNumber.trim(),
    ...(type === 1 && complement ? { complement } : {}),
    ...(name ? { name } : {}),
  };
}
