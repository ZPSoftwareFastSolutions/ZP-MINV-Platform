// Contrato del servidor para la presentación (regla P-07): los tipos de cada petición y respuesta, las operaciones del
// RPC y las listas de permisos y roles, tal como los genera `minv contrato-web`. Los módulos del panel importan de aquí
// y NO declaran a mano ningún tipo del servidor:
//
//   import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
//   const filas: RpcResponseOf<'GetMyReservationsQuery'> = await rpc.send('GetMyReservationsQuery', {});
//
// Llega por `container.ts` porque es el único archivo de la presentación que puede importar la infraestructura.

export type * from './container';
export { ACCOUNT_OPERATIONS, PERMISSION_LIST, ROLE_LIST, RPC_OPERATIONS, isRpcOperation, permissionName, roleName, rpcOperation } from './container';
