// Cajón lateral accesible: role="dialog", foco atrapado, Escape y clic en el fondo cierran, se cierra al navegar,
// devuelve el foco al disparador y bloquea el desplazamiento del fondo. Animación solo con transform/opacity.

import clsx from 'clsx';
import { X } from 'lucide-react';
import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { useInRouterContext, useLocation } from 'react-router-dom';

export interface DrawerProps {
  open: boolean;
  onClose: () => void;
  title: string;
  description?: string;
  side?: 'left' | 'right';
  size?: 'sm' | 'md' | 'lg';
  /** Contenido fijo al pie (totales, botones). */
  footer?: ReactNode;
  /** Elemento extra en la cabecera, a la izquierda del botón de cerrar (una insignia, por ejemplo). */
  headerExtra?: ReactNode;
  children: ReactNode;
  closeLabel?: string;
}

const SIZES = { sm: 'max-w-sm', md: 'max-w-md', lg: 'max-w-lg' } as const;
const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
const CLOSE_MS = 250;

let openDrawers = 0;

/** Cierra el cajón cuando cambia la ruta (sincroniza con el enrutador: por eso va en un efecto y no en el render). */
function CloseOnNavigate({ onClose }: { onClose: () => void }) {
  const location = useLocation();
  const firstKey = useRef(location.key);
  useEffect(() => {
    if (location.key !== firstKey.current) onClose();
  }, [location.key, onClose]);
  return null;
}

export function Drawer({ open, onClose, title, description, side = 'right', size = 'md', footer, headerExtra, children, closeLabel = 'Cerrar' }: DrawerProps) {
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
  const titleId = useId();
  const descriptionId = useId();
  const inRouter = useInRouterContext();

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

  // Bloqueo del fondo (scroll + inert) mientras haya algún cajón abierto.
  useEffect(() => {
    if (!mounted) return;
    openDrawers += 1;
    const root = document.getElementById('root');
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    root?.setAttribute('inert', '');
    return () => {
      openDrawers -= 1;
      if (openDrawers === 0) {
        document.body.style.overflow = previousOverflow;
        root?.removeAttribute('inert');
      }
    };
  }, [mounted]);

  // Foco inicial y devolución del foco al cerrar.
  useEffect(() => {
    if (!mounted) return;
    const panel = panelRef.current;
    const first = panel?.querySelector<HTMLElement>('[data-autofocus]') ?? panel?.querySelector<HTMLElement>(FOCUSABLE);
    const timer = setTimeout(() => first?.focus(), 30);
    return () => {
      clearTimeout(timer);
      restoreFocusRef.current?.focus?.();
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
    const focusable = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
      (element) => element.offsetParent !== null,
    );
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

  const drawer = (
    <div className="fixed inset-0 z-50" aria-hidden={!open || undefined}>
      {inRouter && <CloseOnNavigate onClose={onClose} />}
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
          'absolute inset-y-0 flex w-full flex-col border-border bg-surface shadow-card transition-transform duration-250 ease-out will-change-transform',
          SIZES[size],
          side === 'right' ? 'right-0 border-l' : 'left-0 border-r',
          visible ? 'translate-x-0' : side === 'right' ? 'translate-x-full' : '-translate-x-full',
        )}
      >
        <header className="flex items-start justify-between gap-3 border-b border-border px-4 py-3 sm:px-5">
          <div className="min-w-0">
            <h2 id={titleId} className="font-display text-xl font-semibold text-text">
              {title}
            </h2>
            {description && (
              <p id={descriptionId} className="text-sm text-text-muted">
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
        <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 py-4 sm:px-5">{children}</div>
        {footer && <footer className="border-t border-border bg-surface-2 px-4 py-4 sm:px-5">{footer}</footer>}
      </div>
    </div>
  );

  return createPortal(drawer, document.body);
}
