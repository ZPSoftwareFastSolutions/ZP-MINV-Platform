// ADAPTADOR DEL CONTRATO: el ÚNICO módulo que importa `contract.generated.ts` (lo vigila src/architecture.test.ts).
// Todo lo demás (pasarelas HTTP, modo mock, y la presentación a través de `4-presentation/app/container.ts`) usa los
// nombres de aquí. El generado lo escribe `minv contrato-web` (regla P-07): si alguna lista u operación se llama distinto
// se corrige SOLO este archivo.

import { PERMISSIONS, ROLES, RPC_META } from './contract.generated';
import type * as Contract from './contract.generated';

// Todos los tipos del contrato (peticiones, respuestas, WebSession, RpcRequest, RpcResponse, RpcError…). Los ajustes de
// abajo tienen el mismo nombre y tienen prioridad sobre los del generado.
export type * from './contract.generated';

// ---------------------------------------------------------------------------------------------------- ajustes de la web
// El contrato generado es EXACTO: lo que el servidor lee y escribe. El código de la web anterior a él (el servidor en
// memoria del modo mock, webMappers y sus pruebas) se escribió contra el contrato provisional y difiere en estos puntos.
// Los ajustes lo dejan compilar sin tocar el generado; se aplican solo a las operaciones de la cuenta y de la sesión que
// la web ya usaba (el resto queda exacto) y cada uno se borra cuando se corrija el código que lo necesita
// (docs/architecture/v7-notas/informe-B5.md, «Pendientes»).

/** El servidor lee una enumeración sin distinguir mayúsculas (JsonStringEnumConverter): la web envía `kind: 'cart'`. */
type EnumText<T extends string> = T | Lowercase<T>;

/** La web envía SIEMPRE la cantidad de cada línea (el servidor usaría 1 si faltara). */
export type StorefrontReservationLineInput = Contract.StorefrontReservationLineInput & { quantity: number };

/** `kind` en minúsculas (webMappers.toCreateReservationPayload y las pruebas envían 'build' | 'cart'). */
export type CreateMyReservationCommand = Omit<Contract.CreateMyReservationCommand, 'kind' | 'lines'> & {
  kind?: EnumText<Contract.PcBuildKind>;
  lines: StorefrontReservationLineInput[];
};

/** El modo mock arma el alcance sin `active` (el servidor lo envía: la sucursal activa dentro de `branches`, o null). */
export type BranchAccess = Omit<Contract.BranchAccess, 'active'> & Partial<Pick<Contract.BranchAccess, 'active'>>;

export type WebSession = Omit<Contract.WebSession, 'access'> & { access: BranchAccess };

/** El modo mock no inventa el código del cliente (el servidor siempre envía `customerCode`). */
export type MyAccountView = Omit<Contract.MyAccountView, 'customerCode'> & Partial<Pick<Contract.MyAccountView, 'customerCode'>>;

/** El modo mock y sus pruebas leen `reservedUntil` como texto. El servidor lo declara anulable (nulo si el armado nunca se
 *  reservó), pero en las operaciones de la cuenta siempre llega: el cliente solo ve armados que se reservaron. */
export type StorefrontReservationView = Omit<Contract.StorefrontReservationView, 'reservedUntil'> & { reservedUntil: string };

type AdjustedOperation =
  | 'CancelMyReservationCommand'
  | 'CreateMyReservationCommand'
  | 'GetMyAccountQuery'
  | 'GetMyReservationsQuery'
  | 'SelectBranchCommand'
  | 'UpdateMyAccountCommand';

/** Operaciones del RPC por nombre corto: las del generado, con los ajustes de arriba en la cuenta y la sesión. */
export type RpcOperations = Omit<Contract.RpcOperations, AdjustedOperation> & {
  CancelMyReservationCommand: { request: Contract.CancelMyReservationCommand; response: StorefrontReservationView };
  CreateMyReservationCommand: { request: CreateMyReservationCommand; response: StorefrontReservationView };
  GetMyAccountQuery: { request: Contract.GetMyAccountQuery; response: MyAccountView };
  GetMyReservationsQuery: { request: Contract.GetMyReservationsQuery; response: StorefrontReservationView[] };
  SelectBranchCommand: { request: Contract.SelectBranchCommand; response: BranchAccess };
  UpdateMyAccountCommand: { request: Contract.UpdateMyAccountCommand; response: MyAccountView };
};

// ---------------------------------------------------------------------------------------------------- operaciones

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
