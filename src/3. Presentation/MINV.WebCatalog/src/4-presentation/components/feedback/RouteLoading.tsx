import { LoaderCircle } from 'lucide-react';

/** Se muestra mientras se descarga la pantalla con la que se abrió el sitio (las pantallas de la V7 cargan aparte). */
export function RouteLoading() {
  return (
    <div className="flex min-h-dvh flex-col items-center justify-center gap-3 px-4 text-center" data-testid="ruta-cargando">
      <LoaderCircle aria-hidden="true" className="size-8 animate-spin text-accent" />
      <p role="status" aria-live="polite" className="text-sm text-text-muted">
        Cargando…
      </p>
    </div>
  );
}
