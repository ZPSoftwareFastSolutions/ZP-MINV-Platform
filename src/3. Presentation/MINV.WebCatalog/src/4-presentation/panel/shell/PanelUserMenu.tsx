// Usuario de la barra superior del panel: iniciales, nombre y rol, con un menú «Cambiar contraseña», «Ir a la tienda» y
// «Cerrar sesión». Menú accesible (como el de la tienda): botón con `aria-haspopup`, ↓ lo abre en la primera opción,
// flechas, Inicio y Fin se mueven, Escape cierra y devuelve el foco, Tab cierra, y se cierra al hacer clic afuera y al
// navegar. Cerrar sesión lleva a la tienda (lo hace la guarda de la ruta).

import clsx from 'clsx';
import { ChevronDown, KeyRound, LogOut, Store } from 'lucide-react';
import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { initialsOf } from '@/1-domain/auth/permissions';
import { roleName } from '@/4-presentation/app/contract';
import { ROUTES } from '@/4-presentation/app/routes';
import { useLogout } from '@/4-presentation/hooks/useLogout';
import { useSession } from '@/4-presentation/hooks/useSession';

const ITEM =
  'flex min-h-11 w-full cursor-pointer items-center gap-3 rounded-lg px-3 text-left text-sm font-medium text-text transition-colors duration-200 hover:bg-surface-3 focus-visible:bg-surface-3 disabled:cursor-not-allowed disabled:opacity-50 [&_svg]:size-4 [&_svg]:shrink-0 [&_svg]:text-accent';

export function PanelUserMenu() {
  const { session } = useSession();
  const { logout, leaving } = useLogout('panel');
  const location = useLocation();
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const buttonId = useId();
  const menuId = useId();

  // Se cierra al navegar.
  const [lastKey, setLastKey] = useState(location.key);
  if (lastKey !== location.key) {
    setLastKey(location.key);
    setOpen(false);
  }

  // Al abrir, el foco va a la primera opción; un clic fuera lo cierra.
  useEffect(() => {
    if (!open) return;
    menuRef.current?.querySelector<HTMLElement>('[role="menuitem"]')?.focus();
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, [open]);

  if (!session) return null;
  const roles = session.roles.map(roleName).join(', ');
  const here = `${location.pathname}${location.search}`;

  const close = (restoreFocus: boolean) => {
    setOpen(false);
    if (restoreFocus) buttonRef.current?.focus();
  };

  const onMenuKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const items = Array.from(menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? []);
    const index = items.indexOf(document.activeElement as HTMLElement);
    let next = -1;
    if (event.key === 'ArrowDown') next = (index + 1) % items.length;
    else if (event.key === 'ArrowUp') next = (index - 1 + items.length) % items.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = items.length - 1;
    else if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      close(true);
      return;
    } else if (event.key === 'Tab') {
      setOpen(false);
      return;
    } else return;
    event.preventDefault();
    items[next]?.focus();
  };

  return (
    <div ref={rootRef} className="relative shrink-0">
      <button
        ref={buttonRef}
        id={buttonId}
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        aria-label={`Cuenta de ${session.displayName} (${roles})`}
        title={session.displayName}
        onClick={() => setOpen((current) => !current)}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown' && !open) {
            event.preventDefault();
            setOpen(true);
          }
        }}
        className="flex min-h-11 max-w-64 cursor-pointer items-center gap-2 rounded-xl border border-border bg-surface-2 py-1 pr-2 pl-1.5 text-left text-sm text-text transition-colors duration-200 hover:border-border-strong hover:bg-surface-3"
      >
        <span aria-hidden="true" className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-primary-soft font-display text-xs font-bold text-primary-text">
          {initialsOf(session)}
        </span>
        <span className="min-w-0 max-lg:hidden">
          <span className="block truncate font-semibold" data-testid="usuario-nombre">
            {session.displayName}
          </span>
          <span className="block truncate text-xs text-text-muted" data-testid="usuario-rol">
            {roles}
          </span>
        </span>
        <ChevronDown aria-hidden="true" className={clsx('size-4 shrink-0 text-text-muted transition-transform duration-200', open && 'rotate-180')} />
      </button>

      {open && (
        <div
          ref={menuRef}
          id={menuId}
          role="menu"
          aria-labelledby={buttonId}
          onKeyDown={onMenuKeyDown}
          className="absolute right-0 z-40 mt-2 w-72 max-w-[calc(100vw-2rem)] rounded-card border border-border bg-surface-2 p-2 shadow-card"
        >
          <div className="border-b border-border px-3 pt-1 pb-3">
            <p className="truncate text-sm font-semibold text-text">{session.displayName}</p>
            <p className="truncate text-xs text-text-muted">{session.email}</p>
            <p className="mt-1 truncate text-xs text-text-muted">
              {roles} · {session.company}
            </p>
          </div>
          <div className="mt-2 space-y-0.5">
            <Link to={ROUTES.changePasswordReturning(here)} role="menuitem" tabIndex={-1} className={ITEM} onClick={() => setOpen(false)}>
              <KeyRound aria-hidden="true" />
              Cambiar contraseña
            </Link>
            <Link to={ROUTES.home} role="menuitem" tabIndex={-1} className={ITEM} onClick={() => setOpen(false)}>
              <Store aria-hidden="true" />
              Ir a la tienda
            </Link>
            <button
              type="button"
              role="menuitem"
              tabIndex={-1}
              disabled={leaving}
              className={ITEM}
              onClick={() => {
                close(false);
                void logout();
              }}
            >
              <LogOut aria-hidden="true" />
              Cerrar sesión
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
