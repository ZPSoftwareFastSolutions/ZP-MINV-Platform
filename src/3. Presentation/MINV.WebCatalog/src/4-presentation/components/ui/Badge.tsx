import clsx from 'clsx';
import type { HTMLAttributes, ReactNode } from 'react';

export type BadgeTone = 'oferta' | 'nuevo' | 'destacado' | 'agotado' | 'aviso' | 'exito' | 'condicion' | 'neutral';
export type BadgeVariant = 'soft' | 'solid' | 'outline';

export interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  tone?: BadgeTone;
  variant?: BadgeVariant;
  size?: 'sm' | 'md';
  icon?: ReactNode;
}

const SOFT: Record<BadgeTone, string> = {
  oferta: 'border-cta/40 bg-cta/15 text-cta-hover',
  nuevo: 'border-accent/40 bg-accent-soft text-accent-hover',
  destacado: 'border-primary/50 bg-primary-soft text-primary-text',
  agotado: 'border-danger/40 bg-danger-soft text-danger-text',
  aviso: 'border-warning/40 bg-warning-soft text-warning-text',
  exito: 'border-success/40 bg-success-soft text-success-text',
  condicion: 'border-border-strong bg-surface-3 text-text-muted',
  neutral: 'border-border bg-surface-2 text-text-muted',
};

const SOLID: Record<BadgeTone, string> = {
  oferta: 'border-transparent bg-cta text-bg',
  nuevo: 'border-transparent bg-accent text-bg',
  destacado: 'border-transparent bg-primary text-white',
  agotado: 'border-transparent bg-danger text-bg',
  aviso: 'border-transparent bg-warning text-bg',
  exito: 'border-transparent bg-success text-bg',
  condicion: 'border-transparent bg-surface-3 text-text',
  neutral: 'border-transparent bg-surface-3 text-text',
};

const OUTLINE: Record<BadgeTone, string> = {
  oferta: 'border-cta/60 text-cta-hover',
  nuevo: 'border-accent/60 text-accent-hover',
  destacado: 'border-primary/70 text-primary-text',
  agotado: 'border-danger/60 text-danger-text',
  aviso: 'border-warning/60 text-warning-text',
  exito: 'border-success/60 text-success-text',
  condicion: 'border-border-strong text-text-muted',
  neutral: 'border-border text-text-muted',
};

const STYLES: Record<BadgeVariant, Record<BadgeTone, string>> = { soft: SOFT, solid: SOLID, outline: OUTLINE };

/** Insignia de estado: oferta (rosa), nuevo (cian), destacado (violeta), agotado, aviso, éxito, condición, neutral. */
export function Badge({ tone = 'neutral', variant = 'soft', size = 'md', icon, className, children, ...props }: BadgeProps) {
  return (
    <span
      className={clsx(
        'inline-flex items-center gap-1 whitespace-nowrap rounded-full border font-semibold uppercase tracking-wide',
        size === 'sm' ? 'h-5 px-2 text-[0.6875rem] [&_svg]:size-3' : 'h-6 px-2.5 text-xs [&_svg]:size-3.5',
        STYLES[variant][tone],
        className,
      )}
      {...props}
    >
      {icon && (
        <span aria-hidden="true" className="inline-flex">
          {icon}
        </span>
      )}
      {children}
    </span>
  );
}
