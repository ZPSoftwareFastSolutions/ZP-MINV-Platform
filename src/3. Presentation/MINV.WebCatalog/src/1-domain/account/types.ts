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

/** Una línea de la reserva hecha con la cuenta (en un carrito, sin ranura). */
export interface AccountReservationLine {
  sku: string;
  quantity: number;
  slot?: string | null;
}

/**
 * Reserva con los datos de la cuenta (`CreateMyReservationCommand`): el nombre, el teléfono y el correo salen de la
 * cuenta del cliente de la sesión, y la reserva queda ligada a su cliente. No lleva datos para la factura: la caja
 * usa el documento de la cuenta al cobrar.
 */
export interface AccountReservationRequest {
  lines: AccountReservationLine[];
  /** `cart` (carrito o artículo suelto) o `build` (armado). */
  kind: 'build' | 'cart';
  /** Días para recogerla (1 a 3); sin valor, las horas configuradas en el servidor. */
  holdDays?: number;
  /** En UNA línea (el servidor rechaza los saltos de línea). */
  notes?: string;
  name?: string;
}

export interface PasswordChange {
  currentPassword: string;
  newPassword: string;
}
