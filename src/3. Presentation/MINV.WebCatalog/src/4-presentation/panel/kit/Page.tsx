// USO · Estructura de TODA pantalla del panel: migas, título, descripción y botones de acción a la derecha (la acción
// principal al final). Debajo va el contenido (FilterBar, Toolbar, DataTable…). Fija el título de la pestaña.
//
//   <Page title="Ventas" description="Ventas de la sucursal activa."
//     breadcrumbs={[{ label: 'Panel', to: '/panel' }, { label: 'Ventas' }]}
//     actions={<><Button variant="outline" leftIcon={<Download />} onClick={exportar}>Exportar CSV</Button>
//               <Button leftIcon={<Plus />} to="/panel/caja">Nueva venta</Button></>}>
//     …
//   </Page>
//
// No agrega márgenes laterales: los pone el esqueleto del panel. En el teléfono los botones bajan debajo del título y
// las migas se reducen a «‹ pantalla anterior». Los enlaces de las migas miden 44 px de alto.

import clsx from 'clsx';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useDocumentMeta } from '@/4-presentation/hooks/useDocumentMeta';

export interface PageCrumb {
  label: string;
  /** Sin `to`, es la pantalla actual. */
  to?: string;
}

export interface PageProps {
  title: string;
  /** Una o dos líneas: para qué sirve la pantalla. */
  description?: ReactNode;
  /** Migas: la última es la pantalla actual (sin `to`). */
  breadcrumbs?: readonly PageCrumb[];
  /** Botones de acción (Button, a la derecha del título). */
  actions?: ReactNode;
  /** Distintivo junto al título (estado, sucursal). */
  badge?: ReactNode;
  children?: ReactNode;
  className?: string;
}

const CRUMB_LINK = 'inline-flex min-h-11 items-center gap-1 rounded-md px-1 transition-colors duration-200 hover:text-accent-hover';

function Crumbs({ items }: { items: readonly PageCrumb[] }) {
  // En el teléfono solo se ve «‹ la pantalla anterior» (la ruta completa queda para lectores de pantalla).
  const parent = [...items].reverse().find((item, index) => index > 0 && item.to);
  return (
    <nav aria-label="Migas de pan" className="-ml-1 text-sm text-text-muted">
      {parent?.to && (
        <Link to={parent.to} className={clsx(CRUMB_LINK, 'font-medium sm:hidden')}>
          <ChevronLeft aria-hidden="true" className="size-4" />
          {parent.label}
        </Link>
      )}
      <ol className="flex flex-wrap items-center gap-0.5 max-sm:sr-only">
        {items.map((item, index) => {
          const last = index === items.length - 1;
          return (
            <li key={`${item.label}-${index}`} className="flex min-w-0 items-center gap-0.5">
              {index > 0 && <ChevronRight aria-hidden="true" className="size-4 shrink-0 text-text-faint" />}
              {item.to && !last ? (
                <Link to={item.to} className={clsx(CRUMB_LINK, 'truncate')}>
                  {item.label}
                </Link>
              ) : (
                <span aria-current={last ? 'page' : undefined} className="truncate px-1 py-1 font-medium text-text">
                  {item.label}
                </span>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

export function Page({ title, description, breadcrumbs, actions, badge, children, className }: PageProps) {
  useDocumentMeta({ title: `${title} · Panel` });
  return (
    <div className={clsx('min-w-0 space-y-5', className)}>
      <header className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div className="min-w-0">
          {breadcrumbs && breadcrumbs.length > 0 && <Crumbs items={breadcrumbs} />}
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="min-w-0 break-words text-2xl sm:text-3xl">{title}</h1>
            {badge}
          </div>
          {description && <div className="mt-1 max-w-3xl text-sm text-text-muted sm:text-base">{description}</div>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2 lg:shrink-0 lg:justify-end [&>*]:max-sm:flex-1">{actions}</div>}
      </header>
      {children}
    </div>
  );
}
