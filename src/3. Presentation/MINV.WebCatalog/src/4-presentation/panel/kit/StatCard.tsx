// USO · Tarjeta de indicador (ventas del día, stock bajo, reservas activas). Regla P-10: los indicadores van DENTRO de
// un Collapsible «Ver …», nunca a la vista al entrar.
//
//   <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
//     <StatCard label="Ventas de hoy" value={formatMoney(12345)} hint="34 ventas" icon={<Receipt />}
//       trend={{ direction: 'up', text: '8 % más que ayer' }} />
//     <StatCard label="Productos con stock bajo" value="7" tone="warning" icon={<TriangleAlert />} />
//   </div>
//
// `trend.positive` dice si la subida es buena (por defecto: subir es bueno); el color nunca va solo (flecha y texto).

import clsx from 'clsx';
import { Minus, TrendingDown, TrendingUp } from 'lucide-react';
import type { ReactNode } from 'react';
import { Skeleton } from '@/4-presentation/components/ui/Skeleton';

export interface StatTrend {
  direction: 'up' | 'down' | 'flat';
  /** «8 % más que ayer». */
  text: string;
  /** ¿Es una buena noticia? Por defecto, subir sí y bajar no. */
  positive?: boolean;
}

export interface StatCardProps {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  icon?: ReactNode;
  trend?: StatTrend;
  /** Color del ícono y del borde: normal, éxito, aviso o peligro. */
  tone?: 'default' | 'success' | 'warning' | 'danger';
  loading?: boolean;
  className?: string;
}

const ICON_TONES = {
  default: 'bg-primary-soft text-primary-text',
  success: 'bg-success-soft text-success-text',
  warning: 'bg-warning-soft text-warning-text',
  danger: 'bg-danger-soft text-danger-text',
} as const;

const BORDER_TONES = {
  default: 'border-border',
  success: 'border-success/40',
  warning: 'border-warning/40',
  danger: 'border-danger/40',
} as const;

export function StatCard({ label, value, hint, icon, trend, tone = 'default', loading = false, className }: StatCardProps) {
  const good = trend ? (trend.positive ?? trend.direction === 'up') : false;
  const TrendIcon = trend?.direction === 'up' ? TrendingUp : trend?.direction === 'down' ? TrendingDown : Minus;
  return (
    <div className={clsx('min-w-0 rounded-card border bg-surface p-4 shadow-card', BORDER_TONES[tone], className)}>
      <div className="flex items-start justify-between gap-3">
        <p className="min-w-0 text-sm font-medium text-text-muted">{label}</p>
        {icon && (
          <span aria-hidden="true" className={clsx('flex size-9 shrink-0 items-center justify-center rounded-xl [&_svg]:size-5', ICON_TONES[tone])}>
            {icon}
          </span>
        )}
      </div>
      {loading ? (
        <>
          <Skeleton className="mt-2 h-8 w-32" />
          <span className="sr-only">Cargando {label.toLowerCase()}…</span>
        </>
      ) : (
        <p className="mt-1 font-display text-2xl font-semibold break-words text-text tabular-nums">{value}</p>
      )}
      {hint && !loading && <p className="mt-1 text-sm text-text-muted">{hint}</p>}
      {trend && !loading && (
        <p className={clsx('mt-2 inline-flex items-center gap-1 text-sm font-medium', trend.direction === 'flat' ? 'text-text-muted' : good ? 'text-success-text' : 'text-danger-text')}>
          <TrendIcon aria-hidden="true" className="size-4" />
          {trend.text}
        </p>
      )}
    </div>
  );
}
