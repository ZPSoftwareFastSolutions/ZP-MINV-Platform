// «Cambiar contraseña» (/cambiar-contrasena): para el personal y los clientes. Es también la pantalla obligatoria cuando
// la sesión llega con `mustChangePassword` (el administrador asignó la contraseña): las guardas traen aquí desde
// cualquier ruta protegida y, al cambiarla, la persona sigue a donde iba (`volver`, solo rutas internas).

import { useNavigate, useSearchParams } from 'react-router-dom';
import type { Voice } from '@/1-domain/auth/errors';
import { destinationAfterPasswordChange, RETURN_PARAM, safeReturnPath } from '@/2-application/auth/navigation';
import { ROUTES } from '@/4-presentation/app/routes';
import { AuthLayout } from '@/4-presentation/components/auth/AuthLayout';
import { ChangePasswordForm } from '@/4-presentation/components/auth/ChangePasswordForm';
import { Alert } from '@/4-presentation/components/ui/Alert';
import { useDocumentMeta } from '@/4-presentation/hooks/useDocumentMeta';
import { useSession } from '@/4-presentation/hooks/useSession';
import { useToast } from '@/4-presentation/hooks/useToast';

export function ChangePasswordPage() {
  useDocumentMeta({ title: 'Cambiar contraseña' });
  const { session, refresh } = useSession();
  const navigate = useNavigate();
  const toast = useToast();
  const [params] = useSearchParams();
  const returnTo = safeReturnPath(params.get(RETURN_PARAM));

  // La guarda de la ruta garantiza que hay sesión; mientras redirige no se dibuja nada.
  if (!session) return null;
  const voice: Voice = session.kind === 'staff' ? 'panel' : 'store';
  const kind = session.kind;

  const changed = async () => {
    // El servidor quita la marca «debe cambiarla»: se vuelve a leer la sesión antes de seguir.
    const updated = await refresh();
    toast.notify({ tone: 'success', title: 'Contraseña cambiada', description: voice === 'store' ? 'Usala la próxima vez que ingreses.' : 'Úsela la próxima vez que ingrese.' });
    if (!updated) {
      // El servidor cerró las sesiones al cambiar la contraseña: hay que ingresar con la nueva.
      navigate(ROUTES.loginReturning(returnTo), { replace: true });
      return;
    }
    navigate(destinationAfterPasswordChange({ kind }, returnTo), { replace: true });
  };

  return (
    <AuthLayout
      eyebrow={session.displayName}
      title="Cambiar contraseña"
      description={voice === 'store' ? 'Elegí una contraseña que no uses en otros sitios.' : 'Elija una contraseña que no use en otros sitios.'}
    >
      {session.mustChangePassword && (
        <Alert tone="warning" title={voice === 'store' ? 'Tenés que cambiar tu contraseña' : 'Debe cambiar su contraseña'} className="mb-4">
          {voice === 'store'
            ? 'La contraseña que tenés te la asignaron en la tienda. Cambiala para seguir.'
            : 'La contraseña actual fue asignada por el administrador. Cámbiela para continuar.'}
        </Alert>
      )}
      <ChangePasswordForm voice={voice} onChanged={changed} />
    </AuthLayout>
  );
}
