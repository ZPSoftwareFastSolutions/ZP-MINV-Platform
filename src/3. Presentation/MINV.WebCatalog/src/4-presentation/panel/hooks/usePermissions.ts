// Permisos de la sesión para MOSTRAR u ocultar botones, pestañas y acciones del panel. Es comodidad, no seguridad: el
// servidor vuelve a decidir en cada pedido (regla P-01) y, si rechaza, el aviso dice qué permiso falta.
//
//   const { can, canRun } = usePermissions();
//   {can('sales.pcbuild.manage') && <Button>Nueva reserva</Button>}
//   {canRun('<Operación>Command') && …}          ← con los permisos que declara el contrato para esa operación

import { useContext, useMemo } from 'react';
import type { Session } from '@/1-domain/auth/types';
import { rpcOperation, type RpcOperationName } from '@/4-presentation/app/contract';
import { SessionContext } from '@/4-presentation/state/SessionContext';

export interface Permissions {
  /** La sesión (null fuera de una ruta con sesión). */
  session: Session | null;
  /** Códigos de permiso de la sesión. */
  permissions: readonly string[];
  /** ¿Tiene el permiso? */
  can(permission: string): boolean;
  /** ¿Tiene al menos uno? (una lista vacía = basta con tener sesión). */
  canAny(permissions: readonly string[]): boolean;
  /** ¿Tiene todos? (una lista vacía = basta con tener sesión). */
  canAll(permissions: readonly string[]): boolean;
  /** Los de la lista que NO tiene. */
  missing(permissions: readonly string[]): string[];
  /** ¿Puede ejecutar la operación del RPC? (todos los permisos que declara el contrato; una desconocida, no). */
  canRun(operation: RpcOperationName): boolean;
}

/** Permisos de una sesión (función pura: la usan el hook y las pruebas). */
export function permissionsOf(session: Session | null): Permissions {
  const granted = session?.permissions ?? [];
  const has = (permission: string) => granted.includes(permission);
  return {
    session,
    permissions: granted,
    can: (permission) => session !== null && has(permission),
    canAny: (list) => session !== null && (list.length === 0 || list.some(has)),
    canAll: (list) => session !== null && list.every(has),
    missing: (list) => list.filter((permission) => !has(permission)),
    canRun: (operation) => {
      const info = rpcOperation(operation);
      return session !== null && info !== undefined && info.permissions.every(has);
    },
  };
}

/** Permisos de la sesión vigente. Fuera de `<SessionProvider>` responde como si no hubiera sesión (todo falso). */
export function usePermissions(): Permissions {
  const session = useContext(SessionContext)?.session ?? null;
  return useMemo(() => permissionsOf(session), [session]);
}
