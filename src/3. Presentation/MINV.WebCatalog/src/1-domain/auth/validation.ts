// Reglas puras de los formularios de acceso: ingresar, registrarse y cambiar la contraseña. Son las mismas que aplica
// el servidor (correo, contraseña de 8 a 128 caracteres con letras y números, teléfono boliviano, nombre sin caracteres
// de control); validarlas antes evita un viaje y permite marcar el campo exacto.

import { formatBolivianPhone, isBolivianPhone, isValidEmail } from '@/1-domain/storefront/contact';
import type { Voice } from './errors';
import type { Credentials, Registration } from './types';

export const PASSWORD_LIMITS = { min: 8, max: 128 } as const;
export const NAME_MAX_LENGTH = 120;
export const EMAIL_MAX_LENGTH = 254;
export const PHONE_MAX_LENGTH = 30;

const HAS_LETTER = /\p{L}/u;
const HAS_DIGIT = /\p{Nd}/u;
const CONTROL_CHARS = /\p{Cc}/u;

export function hasControlChars(text: string): boolean {
  return CONTROL_CHARS.test(text);
}

export type PasswordRuleKey = 'length' | 'letter' | 'digit';

export interface PasswordRuleState {
  key: PasswordRuleKey;
  /** Texto del requisito («Entre 8 y 128 caracteres»). */
  label: string;
  met: boolean;
}

/** Requisitos de la contraseña con su estado, para el indicador que se va marcando mientras la persona escribe. */
export function passwordRules(password: string): PasswordRuleState[] {
  return [
    { key: 'length', label: `Entre ${PASSWORD_LIMITS.min} y ${PASSWORD_LIMITS.max} caracteres`, met: password.length >= PASSWORD_LIMITS.min && password.length <= PASSWORD_LIMITS.max },
    { key: 'letter', label: 'Al menos una letra', met: HAS_LETTER.test(password) },
    { key: 'digit', label: 'Al menos un número', met: HAS_DIGIT.test(password) },
  ];
}

export function isStrongPassword(password: string): boolean {
  return passwordRules(password).every((rule) => rule.met);
}

interface Texts {
  emailRequired: string;
  emailInvalid: string;
  passwordRequired: string;
  nameRequired: string;
  phoneRequired: string;
  confirmRequired: string;
  currentRequired: string;
  newRequired: string;
}

const TEXTS: Record<Voice, Texts> = {
  store: {
    emailRequired: 'Indicá tu correo.',
    emailInvalid: 'Revisá el correo: debe tener la forma nombre@dominio.',
    passwordRequired: 'Indicá tu contraseña.',
    nameRequired: 'Indicá tu nombre y apellido.',
    phoneRequired: 'Indicá un teléfono o WhatsApp.',
    confirmRequired: 'Repetí la contraseña.',
    currentRequired: 'Indicá tu contraseña actual.',
    newRequired: 'Indicá la nueva contraseña.',
  },
  panel: {
    emailRequired: 'Indique su correo.',
    emailInvalid: 'Revise el correo: debe tener la forma nombre@dominio.',
    passwordRequired: 'Indique su contraseña.',
    nameRequired: 'Indique su nombre y apellido.',
    phoneRequired: 'Indique un teléfono.',
    confirmRequired: 'Repita la contraseña.',
    currentRequired: 'Indique su contraseña actual.',
    newRequired: 'Indique la nueva contraseña.',
  },
};

const PASSWORD_WEAK = `La contraseña debe tener entre ${PASSWORD_LIMITS.min} y ${PASSWORD_LIMITS.max} caracteres y combinar letras y números.`;
const PASSWORD_MISMATCH = 'Las contraseñas no coinciden.';
const PHONE_INVALID = 'El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.';

function emailError(value: string, texts: Texts): string | undefined {
  const email = value.trim();
  if (email.length === 0) return texts.emailRequired;
  if (email.length > EMAIL_MAX_LENGTH || !isValidEmail(email) || hasControlChars(email)) return texts.emailInvalid;
  return undefined;
}

