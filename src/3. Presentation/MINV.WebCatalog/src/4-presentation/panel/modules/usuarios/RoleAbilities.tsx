// Módulo «Usuarios» · lo que puede hacer un rol, en palabras: sus funciones agrupadas por área (las descripciones salen
// del servidor, `GetRolesQuery`). La usan la pestaña «Roles y permisos» y el detalle de un usuario.

import { Check, X } from 'lucide-react';
import type { PermissionEntry, RoleView } from './users';

export interface RoleAbilitiesProps {
  view: RoleView;
  /** Nivel de los títulos de cada área (3 en una tarjeta de rol; 4 dentro de otra sección). */
  level?: 3 | 4;
}

export function RoleAbilities({ view, level = 3 }: RoleAbilitiesProps) {
  const Heading = level === 3 ? 'h3' : 'h4';
  if (view.groups.length === 0) {
    return <p className="text-sm text-text-muted">{view.granted === 0 ? 'Este rol no tiene funciones asignadas.' : 'Ninguna de sus funciones coincide con los filtros.'}</p>;
  }
  return (
    <div className="space-y-4">
      {view.groups.map((group) => (
        <div key={group.area}>
          <Heading className="text-xs font-semibold tracking-wide text-text-faint uppercase">{group.label}</Heading>
          <PermissionList items={group.items} granted />
        </div>
      ))}
    </div>
  );
}

export interface PermissionListProps {
  items: readonly PermissionEntry[];
  /** true = las tiene (✓); false = no las tiene (✗, atenuadas). */
  granted: boolean;
}

export function PermissionList({ items, granted }: PermissionListProps) {
  return (
    <ul className="mt-1.5 space-y-1.5">
      {items.map((item) => (
        <li key={item.code} className={granted ? 'flex items-start gap-2 text-sm text-text' : 'flex items-start gap-2 text-sm text-text-muted'}>
          {granted ? (
            <Check aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-success-text" />
          ) : (
            <X aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-text-faint" />
          )}
          <span className="min-w-0">
            <span className="sr-only">{granted ? 'Puede: ' : 'No puede: '}</span>
            {item.name}
          </span>
        </li>
      ))}
    </ul>
  );
}
