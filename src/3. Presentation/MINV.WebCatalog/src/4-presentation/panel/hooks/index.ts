// Hooks del panel. Los módulos importan de aquí:
//
//   import { useRpcQuery, useRpcCommand, usePermissions, useTableState } from '@/4-presentation/panel/hooks';
//
// Los avisos (`useNotify`) están en el conjunto de componentes (`@/4-presentation/panel/kit`).

export { useRpcQuery, type RpcQuery, type RpcQueryOptions, type RpcQueryStatus } from './useRpcQuery';
export { useRpcCommand, type RpcCommand, type RpcCommandOptions, type RpcCommandOutcome } from './useRpcCommand';
export { permissionsOf, usePermissions, type Permissions } from './usePermissions';
export { TABLE_PARAMS, useTableState, type FilterValues, type TableSortProps, type TableState, type TableStateOptions } from './useTableState';
