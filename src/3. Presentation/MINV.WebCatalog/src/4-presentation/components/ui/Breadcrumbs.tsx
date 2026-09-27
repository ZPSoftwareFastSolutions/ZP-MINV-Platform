import clsx from 'clsx';
import { ChevronLeft, ChevronRight, Home } from 'lucide-react';
import { Link } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';

export interface BreadcrumbItem {
  label: string;
  /** Sin `to`, el elemento es la página actual. */
  to?: string;
}

export interface BreadcrumbsProps {
  items: readonly BreadcrumbItem[];
  /** Antepone «Inicio» (por defecto sí). */
  withHome?: boolean;
  /**
   * En pantallas angostas muestra solo «‹ {padre inmediato}» (la ruta completa queda para lectores de pantalla y
   * desde sm): evita tres líneas de migas antes del contenido en móvil.
   */
  compactOnMobile?: boolean;
  className?: string;
}

const LINK = 'inline-flex min-h-11 items-center gap-1 rounded-md py-1 transition-colors duration-200 hover:text-accent-hover sm:min-h-8';

/** Migas de pan: Inicio › Componentes › Procesadores. El último elemento lleva aria-current="page". */
export function Breadcrumbs({ items, withHome = true, compactOnMobile = false, className }: BreadcrumbsProps) {
  const all: BreadcrumbItem[] = withHome ? [{ label: 'Inicio', to: ROUTES.home }, ...items] : [...items];
  const parent = [...all].reverse().find((item, index) => index > 0 && item.to);
  return (
    <nav aria-label="Migas de pan" className={clsx('text-sm text-text-muted', className)}>
      {compactOnMobile && parent?.to && (
        <Link to={parent.to} className={clsx(LINK, 'font-medium sm:hidden')}>
          <ChevronLeft aria-hidden="true" className="size-4" />
          {parent.label}
        </Link>
      )}
      <ol className={clsx('flex flex-wrap items-center gap-1', compactOnMobile && 'max-sm:sr-only')}>
        {all.map((item, index) => {
          const last = index === all.length - 1;
          const isHome = withHome && index === 0;
          return (
            <li key={`${item.label}-${index}`} className="flex min-w-0 items-center gap-1">
              {index > 0 && <ChevronRight aria-hidden="true" className="size-4 shrink-0 text-text-faint" />}
              {item.to && !last ? (
                <Link to={item.to} className={clsx(LINK, 'truncate', isHome && 'min-w-11 justify-center sm:min-w-0 sm:justify-start')}>
                  {isHome && <Home aria-hidden="true" className="size-4" />}
                  <span className={clsx(isHome && 'sr-only sm:not-sr-only')}>{item.label}</span>
                </Link>
              ) : (
                <span aria-current={last ? 'page' : undefined} className="truncate py-1 font-medium text-text">
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
