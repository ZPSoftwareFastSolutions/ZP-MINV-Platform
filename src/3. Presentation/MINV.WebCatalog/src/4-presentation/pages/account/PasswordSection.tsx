// «Cambiar contraseña» dentro de «Mi cuenta»: el mismo formulario de /cambiar-contrasena, sin salir de la cuenta.

import { KeyRound } from 'lucide-react';
import { ChangePasswordForm } from '@/4-presentation/components/auth/ChangePasswordForm';
import { Card } from '@/4-presentation/components/ui/Card';
import { useSession } from '@/4-presentation/hooks/useSession';
import { useToast } from '@/4-presentation/hooks/useToast';

export function PasswordSection() {
  const { refresh } = useSession();
  const toast = useToast();

  const changed = async () => {
    toast.notify({ tone: 'success', title: 'Contraseña cambiada', description: 'Usala la próxima vez que ingreses.' });
    // Si el servidor cerró las sesiones al cambiarla, la guarda de la ruta lleva a ingresar con la nueva.
    await refresh();
  };

  return (
    <Card as="section" aria-labelledby="contrasena-titulo" padding="md" className="max-w-xl">
      <h2 id="contrasena-titulo" className="mb-1 flex items-center gap-2 text-2xl">
        <KeyRound aria-hidden="true" className="size-6 text-accent" />
        Cambiar contraseña
      </h2>
      <p className="mb-4 text-sm text-text-muted">Elegí una contraseña que no uses en otros sitios.</p>
      <ChangePasswordForm voice="store" onChanged={changed} />
    </Card>
  );
}
