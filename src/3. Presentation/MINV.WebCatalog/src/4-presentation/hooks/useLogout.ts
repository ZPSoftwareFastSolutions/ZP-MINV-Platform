import { useCallback, useState } from 'react';
import { asWebApiError, describeWebApiError, type Voice } from '@/1-domain/auth/errors';
import { useSession } from './useSession';
import { useToast } from './useToast';

const TEXT: Record<Voice, { closed: string; farewell?: string; failed: string }> = {
  store: { closed: 'Sesión cerrada', farewell: 'Volvé cuando quieras.', failed: 'No pudimos cerrar la sesión' },
  panel: { closed: 'Sesión cerrada', failed: 'No se pudo cerrar la sesión' },
};

/**
 * «Cerrar sesión» con su aviso. Si estaba en una ruta protegida, la guarda la devuelve a la tienda. Si el servidor no
 * respondió, la sesión NO se da por cerrada (la cookie seguiría viva): se avisa para reintentar.
 */
export function useLogout(voice: Voice = 'store'): { logout: () => Promise<void>; leaving: boolean } {
  const { logout: closeSession } = useSession();
  const toast = useToast();
  const [leaving, setLeaving] = useState(false);

  const logout = useCallback(async () => {
    setLeaving(true);
    try {
      await closeSession();
      toast.notify({ tone: 'info', title: TEXT[voice].closed, description: TEXT[voice].farewell });
    } catch (error) {
      toast.notify({ tone: 'danger', title: TEXT[voice].failed, description: describeWebApiError(asWebApiError(error), voice), duration: 8000 });
    } finally {
      setLeaving(false);
    }
  }, [closeSession, toast, voice]);

  return { logout, leaving };
}
