import { useContext } from 'react';
import { SessionContext, type SessionApi } from '@/4-presentation/state/SessionContext';

/** Sesión web: `const { session, login, logout, can } = useSession()`. */
export function useSession(): SessionApi {
  const api = useContext(SessionContext);
  if (!api) throw new Error('useSession() debe usarse dentro de <SessionProvider>.');
  return api;
}
