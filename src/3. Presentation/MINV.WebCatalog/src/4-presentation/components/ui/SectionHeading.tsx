import clsx from 'clsx';
import { ArrowRight } from 'lucide-react';
import { Link } from 'react-router-dom';

export interface SectionHeadingProps {
  title: string;
  subtitle?: string;
  /** Etiqueta pequeña sobre el título («Lo más vendido»). */
  eyebrow?: string;
  /** Enlace «Ver todo» a la derecha. */
  action?: { label: string; to: string };
  as?: 'h1' | 'h2' | 'h3';
  id?: string;
  align?: 'start' | 'center';
  className?: string;
}

/** Título de sección con subtítulo y enlace «Ver todo». */
export function SectionHeading({ title, subtitle, eyebrow, action, as: Tag = 'h2', id, align = 'start', className }: SectionHeadingProps) {
  return (
    <div
      className={clsx(
        'flex gap-4',
        align === 'center' ? 'flex-col items-center text-center' : 'flex-col sm:flex-row sm:items-end sm:justify-between',
        className,
      )}
    >
      <div className="min-w-0">
        {eyebrow && <p className="mb-1 text-xs font-semibold uppercase tracking-[0.2em] text-accent">{eyebrow}</p>}
        <Tag id={id} className={clsx('font-display font-semibold tracking-tight text-text', Tag === 'h1' ? 'text-3xl sm:text-4xl' : 'text-2xl sm:text-3xl')}>
          {title}
        </Tag>
        {subtitle && <p className="mt-1 max-w-2xl text-base text-text-muted">{subtitle}</p>}
      </div>
      {action && (
        <Link
          to={action.to}
          className="inline-flex h-11 shrink-0 items-center gap-1 self-start text-sm font-semibold text-accent transition-colors duration-200 hover:text-accent-hover sm:self-auto"
        >
          {action.label}
          <ArrowRight aria-hidden="true" className="size-4" />
        </Link>
      )}
    </div>
  );
}
