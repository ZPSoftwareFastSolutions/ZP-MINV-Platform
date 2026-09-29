// USO · Menú de acciones de una fila (botón «⋯»). Úselo en la columna de acciones de DataTable (prop `rowActions`) o
// suelto en una tarjeta:
//
//   <RowActions label={`Acciones de la venta ${venta.number}`} actions={[
//     { label: 'Ver detalle', icon: <Eye />, onSelect: () => abrir(venta) },
//     { label: 'Reimprimir', icon: <Printer />, onSelect: () => reimprimir(venta), permission: 'sales.view' },
//     { label: 'Anular', icon: <Ban />, tone: 'danger', onSelect: () => confirmarAnulacion(venta),
//       disabled: venta.status === 'Voided', disabledReason: 'La venta ya está anulada.' },
//   ]} />
//
// Menú accesible (patrón «menu button»): Enter, Espacio o ↓ lo abren en la primera opción y ↑ en la última; flechas,
// Inicio y Fin se mueven; una letra salta a la opción que empieza con ella; Escape cierra y devuelve el foco; Tab cierra.
// Se dibuja sobre la página (no lo recorta el desplazamiento de la tabla) y se cierra al desplazar o al hacer clic afuera.
// Las acciones con `permission` se ocultan si la sesión no lo tiene (comodidad: el servidor decide). Las peligrosas
// (`tone: 'danger'`) van separadas al final. Sin acciones visibles no se dibuja nada.

