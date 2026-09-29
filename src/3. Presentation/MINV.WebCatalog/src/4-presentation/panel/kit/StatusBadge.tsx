// USO · Distintivo de estado (venta pagada, reserva vencida, caja abierta). El color nunca va solo: siempre lleva el
// nombre del estado y un punto.
//
//   <StatusBadge tone="success">Pagada</StatusBadge>
//   <StatusBadge status={venta.status} statuses={ESTADOS_VENTA} />      ← con los estados del módulo (statuses.ts)

import clsx from 'clsx';
import type { ReactNode } from 'react';
import { statusOf, type StatusMap, type StatusTone } from './statuses';

const TONES: Record<StatusTone, string> = {
  neutral: 'border-border-strong bg-surface-2 text-text-muted',
  info: 'border-accent/40 bg-accent-soft text-accent-hover',
  success: 'border-success/40 bg-success-soft text-success-text',
  warning: 'border-warning/40 bg-warning-soft text-warning-text',
  danger: 'border-danger/40 bg-danger-soft text-danger-text',
  accent: 'border-primary/50 bg-primary-soft text-primary-text',
};

export type StatusBadgeProps = {
  icon?: ReactNode;
  className?: string;
} & ({ tone?: StatusTone; children: ReactNode; status?: undefined; statuses?: undefined } | { status: string | null | undefined; statuses: StatusMap; tone?: undefined; children?: undefined });

export function StatusBadge(props: StatusBadgeProps) {
  const definition = props.statuses ? statusOf(props.statuses, props.status) : null;
  const tone = definition?.tone ?? props.tone ?? 'neutral';
  const content = definition ? definition.label : props.children;
  return (
    <span
      className={clsx(
        'inline-flex max-w-full items-center gap-1.5 whitespace-nowrap rounded-full border px-2.5 py-0.5 text-xs font-semibold',
        TONES[tone],
        props.className,
      )}
    >
      {props.icon ? (
        <span aria-hidden="true" className="inline-flex [&_svg]:size-3.5">
          {props.icon}
        </span>
      ) : (
        <span aria-hidden="true" className="size-1.5 shrink-0 rounded-full bg-current" />
      )}
      <span className="truncate">{content}</span>
    </span>
  );
}
