import { AlertTriangle, Home, RotateCcw } from 'lucide-react';
import { isRouteErrorResponse, useRouteError } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { Container } from '@/4-presentation/components/ui/Container';
import { NotFoundPage } from './NotFoundPage';

/** Pantalla de error del enrutador (errorElement). Un 404 del enrutador se muestra como la página no encontrada. */
export function RouteErrorPage() {
  const error = useRouteError();
  if (isRouteErrorResponse(error) && error.status === 404) return <NotFoundPage />;
  return (
    <Container className="flex min-h-dvh flex-col items-center justify-center py-16 text-center">
      <span aria-hidden="true" className="flex size-20 items-center justify-center rounded-3xl bg-danger-soft text-danger-text">
        <AlertTriangle className="size-10" />
      </span>
      <h1 className="mt-6 font-display text-3xl font-semibold text-text">Algo salió mal</h1>
      <p className="mt-3 max-w-md text-base text-text-muted">Ocurrió un error inesperado al mostrar esta página. Probá recargarla o volvé al inicio.</p>
      <div className="mt-8 flex flex-wrap justify-center gap-3">
        <Button variant="brand" leftIcon={<RotateCcw />} onClick={() => window.location.reload()}>
          Recargar
        </Button>
        <Button href={ROUTES.home} variant="outline" leftIcon={<Home />}>
          Ir al inicio
        </Button>
      </div>
    </Container>
  );
}
