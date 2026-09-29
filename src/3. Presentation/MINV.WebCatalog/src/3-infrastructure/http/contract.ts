// ADAPTADOR DEL CONTRATO: el ÚNICO módulo que importa `contract.generated.ts` (lo vigila src/architecture.test.ts).
// Todo lo demás (pasarelas HTTP, modo mock, y la presentación a través de `4-presentation/app/container.ts`) usa los
// nombres de aquí. Cuando `minv contrato-web` genere el archivo definitivo, si alguna lista u operación se llama
// distinto se corrige SOLO este archivo.

import { PERMISSIONS, ROLES, RPC_META } from './contract.generated';
import type { RpcOperations } from './contract.generated';

// Todos los tipos del contrato (peticiones, respuestas, WebSession, RpcRequest, RpcResponse, RpcError…).
export type * from './contract.generated';

/** Nombre corto de una operación del RPC (`GetMyAccountQuery`). */
export type RpcOperationName = keyof RpcOperations & string;
export type RpcRequestOf<K extends RpcOperationName> = RpcOperations[K]['request'];
export type RpcResponseOf<K extends RpcOperationName> = RpcOperations[K]['response'];

/** Datos de una operación: nombre completo que viaja, si es comando y qué permisos y módulos exige. */
export interface RpcOperationInfo {
  type: string;
  command: boolean;
  permissions: readonly string[];
  modules: readonly string[];
  customer: boolean;
}

/** Operaciones que conoce la web, por nombre corto. */
export const RPC_OPERATIONS: Readonly<Record<RpcOperationName, RpcOperationInfo>> = RPC_META;

export function isRpcOperation(name: string): name is RpcOperationName {
  return Object.prototype.hasOwnProperty.call(RPC_OPERATIONS, name);
}

export function rpcOperation(name: string): RpcOperationInfo | undefined {
  return isRpcOperation(name) ? RPC_OPERATIONS[name] : undefined;
}

export interface NamedCode {
  code: string;
  name: string;
}

/** Permisos y roles del sistema con su nombre en español. */
export const PERMISSION_LIST: readonly NamedCode[] = PERMISSIONS;
export const ROLE_LIST: readonly NamedCode[] = ROLES;

/** Nombre en español de un permiso o rol; si el contrato no lo conoce, el código tal cual. */
export function permissionName(code: string): string {
  return PERMISSION_LIST.find((permission) => permission.code === code)?.name ?? code;
}

export function roleName(code: string): string {
  return ROLE_LIST.find((role) => role.code === code)?.name ?? code;
}

/** Operaciones de la cuenta del cliente y de la propia sesión que usa la web (nombres cortos del contrato). */
export const ACCOUNT_OPERATIONS = {
  account: 'GetMyAccountQuery',
  updateAccount: 'UpdateMyAccountCommand',
  reservations: 'GetMyReservationsQuery',
  cancelReservation: 'CancelMyReservationCommand',
  createReservation: 'CreateMyReservationCommand',
  changePassword: 'ChangePasswordCommand',
  selectBranch: 'SelectBranchCommand',
} as const satisfies Record<string, RpcOperationName>;
