// Pantallas de la carga inicial del catálogo (antes de que exista el enrutador): esqueleto con el logotipo mientras
// llega la instantánea, y el error con «Reintentar». No usan <Link>: se dibujan fuera del RouterProvider.

import { CloudOff, RotateCcw } from 'lucide-react';
import { Button } from '@/4-presentation/components/ui/Button';
import { Skeleton } from '@/4-presentation/components/ui/Skeleton';
import { STORE } from '@/shared/constants';

function StaticLogo() {
  return (
    <div className="flex items-center gap-2.5">
      <img src="/images/logo-256.png" alt="" width={40} height={40} className="size-10 rounded-xl" />
      <span className="flex flex-col leading-none">
        <span className="font-display text-lg font-bold tracking-tight text-gradient-brand">{STORE.wordmark}</span>
        <span className="text-[0.625rem] font-semibold uppercase tracking-[0.32em] text-text-muted">Gaming</span>
      </span>
    </div>
  );
}

/** Esqueleto de la portada (cabecera, hero y una grilla de tarjetas) mientras se carga el catálogo. */
export function CatalogLoadingScreen() {
  return (
    <div className="min-h-dvh" data-testid="catalogo-cargando">
      <div className="border-b border-border bg-bg/85">
        <div className="mx-auto flex h-16 max-w-7xl items-center justify-between gap-4 px-4 sm:px-6">
          <StaticLogo />
          <Skeleton className="hidden h-11 w-full max-w-2xl md:block" />
          <div className="flex items-center gap-2">
            <Skeleton className="h-11 w-32 max-sm:hidden" />
            <Skeleton className="size-11 rounded-xl" />
          </div>
        </div>
      </div>
      <div className="mx-auto max-w-7xl px-4 py-10 sm:px-6 lg:py-16">
        <p role="status" aria-live="polite" className="sr-only">
          Cargando el catálogo de {STORE.shortName}…
        </p>
        <div className="grid gap-10 lg:grid-cols-[1.05fr_0.95fr] lg:items-center" aria-hidden="true">
          <div className="space-y-5">
            <Skeleton className="h-6 w-56 rounded-full" />
            <Skeleton className="h-12 w-full max-w-xl" />
            <Skeleton className="h-12 w-4/5 max-w-lg" />
            <Skeleton className="h-5 w-full max-w-md" />
            <div className="flex gap-3 pt-2">
              <Skeleton className="h-12 w-40 rounded-xl" />
              <Skeleton className="h-12 w-36 rounded-xl" />
            </div>
          </div>
          <div className="mx-auto grid w-full max-w-md grid-cols-[1.3fr_1fr] gap-3 sm:gap-4">
            <Skeleton className="row-span-2 aspect-[3/4] w-full rounded-2xl" />
            <Skeleton className="aspect-square w-full rounded-2xl" />
            <Skeleton className="aspect-square w-full rounded-2xl" />
          </div>
        </div>
        <div className="mt-16 grid grid-cols-2 gap-4 md:grid-cols-4" aria-hidden="true">
          {Array.from({ length: 4 }, (_, index) => (
            <div key={index} className="rounded-card border border-border bg-surface p-3">
              <Skeleton className="aspect-square w-full rounded-xl" />
              <Skeleton className="mt-3 h-3 w-1/2" />
              <Skeleton className="mt-2 h-4 w-full" />
              <Skeleton className="mt-2 h-4 w-3/4" />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

export interface CatalogErrorScreenProps {
  message: string;
  /** Detalle técnico corto del servidor (si lo hubo). */
  detail?: string;
  retrying: boolean;
  onRetry: () => void;
}

export function CatalogErrorScreen({ message, detail, retrying, onRetry }: CatalogErrorScreenProps) {
  return (
    <div className="flex min-h-dvh flex-col" data-testid="catalogo-error">
      <div className="border-b border-border bg-bg/85">
        <div className="mx-auto flex h-16 max-w-7xl items-center px-4 sm:px-6">
          <StaticLogo />
        </div>
      </div>
      <div className="mx-auto flex max-w-2xl flex-1 flex-col items-center justify-center px-4 py-16 text-center">
        <span aria-hidden="true" className="flex size-20 items-center justify-center rounded-3xl bg-danger-soft text-danger-text">
          <CloudOff className="size-10" />
        </span>
        <h1 className="mt-6 font-display text-3xl font-semibold text-text">No pudimos cargar el catálogo</h1>
        <p role="alert" className="mt-3 max-w-md text-base text-text-muted">
          {message}
        </p>
        {detail && detail !== message && <p className="mt-2 max-w-md text-sm text-text-faint">{detail}</p>}
        <div className="mt-8">
          <Button variant="brand" size="lg" leftIcon={<RotateCcw />} loading={retrying} onClick={onRetry}>
            Reintentar
          </Button>
        </div>
        <p className="mt-6 text-sm text-text-faint">
          Si el problema sigue, escribinos por WhatsApp al {STORE.whatsapp} o llamá al {STORE.phone}.
        </p>
      </div>
    </div>
  );
}