import clsx from 'clsx';
import { MoreHorizontal } from 'lucide-react';
import { Fragment, useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { usePermissions } from '../hooks/usePermissions';
import { OPTION, POPOVER } from './styles';

export interface RowActionItem {
  label: string;
  icon?: ReactNode;
  onSelect: () => void;
  /** 'danger' = destructiva (anular, eliminar): en rojo y al final. */
  tone?: 'default' | 'danger';
  disabled?: boolean;
  /** Por qué no se puede (se lee junto a la opción). */
  disabledReason?: string;
  /** Permiso que hace falta para verla. */
  permission?: string;
  hidden?: boolean;
}

export interface RowActionsProps {
  /** Nombre del botón para lectores de pantalla: «Acciones de la venta F-CM-000123». */
  label: string;
  actions: readonly RowActionItem[];
  className?: string;
}

const MENU_WIDTH = 240;
const ITEM_HEIGHT = 44;
const GAP = 4;
const MARGIN = 8;

interface MenuPosition {
  top?: number;
  bottom?: number;
  left: number;
}

/** Debajo del botón y alineado a su derecha; arriba si abajo no entra. Siempre dentro de la ventana. */
function placeMenu(button: HTMLElement, items: number): MenuPosition {
  const rect = button.getBoundingClientRect();
  const height = items * ITEM_HEIGHT + 16;
  const left = Math.max(MARGIN, Math.min(rect.right - MENU_WIDTH, window.innerWidth - MENU_WIDTH - MARGIN));
  const below = window.innerHeight - rect.bottom;
  if (below < height + MARGIN && rect.top > below) return { bottom: window.innerHeight - rect.top + GAP, left };
  return { top: rect.bottom + GAP, left };
}

export function RowActions({ label, actions, className }: RowActionsProps) {
  const { can } = usePermissions();
  const buttonId = useId();
  const menuId = useId();
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState<MenuPosition>({ top: 0, left: 0 });
  const startAt = useRef<'first' | 'last'>('first');

  const visible = actions.filter((action) => !action.hidden && (!action.permission || can(action.permission)));
  const ordered = [...visible.filter((action) => action.tone !== 'danger'), ...visible.filter((action) => action.tone === 'danger')];

  useEffect(() => {
    if (!open) return;
    const items = Array.from(menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? []);
    (startAt.current === 'last' ? items[items.length - 1] : items[0])?.focus();
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node;
      if (!menuRef.current?.contains(target) && !buttonRef.current?.contains(target)) setOpen(false);
    };
    const onScroll = (event: Event) => {
      if (!menuRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const onResize = () => setOpen(false);
    document.addEventListener('pointerdown', onPointerDown);
    window.addEventListener('scroll', onScroll, true);
    window.addEventListener('resize', onResize);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      window.removeEventListener('scroll', onScroll, true);
      window.removeEventListener('resize', onResize);
    };
  }, [open]);

  if (ordered.length === 0) return null;

  const openMenu = (from: 'first' | 'last') => {
    if (buttonRef.current) setPosition(placeMenu(buttonRef.current, ordered.length));
    startAt.current = from;
    setOpen(true);
  };

  const close = (restoreFocus: boolean) => {
    setOpen(false);
    if (restoreFocus) buttonRef.current?.focus();
  };

  const onMenuKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const items = Array.from(menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? []);
    const index = items.indexOf(document.activeElement as HTMLElement);
    let next: number;
    switch (event.key) {
      case 'ArrowDown':
        next = (index + 1) % items.length;
        break;
      case 'ArrowUp':
        next = (index - 1 + items.length) % items.length;
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = items.length - 1;
        break;
      case 'Escape':
        event.preventDefault();
        event.stopPropagation();
        close(true);
        return;
      case 'Tab':
        // El foco vuelve al botón y el Tab sigue desde ahí (al control siguiente de la fila).
        close(true);
        return;
      default: {
        if (event.key.length !== 1 || !/\S/.test(event.key)) return;
        const letter = event.key.toLocaleLowerCase('es');
        const rotated = [...items.slice(index + 1), ...items.slice(0, index + 1)];
        rotated.find((item) => (item.textContent ?? '').trim().toLocaleLowerCase('es').startsWith(letter))?.focus();
        return;
      }
    }
    event.preventDefault();
    items[next]?.focus();
  };

  const firstDanger = ordered.findIndex((action) => action.tone === 'danger');

  return (
    <>
      <button
        ref={buttonRef}
        id={buttonId}
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        aria-label={label}
        title={label}
        onClick={() => (open ? close(false) : openMenu('first'))}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            openMenu(event.key === 'ArrowUp' ? 'last' : 'first');
          }
        }}
        className={clsx(
          'inline-flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-xl text-text-muted transition-colors duration-200 hover:bg-surface-3 hover:text-text',
          open && 'bg-surface-3 text-text',
          className,
        )}
      >
        <MoreHorizontal aria-hidden="true" className="size-5" />
      </button>
      {open &&
        createPortal(
          <div
            ref={menuRef}
            id={menuId}
            role="menu"
            aria-labelledby={buttonId}
            onKeyDown={onMenuKeyDown}
            style={{ position: 'fixed', top: position.top, bottom: position.bottom, left: position.left, width: MENU_WIDTH }}
            className={clsx(POPOVER, 'z-[55] max-h-[60vh] overflow-y-auto')}
            data-testid="menu-acciones"
          >
            {ordered.map((action, index) => {
              const reasonId = action.disabled && action.disabledReason ? `${menuId}-motivo-${index}` : undefined;
              return (
                <Fragment key={`${action.label}-${index}`}>
                  {index === firstDanger && index > 0 && <div role="separator" className="my-1 h-px bg-border" />}
                  <button
                    type="button"
                    role="menuitem"
                    tabIndex={-1}
                    aria-disabled={action.disabled || undefined}
                    aria-describedby={reasonId}
                    onClick={() => {
                      if (action.disabled) return;
                      close(true);
                      action.onSelect();
                    }}
                    className={clsx(
                      OPTION,
                      'hover:bg-surface-3 focus:bg-surface-3',
                      action.tone === 'danger' ? 'text-danger-text [&_svg]:text-danger-text' : '[&_svg]:text-accent',
                      action.disabled && 'cursor-not-allowed opacity-60',
                    )}
                  >
                    {action.icon && (
                      <span aria-hidden="true" className="inline-flex">
                        {action.icon}
                      </span>
                    )}
                    <span className="min-w-0 flex-1">
                      <span className="block">{action.label}</span>
                      {reasonId && (
                        <span id={reasonId} className="block text-xs text-text-muted">
                          {action.disabledReason}
                        </span>
                      )}
                    </span>
                  </button>
                </Fragment>
              );
            })}
          </div>,
          document.body,
        )}
    </>
  );
}
