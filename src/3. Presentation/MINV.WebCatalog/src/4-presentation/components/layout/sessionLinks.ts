// Opciones del menú de la sesión según su tipo: el personal va al panel; el cliente, a su cuenta y a sus reservas; los
// dos pueden cambiar la contraseña. Las comparten el menú de la cabecera y el menú móvil.

import { KeyRound, LayoutDashboard, TicketCheck, UserRound, type LucideIcon } from 'lucide-react';
import type { Session } from '@/1-domain/auth/types';
import { ROUTES } from '@/4-presentation/app/routes';

export interface SessionLink {
  id: string;
  label: string;
  to: string;
  icon: LucideIcon;
}

export function sessionLinks(session: Pick<Session, 'kind'>): SessionLink[] {
  const own: SessionLink[] =
    session.kind === 'staff'
      ? [{ id: 'panel', label: 'Ir al panel', to: ROUTES.panel, icon: LayoutDashboard }]
      : [
          { id: 'cuenta', label: 'Mi cuenta', to: ROUTES.accountSection('datos'), icon: UserRound },
          { id: 'reservas', label: 'Mis reservas', to: ROUTES.accountSection('reservas'), icon: TicketCheck },
        ];
  const password = session.kind === 'staff' ? ROUTES.changePassword : ROUTES.accountSection('contrasena');
  return [...own, { id: 'contrasena', label: 'Cambiar contraseña', to: password, icon: KeyRound }];
}
