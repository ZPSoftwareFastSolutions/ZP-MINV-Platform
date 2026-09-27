import clsx from 'clsx';
import type { HTMLAttributes } from 'react';

export interface CardProps extends HTMLAttributes<HTMLElement> {
  as?: 'div' | 'article' | 'section' | 'li';
  /** Resalta el borde y la sombra al pasar el mouse (sin mover el layout). */
  interactive?: boolean;
  padding?: 'none' | 'sm' | 'md' | 'lg';
  /** Superficie más elevada (segunda capa). */
  elevated?: boolean;
}

const PADDING = { none: '', sm: 'p-3', md: 'p-4 sm:p-5', lg: 'p-6 sm:p-8' } as const;

/** Tarjeta base del sistema: superficie, borde sutil, esquinas `rounded-card` y sombra. */
export function Card({ as: Tag = 'div', interactive = false, padding = 'md', elevated = false, className, ...props }: CardProps) {
  return (
    <Tag
      className={clsx(
        'rounded-card border border-border shadow-card',
        elevated ? 'bg-surface-2' : 'bg-surface',
        interactive && 'transition-[border-color,box-shadow] duration-200 hover:border-border-strong hover:shadow-glow',
        PADDING[padding],
        className,
      )}
      {...props}
    />
  );
}
