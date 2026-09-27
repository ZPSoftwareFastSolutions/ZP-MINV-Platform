import clsx from 'clsx';
import { PackageSearch } from 'lucide-react';
import type { ReactNode } from 'react';

export interface EmptyStateProps {
  icon?: ReactNode;
  title: string;
  description?: string;
  /** Botones o enlaces de acción. */
  children?: ReactNode;
  size?: 'sm' | 'md';
  className?: string;
}

/** Estado vacío (sin resultados, armado vacío) con ícono, texto y acciones. */
export function EmptyState({ icon, title, description, children, size = 'md', className }: EmptyStateProps) {
  return (
    <div
      role="status"
      className={clsx(
        'flex flex-col items-center justify-center text-center',
        size === 'sm' ? 'gap-2 px-4 py-8' : 'gap-3 rounded-card border border-dashed border-border-strong bg-surface/60 px-6 py-14',
        className,
      )}
    >
      <span
        aria-hidden="true"
        className={clsx(
          'flex items-center justify-center rounded-2xl bg-surface-2 text-accent [&_svg]:size-7',
          size === 'sm' ? 'size-12' : 'size-16 [&_svg]:size-8',
        )}
      >
        {icon ?? <PackageSearch />}
      </span>
      <p className={clsx('font-display font-semibold text-text', size === 'sm' ? 'text-base' : 'text-xl')}>{title}</p>
      {description && <p className="max-w-md text-sm text-text-muted">{description}</p>}
      {children && <div className="mt-2 flex flex-wrap items-center justify-center gap-3">{children}</div>}
    </div>
  );
}
