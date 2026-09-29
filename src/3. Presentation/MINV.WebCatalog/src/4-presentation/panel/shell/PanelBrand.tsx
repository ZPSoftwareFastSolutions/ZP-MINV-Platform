// Logotipo del panel: lleva al tablero («Inicio»). Compacto (solo el ícono) en la barra superior del teléfono.

import clsx from 'clsx';
import { Link } from 'react-router-dom';
import { STORE } from '@/shared/constants';
import { PANEL_BASE } from '../registry';

export function PanelBrand({ compact = false, className }: { compact?: boolean; className?: string }) {
  return (
    <Link to={PANEL_BASE} aria-label="Panel del personal, ir al inicio" className={clsx('flex min-h-11 min-w-11 shrink-0 items-center justify-center gap-2.5 rounded-lg', className)}>
      <img src="/images/logo-256.png" alt="" width={40} height={40} className="size-10 rounded-xl" />
      {!compact && (
        <span className="flex flex-col leading-none">
          <span className="font-display text-lg font-bold tracking-tight text-gradient-brand">{STORE.wordmark}</span>
          <span className="text-[0.625rem] font-semibold uppercase tracking-[0.32em] text-text-muted">Panel</span>
        </span>
      )}
    </Link>
  );
}
