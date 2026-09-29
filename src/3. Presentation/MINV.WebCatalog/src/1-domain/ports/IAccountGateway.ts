// Puerto de la CUENTA DEL CLIENTE (V7): sus datos, sus reservas y su contraseña, ya en tipos del dominio. Lo implementa
// la infraestructura sobre el RPC (casos de uso `MINV.Application.Accounts.*`, permisos `account.*`). Opera siempre
// sobre el cliente de la sesión: ningún método recibe un identificador de cliente (regla P-04).

import type { AccountUpdate, CustomerAccount, PasswordChange } from '@/1-domain/account/types';
import type { Reservation } from '@/1-domain/storefront/types';
import type { RpcSendOptions } from './IRpcGateway';

export interface IAccountGateway {
  account(options?: RpcSendOptions): Promise<CustomerAccount>;
  /** Guarda los cambios y devuelve la cuenta como quedó en el servidor. */
  updateAccount(update: AccountUpdate, options?: RpcSendOptions): Promise<CustomerAccount>;
  /** Todas las reservas del cliente (armados y carritos), en cualquier estado. */
  reservations(options?: RpcSendOptions): Promise<Reservation[]>;
  /** Libera una reserva activa del cliente; devuelve la reserva como quedó. */
  cancelReservation(number: string, options?: RpcSendOptions): Promise<Reservation>;
  /** Cambia la contraseña de la sesión. Contraseña actual incorrecta: WebApiError `authentication`. */
  changePassword(change: PasswordChange, options?: RpcSendOptions): Promise<void>;
}
