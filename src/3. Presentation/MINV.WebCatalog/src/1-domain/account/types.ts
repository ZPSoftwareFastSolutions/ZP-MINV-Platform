// Cuenta del cliente (V7): los datos de la persona que ingresó. Los casos de uso del servidor operan SIEMPRE sobre el
// cliente ligado a la sesión: la web nunca envía un identificador de cliente ni de usuario (regla P-04).

import type { DocumentTypeCode } from './documents';

export interface CustomerAccount {
  name: string;
  /** Correo con el que ingresa (no se cambia desde la web). */
  email: string;
  phone: string;
  /** Documento para la factura (opcional): tipo del SIN, número y complemento (solo con CI). */
  documentType: DocumentTypeCode | null;
  documentNumber: string | null;
  complement: string | null;
}

/** Lo que el cliente puede cambiar de su cuenta. */
export interface AccountUpdate {
  name: string;
  phone: string;
  documentType: DocumentTypeCode | null;
  documentNumber: string | null;
  complement: string | null;
}

export interface PasswordChange {
  currentPassword: string;
  newPassword: string;
}
