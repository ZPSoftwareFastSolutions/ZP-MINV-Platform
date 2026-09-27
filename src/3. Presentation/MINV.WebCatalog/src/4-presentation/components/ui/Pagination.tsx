import clsx from 'clsx';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { paginationRange } from './paginationRange';

export interface PaginationProps {
  page: number;
  pageCount: number;
  /** Enlace de cada página (recomendado: conserva los filtros en la URL). */
  getHref?: (page: number) => string;
  /** Alternativa sin enlaces (estado local). */
  onChange?: (page: number) => void;
  siblingCount?: number;
  className?: string;
}

const ITEM = 'inline-flex size-11 cursor-pointer items-center justify-center rounded-xl text-sm font-semibold transition-colors duration-200';

/** Paginación accesible con enlaces (o botones) y elipsis. Se oculta con una sola página. */
export function Pagination({ page, pageCount, getHref, onChange, siblingCount = 1, className }: PaginationProps) {
  if (pageCount <= 1) return null;
  const render = (target: number, label: string, children: ReactNode, current = false) => {
    const disabled = target < 1 || target > pageCount;
    const classes = clsx(
      ITEM,
      current ? 'bg-primary text-white' : 'text-text-muted hover:bg-surface-2 hover:text-text',
      disabled && 'pointer-events-none opacity-40',
    );
    if (getHref && !disabled) {
      return (
        <Link to={getHref(target)} aria-label={label} aria-current={current ? 'page' : undefined} className={classes}>
          {children}
        </Link>
      );
    }
    return (
      <button
        type="button"
        aria-label={label}
        aria-current={current ? 'page' : undefined}
        disabled={disabled}
        onClick={() => onChange?.(target)}
        className={classes}
      >
        {children}
      </button>
    );
  };

  return (
    <nav aria-label="Paginación" className={clsx('flex items-center justify-center gap-1', className)}>
      {render(page - 1, 'Página anterior', <ChevronLeft aria-hidden="true" className="size-5" />)}
      {paginationRange(page, pageCount, siblingCount).map((token, index) =>
        token === 'gap' ? (
          <span key={`gap-${index}`} aria-hidden="true" className="inline-flex size-11 items-center justify-center text-text-faint">
            …
          </span>
        ) : (
          <span key={token}>{render(token, `Página ${token}`, token, token === page)}</span>
        ),
      )}
      {render(page + 1, 'Página siguiente', <ChevronRight aria-hidden="true" className="size-5" />)}
    </nav>
  );
}
