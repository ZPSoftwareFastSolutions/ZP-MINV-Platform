// USO · Botón grande con ícono, título y descripción: las funciones del rol en el tablero («Inicio») y los accesos de
// una sección. Con `to` es un enlace del enrutador; con `onClick`, un botón.
//
//   <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
//     <ActionButton icon={<ShoppingCart />} title="Abrir caja" description="Cobre ventas y emita facturas." to="/panel/caja" />
//     <ActionButton icon={<PackageSearch />} title="Consultar stock" description="Existencias por sucursal." to="/panel/stock"
//       badge={<StatusBadge tone="warning">7 bajos</StatusBadge>} />
//   </div>
//
// El nombre accesible es el título; la descripción se lee como descripción. Ocupa todo el ancho de su celda.

import clsx from 'clsx';
import { ArrowRight } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { Link } from 'react-router-dom';

export interface ActionButtonProps {
  title: string;
  description?: string;
  icon: ReactNode;
  /** Ruta del panel a la que lleva. */
  to?: string;
  onClick?: () => void;
  /** 'primary' (violeta, por defecto) · 'accent' (cian) · 'neutral'. */
  tone?: 'primary' | 'accent' | 'neutral';
  /** Distintivo a la derecha del título (pendientes, alertas). */
  badge?: ReactNode;
  disabled?: boolean;
  className?: string;
}

const ICON_TONES = {
  primary: 'bg-primary-soft text-primary-text',
  accent: 'bg-accent-soft text-accent-hover',
  neutral: 'bg-surface-3 text-text',
} as const;

export function ActionButton({ title, description, icon, to, onClick, tone = 'primary', badge, disabled = false, className }: ActionButtonProps) {
  const titleId = useId();
  const badgeId = useId();
  const descriptionId = useId();
  const classes = clsx(
    'group flex min-h-24 w-full min-w-0 items-start gap-4 rounded-card border border-border bg-surface p-4 text-left shadow-card transition-[border-color,background-color,box-shadow] duration-200',
    disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer hover:border-border-strong hover:bg-surface-2 hover:shadow-glow',
    className,
  );
  const content = (
    <>
      <span aria-hidden="true" className={clsx('flex size-12 shrink-0 items-center justify-center rounded-2xl [&_svg]:size-6', ICON_TONES[tone])}>
        {icon}
      </span>
      <span className="min-w-0 flex-1">
        <span className="flex flex-wrap items-center gap-2">
          <span id={titleId} className="font-display text-lg font-semibold text-text">
            {title}
          </span>
          {badge && <span id={badgeId}>{badge}</span>}
        </span>
        {description && (
          <span id={descriptionId} className="mt-0.5 block text-sm text-text-muted">
            {description}
          </span>
        )}
      </span>
      <ArrowRight aria-hidden="true" className="mt-1 size-5 shrink-0 text-text-faint transition-transform duration-200 group-hover:translate-x-0.5 group-hover:text-accent" />
    </>
  );
  // El nombre es el título (y el distintivo); la descripción se lee aparte.
  const labelledBy = badge ? `${titleId} ${badgeId}` : titleId;
  const describedBy = description ? descriptionId : undefined;
  if (to && !disabled) {
    return (
      <Link to={to} aria-labelledby={labelledBy} aria-describedby={describedBy} className={classes}>
        {content}
      </Link>
    );
  }
  return (
    <button type="button" aria-labelledby={labelledBy} aria-describedby={describedBy} disabled={disabled} onClick={onClick} className={classes}>
      {content}
    </button>
  );
}
