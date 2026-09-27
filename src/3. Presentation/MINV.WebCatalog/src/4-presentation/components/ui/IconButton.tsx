import clsx from 'clsx';
import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Link } from 'react-router-dom';

export type IconButtonVariant = 'ghost' | 'subtle' | 'outline' | 'primary';
export type IconButtonSize = 'sm' | 'md' | 'lg';

export interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  /** Texto accesible obligatorio (aria-label y title). */
  label: string;
  icon: ReactNode;
  variant?: IconButtonVariant;
  size?: IconButtonSize;
  /** Contador en una insignia (piezas del armado). Se omite si es 0. */
  badge?: number;
  /** Si se indica, se renderiza como enlace del enrutador. */
  to?: string;
}

const VARIANTS: Record<IconButtonVariant, string> = {
  ghost: 'text-text-muted hover:bg-surface-2 hover:text-text',
  subtle: 'border border-border bg-surface-2 text-text hover:border-border-strong hover:bg-surface-3',
  outline: 'border border-border-strong text-text hover:border-accent hover:text-accent-hover',
  primary: 'bg-primary text-white hover:bg-primary-hover',
};

const SIZES: Record<IconButtonSize, string> = {
  sm: 'size-9 [&_svg]:size-4',
  md: 'size-11 [&_svg]:size-5',
  lg: 'size-12 [&_svg]:size-6',
};

/** Botón de solo ícono (objetivo táctil de 44 px en `md`) con insignia opcional. */
export function IconButton({ label, icon, variant = 'ghost', size = 'md', badge, to, className, type = 'button', ...props }: IconButtonProps) {
  const classes = clsx(
    'relative inline-flex shrink-0 cursor-pointer items-center justify-center rounded-xl transition-colors duration-200',
    'disabled:cursor-not-allowed disabled:opacity-50',
    VARIANTS[variant],
    SIZES[size],
    className,
  );
  const content = (
    <>
      <span aria-hidden="true" className="inline-flex">
        {icon}
      </span>
      {badge != null && badge > 0 && (
        <span
          aria-hidden="true"
          className="absolute -top-1 -right-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-cta px-1 font-display text-xs font-bold text-bg"
        >
          {badge > 99 ? '99+' : badge}
        </span>
      )}
    </>
  );
  if (to) {
    return (
      <Link to={to} aria-label={label} title={label} className={classes}>
        {content}
      </Link>
    );
  }
  return (
    <button type={type} aria-label={label} title={label} className={classes} {...props}>
      {content}
    </button>
  );
}
