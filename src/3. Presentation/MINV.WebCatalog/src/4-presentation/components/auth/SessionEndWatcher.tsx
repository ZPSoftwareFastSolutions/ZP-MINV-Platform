// Vigilante del fin de la sesión, montado una vez en la raíz del enrutador. Cuando un pedido descubre que la sesión
// venció (401), manda a la persona a ingresar conservando a dónde iba, también desde una página pública. Las rutas
// protegidas ya redirigen con su guarda (`RequireSession`); aquí se atiende el resto y se da por atendido el aviso.

import { useEffect } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { AUTH_PATHS, isProtectedPath, isUnder, loginPath } from '@/2-application/auth/navigation';
import { useSession } from '@/4-presentation/hooks/useSession';
import type { LoginLocationState } from './RequireSession';

export function SessionEndWatcher() {
  const { ended, acknowledgeEnd } = useSession();
  const location = useLocation();
  const navigate = useNavigate();

  useEffect(() => {
    if (!ended) return;
    // En una ruta protegida redirige la guarda; el aviso se da por atendido cuando la persona ya salió de ahí.
    if (isProtectedPath(location.pathname)) return;
    if (ended === 'expired' && !isUnder(location.pathname, AUTH_PATHS.login) && !isUnder(location.pathname, AUTH_PATHS.register)) {
      const state: LoginLocationState = { reason: 'expired' };
      navigate(loginPath(`${location.pathname}${location.search}${location.hash}`), { replace: true, state });
      return;
    }
    acknowledgeEnd();
  }, [ended, location, navigate, acknowledgeEnd]);

  return null;
}
