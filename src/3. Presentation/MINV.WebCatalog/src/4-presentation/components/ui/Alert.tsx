// Aviso en línea dentro de una pantalla o un formulario: error, advertencia, éxito o información. Los errores y las
// advertencias se anuncian al aparecer (`role="alert"`); el resto, sin interrumpir (`role="status"`).

import clsx from 'clsx';
import { CircleAlert, CircleCheck, Info, TriangleAlert } from 'lucide-react';
import type { ReactNode, Ref } from 'react';

export type AlertTone = 'danger' | 'warning' | 'success' | 'info';

export interface AlertProps {
  tone?: AlertTone;
  title?: string;
  children?: ReactNode;
  /** Botones o enlaces debajo del texto. */
  actions?: ReactNode;
  className?: string;
  /** Recibe el foco por programa (por ejemplo, tras un envío fallido). */
  ref?: Ref<HTMLDivElement>;
}

const TONES: Record<AlertTone, { box: string; text: string; icon: typeof Info }> = {
  danger: { box: 'border-danger/40 bg-danger-soft', text: 'text-danger-text', icon: CircleAlert },
  warning: { box: 'border-warning/40 bg-warning-soft', text: 'text-warning-text', icon: TriangleAlert },
  success: { box: 'border-success/40 bg-success-soft', text: 'text-success-text', icon: CircleCheck },
  info: { box: 'border-accent/40 bg-accent-soft', text: 'text-accent-hover', icon: Info },
};

export function Alert({ tone = 'danger', title, children, actions, className, ref }: AlertProps) {
  const { box, text, icon: Icon } = TONES[tone];
  const urgent = tone === 'danger' || tone === 'warning';
  return (
    <div ref={ref} tabIndex={-1} role={urgent ? 'alert' : 'status'} className={clsx('flex items-start gap-3 rounded-xl border p-3 text-sm', box, className)}>
      <Icon aria-hidden="true" className={clsx('mt-0.5 size-5 shrink-0', text)} />
      <div className="min-w-0 flex-1">
        {title && <p className={clsx('font-semibold', text)}>{title}</p>}
        {children && <div className={clsx('text-text', title && 'mt-0.5')}>{children}</div>}
        {actions && <div className="mt-2 flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
    </div>
  );
}
