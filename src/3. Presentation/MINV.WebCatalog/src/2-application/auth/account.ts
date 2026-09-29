// Casos de uso de la cuenta del cliente (V7) sobre el puerto IAccountGateway: mis datos, mis reservas (con filtro por
// estado) y cambiar la contraseña. Sin React ni red. Revisan los datos antes de viajar y dejan pasar los errores del
// servidor como WebApiError.

import type { AccountUpdate, CustomerAccount, PasswordChange } from '@/1-domain/account/types';
import { toAccountForm, validateAccountForm } from '@/1-domain/account/validation';
import { WebApiError } from '@/1-domain/auth/errors';
import { validateChangePasswordForm } from '@/1-domain/auth/validation';
import type { CartItem } from '@/1-domain/cart/types';
import type { IAccountGateway } from '@/1-domain/ports/IAccountGateway';
import type { RpcSendOptions } from '@/1-domain/ports/IRpcGateway';
import { toSingleLine } from '@/1-domain/storefront/contact';
import type { Reservation, ReservationStatus } from '@/1-domain/storefront/types';
import { validateCartReservationItems, validateHoldDays } from '../storefront/reservations';

/** V7 · Reserva de un carrito (o un artículo suelto) con los datos de la cuenta. */
export interface AccountReserveInput {
  items: readonly CartItem[];
  /** Días para recogerla (1 a 3). */
  holdDays?: number;
  /** Se envían en UNA línea. */
  notes?: string;
}

export interface AccountUseCases {
  load(options?: RpcSendOptions): Promise<CustomerAccount>;
  save(update: AccountUpdate, options?: RpcSendOptions): Promise<CustomerAccount>;
  /** Reservas del cliente, de la más reciente a la más antigua. */
  reservations(options?: RpcSendOptions): Promise<Reservation[]>;
  /** Libera una reserva activa (el stock vuelve a estar disponible). */
  release(number: string, options?: RpcSendOptions): Promise<Reservation>;
  changePassword(change: PasswordChange, options?: RpcSendOptions): Promise<void>;
  /**
   * V7: reserva con los datos de la cuenta (`CreateMyReservationCommand`, `kind = "cart"`). El reintento por red caída
   * debe usar el MISMO `requestId`.
   */
  reserve(input: AccountReserveInput, options?: RpcSendOptions): Promise<Reservation>;
}

/** Valor del filtro «todas» en la lista de estados. */
export const ALL_STATUSES = 'all';
export type ReservationStatusFilter = ReservationStatus | typeof ALL_STATUSES;

/** Opciones de la lista desplegable de estados, en el orden en que importan al cliente. */
export const RESERVATION_STATUS_OPTIONS: readonly { value: ReservationStatusFilter; label: string }[] = [
  { value: ALL_STATUSES, label: 'Todas' },
  { value: 'Reserved', label: 'Reservadas' },
  { value: 'Sold', label: 'Vendidas' },
  { value: 'Expired', label: 'Vencidas' },
  { value: 'Cancelled', label: 'Canceladas' },
];

export function isReservationStatusFilter(value: string): value is ReservationStatusFilter {
  return RESERVATION_STATUS_OPTIONS.some((option) => option.value === value);
}

export function filterReservations(reservations: readonly Reservation[], status: ReservationStatusFilter): Reservation[] {
  return status === ALL_STATUSES ? [...reservations] : reservations.filter((reservation) => reservation.status === status);
}

/** De la más reciente a la más antigua (las fechas inválidas quedan al final). */
export function sortReservations(reservations: readonly Reservation[]): Reservation[] {
  const time = (reservation: Reservation) => (Number.isNaN(reservation.createdAt.getTime()) ? 0 : reservation.createdAt.getTime());
  return [...reservations].sort((a, b) => time(b) - time(a) || b.number.localeCompare(a.number));
}

function rejectInvalid(errors: Partial<Record<string, string>>): void {
  const messages = Object.values(errors).filter((message): message is string => Boolean(message));
  if (messages.length === 0) return;
  throw new WebApiError({ kind: 'validation', status: 400, message: messages[0], errors: messages });
}

export function createAccountUseCases(gateway: IAccountGateway): AccountUseCases {
  return {
    load: (options) => gateway.account(options),
    async save(update, options) {
      rejectInvalid(validateAccountForm(toAccountForm({ ...update, email: '' })));
      return gateway.updateAccount(update, options);
    },
    reservations: async (options) => sortReservations(await gateway.reservations(options)),
    async release(number, options) {
      const code = number.trim().toUpperCase();
      if (!code) throw new WebApiError({ kind: 'validation', status: 400, message: 'Falta el número de la reserva.' });
      return gateway.cancelReservation(code, options);
    },
    async changePassword(change, options) {
      rejectInvalid(validateChangePasswordForm({ current: change.currentPassword, next: change.newPassword, confirm: change.newPassword }));
      return gateway.changePassword(change, options);
    },
    async reserve(input, options) {
      const problem = validateCartReservationItems(input.items) ?? validateHoldDays(input.holdDays);
      if (problem) throw new WebApiError({ kind: 'validation', status: 400, message: problem, errors: [problem] });
      // El servidor rechaza los saltos de línea en medio de las notas: se unen con un espacio.
      const notes = input.notes ? toSingleLine(input.notes) : '';
      return gateway.createReservation(
        {
          lines: input.items.map((item) => ({ sku: item.sku, quantity: item.quantity })),
          kind: 'cart',
          ...(input.holdDays !== undefined ? { holdDays: input.holdDays } : {}),
          ...(notes ? { notes } : {}),
        },
        options,
      );
    },
  };
}
