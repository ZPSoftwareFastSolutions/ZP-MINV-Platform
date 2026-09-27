// Diálogo modal del armador: centrado en escritorio y como hoja inferior en móvil (variante `sheet` siempre inferior).
// Portal sobre <body>, role="dialog", foco atrapado, Escape (solo en la capa superior) y clic en el fondo cierran,
// devuelve el foco al disparador y deja #root inerte mientras está abierto: toda la mecánica sale de la misma pila de
// capas que el cajón (components/ui/modalLayer.ts). Animación solo con transform/opacity.

import clsx from 'clsx';
import { X } from 'lucide-react';
import { useId, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { useModalDialog, useOpenTransition } from '@/4-presentation/components/ui/modalLayer';

export interface BuilderDialogProps {
  open: boolean;
  onClose: () => void;
  title: string;
  description?: string;
  /** `center`: modal centrado desde sm (hoja inferior en móvil) · `sheet`: hoja inferior en todos los tamaños. */
  variant?: 'center' | 'sheet';
  size?: 'md' | 'lg' | 'xl';
  /** Contenido fijo al pie (totales, botones). */
  footer?: ReactNode;
  /** Elemento extra en la cabecera, a la izquierda del botón de cerrar. */
  headerExtra?: ReactNode;
  closeLabel?: string;
  /**
   * Adónde devolver el foco si el disparador ya no existe al cerrar (por ejemplo, un botón de la hoja de móvil que se
   * cerró para abrir este diálogo).
   */
  fallbackFocus?: () => HTMLElement | null | undefined;
  children: ReactNode;
}

const SIZES = { md: 'sm:max-w-lg', lg: 'sm:max-w-2xl', xl: 'sm:max-w-4xl' } as const;
const CLOSE_MS = 220;

export function BuilderDialog({
  open,
  onClose,
  title,
  description,
  variant = 'center',
  size = 'md',
  footer,
  headerExtra,
  closeLabel = 'Cerrar',
  fallbackFocus,
  children,
}: BuilderDialogProps) {
  const { mounted, visible } = useOpenTransition(open, CLOSE_MS);
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const descriptionId = useId();
  const { trapFocus } = useModalDialog({ mounted, open, onClose, panelRef, fallbackFocus });

  if (!mounted) return null;

  const centered = variant === 'center';

  const dialog = (
    <div className={clsx('fixed inset-0 z-50 flex justify-center', centered ? 'items-end sm:items-center sm:p-4' : 'items-end')} aria-hidden={!open || undefined}>
      <div
        className={clsx('absolute inset-0 bg-bg/70 backdrop-blur-sm transition-opacity duration-200', visible ? 'opacity-100' : 'opacity-0')}
        onClick={onClose}
      />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        onKeyDown={trapFocus}
        className={clsx(
          'relative flex w-full flex-col border border-border bg-surface shadow-card will-change-transform',
          'max-h-[88dvh] rounded-t-3xl transition-[transform,opacity] duration-250 ease-out',
          centered ? clsx('sm:max-h-[85dvh] sm:rounded-card', SIZES[size]) : 'max-w-3xl',
          visible ? 'translate-y-0 opacity-100' : centered ? 'translate-y-6 opacity-0 sm:translate-y-3' : 'translate-y-full opacity-100',
        )}
      >
        {!centered && <span aria-hidden="true" className="mx-auto mt-2 block h-1.5 w-10 rounded-full bg-border-strong" />}
        <header className="flex items-start justify-between gap-3 border-b border-border px-4 py-3 sm:px-6 sm:py-4">
          <div className="min-w-0">
            <h2 id={titleId} className="font-display text-xl font-semibold text-text">
              {title}
            </h2>
            {description && (
              <p id={descriptionId} className="mt-0.5 text-sm text-text-muted">
                {description}
              </p>
            )}
          </div>
          <div className="flex shrink-0 items-center gap-2">
            {headerExtra}
            <button
              type="button"
              data-autofocus
              aria-label={closeLabel}
              title={closeLabel}
              onClick={onClose}
              className="flex size-11 cursor-pointer items-center justify-center rounded-xl text-text-muted transition-colors duration-200 hover:bg-surface-2 hover:text-text"
            >
              <X aria-hidden="true" className="size-5" />
            </button>
          </div>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 py-4 sm:px-6">{children}</div>
        {footer && <footer className="border-t border-border bg-surface-2 px-4 py-4 sm:px-6">{footer}</footer>}
      </div>
    </div>
  );

  return createPortal(dialog, document.body);
}
