// Carga la instantánea del catálogo desde el origen (API o mock) y entrega los servicios a toda la presentación.
// Estados: cargando (esqueleto), error (mensaje y «Reintentar») y listo. Una vez listo, la refresca al volver a la pestaña
// y cada 60 s SIN parpadeos: la instantánea anterior sigue en pantalla hasta que llega la nueva; si un refresco falla, se
// conserva la anterior. Estado en memoria; nada de storage.
//
// V7 · W3b: la carga y la espera están separadas, para que la tienda caída no bloquee el resto del sitio.
// - `CatalogStateProvider` carga y refresca la instantánea y publica su estado (`CatalogStatusContext`) y, cuando hay una,
//   los servicios (`ServicesContext`). NO bloquea a sus hijos: la aplicación lo pone arriba del enrutador y así las
//   pantallas de la sesión y el panel se dibujan aunque el catálogo no haya llegado o haya fallado.
// - `CatalogGate` espera el catálogo: mientras carga o si falló muestra el esqueleto o el error con «Reintentar» y, cuando
//   está listo, su contenido. En la tabla de rutas envuelve SOLO las páginas de la tienda (`variant="body"`, dentro de la
//   estructura de la tienda); sin hijos dibuja la ruta anidada (`<Outlet />`).
// - `CatalogProvider` = los dos juntos a pantalla completa, como en la V6 (lo usan las pruebas de componentes).

import { useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Outlet } from 'react-router-dom';
import { asStorefrontError, describeStorefrontError, type StorefrontError } from '@/1-domain/storefront/errors';
import type { CatalogSnapshot } from '@/1-domain/storefront/types';
import { createServices, type Sources } from '@/4-presentation/app/container';
import { ServicesContext } from '@/4-presentation/app/ServicesContext';
import { CatalogErrorBody, CatalogErrorScreen, CatalogLoadingBody, CatalogLoadingScreen } from '@/4-presentation/components/feedback/CatalogScreens';
import { CatalogStatusContext, type CatalogStatus } from './CatalogStatusContext';

/** Cada cuánto se refresca la instantánea mientras la pestaña está visible. */
export const CATALOG_REFRESH_MS = 60_000;
/** Al volver a la pestaña, se refresca si la instantánea tiene más de este tiempo. */
export const CATALOG_STALE_MS = 15_000;

export interface CatalogProviderProps {
  /** Origen ya elegido (o su promesa: el mock se carga en un fragmento aparte). */
  sources: Sources | Promise<Sources>;
  refreshMs?: number;
  staleMs?: number;
  children: ReactNode;
}

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; error: StorefrontError; retrying: boolean }
  | { status: 'ready'; snapshot: CatalogSnapshot; sources: Sources; loadedAt: number };

type Publish = (update: LoadState | ((current: LoadState) => LoadState)) => void;

/**
 * Cargador de la instantánea (fuera de React): una sola petición en vuelo a la vez, recuerda cuándo llegó la última y
 * publica el resultado. `background` no muestra ni el esqueleto ni el error: conserva lo que ya se ve.
 */
class CatalogLoader {
  private readonly sources: Sources | Promise<Sources>;
  private readonly publish: Publish;
  private inFlight: Promise<boolean> | null = null;
  /** Momento (ms) en que llegó la última instantánea; 0 si todavía no hay ninguna. */
  loadedAt = 0;

  constructor(sources: Sources | Promise<Sources>, publish: Publish) {
    this.sources = sources;
    this.publish = publish;
  }

  /** Devuelve si llegó una instantánea nueva (falso: la carga falló y, en segundo plano, se conserva la anterior). */
  load(background: boolean): Promise<boolean> {
    if (this.inFlight) return this.inFlight;
    const run = (async () => {
      try {
        const resolved = await this.sources;
        const snapshot = await resolved.source.load();
        this.loadedAt = Date.now();
        this.publish({ status: 'ready', snapshot, sources: resolved, loadedAt: this.loadedAt });
        return true;
      } catch (error) {
        const failure = asStorefrontError(error);
        this.publish((current) => (background && current.status === 'ready' ? current : { status: 'error', error: failure, retrying: false }));
        return false;
      } finally {
        this.inFlight = null;
      }
    })();
    this.inFlight = run;
    return run;
  }
}

