// Cajón lateral accesible: role="dialog", foco atrapado, Escape y clic en el fondo cierran, se cierra al navegar,
// devuelve el foco al disparador y bloquea el desplazamiento del fondo (pila de capas modales compartida con los
// diálogos del armador: modalLayer.ts). Animación solo con transform/opacity.

import clsx from 'clsx';
import { X } from 'lucide-react';
import { useEffect, useId, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { useInRouterContext, useLocation } from 'react-router-dom';
import { useModalDialog, useOpenTransition } from './modalLayer';

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
const CLOSE_MS = 250;

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
  const { mounted, visible } = useOpenTransition(open, CLOSE_MS);
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const descriptionId = useId();
  const inRouter = useInRouterContext();
  const { trapFocus } = useModalDialog({ mounted, open, onClose, panelRef });

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