/** Nombre de una persona: obligatorio, hasta 120 caracteres y sin caracteres de control (saltos de línea, tabulaciones…). */
export function personNameError(value: string, voice: Voice = 'store'): string | undefined {
  if (hasControlChars(value)) return 'El nombre no puede tener saltos de línea ni caracteres especiales de control.';
  const name = value.trim();
  if (name.length === 0) return TEXTS[voice].nameRequired;
  if (name.length > NAME_MAX_LENGTH) return `El nombre no puede superar ${NAME_MAX_LENGTH} caracteres.`;
  return undefined;
}

/** Teléfono boliviano: obligatorio, 7 u 8 dígitos con o sin +591. */
export function phoneError(value: string, voice: Voice = 'store'): string | undefined {
  const phone = value.trim();
  if (phone.length === 0) return TEXTS[voice].phoneRequired;
  if (phone.length > PHONE_MAX_LENGTH || !isBolivianPhone(phone)) return PHONE_INVALID;
  return undefined;
}

function withoutEmpty<T extends object>(errors: T): T {
  return Object.fromEntries(Object.entries(errors).filter(([, message]) => message !== undefined)) as T;
}

// ------------------------------------------------------------------------------------------------ ingresar
export interface LoginFormInput {
  email: string;
  password: string;
}

export type LoginFormErrors = Partial<Record<keyof LoginFormInput, string>>;

/** Al ingresar solo se exige que haya correo y contraseña: NO se revela si la contraseña cumple las reglas (P-03). */
export function validateLoginForm(input: LoginFormInput, voice: Voice = 'store'): LoginFormErrors {
  const texts = TEXTS[voice];
  return withoutEmpty<LoginFormErrors>({
    email: emailError(input.email, texts),
    password: input.password.length === 0 ? texts.passwordRequired : undefined,
  });
}

export function toCredentials(input: LoginFormInput): Credentials {
  return { email: input.email.trim(), password: input.password };
}

// ------------------------------------------------------------------------------------------------ registrarse
export interface RegisterFormInput {
  name: string;
  email: string;
  phone: string;
  password: string;
  confirm: string;
}

export type RegisterFormField = keyof RegisterFormInput;
export type RegisterFormErrors = Partial<Record<RegisterFormField, string>>;

export function validateRegisterForm(input: RegisterFormInput, voice: Voice = 'store'): RegisterFormErrors {
  const texts = TEXTS[voice];
  let password: string | undefined;
  if (input.password.length === 0) password = texts.passwordRequired;
  else if (!isStrongPassword(input.password)) password = PASSWORD_WEAK;

  let confirm: string | undefined;
  if (input.confirm.length === 0) confirm = texts.confirmRequired;
  else if (input.confirm !== input.password) confirm = PASSWORD_MISMATCH;

  return withoutEmpty<RegisterFormErrors>({
    name: personNameError(input.name, voice),
    email: emailError(input.email, texts),
    phone: phoneError(input.phone, voice),
    password,
    confirm,
  });
}

/** Datos listos para enviar (recortados, teléfono como «+591 71234567»). Exige un formulario ya validado. */
export function toRegistration(input: RegisterFormInput): Registration {
  return { name: input.name.trim(), email: input.email.trim(), phone: formatBolivianPhone(input.phone), password: input.password };
}

// ------------------------------------------------------------------------------------------------ cambiar contraseña
export interface ChangePasswordFormInput {
  current: string;
  next: string;
  confirm: string;
}

export type ChangePasswordFormErrors = Partial<Record<keyof ChangePasswordFormInput, string>>;

export function validateChangePasswordForm(input: ChangePasswordFormInput, voice: Voice = 'store'): ChangePasswordFormErrors {
  const texts = TEXTS[voice];
  let next: string | undefined;
  if (input.next.length === 0) next = texts.newRequired;
  else if (!isStrongPassword(input.next)) next = PASSWORD_WEAK;
  else if (input.next === input.current) next = 'La nueva contraseña debe ser distinta de la actual.';

  let confirm: string | undefined;
  if (input.confirm.length === 0) confirm = texts.confirmRequired;
  else if (input.confirm !== input.next) confirm = PASSWORD_MISMATCH;

  return withoutEmpty<ChangePasswordFormErrors>({
    current: input.current.length === 0 ? texts.currentRequired : undefined,
    next,
    confirm,
  });
}