/** Carga y refresca el catálogo sin bloquear a sus hijos (ver el comentario del archivo). */
export function CatalogStateProvider({ sources, refreshMs = CATALOG_REFRESH_MS, staleMs = CATALOG_STALE_MS, children }: CatalogProviderProps) {
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  // El cargador se crea una vez con el origen inicial (setState es estable); cambiar `sources` después no se contempla.
  const [loader] = useState(() => new CatalogLoader(sources, setState));

  // Carga inicial (el estado ya es «cargando»).
  useEffect(() => {
    void loader.load(false);
  }, [loader]);

  // Frescura: cada `refreshMs` con la pestaña visible, y al volver a la pestaña si la instantánea ya envejeció.
  useEffect(() => {
    if (state.status !== 'ready') return;
    const tick = () => {
      if (typeof document === 'undefined' || document.visibilityState === 'visible') void loader.load(true);
    };
    const interval = setInterval(tick, refreshMs);
    const onVisible = () => {
      if (document.visibilityState === 'visible' && Date.now() - loader.loadedAt >= staleMs) void loader.load(true);
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      clearInterval(interval);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [state.status, refreshMs, staleMs, loader]);

  const refresh = useCallback(() => loader.load(true), [loader]);

  /** «Reintentar» desde la pantalla de error: marca el botón como ocupado y vuelve a cargar. */
  const retry = useCallback(() => {
    setState((current) => (current.status === 'error' ? { ...current, retrying: true } : current));
    void loader.load(false);
  }, [loader]);

  const services = useMemo(
    () => (state.status === 'ready' ? createServices(state.snapshot, state.sources, refresh) : null),
    [state, refresh],
  );

  const status = useMemo<CatalogStatus>(
    () => ({
      status: state.status,
      error: state.status === 'error' ? state.error : null,
      retrying: state.status === 'error' && state.retrying,
      retry,
    }),
    [state, retry],
  );

  return (
    <CatalogStatusContext.Provider value={status}>
      <ServicesContext.Provider value={services}>{children}</ServicesContext.Provider>
    </CatalogStatusContext.Provider>
  );
}

export interface CatalogGateProps {
  /** Qué mostrar con el catálogo listo; sin hijos, la ruta anidada (`<Outlet />`). */
  children?: ReactNode;
  /** `screen`: esqueleto y error a pantalla completa · `body` (por defecto): solo el contenido, dentro de la tienda. */
  variant?: 'screen' | 'body';
}

/** Espera el catálogo: esqueleto mientras carga, error con «Reintentar» si falló y el contenido cuando está listo. */
export function CatalogGate({ children, variant = 'body' }: CatalogGateProps) {
  const services = useContext(ServicesContext);
  const catalog = useContext(CatalogStatusContext);
  if (services) return <>{children ?? <Outlet />}</>;
  if (!catalog || catalog.status !== 'error') return variant === 'screen' ? <CatalogLoadingScreen /> : <CatalogLoadingBody />;
  const error = catalog.error;
  const props = {
    message: error ? describeStorefrontError(error) : 'No pudimos cargar el catálogo.',
    detail: error && error.kind !== 'network' && error.kind !== 'unknown' ? error.detail : undefined,
    retrying: catalog.retrying,
    onRetry: catalog.retry,
  };
  return variant === 'screen' ? <CatalogErrorScreen {...props} /> : <CatalogErrorBody {...props} />;
}

/** Carga el catálogo y espera a tenerlo antes de dibujar a sus hijos, a pantalla completa (como en la V6). */
export function CatalogProvider({ children, ...props }: CatalogProviderProps) {
  return (
    <CatalogStateProvider {...props}>
      <CatalogGate variant="screen">{children}</CatalogGate>
    </CatalogStateProvider>
  );
}
