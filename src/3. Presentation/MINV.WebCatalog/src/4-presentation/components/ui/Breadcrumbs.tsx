import clsx from 'clsx';
import { ChevronRight, Home } from 'lucide-react';
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
  className?: string;
}

/** Migas de pan: Inicio › Componentes › Procesadores. El último elemento lleva aria-current="page". */
export function Breadcrumbs({ items, withHome = true, className }: BreadcrumbsProps) {
  const all: BreadcrumbItem[] = withHome ? [{ label: 'Inicio', to: ROUTES.home }, ...items] : [...items];
  return (
    <nav aria-label="Migas de pan" className={clsx('text-sm text-text-muted', className)}>
      <ol className="flex flex-wrap items-center gap-1">
        {all.map((item, index) => {
          const last = index === all.length - 1;
          const isHome = withHome && index === 0;
          return (
            <li key={`${item.label}-${index}`} className="flex min-w-0 items-center gap-1">
              {index > 0 && <ChevronRight aria-hidden="true" className="size-4 shrink-0 text-text-faint" />}
              {item.to && !last ? (
                <Link
                  to={item.to}
                  className="inline-flex min-h-8 items-center gap-1 truncate rounded-md py-1 transition-colors duration-200 hover:text-accent-hover"
                >
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
