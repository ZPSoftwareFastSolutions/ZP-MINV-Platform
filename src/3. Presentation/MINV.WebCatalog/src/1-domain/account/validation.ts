// Reglas puras del formulario «Mis datos»: nombre, teléfono boliviano y documento opcional para la factura.

import { personNameError, phoneError } from '@/1-domain/auth/validation';
import { formatBolivianPhone } from '@/1-domain/storefront/contact';
import { parseDocumentType, validateBuyerDocument, type BuyerDocumentInput } from './documents';
import type { AccountUpdate, CustomerAccount } from './types';

export interface AccountFormInput extends BuyerDocumentInput {
  name: string;
  phone: string;
}

export type AccountFormField = keyof AccountFormInput;
export type AccountFormErrors = Partial<Record<AccountFormField, string>>;

/** Formulario a partir de la cuenta que devolvió el servidor. */
export function toAccountForm(account: CustomerAccount): AccountFormInput {
  return {
    name: account.name,
    phone: account.phone,
    documentType: account.documentType === null ? '' : String(account.documentType),
    documentNumber: account.documentNumber ?? '',
    complement: account.complement ?? '',
  };
}

export function validateAccountForm(input: AccountFormInput): AccountFormErrors {
  const errors: AccountFormErrors = { ...validateBuyerDocument(input) };
  const name = personNameError(input.name);
  if (name) errors.name = name;
  const phone = phoneError(input.phone);
  if (phone) errors.phone = phone;
  return errors;
}

/** Cambios listos para enviar (recortados; sin tipo de documento no viaja ni número ni complemento). */
export function toAccountUpdate(input: AccountFormInput): AccountUpdate {
  const documentType = parseDocumentType(input.documentType);
  const complement = input.complement.trim().toUpperCase();
  return {
    name: input.name.trim(),
    phone: formatBolivianPhone(input.phone),
    documentType,
    documentNumber: documentType === null ? null : input.documentNumber.trim(),
    complement: documentType === 1 && complement.length > 0 ? complement : null,
  };
}
