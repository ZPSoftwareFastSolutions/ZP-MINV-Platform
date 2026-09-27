import clsx from 'clsx';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';

export interface LinkChipProps {
  to: string;
  selected?: boolean;
  count?: number;
  icon?: ReactNode;
  size?: 'sm' | 'md';
  className?: string;
  children: ReactNode;
}

/** Chip de navegación (subcategorías, sugerencias): mismo aspecto que `Chip`, pero es un enlace del enrutador. */
export function LinkChip({ to, selected = false, count, icon, size = 'md', className, children }: LinkChipProps) {
  return (
    <Link
      to={to}
      aria-current={selected ? 'page' : undefined}
      className={clsx(
        'inline-flex items-center gap-1.5 whitespace-nowrap rounded-full border font-medium transition-colors duration-200',
        size === 'sm' ? 'h-9 px-3 text-sm max-sm:h-11 [&_svg]:size-4' : 'h-11 px-4 text-sm [&_svg]:size-4',
        selected
          ? 'border-accent/60 bg-accent-soft text-accent-hover hover:border-accent'
          : 'border-border bg-surface-2 text-text-muted hover:border-border-strong hover:text-text',
        className,
      )}
    >
      {icon && (
        <span aria-hidden="true" className="inline-flex">
          {icon}
        </span>
      )}
      <span>{children}</span>
      {count != null && (
        <span className={clsx('rounded-full px-1.5 text-xs tabular-nums', selected ? 'bg-accent/20' : 'bg-surface-3')}>{count}</span>
      )}
    </Link>
  );
}
