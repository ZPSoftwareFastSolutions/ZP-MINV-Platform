// Reglas de acceso de los módulos del panel: con qué permisos se ven un módulo, una de sus pantallas, un botón del
// tablero o una estadística. Es comodidad, no seguridad: el servidor vuelve a decidir en cada pedido (regla P-01).
//
//   { all: ['iam.audit.view'] }                                   ← hace falta ESE permiso
//   { any: ['inventory.movements.register.warehouse', 'inventory.movements.register.sales'] }   ← basta UNO de ellos
//   { all: ['sales.view', 'inventory.stock.view'] }               ← hacen falta los DOS
//   {}                                                            ← basta con tener sesión del personal
//
// Con `all` y `any` a la vez hacen falta todos los de `all` Y al menos uno de `any`.

import { permissionName } from '@/4-presentation/app/contract';

export interface PermissionRule {
  /** Basta con UNO de estos permisos (vacío = no exige nada). */
  any?: readonly string[];
  /** Hacen falta TODOS estos permisos (vacío = no exige nada). */
  all?: readonly string[];
}

/** ¿Los permisos de la sesión cumplen la regla? Sin regla, sí. */
export function allows(rule: PermissionRule | undefined, granted: readonly string[]): boolean {
  if (!rule) return true;
  const all = rule.all ?? [];
  const any = rule.any ?? [];
  return all.every((permission) => granted.includes(permission)) && (any.length === 0 || any.some((permission) => granted.includes(permission)));
}

/** Lo que falta para cumplir una o varias reglas (las de un módulo y su pantalla). */
export interface MissingPermissions {
  /** Permisos que hacen falta sí o sí. */
  all: string[];
  /** Alternativas: hace falta al menos UNO de estos (vacío si no hay alternativas pendientes). */
  any: string[];
}

/** Qué le falta a la sesión para cumplir TODAS las reglas. */
export function missingFor(rules: readonly (PermissionRule | undefined)[], granted: readonly string[]): MissingPermissions {
  const all = new Set<string>();
  const any = new Set<string>();
  for (const rule of rules) {
    if (!rule) continue;
    for (const permission of rule.all ?? []) if (!granted.includes(permission)) all.add(permission);
    const alternatives = rule.any ?? [];
    if (alternatives.length > 0 && !alternatives.some((permission) => granted.includes(permission))) {
      // Una sola alternativa es, en realidad, un permiso obligatorio.
      if (alternatives.length === 1) all.add(alternatives[0]);
      else for (const permission of alternatives) any.add(permission);
    }
  }
  return { all: [...all], any: [...any].filter((permission) => !all.has(permission)) };
}

export function hasMissing(missing: MissingPermissions): boolean {
  return missing.all.length > 0 || missing.any.length > 0;
}

function quoted(codes: readonly string[]): string[] {
  return codes.map((code) => `«${permissionName(code)}»`);
}

function joined(items: readonly string[], last: 'y' | 'o'): string {
  if (items.length <= 1) return items[0] ?? '';
  return `${items.slice(0, -1).join(', ')} ${last} ${items[items.length - 1]}`;
}

/**
 * Lo que falta, en palabras y con los nombres de los permisos del contrato:
 * «Falta el permiso «Consultar la auditoría y la actividad». Pida al administrador que se lo asigne.»
 * «Necesita al menos uno de estos permisos: «Registrar entradas, saldo inicial y ajustes» o «Registrar salidas». …»
 */
export function missingText(missing: MissingPermissions): string {
  const parts: string[] = [];
  if (missing.all.length === 1) parts.push(`Falta el permiso ${quoted(missing.all)[0]}.`);
  else if (missing.all.length > 1) parts.push(`Faltan los permisos ${joined(quoted(missing.all), 'y')}.`);
  if (missing.any.length > 0) parts.push(`Necesita al menos uno de estos permisos: ${joined(quoted(missing.any), 'o')}.`);
  if (parts.length === 0) return 'Su cuenta no tiene acceso a esta pantalla. Pida al administrador que se lo asigne.';
  const plural = missing.all.length + (missing.any.length > 0 ? 1 : 0) > 1;
  parts.push(plural ? 'Pida al administrador que se los asigne.' : 'Pida al administrador que se lo asigne.');
  return parts.join(' ');
}
