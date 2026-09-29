// Guarda de las rutas protegidas. `/panel/*` exige una sesión del personal y `/mi-cuenta/*` una de cliente; sin sesión
// manda a `/ingresar?volver=…` conservando a dónde iba la persona. Si la sesión debe cambiar la contraseña, toda ruta
// protegida lleva a esa pantalla hasta que la cambie.
//
// Es comodidad, no seguridad: aunque alguien salte esta guarda, el servidor revisa la sesión y los permisos en cada
// pedido (regla P-01) y la página no recibe ningún dato.

import { LoaderCircle } from 'lucide-react';
import type { ReactNode } from 'react';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { describeWebApiError } from '@/1-domain/auth/errors';
import type { SessionKind } from '@/1-domain/auth/types';
import { AUTH_PATHS, changePasswordPath, homeFor, isUnder, loginPath, RETURN_PARAM } from '@/2-application/auth/navigation';
import { ROUTES } from '@/4-presentation/app/routes';
import { ErrorState } from '@/4-presentation/components/feedback/AsyncState';
import { Container } from '@/4-presentation/components/ui/Container';
import { useSession } from '@/4-presentation/hooks/useSession';

/** Estado que viaja en la navegación hacia /ingresar (no va en la URL). */
export interface LoginLocationState {
  reason?: 'expired';
}

export interface RequireSessionProps {
  /** Tipo de sesión que exige la ruta; sin valor, cualquier sesión. */
  kind?: SessionKind;
  /** Contenido protegido; sin hijos dibuja la ruta anidada (`<Outlet />`). */
  children?: ReactNode;
}

export function SessionLoadingScreen() {
  return (
    <Container className="flex min-h-[50vh] flex-col items-center justify-center gap-3 py-16 text-center" data-testid="sesion-cargando">
      <LoaderCircle aria-hidden="true" className="size-8 animate-spin text-accent" />
      <p role="status" aria-live="polite" className="text-sm text-text-muted">
        Comprobando la sesión…
      </p>
    </Container>
  );
}

export function RequireSession({ kind, children }: RequireSessionProps) {
  const { status, session, loadError, ended, refresh } = useSession();
  const location = useLocation();
  const here = `${location.pathname}${location.search}${location.hash}`;

  if (status === 'loading') return <SessionLoadingScreen />;

  if (!session) {
    if (loadError) {
      return (
        <Container className="py-12">
          <ErrorState title="No se pudo comprobar la sesión" message={describeWebApiError(loadError, 'panel')} onRetry={() => void refresh()} />
        </Container>
      );
    }
    // La persona cerró la sesión estando aquí: vuelve a la tienda, no a la pantalla de ingreso.
    if (ended === 'logout') return <Navigate to={ROUTES.home} replace />;
    const state: LoginLocationState | undefined = ended === 'expired' ? { reason: 'expired' } : undefined;
    // Desde «Cambiar contraseña» se vuelve a donde iba la persona, no a esa pantalla.
    const returnTo = isUnder(location.pathname, AUTH_PATHS.changePassword) ? new URLSearchParams(location.search).get(RETURN_PARAM) : here;
    return <Navigate to={loginPath(returnTo)} replace state={state} />;
  }

  // Un cliente no entra al panel y el personal no entra a «Mi cuenta»: cada uno va a su inicio.
  if (kind && session.kind !== kind) return <Navigate to={homeFor(session.kind)} replace />;

  if (session.mustChangePassword && !isUnder(location.pathname, AUTH_PATHS.changePassword)) {
    return <Navigate to={changePasswordPath(here)} replace />;
  }

  return <>{children ?? <Outlet />}</>;
}
