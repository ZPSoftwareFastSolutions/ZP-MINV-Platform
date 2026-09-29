// USO · Muestra su contenido solo si la sesión tiene el permiso (o los permisos que el contrato declara para una
// operación). Es comodidad: el servidor vuelve a decidir en cada pedido (regla P-01).
//
//   <PermissionGate permission="sales.pcbuild.manage"><Button>Nueva reserva</Button></PermissionGate>
//   <PermissionGate permission={['billing.view', 'reports.view']} mode="any">…</PermissionGate>
//   <PermissionGate operation="GetMyAccountQuery" fallback={<AccessDenied operation="GetMyAccountQuery" />}>…</PermissionGate>
//
// `AccessDenied` explica en palabras qué permiso falta (para una pantalla o sección completa).

import clsx from 'clsx';
import { ShieldAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { rpcOperation, type RpcOperationName } from '@/4-presentation/app/contract';
import { usePermissions } from '../hooks/usePermissions';
import { missingPermissionsText } from '../lib/rpc';

export interface PermissionGateProps {
  /** Permiso o lista de permisos. */
  permission?: string | readonly string[];
  /** Con una lista: 'all' (por defecto) exige todos; 'any' basta uno. */
  mode?: 'all' | 'any';
  /** Operación del RPC: exige los permisos que declara el contrato. */
  operation?: RpcOperationName;
  /** Qué mostrar sin permiso (por defecto, nada). */
  fallback?: ReactNode;
  children: ReactNode;
}

export function PermissionGate({ permission, mode = 'all', operation, fallback = null, children }: PermissionGateProps) {
  const { canAll, canAny, canRun, session } = usePermissions();
  const list = permission === undefined ? [] : typeof permission === 'string' ? [permission] : permission;
  let allowed = session !== null && (mode === 'any' && list.length > 0 ? canAny(list) : canAll(list));
  if (operation) allowed = allowed && canRun(operation);
  return <>{allowed ? children : fallback}</>;
}

export interface AccessDeniedProps {
  /** Permisos que hacen falta. */
  permissions?: readonly string[];
  /** O la operación (se usan los permisos que declara el contrato). */
  operation?: RpcOperationName;
  title?: string;
  className?: string;
}

export function AccessDenied({ permissions, operation, title = 'No tiene permiso para ver esto', className }: AccessDeniedProps) {
  const { missing } = usePermissions();
  const required = permissions ?? (operation ? (rpcOperation(operation)?.permissions ?? []) : []);
  const lacking = missing(required);
  return (
    <div role="alert" className={clsx('flex flex-col items-center gap-3 rounded-card border border-warning/40 bg-warning-soft/40 px-6 py-10 text-center', className)}>
      <span aria-hidden="true" className="flex size-14 items-center justify-center rounded-2xl bg-warning-soft text-warning-text">
        <ShieldAlert className="size-7" />
      </span>
      <p className="font-display text-lg font-semibold text-text">{title}</p>
      <p className="max-w-md text-sm text-text-muted">
        {lacking.length > 0 ? missingPermissionsText(lacking) : 'Su cuenta no tiene acceso a esta sección. Pida al administrador que se lo asigne.'}
      </p>
    </div>
  );
}
