// Diálogo modal del armador: centrado en escritorio y como hoja inferior en móvil (variante `sheet` siempre inferior).
// Portal sobre <body>, role="dialog", foco atrapado, Escape y clic en el fondo cierran, devuelve el foco al disparador y
// deja #root inerte mientras está abierto. Animación solo con transform/opacity (respeta prefers-reduced-motion).

import clsx from 'clsx';
import { X } from 'lucide-react';
import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { createPortal } from 'react-dom';

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
const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
const CLOSE_MS = 220;

// Diálogos montados a la vez (la hoja de móvil sigue montada mientras se cierra y ya se abre el resumen final): el
// bloqueo del fondo se toma con el primero y se devuelve con el último, con el overflow que tenía el body antes.
let openDialogs = 0;
let savedOverflow = '';

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
  // `closing` mantiene el panel montado durante la animación de salida; `visible` dispara la de entrada.
  const [prevOpen, setPrevOpen] = useState(open);
  const [closing, setClosing] = useState(false);
  const [visible, setVisible] = useState(false);
  if (prevOpen !== open) {
    setPrevOpen(open);
    if (open) {
      setClosing(false);
    } else {
      setVisible(false);
      setClosing(true);
    }
  }
  const mounted = open || closing;

  const panelRef = useRef<HTMLDivElement>(null);
  const restoreFocusRef = useRef<HTMLElement | null>(null);
  const fallbackFocusRef = useRef(fallbackFocus);
  const titleId = useId();
  const descriptionId = useId();

  useEffect(() => {
    fallbackFocusRef.current = fallbackFocus;
  }, [fallbackFocus]);

  useEffect(() => {
    if (!open) return;
    restoreFocusRef.current = document.activeElement as HTMLElement | null;
    const frame = requestAnimationFrame(() => setVisible(true));
    return () => cancelAnimationFrame(frame);
  }, [open]);

  useEffect(() => {
    if (!closing) return;
    const timer = setTimeout(() => setClosing(false), CLOSE_MS);
    return () => clearTimeout(timer);
  }, [closing]);

  // Bloqueo del fondo (scroll + inert) mientras haya algún diálogo abierto.
  useEffect(() => {
    if (!mounted) return;
    const root = document.getElementById('root');
    if (openDialogs === 0) {
      savedOverflow = document.body.style.overflow;
      document.body.style.overflow = 'hidden';
      root?.setAttribute('inert', '');
    }
    openDialogs += 1;
    return () => {
      openDialogs -= 1;
      if (openDialogs === 0) {
        document.body.style.overflow = savedOverflow;
        root?.removeAttribute('inert');
      }
    };
  }, [mounted]);

  // Foco inicial y devolución del foco al cerrar (al disparador si sigue en la página; si no, al elemento de reserva).
  useEffect(() => {
    if (!mounted) return;
    const panel = panelRef.current;
    const first = panel?.querySelector<HTMLElement>('[data-autofocus]') ?? panel?.querySelector<HTMLElement>(FOCUSABLE);
    const timer = setTimeout(() => first?.focus(), 30);
    return () => {
      clearTimeout(timer);
      const previous = restoreFocusRef.current;
      const usable = previous && previous !== document.body && previous.isConnected && !previous.closest('[inert]');
      const target = usable ? previous : fallbackFocusRef.current?.();
      target?.focus?.();
    };
  }, [mounted]);

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        onClose();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [open, onClose]);

  const trapFocus = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Tab' || !panelRef.current) return;
    const focusable = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((element) => element.offsetParent !== null);
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

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
