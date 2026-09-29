// Sesión web de toda la página (V7). Al arrancar pregunta al servidor si hay sesión (`GET /api/v1/web/session`: la
// cookie HttpOnly la adjunta el navegador) y expone la sesión, ingresar, registrarse y salir con `useSession()`.
//
// - La sesión vive SOLO en la memoria de React. Nada se guarda en localStorage, sessionStorage ni cookies propias
//   (regla P-02): al recargar, se vuelve a preguntar al servidor.
// - Si CUALQUIER pedido RPC responde 401, la sesión se limpia y queda marcada como vencida; las guardas de ruta y el
//   vigilante (`SessionEndWatcher`) mandan a ingresar conservando a dónde iba la persona.
// - No bloquea la tienda: mientras carga, las páginas públicas se ven igual; solo las rutas protegidas esperan.

import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { asWebApiError, type WebApiError } from '@/1-domain/auth/errors';
import { can } from '@/1-domain/auth/permissions';
import type { Credentials, Registration, Session, SessionEnd } from '@/1-domain/auth/types';
import type { WebServices } from '@/4-presentation/app/container';
import { SessionContext, WebServicesContext, type SessionApi } from './SessionContext';

/** Al volver a la pestaña se vuelve a comprobar la sesión si la última comprobación tiene más de este tiempo. */
export const SESSION_RECHECK_MS = 60_000;

export interface SessionProviderProps {
  /** Servicios de la sesión ya elegidos (o su promesa: el modo mock se carga en un fragmento aparte). */
  web: WebServices | Promise<WebServices>;
  recheckMs?: number;
  children: ReactNode;
}

interface SessionState {
  status: 'loading' | 'ready';
  session: Session | null;
  loadError: WebApiError | null;
  ended: SessionEnd | null;
}

const INITIAL: SessionState = { status: 'loading', session: null, loadError: null, ended: null };

export function SessionProvider({ web, recheckMs = SESSION_RECHECK_MS, children }: SessionProviderProps) {
  // Los servicios se eligen una vez con el valor inicial; cambiar `web` después no se contempla.
  const [ready] = useState(() => Promise.resolve(web));
  const [services, setServices] = useState<WebServices | null>(null);
  const [state, setState] = useState<SessionState>(INITIAL);
  /** Cambia con cada ingreso, cierre o vencimiento: una respuesta vieja de `GET /session` no pisa el estado nuevo. */
  const version = useRef(0);
  const checkedAt = useRef(0);
  const sessionRef = useRef<Session | null>(null);

  useEffect(() => {
    sessionRef.current = state.session;
  }, [state.session]);

  const refresh = useCallback(async (): Promise<Session | null> => {
    const started = version.current;
    // Sin sesión a la vista se muestra «cargando»; con sesión, la comprobación es silenciosa (sin parpadeos).
    setState((current) => (current.session ? current : { ...current, status: 'loading' }));
    try {
      const session = await (await ready).session.current();
      checkedAt.current = Date.now();
      if (version.current !== started) return sessionRef.current;
      setState((current) => ({
        status: 'ready',
        session,
        loadError: null,
        // Había sesión y el servidor ya no la reconoce: venció (o se cerró en otra pestaña).
        ended: session ? null : current.session ? 'expired' : current.ended,
      }));
      return session;
    } catch (error) {
      const failure = asWebApiError(error);
      if (version.current !== started) return sessionRef.current;
      // Una falla pasajera no tira una sesión que está funcionando; sin sesión, queda el error para «Reintentar».
      setState((current) => (current.session ? { ...current, status: 'ready' } : { status: 'ready', session: null, loadError: failure, ended: current.ended }));
      return sessionRef.current;
    }
  }, [ready]);

  // Carga inicial y escucha de los 401 del RPC.
  useEffect(() => {
    let alive = true;
    let stop: (() => void) | undefined;
    ready
      .then((resolved) => {
        if (!alive) return;
        setServices(resolved);
        stop = resolved.onSessionExpired(() => {
          version.current += 1;
          setState({ status: 'ready', session: null, loadError: null, ended: 'expired' });
        });
        return refresh();
      })
      .catch((error: unknown) => {
        // No se pudieron preparar los servicios (por ejemplo, no bajó el fragmento del modo mock).
        if (alive) setState({ status: 'ready', session: null, loadError: asWebApiError(error), ended: null });
      });
    return () => {
      alive = false;
      stop?.();
    };
  }, [ready, refresh]);

  // Al volver a la pestaña: si la sesión se cerró en otra pestaña o venció, esta también lo refleja.
  useEffect(() => {
    if (!state.session) return;
    const onVisible = () => {
      if (document.visibilityState === 'visible' && Date.now() - checkedAt.current >= recheckMs) void refresh();
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => document.removeEventListener('visibilitychange', onVisible);
  }, [state.session, recheckMs, refresh]);

  const started = useCallback((session: Session): Session => {
    version.current += 1;
    checkedAt.current = Date.now();
    setState({ status: 'ready', session, loadError: null, ended: null });
    return session;
  }, []);

  const login = useCallback(async (credentials: Credentials) => started(await (await ready).session.login(credentials)), [ready, started]);

  const register = useCallback(async (registration: Registration) => started(await (await ready).session.register(registration)), [ready, started]);

  const logout = useCallback(async () => {
    await (await ready).session.logout();
    version.current += 1;
    setState({ status: 'ready', session: null, loadError: null, ended: 'logout' });
  }, [ready]);

  const acknowledgeEnd = useCallback(() => {
    setState((current) => (current.ended ? { ...current, ended: null } : current));
  }, []);

  const api = useMemo<SessionApi>(
    () => ({
      status: state.status,
      session: state.session,
      loadError: state.loadError,
      ended: state.ended,
      mode: services?.mode ?? 'api',
      demoUsers: services?.demoUsers ?? [],
      login,
      register,
      logout,
      refresh,
      acknowledgeEnd,
      can: (permission) => can(state.session, permission),
    }),
    [state, services, login, register, logout, refresh, acknowledgeEnd],
  );

  return (
    <SessionContext.Provider value={api}>
      <WebServicesContext.Provider value={services}>{children}</WebServicesContext.Provider>
    </SessionContext.Provider>
  );
}
