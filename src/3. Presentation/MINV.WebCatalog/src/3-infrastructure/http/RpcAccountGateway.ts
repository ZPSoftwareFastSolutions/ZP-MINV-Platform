// IAccountGateway sobre el RPC: los casos de uso `MINV.Application.Accounts.*` y el cambio de contraseña, traducidos al
// dominio. Funciona igual con el RPC por HTTP y con el RPC en memoria del modo mock. No envía ningún identificador de
// cliente: el servidor opera sobre el cliente de la sesión (regla P-04).

import type { AccountUpdate, CustomerAccount, PasswordChange } from '@/1-domain/account/types';
import { WebApiError } from '@/1-domain/auth/errors';
import type { IAccountGateway } from '@/1-domain/ports/IAccountGateway';
import type { IRpcGateway, RpcSendOptions } from '@/1-domain/ports/IRpcGateway';
import type { Reservation } from '@/1-domain/storefront/types';
import { ACCOUNT_OPERATIONS, type RpcOperations } from './contract';
import { looksLikeAccount, looksLikeReservation, toAccountReservation, toAccountReservations, toCustomerAccount, toUpdateAccountPayload } from './webMappers';

export class RpcAccountGateway implements IAccountGateway {
  private readonly rpc: IRpcGateway<RpcOperations>;

  constructor(rpc: IRpcGateway<RpcOperations>) {
    this.rpc = rpc;
  }

  async account(options?: RpcSendOptions): Promise<CustomerAccount> {
    return toCustomerAccount(await this.rpc.send(ACCOUNT_OPERATIONS.account, {}, options));
  }

  async updateAccount(update: AccountUpdate, options?: RpcSendOptions): Promise<CustomerAccount> {
    const response: unknown = await this.rpc.send(ACCOUNT_OPERATIONS.updateAccount, toUpdateAccountPayload(update), options);
    // Si el comando no devuelve la cuenta, se vuelve a leer: la pantalla muestra SIEMPRE lo que quedó en el servidor.
    return looksLikeAccount(response) ? toCustomerAccount(response) : this.account({ signal: options?.signal });
  }

  async reservations(options?: RpcSendOptions): Promise<Reservation[]> {
    return toAccountReservations(await this.rpc.send(ACCOUNT_OPERATIONS.reservations, {}, options));
  }

  async cancelReservation(number: string, options?: RpcSendOptions): Promise<Reservation> {
    const response: unknown = await this.rpc.send(ACCOUNT_OPERATIONS.cancelReservation, { number }, options);
    if (looksLikeReservation(response)) return toAccountReservation(response);
    const updated = (await this.reservations({ signal: options?.signal })).find((reservation) => reservation.number === number);
    if (!updated) throw new WebApiError({ kind: 'not_found', status: 404, message: `La reserva ${number} ya no está en su cuenta.` });
    return updated;
  }

  async changePassword(change: PasswordChange, options?: RpcSendOptions): Promise<void> {
    await this.rpc.send(ACCOUNT_OPERATIONS.changePassword, { currentPassword: change.currentPassword, newPassword: change.newPassword }, options);
  }
}
