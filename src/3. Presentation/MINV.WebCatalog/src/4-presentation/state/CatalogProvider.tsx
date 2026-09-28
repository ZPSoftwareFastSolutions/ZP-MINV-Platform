// Carga la instantánea del catálogo desde el origen (API o mock) y entrega los servicios a toda la presentación.
// Estados: cargando (pantalla con logotipo y esqueleto), error (mensaje y «Reintentar») y listo. Una vez listo, la
// refresca al volver a la pestaña y cada 60 s SIN parpadeos: la instantánea anterior sigue en pantalla hasta que llega
// la nueva; si un refresco falla, se conserva la anterior. Estado en memoria; nada de storage.

import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { asStorefrontError, describeStorefrontError, type StorefrontError } from '@/1-domain/storefront/errors';
import type { CatalogSnapshot } from '@/1-domain/storefront/types';
import { createServices, type Sources } from '@/4-presentation/app/container';
import { ServicesProvider } from '@/4-presentation/app/ServicesProvider';
import { CatalogErrorScreen, CatalogLoadingScreen } from '@/4-presentation/components/feedback/CatalogScreens';

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
  private inFlight: Promise<void> | null = null;
  /** Momento (ms) en que llegó la última instantánea; 0 si todavía no hay ninguna. */
  loadedAt = 0;

  constructor(sources: Sources | Promise<Sources>, publish: Publish) {
    this.sources = sources;
    this.publish = publish;
  }

  load(background: boolean): Promise<void> {
    if (this.inFlight) return this.inFlight;
    const run = (async () => {
      try {
        const resolved = await this.sources;
        const snapshot = await resolved.source.load();
        this.loadedAt = Date.now();
        this.publish({ status: 'ready', snapshot, sources: resolved, loadedAt: this.loadedAt });
      } catch (error) {
        const failure = asStorefrontError(error);
        this.publish((current) => (background && current.status === 'ready' ? current : { status: 'error', error: failure, retrying: false }));
      } finally {
        this.inFlight = null;
      }
    })();
    this.inFlight = run;
    return run;
  }
}

export function CatalogProvider({ sources, refreshMs = CATALOG_REFRESH_MS, staleMs = CATALOG_STALE_MS, children }: CatalogProviderProps) {
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

  if (state.status === 'loading') return <CatalogLoadingScreen />;
  if (state.status === 'error' || !services) {
    const error = state.status === 'error' ? state.error : null;
    return (
      <CatalogErrorScreen
        message={error ? describeStorefrontError(error) : 'No pudimos cargar el catálogo.'}
        detail={error && error.kind !== 'network' && error.kind !== 'unknown' ? error.detail : undefined}
        retrying={state.status === 'error' && state.retrying}
        onRetry={retry}
      />
    );
  }
  return <ServicesProvider services={services}>{children}</ServicesProvider>;
}
