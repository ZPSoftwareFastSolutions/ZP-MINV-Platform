// PUNTO DE MONTAJE DEL PANEL (`/panel/*`) · componente PROVISIONAL del paquete W1.
//
// El paquete W3 reemplaza el contenido de este archivo por el panel real (esqueleto en `panel/shell/`, componentes en
// `panel/kit/` y módulos en `panel/modules/<módulo>/module.tsx`), conservando el nombre del archivo y la exportación
// `PanelRoot`: el enrutador lo carga con carga diferida y ya lo protege con la guarda (solo sesión del personal).
// Dentro puede usar rutas propias (`<Routes>`) relativas a `/panel`.
//
// Textos en español neutro (es la parte del personal).

import { Construction, LogOut, Store } from 'lucide-react';
import { activeBranch } from '@/1-domain/auth/permissions';
import { ROUTES } from '@/4-presentation/app/routes';
import { roleName } from '@/4-presentation/app/contract';
import { Logo } from '@/4-presentation/components/layout/Logo';
import { Badge } from '@/4-presentation/components/ui/Badge';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { Container } from '@/4-presentation/components/ui/Container';
import { useDocumentMeta } from '@/4-presentation/hooks/useDocumentMeta';
import { useLogout } from '@/4-presentation/hooks/useLogout';
import { useSession } from '@/4-presentation/hooks/useSession';

export function PanelRoot() {
  useDocumentMeta({ title: 'Panel' });
  const { session } = useSession();
  const { logout, leaving } = useLogout('panel');
  if (!session) return null;
  const branch = activeBranch(session);

  return (
    <div className="flex min-h-dvh flex-col">
      <a href="#contenido" className="skip-link">
        Saltar al contenido
      </a>
      <header className="border-b border-border bg-bg/85">
        <Container className="flex min-h-16 flex-wrap items-center justify-between gap-2 py-2">
          <Logo compactOnNarrow />
          <div className="flex flex-wrap items-center gap-2">
            <Button to={ROUTES.home} variant="ghost" leftIcon={<Store />}>
              Ir a la tienda
            </Button>
            <Button variant="outline" leftIcon={<LogOut />} loading={leaving} onClick={() => void logout()}>
              Cerrar sesión
            </Button>
          </div>
        </Container>
      </header>
      <main id="contenido" tabIndex={-1} className="flex-1 outline-none">
        <Container className="py-10">
          <p className="mb-2 text-xs font-semibold uppercase tracking-[0.2em] text-accent">Panel del personal</p>
          <h1 className="text-3xl sm:text-4xl">Hola, {session.displayName}</h1>
          <div className="mt-3 flex flex-wrap items-center gap-2 text-sm text-text-muted">
            <span>{session.company}</span>
            {session.roles.map((role) => (
              <Badge key={role} tone="destacado">
                {roleName(role)}
              </Badge>
            ))}
          </div>
          {branch && (
            <p className="mt-2 text-sm text-text-muted">
              Sucursal activa: <span className="font-medium text-text">{branch.name}</span>
            </p>
          )}

          <Card padding="lg" className="mt-8 max-w-2xl" data-testid="panel-en-construccion">
            <span aria-hidden="true" className="flex size-14 items-center justify-center rounded-2xl bg-surface-2 text-accent">
              <Construction className="size-7" />
            </span>
            <h2 className="mt-4 text-2xl">Panel en construcción</h2>
            <p className="mt-2 text-base text-text-muted">
              La sesión ya está iniciada. Las funciones del panel (ventas, inventario, compras, reservas y administración) se habilitan en esta misma dirección.
            </p>
          </Card>
        </Container>
      </main>
    </div>
  );
}
