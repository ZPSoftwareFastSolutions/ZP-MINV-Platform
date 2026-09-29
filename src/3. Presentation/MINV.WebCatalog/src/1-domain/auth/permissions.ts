// Reglas puras de roles y permisos sobre la sesión. Sirven para MOSTRAR u ocultar (menú, botones): la decisión real la
// toma el servidor en cada petición (regla P-01). Los nombres de todos los permisos y roles salen del contrato generado.

import type { Session, SessionBranch } from './types';

/** Rol de las cuentas que se registran solas en la tienda. */
export const CUSTOMER_ROLE = 'CLIENTE';

/** Permisos de la cuenta de cliente (los únicos que una sesión `customer` puede ejecutar). */
export const ACCOUNT_PERMISSIONS = {
  manage: 'account.manage',
  reserve: 'account.reserve',
} as const;

type WithPermissions = Pick<Session, 'permissions'> | null | undefined;

/** ¿La sesión tiene el permiso? Sin sesión, nunca. */
export function can(session: WithPermissions, permission: string): boolean {
  return Boolean(session) && session!.permissions.includes(permission);
}

/** ¿Tiene al menos uno? Una lista vacía significa «basta con tener sesión». */
export function canAny(session: WithPermissions, permissions: readonly string[]): boolean {
  if (!session) return false;
  return permissions.length === 0 || permissions.some((permission) => session.permissions.includes(permission));
}

/** ¿Tiene todos? Una lista vacía significa «basta con tener sesión». */
export function canAll(session: WithPermissions, permissions: readonly string[]): boolean {
  if (!session) return false;
  return permissions.every((permission) => session.permissions.includes(permission));
}

export function hasRole(session: Pick<Session, 'roles'> | null | undefined, role: string): boolean {
  return Boolean(session) && session!.roles.includes(role);
}

export function isStaff(session: Pick<Session, 'kind'> | null | undefined): boolean {
  return session?.kind === 'staff';
}

export function isCustomer(session: Pick<Session, 'kind'> | null | undefined): boolean {
  return session?.kind === 'customer';
}

/** Sucursal activa de la sesión (donde se registran las operaciones que no indican otra), o null. */
export function activeBranch(session: Pick<Session, 'access'> | null | undefined): SessionBranch | null {
  if (!session) return null;
  return session.access.branches.find((branch) => branch.id === session.access.activeBranchId) ?? null;
}

/** Iniciales para el avatar: «Valentina Aguirre» → «VA»; sin nombre, la primera letra del correo. */
export function initialsOf(session: Pick<Session, 'displayName' | 'email'>): string {
  const words = session.displayName.trim().split(/\s+/).filter(Boolean);
  const letters = words.length >= 2 ? `${words[0][0]}${words[words.length - 1][0]}` : (words[0] ?? session.email).slice(0, 2);
  return letters.toUpperCase();
}
