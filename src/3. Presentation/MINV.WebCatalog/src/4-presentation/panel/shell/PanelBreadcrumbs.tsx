// Migas de pan de la barra superior (las arma el esqueleto con el registro: los módulos NO pasan `breadcrumbs` a
// `Page`). Desde 640 px, el camino completo; en el teléfono, solo «‹ pantalla anterior» (el camino completo se oculta del
// todo: si quedara solo para lectores de pantalla, sus enlaces recibirían el foco sin verse). Los enlaces miden 44 px.

import clsx from 'clsx';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { Link, useLocation } from 'react-router-dom';
import { usePanelRegistry } from '../registry';
import { crumbsFor } from './crumbs';

const LINK = 'inline-flex min-h-11 min-w-11 items-center justify-center gap-1 rounded-md px-1 transition-colors duration-200 hover:text-accent-hover';

export function PanelBreadcrumbs({ className }: { className?: string }) {
  const registry = usePanelRegistry();
  const { pathname } = useLocation();
  const crumbs = crumbsFor(registry, pathname);
  // La miga anterior con enlace (la sección no tiene): «‹ Inicio», «‹ Ventas».
  const parent = [...crumbs].reverse().find((crumb, index) => index > 0 && crumb.to);
  return (
    <nav aria-label="Migas de pan" className={clsx('min-w-0 text-sm text-text-muted', className)}>
      {parent?.to && (
        <Link to={parent.to} className={clsx(LINK, 'max-w-full font-medium sm:hidden')}>
          <ChevronLeft aria-hidden="true" className="size-4 shrink-0" />
          <span className="truncate">{parent.label}</span>
        </Link>
      )}
      <ol className="flex min-w-0 items-center gap-0.5 max-sm:hidden">
        {crumbs.map((crumb, index) => {
          const last = index === crumbs.length - 1;
          // La sección (sin enlace) se omite en pantallas medianas: el camino entra en una sola línea.
          const section = !crumb.to && !last;
          return (
            <li key={`${crumb.label}-${index}`} className={clsx('flex min-w-0 items-center gap-0.5', section && 'max-xl:hidden', last ? 'shrink' : 'shrink-0')}>
              {index > 0 && <ChevronRight aria-hidden="true" className="size-4 shrink-0 text-text-faint" />}
              {crumb.to && !last ? (
                <Link to={crumb.to} className={clsx(LINK, 'truncate')}>
                  {crumb.label}
                </Link>
              ) : (
                <span aria-current={last ? 'page' : undefined} className={clsx('truncate px-1 py-1', last ? 'font-medium text-text' : 'text-text-muted')}>
                  {crumb.label}
                </span>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
